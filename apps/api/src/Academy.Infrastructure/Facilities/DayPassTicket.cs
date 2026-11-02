using Academy.Infrastructure.Structure;
using Academy.Infrastructure.People;

namespace Academy.Infrastructure.Facilities;

public enum DayPassTicketStatus { PendingProof = 1, UnderReview = 2, Confirmed = 3, Rejected = 4 }

// A paid admission request is not a training subscription, attendance row, or
// proof that a transfer settled. Only explicit staff review confirms it.
public sealed class DayPassTicket : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid ResourceBookingId { get; set; }
    public Guid PlayerId { get; set; }
    public Guid SubscriptionPlanId { get; set; }
    public int PlanRevision { get; set; }
    public required string PlanName { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public Guid RequestedByUserId { get; set; }
    public required string RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public DayPassTicketStatus Status { get; set; } = DayPassTicketStatus.PendingProof;
    public Guid? ProofAssetId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public string? ReviewReason { get; set; }
    public Player Player { get; set; } = null!;
}
