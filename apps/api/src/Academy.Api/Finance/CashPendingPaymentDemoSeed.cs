using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

// Explicit Demo/LegacyRegression cash fixture only. Never enables cash, opens a
// shift, confirms payment, resets data or overwrites a reviewed example.
public static class CashPendingPaymentDemoSeed
{
    public static Guid Id(int n) => Guid.Parse($"84000010-0000-0000-0000-{n:D12}");
    public static async Task Seed(FoundationDbContext db, DiscountRules discounts)
    {
        var academy = DemoSeed.NogoomAcademyId;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"v2-demo-pending-cash"},0))");
        if (await db.Players.AnyAsync(p => p.Id == Id(2))) return;
        var group = await db.TrainingGroups.SingleOrDefaultAsync(g => g.AcademyId == academy && g.Id == Slice2DemoSeed.FootballGroupId);
        var plan = await db.SubscriptionPlans.SingleOrDefaultAsync(p => p.AcademyId == academy && p.Id == Slice3DemoSeed.FootballMonthlyId);
        if (group is null || plan is null) return;
        var owner = await db.AcademyMemberships.Where(m => m.AcademyId == academy && m.User.Email == DemoSeed.OwnerEmail).Select(m => m.UserId).SingleAsync();
        var guardian = await db.GuardianProfiles.Where(g => g.AcademyId == academy && g.User.PhoneNumber == DemoSeed.GuardianPhone).Select(g => g.Id).SingleAsync();
        var at = new DateTimeOffset(2026, 9, 28, 6, 0, 0, TimeSpan.Zero);
        db.TrainingGroups.Add(new() { Id = Id(1), AcademyId = academy, ArabicName = "مراجعة طلب النقدي — مجموعة اصطناعية", BranchId = group.BranchId, SportId = group.SportId, AgeCategoryId = group.AgeCategoryId, Capacity = 2, CreatedAtUtc = at, UpdatedAtUtc = at });
        db.Players.Add(new() { Id = Id(2), AcademyId = academy, PlayerCode = "DEMO-PENDING-CASH", ArabicName = "لاعب طلب نقدي اصطناعي", DateOfBirth = new(2017, 1, 1), CreatedAtUtc = at, UpdatedAtUtc = at });
        db.SportEnrollments.Add(new() { Id = Id(3), AcademyId = academy, PlayerId = Id(2), BranchId = group.BranchId, SportId = group.SportId, TrainingGroupId = Id(1), CreatedAtUtc = at, UpdatedAtUtc = at });
        db.GuardianPlayerLinks.Add(new() { Id = Id(4), AcademyId = academy, GuardianId = guardian, PlayerId = Id(2), CreatedByUserId = owner, CreatedAtUtc = at, UpdatedAtUtc = at });
        await db.SaveChangesAsync();
        var price = await discounts.CalculateAsync(academy, Id(2), plan.Price, owner, plan: plan, branchId: group.BranchId);
        var renewal = new RenewalRequest { Id = Id(5), AcademyId = academy, SportEnrollmentId = Id(3), SportId = group.SportId, SubscriptionPlanId = plan.Id,
            RequestedByUserId = owner, RequestedAtUtc = at, OriginalAmount = price.OriginalAmount, DiscountType = price.DiscountType, DiscountValue = price.DiscountValue,
            DiscountAmount = price.DiscountAmount, DiscountReason = price.DiscountReason, DiscountAppliedByUserId = price.DiscountAppliedByUserId,
            DiscountAppliedAtUtc = price.DiscountAppliedAtUtc, DiscountRuleIdSnapshot = price.DiscountRuleId, DiscountRuleRevisionSnapshot = price.DiscountRuleRevision,
            FinalAmount = price.FinalAmount, AmountExpected = price.FinalAmount, Currency = plan.Currency, Status = RenewalRequestStatus.PaymentInProgress,
            PaymentRequestId = Id(6), IdempotencyKey = "v2-demo-pending-cash", CreatedAtUtc = at, UpdatedAtUtc = at };
        PlanTerms.From(plan).Capture(renewal); db.RenewalRequests.Add(renewal);
        db.PaymentRequests.Add(new() { Id = Id(6), AcademyId = academy, RenewalRequestId = Id(5), Provider = "Manual", ProviderEnvironment = "Manual", Amount = price.FinalAmount,
            Currency = plan.Currency, Status = PaymentRequestStatus.Pending, ProviderReference = "demo-pending-cash", CheckoutReference = "demo-pending-cash", CreatedAtUtc = at, UpdatedAtUtc = at });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
