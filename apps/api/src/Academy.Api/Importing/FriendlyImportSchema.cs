namespace Academy.Api.Importing;

// A simpler transport for the same connected plan, authority and atomic worker.
// Linking codes are customer-owned references, never database identifiers.
public static class FriendlyImportSchema
{
    public const string Version = "iAcademy.Connected.2";
    public static readonly Dictionary<string, ConnectedImportSchema.Field[]> Sheets = Create();
    private static Dictionary<string, ConnectedImportSchema.Field[]> Create()
    {
        var result = ConnectedImportSchema.Sheets.Where(s => s.Key != "الأعضاء").ToDictionary(s => s.Key == "الحصص" ? "المواعيد الأسبوعية" : s.Key, s => s.Value);
        result["أولياء الأمور"] = [new("Code","كود ولي الأمر",true),new("Name","اسم ولي الأمر",true),new("Phone","الهاتف كنص",true),new("Associate","ربط حساب قائم بعد المراجعة")];
        result["اللاعبون"] = [new("Code","كود اللاعب",true),new("Name","اسم اللاعب",true),new("Birth","الميلاد yyyy-MM-dd",true),new("Gender","ذكر أو أنثى"),new("Guardian","كود ولي الأمر الأساسي",true),new("Registered","تاريخ التسجيل yyyy-MM-dd",true)];
        result["علاقات أولياء الأمور"] = [new("Code","كود العلاقة",true),new("Player","كود اللاعب",true),new("Guardian","كود ولي الأمر",true),new("Relation","صلة القرابة",true)];
        result["التسجيلات"] = [new("Code","كود التسجيل",true),new("Player","كود اللاعب",true),new("Branch","كود الفرع",true),new("Sport","كود الرياضة",true),new("Group","كود المجموعة",true),new("Plan","كود الباقة",true),new("Level","كود المستوى"),new("Period","كود الفترة الحالية",true),new("Start","بداية الاشتراك yyyy-MM-dd",true),new("End","نهاية الاشتراك yyyy-MM-dd"),new("Entitled","إجمالي الحصص"),new("Remaining","الحصص المتبقية"),new("Net","الصافي المتفق",true),new("Currency","العملة",true)];
        result["فترات سابقة اختيارية"] = [new("Code","كود الفترة",true),new("Enrollment","كود التسجيل",true),new("Plan","كود الباقة",true),new("Start","البداية yyyy-MM-dd",true),new("End","النهاية yyyy-MM-dd"),new("Entitled","إجمالي الحصص"),new("Remaining","الحصص المتبقية"),new("Net","الصافي المتفق",true),new("Currency","العملة",true)];
        result["حصص فعلية اختيارية"] = [new("Code","كود الحصة",true),new("Group","كود المجموعة",true),new("Date","التاريخ yyyy-MM-dd",true),new("Start","البداية HH:mm",true),new("End","النهاية HH:mm",true),new("State","منعقدة أو مجدولة أو ملغاة",true)];
        result["حضور اختياري"] = [new("Code","كود سجل الحضور",true),new("Session","كود الحصة",true),new("Enrollment","كود التسجيل",true),new("State","حاضر أو متأخر أو غائب أو بعذر",true),new("Period","كود الفترة المخصوم منها")];
        result["معايير تقييم اختيارية"] = [new("Code","كود المعيار",true),new("Sport","كود الرياضة",true),new("Name","اسم المعيار",true),new("Weight","الوزن",true),new("Axis","محور الكرة الاختياري")];
        result["تقييمات اختيارية"] = [new("Code","كود التقييم",true),new("Enrollment","كود التسجيل",true),new("Group","كود المجموعة",true),new("Coach","كود المدرب",true),new("Date","التاريخ yyyy-MM-dd",true),new("Month","فترة التقرير yyyy-MM",true),new("State","مسودة أو منشور",true),new("Session","كود الحصة"),new("Notes","ملاحظة عامة")];
        result["درجات اختيارية"] = [new("Code","كود الدرجة",true),new("Evaluation","كود التقييم",true),new("Criterion","كود المعيار",true),new("Score","درجة من 0 إلى 100")];
        result["تحصيلات سابقة اختيارية"] = [new("Code","كود التحصيل",true),new("Period","كود الفترة",true),new("Date","تاريخ القبض yyyy-MM-dd",true),new("Amount","المبلغ المقبوض",true),new("Currency","العملة",true),new("Method","نقدي أو إلكتروني",true),new("Evidence","مرجع السند التاريخي",true)];
        return result;
    }
    public static readonly string[] Names = new[] { "ابدأ هنا" }.Concat(Sheets.Keys).ToArray();
    public static bool IsFriendly(ImportWorkbook book) => book.Sheets.Any(s => s.Name == "اللاعبون");
    public static bool Historical(string sheet) => sheet.EndsWith("اختيارية") || sheet.EndsWith("اختياري");
    public static int RowLimit(string sheet) => sheet == "حضور اختياري" ? 20000 : Historical(sheet) ? 10000 : ImportSchema.MaxRows;
    public static ImportWorkbook Template(Guid academy, string currency, DateOnly day, string source = "academy-opening")
    {
        var book = ConnectedImportSchema.Template(academy, currency, day, source: source);
        var guide = new ImportSheet("ابدأ هنا", new[] {
            ConnectedImportSchema.Row("iAcademy One — بداية أكاديميتك", "املأ البيانات المطلوبة ثم ارفع الملف للمعاينة. النجمة * تعني حقلًا مطلوبًا عند وجود صف."),
            ConnectedImportSchema.Row("1. الإعدادات والمراجع", "راجع الأكاديمية والمصدر وتاريخ القطع. املأ الفروع BR01 والرياضات SP01 والفئات AG01 ثم المدربين CO01 والمجموعات GR01 والباقات PK01 والمواعيد الأسبوعية."),
            ConnectedImportSchema.Row("2. أولياء الأمور واللاعبون", "ولي الأمر مرة واحدة GU001. اللاعب مرة واحدة PL0001 دون هاتف خاص به. أدخل كود ولي الأمر الأساسي في صف اللاعب. الأخوان يشيران لنفس ولي الأمر."),
            ConnectedImportSchema.Row("3. العلاقات والتسجيلات", "لكل لاعب علاقة أساسية صريحة في ورقة العلاقات. أضف صفًا لولي أمر إضافي. التسجيل EN0001 يربط اللاعب بمجموعة وباقة متوافقتين. رياضة ثانية = صف تسجيل آخر لنفس اللاعب."),
            ConnectedImportSchema.Row("4. التاريخ الاختياري", "يمكن ترك أوراق التاريخ فارغة. الفترة الحالية في التسجيلات، والفترات السابقة في ورقتها. الحضور على حصة فعلية. المسودة لا تظهر لولي الأمر."),
            ConnectedImportSchema.Row("الهواتف والأكواد", "اكتب كنص. يقبل الهاتف المصري 010… أو +2010… والأرقام العربية. لا تدمج أشخاصًا بالاسم. الهاتف ذو أكثر من دور يحتاج نفس الهوية وربطًا صريحًا بعد المراجعة."),
            ConnectedImportSchema.Row("المال والتاريخ", "الصافي اتفاق اشتراك وليس إثبات قبض. ورقة التحصيلات فقط توثق القبض بسند وتاريخه بعد اعتماد مالي منفصل. لا تكتب قبضًا لم يحدث. الحصص المتبقية تشمل كل الاستهلاك التاريخي المسجل."),
            ConnectedImportSchema.Row("5. الرفع", "احفظ xlsx ثم اختر الملف، صحح الورقة والصف والعمود المذكورين، راجع الأعداد والعلاقات والتاريخ والمال، ثم أكد الدفعة. لا يضاف شيء أثناء المعاينة."),
            ConnectedImportSchema.Row("التصحيح وإعادة الرفع", "لا تغير كود المصدر أو أكواد الأشخاص والسجلات. النشر ذري: نجاح كامل أو تراجع كامل. الملف نفسه من المنصة أو المالك يعرض نفس النتيجة دون تكرار."),
            ConnectedImportSchema.Row("الحدود", "حتى 1000 صف بكل ورقة أساسية، و10000 بكل ورقة تاريخ، و20000 للحضور، وحجم مضغوط 5 ميجابايت. القوائم مساعدة، وفحص الخادم هو المرجع النهائي. لا صيغ أو ملفات مرفقة."),
        });
        var sheets = new List<ImportSheet> { guide };
        foreach (var (name, fields) in Sheets)
        {
            var oldName = name == "المواعيد الأسبوعية" ? "الحصص" : name;
            var rows = book.Sheets.FirstOrDefault(s => s.Name == oldName)?.Rows ?? [ConnectedImportSchema.Row(fields.Select(f => f.Header).ToArray())];
            if (name == "إعدادات الملف") rows = rows.Select(r => r.Count == 2 && r[0].Value == "نسخة القالب" ? ConnectedImportSchema.Row("نسخة القالب", Version) : r).ToArray();
            sheets.Add(new(name, rows));
        }
        return new(sheets);
    }
}
