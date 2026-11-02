using Academy.Infrastructure.Structure;
namespace Academy.Infrastructure.Operations;
public sealed class AudienceCommunication : TenantEntity
{
    public Guid? TrainingGroupId { get; set; }
    public Guid? TeamId { get; set; }
    public string? InviteUrl { get; set; }
    public bool CoachCanSend { get; set; }
    public Guid UpdatedByMembershipId { get; set; }
}
public sealed class AudienceLinkAudit : TenantEntity
{
    public Guid CommunicationId { get; set; }
    public Guid ActorMembershipId { get; set; }
    public bool Linked { get; set; }
    public bool CoachCanSend { get; set; }
}
public sealed class AudienceAnnouncement : TenantEntity
{
    public Guid? TrainingGroupId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? MatchId { get; set; }
    public Guid ActorMembershipId { get; set; }
    public Guid RequestId { get; set; }
    public required string Message { get; set; }
    public bool IncludeInvitation { get; set; }
    public int PlayerCount { get; set; }
    public int RecipientCount { get; set; }
}
