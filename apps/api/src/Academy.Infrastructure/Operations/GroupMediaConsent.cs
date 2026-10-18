using Academy.Infrastructure.Structure;
namespace Academy.Infrastructure.Operations;

// A guardian's separate, authenticated choice for one child's group sharing.
// Append-only: withdrawing or giving consent again does not revive an old publication.
public sealed class GroupMediaConsent : TenantEntity
{
    public Guid GuardianPlayerLinkId { get; set; }
    public Guid PlayerId { get; set; }
    public Guid ActorUserId { get; set; }
    public int Revision { get; set; }
    public bool Allowed { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
}
public sealed class PrivateMediaConsentBasis : TenantEntity
{
    public Guid AssetId { get; set; }
    public Guid PlayerId { get; set; }
    public Guid ConsentId { get; set; }
}
// Withdrawal never overwrites the publication or permits restoring it in place.
public sealed class PrivateMediaWithdrawal : TenantEntity
{
    public Guid AssetId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
    public string? Note { get; set; }
}
