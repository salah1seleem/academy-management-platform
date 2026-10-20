using Academy.Infrastructure.Structure;
namespace Academy.Infrastructure.Operations;
public sealed class MonthlyPlayerStar : TenantEntity
{
    public Guid CoachMembershipId {get;set;}
    public Guid TeamId {get;set;}
    public Guid PlayerId {get;set;}
    public DateOnly Month {get;set;}
    public int Slot {get;set;}
    public Guid RequestId {get;set;}
}
