using System.Text.Json;
using Academy.Infrastructure.Structure;

namespace Academy.Infrastructure.Tenancy;

public enum ConfigurationState { Draft, Validated, Staged, Active, Suspended, Superseded }

public sealed class AcademyConfigurationVersion : TenantEntity
{
    public int Revision { get; set; }
    public ConfigurationState State { get; set; }
    public required string Payload { get; set; }
    public required string ContentHash { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? EffectiveAtUtc { get; set; }
}

public sealed class AcademyFeatureEntitlement : TenantEntity
{
    public required string Feature { get; set; }
    public bool Enabled { get; set; }
    public required string PlanCode { get; set; }
}

public sealed class ConfigurationTransition : TenantEntity
{
    public Guid ConfigurationId { get; set; }
    public Guid? ActorUserId { get; set; }
    public ConfigurationState From { get; set; }
    public ConfigurationState To { get; set; }
}

// Product licenses, user permissions and operational preferences are independent.
// Unknown feature identifiers never inherit permission from another feature.
public static class AcademyFeatures
{
    public static readonly string[] Existing = ["training", "nutrition", "medical", "media", "catalog", "news", "matches", "payroll"];
    public static readonly string[] All = [.. Existing, "facilities", "expenses", "external_partners", "learning_hub", "calendar", "crm", "simulator", "community_ads", "voice_notes", "ai_assistant"];
}

public sealed record AcademyRuntimeSettings(
    string Currency, string TimeZone, Dictionary<string, bool> Features,
    bool CashEnabled = false, int ConsecutiveAbsenceAlert = 3, bool TenantMarketingOptIn = false,
    Dictionary<string, string[]>? HelpAudiences = null)
{
    public static AcademyRuntimeSettings Initial(string currency, string zone, bool preserveExisting = false) =>
        new(currency, zone, AcademyFeatures.All.ToDictionary(f => f, f => preserveExisting && AcademyFeatures.Existing.Contains(f)), preserveExisting);

    public string[] Validate(IReadOnlyDictionary<string, bool> entitlements, string existingCurrency, string existingTimeZone)
    {
        var errors = new List<string>();
        if (Currency != existingCurrency) errors.Add("عملة الأكاديمية لا تُغيّر من إعدادات التشغيل بعد إنشاء السجلات المالية.");
        if (string.IsNullOrWhiteSpace(TimeZone) || !TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out _)) errors.Add("المنطقة الزمنية غير صالحة.");
        else if (TimeZone != existingTimeZone) errors.Add("تغيير المنطقة الزمنية لأكاديمية قائمة يحتاج ترحيلًا مستقلًا للجداول؛ لا يُنفذ من هذه الشاشة.");
        if (ConsecutiveAbsenceAlert is < 1 or > 30) errors.Add("تنبيه الغياب المتتالي يجب أن يكون بين 1 و30 حصة.");
        if (Features is null || Features.Count != AcademyFeatures.All.Length || AcademyFeatures.All.Any(f => !Features.ContainsKey(f))) errors.Add("قائمة الميزات لا تطابق إصدار الإعدادات.");
        else foreach (var f in Features.Where(f => f.Value))
            if (!entitlements.TryGetValue(f.Key, out var licensed) || !licensed) errors.Add("تتضمن المسودة ميزة غير متاحة في خطة الأكاديمية.");
        if (TenantMarketingOptIn && (Features is null || !Features.GetValueOrDefault("community_ads"))) errors.Add("موافقة الإعلانات تتطلب إتاحة ميزة الإعلانات أولًا.");
        return errors.ToArray();
    }
    public string Serialize() => JsonSerializer.Serialize(this with
    {
        Features = Features.OrderBy(f => f.Key, StringComparer.Ordinal).ToDictionary(f => f.Key, f => f.Value),
        HelpAudiences = HelpAudiences?.OrderBy(a => a.Key, StringComparer.Ordinal).ToDictionary(a => a.Key, a => a.Value is null ? null! : a.Value.OrderBy(r => r, StringComparer.Ordinal).ToArray())
    });
    public static AcademyRuntimeSettings Read(string payload) => JsonSerializer.Deserialize<AcademyRuntimeSettings>(payload) ?? throw new InvalidOperationException("Invalid stored configuration");
}
