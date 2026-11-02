using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Demo;
using Academy.Api.Operations;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Academy.IntegrationTests;

public sealed partial class V2StaffScopeTests
{
    private static string SalaryRoot(Guid employee)=>$"/api/v1/manage/employees/{employee}/salary-versions/";
    private static Task<JsonElement> SalaryTerms(HttpClient c,Guid e)=>c.GetFromJsonAsync<JsonElement>(SalaryRoot(e));
    private static async Task<HttpResponseMessage> SalaryPost(HttpClient c,Guid e,SalaryChange body,string? key=null)
    {
        var token=(await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();using var r=new HttpRequestMessage(HttpMethod.Post,SalaryRoot(e)){Content=JsonContent.Create(body)};r.Headers.Add("X-CSRF-TOKEN",token);r.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());return await c.SendAsync(r);
    }
    private static SalaryChange SalaryBody(JsonElement data,DateOnly month,decimal amount)=>new(month,amount,data.GetProperty("revision").GetInt32(),data.GetProperty("currentSalary").GetDecimal(),"تغيير اصطناعي موثق");

    [Fact]
    public async Task V3_salary_future_versions_leave_past_paid_and_current_rates_unchanged_and_are_idempotent_immutable_sources()
    {
        var f=await AgreementFixture();using var owner=f.Owner;var month=new DateOnly(f.Today.Year,f.Today.Month,1);var next=month.AddMonths(1);var before=await SalaryTerms(owner,f.Employee);
        Assert.True(before.GetProperty("hasVerifiedHistory").GetBoolean());Assert.Equal(1,before.GetProperty("revision").GetInt32());var body=SalaryBody(before,next,3500);var key=Guid.NewGuid().ToString();
        var results=await Task.WhenAll(SalaryPost(owner,f.Employee,body,key),SalaryPost(owner,f.Employee,body,key));Assert.Single(results,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(results,r=>r.StatusCode==HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict,(await SalaryPost(owner,f.Employee,body with {MonthlySalary=3600},key)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await SalaryPost(owner,f.Employee,body)).StatusCode);
        Assert.Equal(3000,(await PrivatePayroll(owner,f.Employee,month)).GetProperty("monthlySalary").GetDecimal());Assert.Equal(3000,(await PrivatePayroll(owner,f.Employee,month.AddMonths(-1))).GetProperty("monthlySalary").GetDecimal());Assert.Equal(3500,(await PrivatePayroll(owner,f.Employee,next)).GetProperty("monthlySalary").GetDecimal());
        var a=await AgreementId(owner,f.Coach,new(f.Branch,f.Sport,next,null,"Percent",40,"DeliveredCollected","GroupOnly"));
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(3500,(await db.CoachCompensationAgreements.SingleAsync(x=>x.Id==a)).BaseSalarySnapshot);
        (await Write(owner,$"/api/v1/manage/payroll/coaches/{f.Coach}/agreements/{a}/approve",new CoachAgreementApprovalWrite(null),false)).EnsureSuccessStatusCode();
        (await PrivateFinancialWrite(owner,$"/api/v1/manage/payroll/{f.Employee}/pay",new SalaryPay(month,3000))).EnsureSuccessStatusCode();
        var changed=await SalaryTerms(owner,f.Employee);Assert.Equal(HttpStatusCode.Conflict,(await SalaryPost(owner,f.Employee,SalaryBody(changed,month,4000))).StatusCode);
        var paid=await PrivatePayroll(owner,f.Employee,month);Assert.Equal("PaidSnapshot",paid.GetProperty("salaryBasis").GetString());Assert.Equal(3000,paid.GetProperty("netDue").GetDecimal());
        Assert.Equal(3000,(await db.Employees.SingleAsync(e=>e.Id==f.Employee)).MonthlySalary);Assert.False(await db.PayrollAdjustments.AnyAsync(e=>e.EmployeeId==f.Employee));
        var v=await db.SalaryVersions.SingleAsync(v=>v.EmployeeId==f.Employee&&v.Revision==2);v.MonthlySalary=1;await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"SalaryVersions\" WHERE \"Id\"={v.Id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Employees\" SET \"MonthlySalary\"=1 WHERE \"Id\"={f.Employee}"))).SqlState);
    }

    [Fact]
    public async Task V3_salary_legacy_bridge_starts_at_review_month_never_invents_older_history_or_payments()
    {
        var f=await AgreementFixture();using var owner=f.Owner;var id=Guid.NewGuid();var month=new DateOnly(f.Today.Year,f.Today.Month,1);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        db.Employees.Add(new() {Id=id,AcademyId=DemoSeed.NogoomAcademyId,Name="موظف مرجع راتب قديم اصطناعي",Phone="+2010"+Random.Shared.Next(10000000,99999999),JobTitle="عامل",BranchId=f.Branch,MonthlySalary=3100,Currency="EGP",ContractStart=new(2025,1,1),CreatedAtUtc=DateTimeOffset.Parse("2025-01-01T00:00:00Z")});await db.SaveChangesAsync();
        var before=await SalaryTerms(owner,id);Assert.False(before.GetProperty("hasVerifiedHistory").GetBoolean());Assert.Empty(before.GetProperty("items").EnumerateArray());
        (await SalaryPost(owner,id,SalaryBody(before,month.AddMonths(1),3600))).EnsureSuccessStatusCode();
        var rows=await db.SalaryVersions.Where(v=>v.EmployeeId==id).OrderBy(v=>v.Revision).ToArrayAsync();Assert.Equal(2,rows.Length);Assert.Equal("LegacyBaseline",rows[0].Kind);Assert.Equal(month,rows[0].EffectiveFrom);Assert.Equal(3100,rows[0].MonthlySalary);Assert.Equal(3600,rows[1].MonthlySalary);
        Assert.Equal("LegacyUnverified",(await PrivatePayroll(owner,id,month.AddMonths(-1))).GetProperty("salaryBasis").GetString());Assert.Equal(3100,(await PrivatePayroll(owner,id,month.AddMonths(-1))).GetProperty("monthlySalary").GetDecimal());
        Assert.False(await db.PayrollSettlements.AnyAsync(p=>p.EmployeeId==id));Assert.False(await db.PayrollAdjustments.AnyAsync(p=>p.EmployeeId==id));
    }

    [Fact]
    public async Task V3_salary_requires_distinct_grant_valid_branch_no_self_approval_and_protects_coach_mobile_privacy()
    {
        var f=await AgreementFixture();using var owner=f.Owner;var month=new DateOnly(f.Today.Year,f.Today.Month,1);var terms=await SalaryTerms(owner,f.Employee);var body=SalaryBody(terms,month,4000);
        var a=await CreateAdmin();using var admin=a.Admin;using var grantOwner=a.Owner;
        (await Write(grantOwner,$"/api/v1/manage/staff/{a.Member}/access",Access(f.Branch,true,true) with {SensitivePermissions=[AcademyPermissions.PayrollPay,AcademyPermissions.CoachAgreementManage]})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden,(await SalaryPost(admin,f.Employee,body)).StatusCode);
        (await Write(grantOwner,$"/api/v1/manage/staff/{a.Member}/access",Access(f.Branch,true,true) with {SensitivePermissions=[AcademyPermissions.SalaryManage]})).EnsureSuccessStatusCode();
        var saved=await SalaryPost(admin,f.Employee,body);Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync(SalaryRoot(f.Employee))).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await SalaryPost(foreign,f.Employee,body)).StatusCode);
        var self=await Write(owner,"/api/v1/manage/employees",new EmployeeWrite("إداري عرض ذاتي","010"+Random.Shared.Next(10000000,99999999),"إداري",f.Branch,3000,month,null,a.Member,null),false);self.EnsureSuccessStatusCode();var selfId=(await self.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden,(await SalaryPost(admin,selfId,SalaryBody(await SalaryTerms(owner,selfId),month,5000))).StatusCode);
        (await Write(grantOwner,$"/api/v1/manage/staff/{a.Member}/access",Access(f.OtherBranch,true,true) with {SensitivePermissions=[AcademyPermissions.SalaryManage]})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync(SalaryRoot(f.Employee))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(SalaryRoot(f.Employee),body)).StatusCode);Assert.Equal(HttpStatusCode.UnprocessableEntity,(await SalaryPost(owner,f.Employee,body with {MonthlySalary=12.345m})).StatusCode);
        using var coach=factory.CreateClient();await MobileCoachTestSession.SignInAsync(coach,factory.Services,f.Email);Assert.Equal(HttpStatusCode.Forbidden,(await coach.GetAsync(SalaryRoot(f.Employee))).StatusCode);
        var own=await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/payroll/salary-versions");Assert.Equal(4000,own.GetProperty("currentSalary").GetDecimal());Assert.False(own.GetProperty("canChange").GetBoolean());Assert.All(own.GetProperty("items").EnumerateArray(),v=> {Assert.Equal(JsonValueKind.Null,v.GetProperty("note").ValueKind);Assert.Equal(JsonValueKind.Null,v.GetProperty("approvedBy").ValueKind);});
    }

    [Fact]
    public async Task V3_salary_admin_with_second_coach_role_cannot_change_own_rate_or_create_another_self_employee()
    {
        var f=await AgreementFixture();using var owner=f.Owner;await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var user=await db.AcademyMemberships.Where(m=>m.Id==f.Coach).Select(m=>m.UserId).SingleAsync();var admin=Guid.NewGuid();db.AcademyMemberships.Add(new(){Id=admin,AcademyId=DemoSeed.NogoomAcademyId,UserId=user,Role=Academy.Infrastructure.Tenancy.AcademyRole.AcademyAdmin,UseActionGrants=true,AllBranches=false,AllSports=true});await db.SaveChangesAsync();
        (await Write(owner,$"/api/v1/manage/staff/{admin}/access",Access(f.Branch,true,true) with {SensitivePermissions=[AcademyPermissions.SalaryManage]})).EnsureSuccessStatusCode();using var self=await Staff(f.Email);
        var terms=await SalaryTerms(self,f.Employee);Assert.False(terms.GetProperty("canChange").GetBoolean());Assert.Equal(HttpStatusCode.Forbidden,(await SalaryPost(self,f.Employee,SalaryBody(terms,new(f.Today.Year,f.Today.Month,1),9999))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Write(self,"/api/v1/manage/employees",new EmployeeWrite("تلاعب ذاتي اصطناعي","010"+Random.Shared.Next(10000000,99999999),"مدرب",f.Branch,9999,new(f.Today.Year,f.Today.Month,1),null,admin,null),false)).StatusCode);
    }

    [Fact]
    public async Task V3_salary_demo_is_opt_in_repeatable_preserves_manual_decisions_and_blocks_production()
    {
        await using var demo=factory.WithWebHostBuilder(b=>b.UseSetting("Demo:SeedProfile","Football"));using var client=demo.CreateClient();await using var scope=demo.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var env=scope.ServiceProvider.GetRequiredService<IHostEnvironment>();var options=scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;var clock=scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var before=await db.SalaryVersions.AsNoTracking().Where(v=>v.EmployeeId==SalaryHistoryDemoSeed.EmployeeId).OrderBy(v=>v.Revision).Select(v=>new {v.Id,v.MonthlySalary,v.EffectiveFrom}).ToArrayAsync();
        await SalaryHistoryDemoSeed.Seed(db,env,options,clock);await SalaryHistoryDemoSeed.Seed(db,env,options,clock);var rows=await db.SalaryVersions.Where(v=>v.EmployeeId==SalaryHistoryDemoSeed.EmployeeId).OrderBy(v=>v.Revision).ToArrayAsync();
        if(before.Length==0){Assert.Equal(2,rows.Length);Assert.Equal(500,rows[1].MonthlySalary-rows[0].MonthlySalary);Assert.Equal(rows[0].EffectiveFrom.AddMonths(1),rows[1].EffectiveFrom);}
        else Assert.Equal(before,rows.Select(v=>new {v.Id,v.MonthlySalary,v.EffectiveFrom}).ToArray()); // An approved manual change must never be replaced by seed defaults.
        Assert.False(await db.SalaryVersions.AnyAsync(v=>v.EmployeeId==SalaryHistoryDemoSeed.EmployeeId&&v.AcademyId==DemoSeed.FutureAcademyId));var old=env.EnvironmentName;try{env.EnvironmentName="Production";await Assert.ThrowsAsync<InvalidOperationException>(()=>SalaryHistoryDemoSeed.Seed(db,env,options,clock));}finally{env.EnvironmentName=old;}
    }
}
