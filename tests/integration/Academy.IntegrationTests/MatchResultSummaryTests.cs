using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact]
    public async Task Disabled_competitions_block_all_four_roles_and_preserve_saved_history()
    {
        using var owner=await Staff();using var admin=await Staff(Academy.Api.Auth.DemoSeed.AdminEmail);
        using var coach=await Mobile(Academy.Infrastructure.Tenancy.AcademyRole.Coach);using var guardian=await Mobile(Academy.Infrastructure.Tenancy.AcademyRole.Guardian);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var academy=Academy.Api.Auth.DemoSeed.NogoomAcademyId;var entitlement=await db.AcademyFeatureEntitlements.SingleAsync(e=>e.AcademyId==academy&&e.Feature=="matches");
        var before=entitlement.Enabled;var count=await db.AcademyMatches.CountAsync(m=>m.AcademyId==academy);
        try {
            entitlement.Enabled=false;await db.SaveChangesAsync();
            foreach(var client in new[]{owner,admin,coach,guardian})Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/v1/my-match-access")).StatusCode);
            foreach(var client in new[]{owner,admin})Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/v1/manage/matches/options")).StatusCode);
            foreach(var client in new[]{coach,guardian})Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/v1/my-matches")).StatusCode);
            Assert.Equal(count,await db.AcademyMatches.CountAsync(m=>m.AcademyId==academy));
        } finally {entitlement.Enabled=before;await db.SaveChangesAsync();}
    }
    [Fact]
    public async Task Goal_results_are_atomic_roster_scoped_draft_silent_and_summary_identity_survives_corrections()
    {
        using var owner=await Staff();using var guardian=await Mobile(Academy.Infrastructure.Tenancy.AcademyRole.Guardian);
        var pushToken=new string('a',64);
        Assert.Equal(HttpStatusCode.Forbidden,(await Send(owner,"/api/v1/mobile-push/device",new PushDeviceWrite("APNs",pushToken),true)).StatusCode);
        (await Send(guardian,"/api/v1/mobile-push/device",new PushDeviceWrite("APNs",pushToken),true)).EnsureSuccessStatusCode();
        var f=await Fixtures(owner);var request=Guid.NewGuid();
        var body=new MatchWrite(f.Tournament,f.Team,"منافس أهداف خيالي",new(2026,9,25),new(17,0),"ملعب خيالي",null,4,2,[],true,true,
            GoalSlots:[new(1,"Player",f.Player),new(2,"Player",f.Player),new(3,"Unknown"),new(4,"OwnGoal")],ResultFinalized:false,RequestId:request);
        var created=await Send(owner,"/api/v1/manage/matches/",body);created.EnsureSuccessStatusCode();
        var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();var path=$"/api/v1/manage/matches/{id}";
        (await Send(owner,"/api/v1/manage/matches/",body)).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Single(await db.AcademyMatches.Where(m=>m.CreationRequestId==request).ToArrayAsync());
        Assert.Empty(await db.AcademyNotices.Where(n=>n.MatchId==id&&n.EventKey=="MatchGoals").ToArrayAsync());
        Assert.Empty(await db.MobilePushDeliveries.Where(d=>db.AcademyNotices.Any(n=>n.Id==d.NoticeId&&n.MatchId==id)).ToArrayAsync());
        var registered=await db.MobilePushDevices.SingleAsync(d=>d.TokenHash==Academy.Api.Mobile.MobileAuthenticationHandler.Hash(pushToken));Assert.NotEqual(pushToken,registered.ProtectedToken);
        Assert.Equal(4,await db.MatchGoalSlots.CountAsync(g=>g.MatchId==id));
        Assert.Equal("ResultDraft",(await owner.GetFromJsonAsync<JsonElement>(path)).GetProperty("status").GetString());
        var invalid=body with{GoalSlots=[new(1,"Player",Guid.NewGuid()),new(2,"Unknown"),new(3,"Unknown"),new(4,"Unknown")],ResultFinalized=true};
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,path,invalid,true)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,path,body with{OurScore=-1},true)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,path,body with{OurScore=2},true)).StatusCode);
        var final=body with{ResultFinalized=true,ResultVersion=1};(await Send(owner,path,final,true)).EnsureSuccessStatusCode();
        var notices=await db.AcademyNotices.AsNoTracking().Where(n=>n.MatchId==id&&n.EventKey=="MatchGoals").ToArrayAsync();Assert.NotEmpty(notices);
        Assert.All(notices,n=>{Assert.Equal(f.Player,n.SubjectPlayerId);Assert.Contains("هدفين",n.Message);Assert.Contains("4",n.Message);});
        var noticeIds=notices.Select(n=>n.Id).Order().ToArray();
        var pending=await db.MobilePushDeliveries.Where(d=>noticeIds.Contains(d.NoticeId)).Select(d=>d.Id).ToArrayAsync();Assert.Single(pending);
        Assert.Equal(HttpStatusCode.OK,(await guardian.GetAsync($"/api/v1/mobile-push/notices/{notices.Single(n=>n.UserId==registered.UserId).Id}")).StatusCode);
        var linkIds=await db.GuardianPlayerLinks.Where(l=>l.PlayerId==f.Player&&l.Guardian.UserId==registered.UserId&&l.IsActive).Select(l=>l.Id).ToArrayAsync();
        try {
            await db.GuardianPlayerLinks.Where(l=>linkIds.Contains(l.Id)).ExecuteUpdateAsync(s=>s.SetProperty(l=>l.IsActive,false));
            Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync($"/api/v1/mobile-push/notices/{notices.Single(n=>n.UserId==registered.UserId).Id}")).StatusCode);
            await using var deliveryScope=factory.Services.CreateAsyncScope();
            await MobilePushWorker.ProcessOne(deliveryScope.ServiceProvider,CancellationToken.None,pending.Single());
            Assert.Equal("Suppressed",await db.MobilePushDeliveries.Where(d=>d.Id==pending.Single()).Select(d=>d.State).SingleAsync());
        } finally {
            await db.GuardianPlayerLinks.Where(l=>linkIds.Contains(l.Id)).ExecuteUpdateAsync(s=>s.SetProperty(l=>l.IsActive,true));
        }
        (await Send(owner,path,final with{ResultVersion=2},true)).EnsureSuccessStatusCode();
        Assert.Equal(noticeIds,await db.AcademyNotices.Where(n=>n.MatchId==id&&n.EventKey=="MatchGoals").OrderBy(n=>n.Id).Select(n=>n.Id).ToArrayAsync());
        var corrected=body with{OurScore=1,GoalSlots=[new(1,"Player",f.Player)],ResultFinalized=true,ResultVersion=2};
        (await Send(owner,path,corrected,true)).EnsureSuccessStatusCode();
        Assert.Single(await db.MatchGoalSlots.Where(g=>g.MatchId==id).ToArrayAsync());
        Assert.All(await db.AcademyNotices.AsNoTracking().Where(n=>n.MatchId==id&&n.EventKey=="MatchGoals").ToArrayAsync(),n=>Assert.Contains("تصحيح نتيجة",n.Message));
        (await Send(owner,path,corrected with{OurScore=0,GoalSlots=[],ResultVersion=3},true)).EnsureSuccessStatusCode();
        Assert.Empty(await db.MatchGoalSlots.Where(g=>g.MatchId==id).ToArrayAsync());
        Assert.Equal(noticeIds,await db.AcademyNotices.Where(n=>n.MatchId==id&&n.EventKey=="MatchGoals").OrderBy(n=>n.Id).Select(n=>n.Id).ToArrayAsync());
        Assert.Equal(4,await db.MatchResultChanges.CountAsync(c=>c.MatchId==id));
        Assert.Equal(pending,await db.MobilePushDeliveries.Where(d=>noticeIds.Contains(d.NoticeId)).Select(d=>d.Id).ToArrayAsync());
        (await Send(guardian,"/api/v1/mobile-push/device",new PushDeviceWrite("APNs",new string('b',64)),true)).EnsureSuccessStatusCode();
        using(var remove=new HttpRequestMessage(HttpMethod.Delete,"/api/v1/mobile-push/device")){(await guardian.SendAsync(remove)).EnsureSuccessStatusCode();}
        Assert.All(await db.MobilePushDevices.AsNoTracking().Where(d=>d.MobileSessionId==registered.MobileSessionId).ToArrayAsync(),d=>Assert.False(d.IsActive));
        using var other=await Staff(Academy.Api.Auth.DemoSeed.FutureOwnerEmail);
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync(path)).StatusCode);
    }
    [Fact]
    public async Task Coach_team_grant_allows_creation_and_results_but_not_structure_or_unassigned_team_roster()
    {
        using var owner=await Staff();var f=await Fixtures(owner);var other=await Fixtures(owner);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var team=await db.MatchTeams.AsNoTracking().SingleAsync(t=>t.Id==f.Team);using var coach=await MatchCoachClient(team.CoachMembershipId);
        // No team has enabled match management yet: the action itself is
        // forbidden. Once granted below, foreign team identities remain404.
        Assert.Equal(HttpStatusCode.Forbidden,(await coach.GetAsync($"/api/v1/manage/matches/teams/{f.Team}/roster")).StatusCode);
        (await Send(owner,$"/api/v1/manage/matches/teams/{team.Id}",new TeamWrite(team.Name,team.SportId,team.BranchId,team.AgeCategoryId,team.CoachMembershipId,[f.Enrollment],true,true),true)).EnsureSuccessStatusCode();
        var body=new MatchWrite(f.Tournament,f.Team,"منافس صلاحية خيالي",new(2026,9,25),new(17,0),"ملعب خيالي",null,1,0,[],true,true,GoalSlots:[new(1,"Player",f.Player)],ResultFinalized:true);
        var created=await Send(coach,"/api/v1/manage/matches/",body);created.EnsureSuccessStatusCode();
        var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Send(coach,$"/api/v1/manage/matches/{id}",body with{OurScore=0,GoalSlots=[],ResultVersion=1},true)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound,(await coach.GetAsync($"/api/v1/manage/matches/teams/{other.Team}/roster")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Send(coach,"/api/v1/manage/matches/tournaments",new TournamentWrite("غير مخول",team.SportId,0,"EGP",true))).StatusCode);
        Assert.Single(await db.MatchTeamPermissionAudits.Where(a=>a.TeamId==f.Team).ToArrayAsync());
    }
}
