using System.Globalization;
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
    public static ImportWorkbook Template(Guid academy, string currency, DateOnly day)
    {
        return new(new[] { Guide() }.Concat(Sheets.Select(s => new ImportSheet(s.Key,
            s.Key == "إعدادات الملف" ? Metadata(academy,day,"academy-opening") : [ConnectedImportSchema.Row(s.Value.Select(c=>c.Header).ToArray())]))).ToArray());
    }
    public static ImportSheet Guide() => new("ابدأ هنا", new[] {
        ConnectedImportSchema.Row("ملف بيانات الأكاديمية", "ابدأ بالفروع والرياضات والمدربين والمجموعات والباقات، ثم أولياء الأمور واللاعبين."),
        ConnectedImportSchema.Row("اللاعبون", "صف واحد لكل لاعب بكود قصير مثل PL01. اكتب الاسم الأول وباقي الاسم والهاتف والميلاد وأكواد المجموعة والباقة والفرع والرياضة. التسجيلات لرياضة إضافية فقط."),
        ConnectedImportSchema.Row("ربط الأبناء", "الهاتف المصري هو مفتاح البحث عن ولي الأمر في ورقته. الأخوة بنفس الهاتف؛ لا نستخدم الاسم للربط. راجع العلاقات قبل الاعتماد، والحساب القائم يحتاج مراجعة الهوية."),
        ConnectedImportSchema.Row("الاشتراك والحصص", "نهاية الاشتراك وإجمالي الحصص يحسبان من الباقة عند تركهما فارغين. المتبقي = إجمالي الحصص ناقص الحضور المستهلك في الملف. عند نقل رصيد تاريخي غير مسجل أدخل المتبقي الصحيح صراحةً."),
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
    public sealed record Normalized(ImportWorkbook Book, Dictionary<string,(string Team,string Tournaments,string Photo)> Extras, Dictionary<string,int> PrimaryRows);
    public static Normalized Normalize(ImportWorkbook original)
    {
        foreach (var (name,fields) in Sheets) {
            var rows=original.Sheet(name).Rows;
            if(rows.Count==0 || !rows[0].Select(c=>c.Value).SequenceEqual(fields.Select(f=>f.Header))) throw new ImportFileException($"عناوين ورقة {name} لا تطابق القالب الحالي.");
            if(rows.Count>FriendlyImportSchema.RowLimit(name)+1) throw new ImportFileException($"تجاوزت ورقة {name} حد الصفوف.");
            if(rows.Skip(1).Any(r=>r.Skip(fields.Length).Any(c=>c.Value!=""))) throw new ImportFileException($"ورقة {name} تحتوي أعمدة إضافية.");
        }
        List<Dictionary<string,ImportCell>> Rows(string name) => original.Sheet(name).Rows.Skip(1).Where(r=>r.Any(c=>c.Value!="")).Select(r=>Sheets[name].Select((f,i)=>(f.Key,Cell:i<r.Count?r[i]:new ImportCell("",true))).ToDictionary(x=>x.Key,x=>x.Cell)).ToList();
        static string V(Dictionary<string,ImportCell> r,string k) => r.GetValueOrDefault(k)?.Value??"";
        var sheets=FriendlyImportSchema.Sheets.ToDictionary(s=>s.Key,s=>new List<IReadOnlyList<ImportCell>> {ConnectedImportSchema.Row(s.Value.Select(f=>f.Header).ToArray())});
        foreach(var (name,fields) in Sheets.Where(s=>s.Key!="اللاعبون")) foreach(var row in Rows(name))
            sheets[name].Add(FriendlyImportSchema.Sheets[name].Select(f=>row.GetValueOrDefault(f.Key)??new ImportCell("",true)).ToArray());
        sheets["إعدادات الملف"]=sheets["إعدادات الملف"].Select(r=>r.Count==2&&r[0].Value=="نسخة القالب"?ConnectedImportSchema.Row("نسخة القالب",FriendlyImportSchema.Version):r).ToList();
        var guardians=Rows("أولياء الأمور"); var packages=Rows("الباقات");
        var extras=new Dictionary<string,(string,string,string)>(); var primary=new Dictionary<string,int>();
        var playerRows=Rows("اللاعبون");
        foreach(var p in playerRows) {
            var code=V(p,"Code"); var phone=EgyptPhoneNormalizer.Normalize(V(p,"Phone"));
            if(!p["Phone"].IsText || phone is null) throw new ImportFileException($"ورقة اللاعبون: اكتب هاتف ولي الأمر كنص صالح للاعب {code}.");
            var matches=guardians.Where(g=>EgyptPhoneNormalizer.Normalize(V(g,"Phone"))==phone).ToArray();
            if(matches.Length!=1) throw new ImportFileException($"ورقة اللاعبون: هاتف {code} يجب أن يطابق ولي أمر واحدًا في ورقة أولياء الأمور؛ الربط بالهاتف وليس الاسم.");
            var guardian=V(matches[0],"Code"); var enrollment="EN-"+code; var period="PER-"+code;
            if(!extras.TryAdd(enrollment,(V(p,"Team"),V(p,"Tournaments"),V(p,"Photo")))) throw new ImportFileException($"كود اللاعب {code} مكرر.");
            primary[enrollment]=original.Sheet("اللاعبون").Rows.ToList().FindIndex(r=>r.Count>0&&r[0].Value==code)+1;
            if(V(p,"Photo") is not ("" or "ذكر" or "أنثى" or "محايد")) throw new ImportFileException($"صورة {code}: اختر ذكر أو أنثى أو محايد، أو اتركها فارغة وارفع الصورة من ملف اللاعب.");
            sheets["اللاعبون"].Add(ConnectedImportSchema.Row(code,(V(p,"First")+" "+V(p,"Last")).Trim(),V(p,"Birth"),V(p,"Gender"),guardian,V(p,"Start")));
            if(V(p,"First")==""||V(p,"Last")=="") throw new ImportFileException($"أكمل الاسم الأول وباقي الاسم للاعب {code}.");
            sheets["علاقات أولياء الأمور"].Add(ConnectedImportSchema.Row("REL-"+code,code,guardian,"ولي أمر"));
            var pack=packages.FirstOrDefault(k=>V(k,"Code")==V(p,"Plan"));
            var start=ImportPreview.Date(V(p,"Start")); var end=V(p,"End"); var entitled=V(p,"Entitled"); var remaining=V(p,"Remaining");
            if(pack is not null) {
                if(end=="" && start.HasValue) {
                    if(int.TryParse(V(pack,"Months"),out var months)&&months is >0 and <=120)end=start.Value.AddMonths(months).AddDays(-1).ToString("yyyy-MM-dd");
                    else if(int.TryParse(V(pack,"Days"),out var days)&&days is >0 and <=3650)end=start.Value.AddDays(days-1).ToString("yyyy-MM-dd");
                }
                if(V(pack,"Type")!="مدة") {
                    if(entitled=="")entitled=V(pack,"Sessions");
                    if(remaining=="" && int.TryParse(entitled,out var total)) {
                        var used=Rows("حضور اختياري").Count(a=>V(a,"Enrollment")==enrollment && V(a,"Period")==period && V(a,"State") is "حاضر" or "متأخر");
                        remaining=(total-used).ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
            sheets["التسجيلات"].Add(ConnectedImportSchema.Row(enrollment,code,V(p,"Branch"),V(p,"Sport"),V(p,"Group"),V(p,"Plan"),"",period,V(p,"Start"),end,entitled,remaining,V(p,"Net")==""&&pack is not null?V(pack,"Price"):V(p,"Net"),V(p,"Currency")));
        }
        return new(new(new[]{Guide()}.Concat(FriendlyImportSchema.Sheets.Select(s=>new ImportSheet(s.Key,sheets[s.Key]))).ToArray()),extras,primary);
    }
}
