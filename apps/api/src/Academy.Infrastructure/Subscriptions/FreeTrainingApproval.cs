using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Subscriptions;

// An authorization to provide service, never a collection, receipt or payment.
public sealed class FreeTrainingApproval : TenantEntity
{
    public Guid SportEnrollmentId { get; set; }
    public Guid SubscriptionPlanId { get; set; }
    public Guid BranchId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid ActorMembershipId { get; set; }
    public decimal WaivedAmount { get; set; }
    public required string Currency { get; set; }
    public required string Reason { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string RequestHash { get; set; }
    public required string QuoteHash { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? InitialSessions { get; set; }
    public required string PlanTermsJson { get; set; }
}
