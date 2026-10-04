namespace Academy.Api.Auth;

public sealed class DemoOptions
{
    public const string SectionName = "Demo";
    public bool SeedEnabled { get; init; }
    public string SeedProfile { get; init; } = "Football";
    public bool FixedOtpEnabled { get; init; }
    public string FixedOtp { get; init; } = string.Empty;
    public string StaffPassword { get; init; } = string.Empty;
}
