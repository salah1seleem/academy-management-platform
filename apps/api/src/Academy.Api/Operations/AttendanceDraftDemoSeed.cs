using System.Security.Cryptography;
using System.Text;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
// Invoked only by the explicit Demo/LegacyRegression flag. No attendance, reset or payment.
public static class AttendanceDraftDemoSeed
{
    public static Guid SessionId(DateOnly day) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"academy-demo-attendance-draft:{day:yyyy-MM-dd}"))[..16]);
    public static async Task Seed(FoundationDbContext db, TimeProvider clock)
    {
        var academy=DemoSeed.NogoomAcademyId;
        var group=await db.TrainingGroups.SingleOrDefaultAsync(g=>g.AcademyId==academy&&g.Id==GroupPlacementDemoSeed.Id(130)&&g.IsActive);
        if(group is null)return;
        var day=await AttendanceCalendar.TodayAsync(academy,db,clock);var id=SessionId(day);
        await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"demo-attendance-draft:"+academy},0))");
        if(await db.TrainingSessions.AnyAsync(s=>s.AcademyId==academy&&(s.Id==id||s.TrainingGroupId==group.Id&&s.SessionDate==day&&s.StartTime==new TimeOnly(6,45))))return;
        var actor=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync();var now=clock.GetUtcNow();
        db.TrainingSessions.Add(new(){Id=id,AcademyId=academy,TrainingGroupId=group.Id,BranchId=group.BranchId,SportId=group.SportId,AgeCategoryId=group.AgeCategoryId,SessionDate=day,StartTime=new(6,45),EndTime=new(7,15),Status=TrainingSessionStatus.Scheduled,Source=TrainingSessionSource.Manual,Notes="تجربة مسودة الحضور وانقطاع الاتصال — بيانات اصطناعية",CreatedByUserId=actor,CreatedAtUtc=now,UpdatedAtUtc=now});
        await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
