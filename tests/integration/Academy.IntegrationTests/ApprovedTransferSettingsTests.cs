using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class BrandingMatchesTests
{
    [Fact]
    public async Task Approved_transfer_settings_enablement_roundtrips_without_discarding_details_or_changing_financial_records()
    {
        using var owner = await Staff();
        using var admin = await Staff(DemoSeed.AdminEmail);
        using var guardian = await Mobile(AcademyRole.Guardian);
        using var other = await Staff(DemoSeed.FutureOwnerEmail);
        const string path = "/api/v1/payment-settings";
        var original = await owner.GetFromJsonAsync<JsonElement>(path);
        var foreign = await other.GetFromJsonAsync<JsonElement>(path);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var count = await db.Collections.CountAsync();
        var total = await db.Collections.SumAsync(c => c.Amount);
        var body = new { instaPayAddress = "synthetic-settings-only@example.test", walletNumber = (string?)null,
            recipientName = "إعداد قبول اصطناعي — لا تحول أموالًا", isActive = false };
        try
        {
            (await Send(owner, path, body, true)).EnsureSuccessStatusCode();
            var disabled = await owner.GetFromJsonAsync<JsonElement>(path);
            Assert.False(disabled.GetProperty("isActive").GetBoolean());
            Assert.False(disabled.GetProperty("configured").GetBoolean());
            Assert.Equal(body.instaPayAddress, disabled.GetProperty("instaPayAddress").GetString());
            Assert.Equal(body.recipientName, disabled.GetProperty("recipientName").GetString());
            (await Send(owner, path, body with { isActive = true }, true)).EnsureSuccessStatusCode();
            var enabled = await guardian.GetFromJsonAsync<JsonElement>(path);
            Assert.True(enabled.GetProperty("isActive").GetBoolean());
            Assert.True(enabled.GetProperty("configured").GetBoolean());
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, path, body, true)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(guardian, path, body, true)).StatusCode);
            Assert.True((await owner.GetFromJsonAsync<JsonElement>(path)).GetProperty("isActive").GetBoolean());
            Assert.Equal(foreign.GetRawText(), (await other.GetFromJsonAsync<JsonElement>(path)).GetRawText());
            Assert.Equal(count, await db.Collections.CountAsync());
            Assert.Equal(total, await db.Collections.SumAsync(c => c.Amount));
        }
        finally
        {
            (await Send(owner, path, new {
                instaPayAddress = original.GetProperty("instaPayAddress").GetString(),
                walletNumber = original.GetProperty("walletNumber").GetString(),
                recipientName = original.GetProperty("recipientName").GetString(),
                isActive = original.GetProperty("isActive").GetBoolean()
            }, true)).EnsureSuccessStatusCode();
        }
    }
}
