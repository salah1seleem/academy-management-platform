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
    [Fact]
    public async Task V3_contract_demo_is_opt_in_idempotent_and_forbidden_in_production()
    {
        await using var demo=factory.WithWebHostBuilder(b=>b.UseSetting("Demo:SeedProfile","Football"));using var client=demo.CreateClient();await using var scope=demo.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var env=scope.ServiceProvider.GetRequiredService<IHostEnvironment>();var options=scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;var clock=scope.ServiceProvider.GetRequiredService<TimeProvider>();
        await ContractHistoryDemoSeed.Seed(db,env,options,clock);var before=await db.ContractVersions.Where(v=>v.EmployeeId==ContractHistoryDemoSeed.EmployeeId).OrderBy(v=>v.Revision).Select(v=>new {v.Id,v.EffectiveFrom,v.ContractEnd}).ToArrayAsync();await ContractHistoryDemoSeed.Seed(db,env,options,clock);Assert.Equal(before,await db.ContractVersions.Where(v=>v.EmployeeId==ContractHistoryDemoSeed.EmployeeId).OrderBy(v=>v.Revision).Select(v=>new {v.Id,v.EffectiveFrom,v.ContractEnd}).ToArrayAsync());Assert.Equal(2,before.Length);Assert.Equal(15,before[1].ContractEnd!.Value.Day);
        var old=env.EnvironmentName;try{env.EnvironmentName="Production";await Assert.ThrowsAsync<InvalidOperationException>(()=>ContractHistoryDemoSeed.Seed(db,env,options,clock));}finally{env.EnvironmentName=old;}
    }
    private static string ContractRoot(Guid id) => $"/api/v1/manage/employees/{id}/contract-versions";
    private static Task<JsonElement> ContractView(HttpClient c,Guid id) => c.GetFromJsonAsync<JsonElement>(ContractRoot(id));
    private static Task<HttpResponseMessage> ContractPost(HttpClient c,Guid id,ContractChange b,string? key=null) => StatementCommand(c,ContractRoot(id),b,key);
    [Fact]
    public async Task V3_contract_future_version_preserves_approved_salary_and_prorates_only_its_month_with_own_safe_read()
    {
        var f=await AgreementFixture();using var owner=f.Owner;var month=new DateOnly(f.Today.Year,f.Today.Month,1);var next=month.AddMonths(1);
        var v=await ContractView(owner,f.Employee);Assert.Equal(1,v.GetProperty("revision").GetInt32());Assert.True(v.GetProperty("hasVerifiedHistory").GetBoolean());
        (await StatementApprove(owner,f.Employee,month,await StatementView(owner,f.Employee,month))).EnsureSuccessStatusCode();
        var body=new ContractChange(next,new(2000,1,1),next.AddDays(14),1,"ملاحظة خاصة بعقد اصطناعي");var key=Guid.NewGuid().ToString();
        var results=await Task.WhenAll(ContractPost(owner,f.Employee,body,key),ContractPost(owner,f.Employee,body,key));Assert.Single(results,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(results,r=>r.StatusCode==HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict,(await ContractPost(owner,f.Employee,body)).StatusCode);
        var current=await PrivatePayroll(owner,f.Employee,month);Assert.Equal(3000,current.GetProperty("baseDue").GetDecimal());Assert.Equal(3000,current.GetProperty("netDue").GetDecimal());
        var future=await PrivatePayroll(owner,f.Employee,next);Assert.Equal(15,future.GetProperty("contractDays").GetInt32());Assert.Equal(decimal.Round(3000m*15/DateTime.DaysInMonth(next.Year,next.Month),2,MidpointRounding.AwayFromZero),future.GetProperty("baseDue").GetDecimal());Assert.Equal(3000,future.GetProperty("monthlySalary").GetDecimal());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var employee=await db.Employees.AsNoTracking().SingleAsync(e=>e.Id==f.Employee);Assert.Null(employee.ContractEnd);Assert.False(await db.PayrollDisbursements.AnyAsync(p=>p.EmployeeId==f.Employee));
        var frozen=await db.PayrollStatements.AsNoTracking().SingleAsync(s=>s.EmployeeId==f.Employee);Assert.Equal(3000,frozen.NetDue);Assert.NotEqual(Guid.Empty,JsonDocument.Parse(frozen.SourceSnapshot).RootElement.GetProperty("contractVersionId").GetGuid());
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Employees\" SET \"ContractEnd\"={f.Today} WHERE \"Id\"={f.Employee}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"ContractVersions\" WHERE \"EmployeeId\"={f.Employee}"))).SqlState);
        var immutable=await db.ContractVersions.FirstAsync(v=>v.EmployeeId==f.Employee);immutable.ContractEnd=f.Today;await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
        using var coach=factory.CreateClient();await MobileCoachTestSession.SignInAsync(coach,factory.Services,f.Email);var own=await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/payroll/contract-versions");Assert.False(own.GetProperty("canChange").GetBoolean());Assert.Equal(JsonValueKind.Null,own.GetProperty("items")[0].GetProperty("note").ValueKind);Assert.DoesNotContain("ملاحظة خاصة",own.GetRawText());Assert.Equal(next.AddDays(14).ToString("yyyy-MM-dd"),own.GetProperty("items")[0].GetProperty("contractEnd").GetString());
        Assert.Equal(HttpStatusCode.Forbidden,(await ContractPost(coach,f.Employee,body)).StatusCode);
    }
    [Fact]
    public async Task V3_contract_current_version_updates_owner_expiry_employee_detail_and_statement_quote_but_not_existing_private_source()
    {
        var f=await PrivateFixture(isolatedBranch:true);using var owner=f.Owner;var month=new DateOnly(f.Today.Year,f.Today.Month,1);
        var path=(await PrivateBindUnits(owner,f.Coach,f.Document,f.Sessions[0])).Single();await PrivatePayment(owner,f.Document,f.Enrollment,f.Plan,f.Actor,1000);(await PrivatePost(owner,path,await PrivatePreview(owner,path))).EnsureSuccessStatusCode();
        var stale=await StatementView(owner,f.Employee,month);(await ContractPost(owner,f.Employee,new(month,new(2000,1,1),f.Today,1))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await StatementApprove(owner,f.Employee,month,stale)).StatusCode);
        var p=await StatementView(owner,f.Employee,month);var baseDue=decimal.Round(3000m*f.Today.Day/DateTime.DaysInMonth(f.Today.Year,f.Today.Month),2,MidpointRounding.AwayFromZero);Assert.Equal(baseDue+50,p.GetProperty("row").GetProperty("netDue").GetDecimal());
        var detail=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/employees/{f.Employee}");Assert.Equal(f.Today.ToString("yyyy-MM-dd"),detail.GetProperty("contractEnd").GetString());
        var edit=new EmployeeWrite(detail.GetProperty("name").GetString()!,detail.GetProperty("phone").GetString()!,detail.GetProperty("jobTitle").GetString()!,f.Branch,3000,new(2000,1,1),f.Today,f.Coach,null);
        Assert.Equal(HttpStatusCode.NoContent,(await Write(owner,$"/api/v1/manage/employees/{f.Employee}",edit)).StatusCode);
        await using(var check=factory.Services.CreateAsyncScope()){var saved=check.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Null(await saved.Employees.Where(e=>e.Id==f.Employee).Select(e=>e.ContractEnd).SingleAsync());}
        var summary=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-summary?allDates=true&branchId={f.Branch}");Assert.Equal(1,summary.GetProperty("payroll").GetProperty("expiringContracts").GetInt32());Assert.Equal(baseDue+50,summary.GetProperty("payroll").GetProperty("netDue").GetDecimal());
        Assert.Equal(50,(await PrivateRows(owner,f.Coach))[0].GetProperty("postedAmount").GetDecimal());
        (await StatementApprove(owner,f.Employee,month,p)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await ContractPost(owner,f.Employee,new(month,new(2000,1,1),null,2))).StatusCode);
    }
    [Fact]
    public async Task V3_contract_grants_are_live_scoped_not_self_and_reject_past_and_foreign_versions()
    {
        var f=await AgreementFixture();using var owner=f.Owner;var a=await CreateAdmin();using var admin=a.Admin;using var grantOwner=a.Owner;var v=await ContractView(owner,f.Employee);var month=DateOnly.Parse(v.GetProperty("currentMonth").GetString()!);var body=new ContractChange(month,new(2000,1,1),month.AddMonths(1).AddDays(-1),1);
        (await Write(grantOwner,$"/api/v1/manage/staff/{a.Member}/access",Access(f.Branch,true,true) with {SensitivePermissions=[AcademyPermissions.PayrollPay]})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await ContractPost(admin,f.Employee,body)).StatusCode);
        var access=Access(f.Branch,true,true) with {SensitivePermissions=[AcademyPermissions.SalaryManage]};(await Write(grantOwner,$"/api/v1/manage/staff/{a.Member}/access",access)).EnsureSuccessStatusCode();(await ContractPost(admin,f.Employee,body)).EnsureSuccessStatusCode();
        (await Write(grantOwner,$"/api/v1/manage/staff/{a.Member}/access",access with {SensitivePermissions=[]})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await ContractPost(admin,f.Employee,body with {ExpectedRevision=2})).StatusCode);
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync(ContractRoot(f.Employee))).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await ContractPost(foreign,f.Employee,body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(ContractRoot(f.Employee),body)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await ContractPost(owner,f.Employee,body with {EffectiveFrom=month.AddMonths(-1),ExpectedRevision=2})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await ContractPost(owner,f.Employee,body with {EffectiveFrom=month.AddMonths(13),ExpectedRevision=2})).StatusCode);
        (await Write(grantOwner,$"/api/v1/manage/staff/{a.Member}/access",Access(f.OtherBranch,true,true) with {SensitivePermissions=[AcademyPermissions.SalaryManage]})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync(ContractRoot(f.Employee))).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var user=await db.AcademyMemberships.Where(m=>m.Id==f.Coach).Select(m=>m.UserId).SingleAsync();var selfId=Guid.NewGuid();
        db.AcademyMemberships.Add(new(){Id=selfId,AcademyId=DemoSeed.NogoomAcademyId,UserId=user,Role=Academy.Infrastructure.Tenancy.AcademyRole.AcademyAdmin,UseActionGrants=true,AllBranches=false,AllSports=true});await db.SaveChangesAsync();
        (await Write(owner,$"/api/v1/manage/staff/{selfId}/access",access)).EnsureSuccessStatusCode();using var self=await Staff(f.Email);Assert.Equal(HttpStatusCode.Forbidden,(await ContractPost(self,f.Employee,body with {ExpectedRevision=2})).StatusCode);
    }
    [Fact]
    public async Task V3_contract_legacy_baseline_starts_at_review_and_raw_sql_cannot_forge_past_or_initial_approval()
    {
        var f=await AgreementFixture();using var owner=f.Owner;await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var month=new DateOnly(f.Today.Year,f.Today.Month,1);var id=Guid.NewGuid();
        db.Employees.Add(new(){Id=id,AcademyId=DemoSeed.NogoomAcademyId,Name="عامل عقد قديم اصطناعي",Phone="+2010"+Random.Shared.Next(10000000,99999999),JobTitle="عامل",BranchId=f.Branch,MonthlySalary=3100,Currency="EGP",ContractStart=new(2025,1,1),CreatedAtUtc=DateTimeOffset.Parse("2025-01-01T00:00:00Z")});await db.SaveChangesAsync();
        var before=await ContractView(owner,id);Assert.False(before.GetProperty("hasVerifiedHistory").GetBoolean());
        (await ContractPost(owner,id,new(month.AddMonths(1),new(2025,1,1),month.AddMonths(2).AddDays(-1),0))).EnsureSuccessStatusCode();
        var rows=await db.ContractVersions.AsNoTracking().Where(v=>v.EmployeeId==id).OrderBy(v=>v.Revision).ToArrayAsync();Assert.Equal(2,rows.Length);Assert.Equal("LegacyBaseline",rows[0].Kind);Assert.Equal(month,rows[0].EffectiveFrom);Assert.Equal(month.AddMonths(1),rows[1].EffectiveFrom);
        Assert.Equal(HttpStatusCode.Conflict,(await ContractPost(owner,id,new(month,new(2025,1,1),null,2))).StatusCode);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ContractVersions\" SELECT (jsonb_populate_record(NULL::\"ContractVersions\",to_jsonb(v)||jsonb_build_object('Id',{Guid.NewGuid()},'RequestKey',{Guid.NewGuid().ToString()},'Revision',3,'PreviousVersionId',{rows[1].Id},'EffectiveFrom',{month.AddMonths(-1)}))).* FROM \"ContractVersions\" v WHERE \"Id\"={rows[1].Id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ContractVersions\" SELECT (jsonb_populate_record(NULL::\"ContractVersions\",to_jsonb(v)||jsonb_build_object('Id',{Guid.NewGuid()},'EmployeeId',{f.Employee},'RequestKey',{Guid.NewGuid().ToString()},'Kind','Initial','Revision',1,'PreviousVersionId',NULL))).* FROM \"ContractVersions\" v WHERE \"Id\"={rows[0].Id}"))).SqlState);
    }
}
