using Academy.Infrastructure.Subscriptions;

namespace Academy.UnitTests;

public class RenewalOpportunityTests
{
    [Theory]
    [InlineData(SubscriptionOrigin.Collected, true)]
    [InlineData(SubscriptionOrigin.ImportedHistorical, true)]
    [InlineData(SubscriptionOrigin.ApprovedWaiver, false)]
    [InlineData((SubscriptionOrigin)999, false)]
    public void Paid_forecast_excludes_waivers_and_unknown_sources(SubscriptionOrigin source, bool expected) => Assert.Equal(expected, RenewalOpportunity.PaidForecastSource(source));
    [Theory]
    [InlineData(SubscriptionPlanType.Duration, null, -1, SubscriptionPeriodStatus.Active, true)]
    [InlineData(SubscriptionPlanType.Duration, null, 0, SubscriptionPeriodStatus.Active, false)]
    [InlineData(SubscriptionPlanType.Combined, 0, 10, SubscriptionPeriodStatus.Active, true)]
    [InlineData(SubscriptionPlanType.Sessions, 0, null, SubscriptionPeriodStatus.Active, true)]
    [InlineData(SubscriptionPlanType.Sessions, null, null, SubscriptionPeriodStatus.Active, false)]
    [InlineData(SubscriptionPlanType.Duration, null, -1, SubscriptionPeriodStatus.Frozen, false)]
    [InlineData(SubscriptionPlanType.Duration, null, -1, SubscriptionPeriodStatus.Cancelled, false)]
    public void Expiry_is_not_inferred_from_missing_data_or_cancelled_and_frozen_service(SubscriptionPlanType type, int? remaining, int? endsIn, SubscriptionPeriodStatus status, bool expected)
    {
        var day = new DateOnly(2026, 9, 28);
        var plan = new SubscriptionPlan { ArabicName = "باقة اختبار", PlanType = type, Currency = "EGP" };
        var period = new SubscriptionPeriod { StartDate = day.AddDays(-30), EndDate = endsIn.HasValue ? day.AddDays(endsIn.Value) : null, RemainingSessions = remaining, Status = status, CurrencySnapshot = "EGP" };
        Assert.Equal(expected, RenewalOpportunity.Expired(period, plan, day));
        period.IsActive = false; Assert.False(RenewalOpportunity.Expired(period, plan, day));
    }
    [Fact]
    public void Historical_type_wins_over_changed_catalog_and_future_service_is_not_expired()
    {
        var day = new DateOnly(2026, 9, 28);
        var plan = new SubscriptionPlan { ArabicName = "باقة أصبحت مدة", PlanType = SubscriptionPlanType.Duration, Currency = "EGP" };
        var period = new SubscriptionPeriod { StartDate = day, RemainingSessions = 0, Status = SubscriptionPeriodStatus.Active, CurrencySnapshot = "EGP", PlanTypeSnapshot = SubscriptionPlanType.Sessions, PlanNameSnapshot = "باقة حصص محفوظة" };
        Assert.True(RenewalOpportunity.Expired(period, plan, day));
        period.StartDate = day.AddDays(1); Assert.False(RenewalOpportunity.Expired(period, plan, day));
        period.StartDate = day; period.ServiceKindSnapshot = AcademyServiceKind.DayPass;
        Assert.False(RenewalOpportunity.Expired(period, plan, day));
    }
}
