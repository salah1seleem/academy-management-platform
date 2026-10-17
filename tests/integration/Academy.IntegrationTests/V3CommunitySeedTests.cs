using Academy.Api.Auth;
using Academy.Api.Demo;
using Academy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Configuration;

namespace Academy.IntegrationTests;
[Collection("Tenant authentication database")]
public sealed class V3CommunitySeedTests
{
    private sealed class Factory(string profile="Football"):WebApplicationFactory<Program>{protected override void ConfigureWebHost(IWebHostBuilder b){b.UseEnvironment("Demo");b.UseSetting("ConnectionStrings:Default",Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")??throw new InvalidOperationException("Isolated DB required"));b.UseSetting("Demo:SeedEnabled","true");b.UseSetting("Demo:SeedProfile",profile);b.UseSetting("Demo:CommunityCampaignSeedEnabled",profile=="Football"?"true":"false");b.UseSetting("Demo:StaffPassword","Demo-Only-123!");b.UseSetting("Demo:ReferenceDate","2026-09-28");}}
    private sealed class ProductionEnvironment:IWebHostEnvironment{public string EnvironmentName{get;set;}="Production";public string ApplicationName{get;set;}="Synthetic seed rejection";public string WebRootPath{get;set;}="";public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();public string ContentRootPath{get;set;}="";public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();}
    [Fact] public async Task Community_seed_is_synthetic_optional_repeatable_and_does_not_restore_withdrawn_consent_or_invent_views()
    {
        int collections;Guid[] campaigns;
        await using(var isolation=new Factory("LegacyRegression")){using var client=isolation.CreateClient();} // Explicit tenant B fixture; no dependence on xUnit case order.
        await using(var first=new Factory())
        {
            using var c=first.CreateClient();await using var scope=first.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var a=FootballDemoSeed.AcademyId;
            campaigns=await db.CommunityCampaigns.Where(c=>c.AcademyId==a).OrderBy(c=>c.Id).Select(c=>c.Id).ToArrayAsync();Assert.Equal(6,campaigns.Length);Assert.Equal(7,await db.CommunityCampaignRevisions.CountAsync(r=>r.AcademyId==a));Assert.Equal(0,await db.CommunityAdEvents.CountAsync(e=>e.AcademyId==a));collections=await db.Collections.CountAsync();
            var urgent=await db.AcademyNotices.SingleAsync(n=>n.Id==FootballDemoSeed.Id("community-demo/urgent-notice"));Assert.Equal(FootballDemoSeed.Id("community-demo/urgent-match"),urgent.MatchId);Assert.True(urgent.UrgentUntilUtc>TimeProvider.System.GetUtcNow());Assert.Equal(FootballDemoSeed.Id("user/guardian12"),urgent.UserId);Assert.StartsWith("تنبيه موعد اصطناعي",urgent.Message);
            var p=await db.GuardianSponsorPreferences.SingleAsync(p=>p.AcademyId==a&&p.UserId==FootballDemoSeed.Id("user/guardian12"));p.InAppOptIn=false;var l=await db.AcademyFeatureEntitlements.SingleAsync(l=>l.AcademyId==a&&l.Feature=="community_ads");l.Enabled=false;await db.SaveChangesAsync();
            Assert.False(await db.AcademyFeatureEntitlements.Where(l=>l.AcademyId==DemoSeed.NogoomAcademyId&&l.Feature=="community_ads").Select(l=>l.Enabled).SingleAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(()=>CommunityCampaignDemoSeed.Seed(db,new ProductionEnvironment(),new DemoOptions{SeedEnabled=true,SeedProfile="Football"},TimeProvider.System,scope.ServiceProvider.GetRequiredService<IConfiguration>()));
        }
        await using(var second=new Factory())
        {
            using var c=second.CreateClient();await using var scope=second.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var a=FootballDemoSeed.AcademyId;Assert.Equal(campaigns,await db.CommunityCampaigns.Where(c=>c.AcademyId==a).OrderBy(c=>c.Id).Select(c=>c.Id).ToArrayAsync());Assert.Equal(7,await db.CommunityCampaignRevisions.CountAsync(r=>r.AcademyId==a));Assert.Equal(0,await db.CommunityAdEvents.CountAsync(e=>e.AcademyId==a));Assert.Equal(collections,await db.Collections.CountAsync());Assert.False(await db.AcademyFeatureEntitlements.Where(l=>l.AcademyId==a&&l.Feature=="community_ads").Select(l=>l.Enabled).SingleAsync());Assert.False(await db.GuardianSponsorPreferences.Where(p=>p.AcademyId==a&&p.UserId==FootballDemoSeed.Id("user/guardian12")).Select(p=>p.InAppOptIn).SingleAsync());
        }
    }
}
