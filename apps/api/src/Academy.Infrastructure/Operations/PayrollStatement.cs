using Academy.Infrastructure.Structure;
namespace Academy.Infrastructure.Operations;

// Approved compensation is not a payment and does not duplicate the payroll engine.
public sealed class PayrollStatement : TenantEntity
{
    // A reviewed, positive private-service residual after employment has ended.
    public Guid? PrivateReviewId { get; set; }
    public string Kind { get; set; } = "MonthlySalary";
    // Opening covers only unrecorded obligations BEFORE this first-of-month boundary.
    // Month reserves the preceding period; it is never presented as earned salary.
    public DateOnly? OpeningAsOf { get; set; }
    public string? OpeningReference { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid BranchId { get; set; }
    public DateOnly Month { get; set; }
    public required string EmployeeName { get; set; }
    public required string Currency { get; set; }
    public decimal MonthlySalary { get; set; }
    public int ContractDays { get; set; }
    public decimal BaseDue { get; set; }
    public decimal Deductions { get; set; }
    public decimal Additions { get; set; }
    public decimal PrivateCompensation { get; set; }
    public decimal NetDue { get; set; }
    public required string SourceSnapshot { get; set; }
    public required string SourceHash { get; set; }
    public Guid ApprovedByMembershipId { get; set; }
    public Guid ActorUserId { get; set; }
    public string? Note { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
}

// One settlement against an approved statement: direct payout or allocation of
// prior advance cash. Neither also creates a legacy settlement.
public sealed class PayrollDisbursement : TenantEntity
{
    // Null = direct cash payout. Otherwise settlement from an already-paid advance,
    // not a second cash movement. Existing immutable rows remain direct payouts.
    public Guid? AdvanceId { get; set; }
    public decimal? AdvanceAppliedBefore { get; set; }
    public decimal? AdvanceRemainingAfter { get; set; }
    public Guid StatementId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid BranchId { get; set; }
    public int Sequence { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidBefore { get; set; }
    public decimal RemainingAfter { get; set; }
    public required string Currency { get; set; }
    public Guid ActorMembershipId { get; set; }
    public Guid ActorUserId { get; set; }
    public string? Reference { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
}
