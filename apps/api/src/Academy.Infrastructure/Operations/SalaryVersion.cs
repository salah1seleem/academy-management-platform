using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Operations;

// Approved monthly rate history. Employee.MonthlySalary remains the untouched legacy reference.
public sealed class SalaryVersion : TenantEntity
{
    public Guid EmployeeId { get; set; }
    public Guid BranchId { get; set; }
    public int Revision { get; set; }
    public Guid? PreviousVersionId { get; set; }
    public required string Kind { get; set; } // Initial, LegacyBaseline, Change
    public DateOnly EffectiveFrom { get; set; }
    public decimal MonthlySalary { get; set; }
    public required string Currency { get; set; }
    public Guid ApprovedByMembershipId { get; set; }
    public Guid ActorUserId { get; set; }
    public string? Note { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
}
