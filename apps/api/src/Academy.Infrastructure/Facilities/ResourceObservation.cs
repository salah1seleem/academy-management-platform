using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Facilities;
public sealed class ResourceObservation : TenantEntity
{
    public Guid ResourceBookingId { get; set; }
    public Guid ResourceId { get; set; }
    public Guid BranchId { get; set; }
    public Guid? TrainingSessionId { get; set; }
    public DateOnly BookingDate { get; set; }
    public int Sequence { get; set; }
    public int Count { get; set; }
    public int ExpectedSnapshot { get; set; }
    public int? RecordedPresentSnapshot { get; set; }
    public int? RecordedTotalSnapshot { get; set; }
    public required string ExpectedBasis { get; set; }
    public required string Source { get; set; }
    public Guid ActorUserId { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public bool OutsideBookedHours { get; set; }
}
public static class ObservationVariance
{
    public static bool NeedsReview(int expected, int? recordedPresent, int observed) => observed != expected || recordedPresent.HasValue && observed != recordedPresent;
}
