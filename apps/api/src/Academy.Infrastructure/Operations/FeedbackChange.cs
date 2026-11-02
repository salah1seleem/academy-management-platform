using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Operations;

// Private case evidence, never projected into guardian options or delegated group summaries.
public sealed class FeedbackChange : TenantEntity
{
    public Guid FeedbackId { get; set; }
    public Guid ActorMembershipId { get; set; }
    public string? BeforeSubject { get; set; }
    public string? AfterSubject { get; set; }
    public string? BeforeUrgency { get; set; }
    public string? AfterUrgency { get; set; }
    public Guid? InvestigationId { get; set; }
    public Guid? InvestigatorMembershipId { get; set; }
    public Guid? RequestId { get; set; }
    public string? RequestHash { get; set; }
    public required string Action { get; set; }
    public required string FromStatus { get; set; }
    public required string ToStatus { get; set; }
    public DateOnly? BeforeDueOn { get; set; }
    public DateOnly? AfterDueOn { get; set; }
    public string? InternalNote { get; set; }
    public string? PublicResponse { get; set; }
}
