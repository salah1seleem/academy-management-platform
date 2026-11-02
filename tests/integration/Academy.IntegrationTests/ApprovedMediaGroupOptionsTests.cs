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
}

public sealed partial class BrandingMatchesTests
{
    [Fact]
    public async Task Media_upload_group_options_deny_guardian_and_coach_without_changing_assigned_coach_groups()
    {
        using var coach = await Mobile(AcademyRole.Coach);
        using var guardian = await Mobile(AcademyRole.Guardian);
        foreach (var client in new[] { coach, guardian })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/assets/media-groups")).StatusCode);
        var assigned = await coach.GetFromJsonAsync<JsonElement>("/api/v1/coach/groups");
        Assert.NotEmpty(assigned.EnumerateArray());
        var id = assigned[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await coach.GetAsync($"/api/v1/assets/media-audience?groupId={id}")).StatusCode);
    }
}
