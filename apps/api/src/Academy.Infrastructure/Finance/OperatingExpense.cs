using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Finance;

public enum OperatingExpenseStatus { Draft = 1, Approved = 2, Posted = 3, Reversed = 4 }
public enum OperatingExpenseScope { Group = 1, Resource = 2 }

// A manual operating expense is not a payroll source or a payment receipt.
// Later recurring instances must reference their own unique occurrence.
public sealed class OperatingExpense : TenantEntity
{
    public Guid BranchId { get; set; }
    public OperatingExpenseScope Scope { get; set; }
    public Guid? TrainingGroupId { get; set; }
    public Guid? ResourceId { get; set; }
    public required string Category { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public DateOnly? PaidOn { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public string? Note { get; set; }
    public OperatingExpenseStatus Status { get; set; } = OperatingExpenseStatus.Draft;
    public Guid CreatedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public Guid? PostedByUserId { get; set; }
    public DateTimeOffset? PostedAtUtc { get; set; }
    public string? PostRequestKey { get; set; }
    public string? PostQuoteHash { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
}

// Stored snapshot; filtering a report never changes the allocation denominator.
// A null group means an explicit, visible unallocated residual.
public sealed class OperatingExpenseAllocation : TenantEntity
{
    public Guid ExpenseId { get; set; }
    public Guid? TrainingGroupId { get; set; }
    public int Weight { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
}
