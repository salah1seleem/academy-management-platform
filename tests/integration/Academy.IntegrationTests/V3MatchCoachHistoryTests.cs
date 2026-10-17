using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;

public sealed partial class BrandingMatchesTests
{
    [Fact]
    public async Task V3_match_coach_rejects_inactive_wrong_role_foreign_or_wrong_sport_target_and_read_only_actor()
    {
        using var owner=await Staff();var f=await Fixtures(owner);var body=new MatchWrite(f.Tournament,f.Team,"منافس حارس الإسناد",new(2026,10,23),new(17,0),"ملعب",null,null,null,[],true,true);var saved=await Send(owner,"/api/v1/manage/matches/",body);saved.EnsureSuccessStatusCode();var id=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();var a=await AdditionalMatchCoach(owner);using var coachClient=a.Client;
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var ownerId=await db.AcademyMemberships.Where(m=>m.AcademyId==DemoSeed.NogoomAcademyId&&m.Role==AcademyRole.AcademyOwner).Select(m=>m.Id).FirstAsync();var foreign=await db.AcademyMemberships.Where(m=>m.AcademyId==DemoSeed.FutureAcademyId).Select(m=>m.Id).FirstAsync();
        foreach(var target in new[]{ownerId,foreign,Guid.NewGuid()})Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,$"/api/v1/manage/matches/{id}",body with{CoachMembershipId=target,CoachAssignmentVersion=1},true)).StatusCode);
        var targetCoach=await db.AcademyMemberships.SingleAsync(m=>m.Id==a.Id);targetCoach.IsActive=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,$"/api/v1/manage/matches/{id}",body with{CoachMembershipId=a.Id,CoachAssignmentVersion=1},true)).StatusCode);targetCoach.IsActive=true;targetCoach.AllSports=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,$"/api/v1/manage/matches/{id}",body with{CoachMembershipId=a.Id,CoachAssignmentVersion=1},true)).StatusCode);targetCoach.AllSports=true;await db.SaveChangesAsync();
        var tag=Guid.NewGuid().ToString("N");var email=$"match.readonly.{tag}@example.test";var added=await Send(owner,"/api/v1/manage/staff",new StaffWrite("قارئ مباريات اصطناعي",email,"010"+string.Concat(tag.Where(char.IsDigit).Take(8)).PadRight(8,'9'),"Demo-Only-123!",AcademyRole.AcademyAdmin,"مراجعة"));added.EnsureSuccessStatusCode();var adminId=(await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Send(owner,$"/api/v1/manage/staff/{adminId}/access",new StaffAccessWrite(true,[],StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,m=="matches",false,false,false)).ToArray(),AllSports:true,Sports:[]),true)).EnsureSuccessStatusCode();using var reader=await Staff(email);Assert.Equal(HttpStatusCode.OK,(await reader.GetAsync($"/api/v1/manage/matches/{id}/coach-history")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await Send(reader,$"/api/v1/manage/matches/{id}",body with{CoachMembershipId=a.Id,CoachAssignmentVersion=1},true)).StatusCode);Assert.Single(await db.MatchCoachAssignments.Where(x=>x.MatchId==id).ToArrayAsync());
    }
    private async Task<(Guid Id,HttpClient Client)> AdditionalMatchCoach(HttpClient owner)
    {
        var tag=Guid.NewGuid().ToString("N");var email=$"match.coach.{tag}@example.test";var phone="010"+string.Concat(tag.Where(char.IsDigit).Take(8)).PadRight(8,'8');
        var added=await Send(owner,"/api/v1/manage/staff",new StaffWrite("مدرب مباراة اصطناعي",email,phone,"Demo-Only-123!",AcademyRole.Coach,"مدرب"));added.EnsureSuccessStatusCode();var id=(await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var c=factory.CreateClient();await MobileCoachTestSession.SignInAsync(c,factory.Services,email);return(id,c);
    }
    private async Task<HttpClient> MatchCoachClient(Guid id)
    {
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var email=await db.AcademyMemberships.Where(m=>m.Id==id).Select(m=>m.User.Email).SingleAsync();var c=factory.CreateClient();await MobileCoachTestSession.SignInAsync(c,factory.Services,email!);return c;
    }
    [Fact]
    public async Task V3_match_coach_is_frozen_independently_of_team_and_notifies_only_current_explicit_assignment()
    {
        using var owner=await Staff();var f=await Fixtures(owner);await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var team=await db.MatchTeams.SingleAsync(x=>x.Id==f.Team);var originalCoach=team.CoachMembershipId;using var original=await MatchCoachClient(originalCoach);var alternate=await AdditionalMatchCoach(owner);using var alternateClient=alternate.Client;
        var body=new MatchWrite(f.Tournament,f.Team,"منافس إسناد محفوظ",new(2026,10,20),new(18,0),"ملعب اصطناعي",null,null,null,[],true,true);
        var saved=await Send(owner,"/api/v1/manage/matches/",body);saved.EnsureSuccessStatusCode();var id=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var first=await db.MatchCoachAssignments.AsNoTracking().SingleAsync(x=>x.MatchId==id);Assert.Equal(originalCoach,first.CoachMembershipId);Assert.Equal(1,first.Sequence);Assert.Equal("FirstPublication",first.Source);
        (await Send(owner,$"/api/v1/manage/matches/teams/{team.Id}",new TeamWrite(team.Name,team.SportId,team.BranchId,team.AgeCategoryId,alternate.Id,[f.Enrollment],true),true)).EnsureSuccessStatusCode();
        Assert.Contains((await original.GetFromJsonAsync<JsonElement>("/api/v1/my-matches")).EnumerateArray(),m=>m.GetProperty("id").GetGuid()==id);Assert.DoesNotContain((await alternateClient.GetFromJsonAsync<JsonElement>("/api/v1/my-matches")).EnumerateArray(),m=>m.GetProperty("id").GetGuid()==id);
        Assert.Contains((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/matches/?coachMembershipId={originalCoach}")).EnumerateArray(),m=>m.GetProperty("id").GetGuid()==id);
        var originalUser=await db.AcademyMemberships.Where(m=>m.Id==originalCoach).Select(m=>m.UserId).SingleAsync();var otherUser=await db.AcademyMemberships.Where(m=>m.Id==alternate.Id).Select(m=>m.UserId).SingleAsync();var oldCount=await db.AcademyNotices.CountAsync(n=>n.UserId==originalUser);var otherCount=await db.AcademyNotices.CountAsync(n=>n.UserId==otherUser);
        (await Send(owner,$"/api/v1/manage/matches/{id}",body with{Venue="ملعب آخر"},true)).EnsureSuccessStatusCode();Assert.Equal(oldCount+1,await db.AcademyNotices.CountAsync(n=>n.UserId==originalUser));Assert.Equal(otherCount,await db.AcademyNotices.CountAsync(n=>n.UserId==otherUser));
        Assert.Single(await db.MatchCoachAssignments.Where(x=>x.MatchId==id).ToArrayAsync());
    }
    [Fact]
    public async Task V3_match_coach_reassignment_is_version_checked_append_only_replay_safe_and_visible_without_money_or_attendance()
    {
        using var owner=await Staff();using var guardian=await Mobile(AcademyRole.Guardian);var f=await Fixtures(owner);var a=await AdditionalMatchCoach(owner);using var client=a.Client;
        var body=new MatchWrite(f.Tournament,f.Team,"منافس مراجعات",new(2026,10,22),new(17,0),"ملعب",null,null,null,[],true,true);var saved=await Send(owner,"/api/v1/manage/matches/",body);saved.EnsureSuccessStatusCode();var id=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var first=await db.MatchCoachAssignments.AsNoTracking().SingleAsync(x=>x.MatchId==id);var collections=await db.Collections.CountAsync();var payments=await db.TournamentPayments.CountAsync();var attendance=await db.PlayerAttendances.CountAsync();var payroll=await db.PayrollAdjustments.CountAsync();
        Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,$"/api/v1/manage/matches/{id}",body with{CoachMembershipId=a.Id,CoachAssignmentVersion=0},true)).StatusCode);
        var change=body with{CoachMembershipId=a.Id,CoachAssignmentVersion=1,CoachAssignmentNote="مراجعة اصطناعية"};(await Send(owner,$"/api/v1/manage/matches/{id}",change,true)).EnsureSuccessStatusCode();var notices=await db.AcademyNotices.CountAsync();(await Send(owner,$"/api/v1/manage/matches/{id}",change,true)).EnsureSuccessStatusCode();Assert.Equal(notices,await db.AcademyNotices.CountAsync());
        var history=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/matches/{id}/coach-history");Assert.Equal(2,history.GetArrayLength());Assert.Equal(2,history[0].GetProperty("version").GetInt32());Assert.Equal("Reassignment",history[0].GetProperty("source").GetString());Assert.Equal(first.Id,history[1].GetProperty("id").GetGuid());
        var feed=await client.GetFromJsonAsync<JsonElement>("/api/v1/my-matches");Assert.Equal("Confirmed",Assert.Single(feed.EnumerateArray(),m=>m.GetProperty("id").GetGuid()==id).GetProperty("coachAssignmentSource").GetString());var child=Assert.Single((await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/my-matches?playerId={f.Player}")).EnumerateArray(),m=>m.GetProperty("id").GetGuid()==id);Assert.NotNull(child.GetProperty("coachName").GetString());Assert.False(child.TryGetProperty("note",out _));
        Assert.Equal(HttpStatusCode.Forbidden,(await Send(client,$"/api/v1/manage/matches/{id}",body,true)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/api/v1/manage/matches/{id}/coach-history")).StatusCode);
        Assert.Equal(collections,await db.Collections.CountAsync());Assert.Equal(payments,await db.TournamentPayments.CountAsync());Assert.Equal(attendance,await db.PlayerAttendances.CountAsync());Assert.Equal(payroll,await db.PayrollAdjustments.CountAsync());
        var row=await db.MatchCoachAssignments.SingleAsync(x=>x.Id==first.Id);row.Note="تعديل غير مسموح";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
        var denied=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"MatchCoachAssignments\" WHERE \"Id\"={first.Id}"));Assert.Equal("23514",denied.SqlState);Assert.Equal(2,await db.MatchCoachAssignments.CountAsync(x=>x.MatchId==id));
        using var other=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/manage/matches/{id}/coach-history")).StatusCode);
    }
    [Fact]
    public async Task V3_legacy_match_coach_stays_unknown_until_explicit_review_and_copy_never_inherits_old_assignment()
    {
        using var owner=await Staff();var f=await Fixtures(owner);await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var clock=factory.Services.GetRequiredService<Academy.Api.Slice3.ISubscriptionClock>();
        var legacy=new AcademyMatch{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,TeamId=f.Team,TournamentId=f.Tournament,Opponent="مباراة قديمة اصطناعية",MatchDate=clock.Today.AddDays(-1),StartTime=new(17,0),Venue="ملعب",PublishedAtUtc=clock.UtcNow,CreatedAtUtc=clock.UtcNow,UpdatedAtUtc=clock.UtcNow};db.AcademyMatches.Add(legacy);db.MatchParticipants.Add(new(){Id=Guid.NewGuid(),AcademyId=legacy.AcademyId,MatchId=legacy.Id,PlayerId=f.Player,CreatedAtUtc=clock.UtcNow,UpdatedAtUtc=clock.UtcNow});await db.SaveChangesAsync();
        var body=new MatchWrite(f.Tournament,f.Team,legacy.Opponent,legacy.MatchDate,legacy.StartTime,legacy.Venue,null,null,null,[],true,true);(await Send(owner,$"/api/v1/manage/matches/{legacy.Id}",body,true)).EnsureSuccessStatusCode();Assert.False(await db.MatchCoachAssignments.AnyAsync(x=>x.MatchId==legacy.Id));Assert.Equal("LegacyCurrentTeam",(await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/matches/{legacy.Id}")).GetProperty("coachAssignmentSource").GetString());
        var coach=await db.MatchTeams.Where(t=>t.Id==f.Team).Select(t=>t.CoachMembershipId).SingleAsync();var before=DateTimeOffset.UtcNow.AddSeconds(-1);(await Send(owner,$"/api/v1/manage/matches/{legacy.Id}",body with{CoachMembershipId=coach,CoachAssignmentVersion=0},true)).EnsureSuccessStatusCode();var reviewed=await db.MatchCoachAssignments.AsNoTracking().SingleAsync(x=>x.MatchId==legacy.Id);Assert.Equal("LegacyReview",reviewed.Source);Assert.True(reviewed.CreatedAtUtc>=before);Assert.True(reviewed.CreatedAtUtc>legacy.PublishedAtUtc);
        var quote=await DraftPreview(owner,legacy.Id);var copy=await DraftCopy(owner,legacy.Id,new(clock.Today.AddDays(2),new(19,0),quote.GetProperty("quoteHash").GetString()!));copy.EnsureSuccessStatusCode();var id=(await copy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();Assert.False(await db.MatchCoachAssignments.AnyAsync(x=>x.MatchId==id));
        var draft=(await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/matches/{id}"));Assert.Equal(0,draft.GetProperty("coachAssignmentVersion").GetInt32());
    }
}
