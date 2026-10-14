using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Attendance;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed partial class V2StaffScopeTests : IAsyncLifetime
{
    private Factory factory = null!;
    public Task InitializeAsync() { factory = new(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Separate test database required")); _ = factory.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await factory.DisposeAsync();
    private async Task<(HttpClient Owner, HttpClient Admin, Guid Member)> CreateAdmin()
    {
        var owner = await Staff(DemoSeed.OwnerEmail); var email = $"scope.{Guid.NewGuid():N}@example.test";
        var result = await Write(owner, "/api/v1/manage/staff", new StaffWrite("إداري اختبار الفروع", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, "اختبار فقط"), false);
        result.EnsureSuccessStatusCode(); var id = (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        return (owner, await Staff(email), id);
    }
    private static StaffAccessWrite Access(Guid branch, bool add = false, bool edit = false) => new(false, [branch], StaffAccessRules.Modules.Select(m => new StaffActionValue(m, true, add, edit, false)).ToArray());
    private async Task<(Guid A, Guid B, Guid GroupA, Guid GroupB, Guid PlayerB)> Data()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var academy = DemoSeed.NogoomAcademyId; var now = DateTimeOffset.UtcNow;
        var sport = await db.Sports.FirstAsync(x => x.AcademyId == academy && x.IsActive);
        var age = await db.AgeCategories.FirstAsync(x => x.AcademyId == academy && x.IsActive);
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var ga = Guid.NewGuid(); var gb = Guid.NewGuid(); var pb = Guid.NewGuid();
        foreach (var pair in new[] { (a, ga, Guid.NewGuid()), (b, gb, pb) })
        {
            db.Branches.Add(new() { Id = pair.Item1, AcademyId = academy, ArabicName = "فرع اختبار نطاق " + pair.Item1.ToString("N")[..10], CreatedAtUtc = now, UpdatedAtUtc = now });
            db.TrainingGroups.Add(new() { Id = pair.Item2, AcademyId = academy, BranchId = pair.Item1, SportId = sport.Id, AgeCategoryId = age.Id, Capacity = 20, ArabicName = "مجموعة اختبار نطاق " + pair.Item2.ToString("N")[..10], CreatedAtUtc = now, UpdatedAtUtc = now });
            db.Players.Add(new() { Id = pair.Item3, AcademyId = academy, PlayerCode = "V2-" + pair.Item3.ToString("N")[..12], ArabicName = "لاعب اختبار اصطناعي", DateOfBirth = new DateOnly(2016, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now });
            db.SportEnrollments.Add(new() { Id = Guid.NewGuid(), AcademyId = academy, PlayerId = pair.Item3, TrainingGroupId = pair.Item2, BranchId = pair.Item1, SportId = sport.Id, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.TrainingSessions.Add(new() { Id = Guid.NewGuid(), AcademyId = academy, TrainingGroupId = pair.Item2, BranchId = pair.Item1, SportId = sport.Id, AgeCategoryId = age.Id, SessionDate = new DateOnly(2026, 9, 28), StartTime = new TimeOnly(16, 0), EndTime = new TimeOnly(17, 0), Source = TrainingSessionSource.Manual, CreatedByUserId = PlatformActor(), CreatedAtUtc = now, UpdatedAtUtc = now });
        }
        // Creator is an existing synthetic account, never real people.
        Guid PlatformActor() => db.Users.Where(x => x.Email == DemoSeed.OwnerEmail).Select(x => x.Id).Single();
        await db.SaveChangesAsync(); return (a, b, ga, gb, pb);
    }

    [Fact]
    public async Task Newly_provisioned_admin_is_default_deny_until_owner_assigns_explicit_scope_and_actions()
    {
        var fixture = await CreateAdmin(); using var owner = fixture.Owner; using var admin = fixture.Admin;
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/people/players")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/reports/financial")).StatusCode);
        var access = await admin.GetFromJsonAsync<JsonElement>("/api/v1/my-access");
        Assert.False(access.GetProperty("allBranches").GetBoolean()); Assert.True(access.GetProperty("useActionGrants").GetBoolean());
        Assert.Empty(access.GetProperty("branches").EnumerateArray()); Assert.Empty(access.GetProperty("actions").EnumerateArray());
    }

    [Fact]
    public async Task Branch_scope_filters_lists_nested_profiles_sessions_reports_and_rejects_IDOR()
    {
        var fixture = await CreateAdmin(); using var owner = fixture.Owner; using var admin = fixture.Admin;
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var data = await Data(); var branches = new[] { data.A, data.B };
        var foreignGroup = await db.TrainingGroups.SingleAsync(x => x.Id == data.GroupB);
        var foreignPlayer = data.PlayerB;
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", Access(branches[0]))).EnsureSuccessStatusCode();
        var options = await admin.GetFromJsonAsync<JsonElement>("/api/v1/manage/structure/options");
        Assert.Equal([branches[0]], options.GetProperty("branches").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray());
        Assert.DoesNotContain(options.GetProperty("groups").EnumerateArray(), x => x.GetProperty("branchId").GetGuid() == branches[1]);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/manage/structure/groups/{foreignGroup.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/people/players/{foreignPlayer}")).StatusCode);
        var players = await admin.GetFromJsonAsync<JsonElement>("/api/v1/people/players");
        Assert.DoesNotContain(players.EnumerateArray(), x => x.GetProperty("id").GetGuid() == foreignPlayer);
        var sessions = await admin.GetFromJsonAsync<JsonElement>("/api/v1/attendance/sessions?allDates=true");
        var allowedSessions = await db.TrainingSessions.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.BranchId == branches[0]).Select(x => x.Id).ToArrayAsync();
        Assert.All(sessions.EnumerateArray(), x => Assert.Contains(x.GetProperty("id").GetGuid(), allowedSessions));
        var financial = await admin.GetAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"); financial.EnsureSuccessStatusCode();
        Assert.DoesNotContain(foreignPlayer.ToString(), await financial.Content.ReadAsStringAsync());
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", Access(branches[1]))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/manage/structure/groups/{foreignGroup.Id}")).StatusCode); // same open session, next request
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/manage/structure/groups/{foreignGroup.Id}")).StatusCode);
    }

    [Fact]
    public async Task Read_add_edit_are_independent_and_scope_writes_cannot_reference_another_branch()
    {
        var fixture = await CreateAdmin(); using var owner = fixture.Owner; using var admin = fixture.Admin;
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var data = await Data();
        var branches = new[] { await db.Branches.SingleAsync(x => x.Id == data.A), await db.Branches.SingleAsync(x => x.Id == data.B) };
        var group = await db.TrainingGroups.FirstAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.BranchId == branches[0].Id);
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", Access(branches[0].Id))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, "/api/v1/manage/branches", new { arabicName = "فرع غير مصرح" }, false)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, $"/api/v1/manage/structure/groups/{group.Id}/status", new { isActive = false })).StatusCode);
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", Access(branches[0].Id, add: true))).EnsureSuccessStatusCode();
        var category = await db.AgeCategories.FirstAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.IsActive);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(admin, "/api/v1/manage/groups", new { arabicName = "تلاعب فرع", branchId = branches[1].Id, sportId = group.SportId, ageCategoryId = category.Id, capacity = 10 }, false)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Write(admin, "/api/v1/manage/groups", new { arabicName = "مجموعة داخل النطاق " + Guid.NewGuid().ToString("N")[..10], branchId = branches[0].Id, sportId = group.SportId, ageCategoryId = category.Id, capacity = 10 }, false)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, $"/api/v1/manage/structure/groups/{group.Id}/status", new { isActive = false })).StatusCode);
        var invalid = Access(branches[0].Id) with { Branches = [Guid.NewGuid()] };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, $"/api/v1/manage/staff/{fixture.Member}/access", Access(branches[1].Id))).StatusCode);
        Assert.True(await db.StaffAccessAudits.AnyAsync(x => x.MembershipId == fixture.Member && x.AcademyId == DemoSeed.NogoomAcademyId));
    }

    [Fact]
    public async Task Unassigned_coach_created_by_branch_admin_remains_visible_only_in_the_provisioning_branch()
    {
        var fixture = await CreateAdmin(); using var owner = fixture.Owner; using var admin = fixture.Admin;
        var data = await Data();
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", Access(data.A, add: true))).EnsureSuccessStatusCode();
        var created = await Write(admin, "/api/v1/manage/coaches", new { displayName = "مدرب اصطناعي بدون إسناد", phoneNumber = "010" + Random.Shared.Next(10000000, 99999999), isActive = true, groupIds = Array.Empty<Guid>() }, false);
        created.EnsureSuccessStatusCode(); var coach = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("membershipId").GetGuid();
        var options = await admin.GetFromJsonAsync<JsonElement>("/api/v1/manage/structure/options");
        Assert.Contains(options.GetProperty("coaches").EnumerateArray(), x => x.GetProperty("id").GetGuid() == coach);
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", Access(data.B))).EnsureSuccessStatusCode();
        options = await admin.GetFromJsonAsync<JsonElement>("/api/v1/manage/structure/options");
        Assert.DoesNotContain(options.GetProperty("coaches").EnumerateArray(), x => x.GetProperty("id").GetGuid() == coach);
    }

    [Fact]
    public async Task Db_scope_protects_unqualified_queries_and_tracked_cross_scope_writes_without_pool_state_leakage()
    {
        await using var first = factory.Services.CreateAsyncScope(); var db = first.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var branches = await db.Branches.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.IsActive).Take(2).Select(x => x.Id).ToArrayAsync();
        db.BindRequestScope(DemoSeed.NogoomAcademyId, Guid.NewGuid(), Guid.NewGuid(), [branches[0]]);
        Assert.All(await db.SportEnrollments.ToListAsync(), x => { Assert.Equal(DemoSeed.NogoomAcademyId, x.AcademyId); Assert.Equal(branches[0], x.BranchId); });
        Assert.All(await db.TrainingGroups.ToListAsync(), x => Assert.Equal(branches[0], x.BranchId));
        db.Branches.Add(new Branch { Id = Guid.NewGuid(), AcademyId = DemoSeed.FutureAcademyId, ArabicName = "مرفوض", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow });
        await Assert.ThrowsAsync<ResourceScopeException>(() => db.SaveChangesAsync());
        await using var second = factory.Services.CreateAsyncScope(); var other = second.ServiceProvider.GetRequiredService<FoundationDbContext>();
        other.BindRequestScope(DemoSeed.FutureAcademyId, Guid.NewGuid(), Guid.NewGuid(), null);
        Assert.All(await other.Branches.ToListAsync(), x => Assert.Equal(DemoSeed.FutureAcademyId, x.AcademyId));
        Assert.False(await other.Branches.AnyAsync(x => x.Id == branches[0]));
    }
    [Fact]
    public async Task V3_templates_fill_existing_grants_without_role_escalation_and_preserve_branch_enforcement()
    {
        var fixture = await CreateAdmin(); using var owner = fixture.Owner; using var admin = fixture.Admin;
        var data = await Data();
        var page = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff/{fixture.Member}/access");
        Assert.Equal(5, page.GetProperty("templates").GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/v1/manage/staff/{fixture.Member}/access")).StatusCode);
        var preset = StaffAccessTemplates.All.Single(x => x.Key == "branch-operator");
        Assert.All(StaffAccessTemplates.All.SelectMany(x => x.Actions), x => Assert.False(x.Delete));
        Assert.DoesNotContain(StaffAccessTemplates.All.SelectMany(x => x.Actions), x => x.Module is "staff" or "settings" or "medical" or "payroll" && x.Read);
        var write = new StaffAccessWrite(false, [data.A], preset.Actions, preset.Key);
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", write)).EnsureSuccessStatusCode();
        var options = await admin.GetFromJsonAsync<JsonElement>("/api/v1/manage/structure/options");
        Assert.Equal([data.A], options.GetProperty("branches").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/people/players/{data.PlayerB}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/reports/financial")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/manage/payroll")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", write with { TemplateKey = "owner" })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(AcademyRole.AcademyAdmin, (await db.AcademyMemberships.SingleAsync(x => x.Id == fixture.Member)).Role);
        var audit = await db.StaffAccessAudits.Where(x => x.MembershipId == fixture.Member).Select(x => x.After).SingleAsync();
        using var auditJson = JsonDocument.Parse(audit);
        Assert.Equal("branch-operator", auditJson.RootElement.GetProperty("TemplateKey").GetString());
    }
    private async Task<HttpClient> Staff(string email) { var c = factory.CreateClient(); (await Write(c, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" }, false)).EnsureSuccessStatusCode(); return c; }
    private static async Task<HttpResponseMessage> Write(HttpClient c, string path, object body, bool put = true) { var csrf = await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); var r = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(body) }; r.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); return await c.SendAsync(r); }
    private sealed class Factory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b) { b.UseEnvironment("Demo"); b.UseSetting("ConnectionStrings:Default", connection); b.UseSetting("Demo:SeedProfile", "LegacyRegression"); b.UseSetting("Demo:SeedEnabled", "true"); b.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); b.UseSetting("Demo:FixedOtpEnabled", "true"); b.UseSetting("Demo:FixedOtp", "246810"); b.UseSetting("Demo:ReferenceDate", "2026-09-28"); }
    }
}
