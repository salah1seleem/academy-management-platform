using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class V2ConfigurationIntegrationTests : IAsyncLifetime
{
    private Factory factory = null!;
    public Task InitializeAsync() { factory = new(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Separate test database required")); _ = factory.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task Draft_validate_stage_activate_preserves_history_and_revokes_server_access()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); using var other = await Staff(DemoSeed.FutureOwnerEmail);
        var original = await Current();
        var settings = AcademyRuntimeSettings.Read(original.Payload);
        var changed = settings with { Features = new(settings.Features), ConsecutiveAbsenceAlert = 4 }; changed.Features["nutrition"] = false;
        var id = await Draft(owner, changed);
        Assert.Equal(id, await Draft(owner, changed)); // repeat submit is one draft
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/v1/content/nutrition")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Change(other, id, ConfigurationState.Validated)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Change(owner, id, ConfigurationState.Active)).StatusCode);
        try
        {
            var initialVersion = await Version(id);
            (await Change(owner, id, ConfigurationState.Validated)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/manage/configuration/{id}/transition", new { target = "Staged", version = initialVersion })).StatusCode);
            (await Change(owner, id, ConfigurationState.Staged, DateTimeOffset.UtcNow.AddDays(1))).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Conflict, (await Change(owner, id, ConfigurationState.Active)).StatusCode);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
                var row = await db.AcademyConfigurationVersions.SingleAsync(x => x.Id == id);
                row.EffectiveAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
            }
            (await Change(owner, id, ConfigurationState.Active)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/content/nutrition")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Write(owner, "/api/v1/content/nutrition", new { arabicName = "لا يكتب" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/v1/content/nutrition")).StatusCode);
            var features = await owner.GetFromJsonAsync<JsonElement>("/api/v1/academy/features"); Assert.DoesNotContain("nutrition", features.GetProperty("features").EnumerateArray().Select(x => x.GetString()));
            var history = await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/configuration/");
            Assert.Contains(history.GetProperty("versions").EnumerateArray(), x => x.GetProperty("id").GetGuid() == original.Id && x.GetProperty("state").GetString() == "Superseded");
            Assert.Contains(history.GetProperty("history").EnumerateArray(), x => x.GetProperty("configurationId").GetGuid() == id && x.GetProperty("to").GetString() == "Active");
        }
        finally { await Activate(owner, await Draft(owner, settings)); }
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/v1/content/nutrition")).StatusCode);
    }

    [Fact]
    public async Task Tenant_owner_cannot_self_license_premium_or_silently_change_financial_currency()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); using var admin = await Staff(DemoSeed.AdminEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/manage/configuration/")).StatusCode);
        var settings = AcademyRuntimeSettings.Read((await Current()).Payload); var changed = settings with { Features = new(settings.Features) }; changed.Features["community_ads"] = true;
        var id = await Draft(owner, changed); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Change(owner, id, ConfigurationState.Validated)).StatusCode);
        var currency = await Draft(owner, settings with { Currency = "USD" }); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Change(owner, currency, ConfigurationState.Validated)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AcademyConfigurationService>().Enabled(DemoSeed.NogoomAcademyId, "unrecognized"));
        Assert.False(await db.AcademyFeatureEntitlements.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Feature == "community_ads").Select(x => x.Enabled).SingleAsync());
    }

    [Fact]
    public async Task License_downgrade_and_suspended_configuration_fail_closed_without_deleting_history()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var license = await db.AcademyFeatureEntitlements.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Feature == "nutrition");
        var active = await db.AcademyConfigurationVersions.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.State == ConfigurationState.Active);
        var meals = await db.NutritionItems.CountAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId);
        try
        {
            license.Enabled = false; await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/content/nutrition")).StatusCode);
            license.Enabled = true; active.State = ConfigurationState.Suspended; await db.SaveChangesAsync();
            await AcademyConfigurationService.SeedDemo(db, [DemoSeed.NogoomAcademyId]);
            Assert.False(await db.AcademyConfigurationVersions.AnyAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.State == ConfigurationState.Active));
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/content/nutrition")).StatusCode);
            Assert.Equal(meals, await db.NutritionItems.CountAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId));
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/v1/manage/configuration/")).StatusCode);
        }
        finally { license.Enabled = true; active.State = ConfigurationState.Active; await db.SaveChangesAsync(); }
    }

    private async Task<AcademyConfigurationVersion> Current() { await using var s = factory.Services.CreateAsyncScope(); return await s.ServiceProvider.GetRequiredService<FoundationDbContext>().AcademyConfigurationVersions.AsNoTracking().SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.State == ConfigurationState.Active); }
    private async Task<uint> Version(Guid id) { await using var s = factory.Services.CreateAsyncScope(); return await s.ServiceProvider.GetRequiredService<FoundationDbContext>().AcademyConfigurationVersions.Where(x => x.Id == id).Select(x => x.Version).SingleAsync(); }
    private static async Task<Guid> Draft(HttpClient c, AcademyRuntimeSettings body) { var r = await Write(c, "/api/v1/manage/configuration/drafts", body); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); }
    private Task<HttpResponseMessage> Change(HttpClient c, Guid id, ConfigurationState target, DateTimeOffset? effective = null) => ChangeCore(c, id, target, effective);
    private async Task<HttpResponseMessage> ChangeCore(HttpClient c, Guid id, ConfigurationState target, DateTimeOffset? effective) => await Write(c, $"/api/v1/manage/configuration/{id}/transition", new { target = target.ToString(), version = await Version(id), effectiveAtUtc = effective });
    private async Task Activate(HttpClient c, Guid id) { foreach (var state in new[] { ConfigurationState.Validated, ConfigurationState.Staged, ConfigurationState.Active }) (await Change(c, id, state)).EnsureSuccessStatusCode(); }
    private async Task<HttpClient> Staff(string email) { var c = factory.CreateClient(); (await Write(c, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" })).EnsureSuccessStatusCode(); return c; }
    private static async Task<HttpResponseMessage> Write(HttpClient c, string path, object body) { var csrf = await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); var r = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; r.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); return await c.SendAsync(r); }
    private sealed class Factory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b) { b.UseEnvironment("Demo"); b.UseSetting("ConnectionStrings:Default", connection); b.UseSetting("Demo:SeedProfile", "LegacyRegression"); b.UseSetting("Demo:SeedEnabled", "true"); b.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); b.UseSetting("Demo:FixedOtpEnabled", "true"); b.UseSetting("Demo:FixedOtp", "246810"); b.UseSetting("Demo:ReferenceDate", "2026-09-28"); }
    }
}
