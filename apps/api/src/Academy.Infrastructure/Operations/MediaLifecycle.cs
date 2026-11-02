using Academy.Infrastructure.Structure;
namespace Academy.Infrastructure.Operations;

// Request is committed before filesystem removal. Neither evidence row is overwritten on retry.
public sealed class PrivateMediaErasure : TenantEntity
{
    public Guid AssetId { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
    public required string Reason { get; set; }
    public string? Note { get; set; }
}
public sealed class PrivateMediaPurge : TenantEntity
{
    public Guid AssetId { get; set; }
    public Guid ErasureId { get; set; }
    public long ReleasedBytes { get; set; }
}
public sealed class PrivateMediaDownloadBudget : TenantEntity
{
    public Guid UserId { get; set; }
    public DateOnly UtcDay { get; set; }
    public long ReservedBytes { get; set; }
}
