using Academy.Infrastructure.Structure;
namespace Academy.Infrastructure.Operations;
public sealed class NoticeTemplate : TenantEntity
{
    public required string Key { get; set; }
    public required string Message { get; set; }
}
public sealed class NoticeDispatch : TenantEntity
{
    public Guid ActorMembershipId { get; set; }
    public Guid RequestId { get; set; }
    public required string RequestHash { get; set; }
    public required string Kind { get; set; }
    public int RecipientCount { get; set; }
}
