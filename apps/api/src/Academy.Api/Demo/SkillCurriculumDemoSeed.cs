using Academy.Api.Auth;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Demo;
public static class SkillCurriculumDemoSeed
{
    public static async Task Seed(FoundationDbContext db, IHostEnvironment env, DemoOptions options)
    {
        var academy = FootballDemoSeed.AcademyId;
        if (!env.IsEnvironment("Demo") || !options.SeedEnabled || options.SeedProfile != "Football" || !await db.Academies.AnyAsync(a => a.Id == academy && a.Slug == FootballDemoSeed.Slug && a.EnglishName == FootballDemoSeed.Marker)) throw new InvalidOperationException("منهج المهارات الاصطناعي يتطلب Football Demo معلوم الهوية.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"football-skill-curriculum-demo"},0))");
        var id = FootballDemoSeed.Id("skill-curriculum/1"); if (await db.SkillCurricula.AnyAsync(c => c.Id == id)) return;
        var sport = await db.SportEnrollments.Where(e => e.AcademyId == academy && e.PlayerId == FootballDemoSeed.Id("player/10")).Select(e => e.SportId).SingleAsync();
        var levelId = FootballDemoSeed.Id("skill-curriculum/level"); var now = DateTimeOffset.Parse("2026-09-28T06:00:00Z"); var owner = FootballDemoSeed.Id("user/owner");
        if (!await db.TrainingLevels.AnyAsync(l => l.Id == levelId)) db.TrainingLevels.Add(new() { Id = levelId, AcademyId = academy, SportId = sport, ArabicName = "أساسيات الكرة — مستوى اصطناعي", Rank = 10, CreatedAtUtc = now, UpdatedAtUtc = now });
        var c = new SkillCurriculum { Id = id, AcademyId = academy, TrainingLevelId = levelId, SportId = sport, RevisionNumber = 1, Title = "خطوات الكرة — منهج عرض اصطناعي", LevelNameSnapshot = "أساسيات الكرة — مستوى اصطناعي", CreatedByUserId = owner, CreationRequestId = id, CreationHash = "synthetic-seed", CreatedAtUtc = now, UpdatedAtUtc = now };
        db.SkillCurricula.Add(c);
        db.SkillRequirements.AddRange(new SkillRequirement { Id = FootballDemoSeed.Id("skill-curriculum/requirement/0"), AcademyId = academy, CurriculumId = id, SportId = sport, Name = "التحكم بالكرة", Unit = "Checklist", Protocol = "مراجعة فنية اصطناعية للتحكم ضمن تمرين الفريق؛ يحدد المدرب الإتقان يدويًا.", DisplayOrder = 0, CreatedAtUtc = now, UpdatedAtUtc = now },
            new SkillRequirement { Id = FootballDemoSeed.Id("skill-curriculum/requirement/1"), AcademyId = academy, CurriculumId = id, SportId = sport, Name = "تمريرات ناجحة من عشر محاولات", Unit = "Count", Protocol = "يسجل المدرب عدد التمريرات الناجحة من عشر محاولات في نفس التمرين الاصطناعي، ويقرر الإتقان فنيًا دون عتبة آلية.", DisplayOrder = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); c.State = "Published"; c.PublishedAtUtc = now; c.PublishedByUserId = owner; c.PublishRequestId = id; c.EditRevision++; await db.SaveChangesAsync(); await tx.CommitAsync();
        // Do not assign a player or alter a reviewed level from startup data.
    }
}
