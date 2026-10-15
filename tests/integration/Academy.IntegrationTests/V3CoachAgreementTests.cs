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
    private async Task<(HttpClient Owner, Guid Coach, string Email, Guid Employee, Guid Branch, Guid OtherBranch, Guid Sport, DateOnly Today)> AgreementFixture()
    {
        var owner = await Staff(DemoSeed.OwnerEmail); var stamp = Guid.NewGuid().ToString("N"); var email = $"agreement.{stamp}@example.test";
        var coachResponse = await Write(owner, "/api/v1/manage/staff", new StaffWrite("مدرب اتفاق اصطناعي " + stamp[..8], email, "010" + Random.Shared.Next(10000000,99999999), "Demo-Only-123!", AcademyRole.Coach, "مدرب عرض"), false); coachResponse.EnsureSuccessStatusCode();
        var coach = (await coachResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var branches = await db.Branches.Where(b => b.AcademyId == DemoSeed.NogoomAcademyId && b.IsActive).OrderBy(b => b.Id).Take(2).Select(b => b.Id).ToArrayAsync();
        var sport = await db.Sports.Where(s => s.AcademyId == DemoSeed.NogoomAcademyId && s.IsActive).Select(s => s.Id).FirstAsync();
        var employeeResponse = await Write(owner, "/api/v1/manage/employees", new EmployeeWrite("موظف الاتفاق " + stamp[..8], "010" + Random.Shared.Next(10000000,99999999), "مدرب", branches[0], 3000, new(2000,1,1), null, coach, null), false); employeeResponse.EnsureSuccessStatusCode();
        var employee = (await employeeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var data = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/payroll/coaches/{coach}/agreements");
        return (owner, coach, email, employee, branches[0], branches[1], sport, DateOnly.Parse(data.GetProperty("today").GetString()!));
    }
    private static async Task<HttpResponseMessage> AgreementPost(HttpClient client, Guid coach, CoachAgreementWrite body, string? key = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/manage/payroll/coaches/{coach}/agreements") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString()); return await client.SendAsync(request);
    }
    private static async Task<Guid> AgreementId(HttpClient owner, Guid coach, CoachAgreementWrite body)
    { var response = await AgreementPost(owner, coach, body); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); }

    [Fact]
    public async Task V3_agreement_draft_approval_versions_expiry_and_historical_commitment_do_not_rewrite_salary_or_create_money()
    {
        var f = await AgreementFixture(); using var owner = f.Owner;
        var body = new CoachAgreementWrite(f.Branch, f.Sport, f.Today, null, "Percent", 40, "DeliveredCollected", "GroupOnly");
        var key = Guid.NewGuid().ToString(); var created = await AgreementPost(owner, f.Coach, body, key); Assert.Equal(HttpStatusCode.Created, created.StatusCode); var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await AgreementPost(owner, f.Coach, body, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await AgreementPost(owner, f.Coach, body with { VariableValue = 41 }, key)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Null(await CoachAgreementEndpoints.Resolve(db, DemoSeed.NogoomAcademyId, f.Coach, f.Branch, f.Sport, f.Today, DateTimeOffset.UtcNow));
        var url = $"/api/v1/manage/payroll/coaches/{f.Coach}/agreements";
        var decisions = await Task.WhenAll(Write(owner, $"{url}/{id}/approve", new CoachAgreementApprovalWrite(null), false), Write(owner, $"{url}/{id}/approve", new CoachAgreementApprovalWrite(null), false)); Assert.All(decisions, r => r.EnsureSuccessStatusCode());
        Assert.Single(await db.CoachCompensationApprovals.Where(a => a.AgreementId == id).ToArrayAsync());
        var second = await AgreementId(owner, f.Coach, body with { EffectiveFrom = f.Today.AddDays(1), EffectiveTo = f.Today.AddDays(1), VariableValue = 30 });
        (await Write(owner, $"{url}/{second}/approve", new CoachAgreementApprovalWrite(id), false)).EnsureSuccessStatusCode();
        Assert.Equal(40, (await CoachAgreementEndpoints.Resolve(db, DemoSeed.NogoomAcademyId, f.Coach, f.Branch, f.Sport, f.Today, DateTimeOffset.UtcNow))!.VariableValue);
        Assert.Equal(30, (await CoachAgreementEndpoints.Resolve(db, DemoSeed.NogoomAcademyId, f.Coach, f.Branch, f.Sport, f.Today.AddDays(1), DateTimeOffset.UtcNow))!.VariableValue);
        Assert.Null(await CoachAgreementEndpoints.Resolve(db, DemoSeed.NogoomAcademyId, f.Coach, f.Branch, f.Sport, f.Today.AddDays(2), DateTimeOffset.UtcNow));
        var secondApproval = await db.CoachCompensationApprovals.SingleAsync(a => a.AgreementId == second);
        Assert.Equal(id, (await CoachAgreementEndpoints.Resolve(db, DemoSeed.NogoomAcademyId, f.Coach, f.Branch, f.Sport, f.Today.AddDays(1), secondApproval.ApprovedAtUtc.AddTicks(-1)))!.Id);
        Assert.Equal(3000, (await db.Employees.SingleAsync(e => e.Id == f.Employee)).MonthlySalary);
        Assert.False(await db.PayrollAdjustments.AnyAsync(a => a.EmployeeId == f.Employee)); Assert.False(await db.PayrollSettlements.AnyAsync(p => p.EmployeeId == f.Employee));
        Assert.Equal(40, (await db.CoachCompensationAgreements.SingleAsync(a => a.Id == id)).VariableValue);
    }

    [Fact]
    public async Task V3_agreement_scope_explicit_permission_zero_and_self_read_do_not_leak_drafts_or_internal_salary_notes()
    {
        var f = await AgreementFixture(); using var owner = f.Owner; var adminFixture = await CreateAdmin(); using var admin = adminFixture.Admin; using var grantOwner = adminFixture.Owner; using var foreign = await Staff(DemoSeed.FutureOwnerEmail);
        var body = new CoachAgreementWrite(f.Branch, f.Sport, f.Today, null, "Percent", 40, "DeliveredCollected", "GroupOnly", "ملاحظة داخلية خاصة");
        foreach (var invalid in new[] { body with { VariableValue = null }, body with { VariableValue = 101 }, body with { BaseCoverage = "IncludesPrivate" }, body with { EffectiveTo = f.Today.AddDays(-1) } })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await AgreementPost(owner, f.Coach, invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await AgreementPost(owner, f.Coach, body with { EffectiveFrom = f.Today.AddDays(-1) })).StatusCode);
        var zero = await AgreementId(owner, f.Coach, body with { BaseCoverage = "IncludesPrivate", VariableValue = 0 });
        var url = $"/api/v1/manage/payroll/coaches/{f.Coach}/agreements";
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(url)).StatusCode);
        var access = Access(f.Branch, true, true); var grantUrl = $"/api/v1/manage/staff/{adminFixture.Member}/access";
        (await Write(grantOwner, grantUrl, access)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await AgreementPost(admin, f.Coach, body)).StatusCode);
        // Addition approval and salary payment are not agreement permissions.
        (await Write(grantOwner, grantUrl, access with { SensitivePermissions = [AcademyPermissions.PayrollAdditionReview, AcademyPermissions.PayrollPay] })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await AgreementPost(admin, f.Coach, body)).StatusCode);
        (await Write(grantOwner, grantUrl, access with { SensitivePermissions = [AcademyPermissions.CoachAgreementManage] })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await AgreementPost(admin, f.Coach, body with { BranchId = f.OtherBranch })).StatusCode);
        var id = await AgreementId(admin, f.Coach, body); using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services, f.Email);
        var own = await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/payroll/agreements"); Assert.Empty(own.GetProperty("items").EnumerateArray());
        (await Write(admin, $"{url}/{id}/approve", new CoachAgreementApprovalWrite(null), false)).EnsureSuccessStatusCode();
        own = await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/payroll/agreements"); var approved = Assert.Single(own.GetProperty("items").EnumerateArray()); Assert.Equal(id, approved.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, approved.GetProperty("note").ValueKind); Assert.Equal(JsonValueKind.Null, approved.GetProperty("baseSalarySnapshot").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await AgreementPost(coach, f.Coach, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await coach.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/staff/payroll/agreements")).StatusCode);
        Assert.DoesNotContain(zero, own.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("id").GetGuid()));
        (await Write(grantOwner, grantUrl, access with { SensitivePermissions = [] })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await AgreementPost(admin, f.Coach, body)).StatusCode);
    }

    [Fact]
    public async Task V3_agreement_concurrent_stale_approval_changed_salary_and_database_immutability_fail_closed()
    {
        var f = await AgreementFixture(); using var owner = f.Owner; var body = new CoachAgreementWrite(f.Branch, f.Sport, f.Today, null, "PerSession", 125, "Delivered", "GroupOnly");
        var first = await AgreementId(owner, f.Coach, body); var second = await AgreementId(owner, f.Coach, body with { VariableValue = 150 }); var url = $"/api/v1/manage/payroll/coaches/{f.Coach}/agreements";
        (await Write(owner, $"{url}/{first}/approve", new CoachAgreementApprovalWrite(null), false)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"{url}/{second}/approve", new CoachAgreementApprovalWrite(null), false)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var month=new DateOnly(f.Today.Year,f.Today.Month,1);
        (await SalaryPost(owner,f.Employee,SalaryBody(await SalaryTerms(owner,f.Employee),month,3200))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"{url}/{second}/approve", new CoachAgreementApprovalWrite(first), false)).StatusCode);
        var tracked = await db.CoachCompensationAgreements.SingleAsync(a => a.Id == first); tracked.VariableValue = 999;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        var sql = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CoachCompensationAgreements\" SET \"VariableValue\"=999 WHERE \"Id\"={first}")); Assert.Equal("23514", sql.SqlState);
        var deletion = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"CoachCompensationApprovals\" WHERE \"AgreementId\"={first}")); Assert.Equal("23514", deletion.SqlState);
        var binding = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Employees\" SET \"MembershipId\"=NULL WHERE \"Id\"={f.Employee}")); Assert.Equal("23514", binding.SqlState);
        Assert.Equal(125, (await db.CoachCompensationAgreements.SingleAsync(a => a.Id == first)).VariableValue);
        Assert.Equal(1, await db.CoachCompensationApprovals.CountAsync(a => a.EmployeeId == f.Employee));
    }

    [Fact]
    public async Task V3_agreement_seed_is_deterministic_preserves_terms_and_rejects_production()
    {
        await using var demo = factory.WithWebHostBuilder(b => b.UseSetting("Demo:SeedProfile", "Football")); using var client = demo.CreateClient();
        await using var scope = demo.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>(); var options = scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;
        await CoachAgreementDemoSeed.Seed(db, env, options); var count = await db.CoachCompensationAgreements.CountAsync(a => a.AcademyId == FootballDemoSeed.AcademyId);
        await CoachAgreementDemoSeed.Seed(db, env, options); Assert.Equal(count, await db.CoachCompensationAgreements.CountAsync(a => a.AcademyId == FootballDemoSeed.AcademyId));
        Assert.Single(await db.CoachCompensationApprovals.Where(a => a.Id == CoachAgreementDemoSeed.ApprovalId).ToArrayAsync());
        Assert.False(await db.CoachCompensationApprovals.AnyAsync(a => a.AgreementId == CoachAgreementDemoSeed.DraftId));
        var row = await db.CoachCompensationAgreements.AsNoTracking().SingleAsync(a => a.Id == CoachAgreementDemoSeed.AgreementId);
        (await Write(client,"/api/v1/auth/login",new {email="owner.football@example.test",password="Demo-Only-123!"},false)).EnsureSuccessStatusCode();
        var terms=await SalaryTerms(client,row.EmployeeId);var from=DateOnly.Parse(terms.GetProperty("currentMonth").GetString()!).AddMonths(1);
        if(terms.GetProperty("items").GetArrayLength()>0){var latest=DateOnly.Parse(terms.GetProperty("items")[0].GetProperty("effectiveFrom").GetString()!);if(latest>from)from=latest;}
        (await SalaryPost(client,row.EmployeeId,SalaryBody(terms,from,terms.GetProperty("currentSalary").GetDecimal()+1))).EnsureSuccessStatusCode();
        await CoachAgreementDemoSeed.Seed(db,env,options);Assert.Equal(row.BaseSalarySnapshot,await db.CoachCompensationAgreements.Where(a=>a.Id==row.Id).Select(a=>a.BaseSalarySnapshot).SingleAsync());
        var environment = env.EnvironmentName;
        try { env.EnvironmentName = "Production"; await Assert.ThrowsAsync<InvalidOperationException>(() => CoachAgreementDemoSeed.Seed(db, env, options)); }
        finally { env.EnvironmentName = environment; }
        Assert.False(await db.CoachCompensationAgreements.AnyAsync(a => a.Id == row.Id && a.AcademyId == DemoSeed.FutureAcademyId));
    }

    [Fact]
    public async Task V3_agreement_admin_with_second_coach_role_cannot_set_own_rate_or_merge_role_permissions()
    {
        var f = await AgreementFixture(); using var owner = f.Owner;
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var user = await db.AcademyMemberships.Where(m => m.Id == f.Coach).Select(m => m.UserId).SingleAsync();
        var adminId = Guid.NewGuid(); db.AcademyMemberships.Add(new() { Id = adminId, AcademyId = DemoSeed.NogoomAcademyId, UserId = user, Role = AcademyRole.AcademyAdmin, UseActionGrants = true, AllBranches = false, AllSports = true }); await db.SaveChangesAsync();
        (await Write(owner, $"/api/v1/manage/staff/{adminId}/access", Access(f.Branch, true, true) with { SensitivePermissions = [AcademyPermissions.CoachAgreementManage] })).EnsureSuccessStatusCode();
        using var self = await Staff(f.Email); var body = new CoachAgreementWrite(f.Branch, f.Sport, f.Today, null, "Percent", 99, "Delivered", "GroupOnly");
        Assert.Equal(HttpStatusCode.Forbidden, (await AgreementPost(self, f.Coach, body)).StatusCode);
        var draft = await AgreementId(owner, f.Coach, body);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(self, $"/api/v1/manage/payroll/coaches/{f.Coach}/agreements/{draft}/approve", new CoachAgreementApprovalWrite(null), false)).StatusCode);
        Assert.False(await db.CoachCompensationApprovals.AnyAsync(a => a.AgreementId == draft));
    }
}
