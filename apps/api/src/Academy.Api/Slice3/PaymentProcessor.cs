using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice3;

public sealed record PaymentProcessingResult(bool Accepted, bool Replay, string State, Guid? ReceiptId = null);

public sealed class PaymentProcessor(FoundationDbContext db, IPaymentGateway gateway, ISubscriptionClock clock, TimeProvider time)
{
    public async Task<PaymentProcessingResult> ProcessAsync(ProviderEventEnvelope value, CancellationToken ct = default)
    {
        if (!ProviderEventIdentity.ValidShape(value)) return new(false, false, "invalid-envelope");
        bool authentic;
        try { authentic = gateway.VerifyEvent(value); } catch (FormatException) { authentic = false; }
        if (!authentic) return new(false, false, "invalid-signature");
        if (db.RequestScopeEnabled && db.RequestAcademyId != value.AcademyId) return new(false, false, "mismatch");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Provider event IDs are globally unique in the existing ledger. Lock the
        // same global key, then the exact tenant payment, before inspecting replay.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"provider-event:{gateway.Name}:{value.ProviderEventId}"},0))", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"PaymentRequests\" WHERE \"AcademyId\"={value.AcademyId} AND \"Id\"={value.PaymentRequestId} FOR UPDATE", ct);
        var payment = await db.PaymentRequests.Include(x => x.RenewalRequest).ThenInclude(x => x.SportEnrollment).ThenInclude(x => x.Player)
            .Include(x => x.RenewalRequest).ThenInclude(x => x.SubscriptionPlan).ThenInclude(x => x.Sport)
            .SingleOrDefaultAsync(x => x.Id == value.PaymentRequestId && x.AcademyId == value.AcademyId, ct);
        if (payment is null) return new(false, false, "mismatch");
        var hash = ProviderEventIdentity.Hash(value);
        var prior = await db.PaymentProviderEvents.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Provider == gateway.Name && x.ProviderEventId == value.ProviderEventId, ct);
        if (prior is not null)
        {
            var same = prior.AcademyId == value.AcademyId && prior.PaymentRequestId == value.PaymentRequestId &&
                (prior.EnvelopeHash is not null ? prior.EnvelopeHash == hash : prior.RawStatus == value.Status && prior.Amount == value.Amount && prior.Currency == value.Currency && payment.ProviderReference == value.ProviderReference);
            if (!same) return await Quarantine("event-reuse");
            var accepted = prior.ProcessingResult is "confirmed" or "already-confirmed" or "failed" or "cancelled" || prior.ProcessingResult.StartsWith("terminal-", StringComparison.Ordinal);
            return new(accepted, true, prior.ProcessingResult);
        }
        var quarantined = await db.ProviderEventQuarantines.AsNoTracking().SingleOrDefaultAsync(q => q.AcademyId == value.AcademyId && q.Provider == gateway.Name && q.ProviderEventId == value.ProviderEventId && q.EnvelopeHash == hash, ct);
        if (quarantined is not null) return new(false, true, "quarantined");
        var mismatch = payment.Provider != gateway.Name ? "provider-mismatch" :
            payment.ProviderReference != value.ProviderReference ? "reference-mismatch" :
            payment.Id != payment.RenewalRequest.PaymentRequestId || payment.Amount != payment.RenewalRequest.FinalAmount || payment.Currency != payment.RenewalRequest.Currency ? "request-terms-mismatch" :
            payment.Amount != value.Amount ? "amount-mismatch" : payment.Currency != value.Currency ? "currency-mismatch" : null;
        if (mismatch is not null) return await Quarantine(mismatch);

        var outcome = value.Status switch { "succeeded" => PaymentEventOutcome.Success, "failed" => PaymentEventOutcome.Failed, "cancelled" => PaymentEventOutcome.Cancelled, _ => (PaymentEventOutcome?)null };
        if (outcome is null) return await Quarantine("unsupported-status");
        var providerEvent = new PaymentProviderEvent
        {
            Id = Guid.NewGuid(), AcademyId = payment.AcademyId, PaymentRequestId = payment.Id, Provider = gateway.Name,
            ProviderEventId = value.ProviderEventId, EventType = "payment.status", RawStatus = value.Status, Outcome = outcome.Value,
            Amount = value.Amount, Currency = value.Currency, ReceivedAtUtc = clock.UtcNow, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow,
            ProcessingResult = "processing", EnvelopeHash = hash
        };
        db.PaymentProviderEvents.Add(providerEvent);

        if (payment.Status == PaymentRequestStatus.Confirmed)
        {
            providerEvent.ProcessedAtUtc = clock.UtcNow; providerEvent.ProcessingResult = "already-confirmed";
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            var existingReceipt = await db.Receipts.Where(x => x.AcademyId == payment.AcademyId && x.Collection.PaymentRequestId == payment.Id).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            return new(true, true, "already-confirmed", existingReceipt);
        }

        if (payment.Status is PaymentRequestStatus.Failed or PaymentRequestStatus.Cancelled or PaymentRequestStatus.Expired)
        {
            providerEvent.ProcessedAtUtc = clock.UtcNow;
            providerEvent.ProcessingResult = $"terminal-{payment.Status.ToString().ToLowerInvariant()}-ignored";
            payment.LastProviderEventAtUtc = clock.UtcNow;
            payment.UpdatedAtUtc = clock.UtcNow;
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return new(true, false, providerEvent.ProcessingResult);
        }

        if (payment.Status != PaymentRequestStatus.Pending)
        {
            providerEvent.ProcessedAtUtc = clock.UtcNow; providerEvent.ProcessingResult = "invalid-transition";
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return new(false, false, providerEvent.ProcessingResult);
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

        if (!PlanCatalog.FormatMatches(payment.RenewalRequest.SportEnrollment.RegistrationFormat, PlanTerms.From(payment.RenewalRequest, payment.RenewalRequest.SubscriptionPlan).Format))
        {
            db.Entry(providerEvent).State = EntityState.Detached;
            return await Quarantine("registration-format-conflict");
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SportEnrollments\" WHERE \"AcademyId\"={payment.AcademyId} AND \"Id\"={payment.RenewalRequest.SportEnrollmentId} FOR UPDATE", ct);
        payment.Status = PaymentRequestStatus.Confirmed; payment.ConfirmedAtUtc = clock.UtcNow;
        payment.RenewalRequest.Status = RenewalRequestStatus.Paid; payment.RenewalRequest.UpdatedAtUtc = clock.UtcNow;
        var collection = new PaymentCollection
        {
            Id = Guid.NewGuid(), AcademyId = payment.AcademyId, SportEnrollmentId = payment.RenewalRequest.SportEnrollmentId,
            RenewalRequestId = payment.RenewalRequestId, PaymentRequestId = payment.Id, Amount = payment.Amount, Currency = payment.Currency,
            PaymentMethod = "Online", Provider = payment.Provider, ProviderReference = payment.ProviderReference, ConfirmedAtUtc = clock.UtcNow,
            ConfirmedBy = "system/provider", CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        await Academy.Api.Operations.VipRewards.Consume(db,payment.RenewalRequest,clock.UtcNow);
        db.Collections.Add(collection);
        var plan = payment.RenewalRequest.SubscriptionPlan;
        var terms = PlanTerms.From(payment.RenewalRequest, plan);
        var latestEnd = await db.SubscriptionPeriods.Where(x => x.AcademyId == payment.AcademyId && x.SportEnrollmentId == payment.RenewalRequest.SportEnrollmentId && x.Status != SubscriptionPeriodStatus.Cancelled && x.EndDate != null)
            .MaxAsync(x => x.EndDate, ct);
        var start = latestEnd.HasValue && latestEnd.Value >= clock.Today ? latestEnd.Value.AddDays(1) : clock.Today;
        var end = terms.EndDate(start);
        var period = new SubscriptionPeriod
        {
            Id = Guid.NewGuid(), AcademyId = payment.AcademyId, SportEnrollmentId = payment.RenewalRequest.SportEnrollmentId, SportId = payment.RenewalRequest.SportId,
            SubscriptionPlanId = plan.Id, CollectionId = collection.Id, StartDate = start, EndDate = end, InitialSessions = terms.Sessions,
            RemainingSessions = terms.Sessions, Status = start > clock.Today ? SubscriptionPeriodStatus.Scheduled : SubscriptionPeriodStatus.Active,
            PriceSnapshot = payment.Amount, CurrencySnapshot = payment.Currency, CreatedByUserId = payment.RenewalRequest.RequestedByUserId,
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        terms.Capture(period); db.SubscriptionPeriods.Add(period);
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(), AcademyId = payment.AcademyId, CollectionId = collection.Id,
            ReceiptNumber = $"RC-{clock.UtcNow:yyyyMMdd}-{collection.Id.ToString("N")[..8].ToUpperInvariant()}",
            PlayerId = payment.RenewalRequest.SportEnrollment.PlayerId, SportEnrollmentId = payment.RenewalRequest.SportEnrollmentId, SubscriptionPlanId = plan.Id,
            PlayerNameSnapshot = payment.RenewalRequest.SportEnrollment.Player.ArabicName, SportNameSnapshot = plan.Sport.ArabicName,
            PlanNameSnapshot = terms.Name, OriginalAmount = payment.RenewalRequest.OriginalAmount,
            DiscountType = payment.RenewalRequest.DiscountType, DiscountValue = payment.RenewalRequest.DiscountValue,
            DiscountAmount = payment.RenewalRequest.DiscountAmount, FinalAmount = payment.RenewalRequest.FinalAmount,
            DiscountRuleIdSnapshot = payment.RenewalRequest.DiscountRuleIdSnapshot,
            DiscountRuleRevisionSnapshot = payment.RenewalRequest.DiscountRuleRevisionSnapshot,
            DiscountReason = payment.RenewalRequest.DiscountReason,
            Amount = payment.Amount, Currency = payment.Currency, PaidAtUtc = clock.UtcNow,
            PaymentMethod = "الدفع الإلكتروني", ProviderReference = payment.ProviderReference, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        db.Receipts.Add(receipt);
        providerEvent.ProcessedAtUtc = clock.UtcNow; providerEvent.ProcessingResult = "confirmed";
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(true, false, "confirmed", receipt.Id);

        async Task<PaymentProcessingResult> Quarantine(string reason)
        {
            var existing = await db.ProviderEventQuarantines.AsNoTracking().AnyAsync(q => q.AcademyId == payment.AcademyId && q.Provider == gateway.Name && q.ProviderEventId == value.ProviderEventId && q.EnvelopeHash == hash, ct);
            if (existing) return new(false, true, "quarantined");
            var at = time.GetUtcNow();
            db.ProviderEventQuarantines.Add(new() {
                Id = Guid.NewGuid(), AcademyId = payment.AcademyId, PaymentRequestId = payment.Id, BranchId = payment.RenewalRequest.SportEnrollment.BranchId,
                Provider = gateway.Name, ProviderEventId = value.ProviderEventId, EnvelopeHash = hash, ReasonCode = reason, ReceivedStatus = value.Status,
                ReceivedReference = value.ProviderReference, ExpectedReference = payment.ProviderReference, ReceivedAmount = value.Amount, ExpectedAmount = payment.RenewalRequest.FinalAmount,
                ReceivedCurrency = value.Currency, ExpectedCurrency = payment.RenewalRequest.Currency, CreatedAtUtc = at, UpdatedAtUtc = at
            });
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return new(false, false, "quarantined");
        }
    }
}
