using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    private async Task<Guid> TransferTarget(HttpClient owner, PlacementFixture f, int capacity = 1)
    {
        var id = await PlacementAlternative(owner, f, 13);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        (await db.TrainingGroups.SingleAsync(g => g.Id == id)).Capacity = capacity; await db.SaveChangesAsync(); return id;
    }
    private async Task<GroupTransferPreview> TransferReview(HttpClient owner, Guid id, Guid group, DateOnly? date = null)
    {
        var r = await Write(owner, $"/api/v1/people/enrollments/{id}/group-transfer/preview", new GroupTransferWrite(group, date ?? PlacementToday.AddDays(1))); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<GroupTransferPreview>())!;
    }
    private async Task<HttpResponseMessage> TransferSave(HttpClient owner, Guid id, GroupTransferPreview p, string? key = null)
    {
        var token = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/people/enrollments/{id}/group-transfer") { Content = JsonContent.Create(new GroupTransferWrite(p.TargetGroupId, p.EffectiveDate, p.Fingerprint)) }; request.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString()); request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString()); return await owner.SendAsync(request);
    }
    [Fact]
    public async Task Dated_transfer_preserves_history_balance_and_money_and_changes_only_the_effective_roster_and_guardian_schedule()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner); (await Place(owner, f.Group, await ReviewPlacement(owner, f.Group, f.First))).EnsureSuccessStatusCode(); var target = await TransferTarget(owner, f);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var player = await PlayerFor(f.First); var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync(); var e = await db.SportEnrollments.SingleAsync(x => x.Id == f.First); e.GroupAssignedFrom = PlacementToday.AddDays(-10);
        db.PlayerAttendances.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingSessionId = f.Past, TrainingGroupId = f.Group, SportEnrollmentId = f.First, RecordedByUserId = actor, Status = AttendanceStatus.Present }); await db.SaveChangesAsync(); var cash = await db.Collections.CountAsync(); var attendance = await db.PlayerAttendances.CountAsync(); var balances = await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == f.First).Select(p => p.RemainingSessions).ToArrayAsync();
        var p = await TransferReview(owner, f.First, target); Assert.Empty(p.Conflicts); Assert.Single(p.RemovedSessions); Assert.Single(p.AddedSessions); var key = Guid.NewGuid().ToString(); var saved = await TransferSave(owner, f.First, p, key); Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync()); Assert.True((await (await TransferSave(owner, f.First, p, key)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        Assert.Equal(cash, await db.Collections.CountAsync()); Assert.Equal(attendance, await db.PlayerAttendances.CountAsync()); Assert.Equal(balances, await db.SubscriptionPeriods.Where(p2 => p2.SportEnrollmentId == f.First).Select(p2 => p2.RemainingSessions).ToArrayAsync()); Assert.Equal(f.Group, await db.SportEnrollments.Where(x => x.Id == f.First).Select(x => x.TrainingGroupId).SingleAsync());
        Assert.Equal(f.Group, await db.SportEnrollments.Where(x => x.Id == f.First).Select(x => FoundationDbContext.AssignedGroupAt(x.AcademyId, x.Id, PlacementToday)).SingleAsync()); Assert.Equal(target, await db.SportEnrollments.Where(x => x.Id == f.First).Select(x => FoundationDbContext.AssignedGroupAt(x.AcademyId, x.Id, p.EffectiveDate)).SingleAsync());
        Assert.Null(await db.SportEnrollments.Where(x => x.Id == f.First).Select(x => FoundationDbContext.AssignedGroupAt(DemoSeed.FutureAcademyId, x.Id, p.EffectiveDate)).SingleAsync());
        Assert.Single((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions/{f.Past}/players")).GetProperty("players").EnumerateArray()); Assert.Empty((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions/{f.Next}/players")).GetProperty("players").EnumerateArray());
        var targetSession = await db.TrainingSessions.Where(s => s.TrainingGroupId == target).Select(s => s.Id).SingleAsync(); Assert.Equal(f.First, Assert.Single((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions/{targetSession}/players")).GetProperty("players").EnumerateArray()).GetProperty("sportEnrollmentId").GetGuid());
        using var guardian = await Guardian(); var profile = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{player}/profile"); var enrollment = Assert.Single(profile.GetProperty("enrollments").EnumerateArray(), x => x.GetProperty("id").GetGuid() == f.First); Assert.Equal(targetSession, Assert.Single(enrollment.GetProperty("upcoming").EnumerateArray()).GetProperty("id").GetGuid()); Assert.Single(enrollment.GetProperty("transfers").EnumerateArray());
        var row = await db.EnrollmentGroupTransfers.SingleAsync(x => x.SportEnrollmentId == f.First); row.EffectiveDate = row.EffectiveDate.AddDays(1); await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.Entry(row).State = EntityState.Unchanged;
        var err = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"EnrollmentGroupTransfers\" WHERE \"Id\"={row.Id}")); Assert.Equal("23514", err.SqlState);
        e.TrainingGroupId = target; await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task Dated_transfer_last_future_seat_has_one_winner_and_also_blocks_new_placement_today()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner); (await Place(owner, f.Group, await ReviewPlacement(owner, f.Group, f.First, f.Second))).EnsureSuccessStatusCode(); var target = await TransferTarget(owner, f); var a = await TransferReview(owner, f.First, target); var b = await TransferReview(owner, f.Second, target); Assert.Empty(a.Conflicts); Assert.Empty(b.Conflicts);
        var results = await Task.WhenAll(TransferSave(owner, f.First, a), TransferSave(owner, f.Second, b)); Assert.Single(results, r => r.IsSuccessStatusCode); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Equal(1, await db.EnrollmentGroupTransfers.CountAsync(t => t.TargetGroupId == target)); Assert.False(await DatedGroupRoster.CanAdd(db, target, PlacementToday, 1));
        var g = await db.TrainingGroups.AsNoTracking().SingleAsync(g => g.Id == target); Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/manage/structure/groups/{target}", new StructureUpdateRequest(g.ArabicName, null, null, null, null, g.BranchId, g.SportId, g.AgeCategoryId, 0), true)).StatusCode);
    }
    [Fact]
    public async Task Dated_transfer_rejects_past_unauthorized_cross_tenant_CSRF_stale_schedule_and_legacy_bypass()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner); (await Place(owner, f.Group, await ReviewPlacement(owner, f.Group, f.First))).EnsureSuccessStatusCode(); var target = await TransferTarget(owner, f); var body = new GroupTransferWrite(target, PlacementToday.AddDays(1)); var path = $"/api/v1/people/enrollments/{f.First}/group-transfer/preview";
        using var foreign = await Staff("owner.future@example.test"); using var guardian = await Guardian(); using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(foreign, path, body)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Write(guardian, path, body)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Write(coach, path, body)).StatusCode); Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, body)).StatusCode); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, path, body with { EffectiveDate = PlacementToday.AddDays(-1) })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/people/enrollments/{f.First}/group", new MoveGroupWrite(target), true)).StatusCode);
        var p = await TransferReview(owner, f.First, target); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var session = await db.TrainingSessions.SingleAsync(s => s.TrainingGroupId == target); session.StartTime = new(14, 0); session.EndTime = new(15, 0); await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Conflict, (await TransferSave(owner, f.First, p)).StatusCode); Assert.False(await db.EnrollmentGroupTransfers.AnyAsync(x => x.SportEnrollmentId == f.First));
    }
    [Fact]
    public async Task Dated_transfer_preserves_recorded_today_and_rejects_overwriting_a_scheduled_decision()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner); (await Place(owner, f.Group, await ReviewPlacement(owner, f.Group, f.First))).EnsureSuccessStatusCode(); var target = await TransferTarget(owner, f);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var session = await db.TrainingSessions.SingleAsync(s => s.Id == f.Past); session.SessionDate = PlacementToday; var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
        db.PlayerAttendances.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingSessionId = f.Past, TrainingGroupId = f.Group, SportEnrollmentId = f.First, RecordedByUserId = actor, Status = AttendanceStatus.Absent }); await db.SaveChangesAsync();
        var recorded = await TransferReview(owner, f.First, target, PlacementToday); Assert.Contains(recorded.Conflicts, c => c.Contains("سجل حضور")); Assert.Equal(HttpStatusCode.Conflict, (await TransferSave(owner, f.First, recorded)).StatusCode);
        var p = await TransferReview(owner, f.First, target); (await TransferSave(owner, f.First, p)).EnsureSuccessStatusCode();
        var earlier = await TransferReview(owner, f.First, target, PlacementToday); Assert.Contains(earlier.Conflicts, c => c.Contains("نقل مجدول")); Assert.Equal(HttpStatusCode.Conflict, (await TransferSave(owner, f.First, earlier)).StatusCode);
        Assert.Equal(1, await db.EnrollmentGroupTransfers.CountAsync(x => x.SportEnrollmentId == f.First)); Assert.Equal(AttendanceStatus.Absent, await db.PlayerAttendances.Where(a => a.TrainingSessionId == f.Past && a.SportEnrollmentId == f.First).Select(a => a.Status).SingleAsync());
    }
    [Fact]
    public async Task Dated_transfer_blocks_later_calendar_conflicts_and_current_read_models_follow_the_effective_group()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner); (await Place(owner, f.Group, await ReviewPlacement(owner, f.Group, f.First))).EnsureSuccessStatusCode(); var target = await TransferTarget(owner, f);
        var p = await TransferReview(owner, f.First, target, PlacementToday); (await TransferSave(owner, f.First, p)).EnsureSuccessStatusCode(); var player = await PlayerFor(f.First);
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/players/{player}"); Assert.Equal(target, Assert.Single(detail.GetProperty("enrollments").EnumerateArray(), e => e.GetProperty("id").GetGuid() == f.First).GetProperty("trainingGroupId").GetGuid());
        var renewal = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/admin-renewals/enrollments/{f.First}"); Assert.Contains(p.TargetGroup, renewal.ToString());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var group = await db.TrainingGroups.SingleAsync(g => g.Id == f.Group); var other = Guid.NewGuid();
        db.TrainingGroups.Add(new() { Id = other, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "مجموعة رياضة أخرى اصطناعية " + other.ToString("N")[..8], BranchId = f.Branch, SportId = Slice2DemoSeed.FootballId, AgeCategoryId = group.AgeCategoryId, Capacity = 10 });
        db.SportEnrollments.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, PlayerId = player, BranchId = f.Branch, SportId = Slice2DemoSeed.FootballId, TrainingGroupId = other });
        db.TrainingSessions.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingGroupId = other, BranchId = f.Branch, SportId = Slice2DemoSeed.FootballId, AgeCategoryId = group.AgeCategoryId, SessionDate = PlacementToday.AddDays(1), StartTime = new(14, 30), EndTime = new(15, 30), CreatedByUserId = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync() }); await db.SaveChangesAsync();
        var coordinator = scope.ServiceProvider.GetRequiredService<Academy.Api.Facilities.ResourceSessionCoordinator>(); await using var tx = await db.Database.BeginTransactionAsync(); await Assert.ThrowsAsync<Academy.Api.Facilities.FacilityRejected>(() => coordinator.CheckRosterSlot(DemoSeed.NogoomAcademyId, target, PlacementToday.AddDays(1), new(14, 0), new(15, 0)));
    }
}
