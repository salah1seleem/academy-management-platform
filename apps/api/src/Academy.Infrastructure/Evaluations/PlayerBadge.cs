using Academy.Infrastructure.Structure;
namespace Academy.Infrastructure.Evaluations;
public interface IBadgeRecord { Guid ActorUserId{get;set;} string ActorName{get;set;} Guid RequestId{get;set;} string RequestHash{get;set;} }
// Explicitly published, immutable encouragement criteria. Never currency or player power.
public sealed class PlayerBadgeRule:TenantEntity,IBadgeRecord
{
    public Guid SportId{get;set;}
    public required string Title{get;set;}
    public required string Criterion{get;set;}
    public int Points{get;set;}
    public Guid ActorUserId{get;set;} public required string ActorName{get;set;} public Guid RequestId{get;set;} public required string RequestHash{get;set;}
}
public sealed class PlayerBadgeRuleRetirement:TenantEntity,IBadgeRecord
{
    public Guid RuleId{get;set;} public Guid SportId{get;set;}
    public string? Note{get;set;}
    public Guid ActorUserId{get;set;} public required string ActorName{get;set;} public Guid RequestId{get;set;} public required string RequestHash{get;set;}
}
public sealed class PlayerBadgeGrant:TenantEntity,IBadgeRecord
{
    public Guid RuleId{get;set;} public Guid SportEnrollmentId{get;set;} public Guid SportId{get;set;}
    public DateOnly ObservedOn{get;set;} public string? FamilyNote{get;set;}
    public Guid ActorUserId{get;set;} public required string ActorName{get;set;} public Guid RequestId{get;set;} public required string RequestHash{get;set;}
}
public sealed class PlayerBadgeRevocation:TenantEntity,IBadgeRecord
{
    public Guid GrantId{get;set;} public Guid SportEnrollmentId{get;set;} public Guid SportId{get;set;}
    public string? FamilyNote{get;set;}
    public Guid ActorUserId{get;set;} public required string ActorName{get;set;} public Guid RequestId{get;set;} public required string RequestHash{get;set;}
}
