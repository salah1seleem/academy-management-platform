using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Subscriptions;

// Signed, known-request discrepancies only. Never stores signatures, card data,
// raw webhook bodies, or an automatically approved financial adjustment.
public sealed class ProviderEventQuarantine : TenantEntity
{
    public Guid PaymentRequestId { get; set; }
    public Guid BranchId { get; set; }
    public required string Provider { get; set; }
    public required string ProviderEventId { get; set; }
    public required string EnvelopeHash { get; set; }
    public required string ReasonCode { get; set; }
    public required string ReceivedStatus { get; set; }
    public required string ReceivedReference { get; set; }
    public required string ExpectedReference { get; set; }
    public decimal ReceivedAmount { get; set; }
    public decimal ExpectedAmount { get; set; }
    public required string ReceivedCurrency { get; set; }
    public required string ExpectedCurrency { get; set; }
    public PaymentRequest PaymentRequest { get; set; } = null!;
}
