using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.People;

public enum NewEnrollmentRequestType { ExistingChildNewSport = 1, NewChild = 2 }
public enum NewEnrollmentRequestStatus { Pending = 1, UnderReview = 2, Approved = 3, Rejected = 4, Cancelled = 5 }

public sealed class NewEnrollmentRequest : TenantEntity
{
    public Guid RequestedByGuardianUserId { get; set; }
    public NewEnrollmentRequestType RequestType { get; set; }
    public Guid? ExistingPlayerId { get; set; }
    public string? NewChildArabicName { get; set; }
    public DateOnly? NewChildDateOfBirth { get; set; }
    public Gender? NewChildGender { get; set; }
    public Guid SportId { get; set; }
    public Guid PreferredBranchId { get; set; }
    public string? Notes { get; set; }
    public NewEnrollmentRequestStatus Status { get; set; } = NewEnrollmentRequestStatus.Pending;
    public string? AdminNotes { get; set; }
    public string? GuardianVisibleReason { get; set; }
    public Guid? ApprovedPlayerId { get; set; }
    public Guid? CreatedSportEnrollmentId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public required string IdempotencyKey { get; set; }

    public ApplicationUser RequestedByGuardianUser { get; set; } = null!;
    public Player? ExistingPlayer { get; set; }
    public Sport Sport { get; set; } = null!;
    public Branch PreferredBranch { get; set; } = null!;
    public Player? ApprovedPlayer { get; set; }
    public SportEnrollment? CreatedSportEnrollment { get; set; }
    public ApplicationUser? ReviewedByUser { get; set; }
}
