using Academy.Infrastructure.Subscriptions;

namespace Academy.Api.Slice3;

// One accounting graph for confirmed subscriptions, including explicitly approved
// historical evidence. This does not activate another period or call a provider.
public static class SubscriptionCollectionPosting
{
    public static (PaymentCollection Collection, Receipt Receipt) Create(PaymentRequest payment, RenewalRequest renewal,
        Guid playerId, string player, string sport, string plan, string method, string receiptMethod,
        DateTimeOffset paidAt, DateTimeOffset storedAt, string confirmedBy, Guid? collectionId = null, Guid? receiptId = null, string? receiptNumber = null)
    {
        if (payment.Status != PaymentRequestStatus.Confirmed || renewal.Status != RenewalRequestStatus.Paid ||
            payment.RenewalRequestId != renewal.Id || payment.AcademyId != renewal.AcademyId ||
            payment.Amount < 0 || payment.Amount != renewal.FinalAmount || payment.Currency != renewal.Currency)
            throw new InvalidOperationException("A confirmed, balanced payment is required before posting.");
        var id = collectionId ?? Guid.NewGuid();
        var collection = new PaymentCollection { Id = id, AcademyId = payment.AcademyId, SportEnrollmentId = renewal.SportEnrollmentId,
            RenewalRequestId = renewal.Id, PaymentRequestId = payment.Id, Amount = payment.Amount, Currency = payment.Currency,
            PaymentMethod = method, Provider = payment.Provider, ProviderReference = payment.ProviderReference, ConfirmedAtUtc = paidAt,
            ConfirmedBy = confirmedBy, CreatedAtUtc = storedAt, UpdatedAtUtc = storedAt };
        var receipt = new Receipt { Id = receiptId ?? Guid.NewGuid(), AcademyId = payment.AcademyId, CollectionId = id,
            ReceiptNumber = receiptNumber ?? $"RC-{paidAt:yyyyMMdd}-{id.ToString("N")[..8].ToUpperInvariant()}",
            PlayerId = playerId, SportEnrollmentId = renewal.SportEnrollmentId, SubscriptionPlanId = renewal.SubscriptionPlanId,
            PlayerNameSnapshot = player, SportNameSnapshot = sport, PlanNameSnapshot = plan,
            OriginalAmount = renewal.OriginalAmount, DiscountType = renewal.DiscountType, DiscountValue = renewal.DiscountValue,
            DiscountAmount = renewal.DiscountAmount, FinalAmount = renewal.FinalAmount,
            DiscountRuleIdSnapshot = renewal.DiscountRuleIdSnapshot, DiscountRuleRevisionSnapshot = renewal.DiscountRuleRevisionSnapshot,
            DiscountReason = renewal.DiscountReason, Amount = payment.Amount, Currency = payment.Currency, PaidAtUtc = paidAt,
            PaymentMethod = receiptMethod, ProviderReference = payment.ProviderReference, CreatedAtUtc = storedAt, UpdatedAtUtc = storedAt };
        return (collection, receipt);
    }
}
