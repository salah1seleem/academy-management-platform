using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Tenancy;

public sealed class SwimmingBranchPolicy : TenantEntity
{
    public Guid SportId { get; set; }
    public Guid BranchId { get; set; }
    public bool Enabled { get; set; }
    public bool PrivateTrainingEnabled { get; set; }
    public bool OpenSwimEnabled { get; set; }
    public bool DayPassEnabled { get; set; }
}
public sealed class SportSettingsAudit : TenantEntity
{
    public Guid SportId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid ActorUserId { get; set; }
    public required string Before { get; set; }
    public required string After { get; set; }
}
