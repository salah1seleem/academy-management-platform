using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

// Enabled LegacyRegression Demo only. Historical receipts are explicitly synthetic, never live payments.
public static class RenewalOpportunityDemoSeed
{
    public static readonly Guid BranchId = Id(1, 1);
    public static Guid Id(int type, int index) => Guid.Parse($"a2030100-{type:0000}-0000-0000-{index:000000000000}");
    public static async Task SeedAsync(FoundationDbContext db)
    {
        if (!await db.Academies.AnyAsync(a => a.Id == DemoSeed.NogoomAcademyId)) return;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"v2-fin01-demo-seed"},0))");
        if (await db.Branches.AnyAsync(b => b.Id == BranchId)) return; // one atomic additive batch, never reset edits
        var academy = DemoSeed.NogoomAcademyId; var sport = Slice2DemoSeed.SwimmingId;
        var age = await db.AgeCategories.SingleAsync(a => a.Id == Slice2DemoSeed.U10Id && a.AcademyId == academy);
        var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
        var now = new DateTimeOffset(2026, 8, 29, 8, 0, 0, TimeSpan.Zero);
        db.Branches.Add(new Branch { Id = BranchId, AcademyId = academy, ArabicName = "فرع مثال فرص التجديد — صناعي", EnglishName = "V2FIN01DemoDatasetv1", CreatedAtUtc = now, UpdatedAtUtc = now });
        var group = new TrainingGroup { Id = Id(2, 1), AcademyId = academy, ArabicName = "مجموعة السباحة — مثال FIN-01", BranchId = BranchId, SportId = sport, AgeCategoryId = age.Id, Capacity = 30, CreatedAtUtc = now, UpdatedAtUtc = now }; db.TrainingGroups.Add(group);
        var plans = new[] { 600m, 700m, 800m }.Select((price, index) => new SubscriptionPlan { Id = Id(3, index + 1), AcademyId = academy, ArabicName = $"باقة مثال التجديد {index + 1}", SportId = sport, PlanType = SubscriptionPlanType.Duration, DurationDays = 30, Price = price, Currency = "EGP", AllBranches = false, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now }).ToArray();
        foreach (var plan in plans) { db.SubscriptionPlans.Add(plan); db.PlanBranchPublications.Add(new() { Id = Id(4, Array.IndexOf(plans, plan) + 1), AcademyId = academy, SubscriptionPlanId = plan.Id, BranchId = BranchId, CreatedAtUtc = now, UpdatedAtUtc = now }); db.PlanPriceVersions.Add(PlanPriceVersion.Create(plan, [BranchId], null, now)); }
        var rule = new DiscountRule { Id = Id(5, 1), AcademyId = academy, ArabicName = "عرض 10% — مثال فرص التجديد", Reason = "مثال صناعي محدد الباقة والفرع", Type = RenewalDiscountType.Percentage, Percentage = 10, Currency = "EGP", BranchId = BranchId, SportId = sport, SubscriptionPlanId = plans[0].Id, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.DiscountRules.Add(rule); db.DiscountRuleVersions.Add(DiscountRuleVersion.Create(rule, null, now));
        for (var i = 1; i <= 20; i++)
        {
            var plan = plans[i <= 13 ? 0 : i <= 17 ? 1 : 2]; var discount = i is >= 9 and <= 13 ? 60m : 0m; var amount = plan.Price - discount;
            var player = new Player { Id = Id(6, i), AcademyId = academy, ArabicName = $"لاعب مثال التجديد {i:00}", PlayerCode = $"DEMO-FIN01-{i:00}", DateOfBirth = new(2017, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now };
            var enrollment = new SportEnrollment { Id = Id(7, i), AcademyId = academy, PlayerId = player.Id, BranchId = BranchId, SportId = sport, TrainingGroupId = group.Id, Status = EnrollmentStatus.Active, CreatedAtUtc = now, UpdatedAtUtc = now };
            var renewal = new RenewalRequest { Id = Id(8, i), AcademyId = academy, SportEnrollmentId = enrollment.Id, SportId = sport, SubscriptionPlanId = plan.Id, RequestedByUserId = actor, RequestedAtUtc = now, OriginalAmount = plan.Price, DiscountType = discount == 0 ? null : RenewalDiscountType.Percentage, DiscountValue = discount == 0 ? null : 10, DiscountAmount = discount, FinalAmount = amount, AmountExpected = amount, DiscountReason = discount == 0 ? null : rule.Reason, DiscountRuleIdSnapshot = discount == 0 ? null : rule.Id, DiscountRuleRevisionSnapshot = discount == 0 ? null : 1, Currency = "EGP", Status = RenewalRequestStatus.Paid, IdempotencyKey = $"v2-fin01-demo-{i}", CreatedAtUtc = now, UpdatedAtUtc = now }; PlanTerms.From(plan).Capture(renewal);
            var payment = new PaymentRequest { Id = Id(9, i), AcademyId = academy, RenewalRequestId = renewal.Id, Amount = amount, Currency = "EGP", Provider = "InternalTest", ProviderEnvironment = "Demo", ProviderReference = $"v2-fin01-demo-{i}", CheckoutReference = $"v2-fin01-demo-{i}", Status = PaymentRequestStatus.Confirmed, ConfirmedAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now }; renewal.PaymentRequestId = payment.Id;
            var collection = new PaymentCollection { Id = Id(10, i), AcademyId = academy, SportEnrollmentId = enrollment.Id, RenewalRequestId = renewal.Id, PaymentRequestId = payment.Id, Amount = amount, Currency = "EGP", PaymentMethod = "InternalTest", Provider = "InternalTest", ProviderReference = payment.ProviderReference, ConfirmedBy = "synthetic-demo-fin01", ConfirmedAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now };
            var period = new SubscriptionPeriod { Id = Id(11, i), AcademyId = academy, SportEnrollmentId = enrollment.Id, SportId = sport, SubscriptionPlanId = plan.Id, CollectionId = collection.Id, StartDate = new(2026, 8, 29), EndDate = new(2026, 9, 27), Status = SubscriptionPeriodStatus.Expired, PriceSnapshot = amount, CurrencySnapshot = "EGP", CreatedByUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now }; PlanTerms.From(plan).Capture(period);
            var receipt = new Receipt { Id = Id(12, i), AcademyId = academy, CollectionId = collection.Id, ReceiptNumber = $"DEMO-FIN01-{i:00}", PlayerId = player.Id, SportEnrollmentId = enrollment.Id, SubscriptionPlanId = plan.Id, PlayerNameSnapshot = player.ArabicName, SportNameSnapshot = "السباحة", PlanNameSnapshot = plan.ArabicName, OriginalAmount = plan.Price, DiscountType = renewal.DiscountType, DiscountValue = renewal.DiscountValue, DiscountAmount = discount, FinalAmount = amount, Amount = amount, Currency = "EGP", PaidAtUtc = now, PaymentMethod = collection.PaymentMethod, ProviderReference = collection.ProviderReference, DiscountReason = renewal.DiscountReason, DiscountRuleIdSnapshot = renewal.DiscountRuleIdSnapshot, DiscountRuleRevisionSnapshot = renewal.DiscountRuleRevisionSnapshot, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.Players.Add(player); db.SportEnrollments.Add(enrollment); db.RenewalRequests.Add(renewal); db.PaymentRequests.Add(payment); db.Collections.Add(collection); db.SubscriptionPeriods.Add(period); db.Receipts.Add(receipt);
            if (discount > 0) db.PlayerDiscountLinks.Add(new() { Id = Id(13, i), AcademyId = academy, PlayerId = player.Id, DiscountRuleId = rule.Id, CreatedAtUtc = now, UpdatedAtUtc = now });
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
