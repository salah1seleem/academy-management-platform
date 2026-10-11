using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Facilities;
using Academy.Api.Operations;
using Academy.Api.Slice4;
using Academy.Api.Slice5;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    private async Task<SessionTransferPreview> SessionReview(HttpClient owner, Guid id, params SessionTransferPair[] pairs)
    {
        var r = await Write(owner, $"/api/v1/people/enrollments/{id}/session-transfer/preview", new SessionTransferWrite(pairs)); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<SessionTransferPreview>())!;
    }
    private async Task<HttpResponseMessage> SessionSave(HttpClient owner, Guid id, SessionTransferPreview p, string? key = null)
    {
        var csrf = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/people/enrollments/{id}/session-transfer") { Content = JsonContent.Create(new SessionTransferWrite(p.Changes.Select(c => new SessionTransferPair(c.Source.Id, c.Target.Id)).ToArray(), p.Fingerprint)) }; request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString()); return await owner.SendAsync(request);
    }
    private async Task<(PlacementFixture F, Guid Target, Guid TargetSession)> OccurrenceReady(HttpClient owner, bool both = false, bool sessions = false)
    {
        var f = await PlacementReady(owner, planType: sessions ? Academy.Infrastructure.Subscriptions.SubscriptionPlanType.Combined : Academy.Infrastructure.Subscriptions.SubscriptionPlanType.Duration); (await Place(owner, f.Group, await ReviewPlacement(owner, f.Group, both ? [f.First, f.Second] : [f.First]))).EnsureSuccessStatusCode(); var target = await TransferTarget(owner, f);
        await using var s = factory.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<FoundationDbContext>(); return (f, target, await db.TrainingSessions.Where(x => x.TrainingGroupId == target).Select(x => x.Id).SingleAsync());
    }
    [Fact]
    public async Task Occurrence_transfer_changes_only_selected_roster_and_guardian_next_session_without_finance_or_balance()
    {
        using var owner = await Staff(); var f = await OccurrenceReady(owner); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var source = await db.TrainingSessions.SingleAsync(s => s.Id == f.F.Next); var later = Guid.NewGuid(); db.TrainingSessions.Add(new() { Id = later, AcademyId = source.AcademyId, TrainingGroupId = source.TrainingGroupId, BranchId = source.BranchId, SportId = source.SportId, AgeCategoryId = source.AgeCategoryId, SessionDate = source.SessionDate.AddDays(1), StartTime = source.StartTime, EndTime = source.EndTime, CreatedByUserId = source.CreatedByUserId }); await db.SaveChangesAsync();
        var before = (await db.Collections.CountAsync(), await db.PlayerAttendances.CountAsync(), await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == f.F.First).Select(p => p.RemainingSessions).SingleAsync());
        var preview = await SessionReview(owner, f.F.First, new SessionTransferPair(f.F.Next, f.TargetSession)); Assert.Empty(preview.Conflicts); var key = Guid.NewGuid().ToString(); (await SessionSave(owner, f.F.First, preview, key)).EnsureSuccessStatusCode(); Assert.True((await (await SessionSave(owner, f.F.First, preview, key)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        Assert.Equal(before, (await db.Collections.CountAsync(), await db.PlayerAttendances.CountAsync(), await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == f.F.First).Select(p => p.RemainingSessions).SingleAsync())); Assert.Equal(f.F.Group, await db.SportEnrollments.Where(e => e.Id == f.F.First).Select(e => e.TrainingGroupId).SingleAsync());
        Assert.False(await db.SportEnrollments.Where(e => e.Id == f.F.First).Select(e => FoundationDbContext.IsSessionAssigned(e.AcademyId, e.Id, f.F.Next)).SingleAsync()); Assert.True(await db.SportEnrollments.Where(e => e.Id == f.F.First).Select(e => FoundationDbContext.IsSessionAssigned(e.AcademyId, e.Id, later)).SingleAsync()); Assert.False(await db.SportEnrollments.Where(e => e.Id == f.F.First).Select(e => FoundationDbContext.IsSessionAssigned(DemoSeed.FutureAcademyId, e.Id, f.TargetSession)).SingleAsync());
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions/{f.F.Next}/players")).GetProperty("players").EnumerateArray()); Assert.Single((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions/{f.TargetSession}/players")).GetProperty("players").EnumerateArray());
        using var guardian = await Guardian(); var player = await PlayerFor(f.F.First); var profile = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{player}/profile"); var e = Assert.Single(profile.GetProperty("enrollments").EnumerateArray(), e => e.GetProperty("id").GetGuid() == f.F.First); Assert.Equal(f.TargetSession, e.GetProperty("upcoming")[0].GetProperty("id").GetGuid()); Assert.Single(e.GetProperty("sessionMoves").EnumerateArray()); Assert.Equal(later, e.GetProperty("upcoming")[1].GetProperty("id").GetGuid());
        var conflict = await TransferReview(owner, f.F.First, f.Target); Assert.Contains(conflict.Conflicts, c => c.Contains("حصص مختارة")); Assert.Equal(HttpStatusCode.Conflict, (await TransferSave(owner, f.F.First, conflict)).StatusCode);
        var row = await db.EnrollmentSessionMoves.SingleAsync(m => m.SportEnrollmentId == f.F.First); var err = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"EnrollmentSessionMoves\" WHERE \"Id\"={row.Id}")); Assert.Equal("23514", err.SqlState);
        row.TargetSessionId = later; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task Occurrence_transfer_last_seat_has_one_winner_and_blocks_capacity_shrink_and_calendar_replacement()
    {
        using var owner = await Staff(); var f = await OccurrenceReady(owner, true); var a = await SessionReview(owner, f.F.First, new SessionTransferPair(f.F.Next, f.TargetSession)); var b = await SessionReview(owner, f.F.Second, new SessionTransferPair(f.F.Next, f.TargetSession)); Assert.Empty(a.Conflicts); Assert.Empty(b.Conflicts);
        var results = await Task.WhenAll(SessionSave(owner, f.F.First, a), SessionSave(owner, f.F.Second, b)); Assert.Single(results, r => r.IsSuccessStatusCode); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.False(await DatedGroupRoster.CanAdd(db, f.Target, PlacementToday, 1));
        var body = new TimetableWrite(PlacementToday.AddDays(1), PlacementToday.AddDays(1), [PlacementToday.AddDays(1).DayOfWeek], new(14, 0), new(15, 0)); var p = await Write(owner, $"/api/v1/manage/groups/{f.Target}/timetable/preview", body); p.EnsureSuccessStatusCode(); Assert.Contains("نقل حصص", await p.Content.ReadAsStringAsync()); Assert.Equal(HttpStatusCode.Conflict, (await SaveTimetable(owner, f.Target, body, Guid.NewGuid().ToString())).StatusCode);
        var session = await db.TrainingSessions.SingleAsync(s => s.Id == f.TargetSession); session.Status = TrainingSessionStatus.Held; await db.SaveChangesAsync(); session.StartTime = new(14, 0); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task Occurrence_transfer_enforces_tenant_role_CSRF_and_rejects_unrelated_past_or_recorded_sessions()
    {
        using var owner = await Staff(); var f = await OccurrenceReady(owner); var path = $"/api/v1/people/enrollments/{f.F.First}/session-transfer/preview"; var body = new SessionTransferWrite([new(f.F.Next, f.TargetSession)]); using var foreign = await Staff("owner.future@example.test"); using var guardian = await Guardian(); using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(foreign, path, body)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Write(guardian, path, body)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Write(coach, path, body)).StatusCode); Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, body)).StatusCode);
        var past = await SessionReview(owner, f.F.First, new SessionTransferPair(f.F.Past, f.TargetSession)); Assert.Contains(past.Conflicts, c => c.Contains("الماضي")); Assert.Equal(HttpStatusCode.Conflict, (await SessionSave(owner, f.F.First, past)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var source = await db.TrainingSessions.SingleAsync(s => s.Id == f.F.Next); source.SessionDate = PlacementToday;
        db.PlayerAttendances.Add(new() { Id = Guid.NewGuid(), AcademyId = source.AcademyId, SportEnrollmentId = f.F.First, TrainingGroupId = f.F.Group, TrainingSessionId = source.Id, RecordedByUserId = source.CreatedByUserId, Status = AttendanceStatus.Absent }); await db.SaveChangesAsync();
        var recorded = await SessionReview(owner, f.F.First, new SessionTransferPair(source.Id, f.TargetSession)); Assert.Contains(recorded.Conflicts, c => c.Contains("سجل حضور")); Assert.Equal(HttpStatusCode.Conflict, (await SessionSave(owner, f.F.First, recorded)).StatusCode); Assert.False(await db.EnrollmentSessionMoves.AnyAsync(m => m.SportEnrollmentId == f.F.First));
    }
    [Fact]
    public async Task Occurrence_transfer_batch_is_atomic_and_rechecks_stale_preview_duplicate_ids_and_overlaps()
    {
        using var owner = await Staff(); var f = await OccurrenceReady(owner); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var s = await db.TrainingSessions.AsNoTracking().SingleAsync(s => s.Id == f.F.Next); var source2 = Guid.NewGuid(); var target2 = Guid.NewGuid();
        db.TrainingSessions.Add(new() { Id = source2, AcademyId = s.AcademyId, TrainingGroupId = f.F.Group, BranchId = s.BranchId, SportId = s.SportId, AgeCategoryId = s.AgeCategoryId, SessionDate = s.SessionDate.AddDays(1), StartTime = new(10, 0), EndTime = new(11, 0), CreatedByUserId = s.CreatedByUserId });
        db.TrainingSessions.Add(new() { Id = target2, AcademyId = s.AcademyId, TrainingGroupId = f.Target, BranchId = s.BranchId, SportId = s.SportId, AgeCategoryId = s.AgeCategoryId, SessionDate = s.SessionDate.AddDays(1), StartTime = new(13, 0), EndTime = new(14, 0), CreatedByUserId = s.CreatedByUserId }); await db.SaveChangesAsync();
        var pairs = new[] { new SessionTransferPair(f.F.Next, f.TargetSession), new SessionTransferPair(source2, target2) }; var p = await SessionReview(owner, f.F.First, pairs); Assert.Empty(p.Conflicts);
        var changed = await db.TrainingSessions.SingleAsync(x => x.Id == target2); changed.StartTime = new(14, 0); changed.EndTime = new(15, 0); await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Conflict, (await SessionSave(owner, f.F.First, p)).StatusCode); Assert.False(await db.EnrollmentSessionMoves.AnyAsync(m => m.SportEnrollmentId == f.F.First));
        var fresh = await SessionReview(owner, f.F.First, pairs); var saved = await SessionSave(owner, f.F.First, fresh); saved.EnsureSuccessStatusCode(); Assert.Equal(2, await db.EnrollmentSessionMoves.CountAsync(m => m.SportEnrollmentId == f.F.First));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, $"/api/v1/people/enrollments/{f.F.First}/session-transfer/preview", new SessionTransferWrite([pairs[0], pairs[0]]))).StatusCode);
        var repeated = await SessionReview(owner, f.F.First, pairs[0]); Assert.Contains(repeated.Conflicts, c => c.Contains("نقل معتمد")); Assert.Equal(HttpStatusCode.Conflict, (await SessionSave(owner, f.F.First, repeated)).StatusCode);
    }
    [Fact]
    public async Task Occurrence_transfer_target_coach_can_evaluate_actual_session_but_cannot_claim_source_group_or_date()
    {
        using var owner = await Staff(); var f = await OccurrenceReady(owner); var p = await SessionReview(owner, f.F.First, new SessionTransferPair(f.F.Next, f.TargetSession)); (await SessionSave(owner, f.F.First, p)).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var assignment = await db.StaffGroupAssignments.Include(a => a.AcademyMembership).ThenInclude(m => m.User).SingleAsync(a => a.TrainingGroupId == f.Target && a.IsActive);
        using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services, assignment.AcademyMembership.User.Email!);
        (await Write(owner, "/api/v1/evaluations/criteria", new CriterionRequest(Academy.Api.Slice2.Slice2DemoSeed.SwimmingId, "معيار الحصة البديلة " + Guid.NewGuid().ToString("N")[..8], null, null, 0, true, 1, null))).EnsureSuccessStatusCode();
        var date = await db.TrainingSessions.Where(s => s.Id == f.TargetSession).Select(s => s.SessionDate).SingleAsync(); var body = new CreateEvaluationRequest(f.F.First, date, "حصة بديلة", null, f.TargetSession);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(coach, "/api/v1/evaluations", body with { TrainingSessionId = null })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(coach, "/api/v1/evaluations", body with { TrainingSessionId = f.F.Next })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(coach, "/api/v1/evaluations", body with { EvaluationDate = date.AddDays(1) })).StatusCode);
        var saved = await Write(coach, "/api/v1/evaluations", body); saved.EnsureSuccessStatusCode(); var id = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(f.Target, await db.PlayerEvaluations.Where(e => e.Id == id).Select(e => e.TrainingGroupId).SingleAsync()); Assert.True((await coach.GetAsync($"/api/v1/evaluations/{id}")).IsSuccessStatusCode);
        var roster = await coach.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions/{f.TargetSession}/players"); Assert.True(roster.GetProperty("canEvaluate").GetBoolean()); Assert.False(roster.GetProperty("canManageAttendance").GetBoolean());
        assignment.IsActive = false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.NotFound, (await Write(coach, "/api/v1/evaluations", body)).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await coach.GetAsync($"/api/v1/evaluations/{id}")).StatusCode);
    }
    [Fact]
    public async Task Occurrence_transfer_today_consumes_at_target_once_not_source_and_reports_real_session_group()
    {
        using var owner = await Staff(); var f = await OccurrenceReady(owner, sessions: true); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        foreach (var id in new[] { f.F.Next, f.TargetSession }) (await db.TrainingSessions.SingleAsync(s => s.Id == id)).SessionDate = PlacementToday; await db.SaveChangesAsync();
        var p = await SessionReview(owner, f.F.First, new SessionTransferPair(f.F.Next, f.TargetSession)); Assert.Empty(p.Conflicts); (await SessionSave(owner, f.F.First, p)).EnsureSuccessStatusCode(); var service = scope.ServiceProvider.GetRequiredService<AttendanceService>(); var actor = await db.Users.Where(x => x.Email == DemoSeed.OwnerEmail).Select(x => x.Id).SingleAsync();
        var before = await db.SubscriptionPeriods.Where(x => x.SportEnrollmentId == f.F.First).Select(x => x.RemainingSessions).SingleAsync(); await Assert.ThrowsAsync<AttendanceNotFoundException>(() => service.SavePlayersAsync(DemoSeed.NogoomAcademyId, f.F.Next, actor, [new(f.F.First, AttendanceStatus.Present)]));
        await service.SavePlayersAsync(DemoSeed.NogoomAcademyId, f.TargetSession, actor, [new(f.F.First, AttendanceStatus.Present)]); await service.SavePlayersAsync(DemoSeed.NogoomAcademyId, f.TargetSession, actor, [new(f.F.First, AttendanceStatus.Present)]);
        db.ChangeTracker.Clear(); Assert.Equal(before - 1, await db.SubscriptionPeriods.Where(x => x.SportEnrollmentId == f.F.First).Select(x => x.RemainingSessions).SingleAsync()); Assert.Equal(f.Target, await db.PlayerAttendances.Where(x => x.SportEnrollmentId == f.F.First).Select(x => x.TrainingGroupId).SingleAsync()); Assert.Single(await db.SubscriptionSessionMovements.Where(x => x.PlayerAttendance.SportEnrollmentId == f.F.First).ToArrayAsync());
    }
}
