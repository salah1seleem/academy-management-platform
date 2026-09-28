using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Content;

public enum NutritionCategory { Breakfast = 1, Lunch = 2, Dinner = 3 }
public enum NutritionDataStatus { DemoUnreviewed = 1, Reviewed = 2 }
public enum MedicalRecordType { Injury = 1, Consultation = 2 }
public enum MedicalRecordStatus { Open = 1, Monitoring = 2, Resolved = 3 }
public enum PlayerMediaType { Image = 1, Video = 2 }

public sealed class SportCatalogItem : TenantEntity
{
    public Guid SportId { get; set; }
    public required string ArabicName { get; set; }
    public string? EnglishName { get; set; }
    public string? ArabicDescription { get; set; }
    public required string ImageReference { get; set; }
    public decimal? DisplayPrice { get; set; }
    public string? Currency { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public int DisplayOrder { get; set; }
    public Sport Sport { get; set; } = null!;
}

public sealed class NutritionItem : TenantEntity
{
    public required string ArabicName { get; set; }
    public required string ArabicDescription { get; set; }
    public required string ImageReference { get; set; }
    public required string ServingDescription { get; set; }
    public decimal? Calories { get; set; }
    public decimal? ProteinGrams { get; set; }
    public decimal? CarbohydratesGrams { get; set; }
    public decimal? FatGrams { get; set; }
    public NutritionDataStatus DataStatus { get; set; }
    public string? SourceDescription { get; set; }
    public ICollection<NutritionCategoryLink> Categories { get; set; } = [];
}

public sealed class NutritionCategoryLink
{
    public Guid AcademyId { get; set; }
    public Guid NutritionItemId { get; set; }
    public NutritionCategory Category { get; set; }
    public int DisplayOrder { get; set; }
    public NutritionItem NutritionItem { get; set; } = null!;
}

public sealed class PlayerMedicalRecord : TenantEntity
{
    public Guid PlayerId { get; set; }
    public MedicalRecordType RecordType { get; set; }
    public required string ArabicTitle { get; set; }
    public DateOnly RecordDate { get; set; }
    public required string ArabicDescription { get; set; }
    public MedicalRecordStatus Status { get; set; }
    public string? StaffNotes { get; set; }
    public string? GuardianVisibleNotes { get; set; }
    public bool IsPublishedToGuardian { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public Player Player { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public ApplicationUser UpdatedByUser { get; set; } = null!;
}

public sealed class PlayerMedia : TenantEntity
{
    public Guid PlayerId { get; set; }
    public PlayerMediaType MediaType { get; set; }
    public required string MediaReference { get; set; }
    public string? ThumbnailReference { get; set; }
    public string? ArabicCaption { get; set; }
    public DateTimeOffset? CapturedAtUtc { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPublishedToGuardian { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Player Player { get; set; } = null!;
    public ApplicationUser CreatedByUser { get; set; } = null!;
}
