namespace Academy.Infrastructure.Structure;

// A manual, sport-specific admission level; not an inferred AI score or a certificate.
public sealed class TrainingLevel : TenantEntity
{
    public Guid SportId { get; set; }
    public required string ArabicName { get; set; }
    public int Rank { get; set; }
}
public sealed class GroupPlacementRevision : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid TrainingGroupId { get; set; }
    public Guid ActorUserId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public required string Snapshot { get; set; }
    public int Placed { get; set; }
}
