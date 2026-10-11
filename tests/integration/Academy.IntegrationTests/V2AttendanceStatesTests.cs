using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    private async Task<PlacementFixture> AttendanceReady(HttpClient owner)
    {
        var f = await PlacementReady(owner, planType: SubscriptionPlanType.Combined); var reviewed = await ReviewPlacement(owner, f.Group, f.First, f.Second); (await Place(owner, f.Group, reviewed)).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); (await db.TrainingSessions.SingleAsync(s => s.Id == f.Next)).SessionDate = PlacementToday; await db.SaveChangesAsync(); return f;
    }
    private static Task<HttpResponseMessage> AttendanceWrite(HttpClient c, Guid session, params PlayerAttendanceChange[] items) => Write(c, $"/api/v1/attendance/sessions/{session}/players", new { items }, true);
    private static async Task<uint> AttendanceVersion(HttpClient c, Guid session, Guid enrollment)
    {
        var roster = await c.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions/{session}/players"); return roster.GetProperty("players").EnumerateArray().Single(e => e.GetProperty("sportEnrollmentId").GetGuid() == enrollment).GetProperty("version").GetUInt32();
    }
    [Fact]
    public async Task Attendance_states_late_present_retry_and_excused_correction_have_exact_balance_effects()
    {
        using var owner = await Staff(); var f = await AttendanceReady(owner); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var before = await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == f.First).Select(p => p.RemainingSessions).SingleAsync(); var money = await db.Collections.CountAsync();
        (await AttendanceWrite(owner, f.Next, new PlayerAttendanceChange(f.First, AttendanceStatus.Late, 0))).EnsureSuccessStatusCode(); var version = await AttendanceVersion(owner, f.Next, f.First); (await AttendanceWrite(owner, f.Next, new PlayerAttendanceChange(f.First, AttendanceStatus.Late, 0))).EnsureSuccessStatusCode(); Assert.Equal(version, await AttendanceVersion(owner, f.Next, f.First));
        (await AttendanceWrite(owner, f.Next, new PlayerAttendanceChange(f.First, AttendanceStatus.Present, version))).EnsureSuccessStatusCode(); Assert.Equal(before - 1, await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == f.First).Select(p => p.RemainingSessions).SingleAsync()); Assert.Equal(1, await db.SubscriptionSessionMovements.CountAsync(m => m.PlayerAttendance.SportEnrollmentId == f.First));
        (await AttendanceWrite(owner, f.Next, new PlayerAttendanceChange(f.First, AttendanceStatus.Excused, await AttendanceVersion(owner, f.Next, f.First)))).EnsureSuccessStatusCode(); (await AttendanceWrite(owner, f.Next, new PlayerAttendanceChange(f.First, AttendanceStatus.Excused, 0))).EnsureSuccessStatusCode(); Assert.Equal(before, await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == f.First).Select(p => p.RemainingSessions).SingleAsync()); Assert.Equal(2, await db.SubscriptionSessionMovements.CountAsync(m => m.PlayerAttendance.SportEnrollmentId == f.First));
        (await AttendanceWrite(owner, f.Next, new PlayerAttendanceChange(f.First, AttendanceStatus.NotRecorded, await AttendanceVersion(owner, f.Next, f.First)))).EnsureSuccessStatusCode(); Assert.Equal(money, await db.Collections.CountAsync()); Assert.Single(await db.PlayerAttendances.Where(a => a.TrainingSessionId == f.Next && a.SportEnrollmentId == f.First).ToArrayAsync());
    }
    [Fact]
    public async Task Attendance_states_stale_batch_is_atomic_and_unchanged_retry_does_not_lose_a_correction()
    {
        using var owner = await Staff(); var f = await AttendanceReady(owner); (await AttendanceWrite(owner, f.Next, new PlayerAttendanceChange(f.First, AttendanceStatus.Late, 0))).EnsureSuccessStatusCode();
        var stale = await AttendanceWrite(owner, f.Next, new PlayerAttendanceChange(f.Second, AttendanceStatus.Present, 0), new PlayerAttendanceChange(f.First, AttendanceStatus.Excused, 0)); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.Equal(0u, await AttendanceVersion(owner, f.Next, f.Second));
        var version = await AttendanceVersion(owner, f.Next, f.First); var results = await Task.WhenAll(AttendanceWrite(owner, f.Next, new PlayerAttendanceChange(f.First, AttendanceStatus.Present, version)), AttendanceWrite(owner, f.Next, new PlayerAttendanceChange(f.First, AttendanceStatus.Excused, version))); Assert.Single(results, r => r.IsSuccessStatusCode); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.False(await db.PlayerAttendances.AnyAsync(a => a.TrainingSessionId == f.Next && a.SportEnrollmentId == f.Second)); var current = await db.PlayerAttendances.Where(a => a.TrainingSessionId == f.Next && a.SportEnrollmentId == f.First).Select(a => a.Status).SingleAsync(); Assert.Equal(AttendanceRules.Attended(current) ? -1 : 0, await db.SubscriptionSessionMovements.Where(m => m.PlayerAttendance.SportEnrollmentId == f.First).SumAsync(m => m.Quantity));
    }
    [Fact]
    public async Task Attendance_states_read_models_reports_and_CSV_preserve_late_and_excused_without_invented_absence()
    {
        using var owner = await Staff(); var f = await AttendanceReady(owner); (await AttendanceWrite(owner, f.Next, new PlayerAttendanceChange(f.First, AttendanceStatus.Late, 0), new PlayerAttendanceChange(f.Second, AttendanceStatus.Excused, 0))).EnsureSuccessStatusCode();
        var day = PlacementToday.ToString("yyyy-MM-dd"); var sessions = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions?from={day}&to={day}&groupId={f.Group}"); var session = Assert.Single(sessions.EnumerateArray()); Assert.Equal(1, session.GetProperty("present").GetInt32()); Assert.Equal(1, session.GetProperty("late").GetInt32()); Assert.Equal(1, session.GetProperty("excused").GetInt32()); Assert.Equal(0, session.GetProperty("absent").GetInt32());
        var report = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/attendance?from={day}&to={day}&groupId={f.Group}&status=Late"); Assert.Equal("Late", Assert.Single(report.GetProperty("items").EnumerateArray()).GetProperty("status").GetString()); var csv = await owner.GetStringAsync($"/api/v1/reports/attendance/export?from={day}&to={day}&groupId={f.Group}"); Assert.Contains("متأخر", csv); Assert.Contains("معتذر", csv);
        using var guardian = await Guardian(); var profile = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{await PlayerFor(f.First)}/profile"); Assert.Equal(1, profile.GetProperty("attendanceSummary").GetProperty("present").GetInt32()); Assert.Equal(1, profile.GetProperty("attendanceSummary").GetProperty("late").GetInt32()); Assert.Equal(0, profile.GetProperty("attendanceSummary").GetProperty("absent").GetInt32());
    }
    [Fact]
    public async Task Attendance_states_keep_scope_CSRF_coach_default_and_future_cancelled_guards()
    {
        using var owner = await Staff(); var f = await AttendanceReady(owner); using var guardian = await Guardian(); using var foreign = await Staff("owner.future@example.test"); using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services); var path = $"/api/v1/attendance/sessions/{f.Next}/players"; var body = new { items = new[] { new PlayerAttendanceChange(f.First, AttendanceStatus.Late, 0) } };
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync(path, body)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Write(guardian, path, body, true)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Write(coach, path, body, true)).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await Write(foreign, path, body, true)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var s = await db.TrainingSessions.SingleAsync(s => s.Id == f.Next); s.SessionDate = PlacementToday.AddDays(1); await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.BadRequest, (await Write(owner, path, body, true)).StatusCode); s.SessionDate = PlacementToday; s.Status = TrainingSessionStatus.Cancelled; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, path, body, true)).StatusCode); Assert.False(await db.PlayerAttendances.AnyAsync(a => a.TrainingSessionId == f.Next));
    }
    [Fact]
    public async Task Attendance_states_late_compensation_is_consumed_once_and_excused_restores_without_original_debit()
    {
        using var owner = await Staff(); var f = await CompensationReady(owner); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); (await db.TrainingSessions.SingleAsync(s => s.Id == f.TargetSession)).SessionDate = PlacementToday; await db.SaveChangesAsync();
        var grant = await CompensationGrant(owner, f.F.First, f.F.Past); var b = await CompensationBookReview(owner, grant, f.TargetSession); var saved = await CompensationCommand(owner, $"/api/v1/people/compensations/{grant}/booking", b); saved.EnsureSuccessStatusCode(); var booking = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); var money = await db.Collections.CountAsync();
        foreach (var status in new[] { AttendanceStatus.Late, AttendanceStatus.Late, AttendanceStatus.Present, AttendanceStatus.Late }) (await AttendanceWrite(owner, f.TargetSession, new PlayerAttendanceChange(f.F.First, status, await AttendanceVersion(owner, f.TargetSession, f.F.First)))).EnsureSuccessStatusCode();
        Assert.Equal(1, await db.CompensationMovements.CountAsync(m => m.GrantId == grant)); var version = await db.CompensationBookings.Where(b => b.Id == booking).Select(b => b.Version).SingleAsync(); Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/people/compensation-bookings/{booking}/cancel", new CompensationCancelWrite(version))).StatusCode);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CompensationBookings\" SET \"State\"='Cancelled' WHERE \"Id\"={booking}"))).SqlState);
        (await AttendanceWrite(owner, f.TargetSession, new PlayerAttendanceChange(f.F.First, AttendanceStatus.Excused, await AttendanceVersion(owner, f.TargetSession, f.F.First)))).EnsureSuccessStatusCode();
        Assert.Equal(2, await db.CompensationMovements.CountAsync(m => m.GrantId == grant)); Assert.Equal(0, await db.CompensationMovements.Where(m => m.GrantId == grant).SumAsync(m => m.Units)); Assert.Empty(await db.SubscriptionSessionMovements.Where(m => m.PlayerAttendance.SportEnrollmentId == f.F.First).ToArrayAsync()); Assert.Equal(money, await db.Collections.CountAsync());
    }
    [Fact]
    public async Task Attendance_states_demo_seed_is_repeatable_and_preserves_reviewer_data_without_automatic_attendance()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var collections = scope.ServiceProvider.GetRequiredService<CollectionService>(); await GroupPlacementDemoSeed.Seed(db, collections); await AttendanceStatesDemoSeed.Seed(db, collections);
        var player = await db.Players.SingleAsync(p => p.Id == GroupPlacementDemoSeed.Id(110)); player.ArabicName = "لاعب حضور راجعه المختبر"; await db.SaveChangesAsync(); var before = (await db.Collections.CountAsync(), await db.PlayerAttendances.CountAsync(), await db.Players.CountAsync()); await AttendanceStatesDemoSeed.Seed(db, collections); await db.Entry(player).ReloadAsync(); Assert.Equal("لاعب حضور راجعه المختبر", player.ArabicName); Assert.Equal(before, (await db.Collections.CountAsync(), await db.PlayerAttendances.CountAsync(), await db.Players.CountAsync())); Assert.False(await db.PlayerAttendances.AnyAsync(a => a.TrainingSessionId == GroupPlacementDemoSeed.Id(131)));
    }
    [Fact]
    public async Task Attendance_states_staff_versions_and_database_state_constraints_are_enforced()
    {
        using var owner = await Staff(); var f = await AttendanceReady(owner); var path = $"/api/v1/attendance/sessions/{f.Next}/staff";
        (await Write(owner, path, new { items = new[] { new StaffAttendanceChange(f.Coach, AttendanceStatus.Late, 0) } }, true)).EnsureSuccessStatusCode(); var roster = await owner.GetFromJsonAsync<JsonElement>(path); var row = Assert.Single(roster.EnumerateArray()); Assert.Equal("Late", row.GetProperty("attendanceStatus").GetString()); var version = row.GetProperty("version").GetUInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, path, new { items = new[] { new StaffAttendanceChange(f.Coach, AttendanceStatus.Excused, 0) } }, true)).StatusCode); (await Write(owner, path, new { items = new[] { new StaffAttendanceChange(f.Coach, AttendanceStatus.Excused, version) } }, true)).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StaffAttendances\" SET \"Status\"='Unknown' WHERE \"TrainingSessionId\"={f.Next}"))).SqlState);
    }
}
