using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice4;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;

public static class SalaryHistoryDemoSeed
{
    public static Guid EmployeeId=>FootballDemoSeed.Id("payroll-demo/employee/0");
    public static async Task Seed(FoundationDbContext db,IHostEnvironment env,DemoOptions options,TimeProvider time)
    {
        var academy=FootballDemoSeed.AcademyId;
        if(!env.IsEnvironment("Demo")||!options.SeedEnabled||options.SeedProfile!="Football"||!await db.Academies.AnyAsync(a=>a.Id==academy&&a.Slug==FootballDemoSeed.Slug&&a.EnglishName==FootballDemoSeed.Marker))throw new InvalidOperationException("تاريخ الرواتب التجريبي يتطلب Football Demo معلوم الهوية.");
        await using var tx=await db.Database.BeginTransactionAsync();await PayrollEndpoints.Lock(db,academy,EmployeeId,default);
        if(await db.SalaryVersions.AnyAsync(v=>v.EmployeeId==EmployeeId))return;
        var e=await db.Employees.SingleOrDefaultAsync(e=>e.Id==EmployeeId&&e.AcademyId==academy);if(e is null)return;
        var month=SalaryHistory.Month(await AttendanceCalendar.TodayAsync(academy,db,time));var t=new TenantMembership(academy,"عرض اصطناعي",AcademyRole.AcademyOwner,FootballDemoSeed.Id("member/owner"));var actor=FootballDemoSeed.Id("user/owner");var now=time.GetUtcNow();
        var baseline=SalaryHistory.New(e,t,actor,"LegacyBaseline",month,e.MonthlySalary,1,null,"demo-salary-baseline","DEMO-SALARY-V1",now,"مثال اصطناعي: المرجع يبدأ شهر المراجعة فقط.");db.SalaryVersions.Add(baseline);await db.SaveChangesAsync();
        db.SalaryVersions.Add(SalaryHistory.New(e,t,actor,"Change",month.AddMonths(1),e.MonthlySalary+500,2,baseline.Id,"demo-salary-future","DEMO-SALARY-V1",now,"زيادة مستقبلية اصطناعية500، بلا صرف أو تغيير الماضي."));await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
