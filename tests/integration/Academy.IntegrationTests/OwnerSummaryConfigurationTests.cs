using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Configuration;
using Academy.Infrastructure.Finance;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class OwnerSummaryConfigurationTests : IAsyncLifetime
{
    private const string Password = "Synthetic-Owner-Summary-123!";
    private const string Period = "from=2026-11-02&to=2026-11-02&includeSubscriptionAnalytics=false";
    private Factory factory = null!;
    private static Guid Id(string name) => new(SHA256.HashData(Encoding.UTF8.GetBytes("owner-summary-regression:" + name))[..16]);

    public Task InitializeAsync()
    {
        factory = new Factory(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Separate PostgreSQL test database required"));
        factory.ClientOptions.BaseAddress = new Uri("https://localhost");
        return Task.CompletedTask;
    }
    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Theory]
    [InlineData(null)]
    [InlineData(ConfigurationState.Draft)]
    [InlineData(ConfigurationState.Suspended)]
    [InlineData(ConfigurationState.Active)]
    public async Task Empty_academy_with_optional_configuration_has_a_real_summary(ConfigurationState? state)
    {
        var key = "empty-" + (state?.ToString() ?? "missing");
        var fixture = await Academy(key);
        if (state.HasValue)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var payload = AcademyRuntimeSettings.Initial("EGP", "Africa/Cairo", preserveExisting: false).Serialize();
            if (!await db.AcademyConfigurationVersions.AnyAsync(v => v.AcademyId == fixture.AcademyId))
            {
                db.AcademyConfigurationVersions.Add(new() { Id = Id(key + ":config"), AcademyId = fixture.AcademyId,
                    Revision = 1, State = state.Value, Payload = payload, ContentHash = AcademyConfigurationService.Hash(payload) });
                await db.SaveChangesAsync();
            }
        }
        using var client = await Login(fixture);
        var summary = await Summary(client, Period);
        Assert.Equal("2026-11-02", summary.GetProperty("from").GetString());
        Assert.Equal("2026-11-02", summary.GetProperty("to").GetString());
        Assert.Equal(0m, summary.GetProperty("collections").GetProperty("total").GetDecimal());
        Assert.Equal(0, summary.GetProperty("paymentRequests").GetProperty("total").GetInt32());
        Assert.Empty(summary.GetProperty("operations").GetProperty("underAssignedCoaches").EnumerateArray());
        await using var check = factory.Services.CreateAsyncScope();
        var checkDb = check.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(state == ConfigurationState.Active,
            await checkDb.AcademyConfigurationVersions.AnyAsync(v => v.AcademyId == fixture.AcademyId && v.State == ConfigurationState.Active));
    }

    [Fact]
    public async Task Missing_configuration_preserves_real_money_local_date_boundaries_and_tenant_isolation()
    {
        var a = await Academy("money-a"); var b = await Academy("money-b");
        // Cairo is UTC+2 on this local day. Include the start, exclude the next midnight.
        await Receipt(a, "before", 11m, "2026-11-01T21:59:59Z");
        await Receipt(a, "start", 25m, "2026-11-01T22:00:00Z");
        await Receipt(a, "end", 37m, "2026-11-02T22:00:00Z");
        await Receipt(b, "foreign", 9000m, "2026-11-01T22:00:00Z");
        using var owner = await Login(a); using var other = await Login(b);
        var summary = await Summary(owner, Period);
        Assert.Equal(25m, summary.GetProperty("collections").GetProperty("total").GetDecimal());
        Assert.Equal(25m, summary.GetProperty("collections").GetProperty("today").GetDecimal());
        Assert.Equal(9000m, (await Summary(other, Period)).GetProperty("collections").GetProperty("total").GetDecimal());
        Assert.Equal(11m, (await Summary(owner, "from=2026-11-01&to=2026-11-01")).GetProperty("collections").GetProperty("total").GetDecimal());
        Assert.Equal(37m, (await Summary(owner, "from=2026-11-03&to=2026-11-03")).GetProperty("collections").GetProperty("total").GetDecimal());
        Assert.Equal(0m, (await Summary(other, Period + "&branchId=" + a.BranchId)).GetProperty("collections").GetProperty("total").GetDecimal());
        owner.DefaultRequestHeaders.Add("X-Academy-Id", b.AcademyId.ToString());
        Assert.Equal(25m, (await Summary(owner, Period + "&academyId=" + b.AcademyId)).GetProperty("collections").GetProperty("total").GetDecimal());
        var financial = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?" + Period);
        Assert.Equal(25m, financial.GetProperty("netAmount").GetDecimal());
        foreach (var range in new[] { "from=2026-11-03&to=2026-11-02", "from=2020-01-01&to=2026-11-02", "from=invalid&to=2026-11-02" })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/v1/reports/owner-summary?" + range)).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/reports/owner-summary?" + Period)).StatusCode);
    }

    private sealed record Fixture(Guid AcademyId, Guid UserId, Guid MembershipId, Guid BranchId, Guid SportId, string Email);
    private async Task<Fixture> Academy(string key)
    {
        var f = new Fixture(Id(key), Id(key + ":user"), Id(key + ":member"), Id(key + ":branch"), Id(key + ":sport"), "summary." + key + "@example.test");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        if (await db.Academies.AnyAsync(a => a.Id == f.AcademyId)) return f;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var created = await users.CreateAsync(new() { Id = f.UserId, UserName = f.Email, Email = f.Email, DisplayName = "مالك اختبار الملخص", IsActive = true }, Password);
        Assert.True(created.Succeeded, string.Join(",", created.Errors.Select(e => e.Code)));
        db.Academies.Add(new() { Id = f.AcademyId, ArabicName = "أكاديمية اختبار الملخص", Slug = "summary-" + key, TimeZone = "Africa/Cairo", DefaultCurrency = "EGP" });
        db.AcademyMemberships.Add(new() { Id = f.MembershipId, AcademyId = f.AcademyId, UserId = f.UserId, Role = AcademyRole.AcademyOwner });
        db.Branches.Add(new() { Id = f.BranchId, AcademyId = f.AcademyId, ArabicName = "فرع الاختبار" });
        db.Sports.Add(new() { Id = f.SportId, AcademyId = f.AcademyId, ArabicName = "رياضة الاختبار" });
        await db.SaveChangesAsync(); return f;
    }
    private async Task Receipt(Fixture f, string key, decimal amount, string at)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var id = Id(f.Email + ":" + key); if (await db.ExternalAgreementReceipts.AnyAsync(x => x.Id == id)) return;
        var agreement = new ExternalAgreement { Id = Id(key + ":agreement:" + f.Email), AcademyId = f.AcademyId, BranchId = f.BranchId, SportId = f.SportId,
            CounterpartyName = "طرف اصطناعي", Title = "تحصيل اختبار الملخص", Kind = ExternalAgreementKind.ExternalProgram,
            FromDate = new(2026, 11, 1), ToDate = new(2026, 11, 3), DueOn = new(2026, 11, 3), Amount = amount, Currency = "EGP",
            ActorUserId = f.UserId, ActorMembershipId = f.MembershipId, RequestId = id, RequestHash = "synthetic-only" };
        db.ExternalAgreements.Add(agreement);
        db.ExternalAgreementReceipts.Add(new() { Id = id, AcademyId = f.AcademyId, Agreement = agreement, BranchId = f.BranchId, SportId = f.SportId,
            Amount = amount, Currency = "EGP", PaymentMethod = "Online", ReceiptNumber = "summary-" + id.ToString("N"), ReceivedAtUtc = DateTimeOffset.Parse(at),
            ActorUserId = f.UserId, ActorMembershipId = f.MembershipId, RequestId = id, RequestHash = "synthetic-only" });
        await db.SaveChangesAsync();
    }
    private async Task<HttpClient> Login(Fixture f)
    {
        var client = factory.CreateClient();
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = f.Email, password = Password }) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode); return client;
    }
    private static async Task<JsonElement> Summary(HttpClient client, string query)
    {
        var response = await client.GetAsync("/api/v1/reports/owner-summary?" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
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
