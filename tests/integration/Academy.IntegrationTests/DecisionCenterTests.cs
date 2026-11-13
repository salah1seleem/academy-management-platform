using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class BrandingMatchesTests
{
    [Fact]
    public async Task Decision_center_requires_owner_and_three_consecutive_recorded_absences_in_active_subscription()
    {
        using var owner = await Staff();
        using var admin = await Staff(DemoSeed.AdminEmail);
        using var coach = await Mobile(AcademyRole.Coach);
        using var guardian = await Mobile(AcademyRole.Guardian);
        using var foreignOwner = await Staff(DemoSeed.FutureOwnerEmail);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var group = await FollowUpGroup(db);
        var records = await AbsenceRecords(db, group);
        var enrollment = await db.SportEnrollments.SingleAsync(e => e.Id == records[0].SportEnrollmentId);
        var today = await AttendanceCalendar.TodayAsync(group.AcademyId, db, TimeProvider.System);
        var latestSession = await db.TrainingSessions.SingleAsync(s => s.Id == records[3].TrainingSessionId);
        latestSession.SessionDate = today.AddDays(-1);
        latestSession.StartTime = new(17, 0);
        latestSession.EndTime = new(18, 0);
        await db.SaveChangesAsync();

        var path = $"/api/v1/manage/decision-center?kind=Absence&branchId={group.BranchId}";
        foreach (var client in new[] { admin, coach, guardian })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreignOwner.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await owner.GetAsync("/api/v1/manage/decision-center?kind=missing&page=0")).StatusCode);

        async Task<JsonElement[]> Items() =>
            (await owner.GetFromJsonAsync<JsonElement>(path)).GetProperty("items")
            .EnumerateArray().Where(p => p.GetProperty("playerId").GetGuid() == enrollment.PlayerId).ToArray();

        records[3].Status = AttendanceStatus.Absent;
        await db.SaveChangesAsync();
        Assert.Empty(await Items());

        var actor = await db.AcademyMemberships.SingleAsync(m =>
            m.AcademyId == group.AcademyId && m.Role == AcademyRole.AcademyOwner &&
            m.UserId == records[0].RecordedByUserId);
        var plan = await db.SubscriptionPlans.FirstAsync(p =>
            p.AcademyId == group.AcademyId && p.SportId == group.SportId && p.IsActive);
        var now = DateTimeOffset.UtcNow;
        var importId = Guid.NewGuid();
        db.AcademyImportJobs.Add(new AcademyImportJob
        {
            Id = importId, AcademyId = group.AcademyId, ActorUserId = actor.UserId, MembershipId = actor.Id,
            SourceSystem = "decision-center-test", FileHash = new string('a', 64), ReferenceStamp = new string('b', 64),
            IdempotencyKey = importId.ToString(), ApprovedBranches = JsonSerializer.Serialize(new[] { group.BranchId }),
            GuardianResolutions = "[]", CutoffDate = today, State = ImportJobState.Completed,
            RowCount = 1, PublishedRows = 1, GuardianLinksApproved = true, OpeningBalancesApproved = true,
            CompletedAtUtc = now, ExpiresAtUtc = now.AddHours(1), CreatedAtUtc = now, UpdatedAtUtc = now,
        });
        db.SubscriptionPeriods.Add(new SubscriptionPeriod
        {
            Id = Guid.NewGuid(), AcademyId = group.AcademyId, SportEnrollmentId = enrollment.Id,
            SportId = group.SportId, SubscriptionPlanId = plan.Id, PlanTypeSnapshot = SubscriptionPlanType.Duration,
            StartDate = today.AddDays(-30), EndDate = today.AddDays(30), CurrencySnapshot = "EGP",
            Origin = SubscriptionOrigin.ImportedHistorical, ImportJobId = importId,
            Status = SubscriptionPeriodStatus.Active, CreatedByUserId = actor.UserId,
            CreatedAtUtc = now, UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();

        var absence = Assert.Single(await Items());
        Assert.Equal(enrollment.Id, absence.GetProperty("enrollmentId").GetGuid());
        Assert.Equal(group.Id, absence.GetProperty("groupId").GetGuid());
        Assert.False(absence.TryGetProperty("notes", out _));

        foreach (var status in new[] { AttendanceStatus.NotRecorded, AttendanceStatus.Present })
        {
            records[3].Status = status;
            await db.SaveChangesAsync();
            Assert.Empty(await Items());
        }
    }
}
