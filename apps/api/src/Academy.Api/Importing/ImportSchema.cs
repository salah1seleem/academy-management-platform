namespace Academy.Api.Importing;

// Template headers and preview/commit validation share this contract.
public static class ImportSchema
{
    public const string Version = "Academity.Import.2.1";
    public const int MaxRows = 1000;
    public sealed record Column(string Key, string Label, bool Required = false);
    public static readonly Column[] Enrollments = [
        new("LegacyPlayerId", "كود اللاعب في المصدر", true), new("LegacyEnrollmentId", "كود التسجيل في المصدر", true),
        new("HouseholdRef", "كود الأسرة الاختياري"), new("PlayerName", "اسم اللاعب", true), new("GuardianName", "اسم ولي الأمر", true),
        new("GuardianPhone", "هاتف ولي الأمر كنص", true), new("DateOfBirth", "الميلاد yyyy-MM-dd", true),
        new("BranchCode", "كود الفرع", true), new("SportCode", "كود الرياضة", true), new("LevelCode", "كود المستوى إن وجد"),
        new("GroupCode", "كود المجموعة", true), new("ProductCode", "كود الباقة", true),
        new("StartDate", "بداية الخدمة yyyy-MM-dd", true), new("EndDate", "نهاية الخدمة yyyy-MM-dd"),
        new("EntitledSessions", "إجمالي الحصص"), new("RemainingSessions", "الحصص المتبقية"),
        new("PricingMode", "agreed_net أو price_and_discount"), new("BasePrice", "السعر قبل الخصم"), new("Discount", "الخصم المتفق"),
        new("AgreedNet", "الصافي التاريخي المتفق"), new("Currency", "العملة", true), new("ExistingNew", "existing أو new", true), new("Notes", "ملاحظات")
    ];
    public static readonly Column[] OpenBalances = [new("LegacyEnrollmentId", "كود التسجيل", true), new("Amount", "رصيد موقع موجب مستحق وسالب لصالح الأسرة", true),
        new("Currency", "العملة", true), new("CutoffDate", "تاريخ الرصيد yyyy-MM-dd", true), new("Reference", "مرجع الرصيد", true), new("ApprovalEvidence", "سند الاعتماد", true)];
    public static readonly string[] Sheets = ["Instructions", "Enrollments", "References", "OpenBalances", "Metadata"];
}

public sealed record ImportCell(string Value, bool IsText);
public sealed record ImportSheet(string Name, IReadOnlyList<IReadOnlyList<ImportCell>> Rows);
public sealed record ImportWorkbook(IReadOnlyList<ImportSheet> Sheets)
{
    public ImportSheet Sheet(string name) => Sheets.Single(s => s.Name == name);
}
public sealed class ImportFileException(string message) : Exception(message);
