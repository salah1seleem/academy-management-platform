using Academy.Infrastructure.Structure;
namespace Academy.Infrastructure.Operations;
// File provenance records the real uploader, including a platform operator.
// It does not manufacture a historical coach or an academy-owner membership.
public sealed class ImportedHistoricalMatch : TenantEntity
{
    public Guid JobId {get;set;}
    public Guid MatchId {get;set;}
    public Guid ActorUserId {get;set;}
    public required string SourceSystem {get;set;}
    public required string SourceReference {get;set;}
    public required string OriginalPayload {get;set;}
    public required string SourceFingerprint {get;set;}
}
