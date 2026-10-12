using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

// Explicit Demo/LegacyRegression opt-in only: a synthetic discrepancy with no
// real webhook, collection, credit, receipt, activation or external communication.
public static class ProviderQuarantineDemoSeed
{
    public static Guid Id(int n) => Guid.Parse($"85000010-0000-0000-0000-{n:D12}");
    public static async Task Seed(FoundationDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"v2-demo-provider-quarantine"},0))");
        if (await db.PaymentRequests.AnyAsync(p => p.Id == Id(2))) return;
        var academy = DemoSeed.NogoomAcademyId;
        var enrollment = await db.SportEnrollments.SingleOrDefaultAsync(e => e.AcademyId == academy && e.PlayerId == Slice2DemoSeed.OmarPlayerId && e.SportId == Slice2DemoSeed.FootballId);
        var plan = await db.SubscriptionPlans.SingleOrDefaultAsync(p => p.Id == Slice3DemoSeed.FootballMonthlyId && p.AcademyId == academy);
        if (enrollment is null || plan is null) return;
        var user = await db.AcademyMemberships.Where(m => m.AcademyId == academy && m.User.Email == DemoSeed.OwnerEmail).Select(m => m.UserId).SingleAsync();
        var at = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
        var renewal = new RenewalRequest { Id = Id(1), AcademyId = academy, SportEnrollmentId = enrollment.Id, SportId = enrollment.SportId, SubscriptionPlanId = plan.Id,
            RequestedByUserId = user, RequestedAtUtc = at, OriginalAmount = 900m, FinalAmount = 900m, AmountExpected = 900m, Currency = "EGP",
            Status = RenewalRequestStatus.PaymentInProgress, PaymentRequestId = Id(2), IdempotencyKey = "v2-demo-provider-quarantine", CreatedAtUtc = at, UpdatedAtUtc = at };
        PlanTerms.From(plan).Capture(renewal); db.RenewalRequests.Add(renewal);
        db.PaymentRequests.Add(new() { Id = Id(2), AcademyId = academy, RenewalRequestId = renewal.Id, Provider = InternalTestPaymentGateway.ProviderName, ProviderEnvironment = "Demo",
            Amount = 900m, Currency = "EGP", Status = PaymentRequestStatus.Pending, ProviderReference = "demo-quarantine-request", CheckoutReference = "demo-not-live", CreatedAtUtc = at, UpdatedAtUtc = at });
        await db.SaveChangesAsync();
        var envelope = new ProviderEventEnvelope(academy, Id(2), "demo-quarantine-request", "demo-quarantine-event", "succeeded", 925m, "EGP", "");
        db.ProviderEventQuarantines.Add(new() { Id = Id(3), AcademyId = academy, PaymentRequestId = Id(2), BranchId = enrollment.BranchId,
            Provider = InternalTestPaymentGateway.ProviderName, ProviderEventId = envelope.ProviderEventId, EnvelopeHash = ProviderEventIdentity.Hash(envelope), ReasonCode = "amount-mismatch", ReceivedStatus = envelope.Status,
            ReceivedReference = envelope.ProviderReference, ExpectedReference = envelope.ProviderReference, ReceivedAmount = 925m, ExpectedAmount = 900m,
            ReceivedCurrency = "EGP", ExpectedCurrency = "EGP", CreatedAtUtc = at, UpdatedAtUtc = at });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
