using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice4;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;

// Explicitly opt-in: two synthetic approvals made now, not invented historic cash.
public static class PayrollAccountDemoSeed
{
    public static Guid EmployeeId => FootballDemoSeed.Id("payroll-demo/employee/3");
    public static async Task Seed(FoundationDbContext db, IHostEnvironment env, DemoOptions options, TimeProvider clock)
    {
        var academy = FootballDemoSeed.AcademyId;
        if (!env.IsEnvironment("Demo") || !options.SeedEnabled || options.SeedProfile != "Football" || !await db.Academies.AnyAsync(a => a.Id == academy && a.Slug == FootballDemoSeed.Slug && a.EnglishName == FootballDemoSeed.Marker))
            throw new InvalidOperationException("كشف الحساب الاصطناعي يتطلب Football Demo معلوم الهوية.");
        var month = SalaryHistory.Month(await AttendanceCalendar.TodayAsync(academy, db, clock));
        await using var tx = await db.Database.BeginTransactionAsync(); await PayrollEndpoints.Lock(db, academy, EmployeeId, month);
        var employee = await db.Employees.SingleOrDefaultAsync(e => e.Id == EmployeeId && e.AcademyId == academy); if (employee is null) return;
        var t = new TenantMembership(academy, "عرض اصطناعي", AcademyRole.AcademyOwner, FootballDemoSeed.Id("member/owner"));
        foreach (var period in new[] { month.AddMonths(-1), month })
        {
            if (await db.PayrollStatements.AnyAsync(s => s.EmployeeId == EmployeeId && s.Month == period) || await PayrollEndpoints.Locked(db, academy, EmployeeId, period)) continue;
            var source = await PayrollStatements.Prepare(EmployeeId, period, t, db);
            if (source is null || source.Value.Row.ContractDays == 0 || source.Value.Row.NetDue <= 0) continue;
            db.PayrollStatements.Add(PayrollStatements.New(employee, period, t, FootballDemoSeed.Id("user/owner"), source.Value, $"demo-account-{EmployeeId:N}-{period:yyyy-MM}", source.Value.Hash, clock.GetUtcNow(), "اعتماد اصطناعي الآن لعرض حساب شهرين؛ لا صرف تاريخي أو سلفة مفترضة."));
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
    }
}
