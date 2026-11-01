using System.Globalization;

namespace Academy.Api.Importing;

// Arabic transport over the existing durable historical-import pipeline.
public static class ConnectedImportSchema
{
    public const string Version = "Academity.Connected.1";
    public sealed record Field(string Key, string Label, bool Required = false) { public string Header => Label + (Required ? " *" : ""); }
    public static readonly Dictionary<string, Field[]> Sheets = new() {
        ["الفروع"] = [new("Code","كود الفرع",true),new("Name","اسم الفرع",true),new("ExistingId","معرف قائم اختياري")],
        ["الرياضات"] = [new("Code","كود الرياضة",true),new("Name","اسم الرياضة",true),new("Kind","نوع الرياضة",true),new("ExistingId","معرف قائم اختياري")],
        ["الفئات العمرية"] = [new("Code","كود الفئة",true),new("Name","اسم الفئة",true),new("MinYear","أول سنة ميلاد"),new("MaxYear","آخر سنة ميلاد"),new("ExistingId","معرف قائم اختياري")],
        ["مستويات التدريب"] = [new("Code","كود المستوى",true),new("Name","اسم المستوى",true),new("Sport","كود الرياضة",true),new("Rank","الترتيب",true),new("ExistingId","معرف قائم اختياري")],
        ["أنماط التدريب"] = [new("Code","كود النمط",true),new("Name","اسم النمط",true)],
        ["المدربون"] = [new("Code","كود المدرب",true),new("Name","اسم المدرب",true),new("Phone","الهاتف كنص",true),new("Branches","أكواد الفروع",true),new("Sports","أكواد الرياضات",true),new("ExistingId","معرف عضوية قائمة اختياري")],
        ["المجموعات"] = [new("Code","كود المجموعة",true),new("Name","اسم المجموعة",true),new("Branch","كود الفرع",true),new("Sport","كود الرياضة",true),new("Age","كود الفئة",true),new("Capacity","السعة",true),new("Level","كود المستوى"),new("Mode","كود النمط",true),new("Coaches","أكواد المدربين",true),new("ExistingId","معرف قائم اختياري")],
        ["الحصص"] = [new("Code","كود الموعد",true),new("Group","كود المجموعة",true),new("Day","اليوم 0 الأحد حتى 6 السبت",true),new("Start","البداية HH:mm",true),new("End","النهاية HH:mm",true),new("From","من yyyy-MM-dd",true),new("To","إلى yyyy-MM-dd",true)],
        ["الباقات"] = [new("Code","كود الباقة",true),new("Name","اسم الباقة",true),new("Sport","كود الرياضة",true),new("Branches","أكواد الفروع",true),new("Type","مدة أو حصص أو مزدوجة",true),new("Price","السعر",true),new("Days","عدد الأيام"),new("Sessions","عدد الحصص"),new("Mode","كود النمط",true),new("ExistingId","معرف قائم اختياري"),new("Months","عدد الأشهر بدل الأيام")],
        ["العروض"] = [new("Code","كود العرض",true),new("Name","اسم العرض",true),new("Type","قيمة ثابتة أو نسبة مئوية",true),new("Value","القيمة",true),new("Branch","كود الفرع"),new("Sport","كود الرياضة"),new("Plan","كود الباقة"),new("From","من yyyy-MM-dd"),new("To","إلى yyyy-MM-dd"),new("ExistingId","معرف قائم اختياري")],
        ["البطولات"] = [new("Code","كود البطولة",true),new("Name","اسم البطولة",true),new("Sport","كود الرياضة",true),new("Fee","رسوم المشاركة",true),new("ExistingId","معرف قائم اختياري")],
        ["الفرق"] = [new("Code","كود الفريق",true),new("Name","اسم الفريق",true),new("Branch","كود الفرع",true),new("Sport","كود الرياضة",true),new("Age","كود الفئة",true),new("Coach","كود المدرب",true),new("ExistingId","معرف قائم اختياري")],
        ["الأعضاء"] = [new("Player","كود اللاعب",true),new("Enrollment","كود التسجيل",true),new("Family","كود الأسرة"),new("Name","اسم اللاعب",true),new("Guardian","اسم ولي الأمر",true),new("Phone","هاتف ولي الأمر كنص",true),new("Birth","الميلاد yyyy-MM-dd",true),new("Branch","كود الفرع",true),new("Sport","كود الرياضة",true),new("Level","كود المستوى"),new("Group","كود المجموعة",true),new("Plan","كود الباقة",true),new("Start","بداية الاشتراك yyyy-MM-dd",true),new("End","نهاية الاشتراك yyyy-MM-dd"),new("Entitled","إجمالي الحصص"),new("Remaining","الحصص المتبقية"),new("Pricing","طريقة السعر"),new("Base","السعر الأصلي المتفق"),new("Discount","الخصم المتفق"),new("Net","الصافي المتفق"),new("Currency","العملة",true),new("State","قائم أو جديد",true),new("Notes","ملاحظات"),new("Gender","ذكر أو أنثى"),new("Offer","كود العرض"),new("Tournaments","أكواد البطولات"),new("Team","كود الفريق")],
        ["الأرصدة الافتتاحية"] = [new("Enrollment","كود التسجيل",true),new("Amount","الرصيد الموقع",true),new("Currency","العملة",true),new("Cutoff","تاريخ القطع yyyy-MM-dd",true),new("Reference","مرجع الرصيد",true),new("Evidence","سند الاعتماد",true)],
        ["إعدادات الملف"] = [new("Key","الإعداد"),new("Value","القيمة")]
    };
    public static readonly string[] Names = new[] { "دليل الاستخدام" }.Concat(Sheets.Keys).ToArray();
    public static IReadOnlyList<ImportCell> Row(params string[] values) => values.Select(v => new ImportCell(v,true)).ToArray();
    public static bool IsConnected(ImportWorkbook b) => b.Sheets.Any(s => s.Name == "الأعضاء") || FriendlyImportSchema.IsFriendly(b);
    public static string? Meta(ImportWorkbook b,string key) => b.Sheet("إعدادات الملف").Rows.Skip(1).FirstOrDefault(r=>r.Count==2&&r[0].Value==key)?[1].Value;
    public static ImportWorkbook Template(Guid academy,string currency,DateOnly day,bool example=false,int count=10,string source="academy-opening",int phoneOffset=0)
    {
        var sheets = new List<ImportSheet> { new("دليل الاستخدام", new[] {
            Row("I Academy One — استيراد أكاديمية", "النجمة * تعني حقلًا مطلوبًا. كل البيانات في النموذج خيالية للاختبار."),
            Row("البدء", "املأ إعدادات الملف وحدد كود مصدر ثابتًا لا يتغير عند التصحيح أو إعادة الرفع. لا تنقل ملف أكاديمية إلى أخرى."),
            Row("الأكواد", "مثال: B01 فرع، S01 رياضة، C01 مدرب، G01 مجموعة، P01 باقة، T01 بطولة. أكواد قصيرة ثابتة وليست أسماء."),
            Row("العلاقات", "الأعضاء تربط الطفل والولي بالمجموعة والباقة. أخوان لهما نفس كود الأسرة والهاتف. رياضتان للطفل = كود لاعب واحد وكودا تسجيل مختلفان."),
            Row("القوائم", "افصل أكواد المدربين/البطولات/الفروع/الرياضات بفاصلة , أو ، أو ;. الفراغ حول الكود مسموح."),
            Row("اختياري", "يمكن ترك أوراق العروض والبطولات والفرق والمستويات والأرصدة فارغة. أنماط التدريب قيم ثابتة لا تُعدل."),
            Row("الفرق", "ليست مجموعات تدريب. للمشاركة في بطولة حدد فريقًا متوافقًا صراحةً. لا تُنشأ مباريات أو نتائج ولا يتغير جمهور مباريات سبق نشرها."),
            Row("الهاتف والتاريخ", "الهواتف والأكواد نص للحفاظ على 0 و+. تواريخ نص yyyy-MM-dd وأوقات HH:mm؛ الأيام 0 الأحد حتى 6 السبت."),
            Row("السعر", "اختر «صافي متفق» للصافي فقط، أو «السعر والخصم» للأصل والخصم والصافي المتطابق. اترك الثلاثة فارغة إن كانت غير معروفة للمراجعة المالية."),
            Row("المال", "السعر الافتتاحي اتفاق تاريخي لا قبض اليوم. العروض تحفظ للاستحقاق القادم ولا تعيد حساب الاتفاق الافتتاحي. لا إيصالات أو دفع أو حضور مستورد."),
            Row("التأكيد", "المعاينة تعرض الأخطاء وروابط الإنشاء/إعادة الاستخدام. لا ينشر شيء قبل التأكيد؛ الدفعة تنشر كاملة أو تتراجع كاملة."),
            Row("إعادة الرفع", "لا تغير المصدر والأكواد. لا يستبدل الملف بيانات قائمة مختلفة؛ صحح الملف قبل الاعتماد. المعرف القائم اختياري من مسؤول مخول لإعادة استخدام سجل مطابق."),
        }.Concat(Sheets.SelectMany(s=>s.Value.Select(f=>Row(s.Key+" — "+f.Header,f.Required?"مطلوب عند وجود صف في هذه الورقة":"اختياري حسب نموذج الخدمة")))).ToArray()) };
        foreach(var (name,fields) in Sheets) sheets.Add(new(name,[Row(fields.Select(f=>f.Header).ToArray())]));
        void Add(string name,params string[] values) { var i=sheets.FindIndex(s=>s.Name==name); var old=sheets[i]; var padded=values.Concat(Enumerable.Repeat("",Math.Max(0,Sheets[name].Length-values.Length))).ToArray(); sheets[i]=old with {Rows=old.Rows.Append(Row(padded)).ToArray()}; }
        Add("أنماط التدريب","GROUP","جماعي"); Add("أنماط التدريب","PRIVATE","خاص"); Add("أنماط التدريب","SEMI","شبه خاص");
        Add("إعدادات الملف","نسخة القالب",Version);Add("إعدادات الملف","الأكاديمية",academy.ToString());Add("إعدادات الملف","كود المصدر",source);Add("إعدادات الملف","تاريخ القطع",day.ToString("yyyy-MM-dd"));
        if(!example)return new(sheets);
        Add("الفروع","B01","فرع الندى التجريبي");
        Add("الرياضات","S01","كرة قدم تدريبية","كرة قدم");Add("الرياضات","S02","سباحة تدريبية","سباحة");
        Add("الفئات العمرية","A01","ناشئون تجريبيون","2014","2018");
        Add("مستويات التدريب","L01","تأسيسي","S01","1");
        Add("المدربون","C01","كابتن تجريبي أول","015"+(phoneOffset+900001).ToString("00000000"),"B01","S01");
        Add("المدربون","C02","كابتن تجريبي ثان","015"+(phoneOffset+900002).ToString("00000000"),"B01","S02");
        Add("المجموعات","G01","مجموعة النجوم التجريبية","B01","S01","A01",Math.Max(20,count).ToString(),"L01","GROUP","C01");
        Add("المجموعات","G02","مجموعة الموج التجريبية","B01","S02","A01","20","","GROUP","C02");
        var end=day.AddDays(27).ToString("yyyy-MM-dd");var start=day.ToString("yyyy-MM-dd");
        Add("الحصص","W01","G01","6","16:00","17:00",start,end);Add("الحصص","W02","G02","6","18:00","19:00",start,end);
        Add("الباقات","P01","باقة الكرة التجريبية","S01","B01","مزدوجة","1000","28","8","GROUP");
        Add("الباقات","P02","باقة الموج التجريبية","S02","B01","مزدوجة","800","28","8","GROUP");
        Add("العروض","O01","عرض تجريبي مستقل","نسبة مئوية","10","B01","S01","P01",start,end);
        Add("البطولات","T01","بطولة الأمل التجريبية","S01","50");Add("البطولات","T02","بطولة الغد التجريبية","S01","0");
        Add("الفرق","TM01","فريق الأمل التجريبي","B01","S01","A01","C01");
        void Member(int n,bool secondSport=false) {var fam=n<=2?1:n;var p="PL"+n.ToString("000");Add("الأعضاء",p,"E"+n.ToString("000")+(secondSport?"B":"A"),"H"+fam,"لاعب خيالي "+n,"ولي أمر خيالي "+fam,"012"+(phoneOffset+fam).ToString("00000000"),"2016-03-15","B01",secondSport?"S02":"S01",secondSport?"":"L01",secondSport?"G02":"G01",secondSport?"P02":"P01",start,end,"8","6","صافي متفق","","",secondSport?"800":"1000",currency,"قائم","مثال اصطناعي","ذكر",!secondSport&&n==4?"O01":"",secondSport||n==1?"":n==2?"T01":"T01, T02",secondSport||n==1?"":"TM01");}
        for(var n=1;n<=count;n++)Member(n);Member(1,true);
        return new(sheets);
    }
}
