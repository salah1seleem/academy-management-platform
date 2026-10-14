using Academy.Infrastructure.Identity;

namespace Academy.Infrastructure.Tenancy;

public sealed class AcademyMembership
{
    public Guid Id { get; set; }
    public Guid AcademyId { get; set; }
    public Guid UserId { get; set; }
    public AcademyRole Role { get; set; }
    public string? JobTitle { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AllBranches { get; set; } = true;
    public bool AllSports { get; set; } = true;
    public bool UseActionGrants { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Academy Academy { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}
