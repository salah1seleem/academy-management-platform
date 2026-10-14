using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Configuration;

public sealed record SportKindWrite(SportKind Kind, uint Version);
public sealed record SwimmingPolicyWrite(bool Enabled, bool PrivateTrainingEnabled, bool OpenSwimEnabled, bool DayPassEnabled, uint Version);
public static class SportSettingsEndpoints
{
    public static void MapSportSettings(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/manage/sport-settings").RequireAuthorization(AcademyPermissions.AcademySettingsManage);
        api.MapGet("/", async (CurrentTenant tenant, FoundationDbContext db, AcademyConfigurationService config, HttpResponse response) => {
            var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
            response.Headers.CacheControl = "no-store, private";
            return Results.Ok(new { facilitiesEnabled = await config.Enabled(t.AcademyId, "facilities"),
                sports = await db.Sports.OrderBy(x => x.ArabicName).Select(x => new { x.Id, name = x.ArabicName, x.Kind, x.IsActive, x.Version }).ToArrayAsync(),
                branches = await db.Branches.Where(x => x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, name = x.ArabicName }).ToArrayAsync() });
        });
        api.MapGet("/{sportId:guid}/swimming", async (Guid sportId, CurrentTenant tenant, FoundationDbContext db, HttpResponse response) => {
            var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
            if (!await db.Sports.AnyAsync(x => x.Id == sportId && x.Kind == SportKind.Swimming)) return Results.NotFound();
            response.Headers.CacheControl = "no-store, private";
            return Results.Ok(new { policies = await db.SwimmingBranchPolicies.Where(x => x.SportId == sportId).ToArrayAsync(),
                history = await db.SportSettingsAudits.Where(x => x.SportId == sportId).OrderByDescending(x => x.CreatedAtUtc).Take(30).Select(x => new { x.BranchId, x.CreatedAtUtc, x.Before, x.After }).ToArrayAsync() });
        });
        api.MapPut("/{sportId:guid}/kind", Kind).AddEndpointFilter<CsrfFilter>();
        api.MapPut("/{sportId:guid}/swimming/{branchId:guid}", Policy).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<bool> Lock(FoundationDbContext db, TenantMembership t)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"}, 0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + t.AcademyId}, 0))");
        return await db.AcademyMemberships.AsNoTracking().AnyAsync(x => x.Id == t.MembershipId && x.AcademyId == t.AcademyId && x.IsActive && x.Role == AcademyRole.AcademyOwner && x.User.IsActive && x.Academy.IsActive);
    }
    private static async Task<IResult> Kind(Guid sportId, SportKindWrite body, CurrentTenant tenant, FoundationDbContext db, ClaimsPrincipal actor, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        if (!Enum.IsDefined(body.Kind)) return Results.UnprocessableEntity(new { message = "اختر نوع رياضة صالحًا." });
        await using var tx = await db.Database.BeginTransactionAsync(); if (!await Lock(db, t)) return Results.Forbid();
        var sport = await db.Sports.SingleOrDefaultAsync(x => x.Id == sportId); if (sport is null) return Results.NotFound();
        if (sport.Version != body.Version) return Results.Conflict(new { message = "تغيرت بيانات الرياضة؛ حدّث الصفحة." });
        if (sport.Kind == body.Kind) return Results.Ok(new { sport.Id, sport.Kind, sport.Version });
        if (await db.FacilitySpaces.AnyAsync(x => x.SportId == sportId) || await db.SwimmingBranchPolicies.AnyAsync(x => x.SportId == sportId)) return Results.Conflict(new { message = "نوع الرياضة مرتبط بإعدادات أو مرافق محفوظة؛ لا تغير التاريخ. أوقف التشغيل من إعدادات الفرع." });
        var before = JsonSerializer.Serialize(new { sport.Kind }); sport.Kind = body.Kind; sport.UpdatedAtUtc = clock.GetUtcNow();
        Audit(db, t.AcademyId, sportId, null, actor, before, body, clock); await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Ok(new { sport.Id, sport.Kind, sport.Version });
    }
    private static async Task<IResult> Policy(Guid sportId, Guid branchId, SwimmingPolicyWrite body, CurrentTenant tenant, FoundationDbContext db, ClaimsPrincipal actor, AcademyConfigurationService config, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(); if (!await Lock(db, t)) return Results.Forbid();
        var sport = await db.Sports.SingleOrDefaultAsync(x => x.Id == sportId && x.Kind == SportKind.Swimming);
        if (sport is null || !await db.Branches.AnyAsync(x => x.Id == branchId && x.IsActive)) return Results.NotFound();
        if (body.Enabled && (!sport.IsActive || !await config.Enabled(t.AcademyId, "facilities"))) return Results.Problem(statusCode: 403, title: "تفعيل السباحة يتطلب الرياضة وميزة المرافق في خطة الأكاديمية وإعداداتها.");
        var row = await db.SwimmingBranchPolicies.SingleOrDefaultAsync(x => x.SportId == sportId && x.BranchId == branchId);
        if ((row?.Version ?? 0) != body.Version) return Results.Conflict(new { message = "تغير إعداد الفرع؛ حدّث الصفحة قبل الحفظ." });
        if (row is not null && row.Enabled == body.Enabled && row.PrivateTrainingEnabled == body.PrivateTrainingEnabled && row.OpenSwimEnabled == body.OpenSwimEnabled && row.DayPassEnabled == body.DayPassEnabled) return Results.Ok(row);
        var before = JsonSerializer.Serialize(row is null ? null : new { row.Enabled, row.PrivateTrainingEnabled, row.OpenSwimEnabled, row.DayPassEnabled });
        if (row is null) { row = new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SportId = sportId, BranchId = branchId, CreatedAtUtc = clock.GetUtcNow() }; db.SwimmingBranchPolicies.Add(row); }
        row.Enabled = body.Enabled; row.PrivateTrainingEnabled = body.PrivateTrainingEnabled; row.OpenSwimEnabled = body.OpenSwimEnabled; row.DayPassEnabled = body.DayPassEnabled; row.UpdatedAtUtc = clock.GetUtcNow();
        Audit(db, t.AcademyId, sportId, branchId, actor, before, body, clock); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(row);
    }
    private static void Audit(FoundationDbContext db, Guid academy, Guid sport, Guid? branch, ClaimsPrincipal actor, string before, object after, TimeProvider clock) =>
        db.SportSettingsAudits.Add(new() { Id = Guid.NewGuid(), AcademyId = academy, SportId = sport, BranchId = branch, ActorUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!), Before = before, After = JsonSerializer.Serialize(after), CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() });
}
