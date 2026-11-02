using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact] public async Task Feedback_incident_uses_occurrence_not_current_coach_preserves_snapshot_and_isolates_family_and_academy()
    {
        using var owner=await Staff(); using var foreign=await Staff(DemoSeed.FutureOwnerEmail); using var coach=await Mobile(AcademyRole.Coach);
        var family=await ExclusiveMediaFamily(); using var parent=family.Client;
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var source=await db.SportEnrollments.AsNoTracking().FirstAsync(e=>e.AcademyId==DemoSeed.NogoomAcademyId && e.IsActive && e.TrainingGroupId!=null && db.StaffGroupAssignments.Any(a=>a.TrainingGroupId==e.TrainingGroupId && a.IsActive && a.AcademyMembership.Role==AcademyRole.Coach));
        var group=await db.TrainingGroups.AsNoTracking().SingleAsync(g=>g.Id==source.TrainingGroupId); var e=await FreshMediaChild(db,source,family.Guardian);
        var options=await parent.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{e.PlayerId}/feedback-options"); var today=DateOnly.Parse(options.GetProperty("asOfDate").GetString()!); var day=today.AddDays(-1); e.GroupAssignedFrom=day; await db.SaveChangesAsync();
        var actor=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync(); var original=await db.StaffGroupAssignments.Where(a=>a.TrainingGroupId==group.Id && a.IsActive && a.AcademyMembership.Role==AcademyRole.Coach).Select(a=>a.AcademyMembershipId).FirstAsync();
        var user=new ApplicationUser{Id=Guid.NewGuid(),UserName=$"incident-{Guid.NewGuid():N}@example.test",DisplayName="مدرب واقعة اصطناعي"}; db.Users.Add(user);
        var replacement=new AcademyMembership{Id=Guid.NewGuid(),AcademyId=group.AcademyId,UserId=user.Id,Role=AcademyRole.Coach}; db.AcademyMemberships.Add(replacement); await db.SaveChangesAsync();
        var minute=Enumerable.Range(0,60).First(m=>!db.TrainingSessions.Any(s=>s.TrainingGroupId==group.Id && s.SessionDate==day && s.StartTime==new TimeOnly(4,m)));
        var session=new TrainingSession{Id=Guid.NewGuid(),AcademyId=group.AcademyId,TrainingGroupId=group.Id,BranchId=group.BranchId,SportId=group.SportId,AgeCategoryId=group.AgeCategoryId,SessionDate=day,StartTime=new(4,minute),EndTime=new(5,minute),CreatedByUserId=actor}; db.TrainingSessions.Add(session); await db.SaveChangesAsync();
        var path=$"/api/v1/guardian/children/{e.PlayerId}/feedback-incidents?incidentOn={day:yyyy-MM-dd}";
        var before=await parent.GetFromJsonAsync<JsonElement>(path); Assert.Empty(before.GetProperty("sessions").EnumerateArray().Single(s=>s.GetProperty("id").GetGuid()==session.Id).GetProperty("coaches").EnumerateArray()); // Never use the currently assigned coach as invented history.
        var attendance=new StaffAttendance{Id=Guid.NewGuid(),AcademyId=group.AcademyId,TrainingSessionId=session.Id,TrainingGroupId=group.Id,AcademyMembershipId=original,Status=AttendanceStatus.Absent,RecordedByUserId=actor};db.StaffAttendances.Add(attendance);await db.SaveChangesAsync();
        Assert.Empty((await parent.GetFromJsonAsync<JsonElement>(path)).GetProperty("sessions").EnumerateArray().Single(s=>s.GetProperty("id").GetGuid()==session.Id).GetProperty("coaches").EnumerateArray());
        attendance.Status=AttendanceStatus.Present;await db.SaveChangesAsync();Assert.Equal("OccurrenceAttendance",Assert.Single((await parent.GetFromJsonAsync<JsonElement>(path)).GetProperty("sessions").EnumerateArray().Single(s=>s.GetProperty("id").GetGuid()==session.Id).GetProperty("coaches").EnumerateArray()).GetProperty("source").GetString());
        attendance.Status=AttendanceStatus.Absent;await db.SaveChangesAsync();
        var decision=new SessionCoachOverride{Id=Guid.NewGuid(),AcademyId=group.AcademyId,TrainingSessionId=session.Id,TrainingGroupId=group.Id,BranchId=group.BranchId,SportId=group.SportId,OriginalCoachMembershipId=original,ActualCoachMembershipId=replacement.Id,Sequence=1,SessionDate=day,StartTime=session.StartTime,EndTime=session.EndTime,ActorUserId=actor,Reason="قرار واقعة اصطناعي للاختبار فقط",QualificationEvidence="مراجعة اصطناعية",RequestKey=Guid.NewGuid().ToString(),RequestHash="synthetic",Snapshot="{}"}; db.SessionCoachOverrides.Add(decision); await db.SaveChangesAsync();
        var read=await parent.GetAsync(path); Assert.True(read.Headers.CacheControl!.NoStore); var context=await read.Content.ReadFromJsonAsync<JsonElement>(); var item=context.GetProperty("sessions").EnumerateArray().Single(s=>s.GetProperty("id").GetGuid()==session.Id); var suggested=Assert.Single(item.GetProperty("coaches").EnumerateArray()); Assert.Equal(replacement.Id,suggested.GetProperty("id").GetGuid()); Assert.Equal("OccurrenceDecision",suggested.GetProperty("source").GetString()); Assert.False(suggested.TryGetProperty("evidenceId",out _));
        var submit=$"/api/v1/guardian/children/{e.PlayerId}/feedback"; var body=new FeedbackWrite(group.Id,replacement.Id,"Complaint",null,"ملاحظة اصطناعية مرتبطة بسياق الحصة","Coach","Routine",day,session.Id); var key=Guid.NewGuid().ToString();
        var money=await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"); var made=await Send(parent,submit,body,key:key); Assert.True(made.StatusCode==HttpStatusCode.Created,await made.Content.ReadAsStringAsync()); var id=(await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var sourcePath=$"/api/v1/manage/feedback/{id}"; var saved=await owner.GetFromJsonAsync<JsonElement>(sourcePath); var evidence=saved.GetProperty("incidentSessionEvidence").GetString()!; Assert.Contains("OccurrenceDecision",evidence);
        user.DisplayName="اسم المدرب الحالي تغير بعد البلاغ"; await db.SaveChangesAsync(); Assert.Equal(evidence,(await owner.GetFromJsonAsync<JsonElement>(sourcePath)).GetProperty("incidentSessionEvidence").GetString()); (await Send(parent,submit,body,key:key)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Send(parent,submit,body with{IncidentSessionId=null},key:key)).StatusCode);
        foreach(var bad in new[]{body with{CoachMembershipId=original},body with{IncidentOn=day.AddDays(-1)},body with{IncidentSessionId=Guid.NewGuid()},body with{TrainingGroupId=Guid.NewGuid()}}) Assert.Equal(HttpStatusCode.NotFound,(await Send(parent,submit,bad,key:Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(parent,submit,body with{Kind="Rating",Score=5},key:Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await parent.GetAsync(path.Replace(day.ToString("yyyy-MM-dd"),today.AddDays(1).ToString("yyyy-MM-dd")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await coach.GetAsync(path)).StatusCode); Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync(sourcePath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await parent.GetAsync($"/api/v1/guardian/children/{Guid.NewGuid()}/feedback-incidents?incidentOn={day:yyyy-MM-dd}")).StatusCode);
        var foreignChild=await db.SportEnrollments.Where(x=>x.AcademyId==DemoSeed.FutureAcademyId).Select(x=>x.PlayerId).FirstAsync();Assert.Equal(HttpStatusCode.NotFound,(await parent.GetAsync($"/api/v1/guardian/children/{foreignChild}/feedback-incidents?incidentOn={day:yyyy-MM-dd}&academyId={DemoSeed.FutureAcademyId}")).StatusCode);
        var tamper=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.GuardianFeedbacks.Where(f=>f.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(f=>f.IncidentSessionEvidence,"{}"))); Assert.Equal("23514",tamper.SqlState);
        e.IsActive=false; await db.SaveChangesAsync(); Assert.DoesNotContain((await parent.GetFromJsonAsync<JsonElement>(path)).GetProperty("sessions").EnumerateArray(),s=>s.GetProperty("id").GetGuid()==session.Id);
        Assert.Equal(HttpStatusCode.NotFound,(await Send(parent,submit,body with{Message="محاولة جديدة بعد سحب التسجيل"},key:Guid.NewGuid().ToString())).StatusCode); Assert.Equal(money,await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
    }
}
