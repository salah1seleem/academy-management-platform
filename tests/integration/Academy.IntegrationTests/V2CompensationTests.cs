using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    private async Task<(PlacementFixture F, Guid Target, Guid TargetSession)> CompensationReady(HttpClient owner, bool both = false)
    {
        var f = await OccurrenceReady(owner, both, sessions: true); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        foreach (var e in await db.SportEnrollments.Where(e => e.TrainingGroupId == f.F.Group).ToArrayAsync()) e.GroupAssignedFrom = PlacementToday.AddDays(-10);
        (await db.TrainingSessions.SingleAsync(s => s.Id == f.F.Past)).Status = TrainingSessionStatus.Cancelled; await db.SaveChangesAsync(); return f;
    }
    private async Task<CompensationGrantWrite> CompensationReview(HttpClient c, Guid enrollment, Guid source, CompensationCause cause = CompensationCause.CancelledSession, CompensationUnit unit = CompensationUnit.Session)
    {
        var b = new CompensationGrantWrite(source, cause, unit, PlacementToday, PlacementToday.AddDays(30)); var r = await Write(c, $"/api/v1/people/enrollments/{enrollment}/compensation/preview", b); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); var p = (await r.Content.ReadFromJsonAsync<CompensationGrantPreview>())!; Assert.Empty(p.Conflicts); return b with { Fingerprint = p.Fingerprint };
    }
    private static async Task<HttpResponseMessage> CompensationCommand(HttpClient c, string path, object body, string? key = null)
    {
        var token = await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var r = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; r.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString()); r.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString()); return await c.SendAsync(r);
    }
    private async Task<Guid> CompensationGrant(HttpClient c, Guid enrollment, Guid source, CompensationCause cause = CompensationCause.CancelledSession, CompensationUnit unit = CompensationUnit.Session)
    {
        var b = await CompensationReview(c, enrollment, source, cause, unit); var r = await CompensationCommand(c, $"/api/v1/people/enrollments/{enrollment}/compensation", b); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private async Task<CompensationBookWrite> CompensationBookReview(HttpClient c, Guid grant, Guid session, bool clear = true)
    {
        var b = new CompensationBookWrite(session); var r = await Write(c, $"/api/v1/people/compensations/{grant}/booking/preview", b); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); var p = (await r.Content.ReadFromJsonAsync<CompensationBookPreview>())!; if (clear) Assert.Empty(p.Conflicts); else Assert.NotEmpty(p.Conflicts); return b with { Fingerprint = p.Fingerprint };
    }
    [Fact]
    public async Task Compensation_grant_is_explicit_idempotent_immutable_and_never_creates_collection_or_child_absence()
    {
        using var owner = await Staff(); var f = await CompensationReady(owner); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var before = (await db.Collections.CountAsync(), await db.PlayerAttendances.CountAsync(), await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == f.F.First).Select(p => p.RemainingSessions).SingleAsync());
        var b = await CompensationReview(owner, f.F.First, f.F.Past); var path = $"/api/v1/people/enrollments/{f.F.First}/compensation"; var key = Guid.NewGuid().ToString(); var saved = await CompensationCommand(owner, path, b, key); saved.EnsureSuccessStatusCode(); var id = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.True((await (await CompensationCommand(owner, path, b, key)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean()); Assert.Equal(HttpStatusCode.Conflict, (await CompensationCommand(owner, path, b with { Unit = CompensationUnit.Minute }, key)).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await CompensationCommand(owner, path, b)).StatusCode);
        Assert.Equal(before, (await db.Collections.CountAsync(), await db.PlayerAttendances.CountAsync(), await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == f.F.First).Select(p => p.RemainingSessions).SingleAsync()));
        var row = await db.CompensationGrants.SingleAsync(g => g.Id == id); Assert.Equal(1, row.Units); Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"CompensationGrants\" WHERE \"Id\"={id}"))).SqlState);
        row.Units = 2; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task Compensation_attendance_consumes_once_restores_once_and_never_double_charges_original_subscription()
    {
        using var owner = await Staff(); var f = await CompensationReady(owner); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); (await db.TrainingSessions.SingleAsync(s => s.Id == f.TargetSession)).SessionDate = PlacementToday; await db.SaveChangesAsync();
        var grant = await CompensationGrant(owner, f.F.First, f.F.Past); var b = await CompensationBookReview(owner, grant, f.TargetSession); var path = $"/api/v1/people/compensations/{grant}/booking"; var saved = await CompensationCommand(owner, path, b); saved.EnsureSuccessStatusCode(); var booking = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var service = scope.ServiceProvider.GetRequiredService<AttendanceService>(); var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync(); var original = await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == f.F.First).Select(p => p.RemainingSessions).SingleAsync();
        foreach (var status in new[] { AttendanceStatus.Present, AttendanceStatus.Present, AttendanceStatus.Absent, AttendanceStatus.Absent, AttendanceStatus.Present }) await service.SavePlayersAsync(DemoSeed.NogoomAcademyId, f.TargetSession, actor, [new(f.F.First, status)]);
        db.ChangeTracker.Clear(); Assert.Equal(original, await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == f.F.First).Select(p => p.RemainingSessions).SingleAsync()); Assert.Equal(1, await db.CompensationMovements.Where(m => m.GrantId == grant).SumAsync(m => m.Units)); Assert.Equal(3, await db.CompensationMovements.CountAsync(m => m.GrantId == grant)); Assert.Empty(await db.SubscriptionSessionMovements.Where(m => m.PlayerAttendance.SportEnrollmentId == f.F.First).ToArrayAsync());
        var row = await db.CompensationBookings.SingleAsync(b => b.Id == booking); Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/people/compensation-bookings/{booking}/cancel", new CompensationCancelWrite(row.Version))).StatusCode);
        using var guardian = await Guardian(); var player = await PlayerFor(f.F.First); var profile = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{player}/profile"); Assert.Equal(1, Assert.Single(profile.GetProperty("compensations").EnumerateArray()).GetProperty("consumed").GetInt32());
        await service.SavePlayersAsync(DemoSeed.NogoomAcademyId, f.TargetSession, actor, [new(f.F.First, AttendanceStatus.NotRecorded)]); (await Write(owner, $"/api/v1/people/compensation-bookings/{booking}/cancel", new CompensationCancelWrite(row.Version))).EnsureSuccessStatusCode();
        Assert.False(await db.SportEnrollments.Where(e => e.Id == f.F.First).Select(e => FoundationDbContext.IsSessionAssigned(e.AcademyId, e.Id, f.TargetSession)).SingleAsync()); Assert.Equal(0, await db.CompensationMovements.Where(m => m.GrantId == grant).SumAsync(m => m.Units));
    }
    [Fact]
    public async Task Compensation_minutes_can_be_split_but_never_overallocated_and_cancellation_preserves_history()
    {
        using var owner = await Staff(); var f = await CompensationReady(owner); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var source = await db.TrainingSessions.SingleAsync(s => s.Id == f.F.Past); source.EndTime = new(12, 0);
        var target = await db.TrainingSessions.AsNoTracking().SingleAsync(s => s.Id == f.TargetSession); var second = Guid.NewGuid(); db.TrainingSessions.Add(new() { Id = second, AcademyId = target.AcademyId, BranchId = target.BranchId, SportId = target.SportId, TrainingGroupId = target.TrainingGroupId, AgeCategoryId = target.AgeCategoryId, SessionDate = target.SessionDate.AddDays(1), StartTime = target.StartTime, EndTime = target.EndTime, CreatedByUserId = target.CreatedByUserId }); await db.SaveChangesAsync();
        var grant = await CompensationGrant(owner, f.F.First, f.F.Past, unit: CompensationUnit.Minute); var path = $"/api/v1/people/compensations/{grant}/booking";
        var first = await CompensationBookReview(owner, grant, target.Id); var key = Guid.NewGuid().ToString(); (await CompensationCommand(owner, path, first, key)).EnsureSuccessStatusCode(); Assert.True((await (await CompensationCommand(owner, path, first, key)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        (await CompensationCommand(owner, path, await CompensationBookReview(owner, grant, second))).EnsureSuccessStatusCode(); Assert.Equal(120, await db.CompensationBookings.Where(b => b.GrantId == grant && b.State == CompensationBookingState.Reserved).SumAsync(b => b.Units));
        var again = await CompensationBookReview(owner, grant, target.Id, false); Assert.Equal(HttpStatusCode.Conflict, (await CompensationCommand(owner, path, again)).StatusCode);
        var row = await db.CompensationBookings.SingleAsync(b => b.GrantId == grant && b.TrainingSessionId == target.Id); Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/people/compensation-bookings/{row.Id}/cancel", new CompensationCancelWrite(row.Version + 1))).StatusCode);
        (await Write(owner, $"/api/v1/people/compensation-bookings/{row.Id}/cancel", new CompensationCancelWrite(row.Version))).EnsureSuccessStatusCode(); Assert.Equal(2, await db.CompensationBookings.CountAsync(b => b.GrantId == grant));
        var retry = await CompensationBookReview(owner, grant, target.Id); (await CompensationCommand(owner, path, retry)).EnsureSuccessStatusCode(); Assert.Equal(3, await db.CompensationBookings.CountAsync(b => b.GrantId == grant));
    }
    [Fact]
    public async Task Compensation_last_seat_is_serialized_and_calendar_cannot_drop_a_reserved_makeup()
    {
        using var owner = await Staff(); var f = await CompensationReady(owner, true); var first = await CompensationGrant(owner, f.F.First, f.F.Past); var second = await CompensationGrant(owner, f.F.Second, f.F.Past); var a = await CompensationBookReview(owner, first, f.TargetSession); var b = await CompensationBookReview(owner, second, f.TargetSession);
        var results = await Task.WhenAll(CompensationCommand(owner, $"/api/v1/people/compensations/{first}/booking", a), CompensationCommand(owner, $"/api/v1/people/compensations/{second}/booking", b)); Assert.Single(results, r => r.IsSuccessStatusCode); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.False(await DatedGroupRoster.CanAdd(db, f.Target, PlacementToday, 1)); var session = await db.TrainingSessions.SingleAsync(s => s.Id == f.TargetSession); session.Status = TrainingSessionStatus.Cancelled; await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task Compensation_can_cover_its_own_full_group_without_double_counting_and_rechecks_stale_capacity()
    {
        using var owner = await Staff(); var f = await CompensationReady(owner); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var group = await db.TrainingGroups.SingleAsync(g => g.Id == f.F.Group); group.Capacity = 1; await db.SaveChangesAsync();
        var grant = await CompensationGrant(owner, f.F.First, f.F.Past); var same = await CompensationBookReview(owner, grant, f.F.Next); var path = $"/api/v1/people/compensations/{grant}/booking";
        group.Capacity = 2; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Conflict, (await CompensationCommand(owner, path, same)).StatusCode);
        var fresh = await CompensationBookReview(owner, grant, f.F.Next); (await CompensationCommand(owner, path, fresh)).EnsureSuccessStatusCode();
        var roster = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions/{f.F.Next}/players"); Assert.True(Assert.Single(roster.GetProperty("players").EnumerateArray()).GetProperty("compensatory").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/manage/groups/{f.F.Group}/placement-level", new PlacementLevelWrite(null), true)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/people/enrollments/{f.F.First}/placement-level", new PlacementLevelWrite(null), true)).StatusCode);
        var otherPeriod = await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId != f.F.First).Select(p => p.Id).FirstAsync(); var source = await db.TrainingSessions.SingleAsync(s => s.Id == f.F.Next);
        var badGrant = Guid.NewGuid(); var key = Guid.NewGuid().ToString();
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"CompensationGrants\" (\"Id\",\"AcademyId\",\"SportEnrollmentId\",\"BranchId\",\"SportId\",\"SourceSessionId\",\"SourceGroupId\",\"SourcePeriodId\",\"Cause\",\"Unit\",\"Units\",\"ValidFrom\",\"ValidTo\",\"ActorUserId\",\"RequestKey\",\"RequestHash\",\"Snapshot\",\"IsActive\",\"CreatedAtUtc\",\"UpdatedAtUtc\") VALUES ({badGrant},{source.AcademyId},{f.F.First},{source.BranchId},{source.SportId},{source.Id},{source.TrainingGroupId},{otherPeriod},'CancelledSession','Session',1,{PlacementToday},{PlacementToday.AddDays(30)},{source.CreatedByUserId},{key},'hash','{{}}'::jsonb,true,now(),now())"))).SqlState);
    }
    [Fact]
    public async Task Compensation_rejects_invalid_reason_dates_scope_roles_CSRF_and_future_attendance()
    {
        using var owner = await Staff(); var f = await CompensationReady(owner); var path = $"/api/v1/people/enrollments/{f.F.First}/compensation/preview"; var b = new CompensationGrantWrite(f.F.Past, CompensationCause.CancelledSession, CompensationUnit.Session, PlacementToday, PlacementToday.AddDays(30));
        using var foreign = await Staff("owner.future@example.test"); using var guardian = await Guardian(); using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(foreign, path, b)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Write(guardian, path, b)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Write(coach, path, b)).StatusCode); Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, b)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, path, b with { ValidTo = PlacementToday.AddDays(91) })).StatusCode);
        var invalid = await Write(owner, path, b with { Cause = CompensationCause.CoachAbsent }); invalid.EnsureSuccessStatusCode(); Assert.NotEmpty((await invalid.Content.ReadFromJsonAsync<CompensationGrantPreview>())!.Conflicts);
        var grant = await CompensationGrant(owner, f.F.First, f.F.Past); var book = await CompensationBookReview(owner, grant, f.TargetSession); (await CompensationCommand(owner, $"/api/v1/people/compensations/{grant}/booking", book)).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var service = scope.ServiceProvider.GetRequiredService<AttendanceService>(); var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync(); await Assert.ThrowsAsync<AttendanceValidationException>(() => service.SavePlayersAsync(DemoSeed.NogoomAcademyId, f.TargetSession, actor, [new(f.F.First, AttendanceStatus.Present)]));
        var other = await Fixture(linkedGuardian: false); Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync($"/api/v1/people/enrollments/{other.Enrollment}/compensations")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync($"/api/v1/guardian/children/{await PlayerFor(other.Enrollment)}/profile")).StatusCode);
    }
}
