using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Evaluations;

// A published set of equally weighted learning requirements for an EXISTING
// training level. This is not a second level catalog or an accreditation.
public sealed class SkillCurriculum : TenantEntity
{
    public Guid TrainingLevelId { get; set; }
    public Guid SportId { get; set; }
    public int RevisionNumber { get; set; }
    public int EditRevision { get; set; } = 1;
    public Guid? CopiedFromId { get; set; }
    public required string Title { get; set; }
    public required string LevelNameSnapshot { get; set; }
    public string State { get; set; } = "Draft";
    public Guid CreatedByUserId { get; set; }
    public Guid CreationRequestId { get; set; }
    public required string CreationHash { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public Guid? PublishRequestId { get; set; }
    public Guid? RetiredByUserId { get; set; }
    public DateTimeOffset? RetiredAtUtc { get; set; }
}

public sealed class SkillRequirement : TenantEntity
{
    public Guid CurriculumId { get; set; }
    public Guid SportId { get; set; }
    public required string Name { get; set; }
    public required string Protocol { get; set; }
    public string Unit { get; set; } = "Checklist";
    public int DisplayOrder { get; set; }
}

public sealed class PlayerSkillPlan : TenantEntity
{
    public Guid SportEnrollmentId { get; set; }
    public Guid CurriculumId { get; set; }
    public Guid SportId { get; set; }
    public Guid AssignedByUserId { get; set; }
    public DateOnly AssignedOn { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
}

// Every review is deliberately published to the child's authorized family.
// Corrections append a new dated review; no overwriting historic measurements.
public sealed class PlayerSkillReview : TenantEntity
{
    public Guid PlanId { get; set; }
    public Guid CurriculumId { get; set; }
    public Guid RequirementId { get; set; }
    public Guid SportId { get; set; }
    public int Sequence { get; set; }
    public Guid ActorUserId { get; set; }
    public required string ActorName { get; set; }
    public DateOnly ObservedOn { get; set; }
    public required string State { get; set; }
    public decimal? MeasuredValue { get; set; }
    public string? Note { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
}
