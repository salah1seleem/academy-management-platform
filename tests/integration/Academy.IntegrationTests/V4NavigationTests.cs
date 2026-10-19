using System.Net.Http.Json;
using System.Text.Json;
namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact] public async Task V4_navigation_facility_flags_are_derived_from_trusted_academy_configuration()
    {
        using var owner=await Staff();
        var result=await owner.GetFromJsonAsync<JsonElement>("/api/v1/academy/experience");
        var nav=result.GetProperty("navigation");
        Assert.Contains(nav.GetProperty("swimmingEnabled").ValueKind,new[]{JsonValueKind.True,JsonValueKind.False});
        Assert.Contains(nav.GetProperty("generalFacilitiesEnabled").ValueKind,new[]{JsonValueKind.True,JsonValueKind.False});
        Assert.Contains(nav.GetProperty("cashEnabled").ValueKind,new[]{JsonValueKind.True,JsonValueKind.False});
        Assert.False(nav.TryGetProperty("academyId",out _));
    }
}
