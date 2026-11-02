using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Operations;

// A proposal and its approval are separate append-only records. The employee's
// base salary stays in Payroll; this snapshot is a reference, not another salary.
public sealed class CoachCompensationAgreement : TenantEntity
{
    public Guid EmployeeId { get; set; }
    public Guid CoachMembershipId { get; set; }
    public Guid BranchId { get; set; }
    public Guid SportId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public decimal BaseSalarySnapshot { get; set; }
    public required string Currency { get; set; }
    public required string VariableMethod { get; set; }
    public decimal VariableValue { get; set; }
    public required string Eligibility { get; set; }
    public required string BaseCoverage { get; set; }
    public string CalculationBasis { get; set; } = "NetAfterDiscountRefundExTaxBeforeGateway";
    public string CollectionAllocationPolicy { get; set; } = "ProportionalNetCollections";
    public string SettlementCycle { get; set; } = "Monthly";
    public string? Note { get; set; }
    public Guid CreatedByMembershipId { get; set; }
    public required string IdempotencyKey { get; set; }
}

public sealed class CoachCompensationApproval : TenantEntity
{
    public Guid AgreementId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid BranchId { get; set; }
    public Guid SportId { get; set; }
    public int Revision { get; set; }
    public Guid ApprovedByMembershipId { get; set; }
    public DateTimeOffset ApprovedAtUtc { get; set; }
}
