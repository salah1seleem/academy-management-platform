using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;

public static class StaffWorkPeriodDemoSeed
{
    public static Guid AttendanceId=>FootballDemoSeed.Id("staff-period-demo/day/1");
    public static Guid ClaimId=>FootballDemoSeed.Id("staff-period-demo/second-shift-claim/1");
    public static async Task SeedClaim(FoundationDbContext db,IHostEnvironment env,DemoOptions options,TimeProvider clock)
    {
        var academy=FootballDemoSeed.AcademyId;
        if(!env.IsEnvironment("Demo")||!options.SeedEnabled||options.SeedProfile!="Football"||!await db.Academies.AnyAsync(a=>a.Id==academy&&a.Slug==FootballDemoSeed.Slug&&a.EnglishName==FootballDemoSeed.Marker))throw new InvalidOperationException("طلب الفترة الاصطناعي يتطلب Football Demo معلوم الهوية.");
        await using var tx=await db.Database.BeginTransactionAsync();
        var row=await db.StaffDailyAttendances.AsNoTracking().SingleOrDefaultAsync(a=>a.Id==AttendanceId&&a.AcademyId==academy);if(row is null)return;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"staff-open:{academy}:{row.MembershipId}"},0))");
        row=await db.StaffDailyAttendances.AsNoTracking().SingleAsync(a=>a.Id==AttendanceId);
        if(row.CheckOut is null||await db.StaffAttendanceExceptions.AnyAsync(r=>r.Id==ClaimId))return;
        var e=await db.Employees.AsNoTracking().SingleOrDefaultAsync(e=>e.MembershipId==row.MembershipId&&e.AcademyId==academy&&e.IsActive);if(e is null)return;
        var zoneId=await db.Academies.Where(a=>a.Id==academy).Select(a=>a.TimeZone).SingleAsync();
        if(!StaffWorkPeriods.TryUtc(row.WorkDate,new(18,0),TimeZoneInfo.FindSystemTimeZoneById(zoneId),out var claimed)||clock.GetUtcNow()<claimed.AddMinutes(5))return;
        var user=await db.AcademyMemberships.Where(m=>m.Id==row.MembershipId&&m.IsActive).Select(m=>m.UserId).SingleAsync();
        db.StaffAttendanceExceptions.Add(new(){Id=ClaimId,AcademyId=academy,EmployeeId=e.Id,MembershipId=row.MembershipId,BranchId=e.BranchId,RequestedByUserId=user,Operation="CheckIn",WorkDate=row.WorkDate,ClaimedAtUtc=claimed,Reason="تعذر الاتصال وقت الوردية الإضافية — مثال اصطناعي",RequestKey="demo-second-shift-claim-v1",RequestHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("demo-second-shift-claim-v1"))),TargetAttendanceId=row.Id,TargetAttendanceVersion=row.Version,RequestTimeZoneId=zoneId,CreatedAtUtc=claimed.AddMinutes(5),UpdatedAtUtc=claimed.AddMinutes(5)});
        await db.SaveChangesAsync();await tx.CommitAsync();
    }
    public static async Task Seed(FoundationDbContext db,IHostEnvironment env,DemoOptions options,TimeProvider clock)
    {
        var academy=FootballDemoSeed.AcademyId;
        if(!env.IsEnvironment("Demo")||!options.SeedEnabled||options.SeedProfile!="Football"||!await db.Academies.AnyAsync(a=>a.Id==academy&&a.Slug==FootballDemoSeed.Slug&&a.EnglishName==FootballDemoSeed.Marker))throw new InvalidOperationException("الفترات الاصطناعية تتطلب Football Demo معلوم الهوية.");
        var employee=await db.Employees.AsNoTracking().SingleOrDefaultAsync(e=>e.Id==FootballDemoSeed.Id("payroll-demo/employee/2")&&e.AcademyId==academy&&e.IsActive);if(employee?.MembershipId is not Guid member)return;
        var day=new DateOnly(2026,10,14);var zoneId=await db.Academies.Where(a=>a.Id==academy).Select(a=>a.TimeZone).SingleAsync();var zone=TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        if(!StaffWorkPeriods.TryUtc(day,new(16,0),zone,out var end)||clock.GetUtcNow()<end.AddMinutes(5))return;var received=end.AddMinutes(5);
        await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"staff-open:{academy}:{member}"},0))");await PayrollEndpoints.Lock(db,academy,employee.Id,day);
        if(await db.StaffDailyAttendances.AnyAsync(a=>a.AcademyId==academy&&a.MembershipId==member&&(a.Id==AttendanceId||a.WorkDate==day||a.Status=="Present"&&a.CheckOut==null))||await PayrollEndpoints.Finalized(db,academy,employee.Id,day))return;
        if(await StaffAttendanceIntervals.Overlaps(db,academy,member,null,day,new(8,0),day,new(10,0))||await StaffAttendanceIntervals.Overlaps(db,academy,member,null,day,new(14,0),day,new(16,0)))return;
        var owner=await db.AcademyMemberships.AsNoTracking().SingleAsync(m=>m.AcademyId==academy&&m.Role==AcademyRole.AcademyOwner&&m.Id==FootballDemoSeed.Id("member/owner"));
        var row=new StaffDailyAttendance{Id=AttendanceId,AcademyId=academy,MembershipId=member,WorkDate=day,Status="Present",CheckIn=new(8,0),CheckOut=new(16,0),CheckOutDate=day,RecordedByUserId=owner.UserId,CreatedAtUtc=received,UpdatedAtUtc=received};db.StaffDailyAttendances.Add(row);await db.SaveChangesAsync();
        var periods=new List<StaffWorkPeriod>();for(var i=0;i<2;i++){var startTime=i==0?new TimeOnly(8,0):new TimeOnly(14,0);var endTime=i==0?new TimeOnly(10,0):new TimeOnly(16,0);StaffWorkPeriods.TryUtc(day,startTime,zone,out var startUtc);StaffWorkPeriods.TryUtc(day,endTime,zone,out var endUtc);var p=new StaffWorkPeriod{Id=FootballDemoSeed.Id($"staff-period-demo/period/{i}"),AcademyId=academy,AttendanceId=row.Id,MembershipId=member,StartDate=day,StartTime=startTime,EndDate=day,EndTime=endTime,StartUtc=startUtc,EndUtc=endUtc,TimeZoneId=zoneId,TrustSource="AdminDeclared",RecordedByUserId=owner.UserId,CreatedAtUtc=received,UpdatedAtUtc=received};periods.Add(p);db.StaffWorkPeriods.Add(p);}await db.SaveChangesAsync();
        db.StaffAttendanceChanges.Add(new(){Id=FootballDemoSeed.Id("staff-period-demo/evidence/1"),AcademyId=academy,AttendanceId=row.Id,MembershipId=member,ActorMembershipId=owner.Id,ActorUserId=owner.UserId,WorkDate=day,BeforeVersion=0,AfterVersion=row.Version,BeforeSnapshot="null",AfterSnapshot=StaffWorkPeriods.Snapshot(row,periods,"فترتا عمل اصطناعيتان للعرض فقط"),RequestKey="staff-period-demo-v1",RequestHash=System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("staff-period-demo-v1"))),CreatedAtUtc=received,UpdatedAtUtc=received});await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
