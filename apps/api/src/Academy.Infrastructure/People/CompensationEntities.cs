using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.People;
public enum CompensationUnit { Session = 1, Minute = 2 }
public enum CompensationCause { CancelledSession = 1, CoachAbsent = 2, ApprovedAbsence = 3 }
public enum CompensationBookingState { Reserved = 1, Cancelled = 2 }

// Explicitly approved compensatory service; never a collection or silent subscription extension.
public sealed class CompensationGrant : TenantEntity
{
    public Guid SportEnrollmentId { get; set; }
    public Guid BranchId { get; set; }
    public Guid SportId { get; set; }
    public Guid SourceSessionId { get; set; }
    public Guid SourceGroupId { get; set; }
    public Guid SourcePeriodId { get; set; }
    public CompensationCause Cause { get; set; }
    public CompensationUnit Unit { get; set; }
    public int Units { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }
    public Guid ActorUserId { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public required string Snapshot { get; set; }
}
public sealed class CompensationBooking : TenantEntity
{
    public Guid GrantId { get; set; }
    public Guid SportEnrollmentId { get; set; }
    public Guid BranchId { get; set; }
    public Guid SportId { get; set; }
    public Guid TrainingSessionId { get; set; }
    public Guid TrainingGroupId { get; set; }
    public int Units { get; set; }
    public CompensationBookingState State { get; set; } = CompensationBookingState.Reserved;
    public Guid ActorUserId { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public required string Snapshot { get; set; }
}
public sealed class CompensationMovement : TenantEntity
{
    public Guid GrantId { get; set; }
    public Guid BookingId { get; set; }
    public Guid PlayerAttendanceId { get; set; }
    public Guid? ReversesMovementId { get; set; }
    public Guid BranchId { get; set; }
    public int Units { get; set; } // Positive consumption; a correction is its exact negative.
    public Guid ActorUserId { get; set; }
}
