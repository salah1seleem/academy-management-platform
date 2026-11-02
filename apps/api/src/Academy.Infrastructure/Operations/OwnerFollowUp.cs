using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Operations;

// A human follow-up is not a command against its source (or any financial account).
public sealed class OwnerFollowUp : TenantEntity
{
    public Guid? TrainingGroupId { get; set; }
    public string SourceKind { get; set; } = "Group";
    public Guid? SourceId { get; set; }
    public Guid? SourceBranchId { get; set; }
    public Guid? SourceSportId { get; set; }
    public string? SourceNameSnapshot { get; set; }
    public string? SourceBranchSnapshot { get; set; }
    public string? SourceSportSnapshot { get; set; }
    public int? ManualPriority { get; set; }
    public Guid CreatedByMembershipId { get; set; }
    public Guid? AssignedToMembershipId { get; set; }
    public required string Condition { get; set; }
    public required string State { get; set; }
    public DateOnly CreatedOn { get; set; }
    public DateOnly? DueOn { get; set; }
    public string? Note { get; set; }
    public string? ResolutionReason { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
    public required string EvidenceJson { get; set; }
}

public sealed class OwnerFollowUpChange : TenantEntity
{
    public Guid FollowUpId { get; set; }
    public Guid ActorMembershipId { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
    public required string BeforeJson { get; set; }
    public required string AfterJson { get; set; }
}
