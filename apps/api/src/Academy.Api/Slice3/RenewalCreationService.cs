using System.Data;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice3;

public enum RenewalCreationState { Created, Replay, NotFound, OpenRenewalExists }

public sealed record RenewalCreationResult(
    RenewalCreationState State,
    Guid? RenewalId = null,
    Guid? PaymentId = null);

public sealed record RenewalFinancialSnapshot(
    decimal OriginalAmount,
    RenewalDiscountType? DiscountType,
    decimal? DiscountValue,
    decimal DiscountAmount,
    decimal FinalAmount,
    string? DiscountReason,
    Guid? DiscountAppliedByUserId,
    DateTimeOffset? DiscountAppliedAtUtc);

public sealed class RenewalCreationService(
    FoundationDbContext db,
    IPaymentGateway gateway,
    ISubscriptionClock clock)
{
    public async Task<RenewalCreationResult> CreateAsync(
        Guid academyId,
        Guid actorUserId,
        Guid enrollmentId,
        Guid planId,
        string idempotencyKey,
        bool preventParallelOpenRenewal,
        RenewalFinancialSnapshot? financialSnapshot = null,
        bool allowInactivePlan = false,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // Transaction-scoped locks make the idempotency replay and the one-open-renewal rule
        // deterministic without adding provider- or demo-specific schema.
        var requestScope = $"renewal-request:{academyId:N}:{actorUserId:N}:{idempotencyKey}";
        var enrollmentScope = $"renewal-enrollment:{academyId:N}:{enrollmentId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({requestScope}, 0))", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({enrollmentScope}, 0))", ct);

        var replay = await db.RenewalRequests.AsNoTracking()
            .Where(x => x.AcademyId == academyId && x.RequestedByUserId == actorUserId && x.IdempotencyKey == idempotencyKey)
            .Select(x => new { RenewalId = x.Id, PaymentId = x.PaymentRequestId })
            .SingleOrDefaultAsync(ct);
        if (replay is not null)
            return new(RenewalCreationState.Replay, replay.RenewalId, replay.PaymentId);

        var enrollment = await db.SportEnrollments
            .SingleOrDefaultAsync(x => x.AcademyId == academyId && x.Id == enrollmentId && x.IsActive, ct);
        if (enrollment is null)
            return new(RenewalCreationState.NotFound);

        var plan = await db.SubscriptionPlans.SingleOrDefaultAsync(
            x => x.AcademyId == academyId && x.Id == planId && x.SportId == enrollment.SportId && (allowInactivePlan || x.IsActive), ct);
        if (plan is null)
            return new(RenewalCreationState.NotFound);

        if (preventParallelOpenRenewal)
        {
            var hasOpenRenewal = await db.RenewalRequests.AnyAsync(x =>
                x.AcademyId == academyId &&
                x.SportEnrollmentId == enrollmentId &&
                (x.Status == RenewalRequestStatus.PendingPayment || x.Status == RenewalRequestStatus.PaymentInProgress) &&
                x.PaymentRequestId != null &&
                db.PaymentRequests.Any(payment =>
                    payment.AcademyId == academyId &&
                    payment.Id == x.PaymentRequestId &&
                    (payment.Status == PaymentRequestStatus.Created || payment.Status == PaymentRequestStatus.Pending)), ct);
            if (hasOpenRenewal)
                return new(RenewalCreationState.OpenRenewalExists);
        }

        var originalAmount = financialSnapshot?.OriginalAmount ?? plan.Price;
        var finalAmount = financialSnapshot?.FinalAmount ?? originalAmount;
        var renewal = new RenewalRequest
        {
            Id = Guid.NewGuid(),
            AcademyId = academyId,
            SportEnrollmentId = enrollment.Id,
            SportId = enrollment.SportId,
            SubscriptionPlanId = plan.Id,
            RequestedByUserId = actorUserId,
            RequestedAtUtc = clock.UtcNow,
            OriginalAmount = originalAmount,
            DiscountType = financialSnapshot?.DiscountType,
            DiscountValue = financialSnapshot?.DiscountValue,
            DiscountAmount = financialSnapshot?.DiscountAmount ?? 0m,
            FinalAmount = finalAmount,
            DiscountReason = financialSnapshot?.DiscountReason,
            DiscountAppliedByUserId = financialSnapshot?.DiscountAppliedByUserId,
            DiscountAppliedAtUtc = financialSnapshot?.DiscountAppliedAtUtc,
            AmountExpected = finalAmount,
            Currency = plan.Currency,
            Status = RenewalRequestStatus.PaymentInProgress,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow
        };
        var paymentId = Guid.NewGuid();
        var session = gateway.CreateCheckout(academyId, paymentId);
        var payment = new PaymentRequest
        {
            Id = paymentId,
            AcademyId = academyId,
            RenewalRequestId = renewal.Id,
            Provider = session.Provider,
            ProviderEnvironment = "Demo",
            Amount = finalAmount,
            Currency = plan.Currency,
            Status = PaymentRequestStatus.Pending,
            ProviderReference = session.ProviderReference,
            CheckoutReference = session.CheckoutReference,
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow
        };
        renewal.PaymentRequestId = payment.Id;
        db.RenewalRequests.Add(renewal);
        db.PaymentRequests.Add(payment);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(RenewalCreationState.Created, renewal.Id, payment.Id);
    }
}
