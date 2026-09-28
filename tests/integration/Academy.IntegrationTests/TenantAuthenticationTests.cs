using System.Net;
using System.Net.Http.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class TenantAuthenticationTests : IAsyncLifetime
{
    private readonly string connectionString = Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")
        ?? throw new InvalidOperationException("ACADEMY_TEST_CONNECTION_STRING is required.");
    private TenantApiFactory factory = null!;

    public Task InitializeAsync()
    {
        factory = new TenantApiFactory(connectionString);
        _ = factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task Unauthenticated_protected_request_is_rejected()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Valid_login_succeeds_and_returns_trusted_tenant()
    {
        using var client = factory.CreateClient();
        await Login(client, DemoSeed.OwnerEmail);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/v1/me");
        Assert.Equal(DemoSeed.NogoomAcademyId, me!.AcademyId);
        Assert.Equal("AcademyOwner", me.Role);
    }

    [Fact]
    public async Task Invalid_credentials_use_same_generic_failure()
    {
        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        var missing = await LoginResponse(first, "missing@example.test", "Wrong-password1!");
        var wrong = await LoginResponse(second, DemoSeed.OwnerEmail, "Wrong-password1!");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        var missingProblem = await missing.Content.ReadFromJsonAsync<ProblemResponse>();
        var wrongProblem = await wrong.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal(missingProblem!.Title, wrongProblem!.Title);
        Assert.Equal(missingProblem.Detail, wrongProblem.Detail);
    }

    [Fact]
    public async Task Academy_A_user_can_access_A_probe()
    {
        using var client = factory.CreateClient(); await Login(client, DemoSeed.OwnerEmail);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/tenant/probe/{DemoSeed.NogoomAcademyId}")).StatusCode);
    }

    [Fact]
    public async Task Academy_A_user_cannot_access_Academy_B_probe()
    {
        using var client = factory.CreateClient(); await Login(client, DemoSeed.OwnerEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/tenant/probe/{DemoSeed.FutureAcademyId}")).StatusCode);
    }

    [Fact]
    public async Task Academy_identifier_tampering_cannot_switch_context()
    {
        using var client = factory.CreateClient(); await Login(client, DemoSeed.OwnerEmail);
        var csrf = await Csrf(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/session/academy")
        {
            Content = JsonContent.Create(new { academyId = DemoSeed.FutureAcademyId })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("X-Academy-Id", DemoSeed.FutureAcademyId.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/v1/me");
        Assert.Equal(DemoSeed.NogoomAcademyId, me!.AcademyId);
    }

    [Fact]
    public async Task Inactive_membership_is_rejected()
    {
        using var client = factory.CreateClient(); await Login(client, DemoSeed.OwnerEmail);
        await SetOwnerMembershipActive(false);
        try { Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/me")).StatusCode); }
        finally { await SetOwnerMembershipActive(true); }
    }

    [Fact]
    public async Task Coach_cannot_provision_staff()
    {
        using var client = factory.CreateClient(); await Login(client, DemoSeed.CoachEmail);
        var csrf = await Csrf(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/staff")
        {
            Content = JsonContent.Create(new { email = "new@example.test", displayName = "حساب تجريبي", temporaryPassword = "Temporary-123!", role = 3 })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_server_session()
    {
        using var client = factory.CreateClient(); await Login(client, DemoSeed.OwnerEmail);
        var csrf = await Csrf(client);
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(logout)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Demo_guardian_otp_authenticates_only_the_guardian_account()
    {
        using var client = factory.CreateClient();
        var csrf = await Csrf(client);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/guardian/otp/request") { Content = JsonContent.Create(new { phoneNumber = "01000000001" }) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var challenge = await response.Content.ReadFromJsonAsync<ChallengeResponse>();
        csrf = await Csrf(client);
        var verify = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/guardian/otp/verify")
        {
            Content = JsonContent.Create(new { challengeId = challenge!.ChallengeId, phoneNumber = DemoSeed.GuardianPhone, code = "246810" })
        };
        verify.Headers.Add("X-CSRF-TOKEN", csrf);
        (await client.SendAsync(verify)).EnsureSuccessStatusCode();
        var me = await client.GetFromJsonAsync<MeResponse>("/api/v1/me");
        Assert.Equal("Guardian", me!.Role);
    }

    [Fact]
    public void Fixed_demo_otp_is_refused_outside_Demo()
    {
        var environment = new StubEnvironment { EnvironmentName = Environments.Production };
        var options = new DemoOptions { FixedOtpEnabled = true, FixedOtp = "246810" };
        Assert.Throws<InvalidOperationException>(() => DemoSeed.ValidateEnvironment(environment, options));
    }

    [Fact]
    public async Task Demo_seed_rerun_is_idempotent()
    {
        await using var beforeScope = factory.Services.CreateAsyncScope();
        var beforeDb = beforeScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var academiesBefore = await beforeDb.Academies.CountAsync();
        var membershipsBefore = await beforeDb.AcademyMemberships.CountAsync();
        await DemoSeed.SeedAsync(factory.Services);
        await DemoSeed.SeedAsync(factory.Services);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(academiesBefore, await db.Academies.CountAsync());
        Assert.Equal(membershipsBefore, await db.AcademyMemberships.CountAsync());
    }

    private async Task SetOwnerMembershipActive(bool active)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var ownerId = await db.Users.Where(x => x.NormalizedEmail == DemoSeed.OwnerEmail.ToUpperInvariant()).Select(x => x.Id).SingleAsync();
        var membership = await db.AcademyMemberships.SingleAsync(x => x.UserId == ownerId && x.AcademyId == DemoSeed.NogoomAcademyId);
        membership.IsActive = active; await db.SaveChangesAsync();
    }

    private static async Task Login(HttpClient client, string email)
    {
        var response = await LoginResponse(client, email, "Demo-Only-123!");
        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> LoginResponse(HttpClient client, string email, string password)
    {
        var token = await Csrf(client);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email, password }) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static async Task<string> Csrf(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        return response!.Token;
    }

    private sealed record CsrfResponse(string Token);
    private sealed record MeResponse(Guid AcademyId, string Role);
    private sealed record ChallengeResponse(Guid ChallengeId);
    private sealed record ProblemResponse(string Title, string Detail);

    private sealed class TenantApiFactory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Demo");
            builder.UseSetting("ConnectionStrings:Default", connection);
            builder.UseSetting("Demo:SeedEnabled", "true");
            builder.UseSetting("Demo:FixedOtpEnabled", "true");
            builder.UseSetting("Demo:FixedOtp", "246810");
            builder.UseSetting("Demo:StaffPassword", "Demo-Only-123!");
        }
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

[CollectionDefinition("Tenant authentication database", DisableParallelization = true)]
public sealed class TenantAuthenticationCollection;
