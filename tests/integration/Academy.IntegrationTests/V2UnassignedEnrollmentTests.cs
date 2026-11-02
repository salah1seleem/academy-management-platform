using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    private async Task<Guid> PlayerFor(Guid enrollment) { await using var s = factory.Services.CreateAsyncScope(); return await s.ServiceProvider.GetRequiredService<FoundationDbContext>().SportEnrollments.Where(x => x.Id == enrollment).Select(x => x.PlayerId).SingleAsync(); }
    [Fact]
    public async Task Unassigned_second_sport_preserves_player_guardian_and_existing_group_without_finance_or_attendance()
    {
        var f = await Fixture(); var player = await PlayerFor(f.Enrollment); using var owner = await Staff(); using var guardian = await Guardian();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var cash = await db.Collections.CountAsync(); var periods = await db.SubscriptionPeriods.CountAsync(); var attendance = await db.PlayerAttendances.CountAsync(); var people = await db.Players.CountAsync(); var links = await db.GuardianPlayerLinks.CountAsync();
        var r = await Write(owner, $"/api/v1/people/players/{player}/unassigned-enrollments", new UnassignedEnrollmentWrite(Slice2DemoSeed.FootballId, f.A)); Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var row = (await r.Content.ReadFromJsonAsync<JsonElement>()); Assert.False(row.GetProperty("subscriptionCreated").GetBoolean());
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/players/{player}"); Assert.Equal(2, detail.GetProperty("enrollments").GetArrayLength()); var pending = detail.GetProperty("enrollments").EnumerateArray().Single(x => x.GetProperty("placement").GetString() == "Unassigned"); Assert.Equal(JsonValueKind.Null, pending.GetProperty("trainingGroupId").ValueKind); Assert.Equal("في انتظار المجموعة", pending.GetProperty("groupName").GetString()); Assert.Equal(f.A, pending.GetProperty("branchId").GetGuid());
        var child = await guardian.GetAsync($"/api/v1/guardian/children/{player}/profile"); Assert.True(child.IsSuccessStatusCode, await child.Content.ReadAsStringAsync()); Assert.Contains("في انتظار المجموعة", await child.Content.ReadAsStringAsync());
        Assert.Equal(f.Group, await db.SportEnrollments.Where(x => x.Id == f.Enrollment).Select(x => x.TrainingGroupId).SingleAsync());
        Assert.Equal(cash, await db.Collections.CountAsync()); Assert.Equal(periods, await db.SubscriptionPeriods.CountAsync()); Assert.Equal(attendance, await db.PlayerAttendances.CountAsync()); Assert.Equal(people, await db.Players.CountAsync()); Assert.Equal(links, await db.GuardianPlayerLinks.CountAsync());
    }
    [Fact]
    public async Task Unassigned_concurrent_retry_has_one_natural_key_and_cannot_bypass_reviewed_placement()
    {
        var f = await Fixture(); var player = await PlayerFor(f.Enrollment); using var a = await Staff(); using var b = await Staff(); var path = $"/api/v1/people/players/{player}/unassigned-enrollments"; var body = new UnassignedEnrollmentWrite(Slice2DemoSeed.FootballId, f.A);
        var responses = await Task.WhenAll(Write(a, path, body), Write(b, path, body)); Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
        var ids = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid())); Assert.Equal(ids[0], ids[1]);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(a, path, new UnassignedEnrollmentWrite(Slice2DemoSeed.SwimmingId, f.A))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(a, $"/api/v1/people/enrollments/{ids[0]}/group", new MoveGroupWrite(f.Group), true)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Equal(1, await db.SportEnrollments.CountAsync(x => x.PlayerId == player && x.TrainingGroupId == null));
    }
    [Fact]
    public async Task Unassigned_write_rejects_foreign_academy_inactive_options_guardian_and_missing_CSRF()
    {
        var f = await Fixture(); var player = await PlayerFor(f.Enrollment); using var owner = await Staff(); using var foreign = await Staff("owner.future@example.test"); using var guardian = await Guardian(); var path = $"/api/v1/people/players/{player}/unassigned-enrollments";
        Assert.Equal(HttpStatusCode.NotFound, (await Write(foreign, path, new UnassignedEnrollmentWrite(Slice2DemoSeed.FootballId, f.A))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(guardian, path, new UnassignedEnrollmentWrite(Slice2DemoSeed.FootballId, f.A))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, new UnassignedEnrollmentWrite(Slice2DemoSeed.FootballId, f.A))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(owner, path, new UnassignedEnrollmentWrite(Guid.NewGuid(), f.A))).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); (await db.Branches.SingleAsync(x => x.Id == f.B)).IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Write(owner, path, new UnassignedEnrollmentWrite(Slice2DemoSeed.FootballId, f.B))).StatusCode);
    }
    [Fact]
    public async Task Unassigned_admin_requires_players_Add_and_current_branch_scope_and_does_not_leak_options()
    {
        var f = await Fixture(); var player = await PlayerFor(f.Enrollment); using var owner = await Staff(); var email = $"intake-{Guid.NewGuid():N}@example.test";
        var created = await Write(owner, "/api/v1/manage/staff", new StaffWrite("إداري انتظار تجريبي", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, null)); created.EnsureSuccessStatusCode(); var member = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); using var admin = await Staff(email);
        var access = new StaffAccessWrite(false, [f.A], StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m == "players", false, false, false)).ToArray()); (await Write(owner, $"/api/v1/manage/staff/{member}/access", access, true)).EnsureSuccessStatusCode();
        var path = $"/api/v1/people/players/{player}/unassigned-enrollments"; var body = new UnassignedEnrollmentWrite(Slice2DemoSeed.FootballId, f.A);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, path, body)).StatusCode);
        access = access with { Actions = access.Actions.Select(a => a with { Add = a.Module == "players" }).ToArray() }; (await Write(owner, $"/api/v1/manage/staff/{member}/access", access, true)).EnsureSuccessStatusCode();
        var options = await admin.GetFromJsonAsync<JsonElement>("/api/v1/people/unassigned/options"); Assert.Equal(f.A, Assert.Single(options.GetProperty("branches").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await Write(admin, path, body with { BranchId = f.B })).StatusCode); (await Write(admin, path, body)).EnsureSuccessStatusCode();
        (await Write(owner, $"/api/v1/manage/staff/{member}/access", access with { Branches = [f.B] }, true)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.NotFound, (await Write(admin, path, body)).StatusCode);
    }
}
