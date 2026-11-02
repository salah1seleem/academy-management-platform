using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
// Explicit Demo/LegacyRegression/GroupPlacement seed only. No reset and no automatic attendance.
public static class AttendanceStatesDemoSeed
{
    public static async Task Seed(FoundationDbContext db, CollectionService collections)
    {
        var academy = DemoSeed.NogoomAcademyId;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + academy}, 0))");
        var source = await db.TrainingGroups.SingleOrDefaultAsync(g => g.AcademyId == academy && g.Id == GroupPlacementDemoSeed.Id(1));
        if (source is null || await db.TrainingGroups.AnyAsync(g => g.Id == GroupPlacementDemoSeed.Id(130))) return;
        var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
        var coach = await db.AcademyMemberships.Where(m => m.AcademyId == academy && m.User.Email == DemoSeed.CoachEmail && m.IsActive).Select(m => m.Id).SingleAsync();
        var guardian = await db.GuardianProfiles.Where(g => g.AcademyId == academy && g.User.PhoneNumber == DemoSeed.GuardianPhone && g.IsActive).Select(g => g.Id).SingleAsync();
        var now = DateTimeOffset.Parse("2026-09-28T06:00:00Z"); var date = new DateOnly(2026, 9, 28);
        var plan = new SubscriptionPlan { Id = GroupPlacementDemoSeed.Id(120), AcademyId = academy, SportId = source.SportId, ArabicName = "رصيد حضور تجريبي — 8 حصص", PlanType = SubscriptionPlanType.Combined, Price = 600, Currency = "EGP", DurationDays = 365, SessionCount = 8, AllBranches = false, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now }; db.SubscriptionPlans.Add(plan);
        db.PlanBranchPublications.Add(new() { Id = GroupPlacementDemoSeed.Id(121), AcademyId = academy, SubscriptionPlanId = plan.Id, BranchId = source.BranchId, CreatedAtUtc = now, UpdatedAtUtc = now }); var version = PlanPriceVersion.Create(plan, [source.BranchId], actor, now); version.Id = GroupPlacementDemoSeed.Id(122); db.PlanPriceVersions.Add(version);
        db.TrainingGroups.Add(new() { Id = GroupPlacementDemoSeed.Id(130), AcademyId = academy, ArabicName = "حضور الحالات الخمس — تجريبي", BranchId = source.BranchId, SportId = source.SportId, AgeCategoryId = source.AgeCategoryId, Capacity = 2, TrainingLevelId = source.TrainingLevelId, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.StaffGroupAssignments.Add(new() { Id = GroupPlacementDemoSeed.Id(132), AcademyId = academy, AcademyMembershipId = coach, TrainingGroupId = GroupPlacementDemoSeed.Id(130), CreatedAtUtc = now, UpdatedAtUtc = now });
        db.TrainingSessions.Add(new() { Id = GroupPlacementDemoSeed.Id(131), AcademyId = academy, TrainingGroupId = GroupPlacementDemoSeed.Id(130), BranchId = source.BranchId, SportId = source.SportId, AgeCategoryId = source.AgeCategoryId, SessionDate = date, StartTime = new(5, 30), EndTime = new(6, 30), CreatedByUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now });
        foreach (var n in new[] { 110, 140 }) {
            db.Players.Add(new() { Id = GroupPlacementDemoSeed.Id(n), AcademyId = academy, PlayerCode = "DEMO-ATT-" + n, ArabicName = "لاعب حالات حضور اصطناعي " + n, DateOfBirth = new(2017, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now });
            db.SportEnrollments.Add(new() { Id = GroupPlacementDemoSeed.Id(n + 1), AcademyId = academy, PlayerId = GroupPlacementDemoSeed.Id(n), BranchId = source.BranchId, SportId = source.SportId, TrainingGroupId = GroupPlacementDemoSeed.Id(130), GroupAssignedFrom = date, TrainingLevelId = source.TrainingLevelId, LevelReviewedByUserId = actor, LevelReviewedAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.GuardianPlayerLinks.Add(new() { Id = GroupPlacementDemoSeed.Id(n + 2), AcademyId = academy, GuardianId = guardian, PlayerId = GroupPlacementDemoSeed.Id(n), CreatedByUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now });
        }
        await db.SaveChangesAsync(); foreach (var n in new[] { 110, 140 }) await collections.CreateCashAsync(academy, actor, GroupPlacementDemoSeed.Id(n + 1), plan.Id, "demo-attendance-states-" + n, "Cash"); await tx.CommitAsync();
    }
}
