using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Demo;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Academy.Infrastructure.Identity;

namespace Academy.IntegrationTests;
public sealed partial class V2StaffScopeTests
{
    [Fact]
    public async Task V3_post_contract_demo_is_opt_in_idempotent_preserves_owner_decisions_and_rejects_production()
    {
        await using var demo=factory.WithWebHostBuilder(b=>b.UseSetting("Demo:SeedProfile","Football"));using var c=demo.CreateClient();await using var scope=demo.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var env=scope.ServiceProvider.GetRequiredService<IHostEnvironment>();var options=scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;var clock=scope.ServiceProvider.GetRequiredService<TimeProvider>();var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await PostContractPrivateDemoSeed.Seed(db,env,options,clock,users);await PostContractPrivateDemoSeed.Seed(db,env,options,clock,users);
        var employee=PostContractPrivateDemoSeed.Id("employee");var coach=PostContractPrivateDemoSeed.Id("member");Assert.Equal(2,await db.PrivateServiceDeliveries.CountAsync(d=>d.EmployeeId==employee));Assert.False(await db.PayrollStatements.AnyAsync(s=>s.EmployeeId==employee));Assert.False(await db.PayrollAdjustments.AnyAsync(s=>s.EmployeeId==employee));
        (await Write(c,"/api/v1/auth/login",new{email="owner.football@example.test",password="Demo-Only-123!"},false)).EnsureSuccessStatusCode();var path=$"{PrivateRoot(coach)}/{PostContractPrivateDemoSeed.Id("delivery/1")}";var quote=await PrivatePreview(c,path);Assert.Equal("Review",quote.GetProperty("action").GetString());(await PrivatePost(c,path,quote)).EnsureSuccessStatusCode();(await FinalPost(c,path,await FinalQuote(c,path))).EnsureSuccessStatusCode();await PostContractPrivateDemoSeed.Seed(db,env,options,clock,users);Assert.Single(await db.PayrollStatements.Where(s=>s.EmployeeId==employee).ToArrayAsync());
        var old=env.EnvironmentName;try{env.EnvironmentName="Production";await Assert.ThrowsAsync<InvalidOperationException>(()=>PostContractPrivateDemoSeed.Seed(db,env,options,clock,users));}finally{env.EnvironmentName=old;}
    }
    private static Task<JsonElement> FinalQuote(HttpClient c,string path)=>c.GetFromJsonAsync<JsonElement>(path+"/final-preview");
    private static Task<HttpResponseMessage> FinalPost(HttpClient c,string path,JsonElement quote,string? key=null)=>StatementCommand(c,path+"/final-settlement",new PrivateFinalWrite(quote.GetProperty("quoteHash").GetString()!,"تسوية اصطناعية خاصة لا تكشف للمدرب"),key);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task V3_post_contract_private_due_has_no_fake_monthly_salary_and_reuses_partial_cash_account(bool inactive)
    {
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Africa/Cairo").DateTime);var month=new DateOnly(today.Year,today.Month,1);var end=month.AddDays(-1);
        var f=await PrivateFixture(historicalService:end,contractEnd:end);using var owner=f.Owner;var path=(await PrivateBindUnits(owner,f.Coach,f.Document,f.Sessions[0],f.Sessions[1]))[0];
        var oldMonth=month.AddMonths(-1);(await StatementApprove(owner,f.Employee,oldMonth,await StatementView(owner,f.Employee,oldMonth))).EnsureSuccessStatusCode();(await StatementPay(owner,f.Employee,oldMonth,3000,0,3000)).EnsureSuccessStatusCode();var old=await StatementView(owner,f.Employee,oldMonth);
        await PrivatePayment(owner,f.Document,f.Enrollment,f.Plan,f.Actor,1000);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        if(inactive){(await db.Employees.SingleAsync(e=>e.Id==f.Employee)).IsActive=false;await db.SaveChangesAsync();}
        var history=await owner.GetFromJsonAsync<JsonElement>(PrivateRoot(f.Coach));Assert.True(history.GetProperty("canReview").GetBoolean());Assert.Equal(!inactive,history.GetProperty("canRecord").GetBoolean());
        var review=await PrivatePreview(owner,path);Assert.Equal("Review",review.GetProperty("action").GetString());Assert.Equal(50,review.GetProperty("difference").GetDecimal());
        Assert.False((await FinalQuote(owner,path)).GetProperty("canFinalize").GetBoolean());(await PrivatePost(owner,path,review)).EnsureSuccessStatusCode();
        var q=await FinalQuote(owner,path);Assert.True(q.GetProperty("canFinalize").GetBoolean());var key=Guid.NewGuid().ToString();var collections=await db.Collections.CountAsync();var expenses=await db.OperatingExpenses.CountAsync();
        var results=await Task.WhenAll(FinalPost(owner,path,q,key),FinalPost(owner,path,q,key));Assert.Single(results,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(results,r=>r.StatusCode==HttpStatusCode.OK);Assert.Equal(HttpStatusCode.Conflict,(await FinalPost(owner,path,q)).StatusCode);
        var entry=await db.PrivateCompensationEntries.SingleAsync(e=>e.EmployeeId==f.Employee&&e.Kind=="PrivateFinal");Assert.Equal(50,entry.Delta);Assert.NotNull(entry.FinalStatementId);Assert.Null(entry.PayrollAdjustmentId);
        var statement=await db.PayrollStatements.SingleAsync(s=>s.Id==entry.FinalStatementId);Assert.Equal("PrivateFinal",statement.Kind);Assert.Equal(0,statement.ContractDays);Assert.Equal(0,statement.MonthlySalary);Assert.Equal(50,statement.NetDue);Assert.Equal(entry.ReviewEntryId,statement.PrivateReviewId);
        Assert.False(await db.PayrollAdjustments.AnyAsync(a=>a.EmployeeId==f.Employee));Assert.False(await db.PayrollStatements.AnyAsync(s=>s.EmployeeId==f.Employee&&s.Kind=="MonthlySalary"&&s.Month==month));
        Assert.Equal("None",(await PrivatePreview(owner,path)).GetProperty("action").GetString());
        var account=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/payroll/{f.Employee}/account");Assert.Equal(3050,account.GetProperty("totalDue").GetDecimal());Assert.Equal(3000,account.GetProperty("totalPaid").GetDecimal());Assert.Equal(50,account.GetProperty("remaining").GetDecimal());Assert.Single(account.GetProperty("items").EnumerateArray());Assert.Single(account.GetProperty("finalSettlements").EnumerateArray());Assert.False(account.GetProperty("currentMonthApproved").GetBoolean());
        var payPath=$"/api/v1/manage/payroll/{f.Employee}/final-settlements/{statement.Id}/disbursements";var payKey=Guid.NewGuid().ToString();var pay=new PayrollOpeningPayment(20,0,50);
        (await StatementCommand(owner,payPath,pay,payKey)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.OK,(await StatementCommand(owner,payPath,pay,payKey)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await StatementCommand(owner,payPath,pay,null)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await StatementCommand(owner,payPath,new PayrollOpeningPayment(31,20,30),null)).StatusCode);
        (await StatementCommand(owner,payPath,new PayrollOpeningPayment(30,20,30),null)).EnsureSuccessStatusCode();
        account=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/payroll/{f.Employee}/account");Assert.Equal(3050,account.GetProperty("totalDue").GetDecimal());Assert.Equal(3050,account.GetProperty("totalPaid").GetDecimal());Assert.Equal(0,account.GetProperty("remaining").GetDecimal());Assert.Equal(3050,account.GetProperty("cashOutRecorded").GetDecimal());
        Assert.Equal(2,await db.PayrollDisbursements.CountAsync(p=>p.StatementId==statement.Id));Assert.Equal(collections,await db.Collections.CountAsync());Assert.Equal(expenses,await db.OperatingExpenses.CountAsync());
        var retained=await StatementView(owner,f.Employee,oldMonth);Assert.Equal(old.GetProperty("statement").GetProperty("sourceHash").GetString(),retained.GetProperty("statement").GetProperty("sourceHash").GetString());Assert.Equal(3000,retained.GetProperty("row").GetProperty("paidAmount").GetDecimal());
        using var coach=factory.CreateClient();await MobileCoachTestSession.SignInAsync(coach,factory.Services,f.Email);var own=await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/payroll/account");Assert.Equal(0,own.GetProperty("remaining").GetDecimal());Assert.DoesNotContain("تسوية اصطناعية",own.GetRawText());Assert.False(own.GetProperty("finalSettlements")[0].GetProperty("canPay").GetBoolean());Assert.Equal(HttpStatusCode.Forbidden,(await FinalPost(coach,path,q)).StatusCode);
        var csv=await owner.GetStringAsync($"/api/v1/manage/payroll/{f.Employee}/account/export?asOf={account.GetProperty("asOf").GetString()}&cutoffUtc={Uri.EscapeDataString(account.GetProperty("cutoffUtc").GetString()!)}");Assert.Contains("استحقاق عمولة بعد نهاية العقد",csv);Assert.Contains("3050.00",csv);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PayrollStatements\" SET \"NetDue\"=51 WHERE \"Id\"={statement.Id}"))).SqlState);Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PrivateCompensationEntries\" WHERE \"Id\"={entry.Id}"))).SqlState);
        // A bare final statement is not a payable source: both immutable sides must commit together.
        var otherPath=(await PrivateRows(owner,f.Coach)).Single(r=>r.GetProperty("ordinal").GetInt32()==2).GetProperty("id").GetGuid();var otherRoot=$"{PrivateRoot(f.Coach)}/{otherPath}";(await PrivatePost(owner,otherRoot,await PrivatePreview(owner,otherRoot))).EnsureSuccessStatusCode();var otherReview=await db.PrivateCompensationEntries.SingleAsync(e=>e.DeliveryId==otherPath&&e.Kind=="Review");
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PayrollStatements\" SELECT (jsonb_populate_record(NULL::\"PayrollStatements\",to_jsonb(s)||jsonb_build_object('Id',{Guid.NewGuid()},'RequestKey',{Guid.NewGuid().ToString()},'PrivateReviewId',{otherReview.Id},'CreatedAtUtc',clock_timestamp(),'SourceSnapshot',(s.\"SourceSnapshot\"::jsonb||jsonb_build_object('reviewEntryId',{otherReview.Id},'deliveryId',{otherPath},'FactsHash',{otherReview.FactsHash}))::text))).* FROM \"PayrollStatements\" s WHERE s.\"Id\"={statement.Id}"))).SqlState);
        Assert.Equal(HttpStatusCode.Forbidden,(await StatementCommand(coach,payPath,new PayrollOpeningPayment(1,50,0),null)).StatusCode);using var foreign=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await StatementCommand(foreign,payPath,new PayrollOpeningPayment(1,50,0),null)).StatusCode);
        var stale=await FinalQuote(owner,path);await PrivatePayment(owner,f.Document,f.Enrollment,f.Plan,f.Actor,1000);Assert.Equal(HttpStatusCode.Conflict,(await FinalPost(owner,path,stale)).StatusCode);(await PrivatePost(owner,path,await PrivatePreview(owner,path))).EnsureSuccessStatusCode();(await FinalPost(owner,path,await FinalQuote(owner,path))).EnsureSuccessStatusCode();
        Assert.Equal(2,await db.PayrollStatements.CountAsync(s=>s.EmployeeId==f.Employee&&s.Kind=="PrivateFinal"));account=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/payroll/{f.Employee}/account");Assert.Equal(3100,account.GetProperty("totalDue").GetDecimal());Assert.Equal(3050,account.GetProperty("totalPaid").GetDecimal());Assert.Equal(50,account.GetProperty("remaining").GetDecimal());Assert.Equal(50,account.GetProperty("finalRemaining").GetDecimal());Assert.Single(account.GetProperty("items").EnumerateArray());Assert.False(await db.PayrollAdjustments.AnyAsync(a=>a.EmployeeId==f.Employee));
    }
    [Fact]
    public async Task V3_post_contract_private_requires_ended_contract_owner_fresh_sources_and_csrf()
    {
        var f=await PrivateFixture();using var owner=f.Owner;var path=(await PrivateBindUnits(owner,f.Coach,f.Document,f.Sessions[0])).Single();await PrivatePayment(owner,f.Document,f.Enrollment,f.Plan,f.Actor,1000);var q=await FinalQuote(owner,path);Assert.False(q.GetProperty("canFinalize").GetBoolean());Assert.Equal(HttpStatusCode.Conflict,(await FinalPost(owner,path,q)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(path+"/final-settlement",new PrivateFinalWrite(q.GetProperty("quoteHash").GetString()!))).StatusCode);
        var a=await CreateAdmin();using var admin=a.Admin;using var grantOwner=a.Owner;(await Write(grantOwner,$"/api/v1/manage/staff/{a.Member}/access",Access(f.Branch,true,true) with{SensitivePermissions=[AcademyPermissions.PayrollPay,AcademyPermissions.PayrollStatementApprove]})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await FinalPost(admin,path,q)).StatusCode);
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await FinalPost(foreign,path,q)).StatusCode);
    }
}
