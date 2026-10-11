using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice3;

public static class PlanPublicationDemoSeed
{
    public static readonly Guid PlanId = Guid.Parse("a2030000-0000-0000-0000-000000000001");
    // Called only by the enabled LegacyRegression Demo profile. Never rewrites an edited record.
    public static async Task SeedAsync(FoundationDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await PlanCatalog.LockAsync(db, DemoSeed.NogoomAcademyId, PlanId);
        if (await db.SubscriptionPlans.AnyAsync(x => x.Id == PlanId)) return;
        if (!await db.Branches.AnyAsync(x => x.Id == Slice2DemoSeed.FifthBranchId && x.AcademyId == DemoSeed.NogoomAcademyId && x.IsActive)
            || !await db.Sports.AnyAsync(x => x.Id == Slice2DemoSeed.SwimmingId && x.AcademyId == DemoSeed.NogoomAcademyId && x.IsActive)) return;
        var now = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
        var plan = new SubscriptionPlan { Id = PlanId, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "سباحة التجمع — 8 حصص / 30 يومًا", SportId = Slice2DemoSeed.SwimmingId, PlanType = SubscriptionPlanType.Combined, Price = 600, Currency = "EGP", DurationDays = 30, SessionCount = 8, AllBranches = false, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.SubscriptionPlans.Add(plan);
        db.PlanBranchPublications.Add(new() { Id = Guid.Parse("a2030000-0000-0000-0000-000000000002"), AcademyId = plan.AcademyId, SubscriptionPlanId = plan.Id, BranchId = Slice2DemoSeed.FifthBranchId, CreatedAtUtc = now, UpdatedAtUtc = now });
        var version = PlanPriceVersion.Create(plan, [Slice2DemoSeed.FifthBranchId], null, now); version.Id = Guid.Parse("a2030000-0000-0000-0000-000000000003"); db.PlanPriceVersions.Add(version);
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
