using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Evaluations;

// A technical goal is not a score, a subscription or a promise of promotion.
public sealed class PlayerDevelopmentGoal : TenantEntity
{
    public Guid SportEnrollmentId { get; set; }
    public Guid SportId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public required string Title { get; set; }
    public required string Target { get; set; }
    public DateOnly ReviewOn { get; set; }
    public string State { get; set; } = "Draft";
    public int Revision { get; set; } = 1;
    public DateTimeOffset? PublishedAtUtc { get; set; }
}

public sealed class PlayerGoalChange : TenantEntity
{
    public Guid GoalId { get; set; }
    public Guid SportId { get; set; }
    public Guid ActorUserId { get; set; }
    public required string ActorName { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
    public int Revision { get; set; }
    public required string Action { get; set; }
    public string? Note { get; set; }
    public required string SnapshotJson { get; set; }
}
