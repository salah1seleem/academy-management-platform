using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Academy.IntegrationTests;

public sealed partial class V2ReceivablesTests
{
    [Fact]
    public async Task Receivable_document_and_list_use_same_academy_day_at_UTC_boundary()
    {
        var f = await Fixture();
        await using var boundary = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<ISubscriptionClock>(new ReceivableBoundaryClock())));
        using var owner = boundary.CreateClient();
        (await Post(owner, "/api/v1/auth/login", new { email = DemoSeed.OwnerEmail, password = "Demo-Only-123!" })).EnsureSuccessStatusCode();
        var created = await Post(owner, "/api/v1/finance/receivables/", new {
            sportEnrollmentId = f.Enrollment, amount = "123.45", dueDate = "2026-09-27", source = "ManualCharge", description = "اختبار يوم الأكاديمية عند حد التوقيت العالمي"
        });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var list = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/receivables?playerId={f.Player}");
        var row = list.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/receivables/{id}");
        Assert.True(row.GetProperty("overdue").GetBoolean());
        Assert.Equal(row.GetProperty("overdue").GetBoolean(), detail.GetProperty("balance").GetProperty("overdue").GetBoolean());
        Assert.Equal(row.GetProperty("outstanding").GetString(), detail.GetProperty("balance").GetProperty("outstanding").GetString());
    }

    private sealed class ReceivableBoundaryClock : ISubscriptionClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 27, 22, 30, 0, TimeSpan.Zero);
        public DateOnly Today => new(2026, 9, 27);
    }
}
