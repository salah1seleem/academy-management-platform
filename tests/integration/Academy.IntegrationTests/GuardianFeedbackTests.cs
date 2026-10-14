using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Demo;
using Academy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class GuardianFeedbackTests : IAsyncLifetime
{
    private Factory factory = null!;
    private readonly List<Guid> created = [];
    public Task InitializeAsync() { factory = new(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Isolated test database required")); _ = factory.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().GuardianFeedbacks.Where(x => created.Contains(x.Id)).ExecuteDeleteAsync();
        await factory.DisposeAsync();
    }
    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, object body, string? key = null)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }
    private async Task<HttpClient> Owner(string email = DemoSeed.OwnerEmail)
    {
        var client = factory.CreateClient(); (await Send(client, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" })).EnsureSuccessStatusCode(); return client;
    }
    private async Task<HttpClient> Guardian()
    {
        var client = factory.CreateClient(); var challenge = await Send(client, "/api/v1/auth/guardian/otp/request", new { phoneNumber = DemoSeed.GuardianPhone }); challenge.EnsureSuccessStatusCode();
        var id = (await challenge.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetGuid();
        (await Send(client, "/api/v1/auth/guardian/otp/verify", new { challengeId = id, phoneNumber = DemoSeed.GuardianPhone, code = "246810" })).EnsureSuccessStatusCode(); return client;
    }

    [Fact]
    public async Task Linked_guardian_rates_assigned_coach_once_and_owner_sees_private_feedback()
    {
        using var guardian = await Guardian(); using var owner = await Owner(); using var foreign = await Owner(DemoSeed.FutureOwnerEmail);
        var child = Slice2DemoSeed.OmarPlayerId;
        var path = $"/api/v1/guardian/children/{child}/feedback";
        Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync($"/api/v1/guardian/children/{Slice2DemoSeed.OtherPlayerId}/feedback-options")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync($"/api/v1/guardian/children/{Slice2DemoSeed.AcademyBPlayerId}/feedback-options")).StatusCode);
        var optionsResponse = await guardian.GetAsync($"{path}-options"); Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
        var options = (await optionsResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("groups").EnumerateArray().First(x => x.GetProperty("coaches").GetArrayLength() > 0);
        var group = options.GetProperty("id").GetGuid(); var coach = options.GetProperty("coaches")[0].GetProperty("id").GetGuid();
        var rating = new { trainingGroupId = group, coachMembershipId = coach, kind = "Rating", score = 5, message = "تدريب منظم" };
        Assert.Equal(HttpStatusCode.NotFound, (await Send(guardian, $"/api/v1/guardian/children/{Slice2DemoSeed.OtherPlayerId}/feedback", rating, "unrelated-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(guardian, path, rating with { coachMembershipId = Guid.NewGuid() }, "foreign-coach-01")).StatusCode);
        var key = Guid.NewGuid().ToString(); var made = await Send(guardian, path, rating, key); Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        var id = (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); created.Add(id);
        Assert.Equal(HttpStatusCode.OK, (await Send(guardian, path, rating, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(guardian, path, rating, Guid.NewGuid().ToString())).StatusCode);
        var complaint = await Send(guardian, path, new { trainingGroupId = group, coachMembershipId = coach, kind = "Complaint", score = (int?)null, message = "نحتاج متابعة الموعد" }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Created, complaint.StatusCode); var complaintId = (await complaint.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); created.Add(complaintId);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/v1/manage/feedback")).StatusCode);
        var list = await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/feedback?kind=Complaint&status=Open"); Assert.Contains(list.EnumerateArray(), x => x.GetProperty("id").GetGuid() == complaintId);
        var foreignList = await foreign.GetFromJsonAsync<JsonElement>("/api/v1/manage/feedback"); Assert.DoesNotContain(foreignList.EnumerateArray(), x => x.GetProperty("id").GetGuid() == complaintId);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(foreign, $"/api/v1/manage/feedback/{complaintId}/review", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, $"/api/v1/manage/feedback/{complaintId}/review", new { })).StatusCode);
        var summary = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/owner-summary?from=2026-10-01&to=2026-10-31");
        var operations = summary.GetProperty("operations");
        Assert.Contains(operations.GetProperty("coachPerformance").EnumerateArray(), x => x.GetProperty("id").GetGuid() == coach && x.GetProperty("parentRating").GetDecimal() == 5);
    }
    [Fact]
    public async Task Football_demo_feedback_is_synthetic_and_idempotent()
    {
        var connection = Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")!;
        using var football = new Factory(connection, "Football"); _ = football.CreateClient();
        await using var scope = football.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(5, await db.GuardianFeedbacks.CountAsync(x => x.AcademyId == FootballDemoSeed.AcademyId && x.IdempotencyKey.StartsWith("demo-")));
        await FootballDemoSeed.RunAsync(scope.ServiceProvider, false);
        Assert.Equal(5, await db.GuardianFeedbacks.CountAsync(x => x.AcademyId == FootballDemoSeed.AcademyId && x.IdempotencyKey.StartsWith("demo-")));
    }
    private sealed class Factory(string connection, string profile = "LegacyRegression") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Demo"); builder.UseSetting("ConnectionStrings:Default", connection);
            builder.UseSetting("Demo:SeedProfile", profile); builder.UseSetting("Demo:SeedEnabled", "true");
            builder.UseSetting("Demo:FixedOtpEnabled", "true"); builder.UseSetting("Demo:FixedOtp", "246810");
            builder.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); builder.UseSetting("Demo:ReferenceDate", "2026-09-28");
        }
    }
}
