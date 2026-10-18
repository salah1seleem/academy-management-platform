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
    private readonly List<Guid> createdNotices = [];
    public Task InitializeAsync() { factory = new(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Isolated test database required")); _ = factory.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await db.AcademyNotices.Where(x => createdNotices.Contains(x.Id)).ExecuteDeleteAsync();
        // Keep immutable case evidence from isolated QA, instead of bypassing its guard during cleanup.
        await db.GuardianFeedbacks.Where(x => created.Contains(x.Id) && !db.FeedbackChanges.Any(c => c.FeedbackId == x.Id)).ExecuteDeleteAsync();
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
        // Reused isolated QA can contain other synthetic ratings. Retain an exact
        // baseline, then assert count and score after this new 5/5, without deleting history.
        int[] baselineScores, baselineSummaryScores;
        await using (var baselineScope = factory.Services.CreateAsyncScope())
        {
            var db = baselineScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var academy = DemoSeed.NogoomAcademyId;
            var zone = TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a=>a.Id==academy).Select(a=>a.TimeZone).SingleAsync());
            var start = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026,10,1), zone); var end = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026,11,1), zone);
            var assigned = await db.StaffGroupAssignments.Where(a=>a.AcademyId==academy&&a.AcademyMembershipId==coach&&a.IsActive&&a.TrainingGroup.IsActive).Select(a=>a.TrainingGroupId).ToArrayAsync();
            baselineScores = await db.GuardianFeedbacks.Where(f=>f.AcademyId==academy&&f.CoachMembershipId==coach&&f.Kind=="Rating"&&assigned.Contains(f.TrainingGroupId)&&f.CreatedAtUtc>=start&&f.CreatedAtUtc<end).Select(f=>f.Score!.Value).ToArrayAsync();
            var allGroups = await db.TrainingGroups.Where(g=>g.AcademyId==academy&&g.IsActive).Select(g=>g.Id).ToArrayAsync();
            baselineSummaryScores = await db.GuardianFeedbacks.Where(f=>f.AcademyId==academy&&f.CoachMembershipId==coach&&f.Kind=="Rating"&&allGroups.Contains(f.TrainingGroupId)&&f.CreatedAtUtc>=start&&f.CreatedAtUtc<end).Select(f=>f.Score!.Value).ToArrayAsync();
        }
        var expectedScore = Math.Round((decimal)(baselineScores.Sum()+5)/(baselineScores.Length+1),1);
        var expectedSummaryScore = Math.Round((decimal)(baselineSummaryScores.Sum()+5)/(baselineSummaryScores.Length+1),1);
        var rating = new { trainingGroupId = group, coachMembershipId = coach, kind = "Rating", score = 5, message = "تدريب منظم" };
        Assert.Equal(HttpStatusCode.NotFound, (await Send(guardian, $"/api/v1/guardian/children/{Slice2DemoSeed.OtherPlayerId}/feedback", rating, "unrelated-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(guardian, path, rating with { coachMembershipId = Guid.NewGuid() }, "foreign-coach-01")).StatusCode);
        var key = Guid.NewGuid().ToString(); var made = await Send(guardian, path, rating, key); Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        var id = (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); created.Add(id);
        Assert.Equal(HttpStatusCode.OK, (await Send(guardian, path, rating, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(guardian, path, rating, Guid.NewGuid().ToString())).StatusCode);
        var complaint = await Send(guardian, path, new { trainingGroupId = group, coachMembershipId = coach, kind = "Complaint", score = (int?)null, message = "نحتاج متابعة الموعد" }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Created, complaint.StatusCode); var complaintId = (await complaint.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); created.Add(complaintId);
        var duePath = $"/api/v1/manage/feedback/{complaintId}/due";
        var due = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(guardian, duePath, new { dueOn = due })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(foreign, duePath, new { dueOn = due })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, duePath, new { dueOn = new DateOnly(2020, 1, 1) })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, $"/api/v1/manage/feedback/{id}/due", new { dueOn = due })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, duePath, new { dueOn = due })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, duePath, new { dueOn = due })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, duePath, new { dueOn = due.AddDays(1) })).StatusCode);
        Assert.DoesNotContain((await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/feedback?overdue=true")).EnumerateArray(), x => x.GetProperty("id").GetGuid() == complaintId);
        // Simulate elapsed time on this isolated synthetic fixture, never on the review database.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            await db.GuardianFeedbacks.Where(x => x.Id == complaintId).ExecuteUpdateAsync(s => s.SetProperty(x => x.DueOn, new DateOnly(2020, 1, 1)));
        }
        Assert.Contains((await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/feedback?overdue=true")).EnumerateArray(), x => x.GetProperty("id").GetGuid() == complaintId && x.GetProperty("overdue").GetBoolean());
        var overdueSummary = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/owner-summary?from=2026-10-01&to=2026-10-31");
        Assert.True(overdueSummary.GetProperty("operations").GetProperty("overdueFeedback").GetInt32() >= 1);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/v1/manage/feedback")).StatusCode);
        var list = await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/feedback?kind=Complaint&status=Open"); Assert.Contains(list.EnumerateArray(), x => x.GetProperty("id").GetGuid() == complaintId);
        var foreignList = await foreign.GetFromJsonAsync<JsonElement>("/api/v1/manage/feedback"); Assert.DoesNotContain(foreignList.EnumerateArray(), x => x.GetProperty("id").GetGuid() == complaintId);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(foreign, $"/api/v1/manage/feedback/{complaintId}/review", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, $"/api/v1/manage/feedback/{complaintId}/review", new { })).StatusCode);
        var unresolved = await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/feedback?kind=Complaint&status=Unresolved");
        Assert.Contains(unresolved.EnumerateArray(), x => x.GetProperty("id").GetGuid() == complaintId && x.GetProperty("status").GetString() == "Reviewed");
        var resolutionPath = $"/api/v1/manage/feedback/{complaintId}/resolve";
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(guardian, resolutionPath, new { response = "راجعنا الشكوى" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(foreign, resolutionPath, new { response = "راجعنا الشكوى" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, resolutionPath, new { response = "قصير" })).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var beforeNotices = await db.AcademyNotices.Select(x => x.Id).ToArrayAsync();
            Assert.Equal(HttpStatusCode.OK, (await Send(owner, resolutionPath, new { response = "تم تعديل موعد الإشعار للأسرة." })).StatusCode);
            var newNotices = await db.AcademyNotices.Where(x => !beforeNotices.Contains(x.Id)).ToArrayAsync();
            Assert.Single(newNotices); createdNotices.Add(newNotices[0].Id);
            Assert.DoesNotContain("نحتاج متابعة الموعد", newNotices[0].Message);
            Assert.Equal(HttpStatusCode.OK, (await Send(owner, resolutionPath, new { response = "تم تعديل موعد الإشعار للأسرة." })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, resolutionPath, new { response = "رد آخر مختلف" })).StatusCode);
            Assert.Single(await db.AcademyNotices.Where(x => createdNotices.Contains(x.Id)).ToArrayAsync());
        }
        var mine = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{child}/feedback-options");
        Assert.Contains(mine.GetProperty("mine").EnumerateArray(), x => x.GetProperty("id").GetGuid() == complaintId &&
            x.GetProperty("status").GetString() == "Resolved" && x.GetProperty("ownerResponse").GetString() == "تم تعديل موعد الإشعار للأسرة.");
        unresolved = await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/feedback?kind=Complaint&status=Unresolved");
        Assert.DoesNotContain(unresolved.EnumerateArray(), x => x.GetProperty("id").GetGuid() == complaintId);
        Assert.DoesNotContain((await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/feedback?overdue=true")).EnumerateArray(), x => x.GetProperty("id").GetGuid() == complaintId);
        var summary = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/owner-summary?from=2026-10-01&to=2026-10-31");
        var operations = summary.GetProperty("operations");
        Assert.Contains(operations.GetProperty("coachPerformance").EnumerateArray(), x => x.GetProperty("id").GetGuid() == coach && x.GetProperty("parentRating").GetDecimal() == expectedSummaryScore && x.GetProperty("parentRatingCount").GetInt32() == baselineSummaryScores.Length+1);
        var coachDetailPath = $"/api/v1/reports/coach-performance/{coach}?from=2026-10-01&to=2026-10-31";
        var coachDetail = await owner.GetFromJsonAsync<JsonElement>(coachDetailPath);
        Assert.Equal(expectedScore, coachDetail.GetProperty("parentRating").GetDecimal());
        Assert.Equal(baselineScores.Length+1, coachDetail.GetProperty("parentRatingCount").GetInt32());
        Assert.Contains(coachDetail.GetProperty("groups").EnumerateArray(), x => x.GetProperty("id").GetGuid() == group);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/reports/coach-performance/{coach}?allDates=true")).StatusCode);
        Assert.Contains((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/feedback?coachId={coach}&kind=Rating")).EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        Assert.DoesNotContain((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/feedback?coachId={Guid.NewGuid()}" )).EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync(coachDetailPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(coachDetailPath)).StatusCode);
    }
    [Fact]
    public async Task Football_private_cases_seed_is_guarded_idempotent_and_preserves_manual_work()
    {
        using var football = new Factory(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")!, "Football"); _ = football.CreateClient();
        await using var scope = football.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var options = new DemoOptions { SeedEnabled = true, SeedProfile = "Football" };
        var env = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>();
        await FeedbackFollowUpDemoSeed.SeedCases(db, env, options);
        var ids = Enumerable.Range(0,2).Select(i=>FootballDemoSeed.Id($"feedback-case-demo/{i}")).ToArray();
        Assert.Equal(2,await db.GuardianFeedbacks.CountAsync(f=>ids.Contains(f.Id)));
        var baselineIds = new[] { FootballDemoSeed.Id("feedback-case-demo/0/start"), FootballDemoSeed.Id("feedback-case-demo/1/start"), FootballDemoSeed.Id("feedback-case-demo/1/wait") };
        Assert.Equal(3,await db.FeedbackChanges.CountAsync(c=>baselineIds.Contains(c.Id)));
        using var owner = football.CreateClient();
        (await Send(owner,"/api/v1/auth/login",new { email="owner.football@example.test", password="Demo-Only-123!" })).EnsureSuccessStatusCode();
        var item = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/feedback/{ids[0]}");
        var status = item.GetProperty("status").GetString()=="AwaitingInfo"?"InProgress":"AwaitingInfo";
        (await Send(owner,$"/api/v1/manage/feedback/{ids[0]}/transition",new Academy.Api.Operations.FeedbackCaseWrite(status,item.GetProperty("version").GetUInt32(),Guid.NewGuid(),"تحديث يدوي اصطناعي يجب ألا يستبدله seed"))).EnsureSuccessStatusCode();
        var states = await db.GuardianFeedbacks.AsNoTracking().Where(f=>ids.Contains(f.Id)).OrderBy(f=>f.Id).Select(f=>new { f.Id,f.Status }).ToArrayAsync();
        var changes = await db.FeedbackChanges.CountAsync(c=>ids.Contains(c.FeedbackId));
        await FeedbackFollowUpDemoSeed.SeedCases(db, env, options);
        Assert.Equal(states, await db.GuardianFeedbacks.AsNoTracking().Where(f=>ids.Contains(f.Id)).OrderBy(f=>f.Id).Select(f=>new { f.Id,f.Status }).ToArrayAsync());
        Assert.Equal(changes, await db.FeedbackChanges.CountAsync(c=>ids.Contains(c.FeedbackId)));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>FeedbackFollowUpDemoSeed.SeedCases(db,new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName="Production" },options));
    }

    [Fact]
    public async Task Football_demo_feedback_is_synthetic_and_idempotent()
    {
        var connection = Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")!;
        using var football = new Factory(connection, "Football"); _ = football.CreateClient();
        await using var scope = football.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(6, await db.GuardianFeedbacks.CountAsync(x => x.AcademyId == FootballDemoSeed.AcademyId && x.IdempotencyKey.StartsWith("demo-")));
        await FootballDemoSeed.RunAsync(scope.ServiceProvider, false);
        Assert.Equal(6, await db.GuardianFeedbacks.CountAsync(x => x.AcademyId == FootballDemoSeed.AcademyId && x.IdempotencyKey.StartsWith("demo-")));
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
