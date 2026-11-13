using System.Data;
using System.Text.Json;
using Academy.Api.Configuration;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice3;

public enum RenewalCreationState { Created, Replay, NotFound, OpenRenewalExists, PayloadConflict, QuoteChanged }

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
    DateTimeOffset? DiscountAppliedAtUtc,
    Guid? DiscountRuleId = null,
    int? DiscountRuleRevision = null);

public sealed record StaffRenewalQuote(Guid EnrollmentId, Guid PlanId, string QuoteHash,
    DateOnly Start, DateOnly? End, int? Sessions, decimal OriginalAmount, decimal DiscountAmount,
    decimal FinalAmount, string Currency);

public sealed class RenewalCreationService(
    FoundationDbContext db,
    IPaymentGateway gateway,
    ISubscriptionClock clock,
    Academy.Api.Operations.DiscountRules rules, IHostEnvironment environment)
{
    internal static IQueryable<PaymentRequest> OpenPayments(FoundationDbContext db, Guid academyId, Guid enrollmentId) =>
        from renewal in db.RenewalRequests
        join payment in db.PaymentRequests on renewal.PaymentRequestId equals (Guid?)payment.Id
        where renewal.AcademyId == academyId && renewal.SportEnrollmentId == enrollmentId &&
              (renewal.Status == RenewalRequestStatus.PendingPayment || renewal.Status == RenewalRequestStatus.PaymentInProgress) &&
              payment.AcademyId == academyId && payment.RenewalRequestId == renewal.Id &&
              (payment.Status == PaymentRequestStatus.Created || payment.Status == PaymentRequestStatus.Pending)
        select payment;

    internal static Task<bool> HasOpenFamilyRenewal(FoundationDbContext db, Guid academyId, Guid enrollmentId, CancellationToken ct = default) =>
        db.FamilyRenewalLines.IgnoreQueryFilters().AnyAsync(l => l.AcademyId == academyId && l.SportEnrollmentId == enrollmentId && l.Request.Status == FamilyRenewalStatus.Pending, ct);

    internal static async Task<DateOnly> PreviewStartAsync(FoundationDbContext db, Guid academyId, Guid enrollmentId, DateOnly today, CancellationToken ct = default)
    {
        var latestEnd = await db.SubscriptionPeriods.AsNoTracking()
            .Where(x => x.AcademyId == academyId && x.SportEnrollmentId == enrollmentId && x.Status != SubscriptionPeriodStatus.Cancelled && x.EndDate != null)
            .MaxAsync(x => x.EndDate, ct);
        return latestEnd is { } end && end >= today ? end.AddDays(1) : today;
    }

    private IQueryable<SubscriptionPlan> AvailablePlans(SportEnrollment enrollment) => db.SubscriptionPlans.TrainingOnly()
        .ForBranch(enrollment.BranchId).Where(x => x.AcademyId == enrollment.AcademyId && x.SportId == enrollment.SportId);

    private async Task<StaffRenewalQuote> BuildStaffQuote(SportEnrollment enrollment, SubscriptionPlan plan,
        RenewalFinancialSnapshot price, CancellationToken ct)
    {
        var terms = PlanTerms.From(plan);
        var start = await PreviewStartAsync(db, enrollment.AcademyId, enrollment.Id, clock.Today, ct);
        var end = terms.EndDate(start);
        var hash = AcademyConfigurationService.Hash(JsonSerializer.Serialize(new
        {
            enrollment.AcademyId, EnrollmentId = enrollment.Id, PlanId = plan.Id, terms, start, end,
            plan.Currency, price.OriginalAmount, price.DiscountAmount, price.FinalAmount,
            price.DiscountType, price.DiscountValue, price.DiscountRuleId, price.DiscountRuleRevision, price.DiscountReason
        }));
        return new(enrollment.Id, plan.Id, hash, start, end, terms.Sessions, price.OriginalAmount,
            price.DiscountAmount, price.FinalAmount, plan.Currency);
    }

    public async Task<(StaffRenewalQuote? Quote, bool HasOpenRenewal)> ReadStaffQuoteAsync(
        Guid academyId, Guid actorUserId, Guid enrollmentId, Guid planId, CancellationToken ct)
    {
        var enrollment = await db.SportEnrollments.AsNoTracking().SingleOrDefaultAsync(
            x => x.AcademyId == academyId && x.Id == enrollmentId && x.IsActive, ct);
        if (enrollment is null) return (null, false);
        var plan = await AvailablePlans(enrollment).ForRegistrationFormat(enrollment.RegistrationFormat).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == planId && x.IsActive, ct);
        if (plan is null) return (null, false);
        if (await HasOpenFamilyRenewal(db, academyId, enrollmentId, ct) || await OpenPayments(db, academyId, enrollmentId).AnyAsync(ct))
            return (null, true);
        var price = await rules.CalculateAsync(academyId, enrollment.PlayerId, plan.Price, actorUserId, plan: plan, branchId: enrollment.BranchId);
        return (await BuildStaffQuote(enrollment, plan, price, ct), false);
    }

    public async Task<RenewalCreationResult> CreateAsync(
        Guid academyId,
        Guid actorUserId,
        Guid enrollmentId,
        Guid planId,
        string idempotencyKey,
        RenewalFinancialSnapshot? financialSnapshot = null,
        bool allowInactivePlan = false,
        CancellationToken ct = default,
        PlanTerms? fixedTerms = null,
        string? expectedQuoteHash = null)
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
            .Select(x => new { RenewalId = x.Id, PaymentId = x.PaymentRequestId, x.SportEnrollmentId, x.SubscriptionPlanId })
            .SingleOrDefaultAsync(ct);
        if (replay is not null)
            return replay.SportEnrollmentId != enrollmentId || replay.SubscriptionPlanId != planId
                ? new(RenewalCreationState.PayloadConflict)
                : new(RenewalCreationState.Replay, replay.RenewalId, replay.PaymentId);

        var enrollment = await db.SportEnrollments
            .SingleOrDefaultAsync(x => x.AcademyId == academyId && x.Id == enrollmentId && x.IsActive, ct);
        if (enrollment is null)
            return new(RenewalCreationState.NotFound);

        await PlanCatalog.LockAsync(db, academyId, planId);
        if (fixedTerms is not null && !PlanCatalog.FormatMatches(enrollment.RegistrationFormat, fixedTerms.Format)) return new(RenewalCreationState.NotFound);
        var availablePlans = AvailablePlans(enrollment);
        if (fixedTerms is null) availablePlans = availablePlans.ForRegistrationFormat(enrollment.RegistrationFormat);
        var plan = await availablePlans.SingleOrDefaultAsync(
            x => x.Id == planId && (allowInactivePlan || x.IsActive), ct);
        if (plan is null)
            return new(RenewalCreationState.NotFound);
        await db.Entry(plan).ReloadAsync(ct);

        if (await HasOpenFamilyRenewal(db, academyId, enrollmentId, ct) || await OpenPayments(db, academyId, enrollmentId).AnyAsync(ct))
            return new(RenewalCreationState.OpenRenewalExists);

        financialSnapshot ??= await rules.CalculateAsync(academyId, enrollment.PlayerId, plan.Price, actorUserId, plan: plan, branchId: enrollment.BranchId);
        if (expectedQuoteHash is not null && (await BuildStaffQuote(enrollment, plan, financialSnapshot, ct)).QuoteHash != expectedQuoteHash)
            return new(RenewalCreationState.QuoteChanged);
        var originalAmount = financialSnapshot.OriginalAmount;
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
            DiscountRuleIdSnapshot = financialSnapshot?.DiscountRuleId,
            DiscountRuleRevisionSnapshot = financialSnapshot?.DiscountRuleRevision,
            AmountExpected = finalAmount,
            Currency = plan.Currency,
            Status = RenewalRequestStatus.PaymentInProgress,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow
        };
        var paymentId = Guid.NewGuid();
        (fixedTerms ?? PlanTerms.From(plan)).Capture(renewal);
        var manual = !environment.IsEnvironment("Demo") && !environment.IsEnvironment("Testing") || await db.AcademyTransferSettings.AnyAsync(x => x.AcademyId == academyId && x.IsActive, ct);
        var session = manual ? new CheckoutSession("Manual", $"manual-{paymentId:N}", $"manual-{paymentId:N}", ["التحويل للأكاديمية"]) : gateway.CreateCheckout(academyId, paymentId);
        var payment = new PaymentRequest
        {
            Id = paymentId,
            AcademyId = academyId,
            RenewalRequestId = renewal.Id,
            Provider = session.Provider,
            ProviderEnvironment = manual ? "Manual" : "Demo",
            Amount = finalAmount,
            Currency = plan.Currency,
            Status = PaymentRequestStatus.Pending,
            ProviderReference = session.ProviderReference,
            CheckoutReference = session.CheckoutReference,
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow
        };
        renewal.PaymentRequestId = payment.Id;
        renewal.VipRewardUseId=await Academy.Api.Operations.VipRewards.Reserve(db,academyId,renewal.DiscountRuleIdSnapshot,"Renewal",renewal.Id,actorUserId,clock.Today,clock.UtcNow,player:enrollment.PlayerId);
        db.RenewalRequests.Add(renewal);
        db.PaymentRequests.Add(payment);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(RenewalCreationState.Created, renewal.Id, payment.Id);
    }
}
