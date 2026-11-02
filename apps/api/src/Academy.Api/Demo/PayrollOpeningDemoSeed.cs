using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice4;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;

public static class PayrollOpeningDemoSeed
{
    public static Guid EmployeeId=>FootballDemoSeed.Id("payroll-demo/employee/4");
    public static async Task Seed(FoundationDbContext db,IHostEnvironment env,DemoOptions options,TimeProvider clock)
    {
        var academy=FootballDemoSeed.AcademyId;
        if(!env.IsEnvironment("Demo")||!options.SeedEnabled||options.SeedProfile!="Football"||!await db.Academies.AnyAsync(a=>a.Id==academy&&a.Slug==FootballDemoSeed.Slug&&a.EnglishName==FootballDemoSeed.Marker))throw new InvalidOperationException("الافتتاح الاصطناعي يتطلب Football Demo معلوم الهوية.");
        await using var tx=await db.Database.BeginTransactionAsync();await PayrollEndpoints.Lock(db,academy,EmployeeId,default);
        // Preserve any manual financial source; never invent an opening on a live employee.
        if(await db.PayrollStatements.AnyAsync(s=>s.EmployeeId==EmployeeId)||await db.PayrollSettlements.AnyAsync(s=>s.EmployeeId==EmployeeId)||await db.PayrollAdvances.AnyAsync(a=>a.EmployeeId==EmployeeId))return;
        var e=await db.Employees.SingleOrDefaultAsync(e=>e.AcademyId==academy&&e.Id==EmployeeId&&e.IsActive);if(e is null)return;
        var facts=await PayrollOpening.Facts(e,await AttendanceCalendar.TodayAsync(academy,db,clock),db);
        var t=new TenantMembership(academy,"عرض اصطناعي",AcademyRole.AcademyOwner,FootballDemoSeed.Id("member/owner"));
        var body=new PayrollOpeningApproval(facts.Latest,1200,"سند افتتاح اصطناعي — ليس رصيد عميل",facts.Hash,"سيناريو عرض قديم1200؛ لا صرف تلقائي.");
        db.PayrollStatements.Add(PayrollOpening.New(e,t,FootballDemoSeed.Id("user/owner"),body,facts.Snapshot,facts.Hash,"demo-opening-v1",facts.Hash,clock.GetUtcNow()));
        await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
