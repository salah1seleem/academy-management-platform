using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact] public async Task Coverage_followup_uses_real_upcoming_sessions_excludes_cancelled_and_keeps_evidence_after_coverage_changes()
    {
        using var owner=await Staff();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var g=await FollowUpGroup(db);
        var now=DateTimeOffset.UtcNow;var today=await AttendanceCalendar.TodayAsync(g.AcademyId,db,TimeProvider.System);var user=await db.AcademyMemberships.Where(m=>m.AcademyId==g.AcademyId&&m.Role==AcademyRole.AcademyOwner).Select(m=>m.UserId).FirstAsync();
        TrainingSession Session(int days,int hour,TrainingSessionStatus status=TrainingSessionStatus.Scheduled,bool active=true)=>new(){Id=Guid.NewGuid(),AcademyId=g.AcademyId,TrainingGroupId=g.Id,BranchId=g.BranchId,SportId=g.SportId,AgeCategoryId=g.AgeCategoryId,SessionDate=today.AddDays(days),StartTime=new(hour,0),EndTime=new(hour+1,0),Status=status,Source=TrainingSessionSource.Manual,IsActive=active,CreatedByUserId=user,CreatedAtUtc=now,UpdatedAtUtc=now};
        var first=Session(1,10);var second=Session(2,11);db.TrainingSessions.AddRange(first,second,Session(-1,12),Session(3,13,TrainingSessionStatus.Cancelled),Session(4,14,TrainingSessionStatus.Held),Session(5,15,active:false),Session(8,16));await db.SaveChangesAsync();
        async Task<JsonElement[]> Candidates()=> (await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/owner-followups/options")).GetProperty("candidates").EnumerateArray().Where(c=>c.GetProperty("condition").GetString()=="GroupSessionCoverage"&&c.GetProperty("source").GetProperty("groupId").GetGuid()==g.Id).ToArray();
        var candidate=Assert.Single(await Candidates());Assert.Equal(new[]{first.Id,second.Id},candidate.GetProperty("source").GetProperty("coverageGaps").EnumerateArray().Select(s=>s.GetProperty("id").GetGuid()).ToArray());
        var body=new OwnerFollowUpCreate(g.Id,"GroupSessionCoverage",Guid.NewGuid());var created=await Send(owner,"/api/v1/manage/owner-followups/",body);Assert.Equal(HttpStatusCode.Created,created.StatusCode);var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        async Task<JsonElement> Read()=> (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/owner-followups/{id}")).GetProperty("item");
        var initial=await Read();var evidence=initial.GetProperty("evidence").GetRawText();Assert.Equal(2,initial.GetProperty("evidence").GetProperty("source").GetProperty("CoverageGaps").GetArrayLength());
        Assert.True(initial.GetProperty("conditionActive").GetBoolean());var collections=await db.Collections.CountAsync();var attendance=await db.StaffAttendances.CountAsync();
        Assert.Equal(HttpStatusCode.OK,(await Send(owner,$"/api/v1/manage/owner-followups/{id}",new OwnerFollowUpWrite("Closed",initial.GetProperty("version").GetUInt32(),Guid.NewGuid(),ResolutionReason:"متابعة بشرية فقط، لم أغير إسنادًا أو حضورًا"))).StatusCode);
        Assert.True((await Read()).GetProperty("conditionActive").GetBoolean());
        var coach=await db.AcademyMemberships.FirstAsync(m=>m.AcademyId==g.AcademyId&&m.Role==AcademyRole.Coach&&m.IsActive&&m.User.IsActive);var allBranches=coach.AllBranches;var allSports=coach.AllSports;
        try{
            coach.AllBranches=true;coach.AllSports=true;db.StaffGroupAssignments.Add(new(){Id=Guid.NewGuid(),AcademyId=g.AcademyId,TrainingGroupId=g.Id,AcademyMembershipId=coach.Id,CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();
            Assert.Empty(await Candidates());var covered=await Read();Assert.False(covered.GetProperty("conditionActive").GetBoolean());Assert.Equal(evidence,covered.GetProperty("evidence").GetRawText());Assert.Equal("Closed",covered.GetProperty("state").GetString());
            coach.IsActive=false;await db.SaveChangesAsync();Assert.Single(await Candidates());Assert.True((await Read()).GetProperty("conditionActive").GetBoolean());
        }finally{coach.IsActive=true;coach.AllBranches=allBranches;coach.AllSports=allSports;await db.SaveChangesAsync();}
        Assert.Equal(collections,await db.Collections.CountAsync());Assert.Equal(attendance,await db.StaffAttendances.CountAsync());
        Assert.Equal(2,await db.OwnerFollowUpChanges.CountAsync(c=>c.FollowUpId==id));
    }
    [Fact] public async Task Coverage_followup_cannot_be_created_from_forged_condition_scope_or_role()
    {
        using var owner=await Staff();using var other=await Staff(DemoSeed.FutureOwnerEmail);using var admin=await Staff(DemoSeed.AdminEmail);using var coach=await Mobile(AcademyRole.Coach);using var guardian=await Mobile(AcademyRole.Guardian);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var g=await FollowUpGroup(db);var body=new OwnerFollowUpCreate(g.Id,"GroupSessionCoverage",Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,"/api/v1/manage/owner-followups/",body)).StatusCode); // No genuine upcoming gap.
        Assert.Equal(HttpStatusCode.NotFound,(await Send(other,"/api/v1/manage/owner-followups/",body)).StatusCode);
        foreach(var client in new[]{admin,coach,guardian})Assert.Equal(HttpStatusCode.Forbidden,(await Send(client,"/api/v1/manage/owner-followups/",body)).StatusCode);
        Assert.False(await db.OwnerFollowUps.AnyAsync(f=>f.TrainingGroupId==g.Id));
    }
}
