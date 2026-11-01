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
    public bool CoachCanManageMatches {get;set;}
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
    public bool ResultFinalized {get;set;}
    public DateTimeOffset? ResultPublishedAtUtc {get;set;}
    public int ResultRevision {get;set;}
    public string? ResultFingerprint {get;set;}
    public Guid? CreationRequestId {get;set;}
    public Guid? CreatedByMembershipId {get;set;}
    public string? CreationHash {get;set;}

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
// Append-only assignment history, independent of the team's current coach.
public sealed class MatchCoachAssignment : TenantEntity
{
    public Guid MatchId { get; set; }
    public Guid CoachMembershipId { get; set; }
    public Guid AssignedByMembershipId { get; set; }
    public int Sequence { get; set; }
    public required string Source { get; set; }
    public string? Note { get; set; }
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

// Sporting membership independent of fee requests and immutable published match audiences.
public sealed class TournamentRegistration : TenantEntity
{
    public Guid TournamentId { get; set; }
    public Guid EnrollmentId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ImportJobId { get; set; }
}

public sealed class MatchGoalSlot : TenantEntity
{
    public Guid MatchId {get;set;}
    public int Ordinal {get;set;}
    public Guid? PlayerId {get;set;}
    public string Kind {get;set;}="Unknown";
}
public sealed class MatchResultChange : TenantEntity
{
    public Guid MatchId {get;set;}
    public Guid ActorMembershipId {get;set;}
    public int Revision {get;set;}
    public required string Before {get;set;}
    public required string After {get;set;}
}
public sealed class MatchTeamPermissionAudit : TenantEntity
{
    public Guid TeamId {get;set;}
    public Guid ActorMembershipId {get;set;}
    public bool Before {get;set;}
    public bool After {get;set;}
}
