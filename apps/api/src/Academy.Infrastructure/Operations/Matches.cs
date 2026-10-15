using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Operations;

public sealed class AcademyTournament : TenantEntity
{
    public required string Name { get; set; }
    public Guid SportId { get; set; }
    public decimal Fee { get; set; }
    public required string Currency { get; set; }
}
public sealed class MatchTeam : TenantEntity
{
    public required string Name { get; set; }
    public Guid SportId { get; set; }
    public Guid BranchId { get; set; }
    public Guid AgeCategoryId { get; set; }
    public Guid CoachMembershipId { get; set; }
}
public sealed class MatchTeamPlayer : TenantEntity
{
    public Guid TeamId { get; set; }
    public Guid EnrollmentId { get; set; }
}
public sealed class AcademyMatch : TenantEntity
{
    public Guid? CopiedFromMatchId { get; set; }
    public Guid? CopiedByMembershipId { get; set; }
    public string? CopyRequestKey { get; set; }
    public string? CopyRequestHash { get; set; }
    public Guid TournamentId { get; set; }
    public Guid TeamId { get; set; }
    public required string Opponent { get; set; }
    public DateOnly MatchDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public required string Venue { get; set; }
    public string? Notes { get; set; }
    public int? OurScore { get; set; }
    public int? OpponentScore { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public string? PublishedFingerprint { get; set; }
}
// Audience frozen on first publication. Later team joiners do not inherit announcements.
public sealed class MatchParticipant : TenantEntity
{
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public bool IsBench { get; set; }
}
public sealed class TournamentPayment : TenantEntity
{
    public Guid TournamentId { get; set; }
    public Guid EnrollmentId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public required string TournamentName { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public required string IdempotencyKey { get; set; }
    public string Status { get; set; } = "Pending";
    public Guid? ProofAssetId { get; set; }
    public string? ReviewReason { get; set; }
    public Guid? ConfirmedByUserId { get; set; }
    public DateTimeOffset? ConfirmedAtUtc { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? PaymentMethod { get; set; }
}
