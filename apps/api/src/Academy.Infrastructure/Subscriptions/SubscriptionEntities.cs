using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Subscriptions;

public enum SubscriptionPlanType { Duration = 1, Sessions = 2, Combined = 3 }
public enum SubscriptionPeriodStatus { Scheduled = 1, Active = 2, Expired = 3, Cancelled = 4, Frozen = 5 }
public enum RenewalRequestStatus { PendingPayment = 1, PaymentInProgress = 2, Paid = 3, Failed = 4, Cancelled = 5, Expired = 6 }
public enum PaymentRequestStatus { Created = 1, Pending = 2, Confirmed = 3, Failed = 4, Cancelled = 5, Expired = 6 }
public enum PaymentEventOutcome { Success = 1, Failed = 2, Cancelled = 3 }

public sealed class BeneficiaryRenewalReference : TenantEntity
{
    public Guid SportEnrollmentId { get; set; }
    public required string CodeHash { get; set; }
    public required string CodeHint { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public Guid GeneratedByUserId { get; set; }
    public SportEnrollment SportEnrollment { get; set; } = null!;
}

public sealed class SubscriptionPlan : TenantEntity
{
    public Guid SportId { get; set; }
    public required string ArabicName { get; set; }
    public string? EnglishName { get; set; }
    public SubscriptionPlanType PlanType { get; set; }
    public decimal Price { get; set; }
    public required string Currency { get; set; }
    public int? DurationDays { get; set; }
    public int? SessionCount { get; set; }
    public int DisplayOrder { get; set; }
    public Sport Sport { get; set; } = null!;
}

public sealed class SubscriptionPeriod : TenantEntity
{
    public Guid SportEnrollmentId { get; set; }
    public Guid SportId { get; set; }
    public Guid SubscriptionPlanId { get; set; }
    public Guid CollectionId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? InitialSessions { get; set; }
    public int? RemainingSessions { get; set; }
    public SubscriptionPeriodStatus Status { get; set; }
    public decimal PriceSnapshot { get; set; }
    public required string CurrencySnapshot { get; set; }
    public Guid CreatedByUserId { get; set; }
    public SportEnrollment SportEnrollment { get; set; } = null!;
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;
    public PaymentCollection Collection { get; set; } = null!;
}

public sealed class RenewalRequest : TenantEntity
{
    public Guid SportEnrollmentId { get; set; }
    public Guid SportId { get; set; }
    public Guid SubscriptionPlanId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public decimal AmountExpected { get; set; }
    public required string Currency { get; set; }
    public RenewalRequestStatus Status { get; set; }
    public Guid? PaymentRequestId { get; set; }
    public required string IdempotencyKey { get; set; }
    public SportEnrollment SportEnrollment { get; set; } = null!;
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;
}

public sealed class PaymentRequest : TenantEntity
{
    public Guid RenewalRequestId { get; set; }
    public required string Provider { get; set; }
    public required string ProviderEnvironment { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public PaymentRequestStatus Status { get; set; }
    public required string ProviderReference { get; set; }
    public required string CheckoutReference { get; set; }
    public DateTimeOffset? ConfirmedAtUtc { get; set; }
    public DateTimeOffset? LastProviderEventAtUtc { get; set; }
    public RenewalRequest RenewalRequest { get; set; } = null!;
}

public sealed class PaymentProviderEvent : TenantEntity
{
    public Guid PaymentRequestId { get; set; }
    public required string Provider { get; set; }
    public required string ProviderEventId { get; set; }
    public required string EventType { get; set; }
    public required string RawStatus { get; set; }
    public PaymentEventOutcome Outcome { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public required string ProcessingResult { get; set; }
    public PaymentRequest PaymentRequest { get; set; } = null!;
}

public sealed class PaymentCollection : TenantEntity
{
    public Guid SportEnrollmentId { get; set; }
    public Guid RenewalRequestId { get; set; }
    public Guid PaymentRequestId { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public required string PaymentMethod { get; set; }
    public required string Provider { get; set; }
    public required string ProviderReference { get; set; }
    public DateTimeOffset ConfirmedAtUtc { get; set; }
    public required string ConfirmedBy { get; set; }
    public SportEnrollment SportEnrollment { get; set; } = null!;
    public RenewalRequest RenewalRequest { get; set; } = null!;
    public PaymentRequest PaymentRequest { get; set; } = null!;
}

public sealed class Receipt : TenantEntity
{
    public Guid CollectionId { get; set; }
    public required string ReceiptNumber { get; set; }
    public Guid PlayerId { get; set; }
    public Guid SportEnrollmentId { get; set; }
    public Guid SubscriptionPlanId { get; set; }
    public required string PlayerNameSnapshot { get; set; }
    public required string SportNameSnapshot { get; set; }
    public required string PlanNameSnapshot { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public DateTimeOffset PaidAtUtc { get; set; }
    public required string PaymentMethod { get; set; }
    public required string ProviderReference { get; set; }
    public PaymentCollection Collection { get; set; } = null!;
}
