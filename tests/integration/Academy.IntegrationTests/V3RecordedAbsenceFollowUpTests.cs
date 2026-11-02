using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    private static async Task<PlayerAttendance[]> AbsenceRecords(FoundationDbContext db, TrainingGroup g)
    {
        var now=DateTimeOffset.UtcNow;var today=await AttendanceCalendar.TodayAsync(g.AcademyId,db,TimeProvider.System);
        var actor=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync();
        var player=new Player{Id=Guid.NewGuid(),AcademyId=g.AcademyId,PlayerCode="ABS-"+Guid.NewGuid().ToString("N")[..12],ArabicName="لاعب متابعة غياب اصطناعي",DateOfBirth=new(2016,1,1),CreatedAtUtc=now,UpdatedAtUtc=now};
        var enrollment=new SportEnrollment{Id=Guid.NewGuid(),AcademyId=g.AcademyId,PlayerId=player.Id,BranchId=g.BranchId,SportId=g.SportId,TrainingGroupId=g.Id,CreatedAtUtc=now,UpdatedAtUtc=now};
        db.Players.Add(player);db.SportEnrollments.Add(enrollment);
        var sessions=Enumerable.Range(0,7).Select(i=>new TrainingSession{Id=Guid.NewGuid(),AcademyId=g.AcademyId,TrainingGroupId=g.Id,BranchId=g.BranchId,SportId=g.SportId,AgeCategoryId=g.AgeCategoryId,
            SessionDate=i<3?today.AddDays(i-3):i==5?today.AddDays(1):today,StartTime=new(0,i==3?0:i),EndTime=new(0,i==3?1:i+1),Status=i==4?TrainingSessionStatus.Cancelled:TrainingSessionStatus.Scheduled,
            IsActive=i!=6,CreatedByUserId=actor,Source=TrainingSessionSource.Manual,CreatedAtUtc=now,UpdatedAtUtc=now}).ToArray();
        var attendance=sessions.Select((s,i)=>new PlayerAttendance{Id=Guid.NewGuid(),AcademyId=g.AcademyId,TrainingSessionId=s.Id,TrainingGroupId=g.Id,SportEnrollmentId=enrollment.Id,Status=i==3?AttendanceStatus.NotRecorded:AttendanceStatus.Absent,
            Notes="ملاحظة طفل خاصة لا تخرج مع ملخص الغياب",RecordedByUserId=actor,CreatedAtUtc=now,UpdatedAtUtc=now}).ToArray();
        db.TrainingSessions.AddRange(sessions);db.PlayerAttendances.AddRange(attendance);await db.SaveChangesAsync();return attendance;
    }

    [Fact] public async Task Recorded_absence_followup_uses_three_eligible_records_and_closure_never_changes_attendance_or_money()
    {
        using var owner=await Staff();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var g=await FollowUpGroup(db);var records=await AbsenceRecords(db,g);
        async Task<JsonElement[]> Candidates()=>(await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/owner-followups/options")).GetProperty("candidates").EnumerateArray().Where(c=>c.GetProperty("condition").GetString()=="GroupRecordedAbsences"&&c.GetProperty("source").GetProperty("groupId").GetGuid()==g.Id).ToArray();
        var summary=Assert.Single(await Candidates()).GetProperty("source").GetProperty("recordedAbsences");Assert.Equal(1,summary.GetProperty("playerCount").GetInt32());Assert.Equal(1,summary.GetProperty("enrollmentCount").GetInt32());Assert.Equal(3,summary.GetProperty("sessionCount").GetInt32());Assert.False(summary.TryGetProperty("evidence",out _));
        foreach(var breaksStreak in new[]{AttendanceStatus.Present,AttendanceStatus.Late,AttendanceStatus.Excused}) {
            records[1].Status=breaksStreak;await db.SaveChangesAsync();Assert.Empty(await Candidates());
        }
        records[1].Status=AttendanceStatus.Absent;await db.SaveChangesAsync();Assert.Single(await Candidates());
        var before=await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28");var notices=await db.AcademyNotices.CountAsync();
        var create=await Send(owner,"/api/v1/manage/owner-followups/",new OwnerFollowUpCreate(g.Id,"GroupRecordedAbsences",Guid.NewGuid()));create.EnsureSuccessStatusCode();var id=(await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        async Task<JsonElement> Read()=>(await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/owner-followups/{id}")).GetProperty("item");
        var item=await Read();var evidence=item.GetProperty("evidence").GetRawText();Assert.Equal(records.Take(3).Select(r=>r.Id).Order(),item.GetProperty("evidence").GetProperty("absenceEvidence").EnumerateArray().Select(r=>r.GetProperty("AttendanceId").GetGuid()).Order());
        (await Send(owner,$"/api/v1/manage/owner-followups/{id}",new OwnerFollowUpWrite("Closed",item.GetProperty("version").GetUInt32(),Guid.NewGuid(),ResolutionReason:"راجعت السجل دون تغيير حضور أو إرسال"))).EnsureSuccessStatusCode();
        Assert.True((await Read()).GetProperty("conditionActive").GetBoolean());Assert.Equal(notices,await db.AcademyNotices.CountAsync());
        foreach(var r in records.Take(3))Assert.Equal(AttendanceStatus.Absent,(await db.PlayerAttendances.AsNoTracking().SingleAsync(a=>a.Id==r.Id)).Status);
        // Actual authorized attendance command on today's unknown register breaks the streak.
        var service=scope.ServiceProvider.GetRequiredService<AttendanceService>();await service.SavePlayersAsync(g.AcademyId,records[3].TrainingSessionId,records[3].RecordedByUserId,[new(records[3].SportEnrollmentId,AttendanceStatus.Present,records[3].Version)]);
        Assert.Empty(await Candidates());item=await Read();Assert.False(item.GetProperty("conditionActive").GetBoolean());Assert.Equal(evidence,item.GetProperty("evidence").GetRawText());Assert.Equal("Closed",item.GetProperty("state").GetString());
        Assert.Equal(before,await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));Assert.Equal(notices,await db.AcademyNotices.CountAsync());
        using var other=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/manage/owner-followups/{id}")).StatusCode);
    }

    [Fact] public async Task Recorded_absence_followup_delegate_has_aggregate_only_not_child_or_attendance_permission()=>await DelegateFixture(async(owner,admin,db,g,member)=>{
        var records=await AbsenceRecords(db,g);var create=await Send(owner,"/api/v1/manage/owner-followups/",new OwnerFollowUpCreate(g.Id,"GroupRecordedAbsences",Guid.NewGuid(),member));create.EnsureSuccessStatusCode();var id=(await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var raw=await admin.GetStringAsync($"/api/v1/my-followups/{id}");Assert.DoesNotContain("لاعب متابعة غياب",raw);Assert.DoesNotContain("ملاحظة طفل",raw);Assert.DoesNotContain("absenceEvidence",raw);
        foreach(var r in records){Assert.DoesNotContain(r.Id.ToString(),raw);Assert.DoesNotContain(r.SportEnrollmentId.ToString(),raw);}
        // Only the first three are child-absence evidence. A future session may
        // independently appear in the existing group coverage metadata.
        foreach(var r in records.Take(3))Assert.DoesNotContain(r.TrainingSessionId.ToString(),raw);
        var detail=JsonDocument.Parse(raw).RootElement;Assert.Equal(1,detail.GetProperty("item").GetProperty("source").GetProperty("recordedAbsences").GetProperty("playerCount").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync($"/api/v1/attendance/sessions/{records[0].TrainingSessionId}/players")).StatusCode);
        (await Send(admin,$"/api/v1/my-followups/{id}",new AssignedFollowUpWrite("Closed",detail.GetProperty("item").GetProperty("version").GetUInt32(),Guid.NewGuid(),ResolutionReason:"إغلاق متابعة فقط لا يمنح قراءة كشف أطفال"))).EnsureSuccessStatusCode();
        var grant=await db.StaffActionGrants.SingleAsync(x=>x.MembershipId==member&&x.Module=="followups");grant.Read=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync($"/api/v1/my-followups/{id}")).StatusCode);
    });
}
