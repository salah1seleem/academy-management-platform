using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2StaffScopeTests
{
    private async Task<(Guid Branch, Guid OtherBranch, Guid Employee, Guid OtherEmployee, Guid Claim, Guid OwnClaim)> PayrollFixture(HttpClient owner, Guid member)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var branches = await db.Branches.Where(b => b.AcademyId == DemoSeed.NogoomAcademyId && b.IsActive).OrderBy(b => b.Id).Take(2).Select(b => b.Id).ToArrayAsync();
        var ids = new List<Guid>();
        foreach (var branch in branches)
        {
            var created = await Write(owner, "/api/v1/manage/employees", new EmployeeWrite("موظف تفويض اصطناعي " + Guid.NewGuid().ToString("N")[..8], "010" + Random.Shared.Next(10000000, 99999999), "عامل عرض", branch, 3100, new(2026, 10, 1), null, null, null), false);
            created.EnsureSuccessStatusCode(); ids.Add((await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }
        var reason = new PayrollReason { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, Name = "حضور مباراة اصطناعية " + Guid.NewGuid().ToString("N")[..8] }; db.PayrollReasons.Add(reason);
        var actor = await db.AcademyMemberships.Where(m => m.Id == member).Select(m => m.UserId).SingleAsync();
        var coach = await db.AcademyMemberships.Where(m => m.AcademyId == DemoSeed.NogoomAcademyId && m.Role == AcademyRole.Coach).Select(m => m.UserId).FirstAsync();
        var claim = Guid.NewGuid(); var own = Guid.NewGuid();
        foreach (var pair in new[] { (claim, coach), (own, actor) })
            db.PayrollAdditionRequests.Add(new() { Id = pair.Item1, AcademyId = DemoSeed.NogoomAcademyId, EmployeeId = ids[0], ReasonId = reason.Id, ReasonName = reason.Name, Amount = 0, Currency = "EGP", Status = "Pending", RequestedByUserId = pair.Item2, IdempotencyKey = pair.Item1.ToString() });
        await db.SaveChangesAsync(); return (branches[0], branches[1], ids[0], ids[1], claim, own);
    }
    private static async Task<HttpResponseMessage> SalaryPayment(HttpClient client, Guid employee, decimal amount)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/manage/payroll/{employee}/pay") { Content = JsonContent.Create(new SalaryPay(new(2026, 10, 1), amount)) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); request.Headers.Add("Idempotency-Key", "v3-pay-" + employee);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task V3_payroll_review_and_payment_are_independent_explicit_grants_with_scope_and_replay_protection()
    {
        var f = await CreateAdmin(); using var owner = f.Owner; using var admin = f.Admin; var p = await PayrollFixture(owner, f.Member);
        var access = Access(p.Branch, add: true, edit: true); var url = $"/api/v1/manage/staff/{f.Member}/access";
        (await Write(owner, url, access)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/manage/payroll?month=2026-10-01")).StatusCode);
        var review = new AdditionReview(true, new(2026, 10, 8), null, 150);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, $"/api/v1/manage/payroll/requests/{p.Claim}/review", review, false)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SalaryPayment(admin, p.Employee, 3100)).StatusCode);
        Assert.DoesNotContain(AcademyPermissions.PayrollPay, (await admin.GetFromJsonAsync<string[]>("/api/v1/my-permissions"))!);
        access = access with { SensitivePermissions = [AcademyPermissions.PayrollAdditionReview] };
        (await Write(owner, url, access)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, $"/api/v1/manage/payroll/requests/{p.OwnClaim}/review", review, false)).StatusCode);
        (await Write(admin, $"/api/v1/manage/payroll/requests/{p.Claim}/review", review, false)).EnsureSuccessStatusCode();
        (await Write(admin, $"/api/v1/manage/payroll/requests/{p.Claim}/review", review, false)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await SalaryPayment(admin, p.Employee, 3250)).StatusCode);
        access = access with { SensitivePermissions = [AcademyPermissions.PayrollPay] };
        (await Write(owner, url, access)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, $"/api/v1/manage/payroll/requests/{p.OwnClaim}/review", review with { Accept = false, Amount = null }, false)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SalaryPayment(admin, p.OtherEmployee, 3100)).StatusCode);
        using var foreign = await Staff(DemoSeed.FutureOwnerEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await SalaryPayment(foreign, p.Employee, 3250)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SalaryPayment(admin, p.Employee, 3100)).StatusCode);
        var payments = await Task.WhenAll(SalaryPayment(admin, p.Employee, 3250), SalaryPayment(admin, p.Employee, 3250)); Assert.All(payments, response => response.EnsureSuccessStatusCode());
        (await Write(owner, url, access with { SensitivePermissions = [] })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await SalaryPayment(admin, p.Employee, 3250)).StatusCode); // even replay requires live authority
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Single(await db.PayrollAdjustments.Where(a => a.AdditionRequestId == p.Claim).ToArrayAsync());
        var settlement = Assert.Single(await db.PayrollSettlements.Where(s => s.EmployeeId == p.Employee).ToArrayAsync()); Assert.Equal(3250, settlement.NetDue);
        Assert.Equal("Pending", await db.PayrollAdditionRequests.Where(r => r.Id == p.OwnClaim).Select(r => r.Status).SingleAsync());
        Assert.False(await db.PayrollSettlements.AnyAsync(s => s.EmployeeId == p.OtherEmployee));
        Assert.Contains("SensitivePermissions", (await db.StaffAccessAudits.Where(a => a.MembershipId == f.Member).OrderByDescending(a => a.CreatedAtUtc).FirstAsync()).After);
        var audit = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/staff-access-audit?allDates=true");
        Assert.Contains(audit.GetProperty("items").EnumerateArray(), a => a.GetProperty("membershipId").GetGuid() == f.Member && a.GetProperty("change").GetString()!.Contains("الرواتب") && !string.IsNullOrEmpty(a.GetProperty("actorName").GetString()));
    }

    [Fact]
    public async Task V3_sensitive_grants_reject_unknown_duplicates_missing_CRUD_narrow_sports_and_additional_contexts()
    {
        var f = await CreateAdmin(); using var owner = f.Owner; using var admin = f.Admin; var p = await PayrollFixture(owner, f.Member);
        var url = $"/api/v1/manage/staff/{f.Member}/access"; var access = Access(p.Branch, add: true, edit: true);
        foreach (var permissions in new[] { new[] { "payroll.arbitrary" }, new[] { AcademyPermissions.PayrollPay, AcademyPermissions.PayrollPay } })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, url, access with { SensitivePermissions = permissions })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, url, Access(p.Branch) with { SensitivePermissions = [AcademyPermissions.PayrollPay] })).StatusCode);
        var sports = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff/{f.Member}/access"); var sport = sports.GetProperty("sports")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, url, access with { AllSports = false, Sports = [sport], SensitivePermissions = [AcademyPermissions.PayrollPay] })).StatusCode);
        var context = await NewContext(owner, f.Member);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, $"/api/v1/manage/staff/{context}/access", access with { AllSports = true, Sports = [], SensitivePermissions = [AcademyPermissions.PayrollPay] })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, url, access with { SensitivePermissions = [AcademyPermissions.PayrollPay] })).StatusCode);
        (await Write(owner, url, access with { SensitivePermissions = [AcademyPermissions.PayrollPay] })).EnsureSuccessStatusCode();
        // Old clients cannot erase a reviewed grant merely by omitting the field.
        (await Write(owner, url, access)).EnsureSuccessStatusCode();
        Assert.Contains(AcademyPermissions.PayrollPay, (await admin.GetFromJsonAsync<string[]>("/api/v1/my-permissions"))!);
        (await Write(owner, url, access with { Actions = Access(p.Branch).Actions })).EnsureSuccessStatusCode();
        Assert.DoesNotContain(AcademyPermissions.PayrollPay, (await admin.GetFromJsonAsync<string[]>("/api/v1/my-permissions"))!); // CRUD never re-enabled by explicit grant
        // A pre-matrix account still needs both legacy read/manage permissions
        // as well as the independent grant. Revoking Read cannot leave Pay alive.
        await using var legacyScope = factory.Services.CreateAsyncScope(); var legacy = legacyScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        (await legacy.AcademyMemberships.SingleAsync(m => m.Id == f.Member)).UseActionGrants = false; await legacy.SaveChangesAsync();
        Assert.Contains(AcademyPermissions.PayrollPay, (await admin.GetFromJsonAsync<string[]>("/api/v1/my-permissions"))!);
        (await legacy.StaffGrants.SingleAsync(g => g.MembershipId == f.Member && g.Permission == AcademyPermissions.PayrollRead)).Allowed = false; await legacy.SaveChangesAsync();
        Assert.DoesNotContain(AcademyPermissions.PayrollPay, (await admin.GetFromJsonAsync<string[]>("/api/v1/my-permissions"))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await SalaryPayment(admin, p.Employee, 3100)).StatusCode);
    }

    [Fact]
    public async Task V3_pending_sensitive_command_rechecks_revoked_permission_and_branch_inside_transaction()
    {
        var f = await CreateAdmin(); using var owner = f.Owner; using var admin = f.Admin; var p = await PayrollFixture(owner, f.Member);
        var access = Access(p.Branch, add: true, edit: true) with { SensitivePermissions = [AcademyPermissions.PayrollPay] }; var url = $"/api/v1/manage/staff/{f.Member}/access";
        (await Write(owner, url, access)).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var user = await db.AcademyMemberships.Where(m => m.Id == f.Member).Select(m => m.UserId).SingleAsync();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, user.ToString()), new("academy_id", DemoSeed.NogoomAcademyId.ToString()), new("membership_id", f.Member.ToString())], "test")) };
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenant>();
        db.BindRequestScope(DemoSeed.NogoomAcademyId, user, f.Member, [p.Branch], null, "payroll", "Add");
        Assert.True(await PayrollSensitiveAccess.Allowed(db, (await tenant.ResolveAsync())!, AcademyPermissions.PayrollPay));
        (await Write(owner, url, access with { SensitivePermissions = [] })).EnsureSuccessStatusCode();
        await using (var tx = await db.Database.BeginTransactionAsync())
            Assert.False(await PayrollSensitiveAccess.LockAndAuthorize(context, tenant, db, AcademyPermissions.PayrollPay));
        (await Write(owner, url, access with { Branches = [p.OtherBranch] })).EnsureSuccessStatusCode();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            Assert.True(await PayrollSensitiveAccess.LockAndAuthorize(context, tenant, db, AcademyPermissions.PayrollPay));
            Assert.False(await PayrollSensitiveAccess.BranchAllowed(db, (await tenant.ResolveAsync())!, p.Branch));
        }
        Assert.False(await db.PayrollSettlements.AnyAsync(s => s.EmployeeId == p.Employee));
    }

    [Fact]
    public async Task V3_inflight_payment_waits_for_grant_lock_and_cannot_commit_after_permission_or_branch_revocation()
    {
        var f = await CreateAdmin(); using var owner = f.Owner; using var admin = f.Admin; var p = await PayrollFixture(owner, f.Member);
        var access = Access(p.Branch, add: true, edit: true) with { SensitivePermissions = [AcademyPermissions.PayrollPay] };
        foreach (var revokeBranch in new[] { false, true })
        {
            (await Write(owner, $"/api/v1/manage/staff/{f.Member}/access", access)).EnsureSuccessStatusCode();
            await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(); var key = $"grants:{DemoSeed.NogoomAcademyId}:{f.Member}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))");
            var pending = SalaryPayment(admin, p.Employee, 3100);
            var blocked = false;
            for (var i = 0; i < 200 && !blocked && !pending.IsCompleted; i++)
            {
                blocked = await db.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM pg_locks WHERE locktype='advisory' AND NOT granted AND classid=((hashtextextended({key},0)>>32)&4294967295)::oid AND objid=(hashtextextended({key},0)&4294967295)::oid) AS \"Value\"").SingleAsync();
                if (!blocked) await Task.Delay(25);
            }
            Assert.True(blocked, "The real HTTP payment must reach its transactional authority lock before revocation.");
            if (revokeBranch)
                (await db.MembershipBranchAccesses.SingleAsync(a => a.MembershipId == f.Member && a.BranchId == p.Branch)).IsActive = false;
            else
                (await db.StaffGrants.SingleAsync(g => g.MembershipId == f.Member && g.Permission == AcademyPermissions.PayrollPay)).Allowed = false;
            await db.SaveChangesAsync(); await tx.CommitAsync();
            Assert.Equal(HttpStatusCode.Forbidden, (await pending.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode);
            Assert.False(await db.PayrollSettlements.AnyAsync(s => s.EmployeeId == p.Employee));
        }
    }
}
