using System.Security.Cryptography;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Demo;
using Academy.Api.Platform;
using Academy.Infrastructure.Content;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Help;

// Explicit QA fixture only; does not license learning_hub or replace authored lessons.
public static class HelpVideoDemoSeed
{
    public static readonly Guid Id = Guid.Parse("30c49077-e718-4d5f-894e-98b7a7107001");
    public static async Task Seed(FoundationDbContext db, IHostEnvironment env, DemoOptions options, IConfiguration config)
    {
        if (!env.IsEnvironment("Demo") || !options.SeedEnabled || options.SeedProfile != "Football" ||
            !await db.Academies.AnyAsync(x => x.Id == FootballDemoSeed.AcademyId && x.Slug == FootballDemoSeed.Slug && x.EnglishName == FootballDemoSeed.Marker))
            throw new InvalidOperationException("فيديو العرض الاصطناعي يتطلب Football Demo معلوم الهوية.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"help-video:coach-sessions"}, 0))");
        // Archived fixtures are deliberately not republished on restart; unknown content is never overwritten.
        if (await db.HelpVideoRevisions.AnyAsync(x => x.Id == Id || x.LessonId == "coach-sessions")) return;
        if (!await db.PlatformAdministrators.AnyAsync(x => x.UserId == PlatformIdentity.DemoUserId && x.IsActive)) throw new InvalidOperationException("هوية منصة الديمو غير متاحة.");
        var source = config["Demo:HelpVideoSeedDirectory"];
        if (string.IsNullOrWhiteSpace(source) || !Path.IsPathFullyQualified(source)) throw new InvalidOperationException("حدد مجلد ملفات الفيديو الاصطناعية صراحةً.");
        var video = await File.ReadAllBytesAsync(Path.Combine(source, "synthetic-guide.mp4")); var image = await File.ReadAllBytesAsync(Path.Combine(source, "synthetic-thumbnail.png"));
        if (video.Length > 25_000_000 || image.Length > 2_000_000 || HelpMediaValidation.Mp4Duration(video) != 2 || !HelpMediaValidation.Png(image) ||
            !Convert.ToHexString(SHA256.HashData(video)).Equals("984f04d36760678fc566490928ac915d28abc99fb212978c9759c0a166c30502", StringComparison.OrdinalIgnoreCase) ||
            !Convert.ToHexString(SHA256.HashData(image)).Equals("692ec5e9dba7c22472930c82e64a967f0a9696847cb1e08c83d99117033cfbb7", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("ملفات فيديو العرض الاصطناعية غير صالحة.");
        var root = Path.Combine(Path.GetFullPath(config["Media:PrivateRoot"] ?? Path.Combine(env.ContentRootPath, "local-uploads")), "help"); Directory.CreateDirectory(root);
        var videoName = "demo-help-" + Id.ToString("N") + ".mp4"; var thumbnailName = "demo-help-" + Id.ToString("N") + ".png";
        async Task Copy(string name, byte[] bytes)
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path)) { if (!SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(SHA256.HashData(bytes))) throw new InvalidOperationException("ملف عرض موجود مختلف؛ لن يتم استبداله."); return; }
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); await output.WriteAsync(bytes);
        }
        await Copy(videoName, video); await Copy(thumbnailName, image);
        var now = DateTimeOffset.Parse("2026-09-28T06:00:00Z");
        db.HelpVideoRevisions.Add(new HelpVideoRevision { Id = Id, LessonId = "coach-sessions", Revision = 1, State = "Published", Title = "عرض اصطناعي — تشغيل شرح حصتي", Description = "ألوان اختبار مصطنعة لمدة ثانيتين لاختبار مشغل الفيديو، وليست درسًا تدريبيًا مكتملًا أو تصويرًا للأطفال.", AlternativeText = "اقرأ خطوات دليل حصتي ومجموعتي أدناه. الفيديو مجرد نمط ألوان اصطناعي لفحص التشغيل.", VideoFile = videoName, ThumbnailFile = thumbnailName, DurationSeconds = 2, CreatedBy = PlatformIdentity.DemoUserId, CreatedAtUtc = now, PublishedAtUtc = now });
        db.PlatformAudits.Add(new() { Id = Guid.Parse("30c49077-e718-4d5f-894e-98b7a7107002"), ActorUserId = PlatformIdentity.DemoUserId, Action = "help-video.demo-seed", AtUtc = now, Details = JsonSerializer.Serialize(new { Id, synthetic = true, noLicenseChange = true }) });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
