using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Attendance;

public enum AttendanceCorrectionKind { Players, Staff }
public enum AttendanceCorrectionState { Pending, Applying, Approved, Rejected }
public sealed class AttendanceCorrection : TenantEntity
{
    public Guid TrainingSessionId { get; set; }
    public Guid TrainingGroupId { get; set; }
    public Guid BranchId { get; set; }
    public DateOnly SessionDate { get; set; }
    public uint TrainingSessionVersion { get; set; }
    public AttendanceCorrectionKind Kind { get; set; }
    public required string Payload { get; set; }
    public required string Reason { get; set; }
    public Guid RequestedByUserId { get; set; }
    public Guid RequestedByMembershipId { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public AttendanceCorrectionState State { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public Guid? DecidedByMembershipId { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }
    public DateTimeOffset? AppliedAtUtc { get; set; }
}
