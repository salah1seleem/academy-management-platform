using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Api.Auth;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;
public static class TrainingFormatDemoSeed
{
    public static async Task Seed(FoundationDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var source = await db.SportEnrollments.SingleOrDefaultAsync(e => e.Id == GroupPlacementDemoSeed.Id(21));
        if (source is null || source.AcademyId != DemoSeed.NogoomAcademyId || !await db.Academies.AnyAsync(a => a.Id == source.AcademyId && a.Slug == "nogoom-demo")) return;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"demo-registration-formats:" + source.AcademyId}, 0))");
        if (!await db.SportEnrollments.AnyAsync(e => e.Id == GroupPlacementDemoSeed.Id(41))) db.SportEnrollments.Add(new() { Id = GroupPlacementDemoSeed.Id(41), AcademyId = source.AcademyId, PlayerId = source.PlayerId, SportId = source.SportId, BranchId = source.BranchId, RegistrationFormat = TrainingRegistrationFormat.Private, TrainingLevelId = source.TrainingLevelId, LevelReviewedByUserId = source.LevelReviewedByUserId, LevelReviewedAtUtc = source.LevelReviewedAtUtc, CreatedAtUtc = source.CreatedAtUtc, UpdatedAtUtc = source.UpdatedAtUtc });
        if (!await db.SubscriptionPlans.AnyAsync(p => p.Id == GroupPlacementDemoSeed.Id(42)))
        {
            var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
            var plan = new SubscriptionPlan { Id = GroupPlacementDemoSeed.Id(42), AcademyId = source.AcademyId, SportId = source.SportId, ArabicName = "تدريب خاص تجريبي — بلا دفع حقيقي", PlanType = SubscriptionPlanType.Duration, TrainingFormat = TrainingFormat.Private, DurationDays = 365, Price = 900, Currency = "EGP", AllBranches = false, Revision = 1, CreatedAtUtc = source.CreatedAtUtc, UpdatedAtUtc = source.CreatedAtUtc }; db.SubscriptionPlans.Add(plan);
            db.PlanBranchPublications.Add(new() { Id = GroupPlacementDemoSeed.Id(43), AcademyId = source.AcademyId, SubscriptionPlanId = plan.Id, BranchId = source.BranchId, CreatedAtUtc = source.CreatedAtUtc, UpdatedAtUtc = source.CreatedAtUtc });
            var version = PlanPriceVersion.Create(plan, [source.BranchId], actor, source.CreatedAtUtc); version.Id = GroupPlacementDemoSeed.Id(44); db.PlanPriceVersions.Add(version);
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
