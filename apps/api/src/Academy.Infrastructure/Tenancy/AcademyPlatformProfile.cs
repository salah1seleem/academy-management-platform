namespace Academy.Infrastructure.Tenancy;

// Commercial terms describe the academy's platform subscription, never player revenue.
public sealed class AcademyPlatformProfile
{
    public Guid AcademyId { get; set; }
    public string OperationalState { get; set; } = "Draft";
    public string Location { get; set; } = "";
    public string ContactPhone { get; set; } = "";
    public string CommercialState { get; set; } = "Unspecified";
    public decimal? PlatformFee { get; set; }
    public string FeeCurrency { get; set; } = "EGP";
    public string BillingPeriod { get; set; } = "Monthly";
    public DateOnly? SubscriptionFrom { get; set; }
    public DateOnly? SubscriptionTo { get; set; }
    public bool PremiumIconEnabled { get; set; }
    public string? BundledIconKey { get; set; }
    public int EditVersion { get; set; } = 1;
}
