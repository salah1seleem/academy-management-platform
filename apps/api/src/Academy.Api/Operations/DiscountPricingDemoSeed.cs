using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class DiscountPricingDemoSeed
{
    public static readonly Guid FixedId = Guid.Parse("a2030000-0000-0000-0000-000000000101");
    public static async Task SeedAsync(FoundationDbContext db)
    {
        // Additive only, invoked exclusively by enabled LegacyRegression Demo startup.
        if (!await db.SubscriptionPlans.AnyAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Id == PlanPublicationDemoSeed.PlanId)) return;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"v2-discount-demo"}, 0))");
        if (await db.DiscountRules.AnyAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Id == FixedId)) return;
        var now = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
        var rule = new DiscountRule { Id = FixedId, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "عرض مدرسة تجريبية — سباحة التجمع", Reason = "", Type = RenewalDiscountType.FixedAmount, FixedAmount = 100, MaximumAmount = 100, Currency = "EGP", BranchId = Slice2DemoSeed.FifthBranchId, SportId = Slice2DemoSeed.SwimmingId, SubscriptionPlanId = PlanPublicationDemoSeed.PlanId, StartsOn = new(2026, 9, 1), EndsOn = new(2026, 12, 31), Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.DiscountRules.Add(rule); var version = DiscountRuleVersion.Create(rule, null, now); version.Id = Guid.Parse("a2030000-0000-0000-0000-000000000102"); db.DiscountRuleVersions.Add(version);
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
