using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Facilities;

public enum FacilitySpaceKind { Pool, Pitch, Studio }
public enum ResourceBookingState { Reserved, Cancelled }
public sealed class Facility : TenantEntity
{
    public Guid BranchId { get; set; }
    public required string Name { get; set; }
}
public sealed class FacilitySpace : TenantEntity
{
    public Guid FacilityId { get; set; }
    public Guid BranchId { get; set; }
    public Guid SportId { get; set; }
    public required string Name { get; set; }
    public FacilitySpaceKind Kind { get; set; }
}
public sealed class FacilityResource : TenantEntity
{
    public Guid FacilitySpaceId { get; set; }
    public Guid BranchId { get; set; }
    public required string Name { get; set; }
    public int Capacity { get; set; }
    public bool SharedBookingsAllowed { get; set; }
    public int[] OperatingDays { get; set; } = [];
    public TimeOnly OpensAt { get; set; }
    public TimeOnly ClosesAt { get; set; }
    public string[] ServiceKinds { get; set; } = [];
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string? ApprovalEvidence { get; set; }
}
public sealed class ResourceClosure : TenantEntity
{
    public Guid ResourceId { get; set; }
    public Guid BranchId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string? Reason { get; set; }
    public Guid ActorUserId { get; set; }
}
public sealed class GroupResourceSchedule : TenantEntity
{
    public Guid TrainingGroupId { get; set; }
    public Guid BranchId { get; set; }
    public Guid ResourceId { get; set; }
    public Guid CoachMembershipId { get; set; }
    public int ReservedSeats { get; set; }
    public bool Exclusive { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }
}
// Immutable receipt of a schedule change, not a financial receipt.
public sealed class TrainingScheduleRevision : TenantEntity
{
    public Guid TrainingGroupId { get; set; }
    public Guid BranchId { get; set; }
    public Guid ActorUserId { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public required string Snapshot { get; set; }
    public int CreatedSessions { get; set; }
    public int PreservedSessions { get; set; }
}
// A reservation is neither payment proof, access permission nor attendance.
public sealed class ResourceBooking : TenantEntity
{
    public Guid ResourceId { get; set; }
    public Guid BranchId { get; set; }
    public Guid? TrainingGroupId { get; set; }
    public Guid? TrainingSessionId { get; set; }
    public Guid? CoachMembershipId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public required string ServiceKind { get; set; }
    public int ReservedSeats { get; set; }
    public int ApprovedCapacitySnapshot { get; set; }
    public bool Exclusive { get; set; }
    public ResourceBookingState State { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
}

public static class BookingCapacity
{
    public readonly record struct Slot(TimeOnly Start, TimeOnly End, int Seats, bool Exclusive);
    public static bool Overlaps(TimeOnly a, TimeOnly b, TimeOnly c, TimeOnly d) => a < d && c < b;
    public static bool Fits(int capacity, bool sharedAllowed, Slot candidate, IEnumerable<Slot> existing)
    {
        if (capacity < 1 || candidate.Seats < 1 || candidate.Seats > capacity || candidate.Start >= candidate.End) return false;
        var overlap = existing.Where(s => Overlaps(candidate.Start, candidate.End, s.Start, s.End)).ToArray();
        if (overlap.Length > 0 && (!sharedAllowed || candidate.Exclusive || overlap.Any(s => s.Exclusive))) return false;
        var points = overlap.SelectMany(s => new[] { (Time: s.Start < candidate.Start ? candidate.Start : s.Start, Delta: s.Seats), (Time: s.End > candidate.End ? candidate.End : s.End, Delta: -s.Seats) })
            .Append((candidate.Start, candidate.Seats)).Append((candidate.End, -candidate.Seats)).OrderBy(p => p.Item1).ThenBy(p => p.Item2);
        var used = 0;
        foreach (var point in points) { used += point.Item2; if (used > capacity) return false; }
        return true;
    }
}
