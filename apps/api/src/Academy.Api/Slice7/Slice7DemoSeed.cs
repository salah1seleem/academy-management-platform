using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Content;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Academy.Api.Slice7;

public static class Slice7DemoSeed
{
    public static readonly Guid PublishedMedicalId = Guid.Parse("81000000-0000-0000-0000-000000000001");
    public static readonly Guid DraftMedicalId = Guid.Parse("81000000-0000-0000-0000-000000000002");
    public static readonly Guid AcademyBMedicalId = Guid.Parse("82000000-0000-0000-0000-000000000001");
    public static readonly Guid PublishedMediaId = Guid.Parse("83000000-0000-0000-0000-000000000001");
    public static readonly Guid DraftMediaId = Guid.Parse("83000000-0000-0000-0000-000000000003");
    public static readonly Guid AcademyBMediaId = Guid.Parse("84000000-0000-0000-0000-000000000001");
    public static readonly Guid InactiveCatalogId = Guid.Parse("85000000-0000-0000-0000-000000000009");
    public static readonly Guid ShakshukaId = Guid.Parse("86000000-0000-0000-0000-000000000001");

    private static readonly (string Name, NutritionCategory[] Categories)[] Meals =
    [
        ("شكشوكة", [NutritionCategory.Breakfast, NutritionCategory.Dinner]), ("كبدة", [NutritionCategory.Breakfast, NutritionCategory.Dinner]),
        ("كشري", [NutritionCategory.Breakfast, NutritionCategory.Lunch]), ("جبنة بالطماطم", [NutritionCategory.Breakfast]),
        ("فول وطعمية", [NutritionCategory.Breakfast]), ("مناقيش", [NutritionCategory.Breakfast]),
        ("فتة", [NutritionCategory.Lunch]), ("كفتة لحم", [NutritionCategory.Lunch]), ("مكرونة بشاميل", [NutritionCategory.Lunch]),
        ("مقلوبة دجاج", [NutritionCategory.Lunch]), ("شيش طاووق", [NutritionCategory.Lunch]), ("شاورما", [NutritionCategory.Dinner]),
        ("حواوشي", [NutritionCategory.Dinner]), ("مسقعة", [NutritionCategory.Dinner]), ("فطير", [NutritionCategory.Dinner])
    ];

    public static async Task SeedAsync(FoundationDbContext db, Guid ownerId, Guid futureOwnerId, DateTimeOffset now, CancellationToken ct)
    {
        var catalog = new[]
        {
            Catalog("85000000-0000-0000-0000-000000000001", Slice2DemoSeed.FootballId, "كرة تدريب", "/demo-assets/catalog-football.svg", 450m, 0m, 1),
            Catalog("85000000-0000-0000-0000-000000000002", Slice2DemoSeed.FootballId, "حقيبة رياضية", "/demo-assets/catalog-football.svg", 650m, 10m, 2),
            Catalog("85000000-0000-0000-0000-000000000003", Slice2DemoSeed.FootballId, "قميص تدريب", "/demo-assets/catalog-football.svg", 380m, null, 3),
            Catalog("85000000-0000-0000-0000-000000000004", Slice2DemoSeed.FootballId, "زجاجة مياه رياضية", "/demo-assets/catalog-football.svg", 180m, null, 4),
            Catalog("85000000-0000-0000-0000-000000000005", Slice2DemoSeed.SwimmingId, "نظارة سباحة", "/demo-assets/catalog-swimming.svg", 320m, null, 1),
            Catalog("85000000-0000-0000-0000-000000000006", Slice2DemoSeed.SwimmingId, "قبعة سباحة", "/demo-assets/catalog-swimming.svg", 160m, null, 2),
            Catalog("85000000-0000-0000-0000-000000000007", Slice2DemoSeed.SwimmingId, "حقيبة سباحة", "/demo-assets/catalog-swimming.svg", 590m, 5m, 3),
            Catalog("85000000-0000-0000-0000-000000000008", Slice2DemoSeed.SwimmingId, "لوح تدريب", "/demo-assets/catalog-swimming.svg", 270m, null, 4),
            Catalog(InactiveCatalogId.ToString(), Slice2DemoSeed.FootballId, "منتج غير منشور", "/demo-assets/catalog-football.svg", null, null, 99, false)
        };
        foreach (var item in catalog) await Add(db.SportCatalogItems, item, ct);

        for (var index = 0; index < Meals.Length; index++)
        {
            var id = index == 0 ? ShakshukaId : Guid.Parse($"86000000-0000-0000-0000-{index + 1:D12}");
            if (await db.NutritionItems.AnyAsync(x => x.Id == id, ct)) continue;
            var meal = Meals[index];
            var item = new NutritionItem { Id = id, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = meal.Name, ArabicDescription = $"تعريف معلوماتي مختصر عن {meal.Name} ضمن مكتبة العرض، وليس توصية غذائية شخصية.", ImageReference = "/demo-assets/nutrition-meal.svg", ServingDescription = "حصة عرض واحدة — المقدار تجريبي", Calories = 200 + index * 9, ProteinGrams = 8 + index % 5, CarbohydratesGrams = 20 + index % 8, FatGrams = 7 + index % 4, DataStatus = NutritionDataStatus.DemoUnreviewed, SourceDescription = "قيم اصطناعية للديمو وغير صالحة كمرجع طبي أو غذائي.", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
            var order = index + 1;
            foreach (var category in meal.Categories) item.Categories.Add(new NutritionCategoryLink { AcademyId = item.AcademyId, NutritionItemId = item.Id, Category = category, DisplayOrder = order });
            db.NutritionItems.Add(item); await db.SaveChangesAsync(ct);
        }

        await Add(db.PlayerMedicalRecords, new PlayerMedicalRecord { Id = PublishedMedicalId, AcademyId = DemoSeed.NogoomAcademyId, PlayerId = Slice2DemoSeed.OmarPlayerId, RecordType = MedicalRecordType.Consultation, ArabicTitle = "متابعة إجهاد بسيط بعد التدريب", RecordDate = new DateOnly(2026, 9, 20), ArabicDescription = "سجل اصطناعي مخصص لعرض آلية النشر فقط.", Status = MedicalRecordStatus.Resolved, StaffNotes = "ملاحظة داخلية لا تظهر لولي الأمر.", GuardianVisibleNotes = "تمت المتابعة وعاد اللاعب للتدريب المعتاد.", IsPublishedToGuardian = true, CreatedByUserId = ownerId, UpdatedByUserId = ownerId, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await Add(db.PlayerMedicalRecords, new PlayerMedicalRecord { Id = DraftMedicalId, AcademyId = DemoSeed.NogoomAcademyId, PlayerId = Slice2DemoSeed.OmarPlayerId, RecordType = MedicalRecordType.Injury, ArabicTitle = "مسودة داخلية غير منشورة", RecordDate = new DateOnly(2026, 9, 25), ArabicDescription = "لا ينبغي أن تظهر لولي الأمر.", Status = MedicalRecordStatus.Monitoring, StaffNotes = "سري للديمو", IsPublishedToGuardian = false, CreatedByUserId = ownerId, UpdatedByUserId = ownerId, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);

        await Add(db.PlayerMedia, new PlayerMedia { Id = PublishedMediaId, AcademyId = DemoSeed.NogoomAcademyId, PlayerId = Slice2DemoSeed.OmarPlayerId, MediaType = PlayerMediaType.Image, MediaReference = "/demo-assets/gallery-training.svg", ArabicCaption = "لحظة من التدريب — رسم تجريبي", CapturedAtUtc = now.AddDays(-7), DisplayOrder = 1, IsPublishedToGuardian = true, CreatedByUserId = ownerId, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await Add(db.PlayerMedia, new PlayerMedia { Id = Guid.Parse("83000000-0000-0000-0000-000000000002"), AcademyId = DemoSeed.NogoomAcademyId, PlayerId = Slice2DemoSeed.OmarPlayerId, MediaType = PlayerMediaType.Image, MediaReference = "/demo-assets/gallery-medal.svg", ArabicCaption = "إنجاز تدريبي — رسم تجريبي", CapturedAtUtc = now.AddDays(-3), DisplayOrder = 2, IsPublishedToGuardian = true, CreatedByUserId = ownerId, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await Add(db.PlayerMedia, new PlayerMedia { Id = DraftMediaId, AcademyId = DemoSeed.NogoomAcademyId, PlayerId = Slice2DemoSeed.OmarPlayerId, MediaType = PlayerMediaType.Image, MediaReference = "/demo-assets/gallery-training.svg", ArabicCaption = "عنصر غير منشور", DisplayOrder = 3, IsPublishedToGuardian = false, CreatedByUserId = ownerId, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);

        var bSport = await db.Sports.Where(x => x.AcademyId == DemoSeed.FutureAcademyId).Select(x => x.Id).FirstAsync(ct);
        var bPlayer = await db.Players.Where(x => x.AcademyId == DemoSeed.FutureAcademyId).Select(x => x.Id).FirstAsync(ct);
        await Add(db.SportCatalogItems, new SportCatalogItem { Id = Guid.Parse("87000000-0000-0000-0000-000000000001"), AcademyId = DemoSeed.FutureAcademyId, SportId = bSport, ArabicName = "منتج الأكاديمية الثانية", ImageReference = "/demo-assets/catalog-football.svg", DisplayOrder = 1, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await Add(db.PlayerMedicalRecords, new PlayerMedicalRecord { Id = AcademyBMedicalId, AcademyId = DemoSeed.FutureAcademyId, PlayerId = bPlayer, RecordType = MedicalRecordType.Consultation, ArabicTitle = "سجل الأكاديمية الثانية", RecordDate = new DateOnly(2026, 9, 20), ArabicDescription = "للعزل فقط", Status = MedicalRecordStatus.Open, StaffNotes = "داخلي", IsPublishedToGuardian = true, CreatedByUserId = futureOwnerId, UpdatedByUserId = futureOwnerId, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await Add(db.PlayerMedia, new PlayerMedia { Id = AcademyBMediaId, AcademyId = DemoSeed.FutureAcademyId, PlayerId = bPlayer, MediaType = PlayerMediaType.Image, MediaReference = "/demo-assets/gallery-training.svg", ArabicCaption = "وسائط الأكاديمية الثانية", DisplayOrder = 1, IsPublishedToGuardian = true, CreatedByUserId = futureOwnerId, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
    }

    private static SportCatalogItem Catalog(string id, Guid sportId, string name, string image, decimal? price, decimal? discount, int order, bool active = true) => new() { Id = Guid.Parse(id), AcademyId = DemoSeed.NogoomAcademyId, SportId = sportId, ArabicName = name, ArabicDescription = "عنصر كتالوج معلوماتي تجريبي للعرض فقط.", ImageReference = image, DisplayPrice = price, Currency = price.HasValue ? "EGP" : null, DiscountPercentage = discount, DisplayOrder = order, IsActive = active, CreatedAtUtc = DateTimeOffset.Parse("2026-09-28T06:00:00Z"), UpdatedAtUtc = DateTimeOffset.Parse("2026-09-28T06:00:00Z") };
    private static async Task Add<TEntity>(DbSet<TEntity> set, TEntity entity, CancellationToken ct) where TEntity : Academy.Infrastructure.Structure.TenantEntity { if (await set.AnyAsync(x => x.Id == entity.Id, ct)) return; set.Add(entity); await set.GetService<ICurrentDbContext>().Context.SaveChangesAsync(ct); }
}
