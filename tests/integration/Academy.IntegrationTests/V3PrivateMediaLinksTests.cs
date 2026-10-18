using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact] public async Task Private_media_links_issue_no_store_tickets_but_never_replace_identity_scope_or_live_withdrawal()
    {
        using var owner=await Staff();var family=await ExclusiveMediaFamily();using var parent=family.Client;
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var source=await db.SportEnrollments.AsNoTracking().FirstAsync(e=>e.AcademyId==DemoSeed.NogoomAcademyId&&e.IsActive&&e.TrainingGroupId!=null);var e=await FreshMediaChild(db,source,family.Guardian);var group=e.TrainingGroupId!.Value;var today=DateOnly.Parse((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/assets/media-audience?groupId={group}")).GetProperty("asOfDate").GetString()!);
        (await Send(parent,$"/api/v1/guardian/children/{e.PlayerId}/group-media-consent",new GroupMediaConsentWrite(true,0,Guid.NewGuid()))).EnsureSuccessStatusCode();var id=await MediaCreated(await PrivacyUpload(owner,group,today,e.PlayerId));var url=$"/api/v1/assets/{id}";
        using var raw=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});raw.DefaultRequestHeaders.Authorization=parent.DefaultRequestHeaders.Authorization;
        var redirect=await raw.GetAsync(url);Assert.Equal(HttpStatusCode.Redirect,redirect.StatusCode);Assert.Contains("no-store",redirect.Headers.CacheControl!.ToString());Assert.Equal("no-referrer",redirect.Headers.GetValues("Referrer-Policy").Single());var ticket=redirect.Headers.Location!.ToString();Assert.StartsWith(url+"?access=ML1.",ticket);
var bytes=await raw.GetAsync(ticket);Assert.Equal(HttpStatusCode.OK,bytes.StatusCode);await AssertMediaPixel(await bytes.Content.ReadAsByteArrayAsync());Assert.Contains("no-store",bytes.Headers.CacheControl!.ToString());Assert.Equal("same-origin",bytes.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync(ticket)).StatusCode); // Even a different authorized reader cannot reuse it.
        using var anonymous=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(ticket)).StatusCode);
        var member=await db.MobileSessions.Where(s=>s.AccessTokenHash==Academy.Api.Mobile.MobileAuthenticationHandler.Hash(raw.DefaultRequestHeaders.Authorization!.Parameter!)).Select(s=>s.MembershipId).SingleAsync();var m=await db.AcademyMemberships.SingleAsync(m=>m.Id==member);m.ContextRevision++;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.NotFound,(await raw.GetAsync(ticket)).StatusCode);
        var freshRedirect=await raw.GetAsync(url);Assert.Equal(HttpStatusCode.Redirect,freshRedirect.StatusCode);var fresh=freshRedirect.Headers.Location!.ToString();Assert.Equal(HttpStatusCode.OK,(await raw.GetAsync(fresh)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await raw.GetAsync(url+"?access=ML1.invalid")).StatusCode);
        var currentSession=await db.MobileSessions.SingleAsync(s=>s.AccessTokenHash==Academy.Api.Mobile.MobileAuthenticationHandler.Hash(raw.DefaultRequestHeaders.Authorization!.Parameter!));var otherToken=Academy.Api.Mobile.MobileAuthenticationHandler.NewToken();var now=DateTimeOffset.UtcNow;
        db.MobileSessions.Add(new(){Id=Guid.NewGuid(),UserId=currentSession.UserId,MembershipId=currentSession.MembershipId,DeviceName="جهاز رابط اصطناعي آخر",AccessTokenHash=Academy.Api.Mobile.MobileAuthenticationHandler.Hash(otherToken),RefreshTokenHash=Academy.Api.Mobile.MobileAuthenticationHandler.Hash(Guid.NewGuid().ToString()),SecurityStamp=currentSession.SecurityStamp,CreatedAtUtc=now,LastUsedAtUtc=now,AccessExpiresAtUtc=now.AddMinutes(10),ExpiresAtUtc=now.AddHours(1)});await db.SaveChangesAsync();
        using var otherSession=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});otherSession.DefaultRequestHeaders.Authorization=new("Bearer",otherToken);Assert.Equal(HttpStatusCode.Redirect,(await otherSession.GetAsync(url)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await otherSession.GetAsync(fresh)).StatusCode);
        (await Send(owner,url+"/withdraw",new MediaWithdrawalWrite(Guid.NewGuid(),null))).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await raw.GetAsync(fresh)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await raw.GetAsync(url)).StatusCode);Assert.Equal(HttpStatusCode.OK,(await owner.GetAsync(url)).StatusCode);
        currentSession.RevokedAtUtc=DateTimeOffset.UtcNow;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.Unauthorized,(await raw.GetAsync(fresh)).StatusCode);
    }
}
