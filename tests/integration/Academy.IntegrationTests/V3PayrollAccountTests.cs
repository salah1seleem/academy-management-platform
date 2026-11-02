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
    private static string AccountRoot(Guid id) => $"/api/v1/manage/payroll/{id}/account";
    [Fact]
    public async Task V3_account_all_approved_months_prior_remainder_and_partial_payments_match_without_posting_again()
    {
        var f = await AgreementFixture(); using var owner = f.Owner;
        var month = new DateOnly(f.Today.Year, f.Today.Month, 1);
        // 25 months proves the account is not the former latest-24 screen total.
        for (var i = 0; i < 25; i++) (await StatementApprove(owner, f.Employee, month.AddMonths(-i), await StatementView(owner, f.Employee, month.AddMonths(-i)))).EnsureSuccessStatusCode();
        (await StatementPay(owner, f.Employee, month, 2500, 0, 3000)).EnsureSuccessStatusCode();
        (await PrivateFinancialWrite(owner, $"/api/v1/manage/payroll/{f.Employee}/pay", new SalaryPay(month.AddMonths(-25), 3000))).EnsureSuccessStatusCode();
        var root = AccountRoot(f.Employee);
        var response = await owner.GetAsync(root); response.EnsureSuccessStatusCode(); Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var v = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(25, v.GetProperty("items").GetArrayLength());
        Assert.Equal(75000, v.GetProperty("totalDue").GetDecimal()); Assert.Equal(2500, v.GetProperty("totalPaid").GetDecimal()); Assert.Equal(72500, v.GetProperty("remaining").GetDecimal());
        Assert.Equal(72000, v.GetProperty("priorRemaining").GetDecimal()); Assert.Equal(500, v.GetProperty("currentRemaining").GetDecimal()); Assert.Equal(1, v.GetProperty("legacyPaidMonthsExcluded").GetInt32());
        Assert.False(v.GetProperty("openingBalanceVerified").GetBoolean()); Assert.False(v.GetProperty("advancesVerified").GetBoolean());
        var cutoff = Uri.EscapeDataString(v.GetProperty("cutoffUtc").GetString()!); var suffix = $"?asOf={f.Today:yyyy-MM-dd}&cutoffUtc={cutoff}";
        (await StatementPay(owner, f.Employee, month, 500, 2500, 500)).EnsureSuccessStatusCode();
        var frozen = await owner.GetFromJsonAsync<JsonElement>(root + suffix); Assert.Equal(72500, frozen.GetProperty("remaining").GetDecimal()); Assert.Equal(2500, frozen.GetProperty("totalPaid").GetDecimal());
        var csv = await owner.GetAsync(root + "/export" + suffix); csv.EnsureSuccessStatusCode(); Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        var text = await csv.Content.ReadAsStringAsync(); Assert.Contains("\"75000.00\",\"2500.00\",\"72500.00\"", text); Assert.Contains("\"72000.00\"", text);
        var now = await owner.GetFromJsonAsync<JsonElement>(root); Assert.Equal(72000, now.GetProperty("remaining").GetDecimal()); Assert.Equal(0, now.GetProperty("currentRemaining").GetDecimal());
        using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services, f.Email);
        var mine = await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/payroll/account"); Assert.Equal(25, mine.GetProperty("items").GetArrayLength()); Assert.Equal(72000, mine.GetProperty("remaining").GetDecimal());
        Assert.DoesNotContain("مرجع صرف اصطناعي", mine.ToString()); Assert.DoesNotContain("اعتماد اصطناعي داخلي", mine.ToString()); Assert.False(mine.GetProperty("items")[0].TryGetProperty("sources", out _));
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(25, await db.PayrollStatements.CountAsync(s => s.EmployeeId == f.Employee)); Assert.Equal(2, await db.PayrollDisbursements.CountAsync(p => p.EmployeeId == f.Employee)); Assert.Equal(1, await db.PayrollSettlements.CountAsync(p => p.EmployeeId == f.Employee)); Assert.False(await db.PayrollAdjustments.AnyAsync(a => a.EmployeeId == f.Employee));
    }

    [Fact]
    public async Task V3_account_cutoff_excludes_later_approval_unapproved_months_and_invalid_future_dates_handles_Cairo_DST()
    {
        var f = await AgreementFixture(); using var owner = f.Owner; var month = new DateOnly(f.Today.Year, f.Today.Month, 1); var root = AccountRoot(f.Employee);
        var empty = await owner.GetFromJsonAsync<JsonElement>(root); Assert.Equal(0, empty.GetProperty("items").GetArrayLength()); Assert.False(empty.GetProperty("currentMonthApproved").GetBoolean()); Assert.False(empty.GetProperty("openingBalanceVerified").GetBoolean());
        var suffix = $"?asOf={f.Today:yyyy-MM-dd}&cutoffUtc={Uri.EscapeDataString(empty.GetProperty("cutoffUtc").GetString()!)}";
        (await StatementApprove(owner, f.Employee, month.AddMonths(-1), await StatementView(owner, f.Employee, month.AddMonths(-1)))).EnsureSuccessStatusCode();
        var frozen = await owner.GetFromJsonAsync<JsonElement>(root + suffix); Assert.Equal(0, frozen.GetProperty("items").GetArrayLength());
        var current = await owner.GetFromJsonAsync<JsonElement>(root); Assert.Equal(3000, current.GetProperty("remaining").GetDecimal()); Assert.Equal(3000, current.GetProperty("priorRemaining").GetDecimal()); Assert.False(current.GetProperty("currentMonthApproved").GetBoolean());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.GetAsync(root + $"?asOf={f.Today.AddDays(1):yyyy-MM-dd}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.GetAsync(root + $"?asOf={f.Today:yyyy-MM-dd}&cutoffUtc=2000-01-01T00:00:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.GetAsync(root + $"?asOf={f.Today:yyyy-MM-dd}&cutoffUtc=2099-01-01T00:00:00Z")).StatusCode);
        var historical = await owner.GetFromJsonAsync<JsonElement>(root + $"?asOf={f.Today.AddDays(-1):yyyy-MM-dd}"); Assert.Empty(historical.GetProperty("items").EnumerateArray());
        // DST begins at the skipped midnight of Cairo's last April Friday.
        var april = new DateOnly(f.Today.Year - 1, 4, 30); while (april.DayOfWeek != DayOfWeek.Friday) april = april.AddDays(-1);
        var beforeDst = await owner.GetFromJsonAsync<JsonElement>(root + $"?asOf={april.AddDays(-1):yyyy-MM-dd}"); Assert.Equal(new DateTimeOffset(april.ToDateTime(new TimeOnly(1, 0)), TimeSpan.FromHours(3)).ToUniversalTime(), beforeDst.GetProperty("cutoffUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task V3_account_reads_and_exports_are_live_branch_tenant_and_own_scoped_with_no_write_grant()
    {
        var f = await AgreementFixture(); using var owner = f.Owner; var month = new DateOnly(f.Today.Year, f.Today.Month, 1);
        (await StatementApprove(owner, f.Employee, month, await StatementView(owner, f.Employee, month))).EnsureSuccessStatusCode();
        var a = await CreateAdmin(); using var admin = a.Admin; using var grantOwner = a.Owner; var accessPath = $"/api/v1/manage/staff/{a.Member}/access";
        var access = Access(f.Branch, true, true) with { SensitivePermissions = [] }; (await Write(grantOwner, accessPath, access)).EnsureSuccessStatusCode();
        foreach (var suffix in new[] { "", "/export" }) Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(AccountRoot(f.Employee) + suffix)).StatusCode);
        (await Write(grantOwner, accessPath, access with { Branches = [f.OtherBranch] })).EnsureSuccessStatusCode();
        foreach (var suffix in new[] { "", "/export" }) Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(AccountRoot(f.Employee) + suffix)).StatusCode);
        using var foreign = await Staff(DemoSeed.FutureOwnerEmail);
        foreach (var suffix in new[] { "", "/export" }) Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(AccountRoot(f.Employee) + suffix)).StatusCode);
        using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services, f.Email);
        Assert.Equal(HttpStatusCode.Forbidden, (await coach.GetAsync(AccountRoot(f.Employee))).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await coach.GetAsync(AccountRoot(f.Employee) + "/export")).StatusCode);
        var other = await AgreementFixture(); using var otherOwner = other.Owner;
        var mine = await coach.GetFromJsonAsync<JsonElement>($"/api/v1/staff/payroll/account?employeeId={other.Employee}"); Assert.Equal(3000, mine.GetProperty("remaining").GetDecimal());
        (await Write(grantOwner, accessPath, access with { Actions = access.Actions.Select(a => a with { Read=false, Add=false, Edit=false, Delete=false }).ToArray() })).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(AccountRoot(f.Employee))).StatusCode);
        using var guardian = factory.CreateClient(); var challenge = await Write(guardian, "/api/v1/auth/guardian/otp/request", new { phoneNumber = DemoSeed.GuardianPhone }, false); challenge.EnsureSuccessStatusCode();
        (await Write(guardian, "/api/v1/auth/guardian/otp/verify", new { phoneNumber = DemoSeed.GuardianPhone, challengeId = (await challenge.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetGuid(), code = "246810" }, false)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/v1/staff/payroll/account")).StatusCode);
    }

    [Fact]
    public async Task V3_account_csv_escapes_employee_formula_and_does_not_export_internal_source_or_actors()
    {
        var f=await AgreementFixture(); using var owner=f.Owner; var month=new DateOnly(f.Today.Year,f.Today.Month,1);
        (await StatementApprove(owner,f.Employee,month,await StatementView(owner,f.Employee,month))).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var employee=await db.Employees.SingleAsync(e=>e.Id==f.Employee);employee.Name=" =1+1";await db.SaveChangesAsync();
        var csv=await owner.GetStringAsync(AccountRoot(f.Employee)+"/export");Assert.Contains("\"' =1+1\"",csv);Assert.DoesNotContain("اعتماد اصطناعي داخلي",csv);Assert.DoesNotContain("SourceSnapshot",csv);Assert.DoesNotContain(f.Employee.ToString(),csv);
    }

    [Fact]
    public async Task V3_account_demo_seed_is_repeatable_without_cash_and_rejects_production()
    {
        await using var demo=factory.WithWebHostBuilder(b=>b.UseSetting("Demo:SeedProfile","Football"));using var c=demo.CreateClient();await using var scope=demo.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var env=scope.ServiceProvider.GetRequiredService<IHostEnvironment>();var options=scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;var clock=scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var before=await db.PayrollDisbursements.CountAsync(p=>p.EmployeeId==PayrollAccountDemoSeed.EmployeeId);await PayrollAccountDemoSeed.Seed(db,env,options,clock);var ids=await db.PayrollStatements.Where(s=>s.EmployeeId==PayrollAccountDemoSeed.EmployeeId).OrderBy(s=>s.Month).Select(s=>s.Id).ToArrayAsync();Assert.NotEmpty(ids);await PayrollAccountDemoSeed.Seed(db,env,options,clock);Assert.Equal(ids,await db.PayrollStatements.Where(s=>s.EmployeeId==PayrollAccountDemoSeed.EmployeeId).OrderBy(s=>s.Month).Select(s=>s.Id).ToArrayAsync());Assert.Equal(before,await db.PayrollDisbursements.CountAsync(p=>p.EmployeeId==PayrollAccountDemoSeed.EmployeeId));
        var old=env.EnvironmentName;try{env.EnvironmentName="Production";await Assert.ThrowsAsync<InvalidOperationException>(()=>PayrollAccountDemoSeed.Seed(db,env,options,clock));}finally{env.EnvironmentName=old;}
    }
}
