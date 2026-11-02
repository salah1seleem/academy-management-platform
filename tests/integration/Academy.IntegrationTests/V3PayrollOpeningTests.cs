using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Demo;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Academy.IntegrationTests;

public sealed partial class V2StaffScopeTests
{
    private static string OpeningRoot(Guid id)=>$"/api/v1/manage/payroll/{id}/opening";
    private static Task<JsonElement> OpeningView(HttpClient c,Guid id)=>c.GetFromJsonAsync<JsonElement>(OpeningRoot(id));
    private static PayrollOpeningApproval OpeningBody(JsonElement v,decimal amount)=>new(DateOnly.Parse(v.GetProperty("latestBoundary").GetString()!),amount,"مرجع مراجعة داخلي اصطناعي",v.GetProperty("quoteHash").GetString()!,"ملاحظة افتتاح داخلية");
    private static Task<HttpResponseMessage> OpeningPost(HttpClient c,Guid id,PayrollOpeningApproval body,string? key=null)=>StatementCommand(c,OpeningRoot(id),body,key);
    private static Task<HttpResponseMessage> OpeningPay(HttpClient c,Guid id,decimal amount,decimal paid,decimal remaining,string? key=null)=>StatementCommand(c,OpeningRoot(id)+"/disbursements",new PayrollOpeningPayment(amount,paid,remaining,"صرف افتتاح اصطناعي"),key);

    [Fact]
    public async Task V3_opening_cash_and_advance_allocation_match_account_csv_and_own_mobile_without_new_salary_or_expense()
    {
        var f=await AgreementFixture();using var owner=f.Owner;var month=new DateOnly(f.Today.Year,f.Today.Month,1);
        await using var countsScope=factory.Services.CreateAsyncScope();var countsDb=countsScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var collectionCount=await countsDb.Collections.CountAsync(c=>c.AcademyId==DemoSeed.NogoomAcademyId);var expenseCount=await countsDb.OperatingExpenses.CountAsync(e=>e.AcademyId==DemoSeed.NogoomAcademyId);
        var before=await owner.GetFromJsonAsync<JsonElement>(AccountRoot(f.Employee));var cutoff=Uri.EscapeDataString(before.GetProperty("cutoffUtc").GetString()!);
        var view=await OpeningView(owner,f.Employee);Assert.True(view.GetProperty("canApprove").GetBoolean());
        var body=OpeningBody(view,1200);(await OpeningPost(owner,f.Employee,body)).EnsureSuccessStatusCode();
        (await StatementApprove(owner,f.Employee,month,await StatementView(owner,f.Employee,month))).EnsureSuccessStatusCode();
        var a=await AdvanceRecord(owner,f.Employee,500);a.EnsureSuccessStatusCode();var advance=(await a.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await StatementCommand(owner,$"/api/v1/manage/payroll/{f.Employee}/advances/{advance}/applications",new PayrollAdvanceApply(month.AddMonths(-1),300,500,1200,true),null)).EnsureSuccessStatusCode();
        (await OpeningPay(owner,f.Employee,200,300,900)).EnsureSuccessStatusCode();
        (await StatementPay(owner,f.Employee,month,1000,0,3000)).EnsureSuccessStatusCode();
        var account=await owner.GetFromJsonAsync<JsonElement>(AccountRoot(f.Employee));Assert.True(account.GetProperty("openingBalanceVerified").GetBoolean());
        Assert.Equal(4200,account.GetProperty("totalDue").GetDecimal());Assert.Equal(1500,account.GetProperty("totalPaid").GetDecimal());Assert.Equal(2700,account.GetProperty("remaining").GetDecimal());Assert.Equal(700,account.GetProperty("priorRemaining").GetDecimal());Assert.Equal(2000,account.GetProperty("currentRemaining").GetDecimal());
        Assert.Equal(1700,account.GetProperty("cashOutRecorded").GetDecimal());Assert.Equal(200,account.GetProperty("unallocatedAdvances").GetDecimal());Assert.Single(account.GetProperty("items").EnumerateArray());Assert.Equal(700,account.GetProperty("opening").GetProperty("remaining").GetDecimal());
        var csv=await owner.GetStringAsync(AccountRoot(f.Employee)+"/export");Assert.Contains("\"4200.00\",\"1500.00\",\"2700.00\"",csv);Assert.Contains("افتتاح",csv);Assert.DoesNotContain("مرجع مراجعة داخلي",csv);
        var frozen=await owner.GetFromJsonAsync<JsonElement>(AccountRoot(f.Employee)+$"?asOf={f.Today:yyyy-MM-dd}&cutoffUtc={cutoff}");Assert.False(frozen.GetProperty("openingBalanceVerified").GetBoolean());Assert.Equal(0,frozen.GetProperty("totalDue").GetDecimal());
        Assert.Equal(3000,(await PrivatePayroll(owner,f.Employee,month)).GetProperty("netDue").GetDecimal());
        using var coach=factory.CreateClient();await MobileCoachTestSession.SignInAsync(coach,factory.Services,f.Email);var own=await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/payroll/account");Assert.Equal(2700,own.GetProperty("remaining").GetDecimal());Assert.DoesNotContain("مرجع مراجعة داخلي",own.GetRawText());Assert.DoesNotContain("ملاحظة افتتاح داخلية",own.GetRawText());Assert.False(own.GetProperty("opening").TryGetProperty("actorUserId",out _));
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(2,await db.PayrollStatements.CountAsync(s=>s.EmployeeId==f.Employee));Assert.Equal(3,await db.PayrollDisbursements.CountAsync(p=>p.EmployeeId==f.Employee));Assert.False(await db.PayrollAdjustments.AnyAsync(a=>a.EmployeeId==f.Employee));Assert.False(await db.PayrollSettlements.AnyAsync(s=>s.EmployeeId==f.Employee));
        Assert.Equal(collectionCount,await db.Collections.CountAsync(c=>c.AcademyId==DemoSeed.NogoomAcademyId));Assert.Equal(expenseCount,await db.OperatingExpenses.CountAsync(e=>e.AcademyId==DemoSeed.NogoomAcademyId));
    }
    [Fact]
    public async Task V3_opening_zero_requires_explicit_review_and_rejects_overlapping_legacy_or_approved_months_and_backposting()
    {
        var f=await AgreementFixture();using var owner=f.Owner;var month=new DateOnly(f.Today.Year,f.Today.Month,1);var initial=await OpeningView(owner,f.Employee);
        Assert.Equal(JsonValueKind.Null,initial.GetProperty("opening").ValueKind);var b=OpeningBody(initial,0);
        foreach(var bad in new[]{b with{Amount=-1},b with{Amount=1.001m},b with{Reference=""},b with{AsOf=month.AddDays(1)}})Assert.Equal(HttpStatusCode.UnprocessableEntity,(await OpeningPost(owner,f.Employee,bad)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await OpeningPost(owner,f.Employee,b with{AsOf=month.AddMonths(1)})).StatusCode);
        (await StatementApprove(owner,f.Employee,month.AddMonths(-1),await StatementView(owner,f.Employee,month.AddMonths(-1)))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await OpeningPost(owner,f.Employee,b)).StatusCode);var next=await OpeningView(owner,f.Employee);Assert.Equal(month.AddMonths(-1).ToString("yyyy-MM-dd"),next.GetProperty("latestBoundary").GetString());
        (await OpeningPost(owner,f.Employee,OpeningBody(next,0))).EnsureSuccessStatusCode();var account=await owner.GetFromJsonAsync<JsonElement>(AccountRoot(f.Employee));Assert.True(account.GetProperty("openingBalanceVerified").GetBoolean());Assert.Equal(0,account.GetProperty("opening").GetProperty("amount").GetDecimal());Assert.False((await OpeningView(owner,f.Employee)).GetProperty("canPay").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict,(await owner.GetAsync(StatementRoot(f.Employee)+$"?month={month.AddMonths(-3):yyyy-MM-dd}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await PrivateFinancialWrite(owner,$"/api/v1/manage/payroll/{f.Employee}/pay",new SalaryPay(month.AddMonths(-3),3000))).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var id=Guid.NewGuid();var older=month.AddMonths(-3);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PayrollStatements\" SELECT (jsonb_populate_record(NULL::\"PayrollStatements\",to_jsonb(s)||jsonb_build_object('Id',{id},'Month',{older},'RequestKey','forged-old-month'))).* FROM \"PayrollStatements\" s WHERE s.\"EmployeeId\"={f.Employee} AND s.\"Kind\"='MonthlySalary'"))).SqlState);
        var legacy=await AgreementFixture();using var legacyOwner=legacy.Owner;(await PrivateFinancialWrite(legacyOwner,$"/api/v1/manage/payroll/{legacy.Employee}/pay",new SalaryPay(month.AddMonths(-1),3000))).EnsureSuccessStatusCode();var lv=await OpeningView(legacyOwner,legacy.Employee);Assert.Equal(month.AddMonths(-1).ToString("yyyy-MM-dd"),lv.GetProperty("latestBoundary").GetString());Assert.Equal(HttpStatusCode.Conflict,(await OpeningPost(legacyOwner,legacy.Employee,OpeningBody(lv,1) with{AsOf=month})).StatusCode);
    }
    [Fact]
    public async Task V3_opening_once_only_stale_review_immutable_sources_and_no_overpayment()
    {
        var f=await AgreementFixture();using var owner=f.Owner;var stale=OpeningBody(await OpeningView(owner,f.Employee),1200);
        (await AdvanceRecord(owner,f.Employee,100)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await OpeningPost(owner,f.Employee,stale)).StatusCode);
        var b=OpeningBody(await OpeningView(owner,f.Employee),1200);var key=Guid.NewGuid().ToString();var approvals=await Task.WhenAll(OpeningPost(owner,f.Employee,b,key),OpeningPost(owner,f.Employee,b,key));Assert.Single(approvals,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(approvals,r=>r.StatusCode==HttpStatusCode.OK);Assert.Equal(HttpStatusCode.Conflict,(await OpeningPost(owner,f.Employee,b with{Amount=1300},key)).StatusCode);
        var payKey=Guid.NewGuid().ToString();var pays=await Task.WhenAll(OpeningPay(owner,f.Employee,500,0,1200,payKey),OpeningPay(owner,f.Employee,500,0,1200,payKey));Assert.Single(pays,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(pays,r=>r.StatusCode==HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict,(await OpeningPay(owner,f.Employee,701,500,700)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await OpeningPay(owner,f.Employee,1,0,1200)).StatusCode);Assert.Equal(HttpStatusCode.UnprocessableEntity,(await OpeningPay(owner,f.Employee,1.001m,500,700)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(OpeningRoot(f.Employee),b)).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var s=await db.PayrollStatements.SingleAsync(s=>s.EmployeeId==f.Employee);s.NetDue=1;await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PayrollStatements\" WHERE \"Id\"={s.Id}"))).SqlState);
    }
    [Fact]
    public async Task V3_opening_owner_review_live_branch_tenant_payment_grant_and_own_scope_are_independent()
    {
        var f=await AgreementFixture();using var owner=f.Owner;var body=OpeningBody(await OpeningView(owner,f.Employee),1200);var a=await CreateAdmin();using var admin=a.Admin;using var grants=a.Owner;var path=$"/api/v1/manage/staff/{a.Member}/access";
        var access=Access(f.Branch,true,true) with{SensitivePermissions=[AcademyPermissions.PayrollStatementApprove,AcademyPermissions.PayrollPay]};(await Write(grants,path,access)).EnsureSuccessStatusCode();Assert.False((await OpeningView(admin,f.Employee)).GetProperty("canApprove").GetBoolean());Assert.Equal(HttpStatusCode.Forbidden,(await OpeningPost(admin,f.Employee,body)).StatusCode);
        (await OpeningPost(owner,f.Employee,body)).EnsureSuccessStatusCode();(await OpeningPay(admin,f.Employee,100,0,1200)).EnsureSuccessStatusCode();
        (await Write(grants,path,access with{SensitivePermissions=[]})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await OpeningPay(admin,f.Employee,100,100,1100)).StatusCode);
        (await Write(grants,path,access with{Branches=[f.OtherBranch]})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync(OpeningRoot(f.Employee))).StatusCode);
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync(OpeningRoot(f.Employee))).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await OpeningPay(foreign,f.Employee,100,100,1100)).StatusCode);
        using var coach=factory.CreateClient();await MobileCoachTestSession.SignInAsync(coach,factory.Services,f.Email);Assert.Equal(HttpStatusCode.Forbidden,(await coach.GetAsync(OpeningRoot(f.Employee))).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await OpeningPost(coach,f.Employee,body)).StatusCode);
    }
    [Fact]
    public async Task V3_opening_demo_is_repeatable_preserves_manual_sources_has_no_cash_and_rejects_production()
    {
        await using var demo=factory.WithWebHostBuilder(b=>b.UseSetting("Demo:SeedProfile","Football"));using var c=demo.CreateClient();await using var scope=demo.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var env=scope.ServiceProvider.GetRequiredService<IHostEnvironment>();var options=scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;var clock=scope.ServiceProvider.GetRequiredService<TimeProvider>();
        await PayrollOpeningDemoSeed.Seed(db,env,options,clock);var s=await db.PayrollStatements.AsNoTracking().SingleAsync(s=>s.EmployeeId==PayrollOpeningDemoSeed.EmployeeId&&s.Kind=="Opening");Assert.Equal(1200,s.NetDue);await PayrollOpeningDemoSeed.Seed(db,env,options,clock);Assert.Equal(s.Id,(await db.PayrollStatements.SingleAsync(s=>s.EmployeeId==PayrollOpeningDemoSeed.EmployeeId&&s.Kind=="Opening")).Id);Assert.False(await db.PayrollDisbursements.AnyAsync(p=>p.EmployeeId==s.EmployeeId));
        var old=env.EnvironmentName;try{env.EnvironmentName="Production";await Assert.ThrowsAsync<InvalidOperationException>(()=>PayrollOpeningDemoSeed.Seed(db,env,options,clock));}finally{env.EnvironmentName=old;}
    }
    [Fact]
    public async Task V3_opening_self_admin_cannot_pay_through_other_role_and_database_guards_currency_and_actor()
    {
        var f=await AgreementFixture();using var owner=f.Owner;
        (await OpeningPost(owner,f.Employee,OpeningBody(await OpeningView(owner,f.Employee),1200))).EnsureSuccessStatusCode();(await OpeningPay(owner,f.Employee,100,0,1200)).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var user=await db.AcademyMemberships.Where(m=>m.Id==f.Coach).Select(m=>m.UserId).SingleAsync();var member=Guid.NewGuid();
        db.AcademyMemberships.Add(new(){Id=member,AcademyId=DemoSeed.NogoomAcademyId,UserId=user,Role=Academy.Infrastructure.Tenancy.AcademyRole.AcademyAdmin,AllSports=true,UseActionGrants=true});await db.SaveChangesAsync();
        (await Write(owner,$"/api/v1/manage/staff/{member}/access",Access(f.Branch,true,true) with{SensitivePermissions=[AcademyPermissions.PayrollPay,AcademyPermissions.PayrollAdvanceManage,AcademyPermissions.PayrollStatementApprove]})).EnsureSuccessStatusCode();using var self=await Staff(f.Email);Assert.False((await OpeningView(self,f.Employee)).GetProperty("canPay").GetBoolean());Assert.Equal(HttpStatusCode.Forbidden,(await OpeningPay(self,f.Employee,100,100,1100)).StatusCode);
        var id=Guid.NewGuid();Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PayrollDisbursements\" SELECT (jsonb_populate_record(NULL::\"PayrollDisbursements\",to_jsonb(p)||jsonb_build_object('Id',{id},'ActorMembershipId',{member},'ActorUserId',{user},'Sequence',2,'PaidBefore',100,'RemainingAfter',1000,'RequestKey','forged-self-opening-pay'))).* FROM \"PayrollDisbursements\" p WHERE p.\"EmployeeId\"={f.Employee}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Employees\" SET \"Currency\"='USD' WHERE \"Id\"={f.Employee}"))).SqlState);
        var account=await owner.GetFromJsonAsync<JsonElement>(AccountRoot(f.Employee));Assert.Equal(100,account.GetProperty("totalPaid").GetDecimal());Assert.Equal(1100,account.GetProperty("remaining").GetDecimal());
    }
}
