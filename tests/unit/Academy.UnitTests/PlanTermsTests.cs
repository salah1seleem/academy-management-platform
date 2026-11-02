using Academy.Infrastructure.Subscriptions;

namespace Academy.UnitTests;

public class PlanTermsTests
{
    [Theory]
    [InlineData(2026, 9, 28, 2027, 2, 27)]
    [InlineData(2026, 1, 31, 2026, 6, 29)]
    [InlineData(2023, 10, 31, 2024, 3, 30)]
    public void Season_is_calendar_months_not_fixed_thirty_day_blocks(int y, int m, int d, int ey, int em, int ed)
    {
        var terms = new PlanTerms("موسم خمسة أشهر", SubscriptionPlanType.Duration, null, null, 1, 5,
            AcademyServiceKind.Training, TrainingFormat.Season);
        Assert.Equal(new DateOnly(ey, em, ed), terms.EndDate(new(y, m, d)));
    }
    [Fact] public void Day_and_session_plans_keep_existing_boundaries()
    {
        Assert.Equal(new DateOnly(2026, 10, 27), new PlanTerms("شهر", SubscriptionPlanType.Duration, 30, null, 1).EndDate(new(2026, 9, 28)));
        Assert.Null(new PlanTerms("حصص", SubscriptionPlanType.Sessions, null, 8, 1).EndDate(new(2026, 9, 28)));
    }
    [Fact] public void Season_terms_survive_later_product_changes()
    {
        var plan = new SubscriptionPlan { ArabicName = "موسم", Currency = "EGP", DurationMonths = 5, PlanType = SubscriptionPlanType.Duration, ServiceKind = AcademyServiceKind.Training, TrainingFormat = TrainingFormat.Season, ReferenceMonthlyPrice = 600, ChargedMonths = 4, Price = 2400 };
        var renewal = new RenewalRequest { Currency = "EGP", IdempotencyKey = "terms-test" };
        PlanTerms.From(plan).Capture(renewal); plan.DurationMonths = 3; plan.TrainingFormat = TrainingFormat.Group;
        var terms = PlanTerms.From(renewal, plan); Assert.Equal(5, terms.Months); Assert.Equal(TrainingFormat.Season, terms.Format);
        Assert.Equal(480m, plan.Price / terms.Months!.Value); // Analytical equal-service share, not another cash receipt.
    }
}
