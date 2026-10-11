using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public sealed class CollectionService(FoundationDbContext db, ISubscriptionClock clock, DiscountRules discounts)
{
    // Caller owns the transaction. One lock covers manual approval and repeated clicks.
    public async Task<Guid> ConfirmAsync(Guid academyId, Guid actorId, Guid paymentId, string method)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"collection:{academyId}:{paymentId}"}, 0))");
        var existing = await db.Receipts.Where(x => x.AcademyId == academyId && x.Collection.PaymentRequestId == paymentId).Select(x => (Guid?)x.Id).SingleOrDefaultAsync();
        if (existing.HasValue) return existing.Value;
        var payment = await db.PaymentRequests.Include(x => x.RenewalRequest).ThenInclude(x => x.SubscriptionPlan).ThenInclude(x => x.Sport)
            .Include(x => x.RenewalRequest).ThenInclude(x => x.SportEnrollment).ThenInclude(x => x.Player)
            .SingleAsync(x => x.AcademyId == academyId && x.Id == paymentId);
        if (payment.Status != PaymentRequestStatus.Pending) throw new InvalidOperationException("طلب الدفع غير قابل للتحصيل.");
        var r = payment.RenewalRequest; var plan = r.SubscriptionPlan;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"collection-enrollment:{academyId}:{r.SportEnrollmentId}"}, 0))");
        if (payment.Id != r.PaymentRequestId || payment.Amount != r.FinalAmount) throw new InvalidOperationException("طلب الدفع تغير. أعد تحميله.");
        payment.Status = PaymentRequestStatus.Confirmed; payment.ConfirmedAtUtc = clock.UtcNow; payment.UpdatedAtUtc = clock.UtcNow;
        r.Status = RenewalRequestStatus.Paid; r.UpdatedAtUtc = clock.UtcNow;
        var c = new PaymentCollection { Id = Guid.NewGuid(), AcademyId = academyId, SportEnrollmentId = r.SportEnrollmentId, RenewalRequestId = r.Id, PaymentRequestId = paymentId, Amount = payment.Amount, Currency = payment.Currency, PaymentMethod = method, Provider = "Manual", ProviderReference = payment.ProviderReference, ConfirmedAtUtc = clock.UtcNow, ConfirmedBy = actorId.ToString(), CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        db.Collections.Add(c);
        var latest = await db.SubscriptionPeriods.Where(x => x.AcademyId == academyId && x.SportEnrollmentId == r.SportEnrollmentId && x.Status != SubscriptionPeriodStatus.Cancelled).MaxAsync(x => x.EndDate);
        var start = latest.HasValue && latest >= clock.Today ? latest.Value.AddDays(1) : clock.Today;
        db.SubscriptionPeriods.Add(new SubscriptionPeriod { Id = Guid.NewGuid(), AcademyId = academyId, SportEnrollmentId = r.SportEnrollmentId, SportId = r.SportId, SubscriptionPlanId = plan.Id, CollectionId = c.Id, StartDate = start, EndDate = plan.DurationDays.HasValue ? start.AddDays(plan.DurationDays.Value - 1) : null, InitialSessions = plan.SessionCount, RemainingSessions = plan.SessionCount, Status = start > clock.Today ? SubscriptionPeriodStatus.Scheduled : SubscriptionPeriodStatus.Active, PriceSnapshot = payment.Amount, CurrencySnapshot = payment.Currency, CreatedByUserId = actorId, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
        var receipt = new Receipt { Id = Guid.NewGuid(), AcademyId = academyId, CollectionId = c.Id, ReceiptNumber = $"RC-{clock.Today:yyyyMMdd}-{c.Id.ToString("N")[..8]}", PlayerId = r.SportEnrollment.PlayerId, SportEnrollmentId = r.SportEnrollmentId, SubscriptionPlanId = plan.Id, PlayerNameSnapshot = r.SportEnrollment.Player.ArabicName, SportNameSnapshot = plan.Sport.ArabicName, PlanNameSnapshot = plan.ArabicName, OriginalAmount = r.OriginalAmount, DiscountType = r.DiscountType, DiscountValue = r.DiscountValue, DiscountAmount = r.DiscountAmount, FinalAmount = r.FinalAmount, Amount = c.Amount, Currency = c.Currency, PaidAtUtc = clock.UtcNow, PaymentMethod = method, ProviderReference = c.ProviderReference, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        db.Receipts.Add(receipt); await db.SaveChangesAsync(); return receipt.Id;
    }
    public async Task<Guid> CreateCashAsync(Guid academyId, Guid actorId, Guid enrollmentId, Guid planId, string key, string method, Guid? requestedBy = null, RenewalFinancialSnapshot? fixedSnapshot = null, string? registrationPayloadHash = null)
    {
        var plan = await db.SubscriptionPlans.SingleAsync(x => x.AcademyId == academyId && x.Id == planId && x.IsActive);
        var enrollment = await db.SportEnrollments.SingleAsync(x => x.AcademyId == academyId && x.Id == enrollmentId && x.SportId == plan.SportId);
        var now = clock.UtcNow; var pid = Guid.NewGuid();
        var snapshot = fixedSnapshot ?? await discounts.CalculateAsync(academyId, enrollment.PlayerId, plan.Price, actorId);
        var r = new RenewalRequest { Id = Guid.NewGuid(), AcademyId = academyId, SportEnrollmentId = enrollmentId, SportId = enrollment.SportId, SubscriptionPlanId = planId, RequestedByUserId = requestedBy ?? actorId, RequestedAtUtc = now, OriginalAmount = plan.Price, DiscountType = snapshot.DiscountType, DiscountValue = snapshot.DiscountValue, DiscountAmount = snapshot.DiscountAmount, DiscountReason = snapshot.DiscountReason, DiscountAppliedByUserId = snapshot.DiscountAppliedByUserId, DiscountAppliedAtUtc = snapshot.DiscountAppliedAtUtc, FinalAmount = snapshot.FinalAmount, AmountExpected = snapshot.FinalAmount, Currency = plan.Currency, Status = RenewalRequestStatus.PaymentInProgress, IdempotencyKey = key, PaymentRequestId = pid, CreatedAtUtc = now, UpdatedAtUtc = now };
        var p = new PaymentRequest { Id = pid, AcademyId = academyId, RenewalRequestId = r.Id, Provider = "Manual", ProviderEnvironment = "Manual", Amount = snapshot.FinalAmount, Currency = plan.Currency, Status = PaymentRequestStatus.Pending, ProviderReference = $"manual-{pid:N}", CheckoutReference = $"manual-{pid:N}", CreatedAtUtc = now, UpdatedAtUtc = now };
        r.RegistrationPayloadHash = registrationPayloadHash;
        db.RenewalRequests.Add(r); db.PaymentRequests.Add(p); await db.SaveChangesAsync();
        await ConfirmAsync(academyId, actorId, p.Id, method); return p.Id;
    }
}
