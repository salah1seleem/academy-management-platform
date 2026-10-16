using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class TimetableUxTests : IAsyncLifetime
{
    private Factory factory = null!;
    private readonly List<Guid> groups = [];
    private readonly List<Guid> players = [];
    private readonly List<Guid> coaches = [];
    public Task InitializeAsync() { factory = new Factory(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Separate test database required")); _ = factory.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await db.PlayerAttendances.Where(x => groups.Contains(x.TrainingGroupId)).ExecuteDeleteAsync();
        await db.StaffAttendances.Where(x => groups.Contains(x.TrainingGroupId)).ExecuteDeleteAsync();
        await db.TrainingSessions.Where(x => groups.Contains(x.TrainingGroupId)).ExecuteDeleteAsync();
        await db.RecurringSchedules.Where(x => groups.Contains(x.TrainingGroupId)).ExecuteDeleteAsync();
        await db.StaffGroupAssignments.Where(x => groups.Contains(x.TrainingGroupId)).ExecuteDeleteAsync();
        await db.SportEnrollments.Where(x => players.Contains(x.PlayerId)).ExecuteDeleteAsync();
        await db.GuardianPlayerLinks.Where(x => players.Contains(x.PlayerId)).ExecuteDeleteAsync();
        await db.Players.Where(x => players.Contains(x.Id)).ExecuteDeleteAsync();
        // Timetable revisions are permanent audit history. Archive only this
        // test's groups; do not delete revisions or their referenced groups.
        var historical = db.TrainingScheduleRevisions.Select(x => x.TrainingGroupId);
        await db.TrainingGroups.Where(x => groups.Contains(x.Id) && historical.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        await db.TrainingGroups.Where(x => groups.Contains(x.Id) && !historical.Contains(x.Id)).ExecuteDeleteAsync();
        await db.AcademyMemberships.Where(x => coaches.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        await db.Users.Where(u => db.AcademyMemberships.Any(m => coaches.Contains(m.Id) && m.UserId == u.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        await factory.DisposeAsync();
    }
    private async Task<HttpClient> Staff(string email = DemoSeed.OwnerEmail)
    {
        var c = factory.CreateClient(); (await Send(c, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" })).EnsureSuccessStatusCode(); return c;
    }
    private static async Task<HttpResponseMessage> Send(HttpClient c, string path, object body)
    {
        var csrf = await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); return await c.SendAsync(request);
    }
    private async Task<HttpClient> Guardian()
    {
        var c = factory.CreateClient(); var r = await Send(c, "/api/v1/auth/guardian/otp/request", new { phoneNumber = DemoSeed.GuardianPhone }); r.EnsureSuccessStatusCode(); var challenge = await r.Content.ReadFromJsonAsync<JsonElement>();
        (await Send(c, "/api/v1/auth/guardian/otp/verify", new { phoneNumber = DemoSeed.GuardianPhone, challengeId = challenge.GetProperty("challengeId").GetGuid(), code = "246810" })).EnsureSuccessStatusCode(); return c;
    }
    private async Task<(Guid Group, Guid Player, Guid Enrollment, Guid Guardian, Guid Coach, string CoachEmail)> Fixture()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        // Copy only reference dimensions; another scenario may have archived the
        // shared group. The new group owns its active lifecycle independently.
        var original = await db.TrainingGroups.SingleAsync(x => x.Id == Slice2DemoSeed.FootballGroupId && x.AcademyId == DemoSeed.NogoomAcademyId && x.Branch.IsActive && x.Sport.IsActive && x.AgeCategory.IsActive);
        var guardian = await db.GuardianProfiles.FirstAsync(x => x.AcademyId == original.AcademyId && x.User.PhoneNumber == DemoSeed.GuardianPhone);
        // A real conflict check must remain enabled. Give this scenario its own
        // coach rather than colliding with earlier scenarios' dated sessions.
        var email = $"timetable.coach.{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "مدرب جدول اصطناعي", Email = email, NormalizedEmail = email.ToUpperInvariant(), UserName = email, NormalizedUserName = email.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString("N") };
        var coach = new AcademyMembership { Id = Guid.NewGuid(), AcademyId = original.AcademyId, UserId = user.Id, Role = AcademyRole.Coach };
        db.Users.Add(user); db.AcademyMemberships.Add(coach); coaches.Add(coach.Id);
        var owner = await db.Users.Where(x => x.Email == DemoSeed.OwnerEmail).Select(x => x.Id).SingleAsync();
        var group = new TrainingGroup { Id = Guid.NewGuid(), AcademyId = original.AcademyId, ArabicName = "مجموعة اختبار أيام " + Guid.NewGuid().ToString("N"), BranchId = original.BranchId, SportId = original.SportId, AgeCategoryId = original.AgeCategoryId };
        var player = new Player { Id = Guid.NewGuid(), AcademyId = original.AcademyId, ArabicName = "لاعب اختبار أيام", PlayerCode = "UX-" + Guid.NewGuid().ToString("N")[..16], DateOfBirth = new DateOnly(2017, 5, 1) };
        var enrollment = new SportEnrollment { Id = Guid.NewGuid(), AcademyId = original.AcademyId, PlayerId = player.Id, SportId = group.SportId, BranchId = group.BranchId, TrainingGroupId = group.Id };
        db.TrainingGroups.Add(group); db.Players.Add(player); db.SportEnrollments.Add(enrollment);
        db.GuardianPlayerLinks.Add(new GuardianPlayerLink { Id = Guid.NewGuid(), AcademyId = group.AcademyId, GuardianId = guardian.Id, PlayerId = player.Id, CreatedByUserId = owner });
        db.StaffGroupAssignments.Add(new StaffGroupAssignment { Id = Guid.NewGuid(), AcademyId = group.AcademyId, AcademyMembershipId = coach.Id, TrainingGroupId = group.Id });
        await db.SaveChangesAsync(); groups.Add(group.Id); players.Add(player.Id); return (group.Id, player.Id, enrollment.Id, guardian.Id, coach.Id, email);
    }
    private static object Timetable(int[]? days = null) => new { from = "2026-10-03", to = "2026-10-17", days = days ?? [6, 1, 3], startTime = "17:07:00", endTime = "18:30:00" };

    [Fact]
    public async Task Exact_weekdays_persist_repeat_and_reach_guardian_without_timezone_shift()
    {
        using var owner = await Staff(); using var guardian = await Guardian(); var f = await Fixture();
        (await Send(owner, $"/api/v1/manage/groups/{f.Group}/timetable", Timetable())).EnsureSuccessStatusCode();
        var dates = Enumerable.Range(0, 15).Select(n => new DateOnly(2026, 10, 3).AddDays(n)).Where(d => new[] { DayOfWeek.Saturday, DayOfWeek.Monday, DayOfWeek.Wednesday }.Contains(d.DayOfWeek)).ToArray();
        var rows = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/sessions?groupId={f.Group}"); Assert.Equal(dates.Select(d => d.ToString("yyyy-MM-dd")), rows.EnumerateArray().Select(x => x.GetProperty("sessionDate").GetString()));
        (await Send(owner, $"/api/v1/manage/groups/{f.Group}/timetable", Timetable())).EnsureSuccessStatusCode(); Assert.Equal(dates.Length, (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/sessions?groupId={f.Group}")).GetArrayLength());
        var profile = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{f.Player}/profile");
        var enrollment = profile.GetProperty("enrollments")[0]; Assert.Equal(new[] { "Saturday", "Monday", "Wednesday" }, enrollment.GetProperty("recurring").EnumerateArray().Select(x => x.GetProperty("dayOfWeek").GetString()));
        Assert.All(enrollment.GetProperty("recurring").EnumerateArray(), x => Assert.Equal("17:07:00", x.GetProperty("startTime").GetString()));
        Assert.Equal("2026-10-10", enrollment.GetProperty("upcoming")[0].GetProperty("sessionDate").GetString()); // test clock is October 8; upcoming excludes the past
        using var other = await Staff(DemoSeed.FutureOwnerEmail); Assert.Equal(HttpStatusCode.NotFound, (await Send(other, $"/api/v1/manage/groups/{f.Group}/timetable", Timetable())).StatusCode);
    }

    [Fact]
    public async Task Session_filters_union_siblings_intersect_dimensions_and_do_not_leak_foreign_ids()
    {
        using var owner = await Staff(); var a = await Fixture(); var b = await Fixture();
        (await Send(owner, $"/api/v1/manage/groups/{a.Group}/timetable", Timetable())).EnsureSuccessStatusCode(); (await Send(owner, $"/api/v1/manage/groups/{b.Group}/timetable", Timetable([0, 2, 4]))).EnsureSuccessStatusCode();
        var siblings = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/sessions?guardianId={a.Guardian}&from=2026-10-03&to=2026-10-17");
        Assert.Contains(siblings.EnumerateArray(), x => x.GetProperty("trainingGroupId").GetGuid() == a.Group); Assert.Contains(siblings.EnumerateArray(), x => x.GetProperty("trainingGroupId").GetGuid() == b.Group);
        Assert.Equal(siblings.GetArrayLength(), siblings.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).Distinct().Count());
        var selected = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/sessions?guardianId={a.Guardian}&playerId={a.Player}&coachId={a.Coach}&groupId={a.Group}"); Assert.NotEmpty(selected.EnumerateArray()); Assert.All(selected.EnumerateArray(), x => Assert.Equal(a.Group, x.GetProperty("trainingGroupId").GetGuid()));
        var conflict = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/sessions?playerId={a.Player}&groupId={b.Group}"); Assert.Equal(0, conflict.GetArrayLength());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var foreignGuardian = await db.GuardianProfiles.Where(x => x.AcademyId == DemoSeed.FutureAcademyId).Select(x => x.Id).FirstAsync();
        foreach (var query in new[] { $"guardianId={foreignGuardian}", $"playerId={Slice2DemoSeed.AcademyBPlayerId}" }) Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/sessions?" + query)).GetArrayLength());
        using var other = await Staff(DemoSeed.FutureOwnerEmail); Assert.Equal(0, (await other.GetFromJsonAsync<JsonElement>($"/api/v1/manage/sessions?groupId={a.Group}")).GetArrayLength());
        var options = await other.GetFromJsonAsync<JsonElement>("/api/v1/manage/sessions/options"); Assert.DoesNotContain(options.GetProperty("players").EnumerateArray(), x => x.GetProperty("id").GetGuid() == a.Player);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/v1/manage/sessions?from=2026-10-17&to=2026-10-03")).StatusCode);
    }

    [Fact]
    public async Task Invalid_days_fail_and_guardian_coach_cannot_use_staff_wide_sessions()
    {
        using var owner = await Staff(); using var guardian = await Guardian(); using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services); var f = await Fixture();
        foreach (var days in new[] { Array.Empty<int>(), new[] { 7 }, new[] { 6, 6 } }) Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, $"/api/v1/manage/groups/{f.Group}/timetable", Timetable(days))).StatusCode);
        foreach (var c in new[] { guardian, coach }) foreach (var path in new[] { "/api/v1/manage/sessions", "/api/v1/manage/sessions/options" }) Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Historical_attendance_survives_timetable_edit_and_player_group_move()
    {
        using var owner = await Staff(); var a = await Fixture(); var b = await Fixture();
        (await Send(owner, $"/api/v1/manage/groups/{a.Group}/timetable", Timetable())).EnsureSuccessStatusCode(); (await Send(owner, $"/api/v1/manage/groups/{b.Group}/timetable", Timetable([0, 2, 4]))).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var session = await db.TrainingSessions.Where(x => x.TrainingGroupId == a.Group).OrderBy(x => x.SessionDate).FirstAsync();
        var actor = await db.Users.Where(x => x.Email == DemoSeed.OwnerEmail).Select(x => x.Id).SingleAsync();
        db.PlayerAttendances.Add(new PlayerAttendance { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingSessionId = session.Id, TrainingGroupId = a.Group, SportEnrollmentId = a.Enrollment, Status = AttendanceStatus.Present, RecordedByUserId = actor }); await db.SaveChangesAsync();
        (await Send(owner, $"/api/v1/manage/groups/{a.Group}/timetable", Timetable([0, 2, 4]))).EnsureSuccessStatusCode();
        await db.SportEnrollments.Where(x => x.Id == a.Enrollment).ExecuteUpdateAsync(s => s.SetProperty(x => x.TrainingGroupId, b.Group));
        var old = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/sessions?playerId={a.Player}&groupId={a.Group}"); Assert.Single(old.EnumerateArray()); Assert.Equal(session.Id, old[0].GetProperty("id").GetGuid());
        var current = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/sessions?playerId={a.Player}&groupId={b.Group}"); Assert.NotEmpty(current.EnumerateArray());
        await db.SportEnrollments.Where(x => x.Id == a.Enrollment).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        var inactive = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/sessions?guardianId={a.Guardian}&playerId={a.Player}&groupId={a.Group}");
        Assert.Single(inactive.EnumerateArray()); Assert.Equal(session.Id, inactive[0].GetProperty("id").GetGuid());
        Assert.Equal(AttendanceStatus.Present, (await db.PlayerAttendances.SingleAsync(x => x.TrainingSessionId == session.Id)).Status);
    }

    [Fact]
    public async Task Denied_people_permission_blocks_both_staff_session_data_and_filter_options()
    {
        using var admin = await Staff(DemoSeed.AdminEmail); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var member = await db.AcademyMemberships.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.User.Email == DemoSeed.AdminEmail).Select(x => x.Id).SingleAsync();
        var grant = new StaffGrant { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, MembershipId = member, Permission = AcademyPermissions.PeopleManage, Allowed = false };
        db.StaffGrants.Add(grant); await db.SaveChangesAsync();
        try { foreach (var path in new[] { "/api/v1/manage/sessions", "/api/v1/manage/sessions/options" }) Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path)).StatusCode); }
        finally { await db.StaffGrants.Where(x => x.Id == grant.Id).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task Attendance_defaults_to_real_Cairo_day_and_clear_includes_other_dates_in_time_order()
    {
        using var owner = await Staff(); var f = await Fixture();
        (await Send(owner, $"/api/v1/manage/groups/{f.Group}/timetable", Timetable())).EnsureSuccessStatusCode();
        var today = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions?groupId={f.Group}");
        Assert.Empty(today.EnumerateArray()); // October 8 is Thursday, outside this group's selected days.
        var all = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions?groupId={f.Group}&allDates=true");
        Assert.NotEmpty(all.EnumerateArray()); Assert.All(all.EnumerateArray(), x => Assert.Equal(f.Group, x.GetProperty("trainingGroupId").GetGuid()));
        var dates = all.EnumerateArray().Select(x => x.GetProperty("sessionDate").GetString()).ToArray(); Assert.Equal(dates.Order(), dates);
        using var other = await Staff(DemoSeed.FutureOwnerEmail);
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions?groupId={f.Group}&allDates=true")).EnumerateArray());
    }

    [Fact]
    public async Task No_role_can_record_future_player_or_session_staff_attendance()
    {
        using var owner = await Staff(); using var admin = await Staff(DemoSeed.AdminEmail); using var coach = factory.CreateClient();
        var f = await Fixture(); await MobileCoachTestSession.SignInAsync(coach, factory.Services, f.CoachEmail);
        (await Send(owner, $"/api/v1/manage/groups/{f.Group}/timetable", Timetable())).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var session = await db.TrainingSessions.Where(x => x.TrainingGroupId == f.Group && x.SessionDate > new DateOnly(2026, 10, 8)).OrderBy(x => x.SessionDate).FirstAsync();
        var policy = await db.AcademyExperiences.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId);
        var previous = (policy.CoachAttendanceEnabled, policy.CoachAttendancePolicyConfirmed);
        policy.CoachAttendanceEnabled = true; policy.CoachAttendancePolicyConfirmed = true; await db.SaveChangesAsync();
        try
        {
            // Exercise date validation even for a coach explicitly authorized by the owner.
            // Default-deny is covered independently by V2AttendancePolicyTests.
            foreach (var client in new[] { owner, admin, coach })
            {
                Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, $"/api/v1/attendance/sessions/{session.Id}/players", new { items = new[] { new { sportEnrollmentId = f.Enrollment, status = 2 } } })).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, $"/api/v1/attendance/sessions/{session.Id}/staff", new { items = new[] { new { academyMembershipId = f.Coach, status = 2 } } })).StatusCode);
            }
            Assert.False(await db.PlayerAttendances.AnyAsync(x => x.TrainingSessionId == session.Id)); Assert.False(await db.StaffAttendances.AnyAsync(x => x.TrainingSessionId == session.Id));
        }
        finally { policy.CoachAttendanceEnabled = previous.CoachAttendanceEnabled; policy.CoachAttendancePolicyConfirmed = previous.CoachAttendancePolicyConfirmed; await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task Daily_coach_detail_saves_today_absence_and_discount_and_rejects_future_and_foreign_access()
    {
        using var owner = await Staff(); var f = await Fixture(); const string date = "2026-10-08";
        var day = await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/staff-attendance/"); Assert.Equal(date, day.GetProperty("date").GetString());
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff-attendance/{f.Coach}?date={date}"); Assert.Equal(f.Coach, detail.GetProperty("id").GetGuid()); Assert.Equal(date, detail.GetProperty("today").GetString());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.False(await db.StaffDailyAttendances.AnyAsync(x => x.MembershipId == f.Coach && x.WorkDate == new DateOnly(2026, 10, 8)));
        try
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Put(owner, $"/api/v1/manage/staff-attendance/{f.Coach}", new { date = "2026-10-09", status = "Present", checkIn = "17:30:00", approvedDeduction = 0 })).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Put(owner, $"/api/v1/manage/staff-attendance/{f.Coach}", new { date, status = "Present", checkIn = "17:30:00", approvedDeduction = 0 })).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Put(owner, $"/api/v1/manage/staff-attendance/{f.Coach}", new { date, status = "Present", checkIn = "00:30:00", approvedDeduction = 100 })).StatusCode);
            (await Put(owner, $"/api/v1/manage/staff-attendance/{f.Coach}", new { date, status = "Present", checkIn = "00:30:00", checkOut = "00:50:00", approvedDeduction = 100, reason = "اختبار خصم حضور" })).EnsureSuccessStatusCode();
            (await Put(owner, $"/api/v1/manage/staff-attendance/{f.Coach}", new { date, status = "Absent", approvedDeduction = 100, reason = "اختبار غياب" })).EnsureSuccessStatusCode();
            var report = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/staff-daily?coachId={f.Coach}&allDates=true");
            var saved = Assert.Single(report.GetProperty("items").EnumerateArray(), x => x.GetProperty("workDate").GetString() == date); Assert.Equal("Absent", saved.GetProperty("status").GetString()); Assert.Equal(100, saved.GetProperty("approvedDeduction").GetDecimal());
            var all = await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/staff-attendance/?allDates=true"); Assert.Equal(JsonValueKind.Null, all.GetProperty("date").ValueKind); Assert.Contains(all.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == f.Coach && x.GetProperty("workDate").GetString() == date);
            using var other = await Staff(DemoSeed.FutureOwnerEmail); Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/manage/staff-attendance/{f.Coach}?date={date}")).StatusCode);
        }
        finally { await db.StaffDailyAttendances.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.MembershipId == f.Coach && x.WorkDate == new DateOnly(2026, 10, 8)).ExecuteDeleteAsync(); }
    }

    [Fact]
    public async Task Unbounded_reports_queue_and_exports_remain_authorized_after_clear()
    {
        using var owner = await Staff(); using var admin = await Staff(DemoSeed.AdminEmail);
        foreach (var path in new[] { "/api/v1/reports/owner-summary", "/api/v1/reports/financial", "/api/v1/reports/attendance", "/api/v1/reports/staff-daily", "/api/v1/manage/payment-requests", "/api/v1/reports/financial/export", "/api/v1/reports/attendance/export", "/api/v1/reports/staff-daily/export" })
        {
            (await owner.GetAsync(path + "?allDates=true")).EnsureSuccessStatusCode();
            if (path.Contains("/reports/")) Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path + "?allDates=true")).StatusCode);
        }
    }

    private static async Task<HttpResponseMessage> Put(HttpClient c, string path, object body)
    {
        var csrf = await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); return await c.SendAsync(request);
    }

    private sealed class Factory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b) { b.UseEnvironment("Demo"); b.UseSetting("ConnectionStrings:Default", connection); b.UseSetting("Demo:SeedProfile", "LegacyRegression"); b.UseSetting("Demo:SeedEnabled", "true"); b.UseSetting("Demo:FixedOtpEnabled", "true"); b.UseSetting("Demo:FixedOtp", "246810"); b.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); b.UseSetting("Demo:ReferenceDate", "2026-09-28"); b.ConfigureServices(s => { s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(new AttendanceTestTime()); }); }
    }
    private sealed class AttendanceTestTime : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-10-07T22:10:00Z"); }
}
