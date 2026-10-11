using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.People;

// Append-only. TrainingGroupId on SportEnrollment remains the historical anchor;
// operational membership is resolved for the date of the session, never by a job.
public sealed class EnrollmentGroupTransfer : TenantEntity
{
    public Guid SportEnrollmentId { get; set; }
    public Guid BranchId { get; set; }
    public Guid SportId { get; set; }
    public Guid SourceGroupId { get; set; }
    public Guid TargetGroupId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public Guid ActorUserId { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public required string Snapshot { get; set; }
}
