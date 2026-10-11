namespace Academy.Infrastructure.Subscriptions;

// A forecast is neither a receivable nor cash. Eligibility uses captured service terms.
public static class RenewalOpportunity
{
    public static bool Expired(SubscriptionPeriod period, SubscriptionPlan legacyPlan, DateOnly today)
    {
        var terms = PlanTerms.From(period, legacyPlan);
        return period.IsActive && period.Status != SubscriptionPeriodStatus.Cancelled
            && period.Status != SubscriptionPeriodStatus.Frozen && period.StartDate <= today
            && terms.ServiceKind == AcademyServiceKind.Training
            && (period.EndDate.HasValue && period.EndDate < today
                || terms.Type != SubscriptionPlanType.Duration && period.RemainingSessions.HasValue && period.RemainingSessions <= 0);
    }
}
