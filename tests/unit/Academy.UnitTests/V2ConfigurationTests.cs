using Academy.Infrastructure.Tenancy;

namespace Academy.UnitTests;

public sealed class V2ConfigurationTests
{
    [Theory][InlineData(0)][InlineData(-1)][InlineData(31)]
    public void Invalid_feedback_deadline_cannot_publish(int days)
    {
        var settings = AcademyRuntimeSettings.Initial("EGP", "Africa/Cairo") with { FeedbackFollowUpDays = days };
        Assert.Contains(settings.Validate(new Dictionary<string,bool>(), "EGP", "Africa/Cairo"), e => e.Contains("مهلة"));
    }
    [Fact] public void Legacy_feedback_configuration_has_no_implicit_promise()
    {
        var old = AcademyRuntimeSettings.Read("{\"Currency\":\"EGP\",\"TimeZone\":\"Africa/Cairo\",\"Features\":{}}");
        Assert.Null(old.FeedbackFollowUpDays);
        Assert.Equal(2, AcademyRuntimeSettings.Read((old with { FeedbackFollowUpDays = 2 }).Serialize()).FeedbackFollowUpDays);
    }
    [Theory][InlineData(0)][InlineData(-1)][InlineData(3651)]
    public void Invalid_media_retention_days_do_not_activate(int days)
    {
        var settings=AcademyRuntimeSettings.Initial("EGP","Africa/Cairo") with{MediaRetention=new(days,true)};
        Assert.Contains(settings.Validate(new Dictionary<string,bool>(),"EGP","Africa/Cairo"),e=>e.Contains("الاحتفاظ"));
    }
    [Fact]public void Legacy_media_has_no_automatic_expiry_and_new_retention_requires_an_explicit_setting()
    {
        var old=AcademyRuntimeSettings.Read("{\"Currency\":\"EGP\",\"TimeZone\":\"Africa/Cairo\",\"Features\":{}}");Assert.Null(old.MediaRetention);var policy=new MediaRetentionPolicy(30);Assert.False(policy.DeleteOnExpiry);Assert.Equal(policy,AcademyRuntimeSettings.Read((old with{MediaRetention=policy}).Serialize()).MediaRetention);
    }
    [Fact]
    public void Old_payload_without_help_audience_preserves_defaults_and_new_policy_has_stable_serialization()
    {
        var old = AcademyRuntimeSettings.Read("{\"Currency\":\"EGP\",\"TimeZone\":\"Africa/Cairo\",\"Features\":{}}"); Assert.Null(old.HelpAudiences);
        var first = old with { HelpAudiences = new() { ["evaluations"] = ["Coach", "AcademyOwner"], ["family-renewal"] = [] } };
        var second = old with { HelpAudiences = new() { ["family-renewal"] = [], ["evaluations"] = ["AcademyOwner", "Coach"] } };
        Assert.Equal(first.Serialize(), second.Serialize()); Assert.Empty(AcademyRuntimeSettings.Read(first.Serialize()).HelpAudiences!["family-renewal"]);
    }
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
