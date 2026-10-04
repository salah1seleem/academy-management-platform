using System.Security.Cryptography;
using System.Text;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Api.Slice5;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Content;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Academy.Api.Demo;

// Dedicated synthetic dataset. Legacy multi-sport fixtures are deliberately separate.
public static class FootballDemoSeed
{
    public static readonly Guid AcademyId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    public const string Slug = "football-demo";
    public const string Marker = "Football Demo Dataset v1";
    public const string RenewalCode = "RNW-DEMO-FB-0030-8J7P";
    public static Guid Id(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"football-demo/v1/{key}"))[..16]);
    public static string Phone(int suffix) => $"+2010999{suffix:00000}";

    public static async Task RunAsync(IServiceProvider services, bool reset, CancellationToken ct = default)
    {
        var environment = services.GetRequiredService<IHostEnvironment>();
        var config = services.GetRequiredService<IConfiguration>();
        var options = services.GetRequiredService<IOptions<DemoOptions>>().Value;
        if (!environment.IsEnvironment("Demo") || !options.SeedEnabled || options.SeedProfile != "Football")
            throw new InvalidOperationException("Football seeding/reset requires Demo, SeedEnabled and Football profile.");
        if (string.IsNullOrWhiteSpace(options.StaffPassword)) throw new InvalidOperationException("Demo staff password is required.");
        if (reset && config["Demo:ResetConfirmation"] != "football-demo-only")
            throw new InvalidOperationException("Reset requires explicit Demo:ResetConfirmation=football-demo-only.");
        var db = services.GetRequiredService<FoundationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({Slug}, 938))", ct);
        var academy = await db.Academies.SingleOrDefaultAsync(a => a.Id == AcademyId, ct);
        if (academy is not null && (academy.Slug != Slug || academy.EnglishName != Marker))
            throw new InvalidOperationException("Reserved demo identity has no matching dataset marker. No changes made.");
        if (reset && academy is null) throw new InvalidOperationException("Cannot reset an unmarked/unseeded academy.");
        if (academy is not null && !reset) return; // Never undo manual demo edits during startup.
        if (reset) await ClearMarkedTenant(db, ct);
        var today = services.GetRequiredService<ISubscriptionClock>().Today;
        var now = new DateTimeOffset(today.ToDateTime(new TimeOnly(6, 0)), TimeSpan.Zero);
        if (academy is null)
        {
            db.Academies.Add(new Academy.Infrastructure.Tenancy.Academy { Id = AcademyId, Slug = Slug,
                ArabicName = "أكاديمية نجوم الكرة — عرض تجريبي", EnglishName = Marker, TimeZone = "Africa/Cairo",
                DefaultCurrency = "EGP", CreatedAtUtc = now, UpdatedAtUtc = now });
            await db.SaveChangesAsync(ct);
        }
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        await User(users, db, "owner", "مالك أكاديمية العرض", 10, AcademyRole.AcademyOwner, options.StaffPassword, now, ct);
        await User(users, db, "admin", "إداري أكاديمية العرض", 11, AcademyRole.AcademyAdmin, options.StaffPassword, now, ct);
        string[] coachNames = ["محمود طارق سليمان", "كريم وائل منصور", "حسام شريف عادل", "عمرو أيمن فوزي"];
        for (var i = 0; i < 4; i++) await User(users, db, $"coach{i + 1}", coachNames[i], 20 + i, AcademyRole.Coach, options.StaffPassword, now, ct);
        // 29 guardians: A has one child; B has two. No relationship is inferred from a phone.
        for (var i = 0; i < 29; i++)
        {
            var name = $"ولي أمر تجريبي {i + 1:00}";
            await User(users, db, $"guardian{i + 1}", name, 100 + i, AcademyRole.Guardian, null, now, ct);
            Add(db, new GuardianProfile { DisplayName = name, UserId = Id($"user/guardian{i + 1}"), ContactPhone = Phone(100 + i) }, $"guardian/{i}", now);
        }
        Add(db, new Sport { ArabicName = "كرة القدم", EnglishName = "Football" }, "sport", now);
        string[] branches = ["فرع مدينة نصر", "فرع مدينتي", "فرع الشروق"];
        for (var i = 0; i < 3; i++) Add(db, new Branch { ArabicName = branches[i] }, $"branch/{i}", now);
        for (var i = 0; i < 6; i++)
        {
            var year = 2018 - i;
            Add(db, new AgeCategory { ArabicName = $"مواليد {year}", MinimumBirthYear = year, MaximumBirthYear = year }, $"age/{i}", now);
            Add(db, new TrainingGroup { ArabicName = $"{(i < 2 ? "براعم" : "ناشئين")} {year} — مجموعة أ", BranchId = Id($"branch/{i / 2}"), SportId = Id("sport"), AgeCategoryId = Id($"age/{i}"), Capacity = 20 }, $"group/{i}", now);
            Add(db, new RecurringSchedule { TrainingGroupId = Id($"group/{i}"), DayOfWeek = today.DayOfWeek, StartTime = new TimeOnly(15 + i / 2, 0), EndTime = new TimeOnly(16 + i / 2, 0) }, $"schedule/{i}", now);
            Add(db, new StaffGroupAssignment { TrainingGroupId = Id($"group/{i}"), AcademyMembershipId = Id($"member/coach{i % 4 + 1}") }, $"assignment/{i}", now);
        }
        string[] given = ["آدم", "ياسين", "يوسف", "عمر", "علي", "مالك", "سليم", "زياد", "حمزة", "مروان"];
        string[] families = ["خالد عبد الرحمن", "أحمد إبراهيم", "مصطفى حسن"];
        for (var i = 0; i < 30; i++)
        {
            var group = i / 5; var guardian = i < 2 ? i : i - 1;
            Add(db, new Player { ArabicName = $"{given[i % 10]} {families[i / 10]}", PlayerCode = $"FB-{i + 1:0000}", DateOfBirth = new DateOnly(2018 - group, 3, i % 20 + 1), Gender = Gender.Male,
                HeightCm = 125 + group * 5, WeightKg = 26 + group * 3, PreferredFoot = i % 3 == 0 ? PreferredFoot.Left : PreferredFoot.Right,
                FootballPosition = i % 5 == 0 ? "حارس مرمى" : i % 5 == 1 ? "مدافع" : i % 5 == 2 ? "وسط" : "مهاجم" }, $"player/{i}", now);
            Add(db, new GuardianPlayerLink { PlayerId = Id($"player/{i}"), GuardianId = Id($"guardian/{guardian}"), RelationshipType = "ولي أمر — بيانات خيالية", CreatedByUserId = Id("user/admin") }, $"link/{i}", now);
            Add(db, new SportEnrollment { PlayerId = Id($"player/{i}"), SportId = Id("sport"), BranchId = Id($"branch/{group / 2}"), TrainingGroupId = Id($"group/{group}") }, $"enrollment/{i}", now);
        }
        await db.SaveChangesAsync(ct);
        await History(db, today, now, ct);
        Content(db, today, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static async Task User(UserManager<ApplicationUser> users, FoundationDbContext db, string key, string name, int phoneSuffix, AcademyRole role, string? password, DateTimeOffset now, CancellationToken ct)
    {
        var id = Id($"user/{key}"); var phone = Phone(phoneSuffix); var email = $"{key}.football@example.test";
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            if (await db.Users.AnyAsync(u => u.PhoneNumber == phone || u.NormalizedEmail == users.NormalizeEmail(email), ct))
                throw new InvalidOperationException("Reserved demo identity collision. Existing user was not modified.");
            user = new ApplicationUser { Id = id, UserName = email, Email = email, EmailConfirmed = true, DisplayName = name,
                PhoneNumber = phone, PhoneNumberConfirmed = true, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
            var result = password is null ? await users.CreateAsync(user) : await users.CreateAsync(user, password);
            if (!result.Succeeded) throw new InvalidOperationException("Could not create reserved synthetic identity.");
        }
        else if (user.PhoneNumber != phone || user.Email != email || !user.IsActive)
            throw new InvalidOperationException("Reserved demo identity was edited/disabled. Reset will not overwrite shared identity.");
        db.AcademyMemberships.Add(new AcademyMembership { Id = Id($"member/{key}"), AcademyId = AcademyId, UserId = id, Role = role, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(ct);
    }

    private static T Add<T>(FoundationDbContext db, T row, string key, DateTimeOffset now) where T : TenantEntity
    {
        row.Id = Id(key); row.AcademyId = AcademyId; row.CreatedAtUtc = row.UpdatedAtUtc = now; db.Add(row); return row;
    }

    private static async Task History(FoundationDbContext db, DateOnly today, DateTimeOffset now, CancellationToken ct)
    {
        await Slice3DemoSeed.Plan(db, Id("plan/month"), AcademyId, Id("sport"), "اشتراك شهري", SubscriptionPlanType.Duration, 900m, 30, null, 1, now, ct);
        await Slice3DemoSeed.Plan(db, Id("plan/quarter"), AcademyId, Id("sport"), "اشتراك ثلاثة أشهر", SubscriptionPlanType.Duration, 2500m, 90, null, 2, now, ct);
        await Slice3DemoSeed.Plan(db, Id("plan/year"), AcademyId, Id("sport"), "اشتراك سنوي", SubscriptionPlanType.Duration, 8500m, 365, null, 3, now, ct);
        await Slice3DemoSeed.Plan(db, Id("plan/sessions"), AcademyId, Id("sport"), "باقة 12 حصة", SubscriptionPlanType.Sessions, 1200m, null, 12, 4, now, ct);
        await Slice3DemoSeed.Plan(db, Id("plan/combined"), AcademyId, Id("sport"), "شهر و12 حصة", SubscriptionPlanType.Combined, 1100m, 30, 12, 5, now, ct);
        string[] criterionNames = ["دقة التمرير", "الرؤية", "التمرير تحت الضغط", "التحكم بالكرة", "المراوغة", "اللمسة الأولى", "السرعة", "التسارع", "رد الفعل", "الافتكاك", "التمركز الدفاعي", "المواجهات", "القوة البدنية", "التحمل", "التوازن", "دقة التسديد", "قوة التسديد", "إنهاء الهجمة"];
        FootballAxis[] axes = [FootballAxis.Passing, FootballAxis.Dribbling, FootballAxis.Speed, FootballAxis.Defending, FootballAxis.Physical, FootballAxis.Shooting];
        var criteria = new List<EvaluationCriterion>();
        for (var i = 0; i < criterionNames.Length; i++) criteria.Add(await Slice5DemoSeed.Criterion(db, AcademyId, Id("sport"), criterionNames[i], i + 1, axes[i / 3], now, ct));
        for (var i = 0; i < 30; i++)
        {
            var enrollment = await db.SportEnrollments.SingleAsync(e => e.Id == Id($"enrollment/{i}"), ct);
            var guardian = i < 2 ? i : i - 1; var user = Id($"user/guardian{guardian + 1}");
            var end = today.AddDays(i % 3 == 0 ? 20 : i % 3 == 1 ? 3 : -2);
            // Demo history uses the same financial graph helper, not a second renewal implementation.
            await Slice3DemoSeed.Confirmed(db, Id($"payment/{i}"), enrollment, Id("plan/month"), user, end.AddDays(-29), end, i == 0 ? now : now.AddDays(-i % 20), now, ct);
            var coach = Id($"user/coach{i / 5 % 4 + 1}");
            await Slice5DemoSeed.Evaluation(db, Id($"evaluation/{i}"), enrollment, coach, today.AddDays(-7), "تقييم تدريبي تجريبي", EvaluationStatus.Published, "بيانات خيالية للعرض فقط — تقدم جيد مع التدريب المنتظم.", criteria, criteria.Select((_, c) => (int?)(60 + (i + c * 3) % 35)).ToArray(), now.AddDays(-7), ct);
            if (i == 0) await Slice5DemoSeed.Evaluation(db, Id("evaluation/draft"), enrollment, coach, today, "متابعة غير منشورة", EvaluationStatus.Draft, "مسودة للمدرب فقط", criteria, criteria.Select((_, c) => c < 3 ? (int?)75 : null).ToArray(), now, ct);
            if (i < 2) await Slice3DemoSeed.Incomplete(db, Id($"incomplete/{i}"), enrollment, Id("plan/month"), user, i == 0 ? PaymentRequestStatus.Pending : PaymentRequestStatus.Failed, i == 0 ? RenewalRequestStatus.PaymentInProgress : RenewalRequestStatus.Failed, now, ct);
            if (i == 29) await Slice3DemoSeed.Reference(db, Id("reference"), enrollment, RenewalCode, Id("user/admin"), now, ct);
        }
        for (var group = 0; group < 6; group++)
        for (var week = -2; week <= 1; week++)
        {
            var key = $"session/{group}/{week}"; var coach = Id($"user/coach{group % 4 + 1}");
            Add(db, new TrainingSession { TrainingGroupId = Id($"group/{group}"), BranchId = Id($"branch/{group / 2}"), SportId = Id("sport"), AgeCategoryId = Id($"age/{group}"), SessionDate = today.AddDays(week * 7), StartTime = new TimeOnly(15 + group / 2, 0), EndTime = new TimeOnly(16 + group / 2, 0), RecurringScheduleId = Id($"schedule/{group}"), Source = TrainingSessionSource.RecurringSchedule, Status = week < 0 ? TrainingSessionStatus.Held : TrainingSessionStatus.Scheduled, CreatedByUserId = Id("user/admin") }, key, now);
            if (week > 0) continue;
            Add(db, new StaffAttendance { TrainingSessionId = Id(key), TrainingGroupId = Id($"group/{group}"), AcademyMembershipId = Id($"member/coach{group % 4 + 1}"), Status = AttendanceStatus.Present, RecordedByUserId = coach }, $"staff/{key}", now);
            for (var j = 0; j < 5; j++) Add(db, new PlayerAttendance { TrainingSessionId = Id(key), TrainingGroupId = Id($"group/{group}"), SportEnrollmentId = Id($"enrollment/{group * 5 + j}"), Status = j == 4 ? AttendanceStatus.Absent : week == 0 && j == 3 ? AttendanceStatus.NotRecorded : AttendanceStatus.Present, RecordedByUserId = coach }, $"attendance/{key}/{j}", now);
        }
    }

    private static void Content(FoundationDbContext db, DateOnly today, DateTimeOffset now)
    {
        Add(db, new SportCatalogItem { SportId = Id("sport"), ArabicName = "طقم تدريب كرة القدم", ArabicDescription = "كتالوج عرض فقط — الشراء غير متاح", ImageReference = "/demo-assets/catalog-football.svg", DisplayOrder = 1 }, "catalog", now);
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
            var item = Add(db, Nutrition(meal.Name, meal.Serving, meal.Weight, meal.Calories, meal.Protein, meal.Carbs, meal.Fat,
                $"/demo-assets/nutrition/{meal.Category.ToString().ToLowerInvariant()}-{categoryIndex + 1}.jpg", profileIndex, false, false), $"nutrition/main/{i}", now);
            db.NutritionCategoryLinks.Add(new NutritionCategoryLink { AcademyId = AcademyId, NutritionItemId = item.Id, Category = meal.Category, DisplayOrder = categoryIndex + 1 });
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
            Add(db, Nutrition(snack.Name, snack.Serving, snack.Weight, snack.Calories, snack.Protein, snack.Carbs, snack.Fat,
                $"/demo-assets/nutrition/{(snack.Pre ? "breakfast" : "dinner")}-{profileIndex + 1}.jpg", profileIndex, snack.Pre, !snack.Pre), $"nutrition/snack/{i}", now);
        }
        Add(db, new PlayerMedicalRecord { PlayerId = Id("player/0"), RecordType = MedicalRecordType.Consultation, ArabicTitle = "متابعة تجريبية خيالية", RecordDate = today.AddDays(-7), ArabicDescription = "سجل اصطناعي لتوضيح شاشة المتابعة، لا يخص طفلاً حقيقياً.", Status = MedicalRecordStatus.Resolved, StaffNotes = "ملاحظة داخلية لا تُرسل لولي الأمر", GuardianVisibleNotes = "تمت المتابعة — مثال عرض فقط", IsPublishedToGuardian = true, CreatedByUserId = Id("user/admin"), UpdatedByUserId = Id("user/admin") }, "medical", now);
        Add(db, new PlayerMedia { PlayerId = Id("player/0"), MediaType = PlayerMediaType.Image, MediaReference = "/demo-assets/gallery-training.svg", ArabicCaption = "رسم تدريب توضيحي، ليس صورة طفل حقيقي", IsPublishedToGuardian = true, CreatedByUserId = Id("user/admin"), CapturedAtUtc = now }, "media", now);
    }

    private static NutritionItem Nutrition(string name, string serving, decimal weight, decimal calories, decimal protein, decimal carbs, decimal fat, string image, int profileIndex, bool pre, bool post)
    {
        var profiles = new[] { NutritionServingProfile.Small, NutritionServingProfile.Medium, NutritionServingProfile.Large };
        var minimum = new[] { 6, 10, 14 }; var maximum = new[] { 9, 13, 18 };
        return new NutritionItem
        {
            ArabicName = name,
            ArabicDescription = "اقتراح غذائي عام للاعب كرة قدم ناشئ. تُراعى الحساسية وتعليمات الطبيب عند وجود حالة صحية.",
            ImageReference = image, ServingDescription = serving, ServingWeightGrams = weight,
            Calories = calories, ProteinGrams = protein, CarbohydratesGrams = carbs, FatGrams = fat,
            MinimumAge = minimum[profileIndex], MaximumAge = maximum[profileIndex], ServingProfile = profiles[profileIndex],
            SuitableForTrainingDay = true, SuitableForRestDay = !pre && !post, SuitablePreTraining = pre, SuitablePostTraining = post,
            DataStatus = NutritionDataStatus.Reviewed,
            SourceDescription = "تقدير مركب من مكونات الحصة بالاستناد إلى USDA FoodData Central؛ توقيت الوجبات وفق AAP وSports Dietitians Australia.",
            SourceReference = "https://fdc.nal.usda.gov/ | https://www.healthychildren.org/English/healthy-living/nutrition/Pages/Sports-Nutrition-for-Busy-Families-and-Busy-Lifestyles.aspx | https://www.sportsdietitians.com.au/factsheets/children/adolescent-athlete-factsheet/"
        };
    }

    private static async Task ClearMarkedTenant(FoundationDbContext db, CancellationToken ct)
    {
        // The caller verified environment, fixed ID, slug, marker and explicit confirmation inside this transaction.
        await db.UserSessions.Where(s => s.ActiveAcademyId == AcademyId).ExecuteDeleteAsync(ct);
        await db.MobileSessions.Where(s => s.MembershipId != null && db.AcademyMemberships.Any(m => m.Id == s.MembershipId && m.AcademyId == AcademyId)).ExecuteDeleteAsync(ct);
        // Child-first FK order, including future tenant tables. Never delete global users or Academy itself.
        var pending = db.Model.GetEntityTypes().Where(e => e.FindProperty("AcademyId") is not null).ToHashSet();
        while (pending.Count > 0)
        {
            var leaves = pending.Where(e => !pending.Any(other => other != e && other.GetForeignKeys().Any(fk => fk.PrincipalEntityType == e))).ToArray();
            if (leaves.Length == 0) throw new InvalidOperationException("Tenant FK cycle: reset rolled back; schema needs explicit review.");
            foreach (var entity in leaves)
            {
                // Table names originate solely in the compiled EF model, never CLI/user input.
                var table = entity.GetTableName()!.Replace("\"", "\"\"");
                var sql = $"DELETE FROM \"{table}\" WHERE \"AcademyId\" = {{0}}";
                await db.Database.ExecuteSqlRawAsync(sql, [AcademyId], ct);
                pending.Remove(entity);
            }
        }
        db.ChangeTracker.Clear();
    }
}
