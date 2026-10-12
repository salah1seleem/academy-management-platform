using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

// Explicit LegacyRegression opt-in. Dedicated read-only recommendation fixture:
// other journeys may place their own children without invalidating this one.
public static class PlacementRecommendationDemoSeed
{
    public static async Task Seed(FoundationDbContext db, CollectionService collections)
    {
        var academy = DemoSeed.NogoomAcademyId;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + academy}, 0))");
        var source = await db.TrainingGroups.SingleOrDefaultAsync(g => g.AcademyId == academy && g.Id == GroupPlacementDemoSeed.Id(1));
        if (source is null || await db.Players.AnyAsync(p => p.Id == GroupPlacementDemoSeed.Id(194))) return;
        var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
        var coach = await db.StaffGroupAssignments.Where(a => a.TrainingGroupId == source.Id && a.IsActive).Select(a => a.AcademyMembershipId).SingleAsync();
        var guardian = await db.GuardianProfiles.Where(g => g.AcademyId == academy && g.User.PhoneNumber == DemoSeed.GuardianPhone && g.IsActive).Select(g => g.Id).SingleAsync();
        var now = DateTimeOffset.Parse("2026-09-28T06:00:00Z");
        db.TrainingLevels.Add(new() { Id = GroupPlacementDemoSeed.Id(190), AcademyId = academy, SportId = source.SportId, ArabicName = "مستوى مراجعة البدائل — اصطناعي", Rank = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.TrainingGroups.Add(new() { Id = GroupPlacementDemoSeed.Id(191), AcademyId = academy, ArabicName = "بدائل التسكين — مجموعة اصطناعية مستقلة", BranchId = source.BranchId, SportId = source.SportId, AgeCategoryId = source.AgeCategoryId, TrainingLevelId = GroupPlacementDemoSeed.Id(190), Capacity = 2, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.StaffGroupAssignments.Add(new() { Id = GroupPlacementDemoSeed.Id(192), AcademyId = academy, TrainingGroupId = GroupPlacementDemoSeed.Id(191), AcademyMembershipId = coach, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.TrainingSessions.Add(new() { Id = GroupPlacementDemoSeed.Id(193), AcademyId = academy, TrainingGroupId = GroupPlacementDemoSeed.Id(191), BranchId = source.BranchId, SportId = source.SportId, AgeCategoryId = source.AgeCategoryId, SessionDate = new(2026, 11, 15), StartTime = new(1, 0), EndTime = new(1, 30), CreatedByUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.Players.Add(new() { Id = GroupPlacementDemoSeed.Id(194), AcademyId = academy, PlayerCode = "DEMO-RECOMMEND-194", ArabicName = "لاعب مراجعة البدائل الاصطناعي", DateOfBirth = new(2017, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now });
        db.SportEnrollments.Add(new() { Id = GroupPlacementDemoSeed.Id(195), AcademyId = academy, PlayerId = GroupPlacementDemoSeed.Id(194), BranchId = source.BranchId, SportId = source.SportId, TrainingLevelId = GroupPlacementDemoSeed.Id(190), LevelReviewedByUserId = actor, LevelReviewedAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.GuardianPlayerLinks.Add(new() { Id = GroupPlacementDemoSeed.Id(196), AcademyId = academy, PlayerId = GroupPlacementDemoSeed.Id(194), GuardianId = guardian, CreatedByUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync();
        await collections.CreateCashAsync(academy, actor, GroupPlacementDemoSeed.Id(195), GroupPlacementDemoSeed.Id(5), "demo-recommendation-194", "InternalTest");
        await tx.CommitAsync();
    }
}
