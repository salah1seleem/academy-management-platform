using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice4;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Demo;

public static class ContractHistoryDemoSeed
{
    public static Guid EmployeeId => FootballDemoSeed.Id("payroll-demo/employee/2");
    public static async Task Seed(FoundationDbContext db,IHostEnvironment env,DemoOptions options,TimeProvider clock)
    {
        var academy=FootballDemoSeed.AcademyId;
        if(!env.IsEnvironment("Demo")||!options.SeedEnabled||options.SeedProfile!="Football"||!await db.Academies.AnyAsync(a=>a.Id==academy&&a.Slug==FootballDemoSeed.Slug&&a.EnglishName==FootballDemoSeed.Marker))throw new InvalidOperationException("تاريخ العقود التجريبي يتطلب Football Demo معلوم الهوية.");
        await using var tx=await db.Database.BeginTransactionAsync();await PayrollEndpoints.Lock(db,academy,EmployeeId,default);
        if(await db.ContractVersions.AnyAsync(v=>v.EmployeeId==EmployeeId))return;
        var e=await db.Employees.SingleOrDefaultAsync(e=>e.Id==EmployeeId&&e.AcademyId==academy);if(e is null)return;
        var month=SalaryHistory.Month(await AttendanceCalendar.TodayAsync(academy,db,clock));var next=month.AddMonths(1);
        if(await db.PayrollStatements.AnyAsync(s=>s.EmployeeId==EmployeeId&&s.Month>=next)||await db.PayrollSettlements.AnyAsync(s=>s.EmployeeId==EmployeeId&&s.Month>=next))return;
        var t=new TenantMembership(academy,"عرض اصطناعي",AcademyRole.AcademyOwner,FootballDemoSeed.Id("member/owner"));var actor=FootballDemoSeed.Id("user/owner");var now=clock.GetUtcNow();
        var baseline=ContractHistory.New(e,t,actor,"LegacyBaseline",month,e.ContractStart,e.ContractEnd,1,null,"demo-contract-baseline","DEMO-CONTRACT-V1",now,"مرجع اصطناعي من شهر المراجعة، بلا تاريخ مفبرك.");db.ContractVersions.Add(baseline);await db.SaveChangesAsync();
        db.ContractVersions.Add(ContractHistory.New(e,t,actor,"Change",next,e.ContractStart,next.AddDays(14),2,baseline.Id,"demo-contract-future","DEMO-CONTRACT-V1",now,"مثال عقد اصطناعي ينتهي يوم15 من الشهر التالي؛ لا تغيير أو صرف للشهر الحالي."));await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
