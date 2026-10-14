using Academy.Infrastructure.Identity;

namespace Academy.Infrastructure.Tenancy;

public sealed class AcademyMembership
{
    public Guid Id { get; set; }
    public Guid AcademyId { get; set; }
    public Guid UserId { get; set; }
    public AcademyRole Role { get; set; }
    // Additional operational role/scope contexts reuse this membership's grants.
    // They are not new users, employee records, or platform roles.
    public Guid? PrimaryMembershipId { get; set; }
    public AcademyMembership? PrimaryMembership { get; set; }
    public string? AccessContextName { get; set; }
    public string? NormalizedContextName { get; set; }
    public int ContextRevision { get; set; } = 1;
    public string? JobTitle { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AllBranches { get; set; } = true;
    public bool AllSports { get; set; } = true;
    public bool UseActionGrants { get; set; }
    public Guid? PermissionTemplateId { get; set; }
    public int? PermissionTemplateRevision { get; set; }
    public bool PermissionTemplateCustomized { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Academy Academy { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}
