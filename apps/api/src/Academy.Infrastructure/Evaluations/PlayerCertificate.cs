using Academy.Infrastructure.Structure;
namespace Academy.Infrastructure.Evaluations;
// Private academy evidence, not an externally accredited qualification.
public sealed class PlayerAchievementCertificate : TenantEntity
{
    public Guid SourceLevelReviewId { get; set; }
    public Guid SportEnrollmentId { get; set; }
    public Guid SportId { get; set; }
    public required string AcademyName { get; set; }
    public required string PlayerName { get; set; }
    public required string SportName { get; set; }
    public required string LevelName { get; set; }
    public DateTimeOffset MilestoneAtUtc { get; set; }
    public DateOnly MilestoneOn { get; set; }
    public DateOnly IssuedOn { get; set; }
    public Guid ActorUserId { get; set; }
    public required string ActorName { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
}
public sealed class PlayerCertificateWithdrawal : TenantEntity
{
    public Guid CertificateId { get; set; }
    public Guid SportEnrollmentId { get; set; }
    public Guid SportId { get; set; }
    public string? FamilyNote { get; set; }
    public Guid ActorUserId { get; set; }
    public required string ActorName { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
}
