using Academy.Api.Auth;

namespace Academy.Api.Operations;

// Templates fill the existing grant matrix; a key never grants a role or permission.
// New templates deliberately exclude deletion, user administration and settings.
public sealed record StaffAccessTemplate(string Key, string Name, string Description, StaffActionValue[] Actions, int? Revision = null, bool Custom = false);
public static class StaffAccessTemplates
{
    public static readonly StaffAccessTemplate[] All = [
        Create("branch-operator", "إداري فرع", "اللاعبون والمجموعات والحضور وطلبات الدفع في الفروع التي تحددها؛ تعديل الباقات والخصومات تفويض منفصل.",
            ["academy", "players", "subscriptions", "payments", "attendance", "evaluations", "matches"],
            ["academy", "players", "payments", "attendance"]),
        Create("academy-manager", "مدير تشغيل الأكاديمية", "التشغيل والمحتوى في النطاق المحدد؛ إدارة الحسابات والماليات الحساسة تحتاج تفويضًا منفصلًا.",
            ["academy", "players", "subscriptions", "payments", "attendance", "evaluations", "matches", "facilities", "news", "catalog", "nutrition"],
            ["academy", "players", "payments", "attendance", "matches", "facilities", "news"]),
        Create("sports-lead", "مدير رياضي", "المجموعات والحصص والتقييمات والمنافسات؛ لا رواتب أو تحصيل أو سجلات طبية.",
            ["academy", "players", "attendance", "evaluations", "matches", "facilities"],
            ["academy", "evaluations", "matches", "facilities"]),
        Create("finance-reader", "مراجع مالي — قراءة", "قراءة التحصيل والاشتراكات والتقارير المالية؛ الصرف والاسترداد والاعتماد لا تُمنح تلقائيًا.",
            ["finance", "payments", "subscriptions", "reports"], []),
        Create("read-only", "متابعة التشغيل — قراءة", "عرض اللاعبين والمجموعات والحضور والتقييمات دون تعديل.",
            ["academy", "players", "attendance", "evaluations", "matches"], [])
    ];
    private static StaffAccessTemplate Create(string key, string name, string description, string[] read, string[] write) =>
        new(key, name, description, StaffAccessRules.Modules.Select(module =>
            new StaffActionValue(module, read.Contains(module), write.Contains(module), write.Contains(module), false)).ToArray());
}
