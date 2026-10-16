using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Operations;

// Append-only evidence for an administrative edit of the existing daily source.
// It is neither another attendance period nor a financial movement.
public sealed class StaffAttendanceChange : TenantEntity
{
    public Guid AttendanceId { get; set; }
    public Guid MembershipId { get; set; }
    public Guid ActorMembershipId { get; set; }
    public Guid ActorUserId { get; set; }
    public DateOnly WorkDate { get; set; }
    public long BeforeVersion { get; set; }
    public long AfterVersion { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public required string BeforeSnapshot { get; set; }
    public required string AfterSnapshot { get; set; }
}
