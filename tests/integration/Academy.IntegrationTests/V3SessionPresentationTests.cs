using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Mobile;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V3CoachWorkspaceTests
{
    [Fact]
    public async Task Session_presentation_uses_explicit_group_format_not_its_name_and_does_not_grant_attendance()
    {
        await using var factory = new Factory(); using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var coach = await db.AcademyMemberships.Include(m => m.User).FirstAsync(m => m.AcademyId == DemoSeed.NogoomAcademyId && m.Role == AcademyRole.Coach && m.IsActive);
        var assigned = await db.StaffGroupAssignments.Where(a => a.AcademyMembershipId == coach.Id && a.IsActive).Select(a => a.TrainingGroup).FirstAsync();
        var now = new DemoClock().GetUtcNow(); var token = MobileAuthenticationHandler.NewToken();
        db.MobileSessions.Add(new MobileSession { Id = Guid.NewGuid(), UserId = coach.UserId, MembershipId = coach.Id, AccessTokenHash = MobileAuthenticationHandler.Hash(token), RefreshTokenHash = MobileAuthenticationHandler.Hash(MobileAuthenticationHandler.NewToken()), SecurityStamp = coach.User.SecurityStamp ?? "", DeviceName = "سياق الحصة الاصطناعي", CreatedAtUtc = now, LastUsedAtUtc = now, AccessExpiresAtUtc = now.AddMinutes(10), ExpiresAtUtc = now.AddHours(1) });
        var sessions = new List<(Guid Id, TrainingRegistrationFormat Format)>();
        foreach (var format in new[] { TrainingRegistrationFormat.Group, TrainingRegistrationFormat.Private, TrainingRegistrationFormat.SemiPrivate })
        {
            var group = new TrainingGroup { Id = Guid.NewGuid(), AcademyId = coach.AcademyId, BranchId = assigned.BranchId, SportId = assigned.SportId, AgeCategoryId = assigned.AgeCategoryId, ArabicName = "اسم مضلل برايفت جماعي " + Guid.NewGuid(), RegistrationFormat = format };
            db.TrainingGroups.Add(group);
            db.StaffGroupAssignments.Add(new StaffGroupAssignment { Id = Guid.NewGuid(), AcademyId = coach.AcademyId, TrainingGroupId = group.Id, AcademyMembershipId = coach.Id });
            var session = new TrainingSession { Id = Guid.NewGuid(), AcademyId = group.AcademyId, TrainingGroupId = group.Id, BranchId = group.BranchId, SportId = group.SportId, AgeCategoryId = group.AgeCategoryId, SessionDate = new(2026, 10, 16), StartTime = new(18, 0), EndTime = new(19, 0), Source = TrainingSessionSource.Manual, CreatedByUserId = coach.UserId };
            db.TrainingSessions.Add(session); sessions.Add((session.Id, format));
        }
        await db.SaveChangesAsync(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var before = (await db.Collections.CountAsync(), await db.PlayerAttendances.CountAsync(), await db.SessionCoachOverrides.CountAsync());
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/attendance/sessions?upcomingOnly=true");
        foreach (var item in sessions)
        {
            var row = list.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == item.Id);
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions/{item.Id}");
            var roster = await client.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions/{item.Id}/players");
            foreach (var json in new[] { row, detail, roster.GetProperty("session") })
            {
                Assert.Equal(item.Format.ToString(), json.GetProperty("trainingFormat").GetString());
                Assert.Equal("CurrentGroup", json.GetProperty("trainingFormatSource").GetString());
                Assert.False(json.GetProperty("isSubstitute").GetBoolean());
                Assert.False(json.TryGetProperty("qualificationEvidence", out _));
                Assert.False(json.TryGetProperty("payBasis", out _));
            }
        }
        var foreign = await db.TrainingSessions.FirstAsync(s => s.AcademyId == DemoSeed.FutureAcademyId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/attendance/sessions/{foreign.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/attendance/sessions/{foreign.Id}/players")).StatusCode);
        Assert.DoesNotContain(list.EnumerateArray(), r => r.GetProperty("id").GetGuid() == foreign.Id);
        Assert.Equal(before, (await db.Collections.CountAsync(), await db.PlayerAttendances.CountAsync(), await db.SessionCoachOverrides.CountAsync()));
    }
}
