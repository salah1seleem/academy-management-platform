using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Tenancy;

public sealed class MembershipBranchAccess : TenantEntity
{
    public Guid MembershipId { get; set; }
    public Guid BranchId { get; set; }
}
public sealed class StaffActionGrant : TenantEntity
{
    public Guid MembershipId { get; set; }
    public required string Module { get; set; }
    public bool Read { get; set; }
    public bool Add { get; set; }
    public bool Edit { get; set; }
    public bool Delete { get; set; }
}
public sealed class StaffAccessAudit : TenantEntity
{
    public Guid ActorUserId { get; set; }
    public Guid MembershipId { get; set; }
    public required string Before { get; set; }
    public required string After { get; set; }
}
