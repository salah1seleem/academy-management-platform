using Academy.Infrastructure.Structure;
namespace Academy.Infrastructure.Operations;

// An append-only correction of the reviewed opening only. Never rewrites cash,
// salary, the original approved source, or the employee's external account.
public sealed class PayrollPayableCorrection : TenantEntity
{
    public Guid StatementId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid BranchId { get; set; }
    public required string Currency { get; set; }
    public int Revision { get; set; }
    public decimal PreviousAmount { get; set; }
    public decimal TargetAmount { get; set; }
    public decimal Delta { get; set; }
    public decimal PaidAtApproval { get; set; }
    public Guid ActorMembershipId { get; set; }
    public Guid ActorUserId { get; set; }
    public required string Reference { get; set; }
    public string? Note { get; set; }
    public required string QuoteHash { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
}
