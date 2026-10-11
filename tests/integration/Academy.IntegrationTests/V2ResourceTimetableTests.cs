using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Facilities;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    private async Task<Guid> ScheduleCoach(HttpClient owner, Guid group)
    {
        var r = await Write(owner, "/api/v1/manage/staff", new StaffWrite("مدرب جدول اصطناعي", $"schedule-{Guid.NewGuid():N}@example.test", "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.Coach, null)); r.EnsureSuccessStatusCode(); var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); db.StaffGroupAssignments.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingGroupId = group, AcademyMembershipId = id }); await db.SaveChangesAsync(); return id;
    }
    private static TimetableWrite ScheduleBody(Guid resource, Guid coach) => new(FacilityDay, FacilityDay.AddDays(1), [FacilityDay.DayOfWeek, FacilityDay.AddDays(1).DayOfWeek], new(17, 0), new(18, 0), new(resource, coach, 4, false));
    private static async Task<HttpResponseMessage> SaveTimetable(HttpClient client, Guid group, TimetableWrite b, string key)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/manage/groups/{group}/timetable") { Content = JsonContent.Create(b) }; req.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); req.Headers.Add("Idempotency-Key", key); return await client.SendAsync(req);
    }
    private async Task<TimetableWrite> Reviewed(HttpClient owner, Guid group, TimetableWrite b, bool expectClear = true)
    {
        var r = await Write(owner, $"/api/v1/manage/groups/{group}/timetable/preview", b); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); var result = (await r.Content.ReadFromJsonAsync<TimetablePreview>())!; if (expectClear) Assert.Empty(result.Conflicts); return b with { PreviewFingerprint = result.Fingerprint };
    }
    [Fact]
    public async Task Resource_timetable_preview_is_read_only_then_session_and_booking_commit_together_with_safe_retry()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await Fixture(); var resource = await FacilityFixture(owner, branch: f.A); var coach = await ScheduleCoach(owner, f.Group); var body = ScheduleBody(resource, coach);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var cash = await db.Collections.CountAsync(); var attendance = await db.PlayerAttendances.CountAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await SaveTimetable(owner, f.Group, body, Guid.NewGuid().ToString())).StatusCode);
        body = await Reviewed(owner, f.Group, body); Assert.False(await db.TrainingSessions.AnyAsync(x => x.TrainingGroupId == f.Group)); Assert.False(await db.GroupResourceSchedules.AnyAsync(x => x.TrainingGroupId == f.Group));
        var key = Guid.NewGuid().ToString(); var saved = await SaveTimetable(owner, f.Group, body, key); Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync()); Assert.Equal(2, (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetInt32());
        var replay = await SaveTimetable(owner, f.Group, body, key); replay.EnsureSuccessStatusCode(); Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        Assert.Equal(2, await db.TrainingSessions.CountAsync(x => x.TrainingGroupId == f.Group)); Assert.Equal(2, await db.ResourceBookings.CountAsync(x => x.TrainingGroupId == f.Group && x.State == ResourceBookingState.Reserved)); Assert.Single(await db.TrainingScheduleRevisions.Where(x => x.TrainingGroupId == f.Group).ToListAsync());
        Assert.Equal(cash, await db.Collections.CountAsync()); Assert.Equal(attendance, await db.PlayerAttendances.CountAsync());
        var details = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/groups/{f.Group}/details"); Assert.Equal(resource, details.GetProperty("resourcePlan").GetProperty("resourceId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await SaveTimetable(owner, f.Group, body with { EndTime = new(19, 0) }, key)).StatusCode);
    }
    [Fact]
    public async Task One_conflicting_day_rejects_the_entire_schedule_without_partial_sessions_or_reservations()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await Fixture(); var resource = await FacilityFixture(owner, branch: f.A); var coach = await ScheduleCoach(owner, f.Group); var body = ScheduleBody(resource, coach);
        (await BookFacility(owner, FacilityBody(resource, 8) with { Date = FacilityDay.AddDays(1) }, Guid.NewGuid().ToString())).EnsureSuccessStatusCode(); body = await Reviewed(owner, f.Group, body, false);
        Assert.Equal(HttpStatusCode.Conflict, (await SaveTimetable(owner, f.Group, body, Guid.NewGuid().ToString())).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.False(await db.TrainingSessions.AnyAsync(x => x.TrainingGroupId == f.Group)); Assert.False(await db.RecurringSchedules.AnyAsync(x => x.TrainingGroupId == f.Group)); Assert.False(await db.GroupResourceSchedules.AnyAsync(x => x.TrainingGroupId == f.Group)); Assert.False(await db.TrainingScheduleRevisions.AnyAsync(x => x.TrainingGroupId == f.Group));
    }
    [Fact]
    public async Task Stale_roster_preview_is_rejected_and_live_feature_revocation_cannot_publish_a_schedule()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await Fixture(); var resource = await FacilityFixture(owner, branch: f.A); var coach = await ScheduleCoach(owner, f.Group); var body = await Reviewed(owner, f.Group, ScheduleBody(resource, coach));
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); (await db.SportEnrollments.SingleAsync(x => x.Id == f.Enrollment)).IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await SaveTimetable(owner, f.Group, body, Guid.NewGuid().ToString())).StatusCode);
        body = await Reviewed(owner, f.Group, body); var license = await db.AcademyFeatureEntitlements.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Feature == "facilities"); license.Enabled = false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Forbidden, (await SaveTimetable(owner, f.Group, body, Guid.NewGuid().ToString())).StatusCode); Assert.False(await db.TrainingSessions.AnyAsync(x => x.TrainingGroupId == f.Group));
    }
    [Fact]
    public async Task Future_replacement_cancels_old_bookings_but_preserves_past_attendance_and_immutable_revision()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await Fixture(); var resource = await FacilityFixture(owner, branch: f.A); var coach = await ScheduleCoach(owner, f.Group); var body = await Reviewed(owner, f.Group, ScheduleBody(resource, coach)); (await SaveTimetable(owner, f.Group, body, Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var previous = await db.ResourceBookings.Where(x => x.TrainingGroupId == f.Group).Select(x => x.Id).ToArrayAsync();
        var history = new TrainingSession { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingGroupId = f.Group, BranchId = f.A, SportId = Slice2DemoSeed.SwimmingId, AgeCategoryId = await db.TrainingGroups.Where(x => x.Id == f.Group).Select(x => x.AgeCategoryId).SingleAsync(), SessionDate = FacilityDay.AddDays(-21), StartTime = new(17, 0), EndTime = new(18, 0), Source = TrainingSessionSource.Manual, Status = TrainingSessionStatus.Held, CreatedByUserId = f.Owner }; db.TrainingSessions.Add(history); db.PlayerAttendances.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingSessionId = history.Id, TrainingGroupId = f.Group, SportEnrollmentId = f.Enrollment, Status = AttendanceStatus.Absent, RecordedByUserId = f.Owner }); await db.SaveChangesAsync();
        body = await Reviewed(owner, f.Group, body with { StartTime = new(19, 0), EndTime = new(20, 0), Resource = null }); (await SaveTimetable(owner, f.Group, body, Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        Assert.Equal(2, await db.ResourceBookings.CountAsync(x => previous.Contains(x.Id) && x.State == ResourceBookingState.Cancelled)); Assert.Equal(2, await db.ResourceBookings.CountAsync(x => x.TrainingGroupId == f.Group && x.State == ResourceBookingState.Reserved && x.StartTime == new TimeOnly(19, 0))); await db.Entry(history).ReloadAsync(); Assert.Equal(TrainingSessionStatus.Held, history.Status); Assert.Equal(new TimeOnly(17, 0), history.StartTime); Assert.Equal(AttendanceStatus.Absent, await db.PlayerAttendances.Where(x => x.TrainingSessionId == history.Id).Select(x => x.Status).SingleAsync());
        var revision = await db.TrainingScheduleRevisions.FirstAsync(x => x.TrainingGroupId == f.Group); revision.Snapshot = "{}"; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.Entry(revision).State = EntityState.Unchanged;
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"TrainingScheduleRevisions\" SET \"Snapshot\" = '{{}}'::jsonb WHERE \"Id\" = {revision.Id}"));
    }
    [Fact]
    public async Task Manual_session_and_status_changes_cannot_bypass_group_resource_reservations()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await Fixture(); var resource = await FacilityFixture(owner, branch: f.A); var coach = await ScheduleCoach(owner, f.Group); var body = await Reviewed(owner, f.Group, ScheduleBody(resource, coach)); (await SaveTimetable(owner, f.Group, body, Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        var manual = new ManualSessionRequest(f.Group, FacilityDay, new(18, 0), new(19, 0), null); var created = await Write(owner, "/api/v1/attendance/sessions", manual); Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync()); var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.True(await db.ResourceBookings.AnyAsync(x => x.TrainingSessionId == id && x.State == ResourceBookingState.Reserved));
        Assert.Equal(HttpStatusCode.BadRequest, (await Write(owner, $"/api/v1/attendance/sessions/{id}/status", new SessionStatusRequest("999"), true)).StatusCode);
        (await Write(owner, $"/api/v1/attendance/sessions/{id}/status", new SessionStatusRequest("Cancelled"), true)).EnsureSuccessStatusCode(); Assert.False(await db.ResourceBookings.AnyAsync(x => x.TrainingSessionId == id && x.State == ResourceBookingState.Reserved));
        (await BookFacility(owner, FacilityBody(resource, 8, 18, 19), Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/attendance/sessions/{id}/status", new SessionStatusRequest("Scheduled"), true)).StatusCode); Assert.Equal(TrainingSessionStatus.Cancelled, await db.TrainingSessions.Where(x => x.Id == id).Select(x => x.Status).SingleAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, "/api/v1/attendance/sessions", manual with { StartTime = new(18, 30), EndTime = new(19, 0) })).StatusCode); Assert.False(await db.TrainingSessions.AnyAsync(x => x.TrainingGroupId == f.Group && x.StartTime == new TimeOnly(18, 30)));
    }
    [Fact]
    public async Task Generated_sessions_reserve_the_lane_atomically_and_conflict_leaves_no_orphan_session()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await Fixture(); var resource = await FacilityFixture(owner, branch: f.A); var coach = await ScheduleCoach(owner, f.Group);
        var body = await Reviewed(owner, f.Group, ScheduleBody(resource, coach)); (await SaveTimetable(owner, f.Group, body, Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); db.RecurringSchedules.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingGroupId = f.Group, ValidFrom = FacilityDay, ValidTo = FacilityDay, DayOfWeek = FacilityDay.DayOfWeek, StartTime = new(18, 0), EndTime = new(19, 0) }); await db.SaveChangesAsync();
        var email = $"generator-admin-{Guid.NewGuid():N}@example.test"; var response = await Write(owner, "/api/v1/manage/staff", new StaffWrite("إداري توليد صناعي", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, null)); response.EnsureSuccessStatusCode(); var membership = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var actions = StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m is "facilities" or "academy" or "attendance", m is "facilities" or "academy" or "attendance", false, false)).ToArray(); (await Write(owner, $"/api/v1/manage/staff/{membership}/access", new StaffAccessWrite(false, [f.A], actions), true)).EnsureSuccessStatusCode(); using var admin = await Staff(email);
        var generated = await Write(admin, "/api/v1/attendance/sessions/generate", new GenerateSessionsRequest(FacilityDay, FacilityDay)); Assert.True(generated.IsSuccessStatusCode, await generated.Content.ReadAsStringAsync()); var session = await db.TrainingSessions.SingleAsync(x => x.TrainingGroupId == f.Group && x.StartTime == new TimeOnly(18, 0)); Assert.True(await db.ResourceBookings.AnyAsync(x => x.TrainingSessionId == session.Id && x.State == ResourceBookingState.Reserved));
        Assert.Equal(0, (await (await Write(admin, "/api/v1/attendance/sessions/generate", new GenerateSessionsRequest(FacilityDay, FacilityDay))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetInt32());
        db.RecurringSchedules.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingGroupId = f.Group, ValidFrom = FacilityDay, ValidTo = FacilityDay, DayOfWeek = FacilityDay.DayOfWeek, StartTime = new(20, 0), EndTime = new(21, 0) }); await db.SaveChangesAsync(); (await BookFacility(owner, FacilityBody(resource, 8, 20, 21), Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Write(admin, "/api/v1/attendance/sessions/generate", new GenerateSessionsRequest(FacilityDay, FacilityDay))).StatusCode); Assert.False(await db.TrainingSessions.AnyAsync(x => x.TrainingGroupId == f.Group && x.StartTime == new TimeOnly(20, 0)));
    }
    [Fact]
    public async Task Resource_schedule_preview_requires_role_tenant_and_csrf_and_explicit_removal_keeps_history()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await Fixture(); var resource = await FacilityFixture(owner, branch: f.A); var coach = await ScheduleCoach(owner, f.Group); var b = ScheduleBody(resource, coach); var path = $"/api/v1/manage/groups/{f.Group}/timetable/preview";
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, b)).StatusCode); using var guardian = await Guardian(); Assert.Equal(HttpStatusCode.Forbidden, (await Write(guardian, path, b)).StatusCode); using var foreign = await Staff("owner.future@example.test"); Assert.Equal(HttpStatusCode.NotFound, (await Write(foreign, path, b)).StatusCode);
        b = await Reviewed(owner, f.Group, b); (await SaveTimetable(owner, f.Group, b, Guid.NewGuid().ToString())).EnsureSuccessStatusCode(); b = await Reviewed(owner, f.Group, b with { Resource = null, RemoveResource = true }); (await SaveTimetable(owner, f.Group, b, Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.False(await db.GroupResourceSchedules.AnyAsync(x => x.TrainingGroupId == f.Group && x.IsActive)); Assert.False(await db.ResourceBookings.AnyAsync(x => x.TrainingGroupId == f.Group && x.State == ResourceBookingState.Reserved)); Assert.Equal(2, await db.TrainingScheduleRevisions.CountAsync(x => x.TrainingGroupId == f.Group)); Assert.Equal(2, await db.TrainingSessions.CountAsync(x => x.TrainingGroupId == f.Group && x.Status == TrainingSessionStatus.Scheduled));
    }
    [Fact]
    public async Task Competing_group_timetables_cannot_overbook_and_no_matching_weekday_is_rejected()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var first = await Fixture(); var secondGroup = Guid.NewGuid(); var resource = await FacilityFixture(owner, branch: first.A);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        db.TrainingGroups.Add(new() { Id = secondGroup, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "مجموعة تنافس اصطناعية " + secondGroup.ToString("N")[..8], BranchId = first.A, SportId = Slice2DemoSeed.SwimmingId, AgeCategoryId = await db.TrainingGroups.Where(x => x.Id == first.Group).Select(x => x.AgeCategoryId).SingleAsync(), Capacity = 100 }); await db.SaveChangesAsync();
        var c1 = await ScheduleCoach(owner, first.Group); var c2 = await ScheduleCoach(owner, secondGroup);
        var b1 = ScheduleBody(resource, c1) with { Resource = new(resource, c1, 5, false) }; var b2 = ScheduleBody(resource, c2) with { Resource = new(resource, c2, 5, false) };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, $"/api/v1/manage/groups/{first.Group}/timetable/preview", b1 with { To = b1.From, Days = [b1.From.AddDays(1).DayOfWeek] })).StatusCode);
        b1 = await Reviewed(owner, first.Group, b1); b2 = await Reviewed(owner, secondGroup, b2);
        var results = await Task.WhenAll(SaveTimetable(owner, first.Group, b1, Guid.NewGuid().ToString()), SaveTimetable(owner, secondGroup, b2, Guid.NewGuid().ToString()));
        Assert.Single(results, r => r.IsSuccessStatusCode); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(2, await db.TrainingSessions.CountAsync(x => x.TrainingGroupId == first.Group || x.TrainingGroupId == secondGroup)); Assert.Equal(2, await db.ResourceBookings.CountAsync(x => x.ResourceId == resource && x.State == ResourceBookingState.Reserved)); Assert.Single(await db.TrainingScheduleRevisions.Where(x => x.TrainingGroupId == first.Group || x.TrainingGroupId == secondGroup).ToArrayAsync());
    }
    [Fact]
    public async Task Own_guardian_and_assigned_coach_see_the_lane_without_resource_roster_or_cancellation_bypass()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await Fixture(); var resource = await FacilityFixture(owner, branch: f.A); var coach = await ScheduleCoach(owner, f.Group);
        var body = await Reviewed(owner, f.Group, ScheduleBody(resource, coach) with { StartTime = new(12, 0), EndTime = new(13, 0) }); (await SaveTimetable(owner, f.Group, body, Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var player = await db.SportEnrollments.Where(x => x.Id == f.Enrollment).Select(x => x.PlayerId).SingleAsync();
        using var guardian = await Guardian(); var home = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/guardian/home"); var child = home.GetProperty("children").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == player); Assert.Contains("مسبح اختبار · A", child.GetProperty("sports")[0].GetProperty("nextSession").GetProperty("resource").GetString());
        var defaultCoach = await db.AcademyMemberships.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.User.Email == DemoSeed.CoachEmail).Select(x => x.Id).SingleAsync(); var assignment = new Academy.Infrastructure.Structure.StaffGroupAssignment { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingGroupId = f.Group, AcademyMembershipId = defaultCoach }; db.StaffGroupAssignments.Add(assignment); await db.SaveChangesAsync();
        using var coachSession = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coachSession, factory.Services);
        try { var sessions = await coachSession.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions?from={FacilityDay:yyyy-MM-dd}&to={FacilityDay:yyyy-MM-dd}"); Assert.Equal("مسبح اختبار · A", sessions.EnumerateArray().Single(x => x.GetProperty("trainingGroupId").GetGuid() == f.Group).GetProperty("resource").GetString()); Assert.Equal(HttpStatusCode.Forbidden, (await coachSession.GetAsync("/api/v1/facilities")).StatusCode); }
        finally { assignment.IsActive = false; await db.SaveChangesAsync(); }
        var booking = await db.ResourceBookings.FirstAsync(x => x.TrainingGroupId == f.Group && x.State == ResourceBookingState.Reserved); Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/facilities/bookings/{booking.Id}/cancel", new { })).StatusCode);
    }
    [Fact]
    public async Task Recorded_same_day_session_and_its_booking_survive_a_future_schedule_replacement()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await Fixture(); var resource = await FacilityFixture(owner, branch: f.A); var coach = await ScheduleCoach(owner, f.Group); var today = FacilityDay.AddDays(-20);
        var body = new TimetableWrite(today, today.AddDays(1), [today.DayOfWeek, today.AddDays(1).DayOfWeek], new(9, 0), new(10, 0), new(resource, coach, 4, false)); body = await Reviewed(owner, f.Group, body); (await SaveTimetable(owner, f.Group, body, Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var held = await db.TrainingSessions.SingleAsync(x => x.TrainingGroupId == f.Group && x.SessionDate == today); held.Status = TrainingSessionStatus.Held; db.PlayerAttendances.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingSessionId = held.Id, TrainingGroupId = f.Group, SportEnrollmentId = f.Enrollment, Status = AttendanceStatus.Present, RecordedByUserId = f.Owner }); await db.SaveChangesAsync(); var booking = await db.ResourceBookings.SingleAsync(x => x.TrainingSessionId == held.Id);
        body = await Reviewed(owner, f.Group, body with { StartTime = new(11, 0), EndTime = new(12, 0), Resource = null }); var result = await SaveTimetable(owner, f.Group, body, Guid.NewGuid().ToString()); result.EnsureSuccessStatusCode(); Assert.Equal(1, (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("preserved").GetInt32());
        await db.Entry(held).ReloadAsync(); await db.Entry(booking).ReloadAsync(); Assert.Equal(TrainingSessionStatus.Held, held.Status); Assert.Equal(new TimeOnly(9, 0), held.StartTime); Assert.Equal(ResourceBookingState.Reserved, booking.State); Assert.Equal(AttendanceStatus.Present, await db.PlayerAttendances.Where(x => x.TrainingSessionId == held.Id).Select(x => x.Status).SingleAsync());
    }
    [Fact]
    public async Task Branch_admin_cannot_double_book_a_coach_in_a_hidden_branch_or_learn_its_roster()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await Fixture(); var resource = await FacilityFixture(owner, branch: f.A); var coach = await ScheduleCoach(owner, f.Group);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var group = await db.TrainingGroups.SingleAsync(x => x.Id == f.Group); var hidden = Guid.NewGuid();
        db.TrainingGroups.Add(new() { Id = hidden, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "اسم فرع مخفي يجب ألا يكشف " + hidden.ToString("N")[..8], BranchId = f.B, SportId = group.SportId, AgeCategoryId = group.AgeCategoryId }); db.StaffGroupAssignments.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingGroupId = hidden, AcademyMembershipId = coach }); db.TrainingSessions.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingGroupId = hidden, BranchId = f.B, SportId = group.SportId, AgeCategoryId = group.AgeCategoryId, SessionDate = FacilityDay, StartTime = new(17, 0), EndTime = new(18, 0), CreatedByUserId = f.Owner, Source = TrainingSessionSource.Manual }); await db.SaveChangesAsync();
        var email = $"schedule-admin-{Guid.NewGuid():N}@example.test"; var response = await Write(owner, "/api/v1/manage/staff", new StaffWrite("إداري جدول صناعي", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, null)); response.EnsureSuccessStatusCode(); var membership = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var actions = StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m is "facilities" or "academy", m is "facilities" or "academy", false, false)).ToArray(); (await Write(owner, $"/api/v1/manage/staff/{membership}/access", new StaffAccessWrite(false, [f.A], actions), true)).EnsureSuccessStatusCode(); using var admin = await Staff(email);
        var preview = await Write(admin, $"/api/v1/manage/groups/{f.Group}/timetable/preview", ScheduleBody(resource, coach)); preview.EnsureSuccessStatusCode(); var json = await preview.Content.ReadAsStringAsync(); var reviewed = JsonSerializer.Deserialize<TimetablePreview>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!; Assert.NotEmpty(reviewed.Conflicts); Assert.DoesNotContain("اسم فرع مخفي", json); Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/manage/groups/{hidden}/details")).StatusCode);
    }
}
