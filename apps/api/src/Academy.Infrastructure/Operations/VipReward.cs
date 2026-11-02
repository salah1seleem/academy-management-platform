using Academy.Infrastructure.Structure;
namespace Academy.Infrastructure.Operations;

// One-off entitlement over DiscountRule, not points or a second pricing ledger.
public sealed class VipReward : TenantEntity
{
    public Guid GuardianId {get;set;}
    public Guid DiscountRuleId {get;set;}
    public Guid NoticeId {get;set;}
    public Guid IssuedByMembershipId {get;set;}
    public Guid RequestId {get;set;}
    public required string RequestHash {get;set;}
    public required string Message {get;set;}
    public DateOnly IssuedOn {get;set;}
    public DateOnly ExpiresOn {get;set;}
    public decimal Percentage {get;set;}
    public required string Currency {get;set;}
    public DateTimeOffset? CancelledAtUtc {get;set;}
    public Guid? CancelledByMembershipId {get;set;}
}
// Exactly one checkout; a family checkout may have several lines. Paid uses never release.
public sealed class VipRewardUse : TenantEntity
{
    public Guid RewardId {get;set;}
    public required string SourceKind {get;set;}
    public Guid SourceId {get;set;}
    public Guid ActorUserId {get;set;}
    public DateTimeOffset? UsedAtUtc {get;set;}
    public DateTimeOffset? ReleasedAtUtc {get;set;}
}
