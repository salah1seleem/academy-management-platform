using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Subscriptions;

public enum FamilyRenewalStatus { Pending, Approved, Rejected, Cancelled }

// Pending family instructions are not individual chargeable payments or receipts.
public sealed class FamilyRenewalRequest : TenantEntity
{
    public Guid? VipRewardUseId {get;set;}
    public Guid GuardianId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string RequestHash { get; set; }
    public required string QuoteHash { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public required string Currency { get; set; }
    public int LineCount { get; set; }
    public FamilyRenewalStatus Status { get; set; }
    public int Revision { get; set; } = 1;
    public Guid? ProofAssetId { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public GuardianProfile Guardian { get; set; } = null!;
    public List<FamilyRenewalLine> Lines { get; set; } = [];
}

public sealed class FamilyRenewalLine : TenantEntity
{
    public Guid RequestId { get; set; }
    public Guid SportEnrollmentId { get; set; }
    public Guid SubscriptionPlanId { get; set; }
    public Guid BranchId { get; set; }
    public Guid PlayerId { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public required string Currency { get; set; }
    public required string FinancialSnapshotJson { get; set; }
    public required string PlanTermsJson { get; set; }
    public Guid? ResultPaymentId { get; set; }
    public FamilyRenewalRequest Request { get; set; } = null!;
    public SportEnrollment SportEnrollment { get; set; } = null!;
}
