using Academy.Api.Auth;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
// Called only by the explicit LegacyRegression opt-in. Never rewrites a placement or a payment.
public static class GroupTransferDemoSeed
{
    public static async Task Seed(FoundationDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var academy = DemoSeed.NogoomAcademyId;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + academy}, 0))");
        var source = await db.TrainingGroups.SingleOrDefaultAsync(g => g.Id == GroupPlacementDemoSeed.Id(1) && g.AcademyId == academy);
        if (source is null || await db.TrainingGroups.AnyAsync(g => g.Id == GroupPlacementDemoSeed.Id(50))) return;
        var coach = await db.StaffGroupAssignments.Where(a => a.TrainingGroupId == source.Id && a.IsActive && a.AcademyMembership.IsActive).Select(a => (Guid?)a.AcademyMembershipId).FirstOrDefaultAsync();
        if (coach is null) return;
        var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
        var date = new DateOnly(2026, 11, 15); var start = new TimeOnly(17, 0); var end = new TimeOnly(18, 0);
        if (await db.TrainingSessions.AnyAsync(s => s.AcademyId == academy && s.SessionDate == date && s.Status != TrainingSessionStatus.Cancelled && s.StartTime < end && start < s.EndTime && db.StaffGroupAssignments.Any(a => a.TrainingGroupId == s.TrainingGroupId && a.AcademyMembershipId == coach && a.IsActive))) return;
        db.TrainingGroups.Add(new() { Id = GroupPlacementDemoSeed.Id(50), AcademyId = academy, ArabicName = "نقل السباحة — موعد مسائي تجريبي", BranchId = source.BranchId, SportId = source.SportId, AgeCategoryId = source.AgeCategoryId, Capacity = 2, TrainingLevelId = source.TrainingLevelId, CreatedAtUtc = source.CreatedAtUtc, UpdatedAtUtc = source.CreatedAtUtc });
        db.StaffGroupAssignments.Add(new() { Id = GroupPlacementDemoSeed.Id(52), AcademyId = academy, TrainingGroupId = GroupPlacementDemoSeed.Id(50), AcademyMembershipId = coach.Value, CreatedAtUtc = source.CreatedAtUtc, UpdatedAtUtc = source.CreatedAtUtc });
        db.TrainingSessions.Add(new() { Id = GroupPlacementDemoSeed.Id(51), AcademyId = academy, TrainingGroupId = GroupPlacementDemoSeed.Id(50), BranchId = source.BranchId, SportId = source.SportId, AgeCategoryId = source.AgeCategoryId, SessionDate = date, StartTime = start, EndTime = end, Source = TrainingSessionSource.Manual, CreatedByUserId = actor, CreatedAtUtc = source.CreatedAtUtc, UpdatedAtUtc = source.CreatedAtUtc });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
