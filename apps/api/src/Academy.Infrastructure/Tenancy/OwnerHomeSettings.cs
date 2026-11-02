using System.Text.Json;

namespace Academy.Infrastructure.Tenancy;

// Stored in the existing versioned academy configuration, not a second settings store.
public sealed record OwnerCollectionTarget(Guid? BranchId, DateOnly From, DateOnly To, decimal Amount);
public sealed record OwnerHighlightPortraits(string Coach="adult-neutral",string Member="adult-neutral",string Player="child-neutral");
public sealed record OwnerHomeSettings(bool QuickActionsAtTop = false, int? CoachAssignmentTarget = null,
    OwnerCollectionTarget[]? Targets = null, OwnerHighlightPortraits? HighlightPortraits = null)
{
    public OwnerCollectionTarget[] OrderedTargets() => (Targets ?? []).OrderBy(t => t?.BranchId)
        .ThenBy(t => t?.From).ThenBy(t => t?.To).ToArray();
    public string[] Validate()
    {
        var errors = new List<string>();
        if(HighlightPortraits is {} portraits &&
           (portraits.Coach is not ("adult-neutral" or "adult-male" or "adult-female" or "coach-01" or "coach-02") || portraits.Member is not ("adult-neutral" or "adult-male" or "adult-female" or "guardian-01" or "guardian-02") || portraits.Player is not ("child-neutral" or "child-male" or "child-female" or "child-01" or "child-02")))
            errors.Add("اختر الصور الرمزية من المكتبة المتاحة فقط.");
        if (CoachAssignmentTarget is < 1 or > 100) errors.Add("هدف إسناد المدرب بين مجموعة و100 مجموعة، أو اتركه دون هدف.");
        if (Targets is { Length: > 500 }) errors.Add("عدد أهداف التحصيل المحفوظة تجاوز الحد؛ راجع الأهداف القديمة.");
        if ((Targets ?? []).Any(t => t is null || t.From.Year < 2000 || t.To < t.From || t.BranchId == Guid.Empty
            || t.Amount <= 0 || t.Amount > 1_000_000_000 || decimal.Round(t.Amount, 2) != t.Amount))
            errors.Add("راجع فرع الهدف وفترته والمبلغ الموجب بدقتين عشريتين.");
        if ((Targets ?? []).Where(t => t is not null).GroupBy(t => new { t.BranchId, t.From, t.To }).Any(g => g.Count() > 1))
            errors.Add("يوجد هدف مكرر لنفس الفرع والفترة.");
        return errors.ToArray();
    }
    public static bool Same(OwnerHomeSettings? a, OwnerHomeSettings? b) => Canonical(a) == Canonical(b);
    private static string Canonical(OwnerHomeSettings? value) {
        var s = value ?? new(); return JsonSerializer.Serialize(s with { Targets = s.OrderedTargets() });
    }
}
