using Academy.Infrastructure.Structure;
using System.Text.Json.Serialization;

namespace Academy.Infrastructure.Subscriptions;

// One immutable record is both sides of a credit application. It is not cash,
// a discount, a refund, or a replacement for the original payment allocation.
public sealed class ReceivableCreditApplication : TenantEntity
{
    public Guid SourceDocumentId { get; set; }
    public Guid TargetDocumentId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid ActorMembershipId { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public decimal Amount { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public decimal SourceBalanceBefore { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public decimal TargetBalanceBefore { get; set; }
    public required string Currency { get; set; }
    public required string Reason { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public required string QuoteHash { get; set; }
}
