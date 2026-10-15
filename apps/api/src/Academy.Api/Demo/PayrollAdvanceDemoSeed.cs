using Academy.Api.Finance;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Demo;

public static class PayrollAdvanceDemoSeed
{
    public static Guid AdvanceId=>FootballDemoSeed.Id("payroll-advance-demo/source");
    public static async Task Seed(FoundationDbContext db,IHostEnvironment env,DemoOptions options,TimeProvider clock)
    {
        // This also verifies the exact reserved Demo marker and rejects Production.
        await PayrollAccountDemoSeed.Seed(db,env,options,clock);
        var academy=FootballDemoSeed.AcademyId;var employee=PayrollAccountDemoSeed.EmployeeId;
        var month=SalaryHistory.Month(await AttendanceCalendar.TodayAsync(academy,db,clock));await using var tx=await db.Database.BeginTransactionAsync();await PayrollEndpoints.Lock(db,academy,employee,month);
        if(await db.PayrollAdvances.AnyAsync(a=>a.AcademyId==academy&&a.Id==AdvanceId))return;
        var e=await db.Employees.SingleOrDefaultAsync(e=>e.AcademyId==academy&&e.Id==employee&&e.IsActive);var s=await db.PayrollStatements.SingleOrDefaultAsync(s=>s.AcademyId==academy&&s.EmployeeId==employee&&s.Month==month);if(e is null||s is null)return;
        var payments=await db.PayrollDisbursements.Where(p=>p.StatementId==s.Id).ToArrayAsync();var paid=payments.Sum(p=>p.Amount);var amount=Math.Min(500,s.NetDue-paid);if(amount<=0)return;
        var now=clock.GetUtcNow();var actor=FootballDemoSeed.Id("user/owner");var member=FootballDemoSeed.Id("member/owner");var hash=StatementComparisonRules.Hash(new{employee,month,amount,profile="advance-demo-v1"});
        db.PayrollAdvances.Add(new PayrollAdvance {Id=AdvanceId,AcademyId=academy,EmployeeId=employee,BranchId=e.BranchId,Currency=e.Currency,Amount=amount,ActorMembershipId=member,ActorUserId=actor,Reference="سلفة اصطناعية للعرض فقط — لا تحويل حقيقي",RequestKey="demo-advance-source",RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();
        db.PayrollDisbursements.Add(new PayrollDisbursement {Id=FootballDemoSeed.Id("payroll-advance-demo/application"),AcademyId=academy,EmployeeId=employee,BranchId=s.BranchId,Currency=s.Currency,StatementId=s.Id,Sequence=payments.Length+1,Amount=amount,PaidBefore=paid,RemainingAfter=s.NetDue-paid-amount,AdvanceId=AdvanceId,AdvanceAppliedBefore=0,AdvanceRemainingAfter=0,ActorMembershipId=member,ActorUserId=actor,RequestKey="demo-advance-application",RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
