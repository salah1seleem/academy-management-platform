using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Attendance;
// Append-only operational decision for one occurrence, never a permanent group assignment.
public sealed class SessionCoachOverride : TenantEntity
{
    public Guid TrainingSessionId { get; set; }
    public Guid TrainingGroupId { get; set; }
    public Guid BranchId { get; set; }
    public Guid SportId { get; set; }
    public Guid OriginalCoachMembershipId { get; set; }
    public Guid ActualCoachMembershipId { get; set; }
    public int Sequence { get; set; }
    public Guid? PreviousDecisionId { get; set; }
    public DateOnly SessionDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public Guid ActorUserId { get; set; }
    public required string Reason { get; set; }
    public required string QualificationEvidence { get; set; }
    public string PayBasis { get; set; } = "ContractReviewPending";
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public required string Snapshot { get; set; }
}
