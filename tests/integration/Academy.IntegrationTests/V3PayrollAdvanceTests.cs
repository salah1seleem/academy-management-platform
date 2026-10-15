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
    private static string AdvanceRoot(Guid id)=>$"/api/v1/manage/payroll/{id}/advances";
    private static Task<HttpResponseMessage> AdvanceRecord(HttpClient c,Guid id,decimal amount,string? key=null)=>StatementCommand(c,AdvanceRoot(id),new PayrollAdvanceWrite(amount,"مرجع سلفة داخلي"),key);
    private static Task<HttpResponseMessage> AdvanceApply(HttpClient c,Guid id,Guid advance,DateOnly month,decimal amount,decimal available,decimal due,string? key=null)=>StatementCommand(c,$"{AdvanceRoot(id)}/{advance}/applications",new PayrollAdvanceApply(month,amount,available,due),key);
    [Fact]
    public async Task V3_advance_concurrent_distinct_allocations_cannot_spend_the_same_pool_twice()
    {
        var f=await AgreementFixture();using var owner=f.Owner;var month=new DateOnly(f.Today.Year,f.Today.Month,1);var r=await AdvanceRecord(owner,f.Employee,750);r.EnsureSuccessStatusCode();var advance=(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        foreach(var m in new[]{month,month.AddMonths(-1)})(await StatementApprove(owner,f.Employee,m,await StatementView(owner,f.Employee,m))).EnsureSuccessStatusCode();
        var results=await Task.WhenAll(AdvanceApply(owner,f.Employee,advance,month,500,750,3000),AdvanceApply(owner,f.Employee,advance,month.AddMonths(-1),500,750,3000));Assert.Single(results,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(results,r=>r.StatusCode==HttpStatusCode.Conflict);
        var v=await owner.GetFromJsonAsync<JsonElement>(AccountRoot(f.Employee));Assert.Equal(500,v.GetProperty("advanceApplied").GetDecimal());Assert.Equal(250,v.GetProperty("unallocatedAdvances").GetDecimal());Assert.Equal(750,v.GetProperty("cashOutRecorded").GetDecimal());Assert.Equal(5500,v.GetProperty("remaining").GetDecimal());
    }
    [Fact]
    public async Task V3_advance_demo_is_explicit_repeatable_and_does_not_overwrite_or_run_in_production()
    {
        await using var demo=factory.WithWebHostBuilder(b=>b.UseSetting("Demo:SeedProfile","Football"));using var c=demo.CreateClient();await using var scope=demo.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var env=scope.ServiceProvider.GetRequiredService<IHostEnvironment>();var options=scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;var clock=scope.ServiceProvider.GetRequiredService<TimeProvider>();
        await PayrollAdvanceDemoSeed.Seed(db,env,options,clock);var source=await db.PayrollAdvances.AsNoTracking().SingleAsync(a=>a.Id==PayrollAdvanceDemoSeed.AdvanceId);var before=await db.PayrollDisbursements.Where(p=>p.AdvanceId==source.Id).SumAsync(p=>p.Amount);Assert.Equal(source.Amount,before);await PayrollAdvanceDemoSeed.Seed(db,env,options,clock);Assert.Equal(before,await db.PayrollDisbursements.Where(p=>p.AdvanceId==source.Id).SumAsync(p=>p.Amount));Assert.Equal(source.Amount,(await db.PayrollAdvances.SingleAsync(a=>a.Id==source.Id)).Amount);
        var old=env.EnvironmentName;try{env.EnvironmentName="Production";await Assert.ThrowsAsync<InvalidOperationException>(()=>PayrollAdvanceDemoSeed.Seed(db,env,options,clock));}finally{env.EnvironmentName=old;}
    }
    [Fact]
    public async Task V3_advance_cash_once_and_allocations_across_months_settle_without_deduction_or_second_cash()
    {
        var f=await AgreementFixture();using var owner=f.Owner;var month=new DateOnly(f.Today.Year,f.Today.Month,1);var key=Guid.NewGuid().ToString();
        var records=await Task.WhenAll(AdvanceRecord(owner,f.Employee,750,key),AdvanceRecord(owner,f.Employee,750,key));Assert.Single(records,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(records,r=>r.StatusCode==HttpStatusCode.OK);var advance=(await records[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var account=await owner.GetFromJsonAsync<JsonElement>(AccountRoot(f.Employee));Assert.Equal(750,account.GetProperty("recordedAdvanceTotal").GetDecimal());Assert.Equal(750,account.GetProperty("unallocatedAdvances").GetDecimal());Assert.Equal(750,account.GetProperty("cashOutRecorded").GetDecimal());Assert.Equal(0,account.GetProperty("totalPaid").GetDecimal());Assert.Equal(0,account.GetProperty("items").GetArrayLength());
        foreach(var period in new[]{month.AddMonths(-1),month})(await StatementApprove(owner,f.Employee,period,await StatementView(owner,f.Employee,period))).EnsureSuccessStatusCode();
        var frozen=await owner.GetFromJsonAsync<JsonElement>(AccountRoot(f.Employee));var suffix=$"?asOf={f.Today:yyyy-MM-dd}&cutoffUtc={Uri.EscapeDataString(frozen.GetProperty("cutoffUtc").GetString()!)}";
        var allocation=Guid.NewGuid().ToString();var applications=await Task.WhenAll(AdvanceApply(owner,f.Employee,advance,month,500,750,3000,allocation),AdvanceApply(owner,f.Employee,advance,month,500,750,3000,allocation));Assert.Single(applications,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(applications,r=>r.StatusCode==HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict,(await AdvanceApply(owner,f.Employee,advance,month,500,750,3000)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await AdvanceApply(owner,f.Employee,advance,month,251,250,2500)).StatusCode);
        var after=await owner.GetFromJsonAsync<JsonElement>(AccountRoot(f.Employee));Assert.Equal(500,after.GetProperty("totalPaid").GetDecimal());Assert.Equal(500,after.GetProperty("advanceApplied").GetDecimal());Assert.Equal(0,after.GetProperty("directPaid").GetDecimal());Assert.Equal(750,after.GetProperty("cashOutRecorded").GetDecimal());Assert.Equal(250,after.GetProperty("unallocatedAdvances").GetDecimal());Assert.Equal(5500,after.GetProperty("remaining").GetDecimal());
        (await StatementPay(owner,f.Employee,month,1000,500,2500)).EnsureSuccessStatusCode();(await AdvanceApply(owner,f.Employee,advance,month.AddMonths(-1),250,250,3000)).EnsureSuccessStatusCode();
        var final=await owner.GetFromJsonAsync<JsonElement>(AccountRoot(f.Employee));Assert.Equal(1750,final.GetProperty("totalPaid").GetDecimal());Assert.Equal(1750,final.GetProperty("cashOutRecorded").GetDecimal());Assert.Equal(750,final.GetProperty("advanceApplied").GetDecimal());Assert.Equal(1000,final.GetProperty("directPaid").GetDecimal());Assert.Equal(0,final.GetProperty("unallocatedAdvances").GetDecimal());Assert.Equal(4250,final.GetProperty("remaining").GetDecimal());
        var row=await PrivatePayroll(owner,f.Employee,month);Assert.Equal(3000,row.GetProperty("netDue").GetDecimal());Assert.Equal(1500,row.GetProperty("paidAmount").GetDecimal());Assert.Equal(1500,row.GetProperty("remainingAmount").GetDecimal());Assert.Equal(0,row.GetProperty("deductions").GetDecimal());
        var prior=await owner.GetFromJsonAsync<JsonElement>(AccountRoot(f.Employee)+suffix);Assert.Equal(0,prior.GetProperty("totalPaid").GetDecimal());Assert.Equal(750,prior.GetProperty("unallocatedAdvances").GetDecimal());
        var csv=await owner.GetStringAsync(AccountRoot(f.Employee)+"/export");Assert.Contains("\"6000.00\",\"1750.00\",\"4250.00\"",csv);Assert.Contains("تخصيص سلفة سابقة",csv);
        using var coach=factory.CreateClient();await MobileCoachTestSession.SignInAsync(coach,factory.Services,f.Email);var mine=await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/payroll/account");Assert.Equal(4250,mine.GetProperty("remaining").GetDecimal());Assert.Equal(750,mine.GetProperty("recordedAdvanceTotal").GetDecimal());Assert.DoesNotContain("مرجع سلفة داخلي",mine.ToString());Assert.Equal("AdvanceApplication",mine.GetProperty("items")[1].GetProperty("payments")[0].GetProperty("kind").GetString());
        var report=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/payroll?month={month:yyyy-MM-dd}&branchId={f.Branch}");var summary=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-summary?allDates=true&branchId={f.Branch}");Assert.Equal(report.GetProperty("unpaid").GetDecimal(),summary.GetProperty("payroll").GetProperty("unpaid").GetDecimal());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Single(await db.PayrollAdvances.Where(a=>a.EmployeeId==f.Employee).ToArrayAsync());Assert.Equal(3,await db.PayrollDisbursements.CountAsync(p=>p.EmployeeId==f.Employee));Assert.False(await db.PayrollAdjustments.AnyAsync(a=>a.EmployeeId==f.Employee));Assert.False(await db.PayrollSettlements.AnyAsync(a=>a.EmployeeId==f.Employee));
    }
    [Fact]
    public async Task V3_advance_authority_is_separate_live_branch_tenant_scoped_and_not_coach_self_service()
    {
        var f=await AgreementFixture();using var owner=f.Owner;var a=await CreateAdmin();using var admin=a.Admin;using var grantOwner=a.Owner;var path=$"/api/v1/manage/staff/{a.Member}/access";var access=Access(f.Branch,true,true) with {SensitivePermissions=[AcademyPermissions.PayrollPay]};(await Write(grantOwner,path,access)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden,(await AdvanceRecord(admin,f.Employee,100)).StatusCode);(await Write(grantOwner,path,access with {SensitivePermissions=[AcademyPermissions.PayrollAdvanceManage]})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await AdvanceRecord(admin,f.Employee,100)).StatusCode);
        access=access with{SensitivePermissions=[AcademyPermissions.PayrollAdvanceManage,AcademyPermissions.PayrollPay]};(await Write(grantOwner,path,access)).EnsureSuccessStatusCode();var result=await AdvanceRecord(admin,f.Employee,100);result.EnsureSuccessStatusCode();var advance=(await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();var month=new DateOnly(f.Today.Year,f.Today.Month,1);(await StatementApprove(owner,f.Employee,month,await StatementView(owner,f.Employee,month))).EnsureSuccessStatusCode();
        (await Write(grantOwner,path,access with{SensitivePermissions=[AcademyPermissions.PayrollPay]})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await AdvanceApply(admin,f.Employee,advance,month,50,100,3000)).StatusCode);
        (await Write(grantOwner,path,access with{Branches=[f.OtherBranch]})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync(AdvanceRoot(f.Employee))).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await AdvanceRecord(admin,f.Employee,50)).StatusCode);
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync(AdvanceRoot(f.Employee))).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await AdvanceApply(foreign,f.Employee,advance,month,50,100,3000)).StatusCode);
        using var coach=factory.CreateClient();await MobileCoachTestSession.SignInAsync(coach,factory.Services,f.Email);Assert.Equal(HttpStatusCode.Forbidden,(await AdvanceRecord(coach,f.Employee,100)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await coach.GetAsync(AdvanceRoot(f.Employee))).StatusCode);
        var other=await AgreementFixture();using var otherOwner=other.Owner;(await StatementApprove(owner,other.Employee,month,await StatementView(owner,other.Employee,month))).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await AdvanceApply(owner,other.Employee,advance,month,50,100,3000)).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var user=await db.AcademyMemberships.Where(m=>m.Id==other.Coach).Select(m=>m.UserId).SingleAsync();var selfId=Guid.NewGuid();db.AcademyMemberships.Add(new(){Id=selfId,AcademyId=DemoSeed.NogoomAcademyId,UserId=user,Role=Academy.Infrastructure.Tenancy.AcademyRole.AcademyAdmin,AllSports=true,UseActionGrants=true});await db.SaveChangesAsync();
        (await Write(owner,$"/api/v1/manage/staff/{selfId}/access",Access(other.Branch,true,true) with {SensitivePermissions=[AcademyPermissions.PayrollPay,AcademyPermissions.PayrollAdvanceManage]})).EnsureSuccessStatusCode();using var self=await Staff(other.Email);Assert.Equal(HttpStatusCode.Forbidden,(await AdvanceRecord(self,other.Employee,100)).StatusCode);
        var ownAdvance=await AdvanceRecord(owner,other.Employee,100);ownAdvance.EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await AdvanceApply(self,other.Employee,(await ownAdvance.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(),month,50,100,3000)).StatusCode);
    }
    [Fact]
    public async Task V3_advance_sql_and_ef_sources_are_immutable_no_future_overflow_or_unapproved_allocation()
    {
        var f=await AgreementFixture();using var owner=f.Owner;var month=new DateOnly(f.Today.Year,f.Today.Month,1);var r=await AdvanceRecord(owner,f.Employee,100);r.EnsureSuccessStatusCode();var advance=(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound,(await AdvanceApply(owner,f.Employee,advance,month,10,100,3000)).StatusCode);Assert.Equal(HttpStatusCode.UnprocessableEntity,(await AdvanceRecord(owner,f.Employee,12.345m)).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(AdvanceRoot(f.Employee),new PayrollAdvanceWrite(100))).StatusCode);
        (await StatementApprove(owner,f.Employee,month,await StatementView(owner,f.Employee,month))).EnsureSuccessStatusCode();(await AdvanceApply(owner,f.Employee,advance,month,100,100,3000)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await AdvanceApply(owner,f.Employee,advance,month,1,0,2900)).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var source=await db.PayrollAdvances.SingleAsync(a=>a.Id==advance);source.Amount=1;await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PayrollAdvances\" WHERE \"Id\"={advance}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Employees\" SET \"BranchId\"={f.OtherBranch} WHERE \"Id\"={f.Employee}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PayrollDisbursements\" (\"Id\",\"AcademyId\",\"StatementId\",\"EmployeeId\",\"BranchId\",\"Currency\",\"Sequence\",\"Amount\",\"PaidBefore\",\"RemainingAfter\",\"AdvanceId\",\"AdvanceAppliedBefore\",\"AdvanceRemainingAfter\",\"ActorMembershipId\",\"ActorUserId\",\"RequestKey\",\"RequestHash\",\"IsActive\",\"CreatedAtUtc\",\"UpdatedAtUtc\") SELECT {Guid.NewGuid()},\"AcademyId\",\"StatementId\",\"EmployeeId\",\"BranchId\",\"Currency\",2,1,100,2899,\"AdvanceId\",100,0,\"ActorMembershipId\",\"ActorUserId\",{Guid.NewGuid().ToString()},\"RequestHash\",true,clock_timestamp(),clock_timestamp() FROM \"PayrollDisbursements\" WHERE \"AdvanceId\"={advance}"))).SqlState);
        Assert.Equal(1,await db.PayrollDisbursements.CountAsync(p=>p.EmployeeId==f.Employee));
    }
}
