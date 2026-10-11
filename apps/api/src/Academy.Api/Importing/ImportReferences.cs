using System.Security.Cryptography;
using System.Text.Json;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Importing;

public sealed record ImportReference(string Kind, Guid Id, string Name, Guid? Branch = null, Guid? Sport = null,
    Guid? Category = null, int? MinYear = null, int? MaxYear = null, int? Capacity = null, int Occupied = 0,
    int Revision = 0, SubscriptionPlanType? PlanType = null, bool AllBranches = false, Guid[]? Branches = null);
public sealed record ImportReferences(Guid AcademyId, string Currency, IReadOnlyList<ImportReference> Values, string Stamp)
{
    public ImportReference? Find(string kind, string code) => Guid.TryParse(code, out var id) ? Values.FirstOrDefault(v => v.Kind == kind && v.Id == id) : null;
    public static async Task<ImportReferences> Load(FoundationDbContext db, Guid academy)
    {
        var currency = await db.Academies.Where(a => a.Id == academy).Select(a => a.DefaultCurrency).SingleAsync();
        var values = new List<ImportReference>();
        values.AddRange(await db.Branches.AsNoTracking().Where(b => b.AcademyId == academy && b.IsActive).Select(b => new ImportReference("branch", b.Id, b.ArabicName, null, null, null, null, null, null, 0, 0, null, false, null)).ToListAsync());
        values.AddRange(await db.Sports.AsNoTracking().Where(b => b.AcademyId == academy && b.IsActive).Select(b => new ImportReference("sport", b.Id, b.ArabicName, null, null, null, null, null, null, 0, 0, null, false, null)).ToListAsync());
        values.AddRange(await db.TrainingGroups.AsNoTracking().Where(g => g.AcademyId == academy && g.IsActive && g.Branch.IsActive && g.Sport.IsActive && g.AgeCategory.IsActive)
            .Select(g => new ImportReference("group", g.Id, g.ArabicName, g.BranchId, g.SportId, g.AgeCategoryId, g.AgeCategory.MinimumBirthYear, g.AgeCategory.MaximumBirthYear, g.Capacity,
                db.SportEnrollments.Count(e => e.AcademyId == academy && e.TrainingGroupId == g.Id && e.IsActive && e.Status == Academy.Infrastructure.People.EnrollmentStatus.Active), 0, null, false, null)).ToListAsync());
        var plans = await db.SubscriptionPlans.TrainingOnly().AsNoTracking().Include(p => p.BranchPublications).Where(p => p.AcademyId == academy && p.IsActive).OrderBy(p => p.Id).ToListAsync();
        values.AddRange(plans.Select(p => new ImportReference("product", p.Id, p.ArabicName, Sport: p.SportId, Revision: p.Revision, PlanType: p.PlanType, AllBranches: p.AllBranches, Branches: p.BranchPublications.Where(b => b.IsActive).Select(b => b.BranchId).Order().ToArray())));
        var ordered = values.OrderBy(v => v.Kind).ThenBy(v => v.Id).ToArray();
        var stamp = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { academy, currency, values = ordered })));
        return new(academy, currency, ordered, stamp);
    }
    public ImportWorkbook Template(DateOnly cutoff)
    {
        static ImportCell Text(string v) => new(v, true);
        static IReadOnlyList<ImportCell> Row(params string[] cells) => cells.Select(Text).ToArray();
        return new([
            new("Instructions", new[] { Row("التعليمات", "طريقة التعبئة"), Row("الحد", "1000 تسجيل في ملف واحد؛ صف لكل لاعب ورياضة."),
                Row("الهوية", "LegacyPlayerId ثابت لكل طفل. لا تستخدم الاسم أو الهاتف كمفتاح الطفل. HouseholdRef يربط الأسرة بعد اعتماد إداري."),
                Row("الهواتف والتواريخ", "الهاتف كنص للحفاظ على الصفر. اكتب التواريخ كنص ISO yyyy-MM-dd."),
                Row("المراجع", "انسخ الأكواد من References. لا تغيّر أسماء الأوراق أو العناوين. المجموعات والجداول تُجهز أولًا."),
                Row("المال", "agreed_net يحفظ الصافي السابق دون خصم جديد. price_and_discount يستلزم الأساس والخصم والصافي المتطابق."),
                Row("نقص المالية", "اترك PricingMode والأسعار فارغة إن كانت غير معروفة؛ لا نعتبرها صفرًا. تتطلب مراجعة مالية لاحقة."),
                Row("الأرصدة", "OpenBalances اختياري؛ الموجب مستحق والسالب لصالح الأسرة، مع cutoff ومرجع وسند. ليست دفعة أو إيصال اليوم."),
                Row("المستوى", "LevelCode اختياري حاليًا. لا تكتب مستوى غير موجود؛ الفئة العمرية تُفحص من المجموعة."),
                Row("المعاينة", "المعاينة لا تنشئ ملفات أو مالًا ولا ترسل رموزًا. تُعتمد الدفعة كاملة بعد الفحص وإعادة التحقق من السعة والمراجع.") }
                .Concat(ImportSchema.Enrollments.Select(c => Row(c.Key, c.Label + (c.Required ? " — مطلوب" : " — اختياري حسب نوع الخدمة"))))
                .Concat(ImportSchema.OpenBalances.Select(c => Row("OpenBalances." + c.Key, c.Label + " — مطلوب عند إدخال رصيد"))).ToArray()),
            new("Enrollments", [ImportSchema.Enrollments.Select(c => Text(c.Key)).ToArray()]),
            new("References", new[] { Row("Kind", "Code", "Name", "BranchCode", "SportCode", "MinBirthYear", "MaxBirthYear", "Capacity", "Occupied", "PriceRevision") }
                .Concat(Values.Select(v => Row(v.Kind, v.Id.ToString(), v.Name, v.Branch?.ToString() ?? "", v.Sport?.ToString() ?? "", v.MinYear?.ToString() ?? "", v.MaxYear?.ToString() ?? "", v.Capacity?.ToString() ?? "", v.Occupied.ToString(), v.Revision.ToString()))).ToArray()),
            new("OpenBalances", [ImportSchema.OpenBalances.Select(c => Text(c.Key)).ToArray()]),
            new("Metadata", [Row("Key", "Value"), Row("SchemaVersion", ImportSchema.Version), Row("AcademyCode", AcademyId.ToString()), Row("SourceSystem", ""), Row("CutoffDate", cutoff.ToString("yyyy-MM-dd")), Row("ReferenceStamp", Stamp)])
        ]);
    }
}
