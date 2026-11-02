using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;
[Collection("Tenant authentication database")]
public sealed class SubscriptionHomeTests
{
    private sealed class Factory:WebApplicationFactory<Program>{protected override void ConfigureWebHost(IWebHostBuilder b){b.UseEnvironment("Demo");b.UseSetting("ConnectionStrings:Default",Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")??throw new InvalidOperationException("Separate database required"));b.UseSetting("Demo:SeedEnabled","true");b.UseSetting("Demo:SeedProfile","LegacyRegression");b.UseSetting("Demo:StaffPassword","Demo-Only-123!");b.UseSetting("Demo:FixedOtpEnabled","true");b.UseSetting("Demo:FixedOtp","246810");b.UseSetting("Imports:WorkerEnabled","false");b.UseSetting("Notifications:WorkerEnabled","false");}}
    private static async Task<HttpClient> Owner(Factory f){var c=f.CreateClient();var csrf=await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");var r=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email=DemoSeed.OwnerEmail,password="Demo-Only-123!"})};r.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());(await c.SendAsync(r)).EnsureSuccessStatusCode();return c;}
    [Fact]
    public async Task Overview_and_six_lazy_breakdowns_reconcile_with_their_lists_and_existing_snapshot_without_money()
    {
        await using var f=new Factory();using var owner=await Owner(f);const string suffix="asOf=2026-09-28";var home=await owner.GetFromJsonAsync<JsonElement>("/api/v1/subscriptions/home?"+suffix);Assert.True(home.GetProperty("expiringIncludedInActive").GetBoolean());Assert.Equal("SportEnrollmentsWithSubscriptions",home.GetProperty("unit").GetString());Assert.False(home.TryGetProperty("collections",out _));
        foreach(var state in new[]{"all","active","expiring","expired"}){var items=await owner.GetFromJsonAsync<JsonElement>("/api/v1/subscriptions/home/items?"+suffix+"&state="+state);var old=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/subscription-snapshot?"+suffix+"&state="+state);var total=home.GetProperty(state=="all"?"total":state).GetInt32();Assert.Equal(total,items.GetProperty("total").GetInt32());Assert.Equal(total,old.GetProperty("total").GetInt32());Assert.Equal(items.GetProperty("items").GetArrayLength(),items.GetProperty("items").EnumerateArray().Select(i=>i.GetProperty("id").GetGuid()).Distinct().Count());}
        foreach(var dimension in new[]{"branches","sports","packages","groups","days","ages"}){
            var response=await owner.GetAsync("/api/v1/subscriptions/home/breakdown?"+suffix+"&dimension="+dimension);response.EnsureSuccessStatusCode();var breakdown=await response.Content.ReadFromJsonAsync<JsonElement>();if(dimension=="groups")Assert.All(breakdown.GetProperty("items").EnumerateArray().Where(i=>i.GetProperty("key").GetString()!="none"),i=>Assert.False(string.IsNullOrWhiteSpace(i.GetProperty("detail").GetString())));Assert.Equal(home.GetProperty("total").GetInt32(),breakdown.GetProperty("items").EnumerateArray().Sum(i=>i.GetProperty("total").GetInt32()));
            foreach(var row in breakdown.GetProperty("items").EnumerateArray()){var key=Uri.EscapeDataString(row.GetProperty("key").GetString()!);var list=await owner.GetAsync("/api/v1/subscriptions/home/items?"+suffix+"&dimension="+dimension+"&key="+key);list.EnsureSuccessStatusCode();Assert.Equal(row.GetProperty("total").GetInt32(),(await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetInt32());}
        }
        var months=await owner.GetFromJsonAsync<JsonElement>("/api/v1/subscriptions/home/breakdown?"+suffix+"&dimension=months");Assert.False(months.GetProperty("additive").GetBoolean());Assert.Equal(home.GetProperty("total").GetInt32(),months.GetProperty("items").EnumerateArray().Last().GetProperty("total").GetInt32());
        foreach(var row in months.GetProperty("items").EnumerateArray()){var dated=await owner.GetFromJsonAsync<JsonElement>("/api/v1/subscriptions/home/items?"+suffix+"&dimension=months&key="+row.GetProperty("key").GetString());Assert.Equal(row.GetProperty("total").GetInt32(),dated.GetProperty("total").GetInt32());Assert.Equal(row.GetProperty("key").GetString(),dated.GetProperty("asOf").GetString());}
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await owner.GetAsync("/api/v1/subscriptions/home/breakdown?dimension=financial")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await owner.GetAsync("/api/v1/subscriptions/home/items?dimension=days&key=6,0")).StatusCode);
        await using var scope=f.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var foreign=await db.Branches.Where(b=>b.AcademyId==DemoSeed.FutureAcademyId).Select(b=>b.Id).FirstAsync();Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync($"/api/v1/subscriptions/home?branchId={foreign}")).StatusCode);
        var summary=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/owner-summary?from=2026-09-01&to=2026-09-28&includeSubscriptionAnalytics=false");Assert.Empty(summary.GetProperty("subscriptions").GetProperty("trend").EnumerateArray());Assert.Empty(summary.GetProperty("subscriptions").GetProperty("branches").EnumerateArray());
        foreach(var name in new[]{"total","active","expiring","expired"})Assert.Equal(home.GetProperty(name).GetInt32(),summary.GetProperty("subscriptions").GetProperty(name).GetInt32());
    }
    [Fact]
    public async Task Window_counts_and_distinct_players_match_drilldowns_without_double_counting_near_expiry()
    {
        await using var f=new Factory();using var owner=await Owner(f);
        const string filter="from=2026-09-01&to=2026-09-28";
        var home=await owner.GetFromJsonAsync<JsonElement>("/api/v1/subscriptions/home?"+filter);
        var unique=await owner.GetFromJsonAsync<JsonElement>("/api/v1/subscriptions/home/items?"+filter+"&uniquePlayers=true");
        Assert.Equal(home.GetProperty("players").GetInt32(),unique.GetProperty("total").GetInt32());
        var people=unique.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(people.Length,people.Select(p=>p.GetProperty("playerId").GetGuid()).Distinct().Count());
        Assert.All(people,p=>Assert.NotEmpty(p.GetProperty("sports").EnumerateArray()));
        var current=await owner.GetFromJsonAsync<JsonElement>("/api/v1/subscriptions/home/items?"+filter+"&state=current");
        var near=await owner.GetFromJsonAsync<JsonElement>("/api/v1/subscriptions/home/items?"+filter+"&state=expiring");
        Assert.Equal(home.GetProperty("active").GetInt32(),current.GetProperty("total").GetInt32()+near.GetProperty("total").GetInt32());
        var activeIds=current.GetProperty("items").EnumerateArray().Select(p=>p.GetProperty("id").GetGuid()).ToArray();
        Assert.DoesNotContain(near.GetProperty("items").EnumerateArray(),p=>activeIds.Contains(p.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await owner.GetAsync("/api/v1/subscriptions/home?from=2026-10-01&to=2026-09-28")).StatusCode);
        var future=await owner.GetFromJsonAsync<JsonElement>("/api/v1/subscriptions/home?from=2099-01-01&to=2099-01-31");
        Assert.Equal(0,future.GetProperty("total").GetInt32());
    }

}
