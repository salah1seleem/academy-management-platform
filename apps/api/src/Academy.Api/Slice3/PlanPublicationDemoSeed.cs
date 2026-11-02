using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice3;

public static class PlanPublicationDemoSeed
{
    public static readonly Guid PlanId = Guid.Parse("a2030000-0000-0000-0000-000000000001");
    public static readonly Guid SeasonPlanId = Guid.Parse("a2030000-0000-0000-0000-000000000004");
    // Called only by the enabled LegacyRegression Demo profile. Never rewrites an edited record.
    public static async Task SeedAsync(FoundationDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await PlanCatalog.LockAsync(db, DemoSeed.NogoomAcademyId, PlanId);
        if (!await db.Branches.AnyAsync(x => x.Id == Slice2DemoSeed.FifthBranchId && x.AcademyId == DemoSeed.NogoomAcademyId && x.IsActive)
            || !await db.Sports.AnyAsync(x => x.Id == Slice2DemoSeed.SwimmingId && x.AcademyId == DemoSeed.NogoomAcademyId && x.IsActive)) return;
        var now = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
        var plan = new SubscriptionPlan { Id = PlanId, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "سباحة التجمع — 8 حصص / 30 يومًا", SportId = Slice2DemoSeed.SwimmingId, PlanType = SubscriptionPlanType.Combined, Price = 600, Currency = "EGP", DurationDays = 30, SessionCount = 8, AllBranches = false, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
        if (!await db.SubscriptionPlans.AnyAsync(x => x.Id == PlanId))
        {
            db.SubscriptionPlans.Add(plan);
            db.PlanBranchPublications.Add(new() { Id = Guid.Parse("a2030000-0000-0000-0000-000000000002"), AcademyId = plan.AcademyId, SubscriptionPlanId = plan.Id, BranchId = Slice2DemoSeed.FifthBranchId, CreatedAtUtc = now, UpdatedAtUtc = now });
            var version = PlanPriceVersion.Create(plan, [Slice2DemoSeed.FifthBranchId], null, now); version.Id = Guid.Parse("a2030000-0000-0000-0000-000000000003"); db.PlanPriceVersions.Add(version);
        }
        if (!await db.SubscriptionPlans.AnyAsync(x => x.Id == SeasonPlanId))
        {
            var season = new SubscriptionPlan { Id = SeasonPlanId, AcademyId = plan.AcademyId, ArabicName = "موسم السباحة الذهبي — خمسة أشهر بسعر أربعة", SportId = plan.SportId, PlanType = SubscriptionPlanType.Duration, DurationMonths = 5, TrainingFormat = TrainingFormat.Season, ReferenceMonthlyPrice = 600, ChargedMonths = 4, Price = 2400, Currency = "EGP", AllBranches = false, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.SubscriptionPlans.Add(season);
            db.PlanBranchPublications.Add(new() { Id = Guid.Parse("a2030000-0000-0000-0000-000000000005"), AcademyId = season.AcademyId, SubscriptionPlanId = season.Id, BranchId = Slice2DemoSeed.FifthBranchId, CreatedAtUtc = now, UpdatedAtUtc = now });
            var version = PlanPriceVersion.Create(season, [Slice2DemoSeed.FifthBranchId], null, now); version.Id = Guid.Parse("a2030000-0000-0000-0000-000000000006"); db.PlanPriceVersions.Add(version);
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
