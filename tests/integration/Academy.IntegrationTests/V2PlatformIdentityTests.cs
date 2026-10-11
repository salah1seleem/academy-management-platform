using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Platform;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class V2PlatformIdentityTests : IAsyncLifetime
{
    private Factory factory = null!;
    public Task InitializeAsync() { factory = new(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Separate test database required")); _ = factory.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task Platform_cookie_is_separate_from_tenant_cookie_and_never_grants_child_access()
    {
        using var client = factory.CreateClient();
        (await Send(client, "/api/v1/auth/login", new { email = DemoSeed.OwnerEmail, password = "Demo-Only-123!" }, platform: false)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/platform/academies")).StatusCode);
        // A legacy branding grant is not sufficient to use the Owner session on platform APIs.
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var actor = await db.Users.Where(x => x.Email == DemoSeed.OwnerEmail).Select(x => x.Id).SingleAsync();
        db.PlatformBrandOperators.Add(new() { UserId = actor }); await db.SaveChangesAsync();
        try { Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/platform/academies")).StatusCode); }
        finally { await db.PlatformBrandOperators.Where(x => x.UserId == actor).ExecuteDeleteAsync(); }
        (await Login(client)).EnsureSuccessStatusCode(); // tenant cookie does not invalidate platform-specific CSRF
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/platform/academies")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/manage/structure/options")).StatusCode); // existing Owner session, not a union
        using var platformOnly = factory.CreateClient(); (await Login(platformOnly)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await platformOnly.GetAsync("/api/v1/manage/structure/options")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await platformOnly.GetAsync("/api/v1/guardian/children")).StatusCode);
        Assert.False(await db.AcademyMemberships.AnyAsync(x => x.UserId == PlatformIdentity.DemoUserId));
        (await Send(platformOnly, "/api/v1/platform/auth/logout", new { })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await platformOnly.GetAsync("/api/v1/platform/me")).StatusCode);
    }

    [Fact]
    public async Task Csrf_password_and_explicit_platform_grant_are_required()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = PlatformIdentity.DemoEmail, password = "Demo-Only-123!", code = "246810" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, "/api/v1/platform/auth/login", new { email = DemoSeed.OwnerEmail, password = "Demo-Only-123!", code = "246810" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, "/api/v1/platform/auth/login", new { email = PlatformIdentity.DemoEmail, password = "wrong", code = "246810" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, "/api/v1/platform/auth/login", new { email = PlatformIdentity.DemoEmail, password = "Demo-Only-123!", code = "000000" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/platform/audit")).StatusCode);
    }

    [Fact]
    public async Task Platform_license_changes_are_audited_and_provisioning_is_atomic_idempotent_and_inactive()
    {
        using var platform = factory.CreateClient(); (await Login(platform)).EnsureSuccessStatusCode();
        using var owner = factory.CreateClient(); (await Send(owner, "/api/v1/auth/login", new { email = DemoSeed.OwnerEmail, password = "Demo-Only-123!" }, false)).EnsureSuccessStatusCode();
        var original = await platform.GetFromJsonAsync<JsonElement>($"/api/v1/platform/academies/{DemoSeed.NogoomAcademyId}/entitlements");
        var features = original.EnumerateArray().ToDictionary(x => x.GetProperty("feature").GetString()!, x => x.GetProperty("enabled").GetBoolean());
        try
        {
            features["nutrition"] = false;
            (await Send(platform, $"/api/v1/platform/academies/{DemoSeed.NogoomAcademyId}/entitlements", new { planCode = "Trial", features }, put: true)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/content/nutrition")).StatusCode);
            var history = await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/audit");
            Assert.Contains(history.EnumerateArray(), x => x.GetProperty("action").GetString() == "academy.entitlements" && x.GetProperty("targetAcademyId").GetGuid() == DemoSeed.NogoomAcademyId);
        }
        finally { features["nutrition"] = true; (await Send(platform, $"/api/v1/platform/academies/{DemoSeed.NogoomAcademyId}/entitlements", new { planCode = "LegacyDemo", features }, put: true)).EnsureSuccessStatusCode(); }
        var request = new PlatformAcademyWrite(Guid.NewGuid(), "demo-v2-" + Guid.NewGuid().ToString("N")[..12], "أكاديمية سباحة اختبارية", "EGP", "Africa/Cairo", "مالك تجريبي", $"owner.{Guid.NewGuid():N}@example.test", "Demo-Only-123!", "swimming");
        var response = await Send(platform, "/api/v1/platform/academies", request); response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var replay = await Send(platform, "/api/v1/platform/academies", request); replay.EnsureSuccessStatusCode(); Assert.Equal(id, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(platform, "/api/v1/platform/academies", request with { ArabicName = "بيانات أخرى" })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.False(await db.Academies.Where(x => x.Id == id).Select(x => x.IsActive).SingleAsync());
        Assert.Equal(1, await db.AcademyMemberships.CountAsync(x => x.AcademyId == id && x.Role == AcademyRole.AcademyOwner));
        Assert.Equal(ConfigurationState.Draft, await db.AcademyConfigurationVersions.Where(x => x.AcademyId == id).Select(x => x.State).SingleAsync());
        Assert.False(await db.AcademyFeatureEntitlements.AnyAsync(x => x.AcademyId == id && x.Enabled));
        Assert.False(await db.Players.AnyAsync(x => x.AcademyId == id));
        Assert.False(await db.Collections.AnyAsync(x => x.AcademyId == id));
    }

    [Fact]
    public async Task Real_authenticator_requires_two_factors_rejects_replay_and_revokes_changed_security_stamp()
    {
        await using var secure = new Factory(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")!, fixedOtp: false);
        using var client = secure.CreateClient();
        await using var scope = secure.Services.CreateAsyncScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(PlatformIdentity.DemoUserId.ToString()))!;
        Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded);
        Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
        var secret = (await users.GetAuthenticatorKeyAsync(user))!;
        try
        {
            var code = Totp(secret);
            (await Send(client, "/api/v1/platform/auth/login", new { email = PlatformIdentity.DemoEmail, password = "Demo-Only-123!", code })).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/platform/me")).StatusCode);
            using var replay = secure.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(replay, "/api/v1/platform/auth/login", new { email = PlatformIdentity.DemoEmail, password = "Demo-Only-123!", code })).StatusCode);
            Assert.True((await users.UpdateSecurityStampAsync(user)).Succeeded);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/platform/me")).StatusCode);
        }
        finally { Assert.True((await users.SetTwoFactorEnabledAsync(user, false)).Succeeded); }
    }
    private static string Totp(string secret)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"; var bytes = new List<byte>(); var bits = 0; var value = 0;
        foreach (var c in secret) { value = (value << 5) | alphabet.IndexOf(c); bits += 5; if (bits >= 8) { bits -= 8; bytes.Add((byte)(value >> bits)); } }
        var step = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30); if (BitConverter.IsLittleEndian) Array.Reverse(step);
        var digest = HMACSHA1.HashData(bytes.ToArray(), step); var offset = digest[^1] & 15;
        return (((digest[offset] & 127) << 24 | digest[offset + 1] << 16 | digest[offset + 2] << 8 | digest[offset + 3]) % 1000000).ToString("D6");
    }
    private static Task<HttpResponseMessage> Login(HttpClient c) => Send(c, "/api/v1/platform/auth/login", new { email = PlatformIdentity.DemoEmail, password = "Demo-Only-123!", code = "246810" });
    private static async Task<HttpResponseMessage> Send(HttpClient c, string path, object body, bool platform = true, bool put = false)
    {
        var csrf = await c.GetFromJsonAsync<JsonElement>(platform ? "/api/v1/platform/auth/csrf" : "/api/v1/auth/csrf");
        var r = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(body) }; r.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); return await c.SendAsync(r);
    }
    private sealed class Factory(string connection, bool fixedOtp = true) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b) { b.UseEnvironment("Demo"); b.UseSetting("ConnectionStrings:Default", connection); b.UseSetting("Demo:SeedProfile", "LegacyRegression"); b.UseSetting("Demo:SeedEnabled", "true"); b.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); b.UseSetting("Demo:FixedOtpEnabled", fixedOtp.ToString()); b.UseSetting("Demo:FixedOtp", "246810"); b.UseSetting("Demo:ReferenceDate", "2026-09-28"); }
    }
}
