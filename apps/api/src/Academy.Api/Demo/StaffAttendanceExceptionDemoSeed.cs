using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;

public static class StaffAttendanceExceptionDemoSeed
{
    public static Guid RequestId => FootballDemoSeed.Id("staff-exception-demo/request/1");
    public static Guid OvernightRequestId => FootballDemoSeed.Id("staff-overnight-demo/request/1");
    public static async Task SeedOvernight(FoundationDbContext db, IHostEnvironment env, DemoOptions options, TimeProvider clock)
    {
        var academy = FootballDemoSeed.AcademyId;
        if (!env.IsEnvironment("Demo") || !options.SeedEnabled || options.SeedProfile != "Football" || !await db.Academies.AnyAsync(a => a.Id == academy && a.Slug == FootballDemoSeed.Slug && a.EnglishName == FootballDemoSeed.Marker)) throw new InvalidOperationException("بيانات الوردية الاصطناعية تتطلب Football Demo معلوم الهوية.");
        var received = DateTimeOffset.Parse("2026-10-15T08:05:00Z"); if (clock.GetUtcNow() < received) return;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"staff-overnight-demo:{academy}"}, 0))");
        if (await db.StaffAttendanceExceptions.AnyAsync(r => r.Id == OvernightRequestId && r.AcademyId == academy)) return;
        var e = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == FootballDemoSeed.Id("payroll-demo/employee/1") && e.AcademyId == academy && e.IsActive);
        if (e?.MembershipId is not Guid member) return;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"staff-open:{academy}:{member}"}, 0))");
        var attendanceId = FootballDemoSeed.Id("staff-overnight-demo/attendance/1");
        if (await db.StaffDailyAttendances.AnyAsync(a => a.AcademyId == academy && a.MembershipId == member && (a.Id == attendanceId || a.WorkDate == new DateOnly(2026,10,14) || a.Status == "Present" && a.CheckIn != null && a.CheckOut == null))) return; // Never replace a reviewer's record.
        var user = await db.AcademyMemberships.Where(m => m.Id == member && m.IsActive).Select(m => m.UserId).SingleAsync();
        var owner = await db.AcademyMemberships.Where(m => m.AcademyId == academy && m.Role == Academy.Infrastructure.Tenancy.AcademyRole.AcademyOwner && m.IsActive).Select(m => m.UserId).FirstAsync();
        var start = DateTimeOffset.Parse("2026-10-14T19:00:00Z");
        var source = new StaffDailyAttendance { Id=attendanceId, AcademyId=academy, MembershipId=member, WorkDate=new(2026,10,14), Status="Present", CheckIn=new(22,0), RecordedByUserId=owner, CreatedAtUtc=start, UpdatedAtUtc=start };
        db.StaffDailyAttendances.Add(source); await db.SaveChangesAsync();
        var claimed = DateTimeOffset.Parse("2026-10-15T00:00:00Z"); var reason = "انصراف وردية عرض اصطناعية بعد منتصف الليل؛ يحتاج مراجعة مستقلة.";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { BranchId=e.BranchId, Operation="CheckOut", claimed, reason, TargetAttendanceId=attendanceId }))));
        db.StaffAttendanceExceptions.Add(new() { Id=OvernightRequestId, AcademyId=academy, EmployeeId=e.Id, MembershipId=member, BranchId=e.BranchId, RequestedByUserId=user, Operation="CheckOut", WorkDate=source.WorkDate, ClaimedAtUtc=claimed, Reason=reason, RequestKey="demo-staff-overnight-v1", RequestHash=hash, TargetAttendanceId=source.Id, TargetAttendanceVersion=source.Version, CreatedAtUtc=received, UpdatedAtUtc=received });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public static async Task Seed(FoundationDbContext db, IHostEnvironment env, DemoOptions options, TimeProvider clock)
    {
        var academy = FootballDemoSeed.AcademyId;
        if (!env.IsEnvironment("Demo") || !options.SeedEnabled || options.SeedProfile != "Football" || !await db.Academies.AnyAsync(a => a.Id == academy && a.Slug == FootballDemoSeed.Slug && a.EnglishName == FootballDemoSeed.Marker)) throw new InvalidOperationException("بيانات إثبات الحضور الاصطناعية تتطلب Football Demo معلوم الهوية.");
        var received = DateTimeOffset.Parse("2026-10-15T08:05:00Z");
        if (clock.GetUtcNow() < received) return;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"staff-exception-demo:{academy}"}, 0))");
        if (await db.StaffAttendanceExceptions.AnyAsync(r => r.Id == RequestId && r.AcademyId == academy)) return; // Preserve a reviewed example.
        var e = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == FootballDemoSeed.Id("payroll-demo/employee/1") && e.AcademyId == academy && e.IsActive);
        if (e?.MembershipId is not Guid member) return;
        var user = await db.AcademyMemberships.Where(m => m.Id == member && m.IsActive).Select(m => m.UserId).SingleAsync();
        var claimed = DateTimeOffset.Parse("2026-10-15T08:00:00Z"); var reason = "طلب عرض اصطناعي: تعذر الحصول على موقع دقيق داخل الفرع.";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { BranchId = e.BranchId, Operation = "CheckIn", claimed, reason }))));
        db.StaffAttendanceExceptions.Add(new() { Id = RequestId, AcademyId = academy, EmployeeId = e.Id, MembershipId = member, BranchId = e.BranchId, RequestedByUserId = user, Operation = "CheckIn", WorkDate = new(2026, 10, 15), ClaimedAtUtc = claimed, Reason = reason, RequestKey = "demo-staff-exception-v1", RequestHash = hash, CreatedAtUtc = received, UpdatedAtUtc = received });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
