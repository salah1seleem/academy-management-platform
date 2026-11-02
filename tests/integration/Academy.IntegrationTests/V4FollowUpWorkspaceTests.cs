using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Tenancy;
namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact] public async Task V4_followup_summary_drilldowns_match_server_filters_and_pages()
    {
        using var owner=await Staff();using var coach=await Mobile(AcademyRole.Coach);
        var summary=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/owner-followups/summary");
        foreach(var pair in new[]{("open",""),("high","&priority=2"),("overdue","&overdue=true"),("unassigned","&unassigned=true")}){
            var list=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/owner-followups?allDates=true&state=Open&page=1&pageSize=1"+pair.Item2);
            Assert.Equal(summary.GetProperty(pair.Item1).GetInt32(),list.GetProperty("count").GetInt32());Assert.True(list.GetProperty("items").GetArrayLength()<=1);
        }
        Assert.Equal(HttpStatusCode.Forbidden,(await coach.GetAsync("/api/v1/manage/owner-followups/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await owner.GetAsync("/api/v1/manage/owner-followups?page=0")).StatusCode);
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail);var other=await foreign.GetFromJsonAsync<JsonElement>("/api/v1/manage/owner-followups?allDates=true");var ours=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/owner-followups?allDates=true");
        var ids=ours.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("id").GetGuid()).ToArray();Assert.DoesNotContain(other.GetProperty("items").EnumerateArray(),x=>ids.Contains(x.GetProperty("id").GetGuid()));
    }
}
