using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Demo;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
namespace Academy.IntegrationTests;

public sealed partial class V2StaffScopeTests
{
    [Fact]
    public async Task V3_statement_sql_cannot_remove_a_frozen_deduction_by_reassigning_the_attendance_member()
    {
        var f=await AgreementFixture();using var owner=f.Owner;var other=await AgreementFixture();using var otherOwner=other.Owner;var month=new DateOnly(f.Today.Year,f.Today.Month,1);
        (await Write(owner,$"/api/v1/manage/staff-attendance/{f.Coach}",new {date=f.Today,status="Absent",approvedDeduction=50,reason="خصم حضور اصطناعي محفوظ"})).EnsureSuccessStatusCode();(await StatementApprove(owner,f.Employee,month,await StatementView(owner,f.Employee,month))).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StaffDailyAttendances\" SET \"MembershipId\"={other.Coach},\"ApprovedDeduction\"=0 WHERE \"MembershipId\"={f.Coach} AND \"WorkDate\"={f.Today}"))).SqlState);
        Assert.Equal(50,await db.StaffDailyAttendances.Where(a=>a.MembershipId==f.Coach&&a.WorkDate==f.Today).Select(a=>a.ApprovedDeduction).SingleAsync());Assert.Equal(2950,(await PrivatePayroll(owner,f.Employee,month)).GetProperty("netDue").GetDecimal());
    }
    [Fact]
    public async Task V3_statement_approved_private_sources_hold_refund_review_without_rewriting_the_approved_month()
    {
        var f=await PrivateFixture();using var owner=f.Owner;var path=(await PrivateBindUnits(owner,f.Coach,f.Document,f.Sessions[0])).Single();await PrivatePayment(owner,f.Document,f.Enrollment,f.Plan,f.Actor,2000);(await PrivatePost(owner,path,await PrivatePreview(owner,path))).EnsureSuccessStatusCode();var month=new DateOnly(f.Today.Year,f.Today.Month,1);
        (await StatementApprove(owner,f.Employee,month,await StatementView(owner,f.Employee,month))).EnsureSuccessStatusCode();
        (await PrivateFinancialWrite(owner,$"/api/v1/finance/receivables/{f.Document}/movements",new {kind="CreditNote",amount=400,reason="تصحيح اصطناعي بعد اعتماد كشف"})).EnsureSuccessStatusCode();var q=await PrivatePreview(owner,path);Assert.Equal("Review",q.GetProperty("action").GetString());(await PrivatePost(owner,path,q)).EnsureSuccessStatusCode();Assert.Equal("ReviewPending",(await PrivatePreview(owner,path)).GetProperty("action").GetString());Assert.Equal(3100,(await PrivatePayroll(owner,f.Employee,month)).GetProperty("netDue").GetDecimal());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(1,await db.PayrollAdjustments.CountAsync(a=>a.EmployeeId==f.Employee));Assert.False(await db.PayrollDisbursements.AnyAsync(p=>p.EmployeeId==f.Employee));
    }
    [Fact]
    public async Task V3_statement_demo_seed_is_opt_in_repeatable_unpaid_and_forbidden_in_production()
    {
        await using var demo=factory.WithWebHostBuilder(b=>b.UseSetting("Demo:SeedProfile","Football"));using var client=demo.CreateClient();await using var scope=demo.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var env=scope.ServiceProvider.GetRequiredService<IHostEnvironment>();var options=scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;var time=scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var beforePayments=await db.PayrollDisbursements.CountAsync(p=>p.EmployeeId==PayrollStatementDemoSeed.EmployeeId);await PayrollStatementDemoSeed.Seed(db,env,options,time);var statement=await db.PayrollStatements.AsNoTracking().SingleAsync(s=>s.EmployeeId==PayrollStatementDemoSeed.EmployeeId);await PayrollStatementDemoSeed.Seed(db,env,options,time);Assert.Equal(statement.Id,(await db.PayrollStatements.SingleAsync(s=>s.EmployeeId==PayrollStatementDemoSeed.EmployeeId)).Id);Assert.Equal(beforePayments,await db.PayrollDisbursements.CountAsync(p=>p.EmployeeId==PayrollStatementDemoSeed.EmployeeId));var old=env.EnvironmentName;try{env.EnvironmentName="Production";await Assert.ThrowsAsync<InvalidOperationException>(()=>PayrollStatementDemoSeed.Seed(db,env,options,time));}finally{env.EnvironmentName=old;}
    }
    private static string StatementRoot(Guid e)=>$"/api/v1/manage/payroll/{e}/statement";
    private static Task<JsonElement> StatementView(HttpClient c,Guid e,DateOnly month)=>c.GetFromJsonAsync<JsonElement>($"{StatementRoot(e)}?month={month:yyyy-MM-dd}");
    private static async Task<HttpResponseMessage> StatementCommand(HttpClient c,string path,object body,string? key)
    {
        var token=(await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();using var r=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};r.Headers.Add("X-CSRF-TOKEN",token);r.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());return await c.SendAsync(r);
    }
    private static Task<HttpResponseMessage> StatementApprove(HttpClient c,Guid e,DateOnly month,JsonElement view,string? key=null)=>StatementCommand(c,StatementRoot(e)+"/approve",new StatementApproval(month,view.GetProperty("quoteHash").GetString()!,"اعتماد اصطناعي داخلي"),key);
    private static Task<HttpResponseMessage> StatementPay(HttpClient c,Guid e,DateOnly month,decimal amount,decimal paid,decimal remaining,string? key=null)=>StatementCommand(c,StatementRoot(e)+"/disbursements",new StatementDisbursement(month,amount,paid,remaining,"مرجع صرف اصطناعي"),key);

    [Fact]
    public async Task V3_statement_approval_and_partial_payments_are_once_only_immutable_and_owner_coach_reports_match()
    {
        var f=await AgreementFixture();using var owner=f.Owner;using var coach=factory.CreateClient();await MobileCoachTestSession.SignInAsync(coach,factory.Services,f.Email);var month=new DateOnly(f.Today.Year,f.Today.Month,1);
        var reason=await Write(owner,"/api/v1/manage/payroll/reasons",new {name="حصة إضافية كشف "+Guid.NewGuid().ToString("N")},false);reason.EnsureSuccessStatusCode();
        var request=await PrivateFinancialWrite(coach,"/api/v1/staff/payroll/requests",new AdditionWrite((await reason.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()));request.EnsureSuccessStatusCode();
        var claim=(await request.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();(await Write(owner,$"/api/v1/manage/payroll/requests/{claim}/review",new AdditionReview(true,f.Today,null,600),false)).EnsureSuccessStatusCode();
        var preview=await StatementView(owner,f.Employee,month);Assert.True(preview.GetProperty("canApprove").GetBoolean());Assert.Equal(3600,preview.GetProperty("row").GetProperty("netDue").GetDecimal());Assert.False(preview.GetProperty("openingBalanceVerified").GetBoolean());
        var key=Guid.NewGuid().ToString();var approvals=await Task.WhenAll(StatementApprove(owner,f.Employee,month,preview,key),StatementApprove(owner,f.Employee,month,preview,key));Assert.Single(approvals,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(approvals,r=>r.StatusCode==HttpStatusCode.OK);
        var partial=Guid.NewGuid().ToString();var payouts=await Task.WhenAll(StatementPay(owner,f.Employee,month,2500,0,3600,partial),StatementPay(owner,f.Employee,month,2500,0,3600,partial));Assert.Single(payouts,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(payouts,r=>r.StatusCode==HttpStatusCode.OK);
        var row=await PrivatePayroll(owner,f.Employee,month);Assert.Equal(3600,row.GetProperty("netDue").GetDecimal());Assert.Equal(2500,row.GetProperty("paidAmount").GetDecimal());Assert.Equal(1100,row.GetProperty("remainingAmount").GetDecimal());Assert.False(row.GetProperty("isPaid").GetBoolean());
        var report=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/payroll?month={month:yyyy-MM-dd}&branchId={f.Branch}");var summary=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-summary?allDates=true&branchId={f.Branch}");Assert.Equal(report.GetProperty("unpaid").GetDecimal(),summary.GetProperty("payroll").GetProperty("unpaid").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict,(await StatementPay(owner,f.Employee,month,100,0,3600)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await StatementPay(owner,f.Employee,month,1200,2500,1100)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await StatementPay(owner,f.Employee,month,2000,0,3600,partial)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await PrivateFinancialWrite(owner,$"/api/v1/manage/payroll/{f.Employee}/pay",new SalaryPay(month,3600))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await coach.GetAsync($"{StatementRoot(f.Employee)}?month={month:yyyy-MM-dd}")).StatusCode);
        var mine=(await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/payroll/statements")).GetProperty("items")[0];Assert.Equal(1100,mine.GetProperty("remaining").GetDecimal());Assert.False(mine.TryGetProperty("note",out _));Assert.False(mine.TryGetProperty("sources",out _));Assert.False(mine.GetProperty("payments")[0].TryGetProperty("reference",out _));
        (await StatementPay(owner,f.Employee,month,1100,2500,1100)).EnsureSuccessStatusCode();Assert.True((await PrivatePayroll(owner,f.Employee,month)).GetProperty("isPaid").GetBoolean());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.False(await db.PayrollSettlements.AnyAsync(s=>s.EmployeeId==f.Employee));Assert.Equal(3600,await db.PayrollDisbursements.Where(p=>p.EmployeeId==f.Employee).SumAsync(p=>p.Amount));Assert.Equal(1,await db.PayrollAdjustments.CountAsync(a=>a.EmployeeId==f.Employee));
        var s=await db.PayrollStatements.SingleAsync(s=>s.EmployeeId==f.Employee);var source=s.SourceSnapshot;s.NetDue=1;await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PayrollStatements\" WHERE \"Id\"={s.Id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PayrollDisbursements\" SET \"Amount\"=1 WHERE \"EmployeeId\"={f.Employee}"))).SqlState);Assert.Equal(source,await db.PayrollStatements.Where(s=>s.EmployeeId==f.Employee).Select(s=>s.SourceSnapshot).SingleAsync());
    }

    [Fact]
    public async Task V3_statement_blocks_stale_source_changes_financial_edits_and_future_approval_without_changing_operations()
    {
        var f=await AgreementFixture();using var owner=f.Owner;var month=new DateOnly(f.Today.Year,f.Today.Month,1);var stale=await StatementView(owner,f.Employee,month);
        var deduction=new AdjustmentWrite(f.Employee,"Deduction",100,f.Today,"خصم اصطناعي للكشف");(await PrivateFinancialWrite(owner,"/api/v1/manage/payroll/adjustments",deduction)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await StatementApprove(owner,f.Employee,month,stale)).StatusCode);var current=await StatementView(owner,f.Employee,month);(await StatementApprove(owner,f.Employee,month,current)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await PrivateFinancialWrite(owner,"/api/v1/manage/payroll/adjustments",deduction with {Amount=1})).StatusCode);
        var salary=await SalaryTerms(owner,f.Employee);Assert.Equal(HttpStatusCode.Conflict,(await SalaryPost(owner,f.Employee,SalaryBody(salary,month,9999))).StatusCode);(await SalaryPost(owner,f.Employee,SalaryBody(salary,month.AddMonths(1),4000))).EnsureSuccessStatusCode();
        var future=await StatementView(owner,f.Employee,month.AddMonths(1));Assert.False(future.GetProperty("canApprove").GetBoolean());Assert.Equal(HttpStatusCode.Conflict,(await StatementApprove(owner,f.Employee,month.AddMonths(1),future)).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PayrollAdjustments\" SET \"Amount\"=1 WHERE \"EmployeeId\"={f.Employee}"))).SqlState);
        (await Write(owner,$"/api/v1/manage/staff-attendance/{f.Coach}",new {date=f.Today,status="Absent",approvedDeduction=0})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await Write(owner,$"/api/v1/manage/staff-attendance/{f.Coach}",new {date=f.Today,status="Absent",approvedDeduction=50,reason="خصم اصطناعي بعد الاعتماد"})).StatusCode);
        Assert.Equal(2900,(await PrivatePayroll(owner,f.Employee,month)).GetProperty("netDue").GetDecimal());
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await StatementPay(owner,f.Employee,month,12.345m,0,2900)).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(StatementRoot(f.Employee)+"/disbursements",new StatementDisbursement(month,100,0,2900))).StatusCode);
    }

    [Fact]
    public async Task V3_statement_grant_is_independent_scoped_live_and_never_self_approved_across_roles()
    {
        var f=await AgreementFixture();using var owner=f.Owner;var month=new DateOnly(f.Today.Year,f.Today.Month,1);var a=await CreateAdmin();using var admin=a.Admin;using var grants=a.Owner;
        var path=$"/api/v1/manage/staff/{a.Member}/access";var access=Access(f.Branch,true,true) with {SensitivePermissions=[AcademyPermissions.PayrollPay,AcademyPermissions.SalaryManage,AcademyPermissions.CoachAgreementManage]};(await Write(grants,path,access)).EnsureSuccessStatusCode();
        var view=await StatementView(owner,f.Employee,month);Assert.Equal(HttpStatusCode.Forbidden,(await StatementApprove(admin,f.Employee,month,view)).StatusCode);
        access=access with {SensitivePermissions=[AcademyPermissions.PayrollStatementApprove]};(await Write(grants,path,access)).EnsureSuccessStatusCode();(await StatementApprove(admin,f.Employee,month,view)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await StatementPay(admin,f.Employee,month,100,0,3000)).StatusCode);
        access=access with {SensitivePermissions=[AcademyPermissions.PayrollPay]};(await Write(grants,path,access)).EnsureSuccessStatusCode();(await StatementPay(admin,f.Employee,month,100,0,3000)).EnsureSuccessStatusCode();
        (await Write(grants,path,access with {SensitivePermissions=[]})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await StatementPay(admin,f.Employee,month,100,100,2900)).StatusCode);
        (await Write(grants,path,access with {Branches=[f.OtherBranch]})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"{StatementRoot(f.Employee)}?month={month:yyyy-MM-dd}")).StatusCode);
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync($"{StatementRoot(f.Employee)}?month={month:yyyy-MM-dd}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await StatementPay(foreign,f.Employee,month,100,100,2900)).StatusCode);
        var own=await AgreementFixture();using var ownOwner=own.Owner;await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var user=await db.AcademyMemberships.Where(m=>m.Id==own.Coach).Select(m=>m.UserId).SingleAsync();var selfId=Guid.NewGuid();db.AcademyMemberships.Add(new(){Id=selfId,AcademyId=DemoSeed.NogoomAcademyId,UserId=user,Role=AcademyRole.AcademyAdmin,AllSports=true,UseActionGrants=true});await db.SaveChangesAsync();
        (await Write(owner,$"/api/v1/manage/staff/{selfId}/access",Access(own.Branch,true,true) with {SensitivePermissions=[AcademyPermissions.PayrollStatementApprove]})).EnsureSuccessStatusCode();using var self=await Staff(own.Email);var selfView=await StatementView(self,own.Employee,month);Assert.False(selfView.GetProperty("canApprove").GetBoolean());Assert.Equal(HttpStatusCode.Forbidden,(await StatementApprove(self,own.Employee,month,selfView)).StatusCode);
    }
}
