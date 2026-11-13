using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;

public static class SubscriptionDiscountEndpoints
{
    public static void MapSubscriptionDiscountEndpoints(this WebApplication app)
    {
        var renewals = app.MapGroup("/api/v1/subscriptions/renewals").RequireAuthorization(AcademyPermissions.SubscriptionRead);
        renewals.MapGet("/{id:guid}", Details);
        renewals.MapGet("/{id:guid}/discount/attempt", Attempt).RequireAuthorization(AcademyPermissions.SubscriptionDiscountManage);
        renewals.MapPost("/{id:guid}/discount", Apply).RequireAuthorization(AcademyPermissions.SubscriptionDiscountManage).AddEndpointFilter<CsrfFilter>();
        renewals.MapPost("/{id:guid}/discount/remove", Remove).RequireAuthorization(AcademyPermissions.SubscriptionDiscountManage).AddEndpointFilter<CsrfFilter>();
    }

    private static async Task<IResult> Details(Guid id, CurrentTenant tenant, FoundationDbContext db,
        ClaimsPrincipal actor, IAuthorizationService authorization, HttpContext http)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        var current = (await tenant.ResolveAsync())!;
        var renewal = await db.RenewalRequests.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.Id == id)
            .Select(x => new
            {
                x.Id, player = x.SportEnrollment.Player.ArabicName, sport = x.SubscriptionPlan.Sport.ArabicName,
                plan = (x.PlanNameSnapshot ?? x.SubscriptionPlan.ArabicName), x.OriginalAmount, discountType = x.DiscountType == null ? null : x.DiscountType.ToString(),
                x.DiscountValue, x.DiscountAmount, x.FinalAmount, x.Currency, status = x.Status.ToString(),
                x.DiscountReason, discountAppliedBy = x.DiscountAppliedByUserId == null ? null : db.Users.Where(u => u.Id == x.DiscountAppliedByUserId).Select(u => u.DisplayName).Single(),
                x.DiscountAppliedAtUtc, x.PaymentRequestId,
                paymentStatus = db.PaymentRequests.Where(p => p.AcademyId == current.AcademyId && p.Id == x.PaymentRequestId && p.RenewalRequestId == x.Id)
                    .Select(p => p.Status.ToString()).SingleOrDefault(),
                x.RequestedAtUtc, x.Version,x.VipRewardUseId
            }).SingleOrDefaultAsync();
        if (renewal is null) return Results.NotFound();
        var audit = await db.RenewalDiscountAdjustments.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.RenewalRequestId == id)
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => new
            {
                x.Id, previousDiscountType = x.PreviousDiscountType == null ? null : x.PreviousDiscountType.ToString(), x.PreviousDiscountValue,
                newDiscountType = x.NewDiscountType == null ? null : x.NewDiscountType.ToString(), x.NewDiscountValue,
                x.PreviousDiscountAmount, x.NewDiscountAmount, x.PreviousFinalAmount, x.NewFinalAmount, x.Reason,
                performedBy = db.Users.Where(u => u.Id == x.PerformedByUserId).Select(u => u.DisplayName).Single(), x.CreatedAtUtc
            }).ToListAsync();
        var canModify = renewal.VipRewardUseId is null && (renewal.status is "PendingPayment" or "PaymentInProgress") &&
            (renewal.paymentStatus is "Created" or "Pending") &&
            await SubscriptionAdjustmentEndpoints.AllowsPeriodAction(current, db, actor, authorization,
                AcademyPermissions.SubscriptionDiscountManage, $"/api/v1/subscriptions/renewals/{id}/discount", "POST");
        var visiblePaymentId = renewal.PaymentRequestId is { } paymentId && renewal.paymentStatus is not null &&
            await Slice3Endpoints.CanReadStaffRenewalPayment(actor, authorization) &&
            await SubscriptionAdjustmentEndpoints.AllowsPeriodAction(current, db, actor, authorization,
                AcademyPermissions.PaymentRead, $"/api/v1/subscriptions/payments/{paymentId}", "GET") ? paymentId : (Guid?)null;
        return Results.Ok(new { renewal.Id, renewal.player, renewal.sport, renewal.plan, renewal.OriginalAmount, renewal.discountType,
            renewal.DiscountValue, renewal.DiscountAmount, renewal.FinalAmount, renewal.Currency, renewal.status, renewal.DiscountReason,
            renewal.discountAppliedBy, renewal.DiscountAppliedAtUtc, renewal.PaymentRequestId, visiblePaymentId, renewal.paymentStatus,
            renewal.RequestedAtUtc, renewal.Version, canModify, adjustments = audit });
    }

    private static async Task<IResult> Attempt(Guid id, CurrentTenant tenant, FoundationDbContext db, HttpContext http)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        var current = (await tenant.ResolveAsync())!;
        var actor = Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var key = http.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) return Results.NotFound();
        var saved = await db.RenewalDiscountAdjustments.AsNoTracking()
            .Where(row => row.AcademyId == current.AcademyId && row.RenewalRequestId == id &&
                row.PerformedByUserId == actor && row.IdempotencyKey == key)
            .Select(row => new { row.RenewalRequestId, adjustmentId = row.Id })
            .SingleOrDefaultAsync(http.RequestAborted);
        return saved is null ? Results.NotFound() : Results.Ok(saved);
    }

    private static Task<IResult> Apply(Guid id, DiscountRequest request, HttpContext http, CurrentTenant tenant,
        ClaimsPrincipal principal, SubscriptionDiscountService service)
        => Run(tenant, principal, http, (academy, actor, key) => service.ApplyAsync(academy, actor, id, request.Type, request.Value, request.Reason, request.ExpectedVersion, key, http.RequestAborted));

    private static Task<IResult> Remove(Guid id, RemoveDiscountRequest request, HttpContext http, CurrentTenant tenant,
        ClaimsPrincipal principal, SubscriptionDiscountService service)
        => Run(tenant, principal, http, (academy, actor, key) => service.RemoveAsync(academy, actor, id, request.Reason, request.ExpectedVersion, key, http.RequestAborted));

    private static async Task<IResult> Run(CurrentTenant tenant, ClaimsPrincipal principal, HttpContext http,
        Func<Guid, Guid, string, Task<RenewalDiscountResult>> action)
    {
        var current = (await tenant.ResolveAsync())!;
        try { return Results.Ok(await action(current.AcademyId, Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!), http.Request.Headers["Idempotency-Key"].ToString())); }
        catch (DiscountNotFoundException) { return Results.NotFound(); }
        catch (DiscountValidationException ex) { return Results.UnprocessableEntity(new { message = ex.Message }); }
        catch (DiscountConflictException ex) { return Results.Conflict(new { message = ex.Message }); }
    }
}

public sealed record DiscountRequest(RenewalDiscountType Type, decimal Value, string Reason, uint ExpectedVersion);
public sealed record RemoveDiscountRequest(string Reason, uint ExpectedVersion);
