using Academy.Infrastructure.Structure;
using System.Text.Json.Serialization;

namespace Academy.Infrastructure.Subscriptions;

public enum ReceivableSource { ManualCharge = 1, OpeningBalance = 2 }
public enum ReceivableMovementKind { CreditNote = 1, PaymentAllocation = 2 }
public sealed class ReceivableDocument : TenantEntity
{
    public Guid SportEnrollmentId { get; set; }
    public Guid PlayerId { get; set; }
    public required string PlayerNameSnapshot { get; set; }
    public Guid BranchId { get; set; }
    public Guid? PlanId { get; set; }
    public string? PlanNameSnapshot { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public decimal? PlanPriceSnapshot { get; set; }
    public required string Description { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public DateOnly DueDate { get; set; }
    public ReceivableSource Source { get; set; }
    public required string SourceKey { get; set; }
    public required string ContentHash { get; set; }
    public Guid ActorUserId { get; set; }
}
public sealed class ReceivableMovement : TenantEntity
{
    public Guid DocumentId { get; set; }
    public ReceivableMovementKind Kind { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public Guid? CollectionId { get; set; }
    public required string Reason { get; set; }
    public required string SourceKey { get; set; }
    public required string ContentHash { get; set; }
    public Guid ActorUserId { get; set; }
}
