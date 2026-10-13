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
    public async Task Player_name_search_does_not_match_unrelated_guardians_without_a_contact_phone()
    {
        await using var family = await NewFamily();
        using var owner = await InboxStaff(DemoSeed.OwnerEmail);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var enrollment = await db.SportEnrollments.SingleAsync(e => e.Id == family.Choices[0].SportEnrollmentId);
        var player = await db.Players.SingleAsync(p => p.Id == enrollment.PlayerId);
        player.ArabicName = "طفل بحث اصطناعي " + Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync();
        Assert.Null((await db.GuardianProfiles.SingleAsync(g => g.Id == family.Guardian)).ContactPhone);
        var rows = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/players?search={Uri.EscapeDataString(" " + player.ArabicName + " ")}");
        Assert.Equal(player.Id, Assert.Single(rows.EnumerateArray()).GetProperty("id").GetGuid());
        var missing = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/players?search={Guid.NewGuid():N}");
        Assert.Empty(missing.EnumerateArray());
        var guardian = await db.GuardianProfiles.SingleAsync(g => g.Id == family.Guardian);
        guardian.ContactPhone = await FamilyPhone(family);
        await db.SaveChangesAsync();
        var phoneRows = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/players?search={Uri.EscapeDataString(guardian.ContactPhone)}");
        var expected = await db.SportEnrollments.Where(e => family.Choices.Select(c => c.SportEnrollmentId).Contains(e.Id)).Select(e => e.PlayerId).ToArrayAsync();
        Assert.Equal(expected.Order(), phoneRows.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).Order());
    }
}
