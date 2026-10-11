using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Operations;

public sealed class PrivateAsset : TenantEntity
{
    public Guid UploadedByUserId { get; set; }
    public required string ContentType { get; set; }
    public required string StorageName { get; set; }
    public long Length { get; set; }
    public Guid? PlayerId { get; set; }
    public Guid? TrainingGroupId { get; set; }
    public required string Purpose { get; set; }
    public string? Caption { get; set; }
}
public sealed class AcademyTransferSettings : TenantEntity
{
    public string? InstaPayAddress { get; set; }
    public string? WalletNumber { get; set; }
    public string? RecipientName { get; set; }
}
// Snapshot of the group roster at upload; later joiners do not inherit old child media.
public sealed class PrivateAssetAudience : TenantEntity
{
    public Guid AssetId { get; set; }
    public Guid PlayerId { get; set; }
}
public sealed class PaymentProof : TenantEntity
{
    public Guid PaymentRequestId { get; set; }
    public Guid AssetId { get; set; }
    public Guid SubmittedByUserId { get; set; }
    public required string Status { get; set; }
    public string? ReviewReason { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
}
public sealed class DiscountRule : TenantEntity
{
    public required string ArabicName { get; set; }
    public decimal Percentage { get; set; }
    public bool IsSiblingOffer { get; set; }
    public required string Reason { get; set; }
}
public sealed class PlayerDiscountLink : TenantEntity
{
    public Guid PlayerId { get; set; }
    public Guid DiscountRuleId { get; set; }
}
public sealed class CollectionRefund : TenantEntity
{
    public Guid CollectionId { get; set; }
    public Guid PlayerId { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public string? Reason { get; set; }
    public Guid PerformedByUserId { get; set; }
    public required string IdempotencyKey { get; set; }
}
public sealed class ProductSale : TenantEntity
{
    public Guid SportId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid ProductId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public Guid? PlayerId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public required string ProductName { get; set; }
    public required string SportName { get; set; }
    public string? BranchName { get; set; }
    public required string Status { get; set; }
    public string? PaymentMethod { get; set; }
    public Guid? ProofAssetId { get; set; }
    public Guid? ConfirmedByUserId { get; set; }
    public DateTimeOffset? ConfirmedAtUtc { get; set; }
    public string? ReviewReason { get; set; }
    public required string IdempotencyKey { get; set; }
}
public sealed class AcademyNotice : TenantEntity
{
    public Guid UserId { get; set; }
    public required string Message { get; set; }
    public bool IsRead { get; set; }
}
public sealed class AcademyNews : TenantEntity
{
    public required string Title { get; set; }
    public required string Body { get; set; }
    public Guid? ImageAssetId { get; set; }
    public int VisibleDays { get; set; } = 7;
    public DateTimeOffset? PublishedAtUtc { get; set; }
}
public sealed class StaffGrant : TenantEntity
{
    public Guid MembershipId { get; set; }
    public required string Permission { get; set; }
    public bool Allowed { get; set; }
}
public sealed class StaffWorkSchedule : TenantEntity
{
    public Guid MembershipId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int LateThresholdMinutes { get; set; }
    public decimal LateDeduction { get; set; }
}
public sealed class StaffDailyAttendance : TenantEntity
{
    public Guid MembershipId { get; set; }
    public DateOnly WorkDate { get; set; }
    public required string Status { get; set; }
    public TimeOnly? CheckIn { get; set; }
    public TimeOnly? CheckOut { get; set; }
    public int LateMinutes { get; set; }
    public decimal SuggestedDeduction { get; set; }
    public decimal ApprovedDeduction { get; set; }
    public string? DeductionReason { get; set; }
    public Guid RecordedByUserId { get; set; }
}
