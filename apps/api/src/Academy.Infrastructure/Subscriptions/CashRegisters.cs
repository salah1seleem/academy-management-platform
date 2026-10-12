using System.Text.Json.Serialization;
using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Subscriptions;

public sealed class CashRegister : TenantEntity
{
    public Guid BranchId { get; set; }
    public required string Name { get; set; }
    public required string Currency { get; set; }
    public Guid CreatedByUserId { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
}
public sealed class CashierAssignment : TenantEntity
{
    public Guid RegisterId { get; set; }
    public Guid MembershipId { get; set; }
    public Guid BranchId { get; set; }
}
public sealed class CashSession : TenantEntity
{
    public Guid RegisterId { get; set; }
    public Guid BranchId { get; set; }
    public Guid OpenedByMembershipId { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public decimal OpeningAmount { get; set; }
    public DateTimeOffset OpenedAtUtc { get; set; }
    public required string OpenKey { get; set; }
    public required string OpenHash { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public decimal? CountedAmount { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public decimal? ExpectedAmount { get; set; }
    public string? ClosingReason { get; set; }
    public string? CloseKey { get; set; }
    public string? CloseHash { get; set; }
}
public enum CashMovementKind { TransferIn = 1, TransferOut = 2 }
public sealed class CashMovement : TenantEntity
{
    public Guid SessionId { get; set; }
    public Guid BranchId { get; set; }
    public Guid TransferId { get; set; }
    public CashMovementKind Kind { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public Guid ActorUserId { get; set; }
}
public sealed class CashTransfer : TenantEntity
{
    public Guid FromSessionId { get; set; }
    public Guid ToSessionId { get; set; }
    public Guid BranchId { get; set; }
    public Guid ToBranchId { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public Guid ActorUserId { get; set; }
    public string? Reason { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
}

// A collection is counted once, with the immutable receipt that produced it.
// Opening float/transfers deliberately remain separate from sales income.
public sealed class CashReceiptPosting : TenantEntity
{
    public Guid SessionId { get; set; }
    public Guid BranchId { get; set; }
    public Guid CollectionId { get; set; }
    public Guid ReceiptId { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public Guid ActorUserId { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public Receipt Receipt { get; set; } = null!;
}

public sealed class CashProductPosting : TenantEntity
{
    public Guid SessionId { get; set; }
    public Guid BranchId { get; set; }
    public Guid ProductSaleId { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public Guid ActorUserId { get; set; }
    public required string RequestHash { get; set; }
    public Academy.Infrastructure.Operations.ProductSale ProductSale { get; set; } = null!;
}
