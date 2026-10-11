using System.Text.Json;
using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Subscriptions;

public enum AcademyServiceKind { Training = 1, DayPass = 2, OpenSwim = 3, FacilityBooking = 4, Event = 5 }
public enum TrainingFormat { Group = 1, Private = 2, SemiPrivate = 3, Season = 4 }

public interface IPlanTermsSnapshot
{
    string? PlanNameSnapshot { get; set; }
    SubscriptionPlanType? PlanTypeSnapshot { get; set; }
    int? DurationDaysSnapshot { get; set; }
    int? SessionCountSnapshot { get; set; }
    int? PlanRevisionSnapshot { get; set; }
    int? DurationMonthsSnapshot { get; set; }
    AcademyServiceKind? ServiceKindSnapshot { get; set; }
    TrainingFormat? TrainingFormatSnapshot { get; set; }
}

public sealed record PlanTerms(string Name, SubscriptionPlanType Type, int? Days, int? Sessions, int Revision,
    int? Months = null, AcademyServiceKind ServiceKind = AcademyServiceKind.Training, TrainingFormat Format = TrainingFormat.Group)
{
    public static PlanTerms From(SubscriptionPlan p) => new(p.ArabicName, p.PlanType, p.DurationDays, p.SessionCount, p.Revision, p.DurationMonths, p.ServiceKind, p.TrainingFormat);
    // Pre-V2 requests have no provable historical terms; preserve legacy behavior explicitly.
    public static PlanTerms From(IPlanTermsSnapshot snapshot, SubscriptionPlan legacy) => snapshot.PlanTypeSnapshot.HasValue
        ? new(snapshot.PlanNameSnapshot!, snapshot.PlanTypeSnapshot.Value, snapshot.DurationDaysSnapshot, snapshot.SessionCountSnapshot, snapshot.PlanRevisionSnapshot ?? 0,
            snapshot.DurationMonthsSnapshot, snapshot.ServiceKindSnapshot ?? AcademyServiceKind.Training, snapshot.TrainingFormatSnapshot ?? TrainingFormat.Group)
        : From(legacy);
    // Inclusive start; the following renewal starts at the exclusive service boundary.
    public DateOnly? EndDate(DateOnly start) => Months is int months ? start.AddMonths(months).AddDays(-1)
        : Days is int days ? start.AddDays(days - 1) : null;
    public void Capture(IPlanTermsSnapshot target)
    {
        target.PlanNameSnapshot = Name; target.PlanTypeSnapshot = Type; target.DurationDaysSnapshot = Days;
        target.SessionCountSnapshot = Sessions; target.PlanRevisionSnapshot = Revision;
        target.DurationMonthsSnapshot = Months; target.ServiceKindSnapshot = ServiceKind; target.TrainingFormatSnapshot = Format;
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
    public int? DurationMonths { get; set; }
    public AcademyServiceKind ServiceKind { get; set; }
    public TrainingFormat TrainingFormat { get; set; }
    public decimal? ReferenceMonthlyPrice { get; set; }
    public int? ChargedMonths { get; set; }
    public bool AllBranches { get; set; }
    public required string BranchIdsJson { get; set; }
    public bool Published { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset EffectiveAtUtc { get; set; }

    public static PlanPriceVersion Create(SubscriptionPlan plan, Guid[] branches, Guid? actor, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), AcademyId = plan.AcademyId, SubscriptionPlanId = plan.Id, Revision = plan.Revision,
        ArabicName = plan.ArabicName, PlanType = plan.PlanType, Price = plan.Price, Currency = plan.Currency,
        DurationDays = plan.DurationDays, SessionCount = plan.SessionCount, DurationMonths = plan.DurationMonths,
        ServiceKind = plan.ServiceKind, TrainingFormat = plan.TrainingFormat, ReferenceMonthlyPrice = plan.ReferenceMonthlyPrice, ChargedMonths = plan.ChargedMonths, AllBranches = plan.AllBranches,
        BranchIdsJson = JsonSerializer.Serialize(branches.Order()), Published = plan.IsActive,
        ActorUserId = actor, EffectiveAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now
    };
}
