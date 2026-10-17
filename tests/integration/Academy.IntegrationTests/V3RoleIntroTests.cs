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
    public async Task Onboarding_metadata_follows_trusted_academy_and_live_entitlements_for_every_role_without_granting_actions()
    {
        using var owner=await Staff();using var admin=await Staff(DemoSeed.AdminEmail);using var guardian=await Mobile(AcademyRole.Guardian);using var coach=await Mobile(AcademyRole.Coach);using var other=await Staff(DemoSeed.FutureOwnerEmail);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var license=await db.AcademyFeatureEntitlements.SingleAsync(x=>x.AcademyId==DemoSeed.NogoomAcademyId&&x.Feature=="nutrition");var enabled=license.Enabled;
        try
        {
            license.Enabled=false;await db.SaveChangesAsync();
            foreach(var client in new[]{owner,admin,guardian,coach})
            {
                var http=await client.GetAsync("/api/v1/academy/experience");http.EnsureSuccessStatusCode();Assert.True(http.Headers.CacheControl!.NoStore);var response=await http.Content.ReadFromJsonAsync<JsonElement>();var intro=response.GetProperty("onboarding");
                Assert.Equal(1,intro.GetProperty("templateVersion").GetInt32());Assert.Contains(intro.GetProperty("sportTheme").GetString(),new[]{"general","football","swimming"});
                Assert.DoesNotContain(intro.GetProperty("features").EnumerateArray(),f=>f.GetString()=="nutrition");Assert.False(response.GetProperty("platformBrandOperator").GetBoolean());Assert.False(intro.TryGetProperty("players",out _));
            }
            Assert.Contains((await other.GetFromJsonAsync<JsonElement>("/api/v1/academy/experience")).GetProperty("onboarding").GetProperty("features").EnumerateArray(),f=>f.GetString()=="nutrition");
            license.Enabled=enabled;await db.SaveChangesAsync();
            Assert.Contains((await guardian.GetFromJsonAsync<JsonElement>("/api/v1/academy/experience")).GetProperty("onboarding").GetProperty("features").EnumerateArray(),f=>f.GetString()=="nutrition");
        }
        finally { license.Enabled=enabled;await db.SaveChangesAsync(); }
    }
}
