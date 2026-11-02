using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class SubscriptionPaymentTests
{
    [Fact]
    public async Task Approved_VIP_exact_read_and_issue_recovery_preserve_owner_and_family_isolation()
    {
        await using var family = await NewFamily();
        await using var unrelated = await NewFamily();
        await QualifyVip(family);
        using var owner = await InboxStaff(DemoSeed.OwnerEmail);
        using var admin = await InboxStaff(DemoSeed.AdminEmail);
        using var foreign = await InboxStaff(DemoSeed.FutureOwnerEmail);
        var body = await VipBody(owner, family.Guardian, 15);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{VipRoot}/requests/{body.RequestId}")).StatusCode);
        var id = await IssueVip(owner, body);
        var recovered = await owner.GetFromJsonAsync<JsonElement>($"{VipRoot}/requests/{body.RequestId}");
        Assert.Equal(body.RequestId, recovered.GetProperty("requestId").GetGuid());
        Assert.Equal(id, Assert.Single(recovered.GetProperty("ids").EnumerateArray()).GetGuid());
        var response = await owner.GetAsync($"{VipRoot}/{id}?page=99");
        response.EnsureSuccessStatusCode();
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var own = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
        Assert.Equal(id, own.GetProperty("id").GetGuid());
        Assert.Equal(family.Guardian, own.GetProperty("guardianId").GetGuid());
        Assert.Equal(body.RequestId, own.GetProperty("requestId").GetGuid());
        Assert.Equal(15, own.GetProperty("percentage").GetDecimal());
        Assert.Equal(body.Message, own.GetProperty("message").GetString());
        Assert.Equal(body.ExpiresOn.ToString("yyyy-MM-dd"), own.GetProperty("expiresOn").GetString());
        Assert.Equal("Available", own.GetProperty("state").GetString());
        var guardianResponse = await family.Client.GetAsync($"/api/v1/guardian/vip-rewards/{id}");
        guardianResponse.EnsureSuccessStatusCode();
        Assert.Contains("no-store", guardianResponse.Headers.CacheControl!.ToString());
        var visible = Assert.Single((await guardianResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
        Assert.Equal(id, visible.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, visible.GetProperty("requestId").ValueKind);
        Assert.Equal(15, visible.GetProperty("percentage").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await unrelated.Client.GetAsync($"/api/v1/guardian/vip-rewards/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"{VipRoot}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"{VipRoot}/requests/{body.RequestId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"{VipRoot}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await family.Client.GetAsync($"{VipRoot}/requests/{body.RequestId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{VipRoot}/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(id, await IssueVip(owner, body));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, await db.VipRewards.CountAsync(r => r.RequestId == body.RequestId));
        (await Post(owner, $"{VipRoot}/{id}/cancel", new { }, await Csrf(owner))).EnsureSuccessStatusCode();
        var cancelled = await family.Client.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/vip-rewards/{id}");
        Assert.Equal("Cancelled", Assert.Single(cancelled.GetProperty("items").EnumerateArray()).GetProperty("state").GetString());
    }
}
