using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    private sealed record PlacementFixture(Guid Branch, Guid OtherBranch, Guid Group, Guid First, Guid Second, Guid Level, Guid Coach, Guid Past, Guid Next);
    private static DateOnly PlacementToday => FacilityDay.AddDays(-20);
    private async Task<PlacementFixture> PlacementReady(HttpClient owner, int capacity = 2, bool secondPaid = true)
    {
        var f = await Fixture(); var planResponse = await Write(owner, "/api/v1/subscriptions/plans", Body(f.A, "استحقاق تسكين اصطناعي " + Guid.NewGuid().ToString("N")[..8], type: SubscriptionPlanType.Duration) with { DurationDays = 365 }); planResponse.EnsureSuccessStatusCode(); var plan = (await planResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var coach = await ScheduleCoach(owner, f.Group); var player = Guid.NewGuid(); var second = Guid.NewGuid(); var past = Guid.NewGuid(); var next = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var group = await db.TrainingGroups.SingleAsync(x => x.Id == f.Group); group.Capacity = capacity;
            (await db.SportEnrollments.SingleAsync(x => x.Id == f.Enrollment)).TrainingGroupId = null;
            db.Players.Add(new() { Id = player, AcademyId = DemoSeed.NogoomAcademyId, PlayerCode = "PLACE-" + player.ToString("N")[..10], ArabicName = "لاعب تسكين ثانٍ اصطناعي", DateOfBirth = new(f.BirthYear, 1, 2) });
            db.SportEnrollments.Add(new() { Id = second, AcademyId = DemoSeed.NogoomAcademyId, PlayerId = player, BranchId = f.A, SportId = Slice2DemoSeed.SwimmingId });
            foreach (var pair in new[] { (past, PlacementToday.AddDays(-1)), (next, PlacementToday.AddDays(1)) }) db.TrainingSessions.Add(new() { Id = pair.Item1, AcademyId = DemoSeed.NogoomAcademyId, TrainingGroupId = f.Group, BranchId = f.A, SportId = Slice2DemoSeed.SwimmingId, AgeCategoryId = group.AgeCategoryId, SessionDate = pair.Item2, StartTime = new(10, 0), EndTime = new(11, 0), Source = TrainingSessionSource.Manual, CreatedByUserId = f.Owner });
            await db.SaveChangesAsync(); await using var tx = await db.Database.BeginTransactionAsync();
            var collections = scope.ServiceProvider.GetRequiredService<CollectionService>(); await collections.CreateCashAsync(DemoSeed.NogoomAcademyId, f.Owner, f.Enrollment, plan, "placement-paid-" + Guid.NewGuid(), "Cash");
            if (secondPaid) await collections.CreateCashAsync(DemoSeed.NogoomAcademyId, f.Owner, second, plan, "placement-paid-" + Guid.NewGuid(), "Cash"); await tx.CommitAsync();
        }
        var levelResponse = await Write(owner, "/api/v1/manage/training-levels", new TrainingLevelWrite(Slice2DemoSeed.SwimmingId, "مستوى اختبار " + Guid.NewGuid().ToString("N")[..8], 1)); levelResponse.EnsureSuccessStatusCode(); var level = (await levelResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        foreach (var e in new[] { f.Enrollment, second }) (await Write(owner, $"/api/v1/people/enrollments/{e}/placement-level", new PlacementLevelWrite(level), true)).EnsureSuccessStatusCode();
        (await Write(owner, $"/api/v1/manage/groups/{f.Group}/placement-level", new PlacementLevelWrite(level), true)).EnsureSuccessStatusCode();
        return new(f.A, f.B, f.Group, f.Enrollment, second, level, coach, past, next);
    }
    private static async Task<PlacementPreview> ReviewPlacement(HttpClient owner, Guid group, params Guid[] ids)
    {
        var r = await Write(owner, $"/api/v1/manage/groups/{group}/placement/preview", new PlacementWrite(ids)); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<PlacementPreview>())!;
    }
    private static async Task<HttpResponseMessage> Place(HttpClient owner, Guid group, PlacementPreview p, string? key = null)
    {
        var csrf = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/manage/groups/{group}/placement") { Content = JsonContent.Create(new PlacementWrite(p.Players.Select(x => x.Id).ToArray(), p.Fingerprint)) }; req.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); req.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString()); return await owner.SendAsync(req);
    }
    [Fact]
    public async Task Group_placement_demo_seed_is_idempotent_preserves_edits_and_keeps_unpaid_child_waiting()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var service = scope.ServiceProvider.GetRequiredService<CollectionService>(); await GroupPlacementDemoSeed.Seed(db, service);
        var group = await db.TrainingGroups.SingleAsync(x => x.Id == GroupPlacementDemoSeed.Id(1)); group.ArabicName = "تسكين تجريبي — تعديل محفوظ"; await db.SaveChangesAsync(); var cash = await db.Collections.CountAsync(); var people = await db.Players.CountAsync(); await GroupPlacementDemoSeed.Seed(db, service);
        Assert.Equal(cash, await db.Collections.CountAsync()); Assert.Equal(people, await db.Players.CountAsync()); await db.Entry(group).ReloadAsync(); Assert.Equal("تسكين تجريبي — تعديل محفوظ", group.ArabicName);
        Assert.Equal(2, await db.SubscriptionPeriods.CountAsync(x => x.SubscriptionPlanId == GroupPlacementDemoSeed.Id(5))); Assert.False(await db.SubscriptionPeriods.AnyAsync(x => x.SportEnrollmentId == GroupPlacementDemoSeed.Id(31))); Assert.False(await db.SportEnrollments.Where(x => x.Id == GroupPlacementDemoSeed.Id(31)).AnyAsync(x => x.TrainingGroupId != null));
        Assert.Equal(new DateOnly(2017, 1, 1), await db.Players.Where(x => x.Id == GroupPlacementDemoSeed.Id(10)).Select(x => x.DateOfBirth).SingleAsync());
        await GroupTransferDemoSeed.Seed(db);
        var target = await db.TrainingGroups.SingleAsync(x => x.Id == GroupPlacementDemoSeed.Id(50)); target.ArabicName = "موعد النقل — تعديل محفوظ"; await db.SaveChangesAsync();
        var sessions = await db.TrainingSessions.CountAsync(); await GroupTransferDemoSeed.Seed(db); await db.Entry(target).ReloadAsync();
        Assert.Equal("موعد النقل — تعديل محفوظ", target.ArabicName); Assert.Equal(sessions, await db.TrainingSessions.CountAsync()); Assert.Equal(cash, await db.Collections.CountAsync());
        Assert.False(await db.EnrollmentGroupTransfers.AnyAsync(x => x.TargetGroupId == target.Id));
    }
    [Fact]
    public async Task Bulk_placement_is_reviewed_atomic_idempotent_and_creates_neither_payment_nor_past_attendance()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var cash = await db.Collections.CountAsync(); var att = await db.PlayerAttendances.CountAsync();
        var candidates = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/groups/{f.Group}/placement-candidates"); Assert.Equal(2, candidates.GetProperty("total").GetInt32()); Assert.All(candidates.GetProperty("players").EnumerateArray(), x => Assert.Equal(0, x.GetProperty("reasons").GetArrayLength()));
        var p = await ReviewPlacement(owner, f.Group, f.First, f.Second); Assert.Empty(p.Conflicts); Assert.All(p.Players, x => Assert.Empty(x.Reasons)); Assert.False(await db.SportEnrollments.AnyAsync(x => x.TrainingGroupId == f.Group));
        var key = Guid.NewGuid().ToString(); var saved = await Place(owner, f.Group, p, key); Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync()); Assert.Equal(2, (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("placed").GetInt32());
        Assert.True((await (await Place(owner, f.Group, p, key)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean()); Assert.Equal(HttpStatusCode.Conflict, (await Place(owner, f.Group, p with { Players = [p.Players[0]] }, key)).StatusCode);
        Assert.Equal(2, await db.SportEnrollments.CountAsync(x => x.TrainingGroupId == f.Group && x.GroupAssignedFrom == PlacementToday)); Assert.Equal(cash, await db.Collections.CountAsync()); Assert.Equal(att, await db.PlayerAttendances.CountAsync());
        var past = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions/{f.Past}/players"); Assert.Empty(past.GetProperty("players").EnumerateArray()); var next = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions/{f.Next}/players"); Assert.Equal(2, next.GetProperty("players").GetArrayLength());
        var revision = await db.GroupPlacementRevisions.SingleAsync(x => x.TrainingGroupId == f.Group); revision.Placed = 99; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.Entry(revision).State = EntityState.Unchanged;
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"GroupPlacementRevisions\" WHERE \"Id\"={revision.Id}")); Assert.Equal("23514", error.SqlState);
    }
    [Fact]
    public async Task Bulk_placement_last_seat_has_one_winner_and_candidates_are_not_blocked_by_page_size()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner, 1); var candidates = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/groups/{f.Group}/placement-candidates"); Assert.All(candidates.GetProperty("players").EnumerateArray(), x => Assert.Equal(0, x.GetProperty("reasons").GetArrayLength())); Assert.Empty(candidates.GetProperty("conflicts").EnumerateArray());
        var a = await ReviewPlacement(owner, f.Group, f.First); var b = await ReviewPlacement(owner, f.Group, f.Second); var results = await Task.WhenAll(Place(owner, f.Group, a), Place(owner, f.Group, b)); Assert.Single(results, x => x.IsSuccessStatusCode); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Equal(1, await db.SportEnrollments.CountAsync(x => x.TrainingGroupId == f.Group)); Assert.Equal(1, await db.GroupPlacementRevisions.CountAsync(x => x.TrainingGroupId == f.Group));
    }
    [Fact]
    public async Task Bulk_placement_one_unpaid_or_wrong_level_rejects_whole_batch_and_stale_preview_cannot_save()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner, secondPaid: false); var p = await ReviewPlacement(owner, f.Group, f.First, f.Second); Assert.Contains(p.Players.Single(x => x.Id == f.Second).Reasons, x => x.Contains("استحقاق")); Assert.Equal(HttpStatusCode.Conflict, (await Place(owner, f.Group, p)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.False(await db.SportEnrollments.AnyAsync(x => x.TrainingGroupId == f.Group));
        p = await ReviewPlacement(owner, f.Group, f.First); Assert.Empty(p.Players[0].Reasons); (await Write(owner, $"/api/v1/people/enrollments/{f.First}/placement-level", new PlacementLevelWrite(null), true)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Conflict, (await Place(owner, f.Group, p)).StatusCode); p = await ReviewPlacement(owner, f.Group, f.First); Assert.Contains(p.Players[0].Reasons, x => x.Contains("مستوى")); Assert.Equal(HttpStatusCode.Conflict, (await Place(owner, f.Group, p)).StatusCode); Assert.False(await db.GroupPlacementRevisions.AnyAsync(x => x.TrainingGroupId == f.Group));
    }
    [Fact]
    public async Task Bulk_placement_rechecks_cancelled_entitlement_and_inactive_coach_without_partial_save()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner); var p = await ReviewPlacement(owner, f.Group, f.First); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var period = await db.SubscriptionPeriods.SingleAsync(x => x.SportEnrollmentId == f.First); period.Status = SubscriptionPeriodStatus.Cancelled; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Conflict, (await Place(owner, f.Group, p)).StatusCode);
        p = await ReviewPlacement(owner, f.Group, f.Second); var coach = await db.AcademyMemberships.SingleAsync(x => x.Id == f.Coach); coach.IsActive = false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Conflict, (await Place(owner, f.Group, p)).StatusCode); p = await ReviewPlacement(owner, f.Group, f.Second); Assert.Contains(p.Conflicts, x => x.Contains("مدرب")); Assert.False(await db.SportEnrollments.AnyAsync(x => x.TrainingGroupId == f.Group));
    }
    [Fact]
    public async Task Bulk_placement_hard_age_sport_branch_and_level_keys_are_tenant_scoped()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner); var other = await Fixture(); var p = await ReviewPlacement(owner, f.Group, other.Enrollment); Assert.Contains(p.Players[0].Reasons, x => x.Contains("الفرع")); Assert.Contains(p.Players[0].Reasons, x => x.Contains("مسكن"));
        var football = await Write(owner, "/api/v1/manage/training-levels", new TrainingLevelWrite(Slice2DemoSeed.FootballId, "مستوى كرة " + Guid.NewGuid().ToString("N")[..8], 1)); football.EnsureSuccessStatusCode(); var level = (await football.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, $"/api/v1/people/enrollments/{f.First}/placement-level", new PlacementLevelWrite(level), true)).StatusCode); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, $"/api/v1/manage/groups/{f.Group}/placement-level", new PlacementLevelWrite(level), true)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var player = await db.Players.SingleAsync(x => x.Id == db.SportEnrollments.Where(e => e.Id == f.First).Select(e => e.PlayerId).Single()); player.DateOfBirth = PlacementToday.AddYears(-40); await db.SaveChangesAsync(); p = await ReviewPlacement(owner, f.Group, f.First); Assert.Contains(p.Players[0].Reasons, x => x.Contains("العمرية")); Assert.Equal(HttpStatusCode.Conflict, (await Place(owner, f.Group, p)).StatusCode);
    }
    [Fact]
    public async Task Bulk_placement_checks_both_edit_grants_branch_tenant_CSRF_and_live_feature_revocation()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner); using var foreign = await Staff("owner.future@example.test"); using var guardian = await Guardian(); var path = $"/api/v1/manage/groups/{f.Group}/placement/preview"; Assert.Equal(HttpStatusCode.NotFound, (await Write(foreign, path, new PlacementWrite([f.First]))).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Write(guardian, path, new PlacementWrite([f.First]))).StatusCode); Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, new PlacementWrite([f.First]))).StatusCode);
        var email = $"placement-{Guid.NewGuid():N}@example.test"; var created = await Write(owner, "/api/v1/manage/staff", new StaffWrite("إداري تسكين اصطناعي", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, null)); created.EnsureSuccessStatusCode(); var member = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); var access = new StaffAccessWrite(false, [f.OtherBranch], StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m is "academy" or "players", false, false, false)).ToArray()); (await Write(owner, $"/api/v1/manage/staff/{member}/access", access, true)).EnsureSuccessStatusCode(); using var admin = await Staff(email); Assert.Equal(HttpStatusCode.NotFound, (await Write(admin, path, new PlacementWrite([f.First]))).StatusCode);
        (await Write(owner, $"/api/v1/manage/staff/{member}/access", access with { Branches = [f.Branch] }, true)).EnsureSuccessStatusCode(); var preview = await ReviewPlacement(admin, f.Group, f.First); Assert.Equal(HttpStatusCode.Forbidden, (await Place(admin, f.Group, preview)).StatusCode);
        var p = await ReviewPlacement(owner, f.Group, f.First); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var license = await db.AcademyFeatureEntitlements.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Feature == "training"); var old = license.Enabled; try { license.Enabled = false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Forbidden, (await Place(owner, f.Group, p)).StatusCode); } finally { license.Enabled = old; await db.SaveChangesAsync(); }
    }
    [Fact]
    public async Task Bulk_placement_uses_reserved_lane_seats_not_only_group_capacity()
    {
        await using var license = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await PlacementReady(owner, 4); var resource = await FacilityFixture(owner, branch: f.Branch); var body = new TimetableWrite(PlacementToday.AddDays(2), PlacementToday.AddDays(2), [PlacementToday.AddDays(2).DayOfWeek], new(10, 0), new(11, 0), new(resource, f.Coach, 1, false)); body = await Reviewed(owner, f.Group, body); (await SaveTimetable(owner, f.Group, body, Guid.NewGuid().ToString())).EnsureSuccessStatusCode(); var p = await ReviewPlacement(owner, f.Group, f.First, f.Second); Assert.Contains(p.Conflicts, x => x.Contains("الحارة")); Assert.Equal(HttpStatusCode.Conflict, (await Place(owner, f.Group, p)).StatusCode);
    }
    [Fact]
    public async Task Bulk_placement_player_other_sport_overlap_blocks_assignment_and_capacity_shrink_is_rejected()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner); var p = await ReviewPlacement(owner, f.Group, f.First); (await Place(owner, f.Group, p)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/people/enrollments/{f.First}/placement-level", new PlacementLevelWrite(null), true)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var other = Guid.NewGuid(); var group = await db.TrainingGroups.SingleAsync(x => x.Id == f.Group); db.TrainingGroups.Add(new() { Id = other, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "تعارض كرة اصطناعي " + other.ToString("N")[..8], BranchId = f.Branch, SportId = Slice2DemoSeed.FootballId, AgeCategoryId = group.AgeCategoryId, Capacity = 10 }); var secondPlayer = await PlayerFor(f.Second); db.SportEnrollments.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, PlayerId = secondPlayer, BranchId = f.Branch, SportId = Slice2DemoSeed.FootballId, TrainingGroupId = other }); db.TrainingSessions.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingGroupId = other, BranchId = f.Branch, SportId = Slice2DemoSeed.FootballId, AgeCategoryId = group.AgeCategoryId, SessionDate = PlacementToday.AddDays(1), StartTime = new(10, 30), EndTime = new(11, 30), CreatedByUserId = await db.Users.Where(x => x.Email == DemoSeed.OwnerEmail).Select(x => x.Id).SingleAsync() }); await db.SaveChangesAsync(); p = await ReviewPlacement(owner, f.Group, f.Second); Assert.Contains(p.Players[0].Reasons, x => x.Contains("يتعارض")); Assert.Equal(HttpStatusCode.Conflict, (await Place(owner, f.Group, p)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/manage/structure/groups/{f.Group}", new StructureUpdateRequest(group.ArabicName, null, null, null, null, group.BranchId, group.SportId, group.AgeCategoryId, 0), true)).StatusCode);
    }
}
