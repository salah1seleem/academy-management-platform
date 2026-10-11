using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Academy.Infrastructure.Subscriptions;

namespace Academy.Api.Importing;

public sealed record ImportIssue(string Sheet, int Row, string Column, string Message)
{
    public string SheetLabel => Sheet switch { "Enrollments" => "التسجيلات", "Metadata" => "إعداد الملف", "OpenBalances" => "الأرصدة الافتتاحية", _ => "القالب" };
    public string ColumnLabel => ImportSchema.Enrollments.Concat(ImportSchema.OpenBalances).FirstOrDefault(c => c.Key == Column)?.Label
        ?? (Column switch { "SourceSystem" => "كود المصدر", "CutoffDate" => "تاريخ القطع", "ReferenceStamp" => "نسخة المراجع", "AcademyCode" => "الأكاديمية", "SchemaVersion" => "نسخة القالب", _ => "بنية الملف" });
}
public sealed record ImportPreviewRow(int Row, string LegacyPlayerId, string LegacyEnrollmentId, string PlayerName, string GuardianName,
    string GuardianPhone, string? HouseholdRef, DateOnly BirthDate, Guid BranchId, Guid SportId, Guid GroupId, Guid ProductId,
    int PriceRevision, DateOnly Start, DateOnly? End, int? EntitledSessions, int? RemainingSessions, string? PricingMode,
    string? BasePrice, string? Discount, string? AgreedNet, string Currency, string ExistingNew, string? Notes);
public sealed record ImportPreviewResult(string SchemaVersion, string FileHash, string ReferenceStamp, string? SourceSystem, DateOnly? CutoffDate,
    int RowCount, int PlayerCount, int HouseholdCount, int FinanciallyIncomplete, bool CanApprove,
    IReadOnlyList<ImportIssue> Errors, IReadOnlyList<ImportPreviewRow> Rows)
{
    public ImportGuardianMatch[] GuardianMatches { get; init; } = [];
}

public static class ImportPreview
{
    public static ImportPreviewResult Validate(ImportWorkbook book, ImportReferences references, string fileHash, ISet<string>? existingEnrollmentIds = null)
    {
        var errors = new List<ImportIssue>(); var totalErrors = 0;
        void Error(string sheet, int row, string col, string message) { totalErrors++; if (errors.Count < 300) errors.Add(new(sheet, row, col, message)); }
        Dictionary<string, ImportCell>[] Rows(string name, ImportSchema.Column[] schema)
        {
            var raw = book.Sheet(name).Rows;
            if (raw.Count == 0 || !raw[0].Select(c => c.Value).SequenceEqual(schema.Select(c => c.Key))) { Error(name, 1, "", "عناوين الأعمدة لا تطابق نسخة القالب."); return []; }
            if (raw.Skip(1).Any(row => row.Skip(schema.Length).Any(c => c.Value.Length > 0))) Error(name, 1, "", "أعمدة إضافية غير مدعومة بعد آخر عنوان.");
            return raw.Skip(1).Select(row => schema.Select((c, i) => (c.Key, Cell: i < row.Count ? row[i] : new ImportCell("", true))).ToDictionary(x => x.Key, x => x.Cell)).ToArray();
        }
        var metadata = book.Sheet("Metadata").Rows.Skip(1).Where(r => r.Count >= 2).ToArray();
        string? Meta(string key) => metadata.FirstOrDefault(r => r[0].Value == key)?[1].Value;
        if (metadata.Select(r => r[0].Value).Distinct().Count() != metadata.Length) Error("Metadata", 1, "Key", "مفتاح إعداد مكرر.");
        if (Meta("SchemaVersion") != ImportSchema.Version) Error("Metadata", 1, "SchemaVersion", "نسخة القالب غير مدعومة. نزّل القالب الحالي.");
        if (Meta("AcademyCode") != references.AcademyId.ToString()) Error("Metadata", 1, "AcademyCode", "القالب ليس لهذه الأكاديمية.");
        if (Meta("ReferenceStamp") != references.Stamp) Error("Metadata", 1, "ReferenceStamp", "تغيّرت المراجع أو الأسعار أو السعة. نزّل قالبًا محدثًا وانقل البيانات إليه.");
        var source = Meta("SourceSystem");
        if (string.IsNullOrWhiteSpace(source) || source.Length > 80 || !Regex.IsMatch(source, "^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))) Error("Metadata", 1, "SourceSystem", "حدد كود المصدر بحروف لاتينية وأرقام حتى 80 حرفًا، مثل legacy-2026.");
        var cutoff = Date(Meta("CutoffDate")); if (cutoff is null) Error("Metadata", 1, "CutoffDate", "أدخل تاريخ القطع بصيغة yyyy-MM-dd.");
        var rawRows = Rows("Enrollments", ImportSchema.Enrollments); var result = new List<ImportPreviewRow>(); var enrollmentIds = new HashSet<string>(StringComparer.Ordinal);
        var playerIdentity = new Dictionary<string, string>(StringComparer.Ordinal); var householdIdentity = new Dictionary<string, string>(StringComparer.Ordinal); var playerSport = new HashSet<string>(StringComparer.Ordinal);
        var rowCount = 0; var incomplete = 0;
        for (var index = 0; index < rawRows.Length; index++)
        {
            var row = rawRows[index]; if (row.Values.All(c => c.Value.Length == 0)) continue; rowCount++; var number = index + 2;
            var before = totalErrors; void Bad(string col, string msg) => Error("Enrollments", number, col, msg);
            string V(string col) => row[col].Value;
            foreach (var c in ImportSchema.Enrollments.Where(c => c.Required)) if (V(c.Key).Length == 0) Bad(c.Key, "حقل مطلوب: " + c.Label);
            foreach (var c in ImportSchema.Enrollments) if (V(c.Key).Length > (c.Key == "Notes" ? 500 : 160)) Bad(c.Key, "القيمة أطول من الحد المسموح.");
            foreach (var key in new[] { "LegacyPlayerId", "LegacyEnrollmentId", "HouseholdRef" }) if (V(key).Length > 0 && !Regex.IsMatch(V(key), "^[A-Za-z0-9_.:-]{1,80}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))) Bad(key, "استخدم كودًا ثابتًا بحروف لاتينية وأرقام دون مسافات.");
            if (!row["GuardianPhone"].IsText || !Regex.IsMatch(V("GuardianPhone"), "^\\+?[0-9]{10,15}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))) Bad("GuardianPhone", "الهاتف يجب أن يكون نصًا من 10 إلى 15 رقمًا؛ لا تحول الهاتف إلى رقم Excel.");
            else if (Academy.Api.Auth.EgyptPhoneNormalizer.Normalize(V("GuardianPhone")) is null) Bad("GuardianPhone", "أدخل هاتفًا مصريًا صالحًا للتفعيل الحالي، مثل 010xxxxxxxx أو +2010xxxxxxxx.");
            var birth = Date(V("DateOfBirth")); var start = Date(V("StartDate")); var end = Date(V("EndDate"));
            if (birth is null || birth > cutoff) Bad("DateOfBirth", "تاريخ الميلاد غير صحيح.");
            if (start is null) Bad("StartDate", "بداية الخدمة مطلوبة بصيغة yyyy-MM-dd.");
            if (V("EndDate").Length > 0 && (end is null || end < start)) Bad("EndDate", "نهاية الخدمة غير صحيحة أو أسبق من البداية.");
            var branch = references.Find("branch", V("BranchCode")); var sport = references.Find("sport", V("SportCode")); var group = references.Find("group", V("GroupCode")); var product = references.Find("product", V("ProductCode"));
            if (branch is null) Bad("BranchCode", "فرع غير متاح ضمن صلاحياتك."); if (sport is null) Bad("SportCode", "رياضة غير متاحة.");
            if (group is null || group.Branch != branch?.Id || group.Sport != sport?.Id) Bad("GroupCode", "المجموعة لا تطابق الفرع والرياضة أو غير متاحة.");
            if (birth is not null && group is not null && (group.MinYear > birth.Value.Year || group.MaxYear < birth.Value.Year)) Bad("DateOfBirth", "الميلاد لا يناسب الفئة العمرية للمجموعة.");
            if (product is null || product.Sport != sport?.Id || !product.AllBranches && product.Branches?.Contains(branch?.Id ?? Guid.Empty) != true) Bad("ProductCode", "الباقة غير منشورة لهذا الفرع والرياضة.");
            if (V("LevelCode").Length > 0) Bad("LevelCode", "لم تُجهز مستويات لهذه النسخة بعد. لا يُستبدل المستوى بالفئة العمرية.");
            int? Sessions(string col) { if (V(col).Length == 0) return null; if (int.TryParse(V(col), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n <= 10000) return n; Bad(col, "عدد الحصص يجب أن يكون صحيحًا غير سالب حتى 10000."); return null; }
            var entitled = Sessions("EntitledSessions"); var remaining = Sessions("RemainingSessions");
            if (entitled.HasValue != remaining.HasValue) Bad("RemainingSessions", "أدخل إجمالي الحصص والمتبقي معًا أو اتركهما فارغين.");
            if (remaining > entitled) Bad("RemainingSessions", "المتبقي أكبر من الاستحقاق.");
            if (product?.PlanType is SubscriptionPlanType.Duration or SubscriptionPlanType.Combined && end is null) Bad("EndDate", "هذه الباقة تتطلب نهاية خدمة.");
            if (product?.PlanType is SubscriptionPlanType.Sessions or SubscriptionPlanType.Combined && (entitled is null || remaining is null)) Bad("RemainingSessions", "هذه الباقة تتطلب الإجمالي والمتبقي.");
            decimal? Money(string col) { if (V(col).Length == 0) return null; var value = Amount(V(col)); if (value is null || value < 0) Bad(col, "مبلغ غير سالب بحد أقصى منزلتين عشريتين."); return value; }
            var basis = Money("BasePrice"); var discount = Money("Discount"); var net = Money("AgreedNet"); var mode = V("PricingMode");
            if (mode.Length == 0 && basis is null && discount is null && net is null) incomplete++;
            else if (mode == "agreed_net") { if (net is null) Bad("AgreedNet", "الصافي المتفق مطلوب."); if (basis is not null || discount is not null) Bad("PricingMode", "agreed_net يحتفظ بالصافي فقط. استخدم price_and_discount عند توفر تفصيل السعر والخصم."); }
            else if (mode == "price_and_discount") { if (basis is null || discount is null || net is null || basis - discount != net) Bad("AgreedNet", "السعر ناقص الخصم يجب أن يساوي الصافي المتفق."); }
            else Bad("PricingMode", "اختر agreed_net أو price_and_discount، أو اترك بيانات المال كلها فارغة للمراجعة.");
            if (V("Currency") != references.Currency) Bad("Currency", "العملة لا تطابق عملة الأكاديمية.");
            if (V("ExistingNew") is not ("existing" or "new")) Bad("ExistingNew", "اكتب existing أو new.");
            if (!enrollmentIds.Add(V("LegacyEnrollmentId"))) Bad("LegacyEnrollmentId", "كود التسجيل مكرر في الملف.");
            if (!playerSport.Add(V("LegacyPlayerId") + ":" + V("SportCode"))) Bad("LegacyPlayerId", "تسجيل الرياضة نفسه مكرر للطفل. لا نكرر اللاعب أو الاشتراك.");
            var identity = JsonSerializer.Serialize(new { name = V("PlayerName"), birth, phone = Academy.Api.Auth.EgyptPhoneNormalizer.Normalize(V("GuardianPhone")), guardian = V("GuardianName"), family = V("HouseholdRef") });
            if (playerIdentity.TryGetValue(V("LegacyPlayerId"), out var previous) && previous != identity) Bad("LegacyPlayerId", "بيانات الطفل المتكرر عبر الرياضات متعارضة."); else playerIdentity[V("LegacyPlayerId")] = identity;
            var family = V("HouseholdRef"); if (family.Length > 0) { var identityKey = Academy.Api.Auth.EgyptPhoneNormalizer.Normalize(V("GuardianPhone")) + ":" + V("GuardianName"); if (householdIdentity.TryGetValue(family, out var known) && known != identityKey) Bad("HouseholdRef", "كود الأسرة يرتبط ببيانات أولياء أمور متعارضة وتحتاج مراجعة."); else householdIdentity[family] = identityKey; }
            if (totalErrors == before) result.Add(new(number, V("LegacyPlayerId"), V("LegacyEnrollmentId"), V("PlayerName"), V("GuardianName"), V("GuardianPhone"), family.Length == 0 ? null : family,
                birth!.Value, branch!.Id, sport!.Id, group!.Id, product!.Id, product.Revision, start!.Value, end, entitled, remaining, mode.Length == 0 ? null : mode,
                basis?.ToString("0.00", CultureInfo.InvariantCulture), discount?.ToString("0.00", CultureInfo.InvariantCulture), net?.ToString("0.00", CultureInfo.InvariantCulture), V("Currency"), V("ExistingNew"), V("Notes")));
        }
        if (rowCount is 0 or > ImportSchema.MaxRows) Error("Enrollments", 1, "", "الملف يتطلب من 1 إلى 1000 تسجيل.");
        foreach (var g in result.GroupBy(r => r.GroupId)) { var reference = references.Values.Single(v => v.Id == g.Key && v.Kind == "group"); if (reference.Capacity is { } capacity && reference.Occupied + g.Count(r => existingEnrollmentIds?.Contains(r.LegacyEnrollmentId) != true) > capacity) Error("Enrollments", g.First().Row, "GroupCode", "المجموعة تتجاوز سعتها الحالية. عالج السعة أو التوزيع قبل الاعتماد."); }
        foreach (var phones in result.GroupBy(r => Academy.Api.Auth.EgyptPhoneNormalizer.Normalize(r.GuardianPhone))) if (phones.Select(p => p.LegacyPlayerId).Distinct().Count() > 1 && (phones.Any(p => p.HouseholdRef is null) || phones.Select(p => p.HouseholdRef).Distinct().Count() != 1)) Error("Enrollments", phones.First().Row, "HouseholdRef", "الهاتف المشترك يحتاج كود أسرة موحدًا واعتماد الربط؛ لا دمج آلي بالهاتف.");
        var balances = Rows("OpenBalances", ImportSchema.OpenBalances);
        var balanceKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < balances.Length; i++)
        {
            var r = balances[i]; if (r.Values.All(c => c.Value.Length == 0)) continue;
            foreach (var c in ImportSchema.OpenBalances) if (r[c.Key].Value.Length == 0) Error("OpenBalances", i + 2, c.Key, "حقل الرصيد مطلوب: " + c.Label);
            if (!enrollmentIds.Contains(r["LegacyEnrollmentId"].Value)) Error("OpenBalances", i + 2, "LegacyEnrollmentId", "الرصيد لا يرتبط بتسجيل في الملف.");
            if (!balanceKeys.Add(r["LegacyEnrollmentId"].Value + ":" + r["Reference"].Value)) Error("OpenBalances", i + 2, "Reference", "مرجع الرصيد مكرر لهذا التسجيل.");
            if (Amount(r["Amount"].Value) is not { } amount || amount == 0) Error("OpenBalances", i + 2, "Amount", "أدخل رصيدًا موجبًا أو سالبًا غير صفر دون تقريب.");
            if (r["Currency"].Value != references.Currency || Date(r["CutoffDate"].Value) != cutoff) Error("OpenBalances", i + 2, "CutoffDate", "عملة أو تاريخ الرصيد لا يطابق إعداد الملف.");
            if (r["Reference"].Value.Length > 120 || r["ApprovalEvidence"].Value.Length > 500) Error("OpenBalances", i + 2, "Reference", "مرجع أو سند الرصيد أطول من المسموح.");
        }
        return new(ImportSchema.Version, fileHash, references.Stamp, source, cutoff, rowCount, playerIdentity.Count, householdIdentity.Count, incomplete, errors.Count == 0, errors, result);
    }
    public static DateOnly? Date(string? value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.Year is >= 1900 and <= 9998 ? d : null;
    public static decimal? Amount(string value) => Regex.IsMatch(value, "^-?[0-9]{1,9}(\\.[0-9]{1,2})?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)) && decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount) ? amount : null;
}
