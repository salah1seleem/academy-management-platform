using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class PayrollIntegrationTests : IAsyncLifetime
{
    private Factory factory = null!;
    private readonly List<Guid> employees = [], reasons = [], grants = [];
    private readonly DateOnly day = new(2026, 10, 8);
    public Task InitializeAsync() { factory = new(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Separate test database required")); _ = factory.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await db.PayrollAdjustments.Where(x => employees.Contains(x.EmployeeId)).ExecuteDeleteAsync();
        await db.PayrollSettlements.Where(x => employees.Contains(x.EmployeeId)).ExecuteDeleteAsync();
        await db.PayrollAdditionRequests.Where(x => employees.Contains(x.EmployeeId)).ExecuteDeleteAsync();
        await db.Employees.Where(x => employees.Contains(x.Id)).ExecuteDeleteAsync();
        await db.PayrollReasons.Where(x => reasons.Contains(x.Id)).ExecuteDeleteAsync();
        await db.StaffGrants.Where(x => grants.Contains(x.Id)).ExecuteDeleteAsync();
        await factory.DisposeAsync();
    }
    private async Task<HttpClient> Staff(string email = DemoSeed.OwnerEmail)
    { var c = factory.CreateClient(); (await Send(c, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" })).EnsureSuccessStatusCode(); return c; }
    private static async Task<HttpResponseMessage> Send(HttpClient c, string path, object body, string? key = null, bool put = false)
    {
        var csrf = await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); var request = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); if (key is not null) request.Headers.Add("Idempotency-Key", key); return await c.SendAsync(request);
    }
    private async Task<(Guid Id, EmployeeWrite Body)> Employee(HttpClient owner, AcademyRole? role = null, bool partial = false)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var branch = await db.Branches.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.IsActive).Select(x => x.Id).FirstAsync();
        var member = role.HasValue ? await db.AcademyMemberships.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Role == role && x.IsActive).Select(x => (Guid?)x.Id).FirstAsync() : null;
        var random = Random.Shared.Next(1_000_000, 9_999_999);
        var body = new EmployeeWrite("موظف اختبار " + random, "0102" + random, role == AcademyRole.Coach ? "مدرب تجريبي" : "موظف تجريبي", branch, 3100, partial ? new(2026, 10, 10) : new(2026, 10, 1), partial ? new(2026, 10, 20) : null, member, null);
        var response = await Send(owner, "/api/v1/manage/employees/", body); response.EnsureSuccessStatusCode(); var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); employees.Add(id); return (id, body);
    }
    private async Task<Guid> Reason(HttpClient owner)
    { var response = await Send(owner, "/api/v1/manage/payroll/reasons", new { name = "ساعة إضافية اختبار " + Guid.NewGuid() }); response.EnsureSuccessStatusCode(); var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); reasons.Add(id); return id; }
    private static async Task<JsonElement> Row(HttpClient owner, Guid id)
    { var data = await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/payroll/?month=2026-10-01"); return data.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("employeeId").GetGuid() == id); }

    [Fact]
    public async Task Employee_without_login_has_partial_contract_salary_and_editable_profile()
    {
        using var owner = await Staff(); var e = await Employee(owner, partial: true);
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/employees/{e.Id}"); Assert.Equal(JsonValueKind.Null, detail.GetProperty("membershipId").ValueKind);
        var row = await Row(owner, e.Id); Assert.Equal(11, row.GetProperty("contractDays").GetInt32()); Assert.Equal(1100, row.GetProperty("netDue").GetDecimal());
        (await Send(owner, $"/api/v1/manage/employees/{e.Id}", e.Body with { JobTitle = "عامل تجهيز الملاعب" }, put: true)).EnsureSuccessStatusCode();
        Assert.Equal("عامل تجهيز الملاعب", (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/employees/{e.Id}")).GetProperty("jobTitle").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, "/api/v1/manage/employees/", e.Body)).StatusCode); // Duplicate phone.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, $"/api/v1/manage/employees/{e.Id}", e.Body with { MonthlySalary = 12.345m }, put: true)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, $"/api/v1/manage/employees/{e.Id}", e.Body with { ContractEnd = new(2026, 9, 30) }, put: true)).StatusCode);
    }

    [Fact]
    public async Task Pending_rejected_and_approved_claims_daily_deduction_and_notice_have_correct_effects()
    {
        using var owner = await Staff(); using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services);
        var e = await Employee(owner, AcademyRole.Coach); var reason = await Reason(owner);
        var request = new AdditionWrite(reason, 150); var key = Guid.NewGuid().ToString();
        var pending = await Send(coach, "/api/v1/staff/payroll/requests", request, key); pending.EnsureSuccessStatusCode(); var id = (await pending.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(0, (await Row(owner, e.Id)).GetProperty("additions").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await Send(coach, "/api/v1/staff/payroll/requests", request, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(coach, "/api/v1/staff/payroll/requests", request with { Amount = 151 }, key)).StatusCode);
        var review = new AdditionReview(true, day, null);
        (await Send(owner, $"/api/v1/manage/payroll/requests/{id}/review", review)).EnsureSuccessStatusCode();
        (await Send(owner, $"/api/v1/manage/payroll/requests/{id}/review", review)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"/api/v1/manage/payroll/requests/{id}/review", review with { Accept = false })).StatusCode);
        var rejected = await Send(coach, "/api/v1/staff/payroll/requests", new AdditionWrite(reason, 200), Guid.NewGuid().ToString()); rejected.EnsureSuccessStatusCode(); var rejectedId = (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Send(owner, $"/api/v1/manage/payroll/requests/{rejectedId}/review", new AdditionReview(false, null, "غير مستحق تجريبيًا"))).EnsureSuccessStatusCode();
        var dailyBody = new { date = day, status = "Absent", approvedDeduction = 50, reason = "خصم غياب اختبار الرواتب" };
        (await Send(owner, $"/api/v1/manage/staff-attendance/{e.Body.MembershipId}", dailyBody, put: true)).EnsureSuccessStatusCode();
        try
        {
            (await Send(owner, "/api/v1/manage/payroll/adjustments", new AdjustmentWrite(e.Id, "Deduction", 100, day, "خصم يدوي اختبار"), Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
            var row = await Row(owner, e.Id); Assert.Equal(150, row.GetProperty("deductions").GetDecimal()); Assert.Equal(150, row.GetProperty("additions").GetDecimal()); Assert.Equal(3100, row.GetProperty("netDue").GetDecimal());
            var all = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/payroll/adjustments?employeeId={e.Id}&kind=Deduction"); Assert.Equal(150, all.EnumerateArray().Sum(x => x.GetProperty("amount").GetDecimal())); Assert.Contains(all.EnumerateArray(), x => x.GetProperty("source").GetString() == "Attendance");
            var own = await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/payroll/"); Assert.Equal(e.Id, own.GetProperty("employee").GetProperty("id").GetGuid()); Assert.Equal(2, own.GetProperty("requests").GetArrayLength()); Assert.False(own.GetProperty("employee").TryGetProperty("monthlySalary", out _));
            await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Single(await db.PayrollAdjustments.Where(x => x.AdditionRequestId == id).ToListAsync());
            var user = await db.AcademyMemberships.Where(x => x.Id == e.Body.MembershipId).Select(x => x.UserId).SingleAsync(); Assert.True(await db.AcademyNotices.AnyAsync(x => x.UserId == user && x.Message.Contains("تم اعتماد طلب المبلغ الإضافي")));
        }
        finally { await using var scope = factory.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().StaffDailyAttendances.Where(x => x.MembershipId == e.Body.MembershipId && x.WorkDate == day).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task Settlement_is_idempotent_immutable_and_locks_later_financial_adjustments()
    {
        using var owner = await Staff(); var e = await Employee(owner); var key = Guid.NewGuid().ToString(); var body = new AdjustmentWrite(e.Id, "Deduction", 100, day, "خصم تجريبي");
        var responses = await Task.WhenAll(Send(owner, "/api/v1/manage/payroll/adjustments", body, key), Send(owner, "/api/v1/manage/payroll/adjustments", body, key)); Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
        Assert.Equal(100, (await Row(owner, e.Id)).GetProperty("deductions").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, "/api/v1/manage/payroll/adjustments", body with { Amount = 99 }, key)).StatusCode);
        var payKey = Guid.NewGuid().ToString(); var pay = new SalaryPay(new(2026, 10, 1), 3000);
        Assert.All(await Task.WhenAll(Send(owner, $"/api/v1/manage/payroll/{e.Id}/pay", pay, payKey), Send(owner, $"/api/v1/manage/payroll/{e.Id}/pay", pay, payKey)), r => Assert.True(r.IsSuccessStatusCode));
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, "/api/v1/manage/payroll/adjustments", body, Guid.NewGuid().ToString())).StatusCode);
        (await Send(owner, $"/api/v1/manage/employees/{e.Id}", e.Body with { MonthlySalary = 5000 }, put: true)).EnsureSuccessStatusCode();
        var row = await Row(owner, e.Id); Assert.True(row.GetProperty("isPaid").GetBoolean()); Assert.Equal(3100, row.GetProperty("monthlySalary").GetDecimal()); Assert.Equal(3000, row.GetProperty("netDue").GetDecimal());
        var another = await Employee(owner); Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"/api/v1/manage/payroll/{another.Id}/pay", pay, payKey)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, $"/api/v1/manage/payroll/{another.Id}/pay", new SalaryPay(new(2026, 11, 1), 3100), Guid.NewGuid().ToString())).StatusCode);
    }

    [Fact]
    public async Task Salary_confirmation_rejects_changed_amount_without_recording_payment()
    {
        using var owner = await Staff(); var employee = await Employee(owner);
        var expected = (await Row(owner, employee.Id)).GetProperty("netDue").GetDecimal();
        (await Send(owner, "/api/v1/manage/payroll/adjustments", new AdjustmentWrite(employee.Id, "Deduction", 100, day, "خصم بعد عرض الراتب"), Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        var key = Guid.NewGuid().ToString(); var pay = new SalaryPay(new(2026, 10, 1), expected);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, $"/api/v1/manage/payroll/{employee.Id}/pay", pay with { ExpectedNetDue = null }, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"/api/v1/manage/payroll/{employee.Id}/pay", pay, key)).StatusCode);
        Assert.False((await Row(owner, employee.Id)).GetProperty("isPaid").GetBoolean());
        (await Send(owner, $"/api/v1/manage/payroll/{employee.Id}/pay", pay with { ExpectedNetDue = 3000 }, key)).EnsureSuccessStatusCode();
        Assert.True((await Row(owner, employee.Id)).GetProperty("isPaid").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"/api/v1/manage/payroll/{employee.Id}/pay", pay, key)).StatusCode);
    }

    [Fact]
    public async Task Payroll_denies_default_admin_coach_management_and_other_academy_ids_and_csrf()
    {
        using var owner = await Staff(); using var other = await Staff(DemoSeed.FutureOwnerEmail); using var admin = await Staff(DemoSeed.AdminEmail); using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services); var e = await Employee(owner); var reason = await Reason(owner);
        foreach (var client in new[] { admin, coach }) foreach (var path in new[] { "/api/v1/manage/payroll/", "/api/v1/manage/employees/", "/api/v1/manage/payroll/requests" }) Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/manage/employees/{e.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(other, $"/api/v1/manage/employees/{e.Id}", e.Body, put: true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(other, "/api/v1/manage/payroll/adjustments", new AdjustmentWrite(e.Id, "Deduction", 10, day, "اختبار عزل"), Guid.NewGuid().ToString())).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>("/api/v1/manage/employees/")).GetProperty("items").EnumerateArray());
        Assert.DoesNotContain((await other.GetFromJsonAsync<JsonElement>("/api/v1/manage/payroll/reasons")).EnumerateArray(), x => x.GetProperty("id").GetGuid() == reason);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/manage/payroll/adjustments", new AdjustmentWrite(e.Id, "Deduction", 10, day, "اختبار"))).StatusCode);
    }

    [Fact]
    public async Task Delegated_reviewer_cannot_approve_own_claim_and_default_date_is_invalid()
    {
        using var owner = await Staff(); using var admin = await Staff(DemoSeed.AdminEmail); var e = await Employee(owner, AcademyRole.AcademyAdmin); var reason = await Reason(owner);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        foreach (var permission in new[] { AcademyPermissions.PayrollRead, AcademyPermissions.PayrollManage }) { var grant = new StaffGrant { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, MembershipId = e.Body.MembershipId!.Value, Permission = permission, Allowed = true }; db.StaffGrants.Add(grant); grants.Add(grant.Id); } await db.SaveChangesAsync();
        var response = await Send(admin, "/api/v1/staff/payroll/requests", new AdditionWrite(reason, 200), Guid.NewGuid().ToString()); response.EnsureSuccessStatusCode(); var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, $"/api/v1/manage/payroll/requests/{id}/review", new AdditionReview(true, day, null))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, $"/api/v1/manage/payroll/requests/{id}/review", new AdditionReview(true, DateOnly.MinValue, null))).StatusCode);
        (await Send(owner, $"/api/v1/manage/payroll/requests/{id}/review", new AdditionReview(true, day, null))).EnsureSuccessStatusCode();
        Assert.Equal(200, (await Row(owner, e.Id)).GetProperty("additions").GetDecimal());
    }

    private sealed class Factory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b) { b.UseEnvironment("Demo"); b.UseSetting("ConnectionStrings:Default", connection); b.UseSetting("Demo:SeedProfile", "LegacyRegression"); b.UseSetting("Demo:SeedEnabled", "true"); b.UseSetting("Demo:FixedOtpEnabled", "true"); b.UseSetting("Demo:FixedOtp", "246810"); b.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); b.UseSetting("Demo:ReferenceDate", "2026-09-28"); b.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning)); b.ConfigureServices(s => { s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(new TestTime()); }); }
    }
    private sealed class TestTime : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-10-08T06:00:00Z"); }
}
