using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice5;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class EvaluationIntegrationTests : IAsyncLifetime
{
    private readonly string connection = Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("ACADEMY_TEST_CONNECTION_STRING is required.");
    private Factory factory = null!;
    public Task InitializeAsync() { factory = new Factory(connection); _ = factory.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Fact] public async Task Admin_creates_criterion_for_own_academy()
    { using var client = await Staff(DemoSeed.AdminEmail); var response = await Post(client, "/api/v1/evaluations/criteria", new { sportId = Slice2DemoSeed.FootballId, arabicName = $"معيار اختبار {Guid.NewGuid():N}", displayOrder = 90, isActive = true, weight = 1.25, footballAxis = "Passing" }); Assert.Equal(HttpStatusCode.Created, response.StatusCode); }

    [Fact] public async Task Cross_tenant_sport_criterion_creation_is_rejected()
    { await using var scope = factory.Services.CreateAsyncScope(); var sport = await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Sports.Where(x => x.AcademyId == DemoSeed.FutureAcademyId).Select(x => x.Id).FirstAsync(); using var client = await Staff(DemoSeed.AdminEmail); Assert.Equal(HttpStatusCode.NotFound, (await Post(client, "/api/v1/evaluations/criteria", new { sportId = sport, arabicName = "معيار مرفوض", displayOrder = 1, isActive = true, weight = 1, footballAxis = "Passing" })).StatusCode); }

    [Fact] public async Task Coach_sees_assigned_group_players_only()
    { using var client = await Staff(DemoSeed.CoachEmail); var json = await client.GetFromJsonAsync<JsonElement>("/api/v1/evaluations/options"); var rows = json.GetProperty("enrollments").EnumerateArray().ToArray(); Assert.NotEmpty(rows); Assert.All(rows, x => Assert.Equal(Slice2DemoSeed.FootballGroupId, x.GetProperty("groupId").GetGuid())); }

    [Fact] public async Task Coach_cannot_evaluate_unrelated_group()
    { await using var scope = factory.Services.CreateAsyncScope(); var enrollment = await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().SportEnrollments.Where(x => x.PlayerId == Slice2DemoSeed.OmarPlayerId && x.SportId == Slice2DemoSeed.SwimmingId).Select(x => x.Id).SingleAsync(); using var client = await Staff(DemoSeed.CoachEmail); Assert.Equal(HttpStatusCode.NotFound, (await Post(client, "/api/v1/evaluations", new { sportEnrollmentId = enrollment, evaluationDate = "2026-09-28", reportingPeriod = "اختبار" })).StatusCode); }

    [Fact] public async Task Draft_evaluation_keeps_nullable_scores()
    { await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.True(await db.EvaluationScores.AnyAsync(x => x.PlayerEvaluationId == Slice5DemoSeed.DraftFootballEvaluationId && x.Score == null)); }

    [Theory] [InlineData(-1)] [InlineData(101)] public async Task Score_outside_zero_to_one_hundred_is_rejected(int score)
    { using var client = await Staff(DemoSeed.AdminEmail); var draft = await CreateDraft(client); var details = await GetDetails(client, draft); var response = await Put(client, $"/api/v1/evaluations/{draft}", new { reportingPeriod = "اختبار", version = details.Version, scores = new[] { new { criterionId = details.CriterionId, score } } }); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }

    [Fact] public async Task Duplicate_criterion_score_in_request_is_rejected()
    { using var client = await Staff(DemoSeed.AdminEmail); var draft = await CreateDraft(client); var details = await GetDetails(client, draft); var response = await Put(client, $"/api/v1/evaluations/{draft}", new { reportingPeriod = "اختبار", version = details.Version, scores = new[] { new { criterionId = details.CriterionId, score = 50 }, new { criterionId = details.CriterionId, score = 60 } } }); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }

    [Fact] public async Task Draft_is_not_visible_to_guardian()
    { using var client = await Guardian(); var rows = await client.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{Slice2DemoSeed.OmarPlayerId}/evaluations"); Assert.DoesNotContain(rows.EnumerateArray(), x => x.GetProperty("id").GetGuid() == Slice5DemoSeed.DraftFootballEvaluationId); }

    [Fact] public async Task Published_evaluation_is_visible_to_linked_guardian()
    { using var client = await Guardian(); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/guardian/evaluations/{Slice5DemoSeed.CurrentFootballEvaluationId}/report")).StatusCode); }

    [Fact] public async Task Guardian_cannot_see_another_child_evaluations()
    { using var client = await Guardian(); Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/guardian/children/{Slice2DemoSeed.OtherPlayerId}/evaluations")).StatusCode); }

    [Fact] public async Task Guardian_cannot_see_academy_b_evaluation()
    { using var client = await Guardian(); Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/guardian/evaluations/{Slice5DemoSeed.AcademyBEvaluationId}/report")).StatusCode); }

    [Fact] public async Task Guardian_cannot_mutate_evaluation()
    { using var client = await Guardian(); Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, $"/api/v1/evaluations/{Slice5DemoSeed.DraftFootballEvaluationId}/publish", new { version = 1 })).StatusCode); }

    [Fact] public async Task Publish_freezes_criterion_metadata()
    { await using var scope = factory.Services.CreateAsyncScope(); var score = await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().EvaluationScores.Include(x => x.EvaluationCriterion).FirstAsync(x => x.PlayerEvaluationId == Slice5DemoSeed.CurrentFootballEvaluationId); Assert.Equal(score.EvaluationCriterion.ArabicName, score.CriterionNameSnapshot); Assert.Equal(score.EvaluationCriterion.Weight, score.WeightSnapshot); Assert.Equal(score.EvaluationCriterion.FootballAxis, score.FootballAxisSnapshot); }

    [Fact] public async Task Criterion_rename_does_not_rewrite_published_history()
    { await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var score = await db.EvaluationScores.Include(x => x.EvaluationCriterion).FirstAsync(x => x.PlayerEvaluationId == Slice5DemoSeed.CurrentFootballEvaluationId); var snapshot = score.CriterionNameSnapshot; var original = score.EvaluationCriterion.ArabicName; try { score.EvaluationCriterion.ArabicName = $"اسم معدل {Guid.NewGuid():N}"; await db.SaveChangesAsync(); db.ChangeTracker.Clear(); Assert.Equal(snapshot, await db.EvaluationScores.Where(x => x.Id == score.Id).Select(x => x.CriterionNameSnapshot).SingleAsync()); } finally { var criterion = await db.EvaluationCriteria.SingleAsync(x => x.Id == score.EvaluationCriterionId); criterion.ArabicName = original; await db.SaveChangesAsync(); } }

    [Fact] public async Task Criterion_weight_change_does_not_rewrite_published_result()
    { await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var score = await db.EvaluationScores.Include(x => x.EvaluationCriterion).FirstAsync(x => x.PlayerEvaluationId == Slice5DemoSeed.CurrentFootballEvaluationId); var snapshot = score.WeightSnapshot; var original = score.EvaluationCriterion.Weight; try { score.EvaluationCriterion.Weight = 9; await db.SaveChangesAsync(); db.ChangeTracker.Clear(); Assert.Equal(snapshot, await db.EvaluationScores.Where(x => x.Id == score.Id).Select(x => x.WeightSnapshot).SingleAsync()); } finally { var criterion = await db.EvaluationCriteria.SingleAsync(x => x.Id == score.EvaluationCriterionId); criterion.Weight = original; await db.SaveChangesAsync(); } }

    [Fact] public void Missing_criteria_are_excluded_not_zero()
    { var calc = new EvaluationReportCalculator().Calculate([Score(80, FootballAxis.Passing, 1), Score(null, FootballAxis.Passing, 9)]); Assert.Equal(80m, calc.Axes.Single(x => x.Axis == FootballAxis.Passing).Value); }

    [Fact] public void Football_axis_weighted_calculation_is_correct()
    { var calc = new EvaluationReportCalculator().Calculate([Score(80, FootballAxis.Passing, 1), Score(50, FootballAxis.Passing, 2)]); Assert.Equal(60m, calc.Axes.Single(x => x.Axis == FootballAxis.Passing).Value); }

    [Fact] public void Overall_excludes_unavailable_axes()
    { var calc = new EvaluationReportCalculator().Calculate([Score(80, FootballAxis.Passing, 1), Score(60, FootballAxis.Speed, 1)]); Assert.Equal(70m, calc.OverallScore); Assert.Equal(2, calc.AvailableAxes); }

    [Fact] public void Completeness_is_informational_and_correct()
    { var calc = new EvaluationReportCalculator().Calculate([Score(100, FootballAxis.Passing, 1), Score(null, FootballAxis.Speed, 1)]); Assert.Equal(1, calc.ScoredCriteria); Assert.Equal(2, calc.TotalApplicableCriteria); Assert.Equal(50m, calc.CompletenessPercentage); }

    [Fact] public async Task Swimming_report_has_no_football_radar_data()
    { using var client = await Staff(DemoSeed.AdminEmail); var report = await client.GetFromJsonAsync<JsonElement>($"/api/v1/evaluations/{Slice5DemoSeed.SwimmingEvaluationId}/report"); Assert.False(report.GetProperty("isFootballReport").GetBoolean()); Assert.Equal(0, report.GetProperty("availableAxes").GetInt32()); }

    [Fact] public async Task Published_evaluation_cannot_be_edited_directly()
    { using var client = await Staff(DemoSeed.AdminEmail); var response = await Put(client, $"/api/v1/evaluations/{Slice5DemoSeed.CurrentFootballEvaluationId}", new { reportingPeriod = "ممنوع", version = 1, scores = Array.Empty<object>() }); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); }

    [Fact] public async Task Stale_draft_update_is_rejected()
    { using var client = await Staff(DemoSeed.AdminEmail); var draft = await CreateDraft(client); var details = await GetDetails(client, draft); (await Put(client, $"/api/v1/evaluations/{draft}", new { reportingPeriod = "أول حفظ", version = details.Version, scores = Array.Empty<object>() })).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Conflict, (await Put(client, $"/api/v1/evaluations/{draft}", new { reportingPeriod = "حفظ قديم", version = details.Version, scores = Array.Empty<object>() })).StatusCode); }

    [Fact] public async Task Tenant_isolation_by_evaluation_id_tampering()
    { using var client = await Staff(DemoSeed.AdminEmail); Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/evaluations/{Slice5DemoSeed.AcademyBEvaluationId}/report")).StatusCode); }

    [Fact] public async Task Payment_and_attendance_regression_endpoints_remain_green()
    { using var client = await Staff(DemoSeed.AdminEmail); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/subscriptions/collections")).StatusCode); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/attendance/sessions?from=2026-09-28&to=2026-09-28")).StatusCode); }

    private async Task<Guid> CreateDraft(HttpClient client)
    { await using var scope = factory.Services.CreateAsyncScope(); var enrollment = await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().SportEnrollments.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.PlayerId == Slice2DemoSeed.OmarPlayerId && x.SportId == Slice2DemoSeed.FootballId).Select(x => x.Id).SingleAsync(); var response = await Post(client, "/api/v1/evaluations", new { sportEnrollmentId = enrollment, evaluationDate = "2026-09-28", reportingPeriod = "اختبار آلي" }); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<Created>())!.Id; }
    private static async Task<Details> GetDetails(HttpClient client, Guid id) { var json = await client.GetFromJsonAsync<JsonElement>($"/api/v1/evaluations/{id}"); return new(json.GetProperty("version").GetUInt32(), json.GetProperty("scores")[0].GetProperty("criterionId").GetGuid()); }
    private async Task<HttpClient> Staff(string email) { var client = factory.CreateClient(); (await Post(client, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" })).EnsureSuccessStatusCode(); return client; }
    private async Task<HttpClient> Guardian() { var client = factory.CreateClient(); var request = await Post(client, "/api/v1/auth/guardian/otp/request", new { phoneNumber = DemoSeed.GuardianPhone }); var challenge = await request.Content.ReadFromJsonAsync<Challenge>(); (await Post(client, "/api/v1/auth/guardian/otp/verify", new { challengeId = challenge!.ChallengeId, phoneNumber = DemoSeed.GuardianPhone, code = "246810" })).EnsureSuccessStatusCode(); return client; }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body) { var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", await Csrf(client)); return await client.SendAsync(request); }
    private static async Task<HttpResponseMessage> Put(HttpClient client, string path, object body) { var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", await Csrf(client)); return await client.SendAsync(request); }
    private static async Task<string> Csrf(HttpClient client) => (await client.GetFromJsonAsync<CsrfDto>("/api/v1/auth/csrf"))!.Token;
    private static EvaluationScore Score(int? value, FootballAxis axis, decimal weight) => new() { CriterionNameSnapshot = "اختبار", Score = value, FootballAxisSnapshot = axis, WeightSnapshot = weight };
    private sealed record CsrfDto(string Token); private sealed record Challenge(Guid ChallengeId); private sealed record Created(Guid Id); private sealed record Details(uint Version, Guid CriterionId);
    private sealed class Factory(string connection) : WebApplicationFactory<Program> { protected override void ConfigureWebHost(IWebHostBuilder builder) { builder.UseEnvironment("Demo"); builder.UseSetting("ConnectionStrings:Default", connection); builder.UseSetting("Demo:SeedProfile", "LegacyRegression"); builder.UseSetting("Demo:SeedEnabled", "true"); builder.UseSetting("Demo:FixedOtpEnabled", "true"); builder.UseSetting("Demo:FixedOtp", "246810"); builder.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); builder.UseSetting("Demo:ReferenceDate", "2026-09-28"); builder.UseSetting("Payments:InternalTest:Enabled", "true"); builder.UseSetting("Payments:InternalTest:SigningKey", "Demo-Test-Signing-Key-Only-123456"); } }
}
