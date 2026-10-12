using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class V2AttendancePolicyTests : IAsyncLifetime
{
    private Factory factory = null!;
    public Task InitializeAsync() { factory = new(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Separate test database required")); _ = factory.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Default_and_legacy_unconfirmed_policy_deny_coach_writes_but_keep_training(bool legacyEnabled)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var row = await db.AcademyExperiences.SingleOrDefaultAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId);
        if (row == null) { row = new AcademyExperience { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, DisplayName = "أكاديمية اختبار" }; db.AcademyExperiences.Add(row); }
        var oldEnabled = row.CoachAttendanceEnabled; var oldConfirmed = row.CoachAttendancePolicyConfirmed;
        row.CoachAttendanceEnabled = legacyEnabled; row.CoachAttendancePolicyConfirmed = false; await db.SaveChangesAsync();
        try
        {
            using var coach = await Coach();
            var experience = await coach.GetFromJsonAsync<JsonElement>("/api/v1/academy/experience");
            Assert.False(experience.GetProperty("coachAttendanceEnabled").GetBoolean());
            var permissions = await coach.GetFromJsonAsync<string[]>("/api/v1/my-permissions");
            Assert.DoesNotContain(AcademyPermissions.AttendanceManage, permissions!);
            Assert.Contains(AcademyPermissions.EvaluationManage, permissions!);
            Assert.Equal(HttpStatusCode.OK, (await coach.GetAsync("/api/v1/coach/groups")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await coach.GetAsync("/api/v1/attendance/sessions")).StatusCode);
            var roster = await coach.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions/{Slice4DemoSeed.TodayFootballSessionId}/players");
            Assert.False(roster.GetProperty("canManageAttendance").GetBoolean());
            Assert.Equal(HttpStatusCode.Forbidden, (await Write(coach, $"/api/v1/attendance/sessions/{Slice4DemoSeed.TodayFootballSessionId}/players", new { items = Array.Empty<object>() })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Write(coach, "/api/v1/manage/academy/experience", new { coachAttendanceEnabled = true })).StatusCode);
            using var admin = await Staff(DemoSeed.AdminEmail);
            Assert.Equal(HttpStatusCode.NotFound, (await Write(admin, $"/api/v1/attendance/sessions/{Guid.NewGuid()}/players", new { items = Array.Empty<object>() })).StatusCode);
        }
        finally { row.CoachAttendanceEnabled = oldEnabled; row.CoachAttendancePolicyConfirmed = oldConfirmed; await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task Explicit_owner_opt_in_is_audited_scoped_idempotent_and_revocable()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail);
        using var coach = await Coach();
        var session = await TodayFootballSession();
        await Write(owner, "/api/v1/manage/academy/experience", new { coachAttendanceEnabled = false });
        var before = await History(owner);
        try
        {
            Assert.Equal(HttpStatusCode.NoContent, (await Write(owner, "/api/v1/manage/academy/experience", new { coachAttendanceEnabled = true })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Write(owner, "/api/v1/manage/academy/experience", new { coachAttendanceEnabled = true })).StatusCode);
            Assert.Equal(before + 1, await History(owner));
            var roster = await coach.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions/{session}/players");
            Assert.True(roster.GetProperty("canManageAttendance").GetBoolean());
            Assert.Equal(HttpStatusCode.NotFound, (await Write(coach, $"/api/v1/attendance/sessions/{Slice4DemoSeed.TodaySwimmingSessionId}/players", new { items = Array.Empty<object>() })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await coach.GetAsync("/api/v1/manage/academy/policy-audit")).StatusCode);
            using var other = await Staff(DemoSeed.FutureOwnerEmail);
            var foreignHistoryBefore = await History(other);
            Assert.Equal(HttpStatusCode.NoContent, (await Write(owner, "/api/v1/manage/academy/experience", new { coachAttendanceEnabled = false })).StatusCode);
            Assert.Equal(before + 2, await History(owner)); Assert.Equal(foreignHistoryBefore, await History(other));
            Assert.Equal(HttpStatusCode.Forbidden, (await Write(coach, $"/api/v1/attendance/sessions/{session}/players", new { items = Array.Empty<object>() })).StatusCode);
        }
        finally { await Write(owner, "/api/v1/manage/academy/experience", new { coachAttendanceEnabled = false }); }
    }
    private async Task<Guid> TodayFootballSession()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var source = await db.TrainingSessions.AsNoTracking().SingleAsync(s => s.Id == Slice4DemoSeed.TodayFootballSessionId);
        var today = await AttendanceCalendar.TodayAsync(source.AcademyId, db, TimeProvider.System);
        var existing = await db.TrainingSessions.Where(s => s.AcademyId == source.AcademyId && s.TrainingGroupId == source.TrainingGroupId && s.SessionDate == today && s.StartTime == source.StartTime).Select(s => (Guid?)s.Id).SingleOrDefaultAsync();
        if (existing.HasValue) return existing.Value;
        source.Id = Guid.NewGuid(); source.SessionDate = today;
        db.TrainingSessions.Add(source); await db.SaveChangesAsync(); return source.Id;
    }
    private static async Task<int> History(HttpClient c) => (await c.GetFromJsonAsync<JsonElement>("/api/v1/manage/academy/policy-audit")).GetArrayLength();
    private async Task<HttpClient> Coach() { var c = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(c, factory.Services); return c; }
    private async Task<HttpClient> Staff(string email) { var c = factory.CreateClient(); (await Write(c, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" }, HttpMethod.Post)).EnsureSuccessStatusCode(); return c; }
    private static async Task<HttpResponseMessage> Write(HttpClient c, string path, object body, HttpMethod? method = null)
    {
        var csrf = await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        var request = new HttpRequestMessage(method ?? HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); return await c.SendAsync(request);
    }
    private sealed class Factory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment("Demo"); b.UseSetting("ConnectionStrings:Default", connection); b.UseSetting("Demo:SeedProfile", "LegacyRegression"); b.UseSetting("Demo:SeedEnabled", "true"); b.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); b.UseSetting("Demo:FixedOtpEnabled", "true"); b.UseSetting("Demo:FixedOtp", "246810"); b.UseSetting("Demo:ReferenceDate", "2026-09-28");
        }
    }
}
