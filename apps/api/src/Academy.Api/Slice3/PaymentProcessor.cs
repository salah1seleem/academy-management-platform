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
        // Callbacks have no member session. The accepted payment supplies the academy.
        var zone = await db.Academies.Where(a => a.Id == payment.AcademyId).Select(a => a.TimeZone).SingleAsync(ct);
        var paidAt = clock.UtcNow;
        var today = clock.DateAt(paidAt, zone);
        payment.Status = PaymentRequestStatus.Confirmed; payment.ConfirmedAtUtc = paidAt;
        payment.RenewalRequest.Status = RenewalRequestStatus.Paid; payment.RenewalRequest.UpdatedAtUtc = paidAt;
        var (collection, receipt) = SubscriptionCollectionPosting.Create(payment, payment.RenewalRequest,
            payment.RenewalRequest.SportEnrollment.PlayerId, payment.RenewalRequest.SportEnrollment.Player.ArabicName,
            payment.RenewalRequest.SubscriptionPlan.Sport.ArabicName, PlanTerms.From(payment.RenewalRequest, payment.RenewalRequest.SubscriptionPlan).Name,
            "Online", "الدفع الإلكتروني", paidAt, paidAt, "system/provider", receiptDay: today);
        await Academy.Api.Operations.VipRewards.Consume(db,payment.RenewalRequest,paidAt);
        db.Collections.Add(collection);
        var plan = payment.RenewalRequest.SubscriptionPlan;
        var terms = PlanTerms.From(payment.RenewalRequest, plan);
        var start = await RenewalCreationService.PreviewStartAsync(db, payment.AcademyId, payment.RenewalRequest.SportEnrollmentId, today, ct);
        var end = terms.EndDate(start);
        var period = new SubscriptionPeriod
        {
            Id = Guid.NewGuid(), AcademyId = payment.AcademyId, SportEnrollmentId = payment.RenewalRequest.SportEnrollmentId, SportId = payment.RenewalRequest.SportId,
            SubscriptionPlanId = plan.Id, CollectionId = collection.Id, StartDate = start, EndDate = end, InitialSessions = terms.Sessions,
            RemainingSessions = terms.Sessions, Status = start > today ? SubscriptionPeriodStatus.Scheduled : SubscriptionPeriodStatus.Active,
            PriceSnapshot = payment.Amount, CurrencySnapshot = payment.Currency, CreatedByUserId = payment.RenewalRequest.RequestedByUserId,
            CreatedAtUtc = paidAt, UpdatedAtUtc = paidAt
        };
        var firstPeriod = !await db.SubscriptionPeriods.AnyAsync(x=>x.AcademyId==payment.AcademyId&&x.SportEnrollmentId==period.SportEnrollmentId,ct);
        terms.Capture(period); db.SubscriptionPeriods.Add(period);
        db.Receipts.Add(receipt);
        providerEvent.ProcessedAtUtc = clock.UtcNow; providerEvent.ProcessingResult = "confirmed";
        await Academy.Api.Operations.GuardianLifecycleNotice.Add(db,payment.AcademyId,period.SportEnrollmentId,firstPeriod?"Enrollment":"SingleRenewal",period.Id,"SubscriptionPeriod",period.Id,clock.UtcNow,token:ct);
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
