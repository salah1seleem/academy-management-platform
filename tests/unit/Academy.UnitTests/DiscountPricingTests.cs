using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Subscriptions;

namespace Academy.UnitTests;

public class DiscountPricingTests
{
    private static DiscountRule Percent(decimal percentage) => new() { Id = Guid.NewGuid(), ArabicName = "عرض اختبار", Reason = "", Percentage = percentage };
    private static DiscountRule Fixed(decimal value) => new() { Id = Guid.NewGuid(), ArabicName = "قيمة اختبار", Reason = "", Type = RenewalDiscountType.FixedAmount, FixedAmount = value };
    [Fact] public void Chooses_best_money_not_highest_raw_value_and_never_stacks()
    {
        var p = Percent(20); var f = Fixed(100); var winner = DiscountPricing.Best([f, p], 1200);
        Assert.Same(p, winner); Assert.Equal(240, DiscountPricing.Amount(winner!, 1200));
        Assert.Same(f, DiscountPricing.Best([f, p], 400));
    }
    [Theory]
    [InlineData(100, 99, 99)]
    [InlineData(50, 1000, 50)]
    public void Fixed_amount_is_capped_at_price(decimal price, decimal value, decimal expected) => Assert.Equal(expected, DiscountPricing.Amount(Fixed(value), price));
    [Fact] public void Explicit_cap_and_precise_half_rounding_are_respected()
    {
        var p = Percent(33.335m); Assert.Equal(66.67m, DiscountPricing.Amount(p, 200));
        p.MaximumAmount = 50; Assert.Equal(50, DiscountPricing.Amount(p, 200));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiscountPricing.Amount(p, 200.001m));
    }
    [Fact] public void Ties_use_priority_then_stable_identifier()
    {
        var a = Fixed(100); var b = Percent(10); a.Priority = 1; b.Priority = 2;
        Assert.Same(a, DiscountPricing.Best([b, a], 1000)); b.Priority = 1;
        var expected = new[] { a, b }.OrderBy(x => x.Id).First(); Assert.Same(expected, DiscountPricing.Best([b, a], 1000));
    }
    [Fact] public void Date_branch_sport_plan_currency_and_activation_are_all_required()
    {
        var today = new DateOnly(2026, 9, 28); var branch = Guid.NewGuid();
        var plan = new SubscriptionPlan { Id = Guid.NewGuid(), SportId = Guid.NewGuid(), ArabicName = "باقة اختبار", Currency = "EGP" };
        var rule = Fixed(100); rule.StartsOn = today; rule.EndsOn = today; rule.BranchId = branch; rule.SportId = plan.SportId; rule.SubscriptionPlanId = plan.Id;
        Assert.True(DiscountPricing.Eligible(rule, today, branch, plan));
        Assert.False(DiscountPricing.Eligible(rule, today.AddDays(1), branch, plan));
        Assert.False(DiscountPricing.Eligible(rule, today.AddDays(-1), branch, plan));
        Assert.False(DiscountPricing.Eligible(rule, today, Guid.NewGuid(), plan));
        Assert.False(DiscountPricing.Eligible(rule, today, branch, null));
        plan.Currency = "USD"; Assert.False(DiscountPricing.Eligible(rule, today, branch, plan));
        plan.Currency = "EGP"; rule.IsActive = false; Assert.False(DiscountPricing.Eligible(rule, today, branch, plan));
    }
}
