using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice3;

public sealed record PaymentProcessingResult(bool Accepted, bool Replay, string State, Guid? ReceiptId = null);

public sealed class PaymentProcessor(FoundationDbContext db, IPaymentGateway gateway, ISubscriptionClock clock)
{
    public async Task<PaymentProcessingResult> ProcessAsync(ProviderEventEnvelope value, CancellationToken ct = default)
    {
        bool authentic;
        try { authentic = gateway.VerifyEvent(value); } catch (FormatException) { authentic = false; }
        if (!authentic) return new(false, false, "invalid-signature");

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var prior = await db.PaymentProviderEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Provider == gateway.Name && x.ProviderEventId == value.ProviderEventId, ct);
        if (prior is not null) return new(true, true, prior.ProcessingResult);

        var payment = await db.PaymentRequests.Include(x => x.RenewalRequest).ThenInclude(x => x.SportEnrollment).ThenInclude(x => x.Player)
            .Include(x => x.RenewalRequest).ThenInclude(x => x.SubscriptionPlan).ThenInclude(x => x.Sport)
            .SingleOrDefaultAsync(x => x.Id == value.PaymentRequestId && x.AcademyId == value.AcademyId, ct);
        if (payment is null || payment.Provider != gateway.Name || payment.ProviderReference != value.ProviderReference || payment.Amount != value.Amount || payment.Currency != value.Currency)
            return new(false, false, "mismatch");

        var outcome = value.Status switch { "succeeded" => PaymentEventOutcome.Success, "failed" => PaymentEventOutcome.Failed, "cancelled" => PaymentEventOutcome.Cancelled, _ => (PaymentEventOutcome?)null };
        if (outcome is null) return new(false, false, "unsupported-status");
        var providerEvent = new PaymentProviderEvent
        {
            Id = Guid.NewGuid(), AcademyId = payment.AcademyId, PaymentRequestId = payment.Id, Provider = gateway.Name,
            ProviderEventId = value.ProviderEventId, EventType = "payment.status", RawStatus = value.Status, Outcome = outcome.Value,
            Amount = value.Amount, Currency = value.Currency, ReceivedAtUtc = clock.UtcNow, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow,
            ProcessingResult = "processing"
        };
        db.PaymentProviderEvents.Add(providerEvent);

        if (payment.Status == PaymentRequestStatus.Confirmed)
        {
            providerEvent.ProcessedAtUtc = clock.UtcNow; providerEvent.ProcessingResult = "already-confirmed";
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            var existingReceipt = await db.Receipts.Where(x => x.AcademyId == payment.AcademyId && x.Collection.PaymentRequestId == payment.Id).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            return new(true, true, "already-confirmed", existingReceipt);
        }

        payment.LastProviderEventAtUtc = clock.UtcNow;
        payment.UpdatedAtUtc = clock.UtcNow;
        if (outcome != PaymentEventOutcome.Success)
        {
            payment.Status = outcome == PaymentEventOutcome.Failed ? PaymentRequestStatus.Failed : PaymentRequestStatus.Cancelled;
            payment.RenewalRequest.Status = outcome == PaymentEventOutcome.Failed ? RenewalRequestStatus.Failed : RenewalRequestStatus.Cancelled;
            payment.RenewalRequest.UpdatedAtUtc = clock.UtcNow;
            providerEvent.ProcessedAtUtc = clock.UtcNow; providerEvent.ProcessingResult = payment.Status.ToString().ToLowerInvariant();
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return new(true, false, providerEvent.ProcessingResult);
        }

        payment.Status = PaymentRequestStatus.Confirmed; payment.ConfirmedAtUtc = clock.UtcNow;
        payment.RenewalRequest.Status = RenewalRequestStatus.Paid; payment.RenewalRequest.UpdatedAtUtc = clock.UtcNow;
        var collection = new PaymentCollection
        {
            Id = Guid.NewGuid(), AcademyId = payment.AcademyId, SportEnrollmentId = payment.RenewalRequest.SportEnrollmentId,
            RenewalRequestId = payment.RenewalRequestId, PaymentRequestId = payment.Id, Amount = payment.Amount, Currency = payment.Currency,
            PaymentMethod = "Online", Provider = payment.Provider, ProviderReference = payment.ProviderReference, ConfirmedAtUtc = clock.UtcNow,
            ConfirmedBy = "system/provider", CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        db.Collections.Add(collection);
        var plan = payment.RenewalRequest.SubscriptionPlan;
        var latestEnd = await db.SubscriptionPeriods.Where(x => x.AcademyId == payment.AcademyId && x.SportEnrollmentId == payment.RenewalRequest.SportEnrollmentId && x.Status != SubscriptionPeriodStatus.Cancelled && x.EndDate != null)
            .MaxAsync(x => x.EndDate, ct);
        var start = latestEnd.HasValue && latestEnd.Value >= clock.Today ? latestEnd.Value.AddDays(1) : clock.Today;
        var end = plan.DurationDays is int days ? start.AddDays(days - 1) : (DateOnly?)null;
        var period = new SubscriptionPeriod
        {
            Id = Guid.NewGuid(), AcademyId = payment.AcademyId, SportEnrollmentId = payment.RenewalRequest.SportEnrollmentId, SportId = payment.RenewalRequest.SportId,
            SubscriptionPlanId = plan.Id, CollectionId = collection.Id, StartDate = start, EndDate = end, InitialSessions = plan.SessionCount,
            RemainingSessions = plan.SessionCount, Status = start > clock.Today ? SubscriptionPeriodStatus.Scheduled : SubscriptionPeriodStatus.Active,
            PriceSnapshot = payment.Amount, CurrencySnapshot = payment.Currency, CreatedByUserId = payment.RenewalRequest.RequestedByUserId,
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        db.SubscriptionPeriods.Add(period);
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(), AcademyId = payment.AcademyId, CollectionId = collection.Id,
            ReceiptNumber = $"RC-{clock.UtcNow:yyyyMMdd}-{collection.Id.ToString("N")[..8].ToUpperInvariant()}",
            PlayerId = payment.RenewalRequest.SportEnrollment.PlayerId, SportEnrollmentId = payment.RenewalRequest.SportEnrollmentId, SubscriptionPlanId = plan.Id,
            PlayerNameSnapshot = payment.RenewalRequest.SportEnrollment.Player.ArabicName, SportNameSnapshot = plan.Sport.ArabicName,
            PlanNameSnapshot = plan.ArabicName, Amount = payment.Amount, Currency = payment.Currency, PaidAtUtc = clock.UtcNow,
            PaymentMethod = "الدفع الإلكتروني", ProviderReference = payment.ProviderReference, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        db.Receipts.Add(receipt);
        providerEvent.ProcessedAtUtc = clock.UtcNow; providerEvent.ProcessingResult = "confirmed";
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(true, false, "confirmed", receipt.Id);
    }
}
