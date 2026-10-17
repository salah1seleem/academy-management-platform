using System.Text.Json;

namespace Academy.Api.Help;

internal static class HelpAudiencePolicy
{
    // Missing entry preserves product eligibility; [] explicitly hides a lesson.
    // This preference can only narrow roles, never grant a product permission.
    internal static bool Allows(Dictionary<string, string[]>? audience, string lesson, string role) =>
        audience is null || !audience.TryGetValue(lesson, out var roles) || roles is not null && roles.Contains(role, StringComparer.Ordinal);
    internal static string[] Validate(Dictionary<string, string[]>? audience)
    {
        if (audience is null) return [];
        if (audience.Count > HelpCatalog.All.Length) return ["قائمة جمهور الأدلة أكبر من المسموح."];
        foreach (var (id, roles) in audience)
        {
            var guide = HelpCatalog.All.SingleOrDefault(g => g.Id == id);
            if (guide is null || roles is null || roles.Length > 4 || roles.Distinct(StringComparer.Ordinal).Count() != roles.Length || roles.Any(r => !guide.Roles.Any(allowed => allowed.ToString() == r)))
                return ["اختر دليلًا ودورًا مؤهلين من القائمة؛ جمهور الدليل لا يمنح صلاحيات إضافية."];
        }
        return [];
    }
    internal static bool Same(Dictionary<string, string[]>? a, Dictionary<string, string[]>? b)
    {
        static string Canonical(Dictionary<string, string[]>? value) => JsonSerializer.Serialize((value ?? []).OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value.OrderBy(r => r, StringComparer.Ordinal).ToArray()));
        return Canonical(a) == Canonical(b);
    }
}
