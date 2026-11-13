using System.Globalization;
using System.Text.RegularExpressions;
using Academy.Api.Auth;

namespace Academy.Api.Importing;

// V3 is a customer-facing layout. Normalization feeds the existing atomic, tenant-scoped importer.
public static class AcademyWorkbookSchema
{
    public const string Version = "iAcademy.Connected.3";
    private static readonly HashSet<string> Removed = ["أنماط التدريب", "مستويات التدريب", "العروض", "الأرصدة الافتتاحية", "علاقات أولياء الأمور"];
    public static readonly ConnectedImportSchema.Field[] Players = [
        new("Code","كود اللاعب",true), new("First","الاسم الأول",true), new("Last","باقي الاسم",true),
        new("Phone","هاتف ولي الأمر كنص",true), new("Birth","الميلاد yyyy-MM-dd",true), new("Gender","ذكر أو أنثى"),
        new("Photo","الصورة الرمزية اختياري"), new("Branch","كود الفرع",true), new("Sport","كود الرياضة",true),
        new("Group","كود المجموعة",true), new("Plan","كود الباقة",true), new("Start","بداية الاشتراك yyyy-MM-dd",true),
        new("End","نهاية الاشتراك yyyy-MM-dd"), new("Entitled","إجمالي الحصص تلقائي"), new("Remaining","الحصص المتبقية تلقائي"),
        new("Net","الصافي المتفق"),new("Currency","العملة",true),new("Team","كود الفريق"),new("Tournaments","أكواد البطولات")];
    public static readonly Dictionary<string, ConnectedImportSchema.Field[]> Sheets = BuildFields();
    private static Dictionary<string, ConnectedImportSchema.Field[]> BuildFields()
    {
        var fields = new Dictionary<string, ConnectedImportSchema.Field[]> { ["اللاعبون"] = Players };
        foreach (var (name, columns) in FriendlyImportSchema.Sheets.Where(s => s.Key != "اللاعبون" && !Removed.Contains(s.Key)))
            fields[name] = columns.Where(c => c.Key != "Level").ToArray();
        return fields;
    }
    public static string[] Names => new[] { "ابدأ هنا" }.Concat(Sheets.Keys).ToArray();
    public static bool IsCurrent(ImportWorkbook book) => book.Sheets.Any(s=>s.Name=="إعدادات الملف") && ConnectedImportSchema.Meta(book,"نسخة القالب") == Version;
    public static (ImportWorkbook Clean, ImportWorkbook Correction, int RelatedRows)? SplitInvalidPlayers(
        ImportWorkbook book, Guid academy, FriendlyImportInput input, IReadOnlyList<ImportIssue> errors)
    {
        if (errors.Count >= 300 || errors.Any(error => error.IsShared) || !IsCurrent(book) ||
            !Guid.TryParse(ConnectedImportSchema.Meta(book,"الأكاديمية"), out var owner) || owner != academy) return null;

        var rows = book.Sheet("اللاعبون").Rows;
        var excluded = errors.Where(error => error.Sheet == "اللاعبون" && error.Row >= 2)
            .Select(error => error.Row).ToHashSet();
        if (excluded.Count == 0) return null;
        if (excluded.Any(row => row > rows.Count || rows[row - 1].All(c => c.Value == ""))) return null;
        var valid = rows.Where((_, index) => !excluded.Contains(index + 1)).ToArray();
        if (!valid.Skip(1).Any(row => row.Count > 3 && EgyptPhoneNormalizer.Normalize(row[3].Value) is not null)) return null;

        var correctionRows = excluded.Order().ToArray();
        var excludedCodes = Codes(correctionRows.Select(row => rows[row - 1][0].Value));
        // Normalization appends a generated primary registration for every usable
        // player. Ownership of uploaded additional registrations uses original rows.
        var registrations = input.Rows["التسجيلات"].Where(row => row.Number <= book.Sheet("التسجيلات").Rows.Count).ToArray();
        var linkedRows = new Dictionary<string, HashSet<int>>();
        FriendlyImportInput.Row[] Take(string sheet, string ownerField, HashSet<string> owners)
        {
            IReadOnlyList<FriendlyImportInput.Row> candidates = sheet == "التسجيلات" ? registrations : input.Rows[sheet];
            var matched = candidates.Where(row => owners.Contains(row[ownerField])).ToArray();
            linkedRows[sheet] = matched.Select(row => row.Number).ToHashSet();
            return matched;
        }
        var additional = Take("التسجيلات", "Player", excludedCodes);
        var enrollmentCodes = Codes(excludedCodes.Select(code => "EN-" + code).Concat(additional.Select(row => row["Code"])));
        var periods = Take("فترات سابقة اختيارية", "Enrollment", enrollmentCodes);
        var periodCodes = Codes(excludedCodes.Select(code => "PER-" + code)
            .Concat(additional.Select(row => row["Period"])).Concat(periods.Select(row => row["Code"])));
        var attendance = Take("حضور اختياري", "Enrollment", enrollmentCodes);
        var assessments = Take("تقييمات اختيارية", "Enrollment", enrollmentCodes);
        Take("درجات اختيارية", "Evaluation", Codes(assessments.Select(row => row["Code"])));
        Take("تحصيلات سابقة اختيارية", "Period", periodCodes);
        var relatedCount = linkedRows.Values.Sum(numbers => numbers.Count);
        if (relatedCount > 0)
        {
            // Splitting must never hide a collision that was visible in one file.
            var primaryCodes = rows.Skip(1).Where(row => row.Count > 0 && row[0].Value != "").Select(row => row[0].Value).ToArray();
            if (HasDuplicates(primaryCodes.Select(code => "EN-" + code).Concat(registrations.Select(row => row["Code"]))) ||
                HasDuplicates(primaryCodes.Select(code => "PER-" + code).Concat(registrations.Select(row => row["Period"]))
                    .Concat(input.Rows["فترات سابقة اختيارية"].Select(row => row["Code"]))) ||
                linkedRows.Keys.Any(sheet => sheet != "التسجيلات" && HasDuplicates(input.Rows[sheet].Select(row => row["Code"]))) ||
                HasDuplicates(input.Rows["تحصيلات سابقة اختيارية"].Select(row => row["Evidence"]))) return null;
        }
        var sessionCodes = Codes(attendance.Select(row => row["Session"]).Concat(assessments.Select(row => row["Session"])));
        var sessionRows = input.Rows["حصص فعلية اختيارية"].Where(row => sessionCodes.Contains(row["Code"])).Select(row => row.Number).ToHashSet();
        var correctionNumbers = linkedRows.ToDictionary(pair => pair.Key, pair => MapNumbers(pair.Value));
        correctionNumbers["اللاعبون"] = MapNumbers(correctionRows);
        if (errors.Any(error => !correctionNumbers.TryGetValue(error.Sheet, out var matching) || !matching.ContainsKey(error.Row))) return null;

        var guide = new[] {
            ConnectedImportSchema.Row("تصحيح صفوف اللاعبين", "صحّح الحقول أدناه، واترك كود اللاعب كما هو إلا إذا كان الكود نفسه سبب المشكلة."),
            ConnectedImportSchema.Row("ترتيب الاعتماد", "افحص ملف السليمين واعتمده أولًا. ثم افحص هذا الملف بعد التصحيح واعتمده."),
            ConnectedImportSchema.Row("هاتف ولي الأمر", "أدخله كنص. إذا لم تجد ولي الأمر بعد تصحيح الهاتف، انسخ صفه المقصود من ورقة أولياء الأمور في الملف الأصلي."),
            ConnectedImportSchema.Row("السجلات المرتبطة", "إذا غيّرت كود اللاعب، حدّث كوده في التسجيلات الإضافية والأكواد المشتقة منه في التاريخ، مثل EN-PL01 وPER-PL01."),
            ConnectedImportSchema.Row("الصف هنا والصف الأصلي", "الحقل والتصحيح المطلوب")
        }.Concat(errors.OrderBy(e => e.Sheet == "اللاعبون" ? 0 : 1).ThenBy(e => e.Row).Select(error => ConnectedImportSchema.Row(
            $"الصف {correctionNumbers[error.Sheet][error.Row]} · الأصل {error.Row}", $"{error.Sheet} · {error.ColumnLabel} · {error.Message}"))).ToArray();
        return (SelectPlayers(valid, false), SelectPlayers([rows[0], .. correctionRows.Select(row => rows[row - 1])], true), relatedCount);

        ImportWorkbook SelectPlayers(IReadOnlyList<IReadOnlyList<ImportCell>> players, bool forCorrection)
        {
            var phones = players.Skip(1).Where(row => row.Count > 3)
                .Select(row => EgyptPhoneNormalizer.Normalize(row[3].Value)).OfType<string>().ToHashSet(StringComparer.Ordinal);
            return new(book.Sheets.Select(sheet => sheet.Name switch {
                "اللاعبون" => sheet with { Rows = players },
                "أولياء الأمور" => sheet with { Rows = sheet.Rows.Where((row, index) => index == 0 ||
                    row.Count > 2 && EgyptPhoneNormalizer.Normalize(row[2].Value) is { } phone && phones.Contains(phone)).ToArray() },
                "ابدأ هنا" when forCorrection => sheet with { Rows = guide },
                _ when linkedRows.TryGetValue(sheet.Name, out var linked)
                    => sheet with { Rows = sheet.Rows.Where((_, index) => index == 0 || linked.Contains(index + 1) == forCorrection).ToArray() },
                "حصص فعلية اختيارية" when forCorrection
                    => sheet with { Rows = sheet.Rows.Where((_, index) => index == 0 || sessionRows.Contains(index + 1)).ToArray() },
                "معايير تقييم اختيارية" when forCorrection && assessments.Length == 0
                    => sheet with { Rows = sheet.Rows.Take(1).ToArray() },
                _ => sheet
            }).ToArray());
        }
        static HashSet<string> Codes(IEnumerable<string> values) => values.Where(value => value.Length > 0).ToHashSet(StringComparer.Ordinal);
        static bool HasDuplicates(IEnumerable<string> values)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return values.Any(value => value.Length > 0 && !seen.Add(value));
        }
        static Dictionary<int, int> MapNumbers(IEnumerable<int> numbers) => numbers.Order()
            .Select((original, index) => (original, current: index + 2)).ToDictionary(row => row.original, row => row.current);
    }
    public static ImportWorkbook Template(Guid academy, string currency, DateOnly day)
    {
        return new(new[] { Guide() }.Concat(Sheets.Select(s => new ImportSheet(s.Key,
            s.Key == "إعدادات الملف" ? Metadata(academy,day,"academy-opening") : [ConnectedImportSchema.Row(s.Value.Select(c=>c.Header).ToArray())]))).ToArray());
    }
    public static ImportSheet Guide() => new("ابدأ هنا", new[] {
        ConnectedImportSchema.Row("ملف بيانات الأكاديمية", "ابدأ بالفروع والرياضات والمدربين والمجموعات والباقات، ثم أولياء الأمور واللاعبين."),
        ConnectedImportSchema.Row("اللاعبون", "صف واحد لكل لاعب بكود قصير مثل PL01. اكتب الاسم الأول وباقي الاسم والهاتف والميلاد وأكواد المجموعة والباقة والفرع والرياضة. التسجيلات لرياضة إضافية فقط."),
        ConnectedImportSchema.Row("ربط الأبناء", "الهاتف المصري هو مفتاح البحث عن ولي الأمر في ورقته. الأخوة بنفس الهاتف؛ لا نستخدم الاسم للربط. راجع العلاقات قبل الاعتماد، والحساب القائم يحتاج مراجعة الهوية."),
        ConnectedImportSchema.Row("الاشتراك والحصص", "تاريخ تسجيل اللاعب هو أقدم بداية اشتراك مرتبطة به في الملف. تحسب النهاية وإجمالي الحصص من الباقة، والمتبقي بخصم الحضور المسجل. أدخل المتبقي صراحةً إذا نقلت رصيدًا غير موثق بالحضور في الملف."),
        ConnectedImportSchema.Row("الصورة", "اختياري: ذكر أو أنثى أو محايد لاختيار صورة رمزية. الصور الشخصية ترفع من ملف اللاعب بعد الاستيراد؛ لا روابط عامة لصور الأطفال."),
        ConnectedImportSchema.Row("الأوراق الاختيارية", "البطولات والفرق والتسجيلات الإضافية وكل أوراق التاريخ يمكن تركها فارغة. مجموعة التدريب تحدد المدرب ومواعيده. فريق البطولة مستقل ويجب أن يطابق الفرع والرياضة والفئة."),
        ConnectedImportSchema.Row("إعدادات الملف", "بيانات ينشئها النظام: نسخة القالب للتحقق، الأكاديمية للعزل، كود المصدر لمنع التكرار، تاريخ القطع لحدود التاريخ. لا تعدلها عند التصحيح أو تنقل الملف لأكاديمية أخرى."),
        ConnectedImportSchema.Row("الخطوة التالية", "احفظ XLSX، اختر الملف ثم متابعة لفحص الملف. المعاينة لا تحفظ بيانات. راجع العلاقات ثم اعتمد. سعر الاشتراك لا يثبت قبضًا."),
        ConnectedImportSchema.Row("النموذج المكتمل", "أسماء اللاعبين وبيانات اتصال الأسر خيالية. لا تستخدمها للاتصال. أكواد الأشخاص والمصدر ثابتة عند إعادة الرفع لتجنب التكرار.") });
    private static IReadOnlyList<IReadOnlyList<ImportCell>> Metadata(Guid academy,DateOnly day,string source) => [ConnectedImportSchema.Row("الإعداد","القيمة"),ConnectedImportSchema.Row("نسخة القالب",Version),ConnectedImportSchema.Row("الأكاديمية",academy.ToString()),ConnectedImportSchema.Row("كود المصدر",source),ConnectedImportSchema.Row("تاريخ القطع",day.ToString("yyyy-MM-dd"))];
    public static ImportWorkbook BindSample(ImportWorkbook book, Guid academy, bool rebasePhones = true)
    {
        if (!IsCurrent(book)) throw new ImportFileException("النموذج المكتمل يحتاج نسخة القالب الحالية.");
        if(rebasePhones) {
            var offset = 1_000_000 + (int)(BitConverter.ToUInt32(academy.ToByteArray(),0)%7_000_000);
            book = new(book.Sheets.Select(s=>s.Name is "المدربون" or "أولياء الأمور" or "اللاعبون" ? s with { Rows=s.Rows.Select((r,i)=>i==0?r:r.Select((c,j)=> (s.Name=="اللاعبون"&&j==3 || s.Name!="اللاعبون"&&j==2) && c.Value.Length==11 ? new ImportCell(c.Value[..3]+(int.Parse(c.Value[3..],CultureInfo.InvariantCulture)+offset).ToString("00000000",CultureInfo.InvariantCulture),true):c).ToArray()).ToArray() }:s).ToArray());
        }
        return new(book.Sheets.Select(s=>s.Name != "إعدادات الملف" ? s : s with { Rows=s.Rows.Select(r=>r.Count==2 && r[0].Value=="الأكاديمية" ? ConnectedImportSchema.Row("الأكاديمية",academy.ToString()) : r).ToArray() }).ToArray());
    }
    public sealed record Normalized(ImportWorkbook Book, Dictionary<string,(string Team,string Tournaments,string Photo)> Extras, Dictionary<int,int> PrimaryRows, List<ImportIssue> Errors);
    public static Normalized Normalize(ImportWorkbook original)
    {
        foreach (var (name,fields) in Sheets) {
            var rows=original.Sheet(name).Rows;
            if(rows.Count==0 || !rows[0].Select(c=>c.Value).SequenceEqual(fields.Select(f=>f.Header))) throw new ImportFileException($"عناوين ورقة {name} لا تطابق القالب الحالي.");
            if(rows.Count>FriendlyImportSchema.RowLimit(name)+1) throw new ImportFileException($"تجاوزت ورقة {name} حد الصفوف.");
            if(rows.Skip(1).Any(r=>r.Skip(fields.Length).Any(c=>c.Value!=""))) throw new ImportFileException($"ورقة {name} تحتوي أعمدة إضافية.");
        }
        Dictionary<string,ImportCell> Cells(string name, IReadOnlyList<ImportCell> row) => Sheets[name].Select((f,i)=>(f.Key,Cell:i<row.Count?row[i]:new ImportCell("",true))).ToDictionary(x=>x.Key,x=>x.Cell);
        IEnumerable<(int Number, Dictionary<string, ImportCell> Values)> Rows(string name) => original.Sheet(name).Rows.Skip(1)
            .Select((row, index) => (Row: row, Number: index + 2)).Where(x => x.Row.Any(c => c.Value != ""))
            .Select(x => (x.Number, Cells(name, x.Row)));
        static string V(Dictionary<string,ImportCell> r,string k) => r.GetValueOrDefault(k)?.Value??"";
        var sheets=FriendlyImportSchema.Sheets.ToDictionary(s=>s.Key,s=>new List<IReadOnlyList<ImportCell>> {ConnectedImportSchema.Row(s.Value.Select(f=>f.Header).ToArray())});
        foreach(var name in Sheets.Keys.Where(s=>s!="اللاعبون")) foreach(var row in original.Sheet(name).Rows.Skip(1)) {
            var cells = Cells(name, row);
            sheets[name].Add(FriendlyImportSchema.Sheets[name].Select(f=>cells.GetValueOrDefault(f.Key)??new ImportCell("",true)).ToArray());
        }
        sheets["إعدادات الملف"]=sheets["إعدادات الملف"].Select(r=>r.Count==2&&r[0].Value=="نسخة القالب"?ConnectedImportSchema.Row("نسخة القالب",FriendlyImportSchema.Version):r).ToList();
        var guardians=Rows("أولياء الأمور").Select(r => r.Values).Where(g => g["Phone"].IsText)
            .ToLookup(g => EgyptPhoneNormalizer.Normalize(V(g,"Phone")), g => V(g,"Code"), StringComparer.Ordinal);
        var packages=Rows("الباقات").Select(r => r.Values).ToLookup(p => V(p,"Code"), StringComparer.Ordinal);
        var attendance=Rows("حضور اختياري").Select(r => r.Values).Where(a => V(a,"State") is "حاضر" or "متأخر")
            .ToLookup(a => (V(a,"Enrollment"), V(a,"Period")));
        var extras=new Dictionary<string,(string,string,string)>(); var primary=new Dictionary<int,int>();
        var errors = new List<ImportIssue>();
        var playerRows=Rows("اللاعبون").ToArray();
        var duplicateCodes = playerRows.GroupBy(r => V(r.Values,"Code"), StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        // The current template has no separate joining date. Derive it from
        // dated registrations and periods whose enrollment code has one owner.
        var enrollments = playerRows.Select(r => (Code: "EN-" + V(r.Values, "Code"), Player: V(r.Values, "Code"), Start: V(r.Values, "Start")))
            .Concat(Rows("التسجيلات").Select(r => (Code: V(r.Values, "Code"), Player: V(r.Values, "Player"), Start: V(r.Values, "Start"))))
            .Where(r => r.Code != "")
            .GroupBy(r => r.Code, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var linkedStarts = enrollments.Values.Select(r => (r.Player, r.Start))
            .Concat(Rows("فترات سابقة اختيارية").Select(r => (
                Player: enrollments.GetValueOrDefault(V(r.Values, "Enrollment")).Player, Start: V(r.Values, "Start"))))
            .Where(r => !string.IsNullOrEmpty(r.Player))
            .Select(r => (r.Player, Date: ImportPreview.Date(r.Start)))
            .Where(r => r.Date.HasValue)
            .ToLookup(r => r.Player, r => r.Date!.Value, StringComparer.Ordinal);
        foreach(var (number,p) in playerRows) {
            var valid = true;
            void Error(string field, string message) {
                valid = false;
                if (errors.Count < 300) errors.Add(new("اللاعبون", number, Players.Single(f => f.Key == field).Header, message));
            }
            var code=V(p,"Code"); var phone=EgyptPhoneNormalizer.NormalizeForNewMobile(V(p,"Phone"));
            var start = ImportPreview.Date(V(p,"Start"));
            var birth = ImportPreview.Date(V(p,"Birth"));
            foreach (var field in Players.Where(f => f.Required && V(p,f.Key) == "")) Error(field.Key,"حقل مطلوب.");
            if (code.Length > 0 && (!p["Code"].IsText || !Regex.IsMatch(code, "^[A-Za-z0-9_.:-]{1,24}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))))
                Error("Code", "اكتب كودًا نصيًا ثابتًا حتى 24 حرفًا لاتينيًا أو رقمًا، مثل PL01.");
            if (code.Length > 0 && duplicateCodes.Contains(code)) Error("Code", "كود اللاعب مكرر. استخدم صفًا واحدًا لكل لاعب.");
            if (V(p,"Phone") != "" && (!p["Phone"].IsText || phone is null))
                Error("Phone", "اكتب رقم موبايل مصري صالحًا في خلية نصية، مثل 01012345678 أو ‎+201012345678.");
            else if (phone is not null && guardians[phone].Count() != 1)
                Error("Phone", "طابق الرقم مع صف واحد في ورقة أولياء الأمور.");
            if (V(p,"Birth") != "" && (birth is null || birth.Value.Year < 2000)) Error("Birth", "أدخل تاريخ ميلاد صحيحًا بصيغة سنة-شهر-يوم.");
            if (V(p,"Start") != "" && (start is null || start.Value.Year < 2000)) Error("Start", "أدخل تاريخ بداية صحيحًا بصيغة سنة-شهر-يوم.");
            if (birth > start) Error("Start", "بداية الاشتراك تسبق تاريخ الميلاد.");
            if (V(p,"Gender") is not ("" or "ذكر" or "أنثى")) Error("Gender", "اختر ذكر أو أنثى، أو اترك الحقل فارغًا.");
            if (V(p,"Photo") is not ("" or "ذكر" or "أنثى" or "محايد")) Error("Photo", "اختر ذكر أو أنثى أو محايد، أو اترك الحقل فارغًا.");
            if (!valid) continue;
            var guardian=guardians[phone!].Single(); var enrollment="EN-"+code; var period="PER-"+code;
            extras.Add(enrollment,(V(p,"Team"),V(p,"Tournaments"),V(p,"Photo")));
            primary[sheets["التسجيلات"].Count + 1] = number;
            var registered = linkedStarts[code].Where(date => date >= birth.GetValueOrDefault()).Append(start!.Value).Min();
            while (sheets["اللاعبون"].Count < number - 1) sheets["اللاعبون"].Add([]);
            sheets["اللاعبون"].Add(ConnectedImportSchema.Row(code,(V(p,"First")+" "+V(p,"Last")).Trim(),V(p,"Birth"),V(p,"Gender"),guardian,registered.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));
            sheets["علاقات أولياء الأمور"].Add(ConnectedImportSchema.Row("REL-"+code,code,guardian,"ولي أمر"));
            var pack=packages[V(p,"Plan")].FirstOrDefault();
            var end=V(p,"End"); var entitled=V(p,"Entitled"); var remaining=V(p,"Remaining");
            if(pack is not null) {
                if(end=="" && start.HasValue) {
                    try {
                        if(int.TryParse(V(pack,"Months"),out var months)&&months is >0 and <=120)end=start.Value.AddMonths(months).AddDays(-1).ToString("yyyy-MM-dd");
                        else if(int.TryParse(V(pack,"Days"),out var days)&&days is >0 and <=3650)end=start.Value.AddDays(days-1).ToString("yyyy-MM-dd");
                    } catch (ArgumentOutOfRangeException) { Error("Start", "بداية الاشتراك لا تسمح بحساب نهاية صالحة لمدة الباقة."); }
                }
                if(V(pack,"Type")!="مدة") {
                    if(entitled=="")entitled=V(pack,"Sessions");
                    if(remaining=="" && int.TryParse(entitled,out var total)) {
                        var used=attendance[(enrollment,period)].Count();
                        remaining=(total-used).ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
            sheets["التسجيلات"].Add(ConnectedImportSchema.Row(enrollment,code,V(p,"Branch"),V(p,"Sport"),V(p,"Group"),V(p,"Plan"),"",period,V(p,"Start"),end,entitled,remaining,V(p,"Net")==""&&pack is not null?V(pack,"Price"):V(p,"Net"),V(p,"Currency")));
        }
        return new(new(new[]{Guide()}.Concat(FriendlyImportSchema.Sheets.Select(s=>new ImportSheet(s.Key,sheets[s.Key]))).ToArray()),extras,primary,errors);
    }
}
