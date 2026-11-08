using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Academy.Api.Platform;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class PlatformAdministratorBootstrapTests : IAsyncLifetime
{
    private const string Password = "Synthetic-Bootstrap-2026!";
    private readonly string database = "academy_test_bootstrap_" + Guid.NewGuid().ToString("N");
    private string control = null!;
    private Factory factory = null!;
    private bool created;

    public async Task InitializeAsync()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Explicit isolated test connection required"));
        if (connection.Host is not ("localhost" or "127.0.0.1" or "::1")
            || connection.Database?.StartsWith("academy_test", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("Bootstrap tests require a loopback academy_test database; remote targets refused");
        connection.Pooling = false;
        control = connection.ConnectionString;
        await using var admin = new NpgsqlConnection(control); await admin.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin)) await command.ExecuteNonQueryAsync();
        created = true;
        connection.Database = database;
        factory = new Factory(connection.ConnectionString);
        factory.ClientOptions.BaseAddress = new Uri("https://localhost");
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync();
        if (!created) return;
        await using var admin = new NpgsqlConnection(control); await admin.OpenAsync();
        // Only the unique database created by this test instance is disposable.
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<Guid> Create(string email, Func<string, Task<string>> confirmation, string password = Password)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await PlatformAdministratorBootstrap.CreateFirst(scope.ServiceProvider.GetRequiredService<FoundationDbContext>(),
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), email, "مسؤول اصطناعي", password, confirmation);
    }

    [Fact]
    public async Task Enrollment_is_atomic_requires_totp_and_never_promotes_or_resets_existing_users()
    {
        const string email = "first-admin@example.test";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(email, _ => Task.FromResult("not-a-code")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(email, _ => throw new InvalidOperationException("synthetic cancel")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(email, _ => throw new Exception("must not prompt"), "weak"));
        await using (var check = factory.Services.CreateAsyncScope())
        {
            var db = check.ServiceProvider.GetRequiredService<FoundationDbContext>();
            Assert.Empty(await db.Users.ToListAsync()); Assert.Empty(await db.PlatformAdministrators.ToListAsync());
            Assert.Empty(await db.PlatformAudits.ToListAsync()); Assert.Empty(await db.PlatformMfaUses.ToListAsync());
            var users = check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.CreateAsync(new() { Id = Guid.NewGuid(), UserName = "owner@example.test", Email = "owner@example.test", DisplayName = "مالك قائم" }, Password)).Succeeded);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create("owner@example.test", _ => throw new Exception("must not prompt")));
        string secret = "", enrolledCode = "";
        var id = await Create(email, key => { secret = key; enrolledCode = Totp(key, -1); return Task.FromResult(enrolledCode); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create("another@example.test", _ => throw new Exception("must not prompt")));
        await using (var check = factory.Services.CreateAsyncScope())
        {
            var db = check.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var users = check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(id.ToString()))!;
            Assert.True(user.TwoFactorEnabled); Assert.True(await users.CheckPasswordAsync(user, Password));
            Assert.Single(await db.PlatformAdministrators.ToListAsync());
            Assert.Empty(await db.AcademyMemberships.Where(m => m.UserId == id).ToListAsync());
            var audit = Assert.Single(await db.PlatformAudits.ToListAsync());
            Assert.Equal("platform.bootstrap", audit.Action);
            Assert.DoesNotContain(secret, audit.Details); Assert.DoesNotContain(Password, audit.Details);
        }
        using var replay = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(replay, email, enrolledCode)).StatusCode);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, email, "invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, email, Totp(secret))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/platform/me")).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/platform/me")).StatusCode);
        await using (var revoke = factory.Services.CreateAsyncScope())
        {
            var db = revoke.ServiceProvider.GetRequiredService<FoundationDbContext>();
            (await db.PlatformAdministrators.SingleAsync()).IsActive = false; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/platform/me")).StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create("replacement@example.test", _ => throw new Exception("must not prompt")));
    }

    [Fact]
    public async Task Concurrent_bootstrap_creates_exactly_one_administrator()
    {
        async Task<bool> Attempt(string email)
        {
            try { await Create(email, key => Task.FromResult(Totp(key))); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var outcomes = await Task.WhenAll(Attempt("admin-a@example.test"), Attempt("admin-b@example.test"));
        Assert.Single(outcomes, x => x);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Single(await db.PlatformAdministrators.ToListAsync());
        Assert.Single(await db.Users.ToListAsync()); Assert.Single(await db.PlatformAudits.ToListAsync());
    }

    private static async Task<HttpResponseMessage> Login(HttpClient client, string email, string code)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/auth/login")
        { Content = JsonContent.Create(new { email, password = Password, code }) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    private static string Totp(string secret, int offset = 0)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>(); var bits = 0; var value = 0;
        foreach (var c in secret) { value = (value << 5) | alphabet.IndexOf(c); bits += 5; if (bits >= 8) { bits -= 8; bytes.Add((byte)(value >> bits)); } }
        var step = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 + offset);
        if (BitConverter.IsLittleEndian) Array.Reverse(step);
        var digest = HMACSHA1.HashData(bytes.ToArray(), step); var index = digest[^1] & 15;
        return (((digest[index] & 127) << 24 | digest[index + 1] << 16 | digest[index + 2] << 8 | digest[index + 3]) % 1000000).ToString("D6");
    }

    private sealed class Factory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing"); builder.UseSetting("ConnectionStrings:Default", connection);
            builder.UseSetting("Demo:SeedEnabled", "false"); builder.UseSetting("Demo:FixedOtpEnabled", "false");
            builder.UseSetting("Imports:WorkerEnabled", "false"); builder.UseSetting("Notifications:WorkerEnabled", "false");
        }
    }
}
