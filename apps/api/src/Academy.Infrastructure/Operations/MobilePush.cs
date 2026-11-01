using Academy.Infrastructure.Structure;
namespace Academy.Infrastructure.Operations;
public sealed class MobilePushDevice
{
    public Guid Id {get;set;}
    public Guid UserId {get;set;}
    public Guid MobileSessionId {get;set;}
    public string Provider {get;set;}="APNs";
    public string TokenHash {get;set;}="";
    public string ProtectedToken {get;set;}="";
    public bool IsActive {get;set;}
    public DateTimeOffset UpdatedAtUtc {get;set;}
}
public sealed class MobilePushDelivery:TenantEntity
{
    public Guid NoticeId {get;set;}
    public Guid DeviceId {get;set;}
    public string State {get;set;}="Pending";
    public int Attempts {get;set;}
    public DateTimeOffset NextAttemptAtUtc {get;set;}
    public DateTimeOffset? AcceptedAtUtc {get;set;}
}
