using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2StaffScopeTests
{
    [Fact]
    public async Task Media_upload_group_options_require_add_permission_keep_branch_and_academy_scope_and_page_in_sql()
    {
        var fixture = await CreateAdmin();
        using var owner = fixture.Owner;
        using var admin = fixture.Admin;
        using var other = await Staff(DemoSeed.FutureOwnerEmail);
        using var anonymous = factory.CreateClient();
        var data = await Data();
        const string path = "/api/v1/assets/media-groups";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path)).StatusCode);
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", Access(data.A), true)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path)).StatusCode); // Read alone cannot start a publication.
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", Access(data.A, add: true), true)).EnsureSuccessStatusCode();
        var scoped = await admin.GetFromJsonAsync<JsonElement>(path + "?pageSize=1&academyId=" + DemoSeed.FutureAcademyId);
        var items = scoped.GetProperty("items").EnumerateArray().ToArray();
        Assert.Single(items);
        Assert.Equal(data.GroupA, items[0].GetProperty("id").GetGuid());
        Assert.False(scoped.GetProperty("hasMore").GetBoolean());
        Assert.False(items[0].TryGetProperty("players", out _));
        Assert.False(items[0].TryGetProperty("guardian", out _));
        var foreign = await other.GetStringAsync(path + "?pageSize=50");
        Assert.DoesNotContain(data.GroupA.ToString(), foreign);
        Assert.DoesNotContain(data.GroupB.ToString(), foreign);
        var first = await owner.GetFromJsonAsync<JsonElement>(path + "?pageSize=1&page=1");
        var second = await owner.GetFromJsonAsync<JsonElement>(path + "?pageSize=1&page=2");
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        Assert.NotEqual(first.GetProperty("items")[0].GetProperty("id").GetGuid(), second.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(first.GetProperty("items")[0].GetProperty("id").GetGuid(), (await owner.GetFromJsonAsync<JsonElement>(path + "?pageSize=1&page=1")).GetProperty("items")[0].GetProperty("id").GetGuid());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var name = await db.TrainingGroups.Where(g => g.Id == data.GroupA).Select(g => g.ArabicName).SingleAsync();
        var found = await owner.GetFromJsonAsync<JsonElement>(path + "?search=" + Uri.EscapeDataString(name));
        Assert.Equal(data.GroupA, Assert.Single(found.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        foreach (var query in new[] { "page=0", "pageSize=51", "search=" + new string('x', 101) })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.GetAsync(path + "?" + query)).StatusCode);
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", Access(data.B, add: true), true)).EnsureSuccessStatusCode();
        var changed = await admin.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(data.GroupB, Assert.Single(changed.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.DoesNotContain(data.GroupA.ToString(), changed.ToString());
    }

    [Fact]
    public async Task Media_upload_player_options_are_minimal_and_require_the_current_branch_and_publish_workflow()
    {
        var fixture = await CreateAdmin();
        using var owner = fixture.Owner;
        using var admin = fixture.Admin;
        using var other = await Staff(DemoSeed.FutureOwnerEmail);
        using var anonymous = factory.CreateClient();
        var data = await Data();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var playerA = await db.SportEnrollments.Where(e => e.BranchId == data.A)
            .Select(e => new { e.Player.Id, e.Player.ArabicName, e.Player.PlayerCode }).SingleAsync();
        const string path = "/api/v1/assets/media-players";
        var accessPath = $"/api/v1/manage/staff/{fixture.Member}/access";
        StaffAccessWrite Grant(Guid branch, bool add, bool publish) =>
            new(false, [branch], StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m == "media", m == "media" && add, m == "media" && add, false)).ToArray(),
                WorkflowActions: publish ? ["workflow.media.manage"] : []);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path)).StatusCode);
        (await Write(owner, accessPath, Grant(data.A, false, false), true)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path)).StatusCode);
        (await Write(owner, accessPath, Grant(data.A, true, false), true)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/assets/media-groups")).StatusCode);
        (await Write(owner, accessPath, Grant(data.A, true, true), true)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/people/players")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/manage/structure/options")).StatusCode);

        var response = await admin.GetAsync(path + $"?pageSize=1&academyId={DemoSeed.FutureAcademyId}&selectedId={data.PlayerB}");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore == true && response.Headers.CacheControl.Private);
        var results = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(results.GetProperty("items").EnumerateArray());
        Assert.Equal(playerA.Id, item.GetProperty("id").GetGuid());
        Assert.Equal(new[] { "arabicName", "id", "playerCode" }, item.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.False(results.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, results.GetProperty("selected").ValueKind);

        foreach (var term in new[] { playerA.ArabicName, playerA.PlayerCode.ToLowerInvariant() })
        {
            var found = await admin.GetFromJsonAsync<JsonElement>(path + "?search=" + Uri.EscapeDataString(term));
            Assert.Equal(playerA.Id, Assert.Single(found.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }
        var pinned = await admin.GetFromJsonAsync<JsonElement>(path + $"?page=2&pageSize=1&selectedId={playerA.Id}");
        Assert.Empty(pinned.GetProperty("items").EnumerateArray());
        Assert.Equal(playerA.Id, pinned.GetProperty("selected").GetProperty("id").GetGuid());
        var foreign = await other.GetStringAsync(path + $"?pageSize=50&selectedId={playerA.Id}");
        Assert.DoesNotContain(playerA.Id.ToString(), foreign);
        Assert.DoesNotContain(data.PlayerB.ToString(), foreign);
        foreach (var query in new[] { "page=0", "page=1000001", "pageSize=51", "search=" + new string('x', 101) })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.GetAsync(path + "?" + query)).StatusCode);

        (await Write(owner, accessPath, Grant(data.B, true, true), true)).EnsureSuccessStatusCode();
        var changed = await admin.GetFromJsonAsync<JsonElement>(path + $"?selectedId={playerA.Id}");
        Assert.Equal(data.PlayerB, Assert.Single(changed.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, changed.GetProperty("selected").ValueKind);
    }
}

public sealed partial class BrandingMatchesTests
{
    [Fact]
    public async Task Media_upload_group_options_deny_guardian_and_coach_without_changing_assigned_coach_groups()
    {
        using var coach = await Mobile(AcademyRole.Coach);
        using var guardian = await Mobile(AcademyRole.Guardian);
        foreach (var client in new[] { coach, guardian })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/assets/media-groups")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/assets/media-players")).StatusCode);
        }
        var assigned = await coach.GetFromJsonAsync<JsonElement>("/api/v1/coach/groups");
        Assert.NotEmpty(assigned.EnumerateArray());
        var id = assigned[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await coach.GetAsync($"/api/v1/assets/media-audience?groupId={id}")).StatusCode);
    }
}
