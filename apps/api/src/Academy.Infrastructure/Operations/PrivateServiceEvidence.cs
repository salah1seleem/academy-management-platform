using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Operations;

// A service allocation over an EXISTING receivable, not a second invoice or payment.
public sealed class PrivateServicePackage : TenantEntity
{
    public Guid DocumentId { get; set; }
    public Guid SportEnrollmentId { get; set; }
    public Guid BranchId { get; set; }
    public Guid SportId { get; set; }
    public int SessionCount { get; set; }
    public required string TrainingFormat { get; set; }
    public required string Currency { get; set; }
    public Guid ReviewedByMembershipId { get; set; }
}

// Explicit financial delivery approval. Neither employee nor child attendance creates this.
// AgreementId points to append-only terms/approval; changing future rates cannot change this unit.
public sealed class PrivateServiceDelivery : TenantEntity
{
    public Guid PackageId { get; set; }
    public Guid TrainingSessionId { get; set; }
    public Guid SportEnrollmentId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid CoachMembershipId { get; set; }
    public Guid AgreementId { get; set; }
    public Guid BranchId { get; set; }
    public Guid SportId { get; set; }
    public int Ordinal { get; set; }
    public DateOnly ServiceDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public Guid ApprovedByMembershipId { get; set; }
}
