using Academy.Infrastructure.Content;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
namespace Academy.Api.Slice7;

// Canonical informational catalog reused from the approved existing library.
// No fees, orders, medical records, personalized diagnoses or cross-academy copies.
public static class DefaultNutritionLibrary
{
    private static Guid Id(Guid academy,string key)=>new(SHA256.HashData(Encoding.UTF8.GetBytes($"nutrition-default-v1:{academy}:{key}"))[..16]);
    public static bool IsDefault(Guid academy,Guid id)=>Enumerable.Range(0,18).Any(i=>Id(academy,$"nutrition/main/{i}")==id)||Enumerable.Range(0,6).Any(i=>Id(academy,$"nutrition/snack/{i}")==id);
    public static async Task EnsureAsync(FoundationDbContext db,Guid academy,bool requireReviewed=false)
    {
        var complete=await db.NutritionCategoryLinks.Where(l=>l.AcademyId==academy&&l.NutritionItem.IsActive&&(!requireReviewed||l.NutritionItem.DataStatus==NutritionDataStatus.Reviewed)).Select(l=>l.Category).Distinct().CountAsync();
        if(complete==3)return;
        var (items,links)=Build(academy,DateTimeOffset.UtcNow);
        var ids=items.Select(i=>i.Id).ToArray();
        var existing=await db.NutritionItems.Where(i=>i.AcademyId==academy&&ids.Contains(i.Id)).ToArrayAsync();
        if(existing.Length==items.Length&&existing.All(i=>i.IsActive))return;
        var owns=db.Database.CurrentTransaction is null;
        await using var transaction=owns?await db.Database.BeginTransactionAsync():null;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"nutrition-default:"+academy},0))");
        existing=await db.NutritionItems.Where(i=>i.AcademyId==academy&&ids.Contains(i.Id)).ToArrayAsync();
        var known=existing.Select(i=>i.Id).ToHashSet();
        foreach(var item in existing)item.IsActive=true;
        foreach(var item in items.Where(i=>!known.Contains(i.Id)))db.NutritionItems.Add(item);
        foreach(var link in links.Where(l=>!known.Contains(l.NutritionItemId)))db.NutritionCategoryLinks.Add(link);
        await db.SaveChangesAsync();
        if(transaction is not null)await transaction.CommitAsync();
    }
    private static (NutritionItem[],NutritionCategoryLink[]) Build(Guid academy,DateTimeOffset now)
    {
        var items=new List<NutritionItem>();var links=new List<NutritionCategoryLink>();
        NutritionItem Stage(NutritionItem item,string key){item.Id=Id(academy,key);item.AcademyId=academy;item.CreatedAtUtc=now;item.UpdatedAtUtc=now;items.Add(item);return item;}
        var meals = new (string Name, NutritionCategory Category, string Serving, decimal Weight, decimal Calories, decimal Protein, decimal Carbs, decimal Fat)[]
        {
            ("شوفان بالحليب والموز", NutritionCategory.Breakfast, "شوفان 30 جم + حليب 150 مل + نصف موزة", 250, 280, 10, 48, 6),
            ("بيض وخبز حبوب كاملة وخضار", NutritionCategory.Breakfast, "بيضة + شريحة خبز + خضار", 190, 240, 12, 27, 9),
            ("زبادي وفاكهة وشوفان", NutritionCategory.Breakfast, "زبادي 170 جم + فاكهة 100 جم + شوفان 35 جم", 305, 330, 15, 55, 7),
            ("جبن قريش وخبز حبوب كاملة وخضار", NutritionCategory.Breakfast, "جبن قريش 120 جم + شريحتا خبز + خضار", 330, 390, 28, 46, 10),
            ("فول وخبز وبيضة", NutritionCategory.Breakfast, "فول 180 جم + خبز صغير + بيضة", 390, 520, 25, 71, 16),
            ("زبدة فول سوداني وموز وحليب", NutritionCategory.Breakfast, "شريحتا خبز + ملعقتان زبدة فول سوداني + موزة + حليب", 480, 610, 23, 85, 22),
            ("أرز ودجاج مشوي وخضار", NutritionCategory.Lunch, "أرز 120 جم + دجاج 75 جم + خضار", 330, 390, 31, 51, 7),
            ("مكرونة ولحم قليل الدهن وسلطة", NutritionCategory.Lunch, "مكرونة 140 جم + لحم 70 جم + سلطة", 360, 455, 30, 58, 12),
            ("بطاطس مشوية ودجاج وخضار", NutritionCategory.Lunch, "بطاطس 200 جم + دجاج 110 جم + خضار", 430, 515, 42, 61, 10),
            ("أرز وسمك مشوي وسلطة", NutritionCategory.Lunch, "أرز 180 جم + سمك 120 جم + سلطة", 440, 540, 38, 70, 12),
            ("مكرونة وتونة وخضار", NutritionCategory.Lunch, "مكرونة 230 جم + تونة 120 جم + خضار", 520, 630, 46, 82, 14),
            ("عدس وأرز وزبادي وسلطة", NutritionCategory.Lunch, "عدس 180 جم + أرز 180 جم + زبادي + سلطة", 620, 720, 32, 116, 14),
            ("ساندويتش دجاج وخضار وزبادي", NutritionCategory.Dinner, "ساندويتش صغير + زبادي 100 جم", 280, 340, 25, 42, 8),
            ("بيض وخبز حبوب كاملة وسلطة", NutritionCategory.Dinner, "بيضة + شريحتا خبز + سلطة", 300, 350, 17, 40, 13),
            ("تونة وخبز وخضار", NutritionCategory.Dinner, "تونة 100 جم + شريحتا خبز + خضار", 350, 420, 35, 46, 10),
            ("زبادي وشوفان وفاكهة", NutritionCategory.Dinner, "زبادي 200 جم + شوفان 45 جم + فاكهة", 380, 430, 19, 70, 9),
            ("جبن قريش وخبز وخضار", NutritionCategory.Dinner, "جبن قريش 160 جم + خبز + خضار", 430, 480, 36, 53, 12),
            ("شوربة عدس وخبز وزبادي", NutritionCategory.Dinner, "شوربة 350 مل + خبز + زبادي", 560, 570, 28, 90, 12)
        };
        for (var i = 0; i < meals.Length; i++)
        {
            var meal = meals[i]; var categoryIndex = i % 6; var profileIndex = categoryIndex / 2;
            var item = Stage(Nutrition(meal.Name, meal.Serving, meal.Weight, meal.Calories, meal.Protein, meal.Carbs, meal.Fat,
                $"/demo-assets/nutrition/{meal.Category.ToString().ToLowerInvariant()}-{categoryIndex + 1}.jpg", profileIndex, false, false), $"nutrition/main/{i}");
            links.Add(new NutritionCategoryLink { AcademyId = academy, NutritionItemId = item.Id, Category = meal.Category, DisplayOrder = categoryIndex + 1 });
        }
        var snacks = new (string Name, string Serving, decimal Weight, decimal Calories, decimal Protein, decimal Carbs, decimal Fat, bool Pre)[]
        {
            ("موز وزبادي قبل التدريب", "موزة صغيرة + زبادي 100 جم", 200, 180, 6, 36, 2, true),
            ("حليب وموز بعد التدريب", "حليب 200 مل + موزة صغيرة", 300, 220, 8, 42, 4, false),
            ("توست حبوب كاملة وعسل قبل التدريب", "شريحتا توست + ملعقة عسل", 105, 255, 7, 51, 3, true),
            ("زبادي وفاكهة وشوفان بعد التدريب", "زبادي 170 جم + فاكهة + شوفان 30 جم", 300, 315, 14, 52, 6, false),
            ("فاكهة وزبادي قبل التدريب", "ثمرة فاكهة + زبادي 170 جم", 320, 260, 10, 49, 4, true),
            ("ساندويتش دجاج صغير بعد التدريب", "خبز حبوب كاملة + دجاج 90 جم + خضار", 260, 410, 34, 43, 10, false)
        };
        for (var i = 0; i < snacks.Length; i++)
        {
            var snack = snacks[i]; var profileIndex = i / 2;
            Stage(Nutrition(snack.Name, snack.Serving, snack.Weight, snack.Calories, snack.Protein, snack.Carbs, snack.Fat,
                $"/demo-assets/nutrition/{(snack.Pre ? "breakfast" : "dinner")}-{profileIndex + 1}.jpg", profileIndex, snack.Pre, !snack.Pre), $"nutrition/snack/{i}");
        }
        return (items.ToArray(),links.ToArray());
    }
    private static NutritionItem Nutrition(string name, string serving, decimal weight, decimal calories, decimal protein, decimal carbs, decimal fat, string image, int profileIndex, bool pre, bool post)
    {
        var profiles = new[] { NutritionServingProfile.Small, NutritionServingProfile.Medium, NutritionServingProfile.Large };
        var minimum = new[] { 6, 10, 14 }; var maximum = new[] { 9, 13, 18 };
        return new NutritionItem
        {
            ArabicName = name,
            ArabicDescription = "اقتراح غذائي عام للاعب ناشئ. تُراعى الحساسية وتعليمات الطبيب عند وجود حالة صحية.",
            ImageReference = image, ServingDescription = serving, ServingWeightGrams = weight,
            Calories = calories, ProteinGrams = protein, CarbohydratesGrams = carbs, FatGrams = fat,
            MinimumAge = minimum[profileIndex], MaximumAge = maximum[profileIndex], ServingProfile = profiles[profileIndex],
            SuitableForTrainingDay = true, SuitableForRestDay = !pre && !post, SuitablePreTraining = pre, SuitablePostTraining = post,
            DataStatus = NutritionDataStatus.Reviewed,
            SourceDescription = "تقدير مركب من مكونات الحصة بالاستناد إلى USDA FoodData Central؛ توقيت الوجبات وفق AAP وSports Dietitians Australia.",
            SourceReference = "https://fdc.nal.usda.gov/ | https://www.healthychildren.org/English/healthy-living/nutrition/Pages/Sports-Nutrition-for-Busy-Families-and-Busy-Lifestyles.aspx | https://www.sportsdietitians.com.au/factsheets/children/adolescent-athlete-factsheet/"
        };
    }

}
