using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice4;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class BrandingMatchesTests
{
    [Fact]
    public async Task Match_agenda_uses_real_academy_time_and_same_audience_and_refreshes_changes_without_money()
    {
        using var owner=await Staff();using var guardian=await Mobile(AcademyRole.Guardian);var f=await Fixtures(owner);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var time=factory.Services.GetRequiredService<TimeProvider>();var today=await AttendanceCalendar.TodayAsync(DemoSeed.NogoomAcademyId,db,time);
        var coachId=await db.MatchTeams.Where(t=>t.Id==f.Team).Select(t=>t.CoachMembershipId).SingleAsync();using var coach=await MatchCoachClient(coachId);
        var body=new MatchWrite(f.Tournament,f.Team,"منافس تقويم قريب",today.AddDays(2),new(18,0),"ملعب التقويم",null,null,null,[],true,true);
        async Task<Guid> Add(MatchWrite value){var r=await Send(owner,"/api/v1/manage/matches/",value);r.EnsureSuccessStatusCode();return(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();}
        var future=await Add(body);var past=await Add(body with{Opponent="ماضٍ في التاريخ الفعلي",MatchDate=today.AddDays(-1)});var draft=await Add(body with{Opponent="مسودة تقويم",Publish=false});var cancelled=await Add(body with{Opponent="تقويم ملغى",IsActive=false});
        var collections=await db.Collections.CountAsync();var attendance=await db.PlayerAttendances.CountAsync();
        foreach(var client in new[]{guardian,coach})
        {
            var list=await client.GetFromJsonAsync<JsonElement>($"/api/v1/my-matches?upcomingOnly=true&playerId={f.Player}");
            Assert.Contains(list.EnumerateArray(),m=>m.GetProperty("id").GetGuid()==future);
            foreach(var excluded in new[]{past,draft,cancelled})Assert.DoesNotContain(list.EnumerateArray(),m=>m.GetProperty("id").GetGuid()==excluded);
            Assert.All(list.EnumerateArray(),m=>{Assert.Equal("Upcoming",m.GetProperty("scheduleWindow").GetString());Assert.True(m.GetProperty("isUpcoming").GetBoolean());});
        }
        var updated=body with{MatchDate=today.AddDays(3),Venue="ملعب بعد التعديل"};(await Send(owner,$"/api/v1/manage/matches/{future}",updated,true)).EnsureSuccessStatusCode();
        var refreshed=await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/my-matches?upcomingOnly=true&playerId={f.Player}");var row=Assert.Single(refreshed.EnumerateArray(),m=>m.GetProperty("id").GetGuid()==future);Assert.Equal(updated.MatchDate.ToString("yyyy-MM-dd"),row.GetProperty("matchDate").GetString());Assert.Equal(updated.Venue,row.GetProperty("venue").GetString());
        (await Send(owner,$"/api/v1/manage/matches/{future}",updated with{IsActive=false},true)).EnsureSuccessStatusCode();
        Assert.DoesNotContain((await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/my-matches?upcomingOnly=true&playerId={f.Player}")).EnumerateArray(),m=>m.GetProperty("id").GetGuid()==future);
        Assert.Contains((await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/my-matches?playerId={f.Player}")).EnumerateArray(),m=>m.GetProperty("id").GetGuid()==future&&m.GetProperty("status").GetString()=="Cancelled");
        using var other=await Staff(DemoSeed.FutureOwnerEmail);Assert.Empty((await other.GetFromJsonAsync<JsonElement>($"/api/v1/my-matches?upcomingOnly=true&playerId={f.Player}")).EnumerateArray());
        Assert.Equal(collections,await db.Collections.CountAsync());Assert.Equal(attendance,await db.PlayerAttendances.CountAsync());
    }
}
