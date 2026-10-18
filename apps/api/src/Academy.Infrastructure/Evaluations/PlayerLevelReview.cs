using Academy.Infrastructure.Structure;
namespace Academy.Infrastructure.Evaluations;
// Evidence of an explicit saved review, not reconstructed historic promotions.
public sealed class PlayerLevelReview : TenantEntity
{
    public Guid SportEnrollmentId { get; set; }
    public Guid SportId { get; set; }
    public int Revision { get; set; }
    public Guid? FromLevelId { get; set; }
    public Guid? ToLevelId { get; set; }
    public string? FromLevelName { get; set; }
    public string? ToLevelName { get; set; }
    public Guid ActorUserId { get; set; }
    public required string ActorName { get; set; }
    public required string Action { get; set; }
    public string? PrivateNote { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
    public DateTimeOffset? PreviousRecordedReviewAtUtc { get; set; }
}
