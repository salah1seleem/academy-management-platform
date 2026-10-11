using Academy.Infrastructure.Tenancy;

namespace Academy.UnitTests;

public sealed class V2ConfigurationTests
{
    [Fact]
    public void New_academy_defaults_do_not_grant_features_cash_or_marketing()
    {
        var settings = AcademyRuntimeSettings.Initial("EGP", "Africa/Cairo");
        Assert.False(settings.CashEnabled); Assert.False(settings.TenantMarketingOptIn);
        Assert.All(settings.Features, f => Assert.False(f.Value));
        Assert.Empty(settings.Validate(new Dictionary<string, bool>(), "EGP", "Africa/Cairo"));
        Assert.Equal(settings.Serialize(), AcademyRuntimeSettings.Read(settings.Serialize()).Serialize());
    }
    [Fact]
    public void License_and_settings_are_independent_and_unknown_features_are_rejected()
    {
        var settings = AcademyRuntimeSettings.Initial("EGP", "Africa/Cairo"); settings.Features["community_ads"] = true;
        Assert.NotEmpty(settings.Validate(new Dictionary<string, bool>(), "EGP", "Africa/Cairo"));
        Assert.Empty(settings.Validate(new Dictionary<string, bool> { ["community_ads"] = true }, "EGP", "Africa/Cairo"));
        settings.Features["injected"] = true;
        Assert.NotEmpty(settings.Validate(new Dictionary<string, bool>(), "EGP", "Africa/Cairo"));
    }
    [Theory]
    [InlineData("USD", "Africa/Cairo", 3)]
    [InlineData("EGP", "invalid-zone", 3)]
    [InlineData("EGP", "UTC", 3)]
    [InlineData("EGP", "Africa/Cairo", 0)]
    [InlineData("EGP", "Africa/Cairo", 31)]
    public void Invalid_settings_cannot_be_activated(string currency, string zone, int absence)
    {
        var settings = AcademyRuntimeSettings.Initial(currency, zone) with { ConsecutiveAbsenceAlert = absence };
        Assert.NotEmpty(settings.Validate(new Dictionary<string, bool>(), "EGP", "Africa/Cairo"));
    }
}
