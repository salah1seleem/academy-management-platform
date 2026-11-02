using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Operations;

// Source journal for the existing payroll adjustments/payables, not a general ledger or second payroll.
public sealed class PrivateCompensationEntry : TenantEntity
{
    public Guid DeliveryId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid BranchId { get; set; }
    public Guid SportId { get; set; }
    public string Component { get; set; } = "PrivateVariable";
    public int Revision { get; set; }
    public required string Kind { get; set; } // Accrual, Review, Reversal, Correction, PrivateFinal
    public decimal PostedBefore { get; set; }
    public decimal? TargetAmount { get; set; }
    public decimal Delta { get; set; } // Review is zero and has NO payroll adjustment.
    public Guid? PayrollAdjustmentId { get; set; }
    public Guid? FinalStatementId { get; set; }
    public Guid? ReviewEntryId { get; set; }
    public DateOnly DueDate { get; set; }
    public required string Currency { get; set; }
    public DateTimeOffset CutoffAtUtc { get; set; }
    public required string SourceSnapshot { get; set; }
    public required string FactsHash { get; set; }
    public required string QuoteHash { get; set; }
    public required string RequestHash { get; set; }
    public required string RequestKey { get; set; }
    public Guid ActorMembershipId { get; set; }
    public Guid ActorUserId { get; set; }
}
