using System.Globalization;
using System.Net;
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
        var branchScope = $"branchId={f.Branch}";
        var cockpitUrl = $"/api/v1/reports/owner-cockpit?{branchScope}&from=2026-09-01&to=2026-09-28";
        var before = (await owner.GetFromJsonAsync<JsonElement>(cockpitUrl)).GetProperty("overdue");
        var created = await Post(owner, "/api/v1/finance/receivables/", new {
            sportEnrollmentId = f.Enrollment, amount = "123.45", dueDate = "2026-09-27", source = "ManualCharge", description = "اختبار يوم الأكاديمية عند حد التوقيت العالمي"
        });
        created.EnsureSuccessStatusCode();
        (await Post(owner, "/api/v1/finance/receivables/", new {
            sportEnrollmentId = f.Enrollment, amount = "450", dueDate = "2026-09-28", source = "ManualCharge", description = "مطالبة مستحقة في نفس يوم الأكاديمية"
        })).EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var list = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/receivables?playerId={f.Player}");
        var row = list.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/receivables/{id}");
        Assert.True(row.GetProperty("overdue").GetBoolean());
        Assert.Single(list.GetProperty("items").EnumerateArray(), x => x.GetProperty("overdue").GetBoolean());
        Assert.Equal(row.GetProperty("overdue").GetBoolean(), detail.GetProperty("balance").GetProperty("overdue").GetBoolean());
        Assert.Equal(row.GetProperty("outstanding").GetString(), detail.GetProperty("balance").GetProperty("outstanding").GetString());
        var branch = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/receivables?{branchScope}&state=overdue");
        var after = (await owner.GetFromJsonAsync<JsonElement>(cockpitUrl)).GetProperty("overdue");
        var expectedAmount = decimal.Parse(branch.GetProperty("overdue").GetString()!, CultureInfo.InvariantCulture);
        var expectedPlayers = branch.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("playerId").GetGuid()).Distinct().Count();
        Assert.Equal(expectedAmount, after.GetProperty("amount").GetDecimal());
        Assert.Equal(expectedPlayers, after.GetProperty("players").GetInt32());
        Assert.Equal(before.GetProperty("amount").GetDecimal() + 123.45m, after.GetProperty("amount").GetDecimal());
        Assert.Equal(before.GetProperty("players").GetInt32() + 1, after.GetProperty("players").GetInt32());
        Assert.Equal("2026-09-28", after.GetProperty("asOf").GetString());
        var summary = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-summary?{branchScope}");
        var summaryBalance = summary.GetProperty("operations").GetProperty("receivables").EnumerateArray().Single(x => x.GetProperty("currency").GetString() == "EGP");
        Assert.Equal(expectedAmount, summaryBalance.GetProperty("overdue").GetDecimal());

        using var otherOwner = await Staff(DemoSeed.FutureOwnerEmail);
        using var guardian = await Guardian();
        Assert.Equal(HttpStatusCode.NotFound, (await otherOwner.GetAsync(cockpitUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherOwner.GetAsync($"/api/v1/finance/receivables/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync(cockpitUrl)).StatusCode);
    }

    private sealed class ReceivableBoundaryClock : ISubscriptionClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 27, 22, 30, 0, TimeSpan.Zero);
        public DateOnly Today => new(2026, 9, 27);
        public DateOnly DateAt(DateTimeOffset instant, string timeZone) => Today;
    }
}
