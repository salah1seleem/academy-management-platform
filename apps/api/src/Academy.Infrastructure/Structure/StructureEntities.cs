using Academy.Infrastructure.Tenancy;

namespace Academy.Infrastructure.Structure;

public abstract class TenantEntity
{
    public Guid Id { get; set; }
    public Guid AcademyId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public uint Version { get; set; }
}

public sealed class Branch : TenantEntity
{
    public required string ArabicName { get; set; }
    public string? EnglishName { get; set; }
    public string? Address { get; set; }
}

public sealed class Sport : TenantEntity
{
    public required string ArabicName { get; set; }
    public string? EnglishName { get; set; }
}

public sealed class AgeCategory : TenantEntity
{
    public required string ArabicName { get; set; }
    public int? MinimumBirthYear { get; set; }
    public int? MaximumBirthYear { get; set; }
}

public sealed class TrainingGroup : TenantEntity
{
    public required string ArabicName { get; set; }
    public string? EnglishName { get; set; }
    public Guid BranchId { get; set; }
    public Guid SportId { get; set; }
    public Guid AgeCategoryId { get; set; }
    public int? Capacity { get; set; }
    public Branch Branch { get; set; } = null!;
    public Sport Sport { get; set; } = null!;
    public AgeCategory AgeCategory { get; set; } = null!;
}

public sealed class RecurringSchedule : TenantEntity
{
    public Guid TrainingGroupId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public TrainingGroup TrainingGroup { get; set; } = null!;
}

public sealed class StaffGroupAssignment : TenantEntity
{
    public Guid AcademyMembershipId { get; set; }
    public Guid TrainingGroupId { get; set; }
    public AcademyMembership AcademyMembership { get; set; } = null!;
    public TrainingGroup TrainingGroup { get; set; } = null!;
}
