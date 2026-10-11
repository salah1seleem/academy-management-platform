using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.People;

public enum ImportJobState { Queued, Running, Completed, Failed, Expired }
public sealed class AcademyImportJob : TenantEntity
{
    public Guid ActorUserId { get; set; }
    public Guid MembershipId { get; set; }
    public required string SourceSystem { get; set; }
    public required string FileHash { get; set; }
    public required string ReferenceStamp { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string ApprovedBranches { get; set; }
    public DateOnly CutoffDate { get; set; }
    public ImportJobState State { get; set; }
    public byte[]? StagedFile { get; set; }
    public int RowCount { get; set; }
    public int PublishedRows { get; set; }
    public int ReplayedRows { get; set; }
    public int FinanciallyIncomplete { get; set; }
    public bool GuardianLinksApproved { get; set; }
    public bool OpeningBalancesApproved { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public int Attempts { get; set; }
    public required string GuardianResolutions { get; set; }
    // Exact approved source rows, including rows replayed from an earlier job.
    public string[] LegacyEnrollmentIds { get; set; } = [];
    public string? ErrorCode { get; set; }
}
// Persistent source mappings, never a fuzzy match on a child's name or telephone.
public sealed class ImportedEnrollment : TenantEntity
{
    public Guid JobId { get; set; }
    public required string SourceSystem { get; set; }
    public required string LegacyPlayerId { get; set; }
    public required string LegacyEnrollmentId { get; set; }
    public required string PlayerIdentityHash { get; set; }
    public required string RowHash { get; set; }
    public required string AgreementSnapshot { get; set; }
    public Guid PlayerId { get; set; }
    public Guid GuardianId { get; set; }
    public Guid SportEnrollmentId { get; set; }
    public Guid SubscriptionPeriodId { get; set; }
    public Guid BranchId { get; set; }
}
public sealed class ImportedHousehold : TenantEntity
{
    public required string SourceSystem { get; set; }
    public required string HouseholdKey { get; set; }
    public required string IdentityHash { get; set; }
    public Guid GuardianId { get; set; }
    public Guid JobId { get; set; }
}
