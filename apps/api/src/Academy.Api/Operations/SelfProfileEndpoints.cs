using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class SelfProfileEndpoints
{
    internal const string GenderClaim = "profile.avatar.gender";
    public static void MapSelfProfile(WebApplication app)
    {
        var api = app.MapGroup("/api/v1/me/profile").RequireAuthorization(AcademyPermissions.TenantAccess);
        api.MapGet("", Read);
        api.MapPut("", Update).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Read(CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, HttpResponse response)
    {
        var t = (await tenant.ResolveAsync())!;
        var actor = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == actor && u.IsActive);
        var photo = await db.PrivateAssets.AsNoTracking().Where(a => a.AcademyId == t.AcademyId && a.UploadedByUserId == actor && a.Purpose == "SelfPhoto" && a.IsActive)
            .OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id).Select(a => (Guid?)a.Id).FirstOrDefaultAsync();
        var gender = await db.UserClaims.Where(c => c.UserId == actor && c.ClaimType == GenderClaim).Select(c => c.ClaimValue).FirstOrDefaultAsync();
        response.Headers.CacheControl = "private, no-store";
        return Results.Ok(new { user.DisplayName, gender, photoReference = photo.HasValue ? $"/api/v1/assets/{photo}" : null, role = t.Role.ToString() });
    }
    public sealed record UpdateProfile(string? Gender);
    private static async Task<IResult> Update(UpdateProfile request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        _ = (await tenant.ResolveAsync())!;
        var actor = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (request.Gender is not (null or "" or "Male" or "Female"))
            return Results.UnprocessableEntity(new { message = "اختر الصورة المناسبة أو اترك الاختيار محايدًا." });
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"self-profile:" + actor},0))");
        var claims = await db.UserClaims.Where(c => c.UserId == actor && c.ClaimType == GenderClaim).ToArrayAsync();
        db.UserClaims.RemoveRange(claims);
        if (!string.IsNullOrEmpty(request.Gender)) db.UserClaims.Add(new IdentityUserClaim<Guid> { UserId = actor, ClaimType = GenderClaim, ClaimValue = request.Gender });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.NoContent();
    }
}
