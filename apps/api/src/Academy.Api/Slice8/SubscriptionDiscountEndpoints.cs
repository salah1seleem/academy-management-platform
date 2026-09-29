using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;

public static class SubscriptionDiscountEndpoints
{
    public static void MapSubscriptionDiscountEndpoints(this WebApplication app)
    {
        var renewals = app.MapGroup("/api/v1/subscriptions/renewals").RequireAuthorization(AcademyPermissions.SubscriptionRead);
        renewals.MapGet("/{id:guid}", Details);
        renewals.MapPost("/{id:guid}/discount", Apply).RequireAuthorization(AcademyPermissions.SubscriptionDiscountManage).AddEndpointFilter<CsrfFilter>();
        renewals.MapPost("/{id:guid}/discount/remove", Remove).RequireAuthorization(AcademyPermissions.SubscriptionDiscountManage).AddEndpointFilter<CsrfFilter>();
    }

    private static async Task<IResult> Details(Guid id, CurrentTenant tenant, FoundationDbContext db)
    {
        var current = (await tenant.ResolveAsync())!;
        var renewal = await db.RenewalRequests.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.Id == id)
            .Select(x => new
            {
                x.Id, player = x.SportEnrollment.Player.ArabicName, sport = x.SubscriptionPlan.Sport.ArabicName,
                plan = x.SubscriptionPlan.ArabicName, x.OriginalAmount, discountType = x.DiscountType == null ? null : x.DiscountType.ToString(),
                x.DiscountValue, x.DiscountAmount, x.FinalAmount, x.Currency, status = x.Status.ToString(),
                x.DiscountReason, discountAppliedBy = x.DiscountAppliedByUserId == null ? null : db.Users.Where(u => u.Id == x.DiscountAppliedByUserId).Select(u => u.DisplayName).Single(),
                x.DiscountAppliedAtUtc, x.PaymentRequestId, paymentStatus = db.PaymentRequests.Where(p => p.AcademyId == current.AcademyId && p.Id == x.PaymentRequestId).Select(p => p.Status.ToString()).Single(),
                x.RequestedAtUtc, x.Version
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
        var canModify = renewal.status is "PendingPayment" or "PaymentInProgress" && renewal.paymentStatus is "Created" or "Pending";
        return Results.Ok(new { renewal.Id, renewal.player, renewal.sport, renewal.plan, renewal.OriginalAmount, renewal.discountType,
            renewal.DiscountValue, renewal.DiscountAmount, renewal.FinalAmount, renewal.Currency, renewal.status, renewal.DiscountReason,
            renewal.discountAppliedBy, renewal.DiscountAppliedAtUtc, renewal.PaymentRequestId, renewal.paymentStatus,
            renewal.RequestedAtUtc, renewal.Version, canModify, adjustments = audit });
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
