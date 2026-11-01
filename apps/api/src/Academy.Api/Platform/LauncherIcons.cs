using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Platform;

// This catalog describes artwork actually bundled by the mobile project. It
// never accepts a URL/downloaded icon or exposes other tenants to members.
public static class LauncherIcons
{
    public const string Professional = "professional-demo";
    public const int MinimumBuild = 1;
    public static void MapLauncherIcons(this WebApplication app)
    {
        var platform = app.MapGroup("/api/v1/platform").RequireAuthorization(PlatformIdentity.Policy);
        platform.MapGet("/launcher-icons", () => Results.Ok(new[] { new { key = Professional, name = "Professional — نموذج عرض مصرح", minimumVersion = "0.1.0+1", minimumBuild = MinimumBuild, platforms = new[] { "ios", "android" } } }));
        platform.MapPut("/academies/{id:guid}/launcher-icon", Save).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/academy/launcher-icon", Choice).RequireAuthorization(AcademyPermissions.TenantAccess);
    }
    public sealed record IconWrite(int Version, bool Enabled, string? Key);
    private static async Task<IResult> Save(Guid id, IconWrite body, FoundationDbContext db, ClaimsPrincipal actor, TimeProvider clock)
    {
        if (body.Key is not (null or Professional)) return Results.UnprocessableEntity(new { title = "اختر رسمًا مضمّنًا في البناء، أو اتركه قيد التجهيز." });
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + id}, 0))");
        if (!await db.Academies.AnyAsync(a => a.Id == id)) return Results.NotFound();
        var profile = await db.AcademyPlatformProfiles.SingleOrDefaultAsync(p => p.AcademyId == id);
        if (profile is null || profile.EditVersion != body.Version) return Results.Conflict(new { title = "حدّث بيانات التجهيز قبل حفظ الأيقونة." });
        var before = new { profile.PremiumIconEnabled, profile.BundledIconKey };
        profile.PremiumIconEnabled = body.Enabled; profile.BundledIconKey = body.Key; profile.EditVersion++;
        db.PlatformAudits.Add(new() { Id = Guid.NewGuid(), ActorUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!), TargetAcademyId = id, Action = "academy.launcher-icon", Details = JsonSerializer.Serialize(new { before, body.Enabled, body.Key }), AtUtc = clock.GetUtcNow() });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { version = profile.EditVersion });
    }
    private static async Task<IResult> Choice(Guid? selectionAcademyId, CurrentTenant tenant, ClaimsPrincipal actor, FoundationDbContext db, HttpResponse response)
    {
        var t = (await tenant.ResolveAsync())!;
        var user = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
        response.Headers.CacheControl = "private, no-store";
        var profile = await db.AcademyPlatformProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.AcademyId == t.AcademyId);
        var key = profile?.PremiumIconEnabled == true ? profile.BundledIconKey : null;
        // Validate a previously selected installed icon independently from the
        // current role/academy. The client ID is only a lookup: a live membership
        // and entitlement for THIS user are required, and no catalog is returned.
        var selectedAllowed = !selectionAcademyId.HasValue || await db.AcademyMemberships.IgnoreQueryFilters().AsNoTracking().AnyAsync(m =>
            m.AcademyId == selectionAcademyId && m.UserId == user && m.IsActive && m.User.IsActive && m.Academy.IsActive
            && (m.PrimaryMembershipId == null || m.PrimaryMembership!.IsActive)
            && db.AcademyPlatformProfiles.Any(p => p.AcademyId == m.AcademyId && p.PremiumIconEnabled && p.BundledIconKey == Professional));
        return Results.Ok(new { academyId = t.AcademyId, academyName = t.AcademyName, enabled = profile?.PremiumIconEnabled == true, key,
            state = profile?.PremiumIconEnabled != true ? "Standard" : key is null ? "AwaitingArtwork" : "Bundled",
            minimumBuild = MinimumBuild, minimumVersion = "0.1.0+1", selectedAllowed });
    }
}
