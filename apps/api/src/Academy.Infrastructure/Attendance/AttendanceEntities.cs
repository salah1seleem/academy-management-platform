using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;

namespace Academy.Infrastructure.Attendance;

public enum TrainingSessionStatus { Scheduled = 1, Held = 2, Cancelled = 3 }
public enum TrainingSessionSource { RecurringSchedule = 1, Manual = 2 }
public enum AttendanceStatus { NotRecorded = 1, Present = 2, Absent = 3 }
public enum SessionMovementType { AttendanceConsume = 1, AttendanceRestore = 2 }

public sealed class TrainingSession : TenantEntity
{
    public Guid TrainingGroupId { get; set; }
    public Guid BranchId { get; set; }
    public Guid SportId { get; set; }
    public Guid AgeCategoryId { get; set; }
    public DateOnly SessionDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public TrainingSessionStatus Status { get; set; } = TrainingSessionStatus.Scheduled;
    public TrainingSessionSource Source { get; set; }
    public Guid? RecurringScheduleId { get; set; }
    public string? Notes { get; set; }
    public Guid CreatedByUserId { get; set; }
    public TrainingGroup TrainingGroup { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
    public Sport Sport { get; set; } = null!;
    public AgeCategory AgeCategory { get; set; } = null!;
    public RecurringSchedule? RecurringSchedule { get; set; }
}

public sealed class PlayerAttendance : TenantEntity
{
    public Guid TrainingSessionId { get; set; }
    public Guid TrainingGroupId { get; set; }
    public Guid SportEnrollmentId { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.NotRecorded;
    public string? Notes { get; set; }
    public Guid RecordedByUserId { get; set; }
    public Guid? ConsumedSubscriptionPeriodId { get; set; }
    public TrainingSession TrainingSession { get; set; } = null!;
    public SportEnrollment SportEnrollment { get; set; } = null!;
    public SubscriptionPeriod? ConsumedSubscriptionPeriod { get; set; }
}

public sealed class StaffAttendance : TenantEntity
{
    public Guid TrainingSessionId { get; set; }
    public Guid TrainingGroupId { get; set; }
    public Guid AcademyMembershipId { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.NotRecorded;
    public string? Notes { get; set; }
    public Guid RecordedByUserId { get; set; }
    public TrainingSession TrainingSession { get; set; } = null!;
    public AcademyMembership AcademyMembership { get; set; } = null!;
}

public sealed class SubscriptionSessionMovement : TenantEntity
{
    public Guid SubscriptionPeriodId { get; set; }
    public Guid PlayerAttendanceId { get; set; }
    public Guid? ReversesMovementId { get; set; }
    public SessionMovementType MovementType { get; set; }
    public int Quantity { get; set; }
    public int BalanceBefore { get; set; }
    public int BalanceAfter { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Guid PerformedByUserId { get; set; }
    public required string Reason { get; set; }
    public SubscriptionPeriod SubscriptionPeriod { get; set; } = null!;
    public PlayerAttendance PlayerAttendance { get; set; } = null!;
    public SubscriptionSessionMovement? ReversesMovement { get; set; }
}
