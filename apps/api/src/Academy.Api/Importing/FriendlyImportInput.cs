using System.Globalization;
using System.Text.RegularExpressions;
using Academy.Api.Auth;

namespace Academy.Api.Importing;

public sealed class FriendlyImportInput
{
    public sealed record Row(string Sheet, int Number, Dictionary<string, ImportCell> Cells)
    {
        public string this[string key] => Cells.GetValueOrDefault(key)?.Value ?? "";
    }
    public Dictionary<string,(string Team,string Tournaments,string Photo)> Extras { get; private set; } = [];
    public Dictionary<int,int> PrimaryRows { get; private set; } = [];
    public IReadOnlyList<ImportIssue> PlayerCellErrors { get; private set; } = [];
    public required ImportWorkbook Original { get; init; }
    public required ImportWorkbook Connected { get; set; }
    private readonly List<Row> memberRows = [];
    public List<ImportIssue> Errors { get; } = [];
    public Dictionary<string, List<Row>> Rows { get; } = [];
    public Dictionary<string, Row> Players => ByCode("اللاعبون");
    public Dictionary<string, Row> Guardians => ByCode("أولياء الأمور");
    public Dictionary<string, Row> Enrollments => ByCode("التسجيلات");
    public Dictionary<string, Row> ByCode(string sheet) => Rows[sheet].DistinctBy(r => r["Code"]).ToDictionary(r => r["Code"], StringComparer.Ordinal);
    public void Error(Row row, string key, string message)
    {
        if (Errors.Count < 300) Errors.Add(new(row.Sheet, row.Number, FriendlyImportSchema.Sheets[row.Sheet].FirstOrDefault(f => f.Key == key)?.Header ?? key, message));
    }
    public bool Need(Row row, bool valid, string key, string message) { if (!valid) Error(row, key, message); return valid; }
    public DateOnly? Date(Row row, string key, bool required = true)
    {
        if (!required && row[key] == "") return null;
        var value = ImportPreview.Date(row[key]); Need(row, value.HasValue && value.Value.Year >= 2000, key, "أدخل تاريخًا صحيحًا yyyy-MM-dd من سنة 2000 فأحدث."); return value;
    }
    public int? Count(Row row, string key, int max = 10000, bool required = true)
    {
        if (!required && row[key] == "") return null;
        var valid = int.TryParse(row[key], NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0 && value <= max;
        Need(row, valid, key, $"أدخل عددًا صحيحًا بين 0 و{max}."); return valid ? value : null;
    }
    public decimal? Amount(Row row, string key)
    {
        var value = ImportPreview.Amount(row[key]); Need(row, value is >= 0, key, "أدخل مبلغًا غير سالب بمنزلتين عشريتين على الأكثر."); return value;
    }
    public ImportIssue MapIssue(ImportIssue issue)
    {
        if (issue.Sheet == "الحصص") return issue with { Sheet = "المواعيد الأسبوعية" };
        if (issue.Sheet == "التسجيلات" && PrimaryRows.TryGetValue(issue.Row, out var primaryRow))
            return MapPlayerIssue(issue, primaryRow, FriendlyImportSchema.Sheets["التسجيلات"].FirstOrDefault(f => f.Header == issue.Column)?.Key ?? issue.Column);
        if (issue.Sheet == "اللاعبون" && PrimaryRows.ContainsValue(issue.Row) &&
            FriendlyImportSchema.Sheets["اللاعبون"].FirstOrDefault(f => f.Header == issue.Column) is { } playerField)
            return MapPlayerIssue(issue, issue.Row, playerField.Key);
        if (issue.Sheet is not ("Enrollments" or "الأعضاء")) return issue;
        var index = issue.Row - 2;
        if (index < 0 || index >= memberRows.Count) return issue with { Sheet = "التسجيلات" };
        var enrollment = memberRows[index];
        var legacy = Array.FindIndex(ImportSchema.Enrollments, f => f.Key == issue.Column);
        var field = legacy >= 0 ? ConnectedImportSchema.Sheets["الأعضاء"][legacy].Key : ConnectedImportSchema.Sheets["الأعضاء"].FirstOrDefault(f => f.Header == issue.Column)?.Key ?? issue.Column;
        Row row = enrollment; var sheet = "التسجيلات";
        if (Players.TryGetValue(enrollment["Player"], out var player)) {
            if (field is "Name" or "Birth" or "Gender" or "Player") { row = player; sheet = "اللاعبون"; if (field == "Player") field = "Code"; }
            if ((field is "Guardian" or "Phone" or "Family") && Guardians.TryGetValue(player["Guardian"], out var guardian)) { row = guardian; sheet = "أولياء الأمور"; field = field == "Guardian" ? "Name" : field == "Family" ? "Code" : "Phone"; }
        }
        if (field == "Enrollment") field = "Code";
        if (sheet != "أولياء الأمور" && PrimaryRows.TryGetValue(enrollment.Number, out var primaryNumber))
            return MapPlayerIssue(issue, primaryNumber, field);
        return issue with { Sheet = sheet, Row = row.Number, Column = FriendlyImportSchema.Sheets[sheet].FirstOrDefault(f => f.Key == field)?.Header ?? issue.Column };
    }

    private static ImportIssue MapPlayerIssue(ImportIssue issue, int row, string field)
    {
        var column = field switch
        {
            "Enrollment" or "Player" or "Period" => "Code",
            "Registered" => "Start",
            _ => field
        };
        return issue with
        {
            Sheet = "اللاعبون",
            Row = row,
            Column = column == "Name" ? "الاسم الأول وباقي الاسم" :
                AcademyWorkbookSchema.Players.FirstOrDefault(f => f.Key == column)?.Header ?? issue.Column
        };
    }
    public static FriendlyImportInput Parse(ImportWorkbook original)
    {
        var current = AcademyWorkbookSchema.IsCurrent(original) ? AcademyWorkbookSchema.Normalize(original) : null;
        if(current is not null) original = current.Book;
        var result = new FriendlyImportInput { Original = original, Connected = original, Extras = current?.Extras ?? [], PrimaryRows = current?.PrimaryRows ?? [], PlayerCellErrors = current?.Errors ?? [] };
        result.Errors.AddRange(result.PlayerCellErrors);
        foreach (var (sheet, fields) in FriendlyImportSchema.Sheets)
        {
            result.Rows[sheet] = [];
            var raw = original.Sheet(sheet).Rows;
            if (raw.Count > FriendlyImportSchema.RowLimit(sheet) + 1) { result.Errors.Add(new(sheet, 1, "", "تجاوزت الورقة حد الصفوف المعلن في ابدأ هنا.")); continue; }
            if (raw.Count == 0 || !raw[0].Select(c => c.Value).SequenceEqual(fields.Select(f => f.Header))) { result.Errors.Add(new(sheet, 1, "", "عناوين الأعمدة لا تطابق القالب. نزّل النسخة الحالية وانقل الصفوف دون تغيير العناوين.")); continue; }
            var codes = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 1; index < raw.Count; index++)
            {
                var values = raw[index]; if (values.All(c => c.Value.Length == 0)) continue;
                var row = new Row(sheet, index + 1, fields.Select((f, i) => (f.Key, Cell: i < values.Count ? values[i] : new ImportCell("", true))).ToDictionary(x => x.Key, x => x.Cell));
                result.Rows[sheet].Add(row);
                foreach (var f in fields) { if (f.Required) result.Need(row, row[f.Key].Length > 0, f.Key, "هذا الحقل مطلوب. املأه أو احذف الصف الفارغ بالكامل."); result.Need(row, row[f.Key].Length <= (f.Key == "Notes" ? 500 : 160), f.Key, "القيمة أطول من الحد المسموح."); }
                result.Need(row, values.Skip(fields.Length).All(c => c.Value == ""), "", "احذف الأعمدة الإضافية غير الموجودة في القالب.");
                if (fields.Any(f => f.Key == "Code"))
                {
                    result.Need(row, row.Cells["Code"].IsText && Regex.IsMatch(row["Code"], "^[A-Za-z0-9_.:-]{1,80}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)), "Code", "أدخل كودًا نصيًا ثابتًا مثل PL0001، دون مسافات.");
                    result.Need(row, codes.Add(row["Code"]), "Code", "الكود مكرر. للشخص صف واحد، وللعلاقة أو التسجيل الإضافي كود آخر.");
                }
            }
        }
        var players = result.Players; var guardians = result.Guardians;
        foreach (var r in guardians.Values)
        {
            var phone = EgyptPhoneNormalizer.Normalize(r["Phone"]);
            result.Need(r, r.Cells["Phone"].IsText && phone != null, "Phone", "اكتب هاتفًا مصريًا صالحًا كنص، مثل 010xxxxxxxx أو +2010xxxxxxxx.");
            if (phone != null) r.Cells["Phone"] = new(phone, true);
            result.Need(r, r["Associate"] is "" or "لا" or "نعم", "Associate", "اختر نعم فقط بعد مراجعة نفس الشخص صاحب الحساب القائم.");
        }
        foreach (var group in guardians.Values.GroupBy(r => r["Phone"]).Where(g => g.Count() > 1)) foreach (var r in group) result.Error(r, "Phone", "الهاتف مكرر بأكثر من كود ولي أمر. استخدم نفس الكود للأخوة، ولا تدمج شخصين مختلفين بالهاتف.");
        var relationships = new HashSet<(string, string)>();
        foreach (var r in result.Rows["علاقات أولياء الأمور"])
        {
            result.Need(r, players.ContainsKey(r["Player"]), "Player", "كود اللاعب غير موجود في ورقة اللاعبون.");
            result.Need(r, guardians.ContainsKey(r["Guardian"]), "Guardian", "كود ولي الأمر غير موجود في ورقة أولياء الأمور.");
            result.Need(r, relationships.Add((r["Player"], r["Guardian"])), "Guardian", "علاقة اللاعب بولي الأمر مكررة.");
        }
        foreach (var r in players.Values)
        {
            result.Need(r, guardians.ContainsKey(r["Guardian"]), "Guardian", "أدخل كود ولي الأمر الأساسي الموجود في ورقته.");
            result.Need(r, relationships.Contains((r["Code"], r["Guardian"])), "Guardian", "أضف علاقة أساسية صريحة في ورقة علاقات أولياء الأمور.");
            var birth = result.Date(r, "Birth"); var registered = result.Date(r, "Registered");
            result.Need(r, birth <= registered, "Registered", "تاريخ التسجيل يجب ألا يسبق الميلاد.");
            result.Need(r, r["Gender"] is "" or "ذكر" or "أنثى", "Gender", "اختر ذكر أو أنثى، أو اتركه فارغًا عند عدم المعرفة.");
        }
        var memberRows = new List<IReadOnlyList<ImportCell>> { ConnectedImportSchema.Row(ConnectedImportSchema.Sheets["الأعضاء"].Select(f => f.Header).ToArray()) };
        var periods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in result.Rows["التسجيلات"])
        {
            if (!result.Need(r, players.ContainsKey(r["Player"]), "Player", "كود اللاعب غير موجود.")) continue;
            var p = players[r["Player"]]; if (!guardians.TryGetValue(p["Guardian"], out var g)) continue;
            result.Need(r, periods.Add(r["Period"]), "Period", "كود الفترة الحالية مكرر.");
            var start = result.Date(r, "Start"); result.Need(r, start >= result.Date(p, "Registered"), "Start", "بداية الاشتراك تسبق تاريخ تسجيل اللاعب.");
            result.memberRows.Add(r);
            memberRows.Add(ConnectedImportSchema.Row(p["Code"], r["Code"], g["Code"], p["Name"], g["Name"], g["Phone"], p["Birth"], r["Branch"], r["Sport"], r["Level"], r["Group"], r["Plan"], r["Start"], r["End"], r["Entitled"], r["Remaining"], "صافي متفق", "", "", r["Net"], r["Currency"], "قائم", "", p["Gender"], "", result.Extras.GetValueOrDefault(r["Code"]).Tournaments ?? "", result.Extras.GetValueOrDefault(r["Code"]).Team ?? ""));
        }
        foreach (var p in players.Values) result.Need(p, result.Rows["التسجيلات"].Any(e => e["Player"] == p["Code"]), "Code", "اللاعب بلا تسجيل رياضي. أضف صف تسجيل متصلًا قبل الاستيراد.");
        var sheets = new List<ImportSheet>();
        foreach (var name in ConnectedImportSchema.Names)
        {
            if (name == "دليل الاستخدام") { sheets.Add(original.Sheet("ابدأ هنا") with { Name = name }); continue; }
            if (name == "الأعضاء") { sheets.Add(new(name, memberRows)); continue; }
            var sheet = original.Sheet(name == "الحصص" ? "المواعيد الأسبوعية" : name) with { Name = name };
            if (name == "إعدادات الملف") sheet = sheet with { Rows = sheet.Rows.Select(r => r.Count == 2 && r[0].Value == "نسخة القالب" ? ConnectedImportSchema.Row("نسخة القالب", ConnectedImportSchema.Version) : r).ToArray() };
            sheets.Add(sheet);
        }
        result.Connected = new(sheets); return result;
    }
}
