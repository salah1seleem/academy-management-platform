using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice4;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Demo;

public static class PayrollStatementDemoSeed
{
    public static Guid EmployeeId=>FootballDemoSeed.Id("payroll-demo/employee/6");
    public static async Task Seed(FoundationDbContext db,IHostEnvironment env,DemoOptions options,TimeProvider time)
    {
        var academy=FootballDemoSeed.AcademyId;
        if(!env.IsEnvironment("Demo")||!options.SeedEnabled||options.SeedProfile!="Football"||!await db.Academies.AnyAsync(a=>a.Id==academy&&a.Slug==FootballDemoSeed.Slug&&a.EnglishName==FootballDemoSeed.Marker))throw new InvalidOperationException("كشف الرواتب الاصطناعي يتطلب Football Demo معلوم الهوية.");
        var month=SalaryHistory.Month(await AttendanceCalendar.TodayAsync(academy,db,time));await using var tx=await db.Database.BeginTransactionAsync();await PayrollEndpoints.Lock(db,academy,EmployeeId,month);
        if(await db.PayrollStatements.AnyAsync(s=>s.EmployeeId==EmployeeId&&s.Month==month)||await PayrollEndpoints.Locked(db,academy,EmployeeId,month))return;
        var e=await db.Employees.SingleOrDefaultAsync(e=>e.Id==EmployeeId&&e.AcademyId==academy);if(e is null)return;
        var t=new TenantMembership(academy,"عرض اصطناعي",AcademyRole.AcademyOwner,FootballDemoSeed.Id("member/owner"));var source=await PayrollStatements.Prepare(e.Id,month,t,db);if(source is null||source.Value.Row.ContractDays==0||source.Value.Row.NetDue<=0)return;
        db.PayrollStatements.Add(PayrollStatements.New(e,month,t,FootballDemoSeed.Id("user/owner"),source.Value,$"demo-statement-{month:yyyy-MM}",source.Value.Hash,time.GetUtcNow(),"كشف اصطناعي معتمد للعرض فقط — لا صرف تلقائي."));await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
