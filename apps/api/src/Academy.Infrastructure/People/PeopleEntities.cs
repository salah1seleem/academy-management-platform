using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.People;

public enum Gender { Male = 1, Female = 2 }
public enum PreferredFoot { Right = 1, Left = 2, Both = 3 }
public enum EnrollmentStatus { Active = 1, Inactive = 2, Archived = 3 }

public sealed class GuardianProfile : TenantEntity
{
    public Guid UserId { get; set; }
    public required string DisplayName { get; set; }
    public string? ContactPhone { get; set; }
    public ApplicationUser User { get; set; } = null!;
}

public sealed class Player : TenantEntity
{
    public required string PlayerCode { get; set; }
    public required string ArabicName { get; set; }
    public string? EnglishName { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public string? PhotoReference { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public PreferredFoot? PreferredFoot { get; set; }
    public string? FootballPosition { get; set; }
    public string? Address { get; set; }
}

public sealed class GuardianPlayerLink : TenantEntity
{
    public Guid GuardianId { get; set; }
    public Guid PlayerId { get; set; }
    public string? RelationshipType { get; set; }
    public Guid CreatedByUserId { get; set; }
    public GuardianProfile Guardian { get; set; } = null!;
    public Player Player { get; set; } = null!;
}

public sealed class SportEnrollment : TenantEntity
{
    public Guid PlayerId { get; set; }
    public Guid SportId { get; set; }
    public Guid BranchId { get; set; }
    public Guid TrainingGroupId { get; set; }
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Active;
    public Player Player { get; set; } = null!;
    public Sport Sport { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
    public TrainingGroup TrainingGroup { get; set; } = null!;
}
