using Academy.Infrastructure.Operations;

namespace Academy.Infrastructure.Subscriptions;

public static class DiscountPricing
{
    public static bool Eligible(DiscountRule r, DateOnly today, Guid? branch, SubscriptionPlan? plan) => r.IsActive
        && (!r.StartsOn.HasValue || r.StartsOn <= today) && (!r.EndsOn.HasValue || r.EndsOn >= today)
        && (!r.BranchId.HasValue || r.BranchId == branch) && (!r.SportId.HasValue || r.SportId == plan?.SportId)
        && (!r.SubscriptionPlanId.HasValue || r.SubscriptionPlanId == plan?.Id)
        && (plan is null || r.Type == RenewalDiscountType.Percentage && !r.MaximumAmount.HasValue || r.Currency == plan.Currency);
    public static decimal Amount(DiscountRule r, decimal basis)
    {
        if (basis < 0 || decimal.Round(basis, 2) != basis) throw new ArgumentOutOfRangeException(nameof(basis));
        var raw = r.Type == RenewalDiscountType.Percentage ? basis * r.Percentage / 100m : r.FixedAmount ?? 0;
        if (r.MaximumAmount is decimal cap) raw = Math.Min(raw, cap);
        return decimal.Round(Math.Clamp(raw, 0, basis), 2, MidpointRounding.AwayFromZero);
    }
    // One winner: greatest monetary benefit, then explicit priority, then a stable ID. Never stack implicitly.
    public static DiscountRule? Best(IEnumerable<DiscountRule> rules, decimal basis) => rules
        .Where(r => Amount(r, basis) > 0).OrderByDescending(r => Amount(r, basis)).ThenBy(r => r.Priority).ThenBy(r => r.Id).FirstOrDefault();
}
