using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Mobile;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V3CoachWorkspaceTests
{
    [Fact]
    public async Task Approved_session_overview_returns_actual_assigned_staff_roles_without_cross_academy_disclosure_or_writes()
    {
        await using var factory = new Factory(); using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var coach = await WorkspaceCoach(db);
        var admin = await db.AcademyMemberships.Include(m => m.User).SingleAsync(m => m.AcademyId == coach.AcademyId && m.Role == AcademyRole.AcademyAdmin && m.PrimaryMembershipId == null && m.IsActive && m.User.IsActive && m.User.Email == DemoSeed.AdminEmail);
        var assigned = await WorkspaceSourceGroup(db, coach);
        var group = new TrainingGroup { Id = Guid.NewGuid(), AcademyId = coach.AcademyId, BranchId = assigned.BranchId, SportId = assigned.SportId, AgeCategoryId = assigned.AgeCategoryId, ArabicName = "مجموعة ملخص الإداري الاصطناعية " + Guid.NewGuid(), RegistrationFormat = TrainingRegistrationFormat.Group };
        db.TrainingGroups.Add(group);
        foreach (var member in new[] { coach.Id, admin.Id }) db.StaffGroupAssignments.Add(new StaffGroupAssignment { Id = Guid.NewGuid(), AcademyId = coach.AcademyId, TrainingGroupId = group.Id, AcademyMembershipId = member });
        var session = new TrainingSession { Id = Guid.NewGuid(), AcademyId = group.AcademyId, TrainingGroupId = group.Id, BranchId = group.BranchId, SportId = group.SportId, AgeCategoryId = group.AgeCategoryId, SessionDate = new(2026, 10, 16), StartTime = new(12, 0), EndTime = new(13, 0), Source = TrainingSessionSource.Manual, CreatedByUserId = coach.UserId };
        db.TrainingSessions.Add(session);
        var now = new DemoClock().GetUtcNow(); var token = MobileAuthenticationHandler.NewToken();
        db.MobileSessions.Add(new MobileSession { Id = Guid.NewGuid(), UserId = coach.UserId, MembershipId = coach.Id, AccessTokenHash = MobileAuthenticationHandler.Hash(token), RefreshTokenHash = MobileAuthenticationHandler.Hash(MobileAuthenticationHandler.NewToken()), SecurityStamp = coach.User.SecurityStamp ?? "", DeviceName = "قبول ملخص إداري اصطناعي", CreatedAtUtc = now, LastUsedAtUtc = now, AccessExpiresAtUtc = now.AddMinutes(10), ExpiresAtUtc = now.AddHours(1) });
        await db.SaveChangesAsync(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var before = (await db.Collections.CountAsync(), await db.PlayerAttendances.CountAsync(), await db.StaffAttendances.CountAsync(), await db.AcademyNotices.CountAsync());
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions?from=2026-10-16&to=2026-10-16&groupId={group.Id}");
        var row = Assert.Single(list.EnumerateArray());
        Assert.Equal(session.Id, row.GetProperty("id").GetGuid());
        var staff = row.GetProperty("staff").EnumerateArray().ToArray();
        Assert.Equal("Coach", Assert.Single(staff, x => x.GetProperty("id").GetGuid() == coach.Id).GetProperty("role").GetString());
        Assert.Equal("AcademyAdmin", Assert.Single(staff, x => x.GetProperty("id").GetGuid() == admin.Id).GetProperty("role").GetString());
        Assert.All(staff, x => Assert.Equal("NotRecorded", x.GetProperty("attendanceStatus").GetString()));
        var foreign = await db.TrainingSessions.FirstAsync(s => s.AcademyId == DemoSeed.FutureAcademyId);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions?from=2026-10-16&to=2026-10-16&groupId={foreign.TrainingGroupId}")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/attendance/sessions/{foreign.Id}")).StatusCode);
        Assert.Equal(before, (await db.Collections.CountAsync(), await db.PlayerAttendances.CountAsync(), await db.StaffAttendances.CountAsync(), await db.AcademyNotices.CountAsync()));
    }
}
