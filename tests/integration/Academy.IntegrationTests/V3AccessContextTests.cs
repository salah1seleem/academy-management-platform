using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Importing;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;
using Academy.Infrastructure.Identity;

namespace Academy.IntegrationTests;

public sealed partial class V2StaffScopeTests
{
    private async Task<Guid> NewContext(HttpClient owner, Guid primary, string? name = null)
    {
        var created = await Write(owner, $"/api/v1/manage/staff/{primary}/contexts", new StaffAccessContextWrite(name ?? "سياق اصطناعي " + Guid.NewGuid().ToString("N")[..8]), false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private async Task<HttpResponseMessage> SetContext(HttpClient owner, Guid primary, Guid context, bool active)
    {
        var list = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff/{primary}/contexts");
        var row = list.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == context);
        return await Write(owner, $"/api/v1/manage/staff/{context}/context", new StaffAccessContextWrite(row.GetProperty("name").GetString()!, active, row.GetProperty("revision").GetInt32()));
    }
    private static async Task<HttpResponseMessage> SelectContext(HttpClient client, Guid academy, Guid member)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/session/academy") { Content = JsonContent.Create(new { academyId = academy, membershipId = member }) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); return await client.SendAsync(request);
    }
    [Fact]
    public async Task V3_context_owner_review_is_required_and_cannot_create_user_employee_or_owner_role()
    {
        var f = await CreateAdmin(); using var owner = f.Owner; using var admin = f.Admin;
        using var foreign = await Staff(DemoSeed.FutureOwnerEmail); using var anonymous = factory.CreateClient();
        var url = $"/api/v1/manage/staff/{f.Member}/contexts";
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, url, new StaffAccessContextWrite("مرفوض"), false)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(foreign, url, new StaffAccessContextWrite("مرفوض"), false)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(url, new StaffAccessContextWrite("دون حماية"))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, url, new StaffAccessContextWrite("فوري", true), false)).StatusCode);
        var name = "مالك بالاسم فقط " + Guid.NewGuid().ToString("N")[..8];
        var id = await NewContext(owner, f.Member, name);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, url, new StaffAccessContextWrite(name), false)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SetContext(owner, f.Member, id, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SelectContext(admin, DemoSeed.NogoomAcademyId, id)).StatusCode);
        var listResponse = await owner.GetAsync(url); Assert.Equal("no-store", listResponse.Headers.CacheControl!.ToString());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var root = await db.AcademyMemberships.SingleAsync(m => m.Id == f.Member); var context = await db.AcademyMemberships.SingleAsync(m => m.Id == id);
        Assert.Equal(root.UserId, context.UserId); Assert.Equal(root.AcademyId, context.AcademyId); Assert.Equal(AcademyRole.AcademyAdmin, context.Role);
        Assert.False(context.IsActive); Assert.False(context.AllBranches); Assert.False(context.AllSports); Assert.Empty(await db.StaffActionGrants.Where(g => g.MembershipId == id).ToArrayAsync());
        Assert.False(await db.Employees.AnyAsync(e => e.MembershipId == id)); Assert.Contains("operational-access-context", (await db.StaffAccessAudits.SingleAsync(a => a.MembershipId == id)).After);
        var staff = await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/staff"); Assert.DoesNotContain(staff.GetProperty("members").EnumerateArray(), m => m.GetProperty("id").GetGuid() == id);
        var accounts = await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/employees"); Assert.DoesNotContain(accounts.GetProperty("accounts").EnumerateArray(), m => m.GetProperty("id").GetGuid() == id);
        var rejected = await Assert.ThrowsAsync<ImportRejected>(() => ImportAuthority.Check(db, context.AcademyId, context.UserId, context.Id, false)); Assert.Equal("authority", rejected.Code);
        var ownerMember = await db.AcademyMemberships.FirstAsync(m => m.AcademyId == root.AcademyId && m.Role == AcademyRole.AcademyOwner);
        var ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AcademyMemberships\" SET \"PrimaryMembershipId\"={ownerMember.Id} WHERE \"Id\"={id}")); Assert.Equal("23514", ex.SqlState);
    }
    [Fact]
    public async Task V3_two_admin_contexts_do_not_multiply_roles_branches_sports_or_writes_and_revoke_live()
    {
        var f = await CreateAdmin(); using var owner = f.Owner; using var admin = f.Admin; var data = await SportsData(); var id = await NewContext(owner, f.Member);
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var age = await db.TrainingGroups.Where(g => g.Id == data.Group).Select(g => g.AgeCategoryId).SingleAsync();
            data.OtherGroup = Guid.NewGuid(); data.OtherPlayer = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            db.TrainingGroups.Add(new TrainingGroup { Id = data.OtherGroup, AcademyId = DemoSeed.NogoomAcademyId, BranchId = data.OtherBranch, SportId = data.OtherSport, AgeCategoryId = age, ArabicName = "مجموعة السياق الثاني " + data.OtherGroup.ToString("N")[..8], Capacity = 20, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.Players.Add(new Player { Id = data.OtherPlayer, AcademyId = DemoSeed.NogoomAcademyId, PlayerCode = "CTX-" + data.OtherPlayer.ToString("N")[..10], ArabicName = "لاعب السياق الثاني الاصطناعي", DateOfBirth = new(2016, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now });
            db.SportEnrollments.Add(new SportEnrollment { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, PlayerId = data.OtherPlayer, BranchId = data.OtherBranch, SportId = data.OtherSport, TrainingGroupId = data.OtherGroup, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.TrainingSessions.Add(new TrainingSession { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, BranchId = data.OtherBranch, SportId = data.OtherSport, AgeCategoryId = age, TrainingGroupId = data.OtherGroup, SessionDate = new(2026, 9, 28), StartTime = new(17, 0), EndTime = new(18, 0), CreatedByUserId = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync(), CreatedAtUtc = now, UpdatedAtUtc = now });
            await db.SaveChangesAsync();
        }
        var strong = Access(data.Branch, add: true, edit: true) with { AllSports = false, Sports = [data.Sport] };
        var weak = Access(data.OtherBranch) with { AllSports = false, Sports = [data.OtherSport] };
        (await Write(owner, $"/api/v1/manage/staff/{f.Member}/access", strong)).EnsureSuccessStatusCode();
        (await Write(owner, $"/api/v1/manage/staff/{id}/access", weak)).EnsureSuccessStatusCode(); (await SetContext(owner, f.Member, id, true)).EnsureSuccessStatusCode();
        var contexts = await admin.GetFromJsonAsync<JsonElement>("/api/v1/my-academies"); Assert.Equal(2, contexts.GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/people/players/{data.OtherPlayer}")).StatusCode);
        (await SelectContext(admin, DemoSeed.NogoomAcademyId, id)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/people/players/{data.OtherPlayer}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/people/players/{data.Player}")).StatusCode);
        var options = await admin.GetFromJsonAsync<JsonElement>("/api/v1/manage/structure/options");
        Assert.Equal([data.OtherBranch], options.GetProperty("branches").EnumerateArray().Select(x => x.GetProperty("id").GetGuid())); Assert.Equal([data.OtherSport], options.GetProperty("sports").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));
        var category = options.GetProperty("categories")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, "/api/v1/manage/groups", new { arabicName = "كتابة غير مخولة", branchId = data.OtherBranch, sportId = data.OtherSport, ageCategoryId = category }, false)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SelectContext(admin, DemoSeed.FutureAcademyId, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SelectContext(admin, DemoSeed.NogoomAcademyId, Guid.NewGuid())).StatusCode);
        var csv = await admin.GetAsync("/api/v1/reports/attendance/export?from=2026-09-01&to=2026-09-28"); csv.EnsureSuccessStatusCode(); Assert.DoesNotContain(data.Group.ToString(), await csv.Content.ReadAsStringAsync());
        (await SetContext(owner, f.Member, id, false)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/people/players")).StatusCode);
        (await SelectContext(admin, DemoSeed.NogoomAcademyId, f.Member)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/people/players/{data.Player}")).StatusCode);
        (await SetContext(owner, f.Member, id, true)).EnsureSuccessStatusCode(); (await SelectContext(admin, DemoSeed.NogoomAcademyId, id)).EnsureSuccessStatusCode();
        (await Write(owner, $"/api/v1/manage/staff/{id}/access", weak with { Actions = weak.Actions.Select(a => a with { Read = false }).ToArray() })).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/people/players")).StatusCode);
        (await SelectContext(admin, DemoSeed.NogoomAcademyId, f.Member)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/people/players/{data.Player}")).StatusCode);
    }
    [Fact]
    public async Task V3_web_multi_context_login_has_no_operational_access_before_explicit_selection()
    {
        var f = await CreateAdmin(); using var owner = f.Owner; using var currentSession = f.Admin; var data = await Data(); var id = await NewContext(owner, f.Member);
        (await Write(owner, $"/api/v1/manage/staff/{f.Member}/access", Access(data.A))).EnsureSuccessStatusCode();
        (await Write(owner, $"/api/v1/manage/staff/{id}/access", Access(data.B) with { AllSports = true, Sports = [] })).EnsureSuccessStatusCode(); (await SetContext(owner, f.Member, id, true)).EnsureSuccessStatusCode();
        string email; await using (var scope = factory.Services.CreateAsyncScope()) email = (await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().AcademyMemberships.Where(m => m.Id == f.Member).Select(m => m.User.Email).SingleAsync())!;
        using var fresh = await Staff(email);
        Assert.Equal(HttpStatusCode.Forbidden, (await fresh.GetAsync("/api/v1/me")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await fresh.GetAsync("/api/v1/people/players")).StatusCode);
        Assert.Equal(2, (await fresh.GetFromJsonAsync<JsonElement>("/api/v1/my-academies")).GetArrayLength());
        (await SelectContext(fresh, DemoSeed.NogoomAcademyId, id)).EnsureSuccessStatusCode();
        Assert.Equal(id, (await fresh.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("membershipId").GetGuid()); Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync($"/api/v1/people/players/{data.PlayerB}")).StatusCode);
        // An already explicit primary session is not switched when another context is created.
        Assert.Equal(f.Member, (await currentSession.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("membershipId").GetGuid()); Assert.Equal(HttpStatusCode.NotFound, (await currentSession.GetAsync($"/api/v1/people/players/{data.PlayerB}")).StatusCode);
    }
    [Fact]
    public async Task V3_context_write_rechecks_revocation_even_when_all_sports_and_parent_activity()
    {
        var f = await CreateAdmin(); using var owner = f.Owner; using var admin = f.Admin; var data = await Data(); var id = await NewContext(owner, f.Member);
        var grant = Access(data.A, add: true, edit: true) with { AllSports = true, Sports = [] };
        (await Write(owner, $"/api/v1/manage/staff/{id}/access", grant)).EnsureSuccessStatusCode(); (await SetContext(owner, f.Member, id, true)).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var user = await db.AcademyMemberships.Where(m => m.Id == id).Select(m => m.UserId).SingleAsync();
        db.BindRequestScope(DemoSeed.NogoomAcademyId, user, id, [data.A], null, "academy", "Add", additionalContext: true);
        var group = await db.TrainingGroups.SingleAsync(g => g.Id == data.GroupA); var newId = Guid.NewGuid();
        db.TrainingGroups.Add(new() { Id = newId, AcademyId = group.AcademyId, BranchId = group.BranchId, SportId = group.SportId, AgeCategoryId = group.AgeCategoryId, ArabicName = "كتابة معلقة " + newId.ToString("N")[..8], Capacity = 5 });
        (await SetContext(owner, f.Member, id, false)).EnsureSuccessStatusCode(); await Assert.ThrowsAsync<ResourceScopeException>(() => db.SaveChangesAsync());
        await using var verifyScope = factory.Services.CreateAsyncScope(); var verify = verifyScope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.False(await verify.TrainingGroups.AnyAsync(g => g.Id == newId));
        (await SetContext(owner, f.Member, id, true)).EnsureSuccessStatusCode();
        var primary = await verify.AcademyMemberships.SingleAsync(m => m.Id == f.Member); primary.IsActive = false; await verify.SaveChangesAsync();
        await Assert.ThrowsAsync<ResourceScopeException>(() => db.SaveChangesAsync()); Assert.Equal(HttpStatusCode.Forbidden, (await SelectContext(admin, DemoSeed.NogoomAcademyId, id)).StatusCode);
        Assert.DoesNotContain((await admin.GetFromJsonAsync<JsonElement>("/api/v1/my-academies")).EnumerateArray(), m => m.GetProperty("membershipId").GetGuid() == id);
    }
    [Fact]
    public async Task V3_mobile_same_role_contexts_rotate_tokens_and_keep_scopes_separate()
    {
        var f = await CreateAdmin(); using var owner = f.Owner; using var web = f.Admin; var data = await SportsData(); var id = await NewContext(owner, f.Member, "متابعة الموبايل الاصطناعية");
        (await Write(owner, $"/api/v1/manage/staff/{f.Member}/access", Access(data.Branch, add: true) with { AllSports = false, Sports = [data.Sport] })).EnsureSuccessStatusCode();
        (await Write(owner, $"/api/v1/manage/staff/{id}/access", Access(data.Branch) with { AllSports = false, Sports = [data.OtherSport] })).EnsureSuccessStatusCode(); (await SetContext(owner, f.Member, id, true)).EnsureSuccessStatusCode();
        string phone;
        await using (var scope = factory.Services.CreateAsyncScope()) phone = (await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().AcademyMemberships.Where(m => m.Id == f.Member).Select(m => m.User.PhoneNumber).SingleAsync())!;
        using var mobile = factory.CreateClient(); var challenge = await mobile.PostAsJsonAsync("/api/v1/mobile/auth/otp/request", new { phoneNumber = phone }); challenge.EnsureSuccessStatusCode();
        var signedIn = await mobile.PostAsJsonAsync("/api/v1/mobile/auth/otp/verify", new { phoneNumber = phone, challengeId = (await challenge.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetGuid(), code = "246810", deviceName = "اختبار سياقات" }); signedIn.EnsureSuccessStatusCode();
        var credential = await signedIn.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(JsonValueKind.Null, credential.GetProperty("membershipId").ValueKind); Assert.Equal(2, credential.GetProperty("memberships").GetArrayLength());
        var context = credential.GetProperty("memberships").EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == id); Assert.Equal("متابعة الموبايل الاصطناعية", context.GetProperty("contextName").GetString()); Assert.Single(context.GetProperty("sportNames").EnumerateArray()); Assert.Equal(3, context.GetProperty("contextRevision").GetInt32());
        void Use(JsonElement c) => mobile.DefaultRequestHeaders.Authorization = new("Bearer", c.GetProperty("accessToken").GetString());
        Use(credential); Assert.Equal(HttpStatusCode.Forbidden, (await mobile.GetAsync("/api/v1/people/players")).StatusCode);
        var selected = await mobile.PostAsJsonAsync("/api/v1/mobile/auth/select-role", new { membershipId = f.Member }); selected.EnsureSuccessStatusCode(); var root = await selected.Content.ReadFromJsonAsync<JsonElement>(); Use(root);
        Assert.Equal(HttpStatusCode.OK, (await mobile.GetAsync($"/api/v1/people/players/{data.Player}")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await mobile.GetAsync($"/api/v1/people/players/{data.OtherPlayer}")).StatusCode);
        var switched = await mobile.PostAsJsonAsync("/api/v1/mobile/auth/select-role", new { membershipId = id }); switched.EnsureSuccessStatusCode(); var current = await switched.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Unauthorized, (await mobile.GetAsync("/api/v1/me")).StatusCode); Use(current);
        Assert.Equal(HttpStatusCode.OK, (await mobile.GetAsync($"/api/v1/people/players/{data.OtherPlayer}")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await mobile.GetAsync($"/api/v1/people/players/{data.Player}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mobile.PostAsJsonAsync("/api/v1/mobile/auth/select-role", new { membershipId = Guid.NewGuid() })).StatusCode);
        var revised = Access(data.Branch) with { AllSports = false, Sports = [data.OtherSport], Actions = Access(data.Branch).Actions.Select(a => a.Module == "players" ? a with { Read = false } : a).ToArray() };
        (await Write(owner, $"/api/v1/manage/staff/{id}/access", revised)).EnsureSuccessStatusCode();
        var refreshed = await mobile.PostAsJsonAsync("/api/v1/mobile/auth/refresh", new { refreshToken = current.GetProperty("refreshToken").GetString() }); refreshed.EnsureSuccessStatusCode(); current = await refreshed.Content.ReadFromJsonAsync<JsonElement>(); Use(current);
        Assert.Equal(4, current.GetProperty("memberships").EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == id).GetProperty("contextRevision").GetInt32()); Assert.Equal(HttpStatusCode.Forbidden, (await mobile.GetAsync("/api/v1/people/players")).StatusCode);
        (await SetContext(owner, f.Member, id, false)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Unauthorized, (await mobile.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await mobile.PostAsJsonAsync("/api/v1/mobile/auth/refresh", new { refreshToken = current.GetProperty("refreshToken").GetString() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await web.GetAsync("/api/v1/people/players")).StatusCode);
    }
    [Fact]
    public async Task V3_context_demo_seed_is_repeatable_preserves_revocation_and_rejects_production()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>(); var options = scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;
        await SportScopeDemoSeed.Seed(db, scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), env, options);
        await AccessContextDemoSeed.Seed(db, env, options); await AccessContextDemoSeed.Seed(db, env, options);
        Assert.Equal(1, await db.AcademyMemberships.CountAsync(m => m.Id == AccessContextDemoSeed.ContextId));
        Assert.Equal(StaffAccessRules.Modules.Length, await db.StaffActionGrants.CountAsync(g => g.MembershipId == AccessContextDemoSeed.ContextId));
        var row = await db.AcademyMemberships.SingleAsync(m => m.Id == AccessContextDemoSeed.ContextId); var active = row.IsActive;
        try { row.IsActive = false; await db.SaveChangesAsync(); await AccessContextDemoSeed.Seed(db, env, options); await db.Entry(row).ReloadAsync(); Assert.False(row.IsActive); } finally { row.IsActive = active; await db.SaveChangesAsync(); }
        var previous = env.EnvironmentName; try { env.EnvironmentName = "Production"; await Assert.ThrowsAsync<InvalidOperationException>(() => AccessContextDemoSeed.Seed(db, env, options)); } finally { env.EnvironmentName = previous; }
        Assert.False(await db.AcademyMemberships.AnyAsync(m => m.Id == AccessContextDemoSeed.ContextId && m.AcademyId == DemoSeed.FutureAcademyId));
    }
}
