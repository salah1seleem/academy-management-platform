using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Mobile;
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
    public async Task Coach_profile_options_recheck_assignment_and_return_only_authorized_player_metadata_and_evaluation_capability()
    {
        await using var factory = new Factory(); using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/evaluations/options")).StatusCode);
        var coach = await db.AcademyMemberships.Include(m => m.User).FirstAsync(m => m.AcademyId == DemoSeed.NogoomAcademyId && m.Role == AcademyRole.Coach && m.IsActive);
        var source = await db.StaffGroupAssignments.Where(a => a.AcademyMembershipId == coach.Id && a.IsActive).Select(a => a.TrainingGroup).FirstAsync();
        TrainingGroup Group() => new() { Id = Guid.NewGuid(), AcademyId = source.AcademyId, ArabicName = "مجموعة ملف المدرب " + Guid.NewGuid(), BranchId = source.BranchId, SportId = source.SportId, AgeCategoryId = source.AgeCategoryId };
        var assigned = Group(); var hidden = Group(); db.TrainingGroups.AddRange(assigned, hidden);
        var assignment = new StaffGroupAssignment { Id = Guid.NewGuid(), AcademyId = coach.AcademyId, AcademyMembershipId = coach.Id, TrainingGroupId = assigned.Id };
        db.StaffGroupAssignments.Add(assignment);
        SportEnrollment Enrollment(TrainingGroup g) {
            var player = new Player { Id = Guid.NewGuid(), AcademyId = g.AcademyId, PlayerCode = "E1C-" + Guid.NewGuid().ToString("N")[..8], ArabicName = "طفل ملف تجريبي", DateOfBirth = new(2018, 5, 1), PhotoReference = "/api/v1/assets/" + Guid.NewGuid(), Address = "عنوان داخلي لا يكشف للمدرب" };
            db.Players.Add(player);
            return new() { Id = Guid.NewGuid(), AcademyId = g.AcademyId, Player = player, PlayerId = player.Id, BranchId = g.BranchId, SportId = g.SportId, TrainingGroupId = g.Id, GroupAssignedFrom = new(2026, 10, 14) };
        }
        var visible = Enrollment(assigned); var invisible = Enrollment(hidden); db.SportEnrollments.AddRange(visible, invisible);
        var token = MobileAuthenticationHandler.NewToken(); var now = new DemoClock().GetUtcNow();
        db.MobileSessions.Add(new MobileSession { Id = Guid.NewGuid(), UserId = coach.UserId, MembershipId = coach.Id, AccessTokenHash = MobileAuthenticationHandler.Hash(token), RefreshTokenHash = MobileAuthenticationHandler.Hash(MobileAuthenticationHandler.NewToken()), SecurityStamp = coach.User.SecurityStamp ?? "", DeviceName = "ملف لاعب تجريبي معزول", CreatedAtUtc = now, LastUsedAtUtc = now, AccessExpiresAtUtc = now.AddMinutes(10), ExpiresAtUtc = now.AddHours(1) });
        await db.SaveChangesAsync(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var moneyBefore = (await db.Collections.CountAsync(), await db.PaymentRequests.CountAsync(), await db.PlayerAttendances.CountAsync());
        var options = await client.GetFromJsonAsync<JsonElement>("/api/v1/evaluations/options");
        Assert.True(options.GetProperty("canEvaluate").GetBoolean());
        var rows = options.GetProperty("enrollments").EnumerateArray().ToArray();
        var row = Assert.Single(rows, p => p.GetProperty("id").GetGuid() == visible.Id);
        Assert.Equal(visible.Player.PhotoReference, row.GetProperty("photoReference").GetString());
        Assert.Equal(assigned.Id, row.GetProperty("groupId").GetGuid());
        Assert.False(row.TryGetProperty("address", out _)); Assert.False(row.TryGetProperty("guardian", out _)); Assert.False(row.TryGetProperty("staffNotes", out _));
        Assert.DoesNotContain(rows, p => p.GetProperty("id").GetGuid() == invisible.Id);
        var foreignPlayers = await db.SportEnrollments.Where(e => e.AcademyId != coach.AcademyId).Select(e => e.Id).ToArrayAsync();
        Assert.NotEmpty(foreignPlayers); Assert.DoesNotContain(rows, p => foreignPlayers.Contains(p.GetProperty("id").GetGuid()));
        assignment.IsActive = false; await db.SaveChangesAsync();
        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/evaluations/options");
        Assert.DoesNotContain(after.GetProperty("enrollments").EnumerateArray(), p => p.GetProperty("id").GetGuid() == visible.Id);
        var groups = await client.GetFromJsonAsync<JsonElement>("/api/v1/coach/groups");
        Assert.DoesNotContain(groups.EnumerateArray(), g => g.GetProperty("id").GetGuid() == assigned.Id);
        Assert.Equal(moneyBefore, (await db.Collections.CountAsync(), await db.PaymentRequests.CountAsync(), await db.PlayerAttendances.CountAsync()));
    }
}
