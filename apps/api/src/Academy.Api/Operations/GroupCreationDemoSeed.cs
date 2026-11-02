using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
// Called only from the explicit Demo / LegacyRegression / GroupPlacement opt-in.
// Never resets a group, a placement or a collection already edited by a reviewer.
public static class GroupCreationDemoSeed
{
    public static async Task Seed(FoundationDbContext db, CollectionService collections)
    {
        var academy = DemoSeed.NogoomAcademyId;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + academy}, 0))");
        var source = await db.TrainingGroups.SingleOrDefaultAsync(g => g.AcademyId == academy && g.Id == GroupPlacementDemoSeed.Id(1));
        if (source is null || await db.Players.AnyAsync(p => p.Id == GroupPlacementDemoSeed.Id(100))) return;
        var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
        var guardian = await db.GuardianProfiles.Where(g => g.AcademyId == academy && g.User.PhoneNumber == DemoSeed.GuardianPhone && g.IsActive).Select(g => g.Id).SingleAsync();
        var now = DateTimeOffset.Parse("2026-09-28T06:00:00Z");
        db.Players.Add(new() { Id = GroupPlacementDemoSeed.Id(100), AcademyId = academy, PlayerCode = "DEMO-GROUP-WIZARD-100", ArabicName = "لاعب إنشاء مجموعة تجريبي", DateOfBirth = new(2017, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now });
        db.SportEnrollments.Add(new() { Id = GroupPlacementDemoSeed.Id(101), AcademyId = academy, PlayerId = GroupPlacementDemoSeed.Id(100), BranchId = source.BranchId, SportId = source.SportId, TrainingLevelId = source.TrainingLevelId, LevelReviewedByUserId = actor, LevelReviewedAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.GuardianPlayerLinks.Add(new() { Id = GroupPlacementDemoSeed.Id(102), AcademyId = academy, GuardianId = guardian, PlayerId = GroupPlacementDemoSeed.Id(100), CreatedByUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync();
        await collections.CreateCashAsync(academy, actor, GroupPlacementDemoSeed.Id(101), GroupPlacementDemoSeed.Id(5), "demo-group-wizard-100", "Cash");
        await tx.CommitAsync();
    }
}
