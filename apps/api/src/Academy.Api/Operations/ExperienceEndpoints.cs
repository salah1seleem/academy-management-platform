using System.Security.Claims;
using System.Text.RegularExpressions;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static partial class ExperienceEndpoints
{
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")] private static partial Regex Hex();
    private static Guid Actor(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static Task<bool> Operator(ClaimsPrincipal user, FoundationDbContext db) => db.PlatformBrandOperators.AnyAsync(x => x.UserId == Actor(user) && x.IsActive && db.Users.Any(u => u.Id == x.UserId && u.IsActive));
    public static void MapExperience(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization(AcademyPermissions.TenantAccess);
        api.MapGet("/academy/experience", Get);
        api.MapGet("/academy/branding/logo", Logo);
        api.MapPut("/manage/academy/experience", Policy).RequireAuthorization(AcademyPermissions.AcademySettingsManage).AddEndpointFilter<CsrfFilter>();
        api.MapGet("/platform/academies", Academies);
        api.MapPut("/platform/academies/{id:guid}/branding", Branding).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/platform/academies/{id:guid}/logo", UploadLogo).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Get(CurrentTenant tenant, ClaimsPrincipal user, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        var x = await db.AcademyExperiences.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId);
        return Results.Ok(new { displayName = x?.DisplayName ?? t.AcademyName, primaryColor = x?.PrimaryColor ?? "#D71935", secondaryColor = x?.SecondaryColor ?? "#171923", logoUrl = x?.Logo == null ? null : "/api/v1/academy/branding/logo", coachAttendanceEnabled = x?.CoachAttendanceEnabled ?? true, platformBrandOperator = await Operator(user, db) });
    }
    private static async Task<IResult> Logo(CurrentTenant tenant, FoundationDbContext db, HttpContext context)
    {
        var t = (await tenant.ResolveAsync())!; var x = await db.AcademyExperiences.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId);
        if (x?.Logo == null) return Results.NotFound();
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(x.Logo, x.LogoContentType!);
    }
    private static async Task<IResult> Policy(AttendancePolicy body, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var row = await Row(t.AcademyId, db, clock);
        row.CoachAttendanceEnabled = body.CoachAttendanceEnabled; row.UpdatedAtUtc = clock.UtcNow; await db.SaveChangesAsync(); return Results.NoContent();
    }
    private static async Task<IResult> Academies(ClaimsPrincipal user, FoundationDbContext db)
    {
        if (!await Operator(user, db)) return Results.Forbid();
        return Results.Ok(await db.Academies.AsNoTracking().OrderBy(x => x.ArabicName).Select(x => new { x.Id, x.Slug, x.IsActive, displayName = db.AcademyExperiences.Where(b => b.AcademyId == x.Id).Select(b => b.DisplayName).FirstOrDefault() ?? x.ArabicName, primaryColor = db.AcademyExperiences.Where(b => b.AcademyId == x.Id).Select(b => b.PrimaryColor).FirstOrDefault() ?? "#D71935", secondaryColor = db.AcademyExperiences.Where(b => b.AcademyId == x.Id).Select(b => b.SecondaryColor).FirstOrDefault() ?? "#171923" }).ToListAsync());
    }
    private static async Task<IResult> Branding(Guid id, BrandingWrite body, ClaimsPrincipal user, FoundationDbContext db, ISubscriptionClock clock)
    {
        if (!await Operator(user, db)) return Results.Forbid();
        if (!await db.Academies.AnyAsync(x => x.Id == id)) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(body.DisplayName) || body.DisplayName.Length > 120 || !Hex().IsMatch(body.PrimaryColor ?? "") || !Hex().IsMatch(body.SecondaryColor ?? "")) return Results.UnprocessableEntity(new { message = "أدخل اسم الأكاديمية ولونين بصيغة صحيحة." });
        var row = await Row(id, db, clock); row.DisplayName = body.DisplayName.Trim(); row.PrimaryColor = body.PrimaryColor!.ToUpperInvariant(); row.SecondaryColor = body.SecondaryColor!.ToUpperInvariant(); row.UpdatedAtUtc = clock.UtcNow;
        Audit(row, user, db, clock); await db.SaveChangesAsync(); return Results.NoContent();
    }
    private static async Task<IResult> UploadLogo(Guid id, HttpContext context, FoundationDbContext db, ISubscriptionClock clock)
    {
        if (!await Operator(context.User, db)) return Results.Forbid();
        if (!await db.Academies.AnyAsync(x => x.Id == id)) return Results.NotFound();
        if (!context.Request.HasFormContentType) return Results.BadRequest();
        var form = await context.Request.ReadFormAsync(); var file = form.Files.GetFile("file");
        if (form.Files.Count != 1 || file == null || file.Length < 12 || file.Length > 2 * 1024 * 1024) return Results.BadRequest(new { message = "اختر صورة شعار واحدة حتى 2 ميجابايت." });
        using var stream = new MemoryStream(); await file.CopyToAsync(stream); var bytes = stream.ToArray(); var type = AssetEndpoints.DetectType(bytes);
        if (type is not ("image/png" or "image/jpeg" or "image/webp") || type != file.ContentType) return Results.BadRequest(new { message = "الشعار PNG أو JPEG أو WebP فقط." });
        var row = await Row(id, db, clock); row.Logo = bytes; row.LogoContentType = type; row.UpdatedAtUtc = clock.UtcNow;
        Audit(row, context.User, db, clock); await db.SaveChangesAsync(); return Results.NoContent();
    }
    private static async Task<AcademyExperience> Row(Guid id, FoundationDbContext db, ISubscriptionClock clock)
    {
        var row = await db.AcademyExperiences.SingleOrDefaultAsync(x => x.AcademyId == id);
        if (row != null) return row;
        row = new AcademyExperience { Id = Guid.NewGuid(), AcademyId = id, DisplayName = await db.Academies.Where(x => x.Id == id).Select(x => x.ArabicName).SingleAsync(), CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow }; db.AcademyExperiences.Add(row); return row;
    }
    private static void Audit(AcademyExperience row, ClaimsPrincipal user, FoundationDbContext db, ISubscriptionClock clock) => db.BrandingAudits.Add(new BrandingAudit { Id = Guid.NewGuid(), AcademyId = row.AcademyId, ActorUserId = Actor(user), DisplayName = row.DisplayName, PrimaryColor = row.PrimaryColor, SecondaryColor = row.SecondaryColor, AtUtc = clock.UtcNow });
}
public sealed record AttendancePolicy(bool CoachAttendanceEnabled);
public sealed record BrandingWrite(string DisplayName, string? PrimaryColor, string? SecondaryColor);
