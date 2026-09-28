using System.Net;
using System.Net.Http.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class DashboardRemediationTests : IAsyncLifetime
{
    private readonly string connection = Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("ACADEMY_TEST_CONNECTION_STRING is required.");
    private Factory factory = null!;
    public Task InitializeAsync() { factory = new Factory(connection); _ = factory.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task Owner_can_update_same_tenant_branch()
    {
        var branch = await CreateBranch("فرع تعديل"); var updatedName = $"فرع تعديل ناجح {branch:N}"; using var client = factory.CreateClient(); await Login(client, DemoSeed.OwnerEmail);
        var response = await Put(client, $"/api/v1/manage/structure/branches/{branch}", new { arabicName = updatedName, englishName = "Updated Branch", description = "عنوان اختباري" }, await Csrf(client));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var saved = await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Branches.SingleAsync(x => x.Id == branch); Assert.Equal(updatedName, saved.ArabicName); Assert.Equal("عنوان اختباري", saved.Address);
    }

    [Fact]
    public async Task Owner_cannot_update_other_tenant_branch()
    {
        using var client = factory.CreateClient(); await Login(client, DemoSeed.OwnerEmail);
        await using var scope = factory.Services.CreateAsyncScope(); var branch = await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Branches.Where(x => x.AcademyId == DemoSeed.FutureAcademyId).Select(x => x.Id).FirstAsync();
        var response = await Put(client, $"/api/v1/manage/structure/branches/{branch}", new { arabicName = "تعديل مرفوض" }, await Csrf(client));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Coach_cannot_use_structure_record_actions()
    {
        using var client = factory.CreateClient(); await Login(client, DemoSeed.CoachEmail);
        var response = await Put(client, $"/api/v1/manage/structure/branches/{Slice2DemoSeed.CityBranchId}", new { arabicName = "ممنوع" }, await Csrf(client));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Staff_player_record_actions_reject_cross_tenant_id()
    {
        using var client = factory.CreateClient(); await Login(client, DemoSeed.OwnerEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/people/players/{Slice2DemoSeed.AcademyBPlayerId}")).StatusCode);
        var response = await Put(client, $"/api/v1/people/players/{Slice2DemoSeed.AcademyBPlayerId}/status", new { isActive = false }, await Csrf(client));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Guardian_cannot_use_staff_player_record_actions()
    {
        using var client = factory.CreateClient(); await GuardianLogin(client);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/people/players/{Slice2DemoSeed.OmarPlayerId}")).StatusCode);
    }

    [Fact]
    public async Task Record_details_return_only_current_tenant_relations()
    {
        using var client = factory.CreateClient(); await Login(client, DemoSeed.OwnerEmail);
        (await client.GetAsync($"/api/v1/people/players/{Slice2DemoSeed.OmarPlayerId}")).EnsureSuccessStatusCode();
        (await client.GetAsync($"/api/v1/people/guardians/{Slice2DemoSeed.MainGuardianId}")).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var coachId = await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().AcademyMemberships.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Role == Academy.Infrastructure.Tenancy.AcademyRole.Coach).Select(x => x.Id).SingleAsync();
        (await client.GetAsync($"/api/v1/manage/structure/coaches/{coachId}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Deactivating_referenced_branch_preserves_group_relation()
    {
        var branchId = await CreateBranch("فرع مرجعي"); var groupId = Guid.NewGuid(); var groupName = $"مجموعة مرجعية {groupId:N}";
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); db.TrainingGroups.Add(new TrainingGroup { Id = groupId, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = groupName, BranchId = branchId, SportId = Slice2DemoSeed.FootballId, AgeCategoryId = Slice2DemoSeed.U10Id, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); }
        using var client = factory.CreateClient(); await Login(client, DemoSeed.OwnerEmail); var response = await Put(client, $"/api/v1/manage/structure/branches/{branchId}/status", new { isActive = false }, await Csrf(client)); Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var verify = factory.Services.CreateAsyncScope(); var database = verify.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.False((await database.Branches.SingleAsync(x => x.Id == branchId)).IsActive); Assert.True(await database.TrainingGroups.AnyAsync(x => x.Id == groupId && x.BranchId == branchId)); Assert.DoesNotContain(groupName, await client.GetStringAsync("/api/v1/manage/structure/options"));
    }

    private async Task<Guid> CreateBranch(string prefix) { await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var id = Guid.NewGuid(); db.Branches.Add(new Branch { Id = id, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = $"{prefix} {id:N}", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); return id; }
    private static async Task Login(HttpClient client, string email) { var response = await Post(client, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" }, await Csrf(client)); response.EnsureSuccessStatusCode(); }
    private static async Task GuardianLogin(HttpClient client) { var request = await Post(client, "/api/v1/auth/guardian/otp/request", new { phoneNumber = DemoSeed.GuardianPhone }, await Csrf(client)); var challenge = await request.Content.ReadFromJsonAsync<Challenge>(); var response = await Post(client, "/api/v1/auth/guardian/otp/verify", new { challengeId = challenge!.ChallengeId, phoneNumber = DemoSeed.GuardianPhone, code = "246810" }, await Csrf(client)); response.EnsureSuccessStatusCode(); }
    private static async Task<string> Csrf(HttpClient client) => (await client.GetFromJsonAsync<CsrfToken>("/api/v1/auth/csrf"))!.Token;
    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object body, string token) => Send(client, HttpMethod.Post, path, body, token);
    private static Task<HttpResponseMessage> Put(HttpClient client, string path, object body, string token) => Send(client, HttpMethod.Put, path, body, token);
    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object body, string token) { var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", token); return client.SendAsync(request); }
    private sealed record CsrfToken(string Token);
    private sealed record Challenge(Guid ChallengeId);
    private sealed class Factory(string connection) : WebApplicationFactory<Program> { protected override void ConfigureWebHost(IWebHostBuilder builder) { builder.UseEnvironment("Demo"); builder.UseSetting("ConnectionStrings:Default", connection); builder.UseSetting("Demo:SeedEnabled", "true"); builder.UseSetting("Demo:FixedOtpEnabled", "true"); builder.UseSetting("Demo:FixedOtp", "246810"); builder.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); } }
}
