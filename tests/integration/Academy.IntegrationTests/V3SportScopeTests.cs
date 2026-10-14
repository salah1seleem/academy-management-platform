using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Importing;
using Academy.Api.Facilities;
using Academy.Api.Operations;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Identity;
using Academy.Infrastructure.Identity;

namespace Academy.IntegrationTests;

public sealed partial class V2StaffScopeTests
{
    private async Task<(Guid Branch, Guid OtherBranch, Guid Sport, Guid OtherSport, Guid Group, Guid OtherGroup, Guid Player, Guid OtherPlayer)> SportsData()
    {
        var data = await Data();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var group = await db.TrainingGroups.SingleAsync(g => g.Id == data.GroupA);
        var otherSport = await db.Sports.FirstAsync(s => s.AcademyId == DemoSeed.NogoomAcademyId && s.IsActive && s.Id != group.SportId);
        var player = await db.SportEnrollments.Where(e => e.TrainingGroupId == group.Id).Select(e => e.PlayerId).SingleAsync();
        var otherGroup = Guid.NewGuid(); var otherPlayer = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        db.TrainingGroups.Add(new() { Id = otherGroup, AcademyId = group.AcademyId, BranchId = data.A, SportId = otherSport.Id, AgeCategoryId = group.AgeCategoryId, Capacity = 20, ArabicName = "مجموعة الرياضة الأخرى " + otherGroup.ToString("N")[..8], CreatedAtUtc = now, UpdatedAtUtc = now });
        db.Players.Add(new() { Id = otherPlayer, AcademyId = group.AcademyId, PlayerCode = "SPORT-" + otherPlayer.ToString("N")[..10], ArabicName = "لاعب الرياضة الأخرى الاصطناعي", DateOfBirth = new(2016, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now });
        db.SportEnrollments.Add(new() { Id = Guid.NewGuid(), AcademyId = group.AcademyId, PlayerId = otherPlayer, TrainingGroupId = otherGroup, BranchId = data.A, SportId = otherSport.Id, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.TrainingSessions.Add(new() { Id = Guid.NewGuid(), AcademyId = group.AcademyId, TrainingGroupId = otherGroup, BranchId = data.A, SportId = otherSport.Id, AgeCategoryId = group.AgeCategoryId, SessionDate = new(2026, 9, 28), StartTime = new(18, 0), EndTime = new(19, 0), CreatedByUserId = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync(), CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); return (data.A, data.B, group.SportId, otherSport.Id, group.Id, otherGroup, player, otherPlayer);
    }
    [Fact]
    public async Task V3_sport_scope_intersects_selected_membership_branch_and_revocation_affects_open_session()
    {
        var fixture = await CreateAdmin(); using var owner = fixture.Owner; using var admin = fixture.Admin; var data = await SportsData();
        var access = Access(data.Branch, add: true, edit: true) with { AllSports = false, Sports = [data.Sport] };
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", access)).EnsureSuccessStatusCode();
        var options = await admin.GetFromJsonAsync<JsonElement>("/api/v1/manage/structure/options");
        Assert.Equal([data.Sport], options.GetProperty("sports").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()));
        Assert.All(options.GetProperty("groups").EnumerateArray(), g => { Assert.Equal(data.Branch, g.GetProperty("branchId").GetGuid()); Assert.Equal(data.Sport, g.GetProperty("sportId").GetGuid()); });
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/people/players/{data.Player}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/people/players/{data.OtherPlayer}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/manage/structure/groups/{data.OtherGroup}")).StatusCode);
        var sessions = await admin.GetFromJsonAsync<JsonElement>("/api/v1/attendance/sessions?allDates=true");
        Assert.All(sessions.EnumerateArray(), s => Assert.Equal(data.Group, s.GetProperty("trainingGroupId").GetGuid()));
        var report = await admin.GetAsync("/api/v1/reports/attendance/export?from=2026-09-01&to=2026-09-28"); report.EnsureSuccessStatusCode();
        Assert.DoesNotContain("لاعب الرياضة الأخرى", await report.Content.ReadAsStringAsync());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var category = await db.AgeCategories.FirstAsync(a => a.AcademyId == DemoSeed.NogoomAcademyId && a.IsActive);
        var bad = await Write(admin, "/api/v1/manage/groups", new { arabicName = "تجاوز نطاق الرياضة", branchId = data.Branch, sportId = data.OtherSport, ageCategoryId = category.Id, capacity = 10 }, false);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        Assert.False(await db.TrainingGroups.AnyAsync(g => g.ArabicName == "تجاوز نطاق الرياضة"));
        var validName = "مجموعة مصرح بها " + Guid.NewGuid().ToString("N")[..8];
        Assert.Equal(HttpStatusCode.Created, (await Write(admin, "/api/v1/manage/groups", new { arabicName = validName, branchId = data.Branch, sportId = data.Sport, ageCategoryId = category.Id, capacity = 10 }, false)).StatusCode);
        Assert.True(await db.TrainingGroups.AnyAsync(g => g.ArabicName == validName && g.SportId == data.Sport && g.BranchId == data.Branch));
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", access with { Sports = [data.OtherSport] })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/people/players/{data.Player}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/people/players/{data.OtherPlayer}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/people/players/{data.Player}")).StatusCode);
    }
    [Fact]
    public async Task V3_invalid_sport_scope_is_rejected_and_legacy_call_cannot_widen_restricted_scope()
    {
        var fixture = await CreateAdmin(); using var owner = fixture.Owner; using var admin = fixture.Admin; var data = await SportsData();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var foreign = await db.Sports.Where(s => s.AcademyId == DemoSeed.FutureAcademyId).Select(s => s.Id).FirstAsync();
        foreach (var sports in new[] { Array.Empty<Guid>(), new[] { Guid.NewGuid() }, new[] { foreign }, new[] { data.Sport, data.Sport } })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", Access(data.Branch) with { AllSports = false, Sports = sports })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", Access(data.Branch) with { AllSports = true })).StatusCode);
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", Access(data.Branch) with { AllSports = false, Sports = [data.Sport] })).EnsureSuccessStatusCode();
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", Access(data.Branch))).EnsureSuccessStatusCode();
        var saved = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff/{fixture.Member}/access");
        Assert.False(saved.GetProperty("allSports").GetBoolean()); Assert.Equal(data.Sport, saved.GetProperty("selectedSports")[0].GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, $"/api/v1/manage/staff/{fixture.Member}/access", Access(data.OtherBranch))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/manage/staff/{Guid.NewGuid()}/access")).StatusCode);
        var audit = await db.StaffAccessAudits.Where(a => a.MembershipId == fixture.Member).OrderBy(a => a.CreatedAtUtc).Select(a => a.After).FirstAsync();
        Assert.Equal(data.Sport.ToString(), JsonDocument.Parse(audit).RootElement.GetProperty("Sports")[0].GetString());
    }
    [Fact]
    public async Task V3_narrow_sport_grant_cannot_access_shared_hr_cash_media_settings_or_publish_queued_import()
    {
        var fixture = await CreateAdmin(); using var owner = fixture.Owner; using var admin = fixture.Admin; var data = await SportsData();
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", Access(data.Branch, add: true, edit: true) with { AllSports = false, Sports = [data.Sport] })).EnsureSuccessStatusCode();
        foreach (var path in new[] { "/api/v1/reports/owner-summary", "/api/v1/reports/financial", "/api/v1/finance/cash-registers", "/api/v1/manage/payroll", "/api/v1/people/import/template", "/api/v1/manage/staff", "/api/v1/manage/staff-attendance", "/api/v1/manage/content/medical", "/api/v1/manage/content/media" })
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var user = await db.AcademyMemberships.Where(m => m.Id == fixture.Member).Select(m => m.UserId).SingleAsync();
        var rejected = await Assert.ThrowsAsync<ImportRejected>(() => ImportAuthority.Check(db, DemoSeed.NogoomAcademyId, user, fixture.Member, false, bind: true));
        Assert.Equal("authority", rejected.Code); Assert.False(db.RequestScopeEnabled);
    }
    [Fact]
    public async Task V3_sport_demo_seed_is_repeatable_preserves_revocation_and_rejects_non_demo_environment()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>(); var options = scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;
        await SportScopeDemoSeed.Seed(db, users, env, options); await SportScopeDemoSeed.Seed(db, users, env, options);
        Assert.Equal(1, await db.AcademyMemberships.CountAsync(m => m.Id == SportScopeDemoSeed.MembershipId));
        Assert.Equal(StaffAccessRules.Modules.Length, await db.StaffActionGrants.CountAsync(g => g.MembershipId == SportScopeDemoSeed.MembershipId));
        var grant = await db.MembershipSportAccesses.SingleAsync(g => g.MembershipId == SportScopeDemoSeed.MembershipId); var old = grant.IsActive;
        try { grant.IsActive = false; await db.SaveChangesAsync(); await SportScopeDemoSeed.Seed(db, users, env, options); await db.Entry(grant).ReloadAsync(); Assert.False(grant.IsActive); }
        finally { grant.IsActive = old; await db.SaveChangesAsync(); }
        Assert.False(await db.AcademyMemberships.AnyAsync(m => m.UserId == SportScopeDemoSeed.UserId && m.AcademyId == DemoSeed.FutureAcademyId));
        var environment = env.EnvironmentName;
        try { env.EnvironmentName = "Production"; await Assert.ThrowsAsync<InvalidOperationException>(() => SportScopeDemoSeed.Seed(db, users, env, options)); }
        finally { env.EnvironmentName = environment; }
    }
    [Fact]
    public async Task V3_scope_or_action_revoked_after_context_binding_blocks_pending_write_without_persisting()
    {
        var fixture = await CreateAdmin(); using var owner = fixture.Owner; using var admin = fixture.Admin; var data = await SportsData();
        var grant = Access(data.Branch, add: true, edit: true) with { AllSports = false, Sports = [data.Sport] };
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", grant)).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var user = await db.AcademyMemberships.Where(m => m.Id == fixture.Member).Select(m => m.UserId).SingleAsync();
        db.BindRequestScope(DemoSeed.NogoomAcademyId, user, fixture.Member, [data.Branch], [data.Sport], "academy", "Edit");
        var group = await db.TrainingGroups.SingleAsync(g => g.Id == data.Group); var oldName = group.ArabicName;
        group.ArabicName = "تعديل مسحوب التفويض";
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", grant with { Sports = [data.OtherSport] })).EnsureSuccessStatusCode();
        await Assert.ThrowsAsync<ResourceScopeException>(() => db.SaveChangesAsync());
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", grant with { Actions = grant.Actions.Select(a => a.Module == "academy" ? a with { Edit = false } : a).ToArray() })).EnsureSuccessStatusCode();
        await Assert.ThrowsAsync<ResourceScopeException>(() => db.SaveChangesAsync());
        await using var checkScope = factory.Services.CreateAsyncScope(); var check = checkScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(oldName, await check.TrainingGroups.Where(g => g.Id == data.Group).Select(g => g.ArabicName).SingleAsync());
    }
    [Fact]
    public async Task V3_second_role_never_widens_selected_admin_and_conflicts_still_see_hidden_sports()
    {
        var fixture = await CreateAdmin(); using var owner = fixture.Owner; using var admin = fixture.Admin; var data = await SportsData();
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", Access(data.Branch, add: true, edit: true) with { AllSports = false, Sports = [data.Sport] })).EnsureSuccessStatusCode();
        await using var setupScope = factory.Services.CreateAsyncScope(); var setup = setupScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var user = await setup.AcademyMemberships.Where(m => m.Id == fixture.Member).Select(m => m.UserId).SingleAsync();
        var coach = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        setup.AcademyMemberships.Add(new() { Id = coach, AcademyId = DemoSeed.NogoomAcademyId, UserId = user, Role = AcademyRole.Coach, AllBranches = true, AllSports = true, CreatedAtUtc = now, UpdatedAtUtc = now });
        foreach (var group in new[] { data.Group, data.OtherGroup }) setup.StaffGroupAssignments.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, AcademyMembershipId = coach, TrainingGroupId = group, CreatedAtUtc = now, UpdatedAtUtc = now });
        await setup.SaveChangesAsync();
        // Same global user has another, broader role. The existing admin cookie
        // must still use only its selected membership, not a union of its roles.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/manage/structure/groups/{data.OtherGroup}")).StatusCode);
        var access = await admin.GetFromJsonAsync<JsonElement>("/api/v1/my-access");
        Assert.False(access.GetProperty("allSports").GetBoolean()); Assert.Equal(data.Sport, access.GetProperty("sports")[0].GetGuid());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        db.BindRequestScope(DemoSeed.NogoomAcademyId, user, fixture.Member, [data.Branch], [data.Sport]);
        Assert.False(await db.TrainingSessions.AnyAsync(s => s.TrainingGroupId == data.OtherGroup));
        await using var tx = await db.Database.BeginTransactionAsync();
        var service = scope.ServiceProvider.GetRequiredService<ResourceSessionCoordinator>();
        var conflict = await Assert.ThrowsAsync<FacilityRejected>(() => service.CheckCoachSlot(DemoSeed.NogoomAcademyId, data.Group, new(2026, 9, 28), new(18, 0), new(19, 0)));
        Assert.Contains("موعد", conflict.Message); Assert.DoesNotContain(data.OtherGroup.ToString(), conflict.Message);
    }
    [Fact]
    public async Task V3_direct_context_guards_tracked_sport_reassignment_and_shared_child_without_pool_leakage()
    {
        var fixture = await CreateAdmin(); using var owner = fixture.Owner; using var admin = fixture.Admin; var data = await SportsData();
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", Access(data.Branch, add: true, edit: true) with { AllSports = false, Sports = [data.Sport] })).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var user = await db.AcademyMemberships.Where(m => m.Id == fixture.Member).Select(m => m.UserId).SingleAsync();
        var hidden = await db.SportEnrollments.SingleAsync(e => e.PlayerId == data.OtherPlayer);
        db.BindRequestScope(DemoSeed.NogoomAcademyId, user, fixture.Member, [data.Branch], [data.Sport]);
        Assert.All(await db.SportEnrollments.ToListAsync(), e => { Assert.Equal(data.Sport, e.SportId); Assert.Equal(data.Branch, e.BranchId); });
        hidden.Status = EnrollmentStatus.Inactive;
        await Assert.ThrowsAsync<ResourceScopeException>(() => db.SaveChangesAsync());
        await using var next = factory.Services.CreateAsyncScope(); var other = next.ServiceProvider.GetRequiredService<FoundationDbContext>();
        other.BindRequestScope(DemoSeed.NogoomAcademyId, Guid.NewGuid(), Guid.NewGuid(), [data.Branch], [data.OtherSport]);
        Assert.False(await other.Players.AnyAsync(p => p.Id == data.Player));
        Assert.True(await other.Players.AnyAsync(p => p.Id == data.OtherPlayer));
        await using var setupScope = factory.Services.CreateAsyncScope(); var setup = setupScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var now = DateTimeOffset.UtcNow;
        setup.SportEnrollments.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, PlayerId = data.Player, BranchId = data.Branch, SportId = data.OtherSport, TrainingGroupId = data.OtherGroup, CreatedAtUtc = now, UpdatedAtUtc = now });
        await setup.SaveChangesAsync();
        await using var sharedScope = factory.Services.CreateAsyncScope(); var shared = sharedScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        shared.BindRequestScope(DemoSeed.NogoomAcademyId, user, fixture.Member, [data.Branch], [data.Sport]);
        var sharedChild = await shared.Players.SingleAsync(p => p.Id == data.Player);
        sharedChild.ArabicName = "تعديل هوية مشتركة مرفوض";
        await Assert.ThrowsAsync<ResourceScopeException>(() => shared.SaveChangesAsync());
    }
}
