using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Subscriptions;

// A human-entered statement comparison is not a provider confirmation or money movement.
public sealed class SettlementComparison : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid? CollectionId { get; set; }
    public required string SourceKind { get; set; }
    public required string AccountTag { get; set; }
    public required string StatementReference { get; set; }
    public required string LineReference { get; set; }
    public required string Provider { get; set; }
    public required string PaymentReference { get; set; }
    public DateOnly SettlementDate { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal FeeAmount { get; set; }
    public decimal NetAmount { get; set; }
    public required string Currency { get; set; }
    public decimal? ExpectedAmount { get; set; }
    public string? ExpectedCurrency { get; set; }
    public required string Result { get; set; }
    public required string ContentHash { get; set; }
    public Guid ActorUserId { get; set; }
    public string? Note { get; set; }
}
