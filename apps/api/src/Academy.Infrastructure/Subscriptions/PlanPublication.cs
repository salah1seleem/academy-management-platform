using System.Text.Json;
using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Subscriptions;

public interface IPlanTermsSnapshot
{
    string? PlanNameSnapshot { get; set; }
    SubscriptionPlanType? PlanTypeSnapshot { get; set; }
    int? DurationDaysSnapshot { get; set; }
    int? SessionCountSnapshot { get; set; }
    int? PlanRevisionSnapshot { get; set; }
}

public sealed record PlanTerms(string Name, SubscriptionPlanType Type, int? Days, int? Sessions, int Revision)
{
    public static PlanTerms From(SubscriptionPlan p) => new(p.ArabicName, p.PlanType, p.DurationDays, p.SessionCount, p.Revision);
    // Pre-V2 requests have no provable historical terms; preserve legacy behavior explicitly.
    public static PlanTerms From(IPlanTermsSnapshot snapshot, SubscriptionPlan legacy) => snapshot.PlanTypeSnapshot.HasValue
        ? new(snapshot.PlanNameSnapshot!, snapshot.PlanTypeSnapshot.Value, snapshot.DurationDaysSnapshot, snapshot.SessionCountSnapshot, snapshot.PlanRevisionSnapshot ?? 0)
        : From(legacy);
    public void Capture(IPlanTermsSnapshot target)
    {
        target.PlanNameSnapshot = Name; target.PlanTypeSnapshot = Type; target.DurationDaysSnapshot = Days;
        target.SessionCountSnapshot = Sessions; target.PlanRevisionSnapshot = Revision;
    }
}

public sealed class PlanBranchPublication : TenantEntity
{
    public Guid SubscriptionPlanId { get; set; }
    public Guid BranchId { get; set; }
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
}

public sealed class PlanPriceVersion : TenantEntity
{
    public Guid SubscriptionPlanId { get; set; }
    public int Revision { get; set; }
    public required string ArabicName { get; set; }
    public SubscriptionPlanType PlanType { get; set; }
    public decimal Price { get; set; }
    public required string Currency { get; set; }
    public int? DurationDays { get; set; }
    public int? SessionCount { get; set; }
    public bool AllBranches { get; set; }
    public required string BranchIdsJson { get; set; }
    public bool Published { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset EffectiveAtUtc { get; set; }

    public static PlanPriceVersion Create(SubscriptionPlan plan, Guid[] branches, Guid? actor, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), AcademyId = plan.AcademyId, SubscriptionPlanId = plan.Id, Revision = plan.Revision,
        ArabicName = plan.ArabicName, PlanType = plan.PlanType, Price = plan.Price, Currency = plan.Currency,
        DurationDays = plan.DurationDays, SessionCount = plan.SessionCount, AllBranches = plan.AllBranches,
        BranchIdsJson = JsonSerializer.Serialize(branches.Order()), Published = plan.IsActive,
        ActorUserId = actor, EffectiveAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now
    };
}
