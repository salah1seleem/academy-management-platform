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
