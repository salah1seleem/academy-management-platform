using Academy.Infrastructure.Structure;
namespace Academy.Infrastructure.Operations;

// Monthly approved employment terms, separate from salary rates and external trading contracts.
public sealed class ContractVersion : TenantEntity
{
    public Guid EmployeeId { get; set; }
    public Guid BranchId { get; set; }
    public int Revision { get; set; }
    public Guid? PreviousVersionId { get; set; }
    public required string Kind { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly ContractStart { get; set; }
    public DateOnly? ContractEnd { get; set; }
    public Guid ApprovedByMembershipId { get; set; }
    public Guid ActorUserId { get; set; }
    public string? Note { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
}
