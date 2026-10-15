using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Demo;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2StaffScopeTests
{
    private static Task<JsonElement> CorrectionQuote(HttpClient c, string path, DateOnly? month = null) => c.GetFromJsonAsync<JsonElement>(path + "/correction-preview" + (month.HasValue ? $"?month={month:yyyy-MM-dd}" : ""));
    private static Task<HttpResponseMessage> CorrectionPost(HttpClient c, string path, JsonElement q, string? key = null) => StatementCommand(c, path + "/corrections", new PrivateCorrectionWrite(DateOnly.Parse(q.GetProperty("month").GetString()!), q.GetProperty("quoteHash").GetString()!, "ملاحظة مراجعة مالية داخلية اصطناعية"), key);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task V3_private_correction_after_approval_or_payout_is_reviewed_once_in_selected_month_without_rewriting_original(bool fullyPaid)
    {
        var f = await PrivateFixture(isolatedBranch: true); using var owner = f.Owner;
        var path = (await PrivateBindUnits(owner, f.Coach, f.Document, f.Sessions[0])).Single();
        await PrivatePayment(owner, f.Document, f.Enrollment, f.Plan, f.Actor, 2000);
        (await PrivatePost(owner, path, await PrivatePreview(owner, path))).EnsureSuccessStatusCode();
        var month = new DateOnly(f.Today.Year, f.Today.Month, 1);
        (await StatementApprove(owner, f.Employee, month, await StatementView(owner, f.Employee, month))).EnsureSuccessStatusCode();
        if (fullyPaid) (await StatementPay(owner, f.Employee, month, 3100, 0, 3100)).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var original = await db.PayrollStatements.AsNoTracking().SingleAsync(s => s.EmployeeId == f.Employee);
        var collections = await db.Collections.CountAsync(); var payouts = await db.PayrollDisbursements.CountAsync();
        (await PrivateFinancialWrite(owner, $"/api/v1/finance/receivables/{f.Document}/movements", new { kind = "CreditNote", amount = 400, reason = "مراجعة تخفيض بعد اعتماد عمولة الخاص" })).EnsureSuccessStatusCode();
        Assert.False((await CorrectionQuote(owner, path)).GetProperty("canCorrect").GetBoolean());
        (await PrivatePost(owner, path, await PrivatePreview(owner, path))).EnsureSuccessStatusCode();
        var closed = await CorrectionQuote(owner, path, month); Assert.False(closed.GetProperty("canCorrect").GetBoolean()); Assert.Equal(HttpStatusCode.Conflict, (await CorrectionPost(owner, path, closed)).StatusCode);
        var q = await CorrectionQuote(owner, path); Assert.True(q.GetProperty("canCorrect").GetBoolean()); Assert.Equal(month.AddMonths(1).ToString("yyyy-MM-dd"), q.GetProperty("month").GetString()); Assert.Equal(-20, q.GetProperty("difference").GetDecimal());
        var key = Guid.NewGuid().ToString();
        var results = await Task.WhenAll(CorrectionPost(owner, path, q, key), CorrectionPost(owner, path, q, key));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created); Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, (await CorrectionPost(owner, path, q)).StatusCode);
        Assert.Equal("None", (await PrivatePreview(owner, path)).GetProperty("action").GetString());
        var next = await PrivatePayroll(owner, f.Employee, month.AddMonths(1));
        Assert.Equal(3000, next.GetProperty("monthlySalary").GetDecimal()); Assert.Equal(20, next.GetProperty("deductions").GetDecimal()); Assert.Equal(-20, next.GetProperty("privateCompensation").GetDecimal()); Assert.Equal(2980, next.GetProperty("netDue").GetDecimal());
        var current = await PrivatePayroll(owner, f.Employee, month);
        Assert.Equal(3100, current.GetProperty("netDue").GetDecimal()); Assert.Equal(fullyPaid ? 3100 : 0, current.GetProperty("paidAmount").GetDecimal()); Assert.Equal(0, current.GetProperty("deductions").GetDecimal());
        var preserved = await db.PayrollStatements.AsNoTracking().SingleAsync(s => s.Id == original.Id);
        Assert.Equal(original.SourceSnapshot, preserved.SourceSnapshot); Assert.Equal(original.SourceHash, preserved.SourceHash);
        Assert.Equal(collections, await db.Collections.CountAsync()); Assert.Equal(payouts, await db.PayrollDisbursements.CountAsync()); Assert.False(await db.PayrollSettlements.AnyAsync(s => s.EmployeeId == f.Employee));
        var entry = await db.PrivateCompensationEntries.AsNoTracking().SingleAsync(e => e.EmployeeId == f.Employee && e.Kind == "Correction");
        var review = await db.PrivateCompensationEntries.SingleAsync(e => e.Id == entry.ReviewEntryId);
        Assert.Equal(review.FactsHash, entry.FactsHash); Assert.Equal(-20, entry.Delta); Assert.Equal(3, entry.Revision);
        Assert.Equal(month.AddMonths(1), entry.DueDate); Assert.Equal(2, await db.PayrollAdjustments.CountAsync(a => a.EmployeeId == f.Employee));
        Assert.Equal("23514", (await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PayrollAdjustments\" SET \"Amount\"=1 WHERE \"Id\"={entry.PayrollAdjustmentId}"))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PrivateCompensationEntries\" WHERE \"Id\"={entry.Id}"))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PrivateCompensationEntries\" SELECT (jsonb_populate_record(NULL::\"PrivateCompensationEntries\",to_jsonb(e)||jsonb_build_object('Id',{Guid.NewGuid()},'RequestKey',{Guid.NewGuid().ToString()},'Revision',4,'PostedBefore',80,'TargetAmount',60,'Delta',-20))).* FROM \"PrivateCompensationEntries\" e WHERE \"Id\"={entry.Id}"))).SqlState);
        using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services, f.Email);
        var own = (await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/payroll/private-services")).GetProperty("items")[0];
        Assert.Equal(80, own.GetProperty("postedAmount").GetDecimal()); Assert.Equal(-20, own.GetProperty("corrections")[0].GetProperty("delta").GetDecimal()); Assert.Equal(month.AddMonths(1).ToString("yyyy-MM-dd"), own.GetProperty("corrections")[0].GetProperty("dueDate").GetString());
        var ownJson = own.GetRawText(); Assert.DoesNotContain("ملاحظة مراجعة", ownJson); Assert.False(own.TryGetProperty("player", out _)); Assert.False(own.TryGetProperty("actor", out _));
        var account = await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/payroll/account"); Assert.Equal(3100, account.GetProperty("totalDue").GetDecimal()); Assert.Equal(fullyPaid ? 0 : 3100, account.GetProperty("remaining").GetDecimal());
        // Scheduling is not approval of a future statement, and does not alter current owner totals.
        var summary = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-summary?allDates=true&branchId={f.Branch}");
        Assert.Equal(3100, summary.GetProperty("payroll").GetProperty("netDue").GetDecimal());
        var future = await StatementView(owner, f.Employee, month.AddMonths(1)); Assert.False(future.GetProperty("canApprove").GetBoolean());
    }

    [Fact]
    public async Task V3_private_correction_does_not_schedule_a_salary_deduction_outside_the_employee_contract()
    {
        var f = await PrivateFixture(); using var owner = f.Owner;
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var employee = await db.Employees.SingleAsync(e => e.Id == f.Employee); employee.ContractEnd = f.Today; await db.SaveChangesAsync();
        var path = (await PrivateBindUnits(owner, f.Coach, f.Document, f.Sessions[0])).Single();
        await PrivatePayment(owner, f.Document, f.Enrollment, f.Plan, f.Actor, 2000);
        (await PrivatePost(owner, path, await PrivatePreview(owner, path))).EnsureSuccessStatusCode();
        var month = new DateOnly(f.Today.Year, f.Today.Month, 1);
        (await StatementApprove(owner, f.Employee, month, await StatementView(owner, f.Employee, month))).EnsureSuccessStatusCode();
        (await PrivateFinancialWrite(owner, $"/api/v1/finance/receivables/{f.Document}/movements", new { kind = "CreditNote", amount = 400, reason = "مراجعة بعد اعتماد آخر شهر عقد" })).EnsureSuccessStatusCode();
        (await PrivatePost(owner, path, await PrivatePreview(owner, path))).EnsureSuccessStatusCode();
        var q = await CorrectionQuote(owner, path); Assert.False(q.GetProperty("canCorrect").GetBoolean()); Assert.Contains("نهاية الخدمة", q.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await CorrectionPost(owner, path, q)).StatusCode);
        Assert.Single(await db.PayrollAdjustments.Where(a => a.EmployeeId == f.Employee).ToArrayAsync());
        Assert.False(await db.PrivateCompensationEntries.AnyAsync(e => e.EmployeeId == f.Employee && e.Kind == "Correction"));
    }

    [Fact]
    public async Task V3_private_correction_rechecks_sources_blocks_invalid_period_and_never_grants_coach_or_admin_financial_authority()
    {
        var f = await PrivateFixture(); using var owner = f.Owner;
        var path = (await PrivateBindUnits(owner, f.Coach, f.Document, f.Sessions[0])).Single();
        await PrivatePayment(owner, f.Document, f.Enrollment, f.Plan, f.Actor, 2000);
        (await PrivatePost(owner, path, await PrivatePreview(owner, path))).EnsureSuccessStatusCode();
        var month = new DateOnly(f.Today.Year, f.Today.Month, 1);
        (await PrivateFinancialWrite(owner, $"/api/v1/manage/payroll/{f.Employee}/pay", new SalaryPay(month, 3100))).EnsureSuccessStatusCode();
        (await PrivateFinancialWrite(owner, $"/api/v1/finance/receivables/{f.Document}/movements", new { kind = "CreditNote", amount = 400, reason = "مراجعة بعد سداد قديم" })).EnsureSuccessStatusCode();
        (await PrivatePost(owner, path, await PrivatePreview(owner, path))).EnsureSuccessStatusCode(); var q = await CorrectionQuote(owner, path);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path + "/corrections", new PrivateCorrectionWrite(month.AddMonths(1), q.GetProperty("quoteHash").GetString()!))).StatusCode);
        foreach (var invalid in new[] { month.AddDays(1), month.AddMonths(-1), month.AddMonths(13) })
        { var bad = await CorrectionQuote(owner, path, invalid); Assert.False(bad.GetProperty("canCorrect").GetBoolean()); Assert.Equal(HttpStatusCode.Conflict, (await CorrectionPost(owner, path, bad)).StatusCode); }
        var adminFixture = await CreateAdmin(); using var admin = adminFixture.Admin; using var grantOwner = adminFixture.Owner;
        (await Write(grantOwner, $"/api/v1/manage/staff/{adminFixture.Member}/access", Access(f.Branch, true, true) with { SensitivePermissions = [AcademyPermissions.PayrollPay, AcademyPermissions.CoachAgreementManage, AcademyPermissions.PayrollStatementApprove] })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path + "/correction-preview")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await CorrectionPost(admin, path, q)).StatusCode);
        using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services, f.Email); Assert.Equal(HttpStatusCode.Forbidden, (await CorrectionPost(coach, path, q)).StatusCode);
        using var foreign = await Staff(DemoSeed.FutureOwnerEmail); Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(path + "/correction-preview")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await CorrectionPost(foreign, path, q)).StatusCode);
        (await PrivateFinancialWrite(owner, $"/api/v1/finance/receivables/{f.Document}/movements", new { kind = "CreditNote", amount = 400, reason = "تغير جديد يحتاج مراجعة مستقلة" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await CorrectionPost(owner, path, q)).StatusCode);
        (await PrivatePost(owner, path, await PrivatePreview(owner, path))).EnsureSuccessStatusCode();
        var fresh = await CorrectionQuote(owner, path); Assert.Equal(-40, fresh.GetProperty("difference").GetDecimal());
        (await CorrectionPost(owner, path, fresh)).EnsureSuccessStatusCode();
        Assert.Equal(3100, (await PrivatePayroll(owner, f.Employee, month)).GetProperty("netDue").GetDecimal());
    }
}
