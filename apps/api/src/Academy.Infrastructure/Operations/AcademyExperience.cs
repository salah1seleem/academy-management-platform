using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Operations;

public sealed class AcademyExperience : TenantEntity
{
    public required string DisplayName { get; set; }
    public string PrimaryColor { get; set; } = "#D71935";
    public string SecondaryColor { get; set; } = "#171923";
    public byte[]? Logo { get; set; }
    public string? LogoContentType { get; set; }
    public bool CoachAttendanceEnabled { get; set; }
    // A legacy default of true is not an explicit permission grant.
    public bool CoachAttendancePolicyConfirmed { get; set; }
}

public sealed class AcademyPolicyAudit : TenantEntity
{
    public Guid ActorUserId { get; set; }
    public required string Policy { get; set; }
    public bool Before { get; set; }
    public bool After { get; set; }
}

// Global, narrowly scoped operator grant. Never issued by academy owners or client claims.
public sealed class PlatformBrandOperator
{
    public Guid UserId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class BrandingAudit
{
    public Guid Id { get; set; }
    public Guid AcademyId { get; set; }
    public Guid ActorUserId { get; set; }
    public required string DisplayName { get; set; }
    public required string PrimaryColor { get; set; }
    public required string SecondaryColor { get; set; }
    public DateTimeOffset AtUtc { get; set; }
}
