using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Evaluations;

// Each submission is a new version, never an in-place rewrite of reviewed content.
public sealed class TrainingExercise : TenantEntity
{
    public Guid TrainingLevelId { get; set; }
    public Guid SportId { get; set; }
    public Guid? CopiedFromId { get; set; }
    public required string Title { get; set; }
    public required string Skill { get; set; }
    public required string Instructions { get; set; }
    public required string Equipment { get; set; }
    public int Minutes { get; set; }
    public required string RightsKind { get; set; }
    public required string RightsReference { get; set; }
    public string State { get; set; } = "Draft";
    public Guid CreatedByUserId { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
}

public sealed class SessionLessonTemplate : TenantEntity
{
    public Guid TrainingLevelId { get; set; }
    public Guid SportId { get; set; }
    public Guid? CopiedFromId { get; set; }
    public required string Title { get; set; }
    public int DeclaredMinutes { get; set; }
    public string State { get; set; } = "Draft";
    public Guid CreatedByUserId { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
}

public sealed class SessionLessonStep : TenantEntity
{
    public Guid TemplateId { get; set; }
    public Guid ExerciseId { get; set; }
    public Guid TrainingLevelId { get; set; }
    public Guid SportId { get; set; }
    public int Position { get; set; }
    public int Minutes { get; set; }
}

// Append-only application of a technically reviewed template to an actual session.
public sealed class SessionLessonAssignment : TenantEntity
{
    public Guid TrainingSessionId { get; set; }
    public Guid TemplateId { get; set; }
    public Guid SportId { get; set; }
    public int Revision { get; set; }
    public Guid ActorUserId { get; set; }
    public required string ActorName { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
}

// Retire one immutable version without rewriting its contents or past applications.
public sealed class TrainingContentArchive : TenantEntity
{
    public Guid? ExerciseId { get; set; }
    public Guid? TemplateId { get; set; }
    public Guid TrainingLevelId { get; set; }
    public Guid SportId { get; set; }
    public Guid ActorUserId { get; set; }
    public required string ActorName { get; set; }
    public string? Note { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
}
