using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
// Explicit LegacyRegression opt-in only. Provides a source incident, not an automatic grant.
public static class CompensationDemoSeed
{
    public static async Task Seed(FoundationDbContext db, CollectionService collections)
    {
        var academy = DemoSeed.NogoomAcademyId;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + academy}, 0))");
        var source = await db.TrainingGroups.SingleOrDefaultAsync(g => g.AcademyId == academy && g.Id == GroupPlacementDemoSeed.Id(1));
        if (source is null || await db.Players.AnyAsync(p => p.Id == GroupPlacementDemoSeed.Id(80))) return;
        var coach = await db.StaffGroupAssignments.Where(a => a.TrainingGroupId == source.Id && a.IsActive).Select(a => a.AcademyMembershipId).FirstAsync();
        var guardian = await db.GuardianProfiles.Where(g => g.AcademyId == academy && g.User.PhoneNumber == DemoSeed.GuardianPhone && g.IsActive).Select(g => g.Id).SingleAsync();
        var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
        var now = DateTimeOffset.Parse("2026-09-28T06:00:00Z");
        foreach (var n in new[] { 70, 90 })
        {
            db.TrainingGroups.Add(new() { Id = GroupPlacementDemoSeed.Id(n), AcademyId = academy, ArabicName = n == 70 ? "السباحة — واقعة تعويض تجريبية" : "السباحة — موعد تعويضي تجريبي", BranchId = source.BranchId, SportId = source.SportId, AgeCategoryId = source.AgeCategoryId, Capacity = 2, TrainingLevelId = source.TrainingLevelId, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.StaffGroupAssignments.Add(new() { Id = GroupPlacementDemoSeed.Id(n + 2), AcademyId = academy, TrainingGroupId = GroupPlacementDemoSeed.Id(n), AcademyMembershipId = coach, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.TrainingSessions.Add(new() { Id = GroupPlacementDemoSeed.Id(n + 1), AcademyId = academy, TrainingGroupId = GroupPlacementDemoSeed.Id(n), BranchId = source.BranchId, SportId = source.SportId, AgeCategoryId = source.AgeCategoryId, SessionDate = n == 70 ? new(2026, 9, 28) : new(2026, 11, 15), StartTime = new(19, 0), EndTime = new(20, 0), Status = n == 70 ? TrainingSessionStatus.Cancelled : TrainingSessionStatus.Scheduled, Source = TrainingSessionSource.Manual, CreatedByUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now });
        }
        db.Players.Add(new() { Id = GroupPlacementDemoSeed.Id(80), AcademyId = academy, PlayerCode = "DEMO-COMPENSATION-80", ArabicName = "لاعب تعويض سباحة تجريبي", DateOfBirth = new(2017, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now });
        db.SportEnrollments.Add(new() { Id = GroupPlacementDemoSeed.Id(81), AcademyId = academy, PlayerId = GroupPlacementDemoSeed.Id(80), BranchId = source.BranchId, SportId = source.SportId, TrainingGroupId = GroupPlacementDemoSeed.Id(70), GroupAssignedFrom = new(2026, 9, 28), TrainingLevelId = source.TrainingLevelId, LevelReviewedByUserId = actor, LevelReviewedAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.GuardianPlayerLinks.Add(new() { Id = GroupPlacementDemoSeed.Id(82), AcademyId = academy, GuardianId = guardian, PlayerId = GroupPlacementDemoSeed.Id(80), CreatedByUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); await collections.CreateCashAsync(academy, actor, GroupPlacementDemoSeed.Id(81), GroupPlacementDemoSeed.Id(5), "demo-compensation-80", "Cash"); await tx.CommitAsync();
    }
}
