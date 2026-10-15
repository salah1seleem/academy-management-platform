using Academy.Infrastructure.Structure;
namespace Academy.Infrastructure.Operations;

// Actual recorded cash before allocation. Never also a PayrollAdjustment or expense.
public sealed class PayrollAdvance : TenantEntity
{
    public Guid EmployeeId { get; set; }
    public Guid BranchId { get; set; }
    public required string Currency { get; set; }
    public decimal Amount { get; set; }
    public Guid ActorMembershipId { get; set; }
    public Guid ActorUserId { get; set; }
    public string? Reference { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
}
