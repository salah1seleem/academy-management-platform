using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Operations;

public sealed class Employee : TenantEntity
{
    public required string Name { get; set; }
    public required string Phone { get; set; }
    public required string JobTitle { get; set; }
    public Guid? MembershipId { get; set; }
    public Guid BranchId { get; set; }
    public decimal MonthlySalary { get; set; }
    public required string Currency { get; set; }
    public DateOnly ContractStart { get; set; }
    public DateOnly? ContractEnd { get; set; }
    public Guid? PhotoAssetId { get; set; }
}

public sealed class PayrollReason : TenantEntity
{
    public required string Name { get; set; }
}

public sealed class PayrollAdditionRequest : TenantEntity
{
    public Guid EmployeeId { get; set; }
    public Guid ReasonId { get; set; }
    public required string ReasonName { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public required string Status { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateOnly? DueDate { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public required string IdempotencyKey { get; set; }
}

public sealed class PayrollAdjustment : TenantEntity
{
    public Guid EmployeeId { get; set; }
    public required string Kind { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public string? Reason { get; set; }
    public Guid? AdditionRequestId { get; set; }
    public Guid PerformedByUserId { get; set; }
    public required string IdempotencyKey { get; set; }
}

// A paid month is a financial snapshot; later salary edits cannot rewrite it.
public sealed class PayrollSettlement : TenantEntity
{
    public Guid EmployeeId { get; set; }
    public DateOnly Month { get; set; }
    public decimal MonthlySalary { get; set; }
    public int ContractDays { get; set; }
    public decimal BaseDue { get; set; }
    public decimal Deductions { get; set; }
    public decimal Additions { get; set; }
    public decimal NetDue { get; set; }
    public required string Currency { get; set; }
    public DateTimeOffset PaidAtUtc { get; set; }
    public Guid PaidByUserId { get; set; }
    public required string IdempotencyKey { get; set; }
}
