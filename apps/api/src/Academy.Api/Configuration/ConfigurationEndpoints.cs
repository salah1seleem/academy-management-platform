using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Configuration;

public static class ConfigurationEndpoints
{
    public static void MapVersionedConfiguration(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/manage/configuration").RequireAuthorization(AcademyPermissions.AcademySettingsManage);
        api.MapGet("/", List);
        api.MapPost("/drafts", Draft).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/{id:guid}/transition", Change).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/academy/features", async (CurrentTenant tenant, FoundationDbContext db, AcademyConfigurationService config) =>
        {
            var t = (await tenant.ResolveAsync())!;
            var version = await db.AcademyConfigurationVersions.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.State == ConfigurationState.Active);
            var enabled = new List<string>(); foreach (var f in AcademyFeatures.All) if (await config.Enabled(t.AcademyId, f)) enabled.Add(f);
            return Results.Ok(new { revision = version?.Revision ?? 0, features = enabled });
        }).RequireAuthorization(AcademyPermissions.TenantAccess);
    }
    private static async Task<IResult> List(CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        return Results.Ok(new
        {
            canManageMediaRetention=await Academy.Api.Operations.PrivateMediaLifecycle.FullOwner(db,t),
            entitlements = await db.AcademyFeatureEntitlements.Where(x => x.AcademyId == t.AcademyId && x.IsActive).Select(x => new { x.Feature, x.Enabled, x.PlanCode }).ToListAsync(),
            versions = (await db.AcademyConfigurationVersions.AsNoTracking().Where(x => x.AcademyId == t.AcademyId).OrderByDescending(x => x.Revision).Take(50).ToListAsync()).Select(x => new { x.Id, x.Revision, x.State, x.ContentHash, x.Version, x.EffectiveAtUtc, settings = AcademyRuntimeSettings.Read(x.Payload) }),
            history = await db.ConfigurationTransitions.AsNoTracking().Where(x => x.AcademyId == t.AcademyId).OrderByDescending(x => x.CreatedAtUtc).Take(100).Select(x => new { x.ConfigurationId, x.From, x.To, x.CreatedAtUtc }).ToListAsync()
        });
    }
    private static async Task<IResult> Draft(AcademyRuntimeSettings body, ClaimsPrincipal actor, CurrentTenant tenant, FoundationDbContext db, AcademyConfigurationService service)
    {
        var t = (await tenant.ResolveAsync())!;
        if (body.Features is null || body.Features.Count > AcademyFeatures.All.Length || body.TimeZone?.Length > 80 || body.Currency?.Length != 3) return Results.Problem(statusCode: 422, title: "إعدادات غير صالحة");
        var audienceErrors = Academy.Api.Help.HelpAudiencePolicy.Validate(body.HelpAudiences);
        if (audienceErrors.Length > 0) return Results.ValidationProblem(new Dictionary<string, string[]> { { "helpAudiences", audienceErrors } }, statusCode: 422);
        await using var tx = await db.Database.BeginTransactionAsync(); await Lock(db, t.AcademyId);
        if(body.MediaRetention!=(await service.Active(t.AcademyId))?.MediaRetention&&!await Academy.Api.Operations.PrivateMediaLifecycle.FullOwner(db,t))return Results.Forbid();
        if(body.MediaRetention is {Days:<1 or >3650})return Results.UnprocessableEntity(new{message="مدة الاحتفاظ بين يوم و3650 يومًا، أو اتركها دون حذف آلي."});
        if (t.Role != AcademyRole.AcademyOwner && !Academy.Api.Help.HelpAudiencePolicy.Same(body.HelpAudiences, (await service.Active(t.AcademyId))?.HelpAudiences)) return Results.Forbid();
        if (t.Role != AcademyRole.AcademyOwner && body.CashEnabled != ((await service.Active(t.AcademyId))?.CashEnabled ?? false)) return Results.Forbid();
        var row = await service.CreateDraft(t.AcademyId, Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!), body);
        await tx.CommitAsync(); return Results.Ok(new { row.Id, row.Revision });
    }
    private static async Task<IResult> Change(Guid id, ConfigurationChange body, ClaimsPrincipal actor, CurrentTenant tenant, FoundationDbContext db, AcademyConfigurationService service, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var user = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await using var tx = await db.Database.BeginTransactionAsync(); await Lock(db, t.AcademyId);
        var row = await db.AcademyConfigurationVersions.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id);
        if (row is null) return Results.NotFound();
        if (row.Version != body.Version) return Results.Problem(statusCode: 409, title: "تغير إصدار الإعدادات؛ حدّث الصفحة");
        var valid = (row.State, body.Target) is (ConfigurationState.Draft, ConfigurationState.Validated) or (ConfigurationState.Validated, ConfigurationState.Staged) or (ConfigurationState.Staged, ConfigurationState.Active) or (ConfigurationState.Active, ConfigurationState.Suspended) or (ConfigurationState.Suspended, ConfigurationState.Staged);
        if (!valid) return Results.Problem(statusCode: 409, title: "انتقال حالة الإعدادات غير مسموح");
        if (body.Target == ConfigurationState.Suspended && t.Role != AcademyRole.AcademyOwner && AcademyRuntimeSettings.Read(row.Payload).CashEnabled) return Results.Forbid();
        if (body.Target is ConfigurationState.Validated or ConfigurationState.Active or ConfigurationState.Staged)
        {
            var errors = await service.Validate(row); if (errors.Length > 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["settings"] = errors }, statusCode: 422);
        }
        if (body.Target == ConfigurationState.Staged) row.EffectiveAtUtc = body.EffectiveAtUtc ?? clock.GetUtcNow();
        if (body.Target == ConfigurationState.Active)
        {
            if (row.EffectiveAtUtc > clock.GetUtcNow()) return Results.Problem(statusCode: 409, title: "لم يحن موعد سريان الإعدادات");
            var previous = await db.AcademyConfigurationVersions.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.State == ConfigurationState.Active);
            if(AcademyRuntimeSettings.Read(row.Payload).MediaRetention!=(previous==null?null:AcademyRuntimeSettings.Read(previous.Payload).MediaRetention)&&!await Academy.Api.Operations.PrivateMediaLifecycle.FullOwner(db,t))return Results.Forbid();
            if (t.Role != AcademyRole.AcademyOwner && !Academy.Api.Help.HelpAudiencePolicy.Same(AcademyRuntimeSettings.Read(row.Payload).HelpAudiences, previous is null ? null : AcademyRuntimeSettings.Read(previous.Payload).HelpAudiences)) return Results.Forbid();
            if (t.Role != AcademyRole.AcademyOwner && AcademyRuntimeSettings.Read(row.Payload).CashEnabled != (previous is not null && AcademyRuntimeSettings.Read(previous.Payload).CashEnabled)) return Results.Forbid();
            if (previous is not null) await service.Transition(previous, ConfigurationState.Superseded, user);
        }
        await service.Transition(row, body.Target, user); await tx.CommitAsync(); return Results.NoContent();
    }
    private static Task<int> Lock(FoundationDbContext db, Guid id) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + id}, 0))");
}
public sealed record ConfigurationChange(ConfigurationState Target, uint Version, DateTimeOffset? EffectiveAtUtc = null);
