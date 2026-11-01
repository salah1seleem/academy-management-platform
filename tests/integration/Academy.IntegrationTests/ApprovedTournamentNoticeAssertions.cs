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
    private async Task AssertTournamentNoticeSource(HttpClient guardian, Guid payment, Guid recipient)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var notices = await db.AcademyNotices.AsNoTracking()
            .Where(n => n.AcademyId == DemoSeed.NogoomAcademyId && n.TargetKind == "Tournament" && n.TargetId == payment)
            .ToArrayAsync();
        var notice = Assert.Single(notices);
        Assert.Equal(recipient, notice.UserId);
        Assert.Equal("Guardian", notice.AudienceRole);
        Assert.Equal("Payment", notice.EventKey);

        async Task<JsonElement[]> Inbox(HttpClient client)
        {
            var all = new List<JsonElement>();
            for (var page = 1; page <= 100; page++)
            {
                var result = await client.GetFromJsonAsync<JsonElement>($"/api/v1/notifications?page={page}");
                all.AddRange(result.GetProperty("items").EnumerateArray());
                if (all.Count >= result.GetProperty("total").GetInt32()) return all.ToArray();
            }
            throw new InvalidOperationException("Test inbox exceeded the bounded pagination fixture.");
        }

        var own = Assert.Single(await Inbox(guardian), n => n.GetProperty("id").GetGuid() == notice.Id);
        Assert.Equal("Tournament", own.GetProperty("targetKind").GetString());
        Assert.Equal(payment, own.GetProperty("targetId").GetGuid());
        Assert.False(own.GetProperty("isRead").GetBoolean());
        var source = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/tournament-payments/{payment}");
        Assert.Equal("Confirmed", source.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(source.GetProperty("receiptNumber").GetString()));

        using var coach = await Mobile(AcademyRole.Coach);
        using var otherAcademy = await Staff(DemoSeed.FutureOwnerEmail);
        foreach (var foreign in new[] { coach, otherAcademy })
        {
            Assert.DoesNotContain(await Inbox(foreign), n => n.GetProperty("id").GetGuid() == notice.Id);
            Assert.Equal(HttpStatusCode.NotFound,
                (await Send(foreign, $"/api/v1/notifications/{notice.Id}/read", new { isRead = true }, true)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(guardian, $"/api/v1/notifications/{notice.Id}/read", new { isRead = true }, true)).StatusCode);
        Assert.True((await Inbox(guardian)).Single(n => n.GetProperty("id").GetGuid() == notice.Id)
            .GetProperty("isRead").GetBoolean());
        Assert.Equal(1, await db.AcademyNotices.CountAsync(n => n.AcademyId == DemoSeed.NogoomAcademyId &&
            n.TargetKind == "Tournament" && n.TargetId == payment));
    }
}
