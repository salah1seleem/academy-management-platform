using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.People;

// One reviewed replacement of a dated session, not a second subscription or group membership.
public sealed class EnrollmentSessionMove : TenantEntity
{
    public Guid SportEnrollmentId { get; set; }
    public Guid BranchId { get; set; }
    public Guid SportId { get; set; }
    public Guid SourceSessionId { get; set; }
    public Guid SourceGroupId { get; set; }
    public Guid TargetSessionId { get; set; }
    public Guid TargetGroupId { get; set; }
    public Guid BatchId { get; set; }
    public Guid ActorUserId { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public required string Snapshot { get; set; }
}
