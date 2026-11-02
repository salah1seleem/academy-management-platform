using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Operations;
public sealed class CommunityCampaign : TenantEntity
{
    public required string InternalName { get; set; }
    public required string Advertiser { get; set; }
    public int CurrentRevision { get; set; }
}
public sealed class CommunityCampaignRevision : TenantEntity
{
    public required string Advertiser { get; set; }
    public Guid CampaignId { get; set; }
    public int Revision { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public required string ButtonText { get; set; }
    public Guid? ImageAssetId { get; set; }
    public required string ImageAlt { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public DateTimeOffset EndsAtUtc { get; set; }
    public int Priority { get; set; }
    public required string State { get; set; }
    public Guid CreatedByMembershipId { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
}
public sealed class CommunityCampaignTarget : TenantEntity
{
    public Guid RevisionId { get; set; }
    public Guid BranchId { get; set; }
    public required string Decision { get; set; }
}
public sealed class CommunityCampaignReview : TenantEntity
{
    public Guid RevisionId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid ActorMembershipId { get; set; }
    public required string Decision { get; set; }
    public string? Note { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
}
public sealed class GuardianSponsorPreference : TenantEntity
{
    public Guid UserId { get; set; }
    public DateTimeOffset? AdultDeclaredAtUtc { get; set; }
    public bool InAppOptIn { get; set; }
    public int ConsentVersion { get; set; } = 1;
}
public sealed class CommunityAdEvent : TenantEntity
{
    public Guid RevisionId { get; set; }
    public Guid BranchId { get; set; }
    public Guid UserId { get; set; }
    public DateOnly ViewDate { get; set; }
    public required string Kind { get; set; }
}
