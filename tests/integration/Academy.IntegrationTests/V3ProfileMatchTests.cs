using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Api.Slice2;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class BrandingMatchesTests
{
    [Fact]
    public async Task Profile_matches_keep_published_player_audience_after_team_removal_and_cancel_without_financial_side_effects()
    {
        using var owner=await Staff();using var guardian=await Mobile(AcademyRole.Guardian);var f=await Fixtures(owner);
        var body=new MatchWrite(f.Tournament,f.Team,"منافس ملف اللاعب",new(2026,10,21),new(18,30),"ملعب اصطناعي",null,null,null,[f.Player],true,true);
        var saved=await Send(owner,"/api/v1/manage/matches/",body);saved.EnsureSuccessStatusCode();var id=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var collections=await db.Collections.CountAsync();var payments=await db.TournamentPayments.CountAsync();var member=await db.MatchTeamPlayers.SingleAsync(p=>p.TeamId==f.Team);member.IsActive=false;await db.SaveChangesAsync();
        var response=await owner.GetAsync($"/api/v1/manage/matches/?playerId={f.Player}");Assert.True(response.Headers.CacheControl!.NoStore);var profile=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Contains(profile.EnumerateArray(),m=>m.GetProperty("id").GetGuid()==id);
        var mine=await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/my-matches?playerId={f.Player}");var row=Assert.Single(mine.EnumerateArray(),m=>m.GetProperty("id").GetGuid()==id);Assert.Equal("Bench",row.GetProperty("participation").GetString());Assert.Equal("Published",row.GetProperty("status").GetString());
        (await Send(owner,$"/api/v1/manage/matches/{id}",body with{IsActive=false},true)).EnsureSuccessStatusCode();
        var cancelled=await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/my-matches?playerId={f.Player}");row=Assert.Single(cancelled.EnumerateArray(),m=>m.GetProperty("id").GetGuid()==id);Assert.Equal("Cancelled",row.GetProperty("status").GetString());Assert.False(row.GetProperty("isUpcoming").GetBoolean());
        Assert.Equal(collections,await db.Collections.CountAsync());Assert.Equal(payments,await db.TournamentPayments.CountAsync());Assert.Single(await db.MatchParticipants.Where(p=>p.MatchId==id&&p.PlayerId==f.Player&&p.IsActive).ToArrayAsync());
        using var other=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/manage/matches/?playerId={f.Player}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync($"/api/v1/manage/matches/?playerId={Slice2DemoSeed.AcademyBPlayerId}")).StatusCode);
    }

    [Fact]
    public async Task Profile_player_and_coach_filters_intersect_existing_role_authority_and_never_expose_drafts()
    {
        using var owner=await Staff();using var guardian=await Mobile(AcademyRole.Guardian);using var coach=await Mobile(AcademyRole.Coach);var f=await Fixtures(owner);
        var body=new MatchWrite(f.Tournament,f.Team,"منافس نطاق الملف",new(2026,10,22),new(17,0),"ملعب",null,null,null,[],true,true);
        var saved=await Send(owner,"/api/v1/manage/matches/",body);saved.EnsureSuccessStatusCode();var id=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var draft=await Send(owner,"/api/v1/manage/matches/",body with{Publish=false,Opponent="مسودة ليست للأسرة"});draft.EnsureSuccessStatusCode();var draftId=(await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var team=await db.MatchTeams.AsNoTracking().SingleAsync(x=>x.Id==f.Team);
        var managed=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/matches/?coachMembershipId={team.CoachMembershipId}&playerId={f.Player}");Assert.Contains(managed.EnumerateArray(),x=>x.GetProperty("id").GetGuid()==id);Assert.DoesNotContain(managed.EnumerateArray(),x=>x.GetProperty("id").GetGuid()==draftId);
        foreach(var client in new[]{guardian,coach})
        {
            var mine=await client.GetFromJsonAsync<JsonElement>($"/api/v1/my-matches?playerId={f.Player}");Assert.Contains(mine.EnumerateArray(),x=>x.GetProperty("id").GetGuid()==id);Assert.DoesNotContain(mine.EnumerateArray(),x=>x.GetProperty("id").GetGuid()==draftId);
            Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/v1/my-matches?playerId={Slice2DemoSeed.AcademyBPlayerId}")).EnumerateArray());
            Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/api/v1/manage/matches/?coachMembershipId={team.CoachMembershipId}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync($"/api/v1/manage/matches/?coachMembershipId={Guid.NewGuid()}")).StatusCode);
        var foreignUser=await db.AcademyMemberships.Where(m=>m.AcademyId==DemoSeed.FutureAcademyId&&m.Role==AcademyRole.AcademyOwner).Select(m=>m.UserId).FirstAsync();
        var foreign=new AcademyMembership{Id=Guid.NewGuid(),AcademyId=DemoSeed.FutureAcademyId,UserId=foreignUser,Role=AcademyRole.Coach,CreatedAtUtc=DateTimeOffset.UtcNow,UpdatedAtUtc=DateTimeOffset.UtcNow};db.AcademyMemberships.Add(foreign);await db.SaveChangesAsync();
        try{Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync($"/api/v1/manage/matches/?coachMembershipId={foreign.Id}")).StatusCode);}
        finally{db.AcademyMemberships.Remove(foreign);await db.SaveChangesAsync();}
    }

    [Fact]
    public async Task Profile_match_read_needs_its_own_grant_and_current_branch_and_sport_scope()
    {
        using var owner=await Staff();var f=await Fixtures(owner);var saved=await Send(owner,"/api/v1/manage/matches/",new MatchWrite(f.Tournament,f.Team,"منافس صلاحية الملف",new(2026,10,20),new(17,0),"ملعب",null,null,null,[],true,true));saved.EnsureSuccessStatusCode();var id=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var tag=Guid.NewGuid().ToString("N");var email=$"profile.matches.{tag}@example.test";var phone="010"+string.Concat(tag.Where(char.IsDigit).Take(8)).PadRight(8,'7');var added=await Send(owner,"/api/v1/manage/staff",new StaffWrite("إداري ملفات اصطناعي",email,phone,"Demo-Only-123!",AcademyRole.AcademyAdmin,"مراجعة"));added.EnsureSuccessStatusCode();var membership=(await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var team=await db.MatchTeams.AsNoTracking().SingleAsync(x=>x.Id==f.Team);var path=$"/api/v1/manage/staff/{membership}/access";
        var access=new StaffAccessWrite(false,[team.BranchId],StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,m=="players",false,false,false)).ToArray(),AllSports:false,Sports:[team.SportId]);(await Send(owner,path,access,true)).EnsureSuccessStatusCode();using var admin=await Staff(email);var url=$"/api/v1/manage/matches/?playerId={f.Player}";Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync(url)).StatusCode);
        access=access with{Actions=StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,m is "players" or "matches",false,false,false)).ToArray()};(await Send(owner,path,access,true)).EnsureSuccessStatusCode();Assert.Contains((await admin.GetFromJsonAsync<JsonElement>(url)).EnumerateArray(),m=>m.GetProperty("id").GetGuid()==id);
        var otherBranch=await db.Branches.Where(b=>b.AcademyId==team.AcademyId&&b.Id!=team.BranchId).Select(b=>b.Id).FirstAsync();(await Send(owner,path,access with{Branches=[otherBranch]},true)).EnsureSuccessStatusCode();var denied=await admin.GetAsync(url);Assert.DoesNotContain(id.ToString(),await denied.Content.ReadAsStringAsync());
        var otherSport=await db.Sports.Where(s=>s.AcademyId==team.AcademyId&&s.Id!=team.SportId).Select(s=>s.Id).FirstAsync();(await Send(owner,path,access with{Sports=[otherSport]},true)).EnsureSuccessStatusCode();denied=await admin.GetAsync(url);Assert.DoesNotContain(id.ToString(),await denied.Content.ReadAsStringAsync());
    }
}
