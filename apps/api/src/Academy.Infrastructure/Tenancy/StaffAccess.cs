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
public sealed class MembershipSportAccess : TenantEntity
{
    public Guid MembershipId { get; set; }
    public Guid SportId { get; set; }
}
public sealed class StaffAccessAudit : TenantEntity
{
    public Guid ActorUserId { get; set; }
    public Guid MembershipId { get; set; }
    public required string Before { get; set; }
    public required string After { get; set; }
}
// Reusable starting permissions, not a live authorization role. The applied
// per-membership matrix and its correlated scope remain authoritative.
public sealed class StaffRoleTemplate : TenantEntity
{
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public string? Description { get; set; }
    public required string ActionsJson { get; set; }
    public int Revision { get; set; } = 1;
}
public sealed class StaffRoleTemplateAudit : TenantEntity
{
    public Guid TemplateId { get; set; }
    public Guid ActorUserId { get; set; }
    public required string Before { get; set; }
    public required string After { get; set; }
}
