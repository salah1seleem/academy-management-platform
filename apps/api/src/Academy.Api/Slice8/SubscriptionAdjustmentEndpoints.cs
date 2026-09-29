using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;

public static class SubscriptionAdjustmentEndpoints
{
    public static void MapSubscriptionAdjustmentEndpoints(this WebApplication app)
    {
        var periods = app.MapGroup("/api/v1/subscriptions/periods").RequireAuthorization(AcademyPermissions.SubscriptionRead);
        periods.MapGet("/{id:guid}", PeriodDetails);
        periods.MapPost("/{id:guid}/freeze", Freeze).RequireAuthorization(AcademyPermissions.SubscriptionAdjustmentManage).AddEndpointFilter<CsrfFilter>();
        periods.MapPost("/{id:guid}/resume", Resume).RequireAuthorization(AcademyPermissions.SubscriptionAdjustmentManage).AddEndpointFilter<CsrfFilter>();
        periods.MapPost("/{id:guid}/days", AdjustDays).RequireAuthorization(AcademyPermissions.SubscriptionAdjustmentManage).AddEndpointFilter<CsrfFilter>();
        periods.MapPost("/{id:guid}/cancel", Cancel).RequireAuthorization(AcademyPermissions.SubscriptionAdjustmentManage).AddEndpointFilter<CsrfFilter>();

        app.MapGet("/api/v1/guardian/subscriptions/periods/{id:guid}/adjustments", GuardianPeriod)
            .RequireAuthorization(AcademyPermissions.GuardianOwnRenewal);
    }

    private static async Task<IResult> PeriodDetails(Guid id, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!;
        var period = await db.SubscriptionPeriods.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.Id == id)
            .Select(x => new
            {
                x.Id, x.SportEnrollmentId, x.StartDate, x.EndDate, x.FrozenFromDate, x.InitialSessions, x.RemainingSessions, x.Status, x.Version,
                plan = x.SubscriptionPlan.ArabicName, planType = x.SubscriptionPlan.PlanType,
                player = x.SportEnrollment.Player.ArabicName, playerCode = x.SportEnrollment.Player.PlayerCode,
                sport = x.SportEnrollment.Sport.ArabicName, branch = x.SportEnrollment.Branch.ArabicName,
                group = x.SportEnrollment.TrainingGroup.ArabicName, x.PriceSnapshot, x.CurrencySnapshot,
                receiptId = db.Receipts.Where(r => r.AcademyId == current.AcademyId && r.CollectionId == x.CollectionId).Select(r => (Guid?)r.Id).SingleOrDefault()
            }).SingleOrDefaultAsync();
        if (period is null) return Results.NotFound();
        var status = SubscriptionPeriodState.Resolve(period.Status, period.StartDate, period.EndDate, period.planType, period.RemainingSessions, clock.Today);
        var history = await db.SubscriptionAdjustments.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.SubscriptionPeriodId == id)
            .OrderByDescending(x => x.PerformedAtUtc)
            .Select(x => new
            {
                x.Id, type = x.AdjustmentType.ToString(), x.EffectiveDate, x.DaysDelta, x.OldEndDate, x.NewEndDate,
                x.Reason, performedBy = db.Users.Where(u => u.Id == x.PerformedByUserId).Select(u => u.DisplayName).Single(), x.PerformedAtUtc
            }).ToListAsync();
        return Results.Ok(new
        {
            period.Id, period.SportEnrollmentId, period.player, period.playerCode, period.sport, period.branch, period.group, period.plan,
            planType = period.planType.ToString(), period.StartDate, period.EndDate, period.FrozenFromDate,
            period.InitialSessions, period.RemainingSessions, status = status.ToString(), period.Version,
            period.PriceSnapshot, period.CurrencySnapshot, period.receiptId, adjustments = history
        });
    }

    private static async Task<IResult> GuardianPeriod(Guid id, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!;
        var actor = UserId(principal);
        var period = await db.SubscriptionPeriods.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.Id == id &&
                db.GuardianPlayerLinks.Any(link => link.AcademyId == current.AcademyId && link.Guardian.UserId == actor && link.PlayerId == x.SportEnrollment.PlayerId && link.IsActive))
            .Select(x => new { x.Id, x.StartDate, x.EndDate, x.FrozenFromDate, x.RemainingSessions, x.Status, planType = x.SubscriptionPlan.PlanType, plan = x.SubscriptionPlan.ArabicName, sport = x.SportEnrollment.Sport.ArabicName })
            .SingleOrDefaultAsync();
        if (period is null) return Results.NotFound();
        var status = SubscriptionPeriodState.Resolve(period.Status, period.StartDate, period.EndDate, period.planType, period.RemainingSessions, clock.Today);
        var events = await db.SubscriptionAdjustments.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.SubscriptionPeriodId == id)
            .OrderByDescending(x => x.PerformedAtUtc).Select(x => new { type = x.AdjustmentType.ToString(), x.EffectiveDate, x.DaysDelta, x.NewEndDate }).ToListAsync();
        return Results.Ok(new { period.Id, period.plan, period.sport, period.StartDate, period.EndDate, period.FrozenFromDate, period.RemainingSessions, status = status.ToString(), adjustments = events });
    }

    private static Task<IResult> Freeze(Guid id, AdjustmentDateRequest request, HttpContext http, CurrentTenant tenant, ClaimsPrincipal principal, SubscriptionAdjustmentService service)
        => Run(tenant, principal, http, (academy, actor, key) => service.FreezeAsync(academy, actor, id, request.EffectiveDate, request.Reason, request.ExpectedVersion, key, http.RequestAborted));

    private static Task<IResult> Resume(Guid id, AdjustmentDateRequest request, HttpContext http, CurrentTenant tenant, ClaimsPrincipal principal, SubscriptionAdjustmentService service)
        => Run(tenant, principal, http, (academy, actor, key) => service.ResumeAsync(academy, actor, id, request.EffectiveDate, request.Reason, request.ExpectedVersion, key, http.RequestAborted));

    private static Task<IResult> AdjustDays(Guid id, DayAdjustmentRequest request, HttpContext http, CurrentTenant tenant, ClaimsPrincipal principal, SubscriptionAdjustmentService service)
    {
        if (!Enum.IsDefined(request.Direction))
            return Task.FromResult<IResult>(Results.UnprocessableEntity(new { message = "نوع تعديل الأيام غير صحيح." }));
        return Run(tenant, principal, http, (academy, actor, key) => service.AdjustDaysAsync(academy, actor, id, request.Days, request.Direction == DayAdjustmentDirection.Add, request.Reason, request.ExpectedVersion, key, http.RequestAborted));
    }

    private static Task<IResult> Cancel(Guid id, AdjustmentDateRequest request, HttpContext http, CurrentTenant tenant, ClaimsPrincipal principal, SubscriptionAdjustmentService service)
        => Run(tenant, principal, http, (academy, actor, key) => service.CancelAsync(academy, actor, id, request.EffectiveDate, request.Reason, request.ExpectedVersion, key, http.RequestAborted));

    private static async Task<IResult> Run(CurrentTenant tenant, ClaimsPrincipal principal, HttpContext http, Func<Guid, Guid, string, Task<SubscriptionAdjustmentResult>> action)
    {
        var current = (await tenant.ResolveAsync())!;
        var key = http.Request.Headers["Idempotency-Key"].ToString();
        try { return Results.Ok(await action(current.AcademyId, UserId(principal), key)); }
        catch (AdjustmentNotFoundException) { return Results.NotFound(); }
        catch (AdjustmentValidationException ex) { return Results.UnprocessableEntity(new { message = ex.Message }); }
        catch (AdjustmentConflictException ex) { return Results.Conflict(new { message = ex.Message }); }
    }

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public enum DayAdjustmentDirection { Add = 1, Deduct = 2 }
public sealed record AdjustmentDateRequest(DateOnly EffectiveDate, string Reason, uint ExpectedVersion);
public sealed record DayAdjustmentRequest(DayAdjustmentDirection Direction, int Days, string Reason, uint ExpectedVersion);
