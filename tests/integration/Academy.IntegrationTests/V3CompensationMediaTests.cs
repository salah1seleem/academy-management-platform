using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    [Fact] public async Task Session_media_compensation_guest_is_included_only_for_actual_reserved_session_and_cancellation_revokes_access()
    {
        using var owner=await Staff();var f=await CompensationReady(owner);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        (await db.TrainingSessions.SingleAsync(s=>s.Id==f.TargetSession)).SessionDate=PlacementToday;await db.SaveChangesAsync();
        var grant=await CompensationGrant(owner,f.F.First,f.F.Past);var booked=await CompensationCommand(owner,$"/api/v1/people/compensations/{grant}/booking",await CompensationBookReview(owner,grant,f.TargetSession));booked.EnsureSuccessStatusCode();var booking=(await booked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var parent=await Guardian();var child=await PlayerFor(f.F.First);(await Write(parent,$"/api/v1/guardian/children/{child}/group-media-consent",new GroupMediaConsentWrite(true,0,Guid.NewGuid()))).EnsureSuccessStatusCode();
        var regular=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/assets/media-audience?groupId={f.Target}");Assert.DoesNotContain(regular.GetProperty("players").EnumerateArray(),p=>p.GetProperty("id").GetGuid()==child);
        var actual=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/assets/media-audience?groupId={f.Target}&sessionId={f.TargetSession}");Assert.True(actual.GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("id").GetGuid()==child).GetProperty("consented").GetBoolean());
        var before=(await db.PlayerAttendances.CountAsync(),await db.CompensationMovements.CountAsync(),await db.SubscriptionSessionMovements.CountAsync());var money=await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28");
        var id=await BrandingMatchesTests.MediaCreated(await BrandingMatchesTests.PrivacyUpload(owner,f.Target,PlacementToday,child,session:f.TargetSession));var url=$"/api/v1/assets/{id}";Assert.Equal(HttpStatusCode.OK,(await parent.GetPrivateAssetAsync(factory,url)).StatusCode);Assert.Contains(id.ToString(),await parent.GetStringAsync($"/api/v1/assets/gallery?playerId={child}"));
        var row=await db.CompensationBookings.SingleAsync(b=>b.Id==booking);(await Write(owner,$"/api/v1/people/compensation-bookings/{booking}/cancel",new CompensationCancelWrite(row.Version))).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await parent.GetPrivateAssetAsync(factory,url)).StatusCode);Assert.DoesNotContain(id.ToString(),await parent.GetStringAsync($"/api/v1/assets/gallery?playerId={child}"));Assert.Equal(HttpStatusCode.OK,(await owner.GetPrivateAssetAsync(factory,url)).StatusCode);
        Assert.Equal(before,(await db.PlayerAttendances.CountAsync(),await db.CompensationMovements.CountAsync(),await db.SubscriptionSessionMovements.CountAsync()));Assert.Equal(money,await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
    }
}
