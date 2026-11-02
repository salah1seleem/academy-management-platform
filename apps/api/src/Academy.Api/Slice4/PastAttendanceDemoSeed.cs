using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Operations;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice4;
// Called only by the explicit Demo/LegacyRegression flag. Never approve or alter attendance.
public static class PastAttendanceDemoSeed
{
    public static Guid RequestId(DateOnly day)=>new(SHA256.HashData(Encoding.UTF8.GetBytes("academity-past-correction:"+day.ToString("yyyy-MM-dd")))[..16]);
    public static async Task Seed(FoundationDbContext db,TimeProvider clock)
    {
        var academy=DemoSeed.NogoomAcademyId;await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:"+academy},0))");var settings=await db.AcademyConfigurationVersions.Where(c=>c.AcademyId==academy&&c.State==ConfigurationState.Active).Select(c=>c.Payload).SingleOrDefaultAsync();if(settings is null||!AcademyRuntimeSettings.Read(settings).Features.GetValueOrDefault("training"))return;
        var day=await AttendanceCalendar.TodayAsync(academy,db,clock);var id=RequestId(day);if(await db.AttendanceCorrections.AnyAsync(c=>c.Id==id)){await tx.CommitAsync();return;}
        var session=await db.TrainingSessions.AsNoTracking().SingleOrDefaultAsync(s=>s.Id==GroupPlacementDemoSeed.Id(131)&&s.AcademyId==academy&&s.Status!=TrainingSessionStatus.Cancelled&&s.TrainingGroup.IsActive&&s.SessionDate<day);if(session is null)return;
        var member=await db.AcademyMemberships.Where(m=>m.AcademyId==academy&&m.User.Email==DemoSeed.OwnerEmail&&m.User.IsActive&&m.IsActive&&m.Role==AcademyRole.AcademyOwner).SingleOrDefaultAsync();var enrollment=GroupPlacementDemoSeed.Id(111);if(member is null||!await db.SportEnrollments.AnyAsync(e=>e.Id==enrollment&&e.Player.IsActive&&FoundationDbContext.IsSessionAssigned(academy,e.Id,session.Id)))return;
        var before=await db.PlayerAttendances.AsNoTracking().SingleOrDefaultAsync(a=>a.TrainingSessionId==session.Id&&a.SportEnrollmentId==enrollment);var status=before?.Status??AttendanceStatus.NotRecorded;var payload=JsonSerializer.Serialize(new[]{new AttendanceCorrectionSnapshot(enrollment,status,status==AttendanceStatus.Absent?AttendanceStatus.Excused:AttendanceStatus.Absent,before?.Version??0)});var now=clock.GetUtcNow();
        db.AttendanceCorrections.Add(new(){Id=id,AcademyId=academy,TrainingSessionId=session.Id,TrainingSessionVersion=session.Version,TrainingGroupId=session.TrainingGroupId,BranchId=session.BranchId,SessionDate=session.SessionDate,Kind=AttendanceCorrectionKind.Players,Payload=payload,Reason="طلب تصحيح حضور اصطناعي — ينتظر مراجعة المالك، بلا تغيير تلقائي",RequestedByUserId=member.UserId,RequestedByMembershipId=member.Id,RequestKey="demo-past-correction:"+day.ToString("yyyy-MM-dd"),RequestHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),ExpiresAtUtc=now.AddDays(7),CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
