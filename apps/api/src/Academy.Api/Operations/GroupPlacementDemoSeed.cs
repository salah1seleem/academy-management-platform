using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
// Explicit Demo/LegacyRegression opt-in only; a committed seed is never reset on restart.
public static class GroupPlacementDemoSeed
{
    public static Guid Id(int n) => Guid.Parse($"5a000000-0000-0000-0000-{n:D12}");
    public static async Task Seed(FoundationDbContext db, CollectionService collections)
    {
        var academy = DemoSeed.NogoomAcademyId; var branch = Slice2DemoSeed.FifthBranchId;
        await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + academy}, 0))");
        if (await db.TrainingGroups.AnyAsync(x => x.Id == Id(1)) || !await db.Academies.AnyAsync(x => x.Id == academy && x.Slug == "nogoom-demo" && x.IsActive) || !await db.Branches.AnyAsync(x => x.Id == branch && x.IsActive)) return;
        var coach = await db.AcademyMemberships.Where(x => x.AcademyId == academy && x.User.Email == DemoSeed.CoachEmail && x.IsActive && x.User.IsActive).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(); var guardian = await db.GuardianProfiles.Where(x => x.AcademyId == academy && x.User.PhoneNumber == DemoSeed.GuardianPhone && x.IsActive).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(); if (coach is null || guardian is null) return;
        var owner = await db.Users.Where(x => x.Email == DemoSeed.OwnerEmail).Select(x => x.Id).SingleAsync(); var now = DateTimeOffset.Parse("2026-09-28T06:00:00Z"); var date = new DateOnly(2026, 11, 15); var start = new TimeOnly(15, 0); var end = new TimeOnly(16, 0);
        if (await db.TrainingSessions.AnyAsync(x => x.AcademyId == academy && x.SessionDate == date && x.Status != TrainingSessionStatus.Cancelled && x.StartTime < end && start < x.EndTime && db.StaffGroupAssignments.Any(a => a.TrainingGroupId == x.TrainingGroupId && a.AcademyMembershipId == coach && a.IsActive))) return;
        db.TrainingLevels.Add(new() { Id = Id(2), AcademyId = academy, SportId = Slice2DemoSeed.SwimmingId, ArabicName = "مبتدئ — تسكين تجريبي", Rank = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.TrainingGroups.Add(new() { Id = Id(1), AcademyId = academy, ArabicName = "تسكين السباحة — مراجعة الأهلية", BranchId = branch, SportId = Slice2DemoSeed.SwimmingId, AgeCategoryId = Slice2DemoSeed.U10Id, Capacity = 3, TrainingLevelId = Id(2), CreatedAtUtc = now, UpdatedAtUtc = now });
        db.StaffGroupAssignments.Add(new() { Id = Id(3), AcademyId = academy, AcademyMembershipId = coach.Value, TrainingGroupId = Id(1), CreatedAtUtc = now, UpdatedAtUtc = now });
        db.TrainingSessions.Add(new() { Id = Id(4), AcademyId = academy, TrainingGroupId = Id(1), BranchId = branch, SportId = Slice2DemoSeed.SwimmingId, AgeCategoryId = Slice2DemoSeed.U10Id, SessionDate = date, StartTime = start, EndTime = end, Source = TrainingSessionSource.Manual, CreatedByUserId = owner, CreatedAtUtc = now, UpdatedAtUtc = now });
        var plan = new SubscriptionPlan { Id = Id(5), AcademyId = academy, SportId = Slice2DemoSeed.SwimmingId, ArabicName = "استحقاق تسكين تجريبي — لا تحصيل حقيقي", PlanType = SubscriptionPlanType.Duration, Price = 600, Currency = "EGP", DurationDays = 365, AllBranches = false, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now }; db.SubscriptionPlans.Add(plan);
        db.PlanBranchPublications.Add(new() { Id = Id(6), AcademyId = academy, SubscriptionPlanId = plan.Id, BranchId = branch, CreatedAtUtc = now, UpdatedAtUtc = now }); var version = PlanPriceVersion.Create(plan, [branch], owner, now); version.Id = Id(7); db.PlanPriceVersions.Add(version);
        foreach (var n in new[] { 10, 20, 30 })
        {
            db.Players.Add(new() { Id = Id(n), AcademyId = academy, PlayerCode = "DEMO-BULK-" + n, ArabicName = n == 30 ? "لاعب انتظار غير مدفوع تجريبي" : "لاعب تسكين مستوفٍ تجريبي " + n, DateOfBirth = new(2017, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now });
            db.SportEnrollments.Add(new() { Id = Id(n + 1), AcademyId = academy, PlayerId = Id(n), SportId = Slice2DemoSeed.SwimmingId, BranchId = branch, TrainingLevelId = Id(2), LevelReviewedByUserId = owner, LevelReviewedAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.GuardianPlayerLinks.Add(new() { Id = Id(n + 2), AcademyId = academy, GuardianId = guardian.Value, PlayerId = Id(n), CreatedByUserId = owner, CreatedAtUtc = now, UpdatedAtUtc = now });
        }
        await db.SaveChangesAsync(); foreach (var n in new[] { 10, 20 }) await collections.CreateCashAsync(academy, owner, Id(n + 1), plan.Id, "demo-bulk-placement-" + n, "Cash"); await tx.CommitAsync();
    }
}
