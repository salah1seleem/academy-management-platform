using System.Text.Json;
using System.Text.Json.Serialization;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public sealed record ExceptionalEnrollmentDiscount(RenewalDiscountType Type,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Value,
    string? Reason, bool ReplaceExistingDiscount = false);

public static class ManualEnrollmentDiscount
{
    public const string Permission = "workflow.subscriptions.exceptional-discount";
    public static async Task<bool> Allowed(FoundationDbContext db, TenantMembership t)
    {
        if (t.Role == AcademyRole.AcademyOwner) return true;
        return t.Role == AcademyRole.AcademyAdmin
            && await db.StaffGrants.AnyAsync(g => g.AcademyId == t.AcademyId && g.MembershipId == t.MembershipId && g.Permission == Permission && g.IsActive && g.Allowed)
            && await db.StaffActionGrants.AnyAsync(g => g.AcademyId == t.AcademyId && g.MembershipId == t.MembershipId && g.Module == "subscriptions" && g.IsActive && g.Read && g.Add);
    }
    public static RenewalFinancialSnapshot Apply(RenewalFinancialSnapshot current, ExceptionalEnrollmentDiscount input, Guid actor, DateTimeOffset at)
    {
        var reason = string.IsNullOrWhiteSpace(input.Reason) ? "خصم استثنائي عند التسجيل" : input.Reason.Trim();
        if (!Enum.IsDefined(input.Type) || input.Value <= 0 || input.Value > (input.Type == RenewalDiscountType.Percentage ? 100 : current.OriginalAmount)
            || string.IsNullOrEmpty(reason) || reason.Length > 500)
            throw new CashRejected("راجع نوع الخصم وقيمته وسببه؛ النسبة حتى 100 والمبلغ لا يتجاوز السعر الأصلي.", 422);
        if (current.DiscountAmount > 0 && !input.ReplaceExistingDiscount)
            throw new CashRejected("يوجد عرض مستحق؛ يلزم تأكيد استبداله بالخصم الاستثنائي. لا تُجمع الخصومات.", 409);
        var value = decimal.Round(input.Value, 2, MidpointRounding.AwayFromZero);
        // One pricing engine and currency rounding for both permanent offers and exceptions.
        var amount = DiscountPricing.Amount(new DiscountRule { ArabicName = "خصم استثنائي", Reason = reason, Type = input.Type, Percentage = value, FixedAmount = value }, current.OriginalAmount);
        if (amount <= 0) throw new CashRejected("قيمة الخصم بعد التقريب يجب أن تكون أكبر من صفر.", 422);
        return new(current.OriginalAmount, input.Type, value, amount, current.OriginalAmount - amount, reason, actor, at);
    }
    public sealed record PricingView(decimal OriginalAmount, RenewalDiscountType? DiscountType, decimal? DiscountValue, decimal DiscountAmount, decimal FinalAmount, string Currency);
    // Call only after the caller has authorized this exact period. Internal reasons/actors stay private.
    public static Task<PricingView?> PriceForPeriod(FoundationDbContext db, Guid academy, Guid periodId) =>
        (from period in db.SubscriptionPeriods.AsNoTracking().Where(p=>p.AcademyId==academy&&p.Id==periodId)
         from r in db.RenewalRequests.Where(r=>r.AcademyId==academy&&r.SportEnrollmentId==period.SportEnrollmentId&&r.RegistrationPayloadHash!=null
             && (period.CollectionId!=null&&db.Collections.Any(c=>c.Id==period.CollectionId&&c.RenewalRequestId==r.Id)
                 ||period.FreeTrainingApprovalId!=null&&db.FreeTrainingApprovals.Any(a=>a.Id==period.FreeTrainingApprovalId&&a.IdempotencyKey==r.IdempotencyKey&&a.ActorUserId==r.RequestedByUserId&&a.RequestHash==r.RegistrationPayloadHash)))
         select new PricingView(r.OriginalAmount,r.DiscountType,r.DiscountValue,r.DiscountAmount,r.FinalAmount,r.Currency)).SingleOrDefaultAsync();
    private static RenewalDiscountAdjustment Adjustment(Guid academy, Guid actor, Guid renewal, RenewalFinancialSnapshot q, string key, DateTimeOffset at, RenewalFinancialSnapshot? previous = null) => new() {
        Id = Guid.NewGuid(), AcademyId = academy, RenewalRequestId = renewal,
        NewDiscountType = q.DiscountType, NewDiscountValue = q.DiscountValue,
        PreviousDiscountType = previous?.DiscountType, PreviousDiscountValue = previous?.DiscountValue,
        PreviousDiscountRuleId = previous?.DiscountRuleId, PreviousDiscountRuleRevision = previous?.DiscountRuleRevision,
        PreviousDiscountAmount = previous?.DiscountAmount ?? 0, PreviousFinalAmount = previous?.FinalAmount ?? q.OriginalAmount, NewDiscountAmount = q.DiscountAmount, NewFinalAmount = q.FinalAmount,
        Reason = q.DiscountReason!, PerformedByUserId = actor, IdempotencyKey = key, CreatedAtUtc = at, UpdatedAtUtc = at
    };
    public static async Task Audit(FoundationDbContext db, Guid academy, Guid actor, Guid payment, RenewalFinancialSnapshot q, string key, DateTimeOffset at, RenewalFinancialSnapshot? previous = null)
    {
        var renewal = await db.PaymentRequests.Where(p => p.Id == payment).Select(p => p.RenewalRequestId).SingleAsync();
        db.RenewalDiscountAdjustments.Add(Adjustment(academy, actor, renewal, q, key, at, previous));
        await db.SaveChangesAsync();
    }
    public static async Task RecordWaiver(FoundationDbContext db, TenantMembership t, Guid actor, SportEnrollment enrollment, Guid planId, RenewalFinancialSnapshot q, string key, string hash, ISubscriptionClock clock, RenewalFinancialSnapshot? previous = null)
    {
        var p = await db.SubscriptionPlans.SingleAsync(p => p.Id == planId);
        var terms = PlanTerms.From(p); var now = clock.UtcNow;
        FreeTrainingService.Record(db,t,actor,enrollment,p,terms,clock.Today,clock.Today,q.DiscountReason!,key,hash,hash,now);
        // A service authorization and immutable pricing audit, with no fictitious collection/payment/receipt.
        var r = new RenewalRequest { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SportEnrollmentId = enrollment.Id, SportId = enrollment.SportId, SubscriptionPlanId = p.Id,
            RequestedByUserId = actor, RequestedAtUtc = now, OriginalAmount = q.OriginalAmount, DiscountType = q.DiscountType, DiscountValue = q.DiscountValue,
            DiscountAmount = q.DiscountAmount, FinalAmount = 0, AmountExpected = 0, Currency = p.Currency, DiscountReason = q.DiscountReason,
            DiscountAppliedByUserId = actor, DiscountAppliedAtUtc = now, Status = RenewalRequestStatus.Paid, IdempotencyKey = key, RegistrationPayloadHash = hash,
            CreatedAtUtc = now, UpdatedAtUtc = now };
        terms.Capture(r); db.RenewalRequests.Add(r); db.RenewalDiscountAdjustments.Add(Adjustment(t.AcademyId, actor, r.Id, q, key, now, previous));
        await db.SaveChangesAsync();
    }
}
