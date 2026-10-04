using System.Net;
using System.Net.Http.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class AttendanceIntegrationTests : IAsyncLifetime
{
    private readonly string connection = Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("ACADEMY_TEST_CONNECTION_STRING is required.");
    private Factory factory = null!;

    public Task InitializeAsync() { factory = new Factory(connection); _ = factory.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Fact] public async Task Session_generation_creates_expected_occurrence()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var from = await GenerationDate(db); var generator = scope.ServiceProvider.GetRequiredService<TrainingSessionGenerator>(); var owner = await OwnerId(scope);
        Assert.Equal(1, await generator.GenerateAsync(DemoSeed.NogoomAcademyId, owner, from, from));
        Assert.True(await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().TrainingSessions.AnyAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.SessionDate == from && x.Source == TrainingSessionSource.RecurringSchedule));
    }

    [Fact] public async Task Session_generation_rerun_is_idempotent()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var from = await GenerationDate(db); var generator = scope.ServiceProvider.GetRequiredService<TrainingSessionGenerator>(); var owner = await OwnerId(scope);
        Assert.Equal(1, await generator.GenerateAsync(DemoSeed.NogoomAcademyId, owner, from, from)); Assert.Equal(0, await generator.GenerateAsync(DemoSeed.NogoomAcademyId, owner, from, from));
    }

    [Fact] public async Task Academy_A_cannot_access_Academy_B_session()
    { using var client = factory.CreateClient(); await StaffLogin(client, DemoSeed.OwnerEmail); Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/attendance/sessions/{Slice4DemoSeed.AcademyBSessionId}")).StatusCode); }

    [Fact] public async Task Coach_cannot_access_unassigned_group_session()
    { using var client = factory.CreateClient(); await StaffLogin(client, DemoSeed.CoachEmail); Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/attendance/sessions/{Slice4DemoSeed.TodaySwimmingSessionId}")).StatusCode); }

    [Fact] public async Task Player_has_at_most_one_attendance_per_session()
    {
        var scenario = await Scenario(SubscriptionPlanType.Sessions); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var actor = await OwnerId(scope); var now = DateTimeOffset.UtcNow;
        db.PlayerAttendances.AddRange(Attendance(scenario, actor, now), Attendance(scenario, actor, now)); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact] public async Task Present_on_Duration_does_not_change_balance()
    { var s = await Scenario(SubscriptionPlanType.Duration); var result = await Save(s, AttendanceStatus.Present); Assert.False(result.SessionConsumed); Assert.Null(result.RemainingSessions); }

    [Fact] public async Task Present_on_Sessions_decrements_exactly_one()
    { var s = await Scenario(SubscriptionPlanType.Sessions); var before = await Balance(s); var result = await Save(s, AttendanceStatus.Present); Assert.True(result.SessionConsumed); Assert.Equal(before - 1, await Balance(s)); await Save(s, AttendanceStatus.Absent); }

    [Fact] public async Task Present_on_Combined_decrements_exactly_one()
    { var s = await Scenario(SubscriptionPlanType.Combined); var before = await Balance(s); var result = await Save(s, AttendanceStatus.Present); Assert.True(result.SessionConsumed); Assert.Equal(before - 1, await Balance(s)); await Save(s, AttendanceStatus.Absent); }

    [Fact] public async Task Absent_does_not_decrement_balance()
    { var s = await Scenario(SubscriptionPlanType.Sessions); var before = await Balance(s); await Save(s, AttendanceStatus.Absent); Assert.Equal(before, await Balance(s)); }

    [Fact] public async Task NotRecorded_does_not_decrement_balance()
    { var s = await Scenario(SubscriptionPlanType.Sessions); var before = await Balance(s); await Save(s, AttendanceStatus.NotRecorded); Assert.Equal(before, await Balance(s)); }

    [Fact] public async Task Present_saved_twice_consumes_once()
    { var s = await Scenario(SubscriptionPlanType.Sessions); var before = await Balance(s); await Save(s, AttendanceStatus.Present); await Save(s, AttendanceStatus.Present); Assert.Equal(before - 1, await Balance(s)); Assert.Equal(1, await MovementCount(s)); await Save(s, AttendanceStatus.Absent); }

    [Fact] public async Task Retried_HTTP_request_consumes_once()
    { var s = await Scenario(SubscriptionPlanType.Sessions); var before = await Balance(s); using var client = factory.CreateClient(); await StaffLogin(client, DemoSeed.OwnerEmail); var csrf = await Csrf(client); (await Put(client, s.SessionId, s.EnrollmentId, 2, csrf)).EnsureSuccessStatusCode(); (await Put(client, s.SessionId, s.EnrollmentId, 2, csrf)).EnsureSuccessStatusCode(); Assert.Equal(before - 1, await Balance(s)); await Save(s, AttendanceStatus.Absent); }

    [Fact] public async Task Concurrent_Present_requests_consume_once()
    {
        var s = await Scenario(SubscriptionPlanType.Sessions); var before = await Balance(s);
        async Task Record() { await using var scope = factory.Services.CreateAsyncScope(); var actor = await OwnerId(scope); await scope.ServiceProvider.GetRequiredService<AttendanceService>().SavePlayersAsync(DemoSeed.NogoomAcademyId, s.SessionId, actor, [new(s.EnrollmentId, AttendanceStatus.Present)]); }
        await Task.WhenAll(Task.Run(Record), Task.Run(Record)); Assert.Equal(before - 1, await Balance(s)); Assert.Equal(1, await MovementCount(s)); await Save(s, AttendanceStatus.Absent);
    }

    [Fact] public async Task Absent_to_Present_consumes_once()
    { var s = await Scenario(SubscriptionPlanType.Sessions); var before = await Balance(s); await Save(s, AttendanceStatus.Absent); await Save(s, AttendanceStatus.Present); Assert.Equal(before - 1, await Balance(s)); await Save(s, AttendanceStatus.Absent); }

    [Fact] public async Task Present_to_Absent_restores_one()
    { var s = await Scenario(SubscriptionPlanType.Sessions); var before = await Balance(s); await Save(s, AttendanceStatus.Present); await Save(s, AttendanceStatus.Absent); Assert.Equal(before, await Balance(s)); Assert.Equal(2, await MovementCount(s)); }

    [Fact] public async Task Present_to_NotRecorded_restores_one()
    { var s = await Scenario(SubscriptionPlanType.Sessions); var before = await Balance(s); await Save(s, AttendanceStatus.Present); await Save(s, AttendanceStatus.NotRecorded); Assert.Equal(before, await Balance(s)); Assert.Equal(2, await MovementCount(s)); }

    [Fact] public async Task Repeated_Present_to_Absent_does_not_over_restore()
    { var s = await Scenario(SubscriptionPlanType.Sessions); var before = await Balance(s); await Save(s, AttendanceStatus.Present); await Save(s, AttendanceStatus.Absent); await Save(s, AttendanceStatus.Absent); Assert.Equal(before, await Balance(s)); Assert.Equal(2, await MovementCount(s)); }

    [Fact] public async Task Zero_balance_never_becomes_negative()
    { var s = await Scenario(SubscriptionPlanType.Sessions); var original = await SetBalance(s, 0); try { await Save(s, AttendanceStatus.Present); Assert.Equal(0, await Balance(s)); } finally { await SetBalance(s, original); } }

    [Fact] public async Task Zero_balance_Present_is_recorded_with_warning()
    { var s = await Scenario(SubscriptionPlanType.Sessions); var original = await SetBalance(s, 0); try { var result = await Save(s, AttendanceStatus.Present); Assert.False(result.SessionConsumed); Assert.Contains("رصيد", result.Warning); Assert.Equal(AttendanceStatus.Present, await AttendanceStatusOf(s)); } finally { await SetBalance(s, original); } }

    [Fact] public async Task Attendance_for_player_in_unrelated_group_is_rejected()
    { var s = await Scenario(SubscriptionPlanType.Sessions); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var unrelated = await db.SportEnrollments.Where(x => x.PlayerId == Slice2DemoSeed.OmarPlayerId && x.SportId == Slice2DemoSeed.SwimmingId).Select(x => x.Id).SingleAsync(); var actor = await OwnerId(scope); await Assert.ThrowsAsync<AttendanceNotFoundException>(() => scope.ServiceProvider.GetRequiredService<AttendanceService>().SavePlayersAsync(DemoSeed.NogoomAcademyId, s.SessionId, actor, [new(unrelated, AttendanceStatus.Present)])); }

    [Fact] public async Task Attendance_for_unrelated_tenant_is_rejected()
    { await using var scope = factory.Services.CreateAsyncScope(); var actor = await OwnerId(scope); await Assert.ThrowsAsync<AttendanceNotFoundException>(() => scope.ServiceProvider.GetRequiredService<AttendanceService>().SavePlayersAsync(DemoSeed.NogoomAcademyId, Slice4DemoSeed.AcademyBSessionId, actor, [new(Guid.NewGuid(), AttendanceStatus.Present)])); }

    [Fact] public async Task Staff_attendance_does_not_affect_player_balance()
    { var s = await Scenario(SubscriptionPlanType.Sessions); var before = await Balance(s); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var membership = await db.AcademyMemberships.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.User.Email == DemoSeed.CoachEmail).Select(x => x.Id).SingleAsync(); var actor = await OwnerId(scope); await scope.ServiceProvider.GetRequiredService<AttendanceService>().SaveStaffAsync(DemoSeed.NogoomAcademyId, s.SessionId, actor, [new(membership, AttendanceStatus.Present)]); Assert.Equal(before, await Balance(s)); }

    [Fact] public async Task Cancelled_session_blocks_new_attendance()
    { await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var enrollment = await db.SportEnrollments.Where(x => x.PlayerId == Slice2DemoSeed.OtherPlayerId && x.SportId == Slice2DemoSeed.FootballId).Select(x => x.Id).SingleAsync(); var actor = await OwnerId(scope); await Assert.ThrowsAsync<AttendanceConflictException>(() => scope.ServiceProvider.GetRequiredService<AttendanceService>().SavePlayersAsync(DemoSeed.NogoomAcademyId, Slice4DemoSeed.CancelledFootballSessionId, actor, [new(enrollment, AttendanceStatus.Present)])); }

    [Fact] public async Task Guardian_sees_only_linked_child_attendance()
    { using var client = await GuardianClient(); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/guardian/children/{Slice2DemoSeed.OmarPlayerId}/attendance")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/guardian/children/{Slice2DemoSeed.OtherPlayerId}/attendance")).StatusCode); }

    [Fact] public async Task Guardian_cannot_mutate_attendance()
    { using var client = await GuardianClient(); var csrf = await Csrf(client); var response = await Put(client, Slice4DemoSeed.TodayFootballSessionId, Guid.NewGuid(), 2, csrf); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); }

    [Fact] public async Task Coach_cannot_access_financial_collections()
    { using var client = factory.CreateClient(); await StaffLogin(client, DemoSeed.CoachEmail); Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/subscriptions/collections")).StatusCode); }

    [Fact] public async Task Session_movement_audit_matches_balance_changes()
    { var s = await Scenario(SubscriptionPlanType.Sessions); var before = await Balance(s); await Save(s, AttendanceStatus.Present); await Save(s, AttendanceStatus.Absent); await using var scope = factory.Services.CreateAsyncScope(); var rows = await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().SubscriptionSessionMovements.Where(x => x.PlayerAttendance.TrainingSessionId == s.SessionId).OrderBy(x => x.OccurredAtUtc).ToListAsync(); Assert.Collection(rows, consume => { Assert.Equal(-1, consume.Quantity); Assert.Equal(before, consume.BalanceBefore); Assert.Equal(before - 1, consume.BalanceAfter); }, restore => { Assert.Equal(1, restore.Quantity); Assert.Equal(before - 1, restore.BalanceBefore); Assert.Equal(before, restore.BalanceAfter); Assert.Equal(rows[0].Id, restore.ReversesMovementId); }); }

    private async Task<ScenarioData> Scenario(SubscriptionPlanType type)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var playerId = type == SubscriptionPlanType.Combined ? Slice2DemoSeed.OmarPlayerId : type == SubscriptionPlanType.Duration ? Slice2DemoSeed.OmarPlayerId : Slice2DemoSeed.OtherPlayerId;
        var sportId = type == SubscriptionPlanType.Combined ? Slice2DemoSeed.SwimmingId : Slice2DemoSeed.FootballId;
        var enrollment = await db.SportEnrollments.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.PlayerId == playerId && x.SportId == sportId);
        var group = await db.TrainingGroups.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Id == enrollment.TrainingGroupId); var actor = await OwnerId(scope);
        var date = new DateOnly(2026, 9, 22); var starts = await db.TrainingSessions.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.TrainingGroupId == group.Id && x.SessionDate == date).Select(x => x.StartTime).ToListAsync(); var start = new TimeOnly(1, 0); while (starts.Contains(start)) start = start.AddMinutes(1);
        var session = new TrainingSession { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingGroupId = group.Id, BranchId = group.BranchId, SportId = group.SportId, AgeCategoryId = group.AgeCategoryId, SessionDate = date, StartTime = start, EndTime = start.AddHours(1), Status = TrainingSessionStatus.Scheduled, Source = TrainingSessionSource.Manual, CreatedByUserId = actor, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
        db.TrainingSessions.Add(session); await db.SaveChangesAsync(); return new(session.Id, enrollment.Id, group.Id);
    }

    private async Task<AttendanceSaveResult> Save(ScenarioData scenario, AttendanceStatus status)
    { await using var scope = factory.Services.CreateAsyncScope(); var actor = await OwnerId(scope); return (await scope.ServiceProvider.GetRequiredService<AttendanceService>().SavePlayersAsync(DemoSeed.NogoomAcademyId, scenario.SessionId, actor, [new(scenario.EnrollmentId, status)])).Single(); }
    private async Task<int> Balance(ScenarioData s) { await using var scope = factory.Services.CreateAsyncScope(); return await EligiblePeriod(scope, s).Select(x => x.RemainingSessions!.Value).FirstAsync(); }
    private async Task<int> SetBalance(ScenarioData s, int value) { await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var period = await EligiblePeriod(scope, s).FirstAsync(); var old = period.RemainingSessions ?? 0; period.RemainingSessions = value; await db.SaveChangesAsync(); return old; }
    private async Task<int> MovementCount(ScenarioData s) { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().SubscriptionSessionMovements.CountAsync(x => x.PlayerAttendance.TrainingSessionId == s.SessionId); }
    private async Task<AttendanceStatus> AttendanceStatusOf(ScenarioData s) { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().PlayerAttendances.Where(x => x.TrainingSessionId == s.SessionId && x.SportEnrollmentId == s.EnrollmentId).Select(x => x.Status).SingleAsync(); }
    private IQueryable<SubscriptionPeriod> EligiblePeriod(AsyncServiceScope scope, ScenarioData s) => scope.ServiceProvider.GetRequiredService<FoundationDbContext>().SubscriptionPeriods.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.SportEnrollmentId == s.EnrollmentId && x.Status != SubscriptionPeriodStatus.Cancelled && x.Status != SubscriptionPeriodStatus.Frozen && x.StartDate <= new DateOnly(2026, 9, 22) && (x.EndDate == null || x.EndDate >= new DateOnly(2026, 9, 22))).OrderBy(x => x.StartDate);
    private static PlayerAttendance Attendance(ScenarioData s, Guid actor, DateTimeOffset now) => new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingSessionId = s.SessionId, TrainingGroupId = s.GroupId, SportEnrollmentId = s.EnrollmentId, Status = AttendanceStatus.NotRecorded, RecordedByUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now };
    private static async Task<Guid> OwnerId(AsyncServiceScope scope) => await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Users.Where(x => x.Email == DemoSeed.OwnerEmail).Select(x => x.Id).SingleAsync();
    private static async Task<DateOnly> GenerationDate(FoundationDbContext db) { var date = new DateOnly(2040, 1, 3); while (await db.TrainingSessions.AnyAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.TrainingGroupId == Slice2DemoSeed.FootballGroupId && x.SessionDate == date)) date = date.AddDays(7); return date; }

    private async Task<HttpClient> GuardianClient() { var client = factory.CreateClient(); var request = await Post(client, "/api/v1/auth/guardian/otp/request", new { phoneNumber = DemoSeed.GuardianPhone }, await Csrf(client)); var challenge = await request.Content.ReadFromJsonAsync<Challenge>(); (await Post(client, "/api/v1/auth/guardian/otp/verify", new { challengeId = challenge!.ChallengeId, phoneNumber = DemoSeed.GuardianPhone, code = "246810" }, await Csrf(client))).EnsureSuccessStatusCode(); return client; }
    private static async Task StaffLogin(HttpClient client, string email) => (await Post(client, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" }, await Csrf(client))).EnsureSuccessStatusCode();
    private static async Task<string> Csrf(HttpClient client) => (await client.GetFromJsonAsync<CsrfDto>("/api/v1/auth/csrf"))!.Token;
    private static Task<HttpResponseMessage> Put(HttpClient client, Guid session, Guid enrollment, int status, string csrf) { var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/attendance/sessions/{session}/players") { Content = JsonContent.Create(new { items = new[] { new { sportEnrollmentId = enrollment, status } } }) }; request.Headers.Add("X-CSRF-TOKEN", csrf); return client.SendAsync(request); }
    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object body, string csrf) { var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", csrf); return client.SendAsync(request); }
    private sealed record ScenarioData(Guid SessionId, Guid EnrollmentId, Guid GroupId); private sealed record CsrfDto(string Token); private sealed record Challenge(Guid ChallengeId);
    private sealed class Factory(string connection) : WebApplicationFactory<Program> { protected override void ConfigureWebHost(IWebHostBuilder builder) { builder.UseEnvironment("Demo"); builder.UseSetting("ConnectionStrings:Default", connection); builder.UseSetting("Demo:SeedProfile", "LegacyRegression"); builder.UseSetting("Demo:SeedEnabled", "true"); builder.UseSetting("Demo:FixedOtpEnabled", "true"); builder.UseSetting("Demo:FixedOtp", "246810"); builder.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); builder.UseSetting("Demo:ReferenceDate", "2026-09-28"); builder.UseSetting("Payments:InternalTest:Enabled", "true"); builder.UseSetting("Payments:InternalTest:SigningKey", "Demo-Test-Signing-Key-Only-123456"); } }
}
