using Academy.Infrastructure.Tenancy;
namespace Academy.UnitTests;
public sealed class OwnerHomeSettingsTests
{
    [Fact] public void Legacy_home_settings_have_no_target_or_implicit_assignment_policy()
    {
        var old=AcademyRuntimeSettings.Read("{\"Currency\":\"EGP\",\"TimeZone\":\"Africa/Cairo\",\"Features\":{}}");
        Assert.Null(old.OwnerHome);Assert.True(OwnerHomeSettings.Same(null,new()));
    }
    [Theory][InlineData(0)][InlineData(-1)][InlineData(101)]
    public void Assignment_target_must_be_explicit_and_valid(int target)=>Assert.NotEmpty(new OwnerHomeSettings(CoachAssignmentTarget:target).Validate());
    [Theory][InlineData("0")][InlineData("-1")][InlineData("1.001")][InlineData("1000000001")]
    public void Collection_target_never_saves_invalid_currency_precision(string value)
    {
        var target=new OwnerCollectionTarget(null,new(2026,9,1),new(2026,9,30),decimal.Parse(value,System.Globalization.CultureInfo.InvariantCulture));
        Assert.NotEmpty(new OwnerHomeSettings(Targets:[target]).Validate());
    }
    [Fact] public void Targets_are_scope_specific_order_independent_and_reject_duplicate_scopes()
    {
        var first=new OwnerCollectionTarget(null,new(2026,9,1),new(2026,9,30),1000.25m);
        var second=first with{BranchId=Guid.Parse("10000000-0000-0000-0000-000000000001")};
        Assert.Empty(new OwnerHomeSettings(Targets:[first,second]).Validate());
        Assert.True(OwnerHomeSettings.Same(new(Targets:[first,second]),new(Targets:[second,first])));
        Assert.NotEmpty(new OwnerHomeSettings(Targets:[first,first]).Validate());
        Assert.NotEmpty(new OwnerHomeSettings(Targets:[first with{To=new(2026,8,1)}]).Validate());
        Assert.NotEmpty(new OwnerHomeSettings(Targets:[null!]).Validate());
        var settings=AcademyRuntimeSettings.Initial("EGP","Africa/Cairo") with{OwnerHome=new(true,3,[first,second])};
        Assert.True(OwnerHomeSettings.Same(settings.OwnerHome,AcademyRuntimeSettings.Read(settings.Serialize()).OwnerHome));
    }
    [Fact] public void Portrait_choices_are_allowlisted_and_roundtrip_without_losing_targets()
    {
        var portraits=new OwnerHighlightPortraits("coach-02","guardian-02","child-02");
        var settings=AcademyRuntimeSettings.Initial("EGP","Africa/Cairo") with{OwnerHome=new(HighlightPortraits:portraits)};
        Assert.Empty(settings.OwnerHome.Validate());
        Assert.Equal(portraits,AcademyRuntimeSettings.Read(settings.Serialize()).OwnerHome!.HighlightPortraits);
        Assert.NotEmpty(new OwnerHomeSettings(HighlightPortraits:portraits with{Player="https://example.test/photo.jpg"}).Validate());
        Assert.NotEmpty(new OwnerHomeSettings(HighlightPortraits:portraits with{Coach="child-01"}).Validate());
        Assert.False(OwnerHomeSettings.Same(new(HighlightPortraits:portraits),new()));
    }
}
