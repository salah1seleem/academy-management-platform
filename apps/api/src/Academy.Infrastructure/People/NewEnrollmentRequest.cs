using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;

namespace Academy.Infrastructure.People;

public enum NewEnrollmentRequestType { ExistingChildNewSport = 1, NewChild = 2 }
public enum NewEnrollmentRequestStatus { Pending = 1, UnderReview = 2, Approved = 3, Rejected = 4, Cancelled = 5 }

public sealed class NewEnrollmentRequest : TenantEntity, IPlanTermsSnapshot
{
    public int? DurationMonthsSnapshot { get; set; }
    public AcademyServiceKind? ServiceKindSnapshot { get; set; }
    public TrainingFormat? TrainingFormatSnapshot { get; set; }
    public string? PlanNameSnapshot { get; set; }
    public SubscriptionPlanType? PlanTypeSnapshot { get; set; }
    public int? DurationDaysSnapshot { get; set; }
    public int? SessionCountSnapshot { get; set; }
    public int? PlanRevisionSnapshot { get; set; }
    public Guid RequestedByGuardianUserId { get; set; }
    public NewEnrollmentRequestType RequestType { get; set; }
    public Guid? ExistingPlayerId { get; set; }
    public string? NewChildArabicName { get; set; }
    public DateOnly? NewChildDateOfBirth { get; set; }
    public Gender? NewChildGender { get; set; }
    public Guid SportId { get; set; }
    public Guid? SubscriptionPlanId { get; set; }
    public decimal? OriginalAmount { get; set; }
    public decimal? FinalAmount { get; set; }
    public string? Currency { get; set; }
    public Guid? ProofAssetId { get; set; }
    public Guid? PaymentRequestId { get; set; }
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
