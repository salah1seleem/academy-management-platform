using Academy.Infrastructure.Structure;
namespace Academy.Infrastructure.Evaluations;

// A request is evidence for a HUMAN technical decision, never a level promotion.
public sealed class PlayerTechnicalReviewRequest : TenantEntity
{
    public Guid SportEnrollmentId { get; set; }
    public Guid PlanId { get; set; }
    public Guid CurriculumId { get; set; }
    public Guid SportId { get; set; }
    public Guid TrainingLevelId { get; set; }
    public required string LevelName { get; set; }
    public required string CurriculumTitle { get; set; }
    public int Sequence { get; set; }
    public int SourceReviewSequence { get; set; }
    public Guid[] EvidenceReviewIds { get; set; } = [];
    public Guid? PreviousRequestId { get; set; }
    public Guid ActorUserId { get; set; }
    public required string ActorName { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
}
public sealed class PlayerTechnicalReviewDecision : TenantEntity
{
    public Guid TechnicalRequestId { get; set; }
    public Guid SportEnrollmentId { get; set; }
    public Guid SportId { get; set; }
    public required string Outcome { get; set; }
    public Guid? RecommendedLevelId { get; set; }
    public string? RecommendedLevelName { get; set; }
    public string? FamilyNote { get; set; }
    public Guid ActorUserId { get; set; }
    public required string ActorName { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
}
