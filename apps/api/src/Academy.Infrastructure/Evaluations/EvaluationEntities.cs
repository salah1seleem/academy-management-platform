using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Evaluations;

public enum FootballAxis
{
    Passing = 1,
    Dribbling = 2,
    Speed = 3,
    Defending = 4,
    Physical = 5,
    Shooting = 6
}

public enum EvaluationStatus
{
    Draft = 1,
    Published = 2,
    Superseded = 3
}

public sealed class EvaluationCriterion : TenantEntity
{
    public Guid SportId { get; set; }
    public required string ArabicName { get; set; }
    public string? EnglishName { get; set; }
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public decimal Weight { get; set; } = 1m;
    public FootballAxis? FootballAxis { get; set; }
    public Sport Sport { get; set; } = null!;
}

public sealed class PlayerEvaluation : TenantEntity
{
    public Guid SportEnrollmentId { get; set; }
    public Guid SportId { get; set; }
    public Guid TrainingGroupId { get; set; }
    public Guid EvaluatedByUserId { get; set; }
    public DateOnly EvaluationDate { get; set; }
    public required string ReportingPeriod { get; set; }
    public EvaluationStatus Status { get; set; } = EvaluationStatus.Draft;
    public string? GeneralNotes { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public int RevisionNumber { get; set; } = 1;
    public Guid? ReplacesEvaluationId { get; set; }
    public SportEnrollment SportEnrollment { get; set; } = null!;
    public Sport Sport { get; set; } = null!;
    public TrainingGroup TrainingGroup { get; set; } = null!;
    public ApplicationUser EvaluatedByUser { get; set; } = null!;
    public ApplicationUser? PublishedByUser { get; set; }
    public PlayerEvaluation? ReplacesEvaluation { get; set; }
    public ICollection<EvaluationScore> Scores { get; set; } = [];
}

public sealed class EvaluationScore : TenantEntity
{
    public Guid PlayerEvaluationId { get; set; }
    public Guid EvaluationCriterionId { get; set; }
    public Guid SportId { get; set; }
    public int? Score { get; set; }
    public string? Notes { get; set; }
    public required string CriterionNameSnapshot { get; set; }
    public decimal WeightSnapshot { get; set; }
    public FootballAxis? FootballAxisSnapshot { get; set; }
    public PlayerEvaluation PlayerEvaluation { get; set; } = null!;
    public EvaluationCriterion EvaluationCriterion { get; set; } = null!;
}
