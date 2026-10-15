using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Mobile;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
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
public sealed class V3CoachWorkspaceTests
{
    private sealed class DemoClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    }
    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment("Demo");
            b.UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Separate test database required"));
            b.UseSetting("Demo:SeedProfile", "LegacyRegression"); b.UseSetting("Demo:SeedEnabled", "true");
            b.UseSetting("Demo:FixedOtpEnabled", "true"); b.UseSetting("Demo:FixedOtp", "246810");
            b.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); b.UseSetting("Demo:ReferenceDate", "2026-09-28");
            b.ConfigureServices(s => { s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(new DemoClock()); });
        }
    }

    [Fact]
    public async Task Upcoming_coach_workspace_uses_real_academy_day_and_live_session_assignment_without_attendance_write()
    {
        await using var factory = new Factory(); using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var coach = await db.AcademyMemberships.Include(m => m.User).FirstAsync(m => m.AcademyId == DemoSeed.NogoomAcademyId && m.Role == AcademyRole.Coach && m.IsActive);
        var token = MobileAuthenticationHandler.NewToken(); var now = new DemoClock().GetUtcNow();
        db.MobileSessions.Add(new MobileSession { Id = Guid.NewGuid(), UserId = coach.UserId, MembershipId = coach.Id, AccessTokenHash = MobileAuthenticationHandler.Hash(token), RefreshTokenHash = MobileAuthenticationHandler.Hash(MobileAuthenticationHandler.NewToken()), SecurityStamp = coach.User.SecurityStamp ?? "", DeviceName = "مدرب تجربة معزولة", CreatedAtUtc = now, LastUsedAtUtc = now, AccessExpiresAtUtc = now.AddMinutes(10), ExpiresAtUtc = now.AddHours(1) });
        var assigned = await db.StaffGroupAssignments.Where(a => a.AcademyId == coach.AcademyId && a.AcademyMembershipId == coach.Id && a.IsActive).Select(a => a.TrainingGroup).FirstAsync();
        var group = new TrainingGroup { Id = Guid.NewGuid(), AcademyId = assigned.AcademyId, ArabicName = "مجموعة رحلة المدرب " + Guid.NewGuid(), BranchId = assigned.BranchId, SportId = assigned.SportId, AgeCategoryId = assigned.AgeCategoryId };
        db.TrainingGroups.Add(group);
        db.StaffGroupAssignments.Add(new StaffGroupAssignment { Id = Guid.NewGuid(), AcademyId = coach.AcademyId, AcademyMembershipId = coach.Id, TrainingGroupId = group.Id });
        var hidden = new TrainingGroup { Id = Guid.NewGuid(), AcademyId = group.AcademyId, ArabicName = "مجموعة مستقلة " + Guid.NewGuid(), BranchId = group.BranchId, SportId = group.SportId, AgeCategoryId = group.AgeCategoryId };
        db.TrainingGroups.Add(hidden);
        var other = await db.TrainingGroups.FirstAsync(g => g.AcademyId == DemoSeed.FutureAcademyId);
        var today = new DateOnly(2026, 10, 15);
        TrainingSession Session(TrainingGroup g, DateOnly date, int hour, TrainingSessionStatus status = TrainingSessionStatus.Scheduled) => new() { Id = Guid.NewGuid(), AcademyId = g.AcademyId, TrainingGroupId = g.Id, BranchId = g.BranchId, SportId = g.SportId, AgeCategoryId = g.AgeCategoryId, SessionDate = date, StartTime = new(hour, 0), EndTime = new(hour, 30), Status = status, Source = TrainingSessionSource.Manual, CreatedByUserId = coach.UserId };
        var later = Session(group, today.AddDays(2), 18); var nearest = Session(group, today, 20); var tomorrow = Session(group, today.AddDays(1), 16); var horizon = Session(group, today.AddDays(14), 17);
        var otherSession = await db.TrainingSessions.SingleOrDefaultAsync(s => s.AcademyId == other.AcademyId && s.TrainingGroupId == other.Id && s.SessionDate == today.AddDays(1) && s.StartTime == new TimeOnly(19, 0));
        if (otherSession is null) { otherSession = Session(other, today.AddDays(1), 19); db.TrainingSessions.Add(otherSession); }
        var excluded = new[] { Session(group, today.AddDays(-1), 17), Session(group, today, 1), Session(group, today.AddDays(15), 17), Session(group, today.AddDays(1), 17, TrainingSessionStatus.Held), Session(group, today.AddDays(1), 18, TrainingSessionStatus.Cancelled), Session(hidden, today.AddDays(1), 19), otherSession };
        db.TrainingSessions.AddRange(new[] { later, nearest, tomorrow, horizon }.Concat(excluded[..^1]));
        var experience = await db.AcademyExperiences.SingleOrDefaultAsync(x => x.AcademyId == coach.AcademyId);
        if (experience is null) { experience = new AcademyExperience { Id = Guid.NewGuid(), AcademyId = coach.AcademyId, DisplayName = "أكاديمية تجربة المدرب" }; db.AcademyExperiences.Add(experience); }
        var previousAttendance = experience.CoachAttendanceEnabled; experience.CoachAttendanceEnabled = false;
        await db.SaveChangesAsync(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            var sessions = await client.GetFromJsonAsync<JsonElement>("/api/v1/attendance/sessions?upcomingOnly=true");
            var ids = sessions.EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToArray();
            var ours = new[] { later.Id, nearest.Id, tomorrow.Id, horizon.Id };
            Assert.Equal(new[] { nearest.Id, tomorrow.Id, later.Id, horizon.Id }, ids.Where(ours.Contains));
            foreach (var s in excluded) Assert.DoesNotContain(s.Id, ids);
            var defaultRows = await client.GetFromJsonAsync<JsonElement>("/api/v1/attendance/sessions");
            Assert.All(defaultRows.EnumerateArray(), s => Assert.Equal("2026-10-15", s.GetProperty("sessionDate").GetString()));
            var roster = await client.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions/{nearest.Id}/players");
            Assert.False(roster.GetProperty("canManageAttendance").GetBoolean()); Assert.True(roster.GetProperty("canEvaluate").GetBoolean());
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/attendance/sessions/{excluded[^2].Id}/players")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/attendance/sessions/{excluded[^1].Id}/players")).StatusCode);
            foreach (var suffix in new[] { "&allDates=true", "&from=2026-10-15", "&to=2026-10-20" })
                Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/attendance/sessions?upcomingOnly=true" + suffix)).StatusCode);
            var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
            using var write = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/attendance/sessions/{nearest.Id}/players") { Content = JsonContent.Create(new { items = Array.Empty<object>() }) };
            write.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(write)).StatusCode);
            Assert.False(await db.PlayerAttendances.AnyAsync(a => a.TrainingSessionId == nearest.Id));
        }
        finally { experience.CoachAttendanceEnabled = previousAttendance; await db.SaveChangesAsync(); }
    }
}
