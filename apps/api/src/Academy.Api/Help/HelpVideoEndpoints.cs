using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Platform;
using Academy.Infrastructure.Content;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Help;

public static class HelpVideoEndpoints
{
    public static void MapHelpVideos(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/platform/help-videos").RequireAuthorization(PlatformIdentity.Policy);
        g.MapGet("/", async (FoundationDbContext db, HttpResponse response) => {
            response.Headers.CacheControl = "private, no-store";
            return Results.Ok(new { lessons = HelpCatalog.All.Select(l => new { l.Id, l.Title, l.Feature, sport = l.Sport?.ToString(), roles = l.Roles.Select(r => r.ToString()) }), revisions = (await db.HelpVideoRevisions.AsNoTracking().OrderBy(x => x.LessonId).ThenByDescending(x => x.Revision).ToListAsync()).Select(ManageView) });
        });
        g.MapPost("/", Create).AddEndpointFilter<CsrfFilter>();
        g.MapPut("/{id:guid}", Edit).AddEndpointFilter<CsrfFilter>();
        g.MapPost("/{id:guid}/upload/{kind}", Upload).AddEndpointFilter<CsrfFilter>();
        g.MapPost("/{id:guid}/transition", Transition).AddEndpointFilter<CsrfFilter>();
        g.MapGet("/{id:guid}/preview/{kind}", async (Guid id, string kind, FoundationDbContext db, IConfiguration config, IHostEnvironment env, HttpResponse response) => {
            var row = await db.HelpVideoRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            return row == null ? Results.NotFound() : FileResult(row, kind, config, env, response);
        });
        app.MapGet("/api/v1/help/lessons/{lesson}/media/{id:guid}/{kind}", Read)
            .RequireAuthorization(AcademyPermissions.TenantAccess);
    }
    internal static object? PublicView(HelpVideoRevision? x) => x == null ? null : new { x.Id, x.Title, x.Description, x.AlternativeText, version = x.Revision, x.DurationSeconds, url = PublicPath(x, "video"), thumbnailUrl = PublicPath(x, "thumbnail") };
    private static string PublicPath(HelpVideoRevision x, string kind) => $"/api/v1/help/lessons/{x.LessonId}/media/{x.Id}/{kind}";
    private static object ManageView(HelpVideoRevision x) => new { x.Id, x.LessonId, x.Revision, x.EditVersion, x.State, x.Title, x.Description, x.AlternativeText, x.DurationSeconds, hasVideo = x.VideoFile != null, hasThumbnail = x.ThumbnailFile != null, x.PublishedAtUtc };
    private static string Root(IConfiguration config, IHostEnvironment env) => Path.Combine(Path.GetFullPath(config["Media:PrivateRoot"] ?? Path.Combine(env.ContentRootPath, "local-uploads")), "help");
    private static void Audit(FoundationDbContext db, HttpContext h, HelpVideoRevision row, string action) => db.PlatformAudits.Add(new PlatformAudit { Id = Guid.NewGuid(), ActorUserId = Guid.Parse(h.User.FindFirstValue(ClaimTypes.NameIdentifier)!), Action = "help-video." + action, AtUtc = DateTimeOffset.UtcNow, Details = JsonSerializer.Serialize(new { row.Id, row.LessonId, row.Revision, row.EditVersion, row.State }) });
    private static Task Lock(FoundationDbContext db, string lesson) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"help-video:" + lesson}, 0))");
    private static bool Valid(HelpVideoMetadata? b) => b != null && !string.IsNullOrWhiteSpace(b.Title) && b.Title.Length <= 160 && !string.IsNullOrWhiteSpace(b.Description) && b.Description.Length <= 1600 && !string.IsNullOrWhiteSpace(b.AlternativeText) && b.AlternativeText.Length <= 300;
    private static IResult Invalid(string message) => Results.UnprocessableEntity(new { message });
    private static IResult Conflict() => Results.Conflict(new { message = "تغير هذا الإصدار أو لم يعد مسودة. حدّث القائمة ثم حاول مجددًا." });
    private static void Metadata(HelpVideoRevision row, HelpVideoMetadata b) { row.Title = b.Title.Trim(); row.Description = b.Description.Trim(); row.AlternativeText = b.AlternativeText.Trim(); }
    private static async Task<IResult> Create(HelpVideoCreate b, FoundationDbContext db, HttpContext h)
    {
        if (!Valid(b.Metadata) || !HelpCatalog.All.Any(x => x.Id == b.LessonId)) return Invalid("اختر دليلًا حاليًا وأدخل العنوان والوصف والبديل النصي ضمن الحدود.");
        await using var tx = await db.Database.BeginTransactionAsync(); await Lock(db, b.LessonId);
        if (await db.HelpVideoRevisions.AnyAsync(x => x.LessonId == b.LessonId && x.State == "Draft")) return Conflict();
        var row = new HelpVideoRevision { Id = Guid.NewGuid(), LessonId = b.LessonId, Revision = (await db.HelpVideoRevisions.Where(x => x.LessonId == b.LessonId).MaxAsync(x => (int?)x.Revision) ?? 0) + 1, Title = "", Description = "", AlternativeText = "", CreatedBy = Guid.Parse(h.User.FindFirstValue(ClaimTypes.NameIdentifier)!), CreatedAtUtc = DateTimeOffset.UtcNow };
        Metadata(row, b.Metadata); db.HelpVideoRevisions.Add(row); Audit(db, h, row, "draft"); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(ManageView(row));
    }
    private static async Task<IResult> Edit(Guid id, HelpVideoEdit b, FoundationDbContext db, HttpContext h)
    {
        if (!Valid(b.Metadata)) return Invalid("أدخل العنوان والوصف والبديل النصي ضمن الحدود.");
        var lesson = await db.HelpVideoRevisions.Where(x => x.Id == id).Select(x => x.LessonId).SingleOrDefaultAsync(); if (lesson == null) return Results.NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(); await Lock(db, lesson);
        var row = await db.HelpVideoRevisions.SingleAsync(x => x.Id == id); if (row.State != "Draft" || row.EditVersion != b.Version) return Conflict();
        Metadata(row, b.Metadata); row.EditVersion++; Audit(db, h, row, "edit"); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(ManageView(row));
    }
    private static async Task<IResult> Upload(Guid id, string kind, HttpContext h, FoundationDbContext db, IConfiguration config, IHostEnvironment env)
    {
        if (kind is not ("video" or "thumbnail") || !h.Request.HasFormContentType) return Invalid("اختر ملف فيديو MP4 أو صورة مصغرة PNG.");
        if (h.Request.ContentLength is null or > 27_000_000) return Invalid("حجم الملف أكبر من الحد المسموح.");
        var form = await h.Request.ReadFormAsync(); if (form.Files.Count != 1 || !int.TryParse(form["version"], out var version)) return Invalid("اختر ملفًا واحدًا وحدّث الإصدار.");
        var file = form.Files[0]; if (file.Length < 12 || file.Length > (kind == "video" ? 25_000_000 : 2_000_000)) return Invalid("الحد الأقصى للفيديو 25 ميجابايت وللصورة 2 ميجابايت.");
        var lesson = await db.HelpVideoRevisions.Where(x => x.Id == id).Select(x => x.LessonId).SingleOrDefaultAsync(); if (lesson == null) return Results.NotFound();
        using var memory = new MemoryStream(); await file.CopyToAsync(memory); var bytes = memory.ToArray(); var duration = kind == "video" ? HelpMediaValidation.Mp4Duration(bytes) : null;
        if (kind == "video" ? duration == null : !HelpMediaValidation.Png(bytes)) return Invalid("الملف غير صالح. استخدم MP4 بترميز H.264 حتى 30 دقيقة، أو صورة PNG حتى 1920 بكسل.");
        await using var tx = await db.Database.BeginTransactionAsync(); await Lock(db, lesson);
        var row = await db.HelpVideoRevisions.SingleAsync(x => x.Id == id); if (row.State != "Draft" || row.EditVersion != version) return Conflict();
        var root = Root(config, env); Directory.CreateDirectory(root); var name = Guid.NewGuid().ToString("N") + (kind == "video" ? ".mp4" : ".png"); var path = Path.Combine(root, name); var old = kind == "video" ? row.VideoFile : row.ThumbnailFile;
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await output.WriteAsync(bytes);
            if (kind == "video") { row.VideoFile = name; row.DurationSeconds = duration; } else row.ThumbnailFile = name;
            row.EditVersion++; Audit(db, h, row, "upload-" + kind); await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
        // Only the replaced file belonging to this draft, never shared tenant media.
        if (old != null) { try { File.Delete(Path.Combine(root, old)); } catch (IOException) { /* Unreferenced private file may be collected later. */ } }
        return Results.Ok(ManageView(row));
    }
    private static async Task<IResult> Transition(Guid id, HelpVideoTransition b, FoundationDbContext db, HttpContext h, IConfiguration config, IHostEnvironment env)
    {
        if (b.Target is not ("Published" or "Archived")) return Invalid("اختر نشرًا أو أرشفة.");
        var lesson = await db.HelpVideoRevisions.Where(x => x.Id == id).Select(x => x.LessonId).SingleOrDefaultAsync(); if (lesson == null) return Results.NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(); await Lock(db, lesson);
        var row = await db.HelpVideoRevisions.SingleAsync(x => x.Id == id); if (row.EditVersion != b.Version || row.State == "Archived" || b.Target == "Published" && row.State != "Draft") return Conflict();
        if (b.Target == "Published")
        {
            if (row.VideoFile == null || row.ThumbnailFile == null || row.DurationSeconds is null or <= 0 || !File.Exists(Path.Combine(Root(config, env), row.VideoFile)) || !File.Exists(Path.Combine(Root(config, env), row.ThumbnailFile))) return Invalid("أكمل رفع الفيديو والصورة المصغرة قبل النشر.");
            foreach (var previous in await db.HelpVideoRevisions.Where(x => x.LessonId == lesson && x.State == "Published").ToListAsync()) { previous.State = "Archived"; previous.EditVersion++; Audit(db, h, previous, "replaced"); }
            await db.SaveChangesAsync(); row.PublishedAtUtc = DateTimeOffset.UtcNow;
        }
        row.State = b.Target; row.EditVersion++; Audit(db, h, row, b.Target == "Published" ? "publish" : "archive"); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(ManageView(row));
    }
    private static async Task<IResult> Read(string lesson, Guid id, string kind, CurrentTenant tenant, FoundationDbContext db, AcademyConfigurationService config, IAuthorizationService authorization, HttpContext h, IConfiguration settings, IHostEnvironment env)
    {
        h.Response.Headers.CacheControl = "private, no-store";
        if (!(await HelpEndpoints.Allowed(tenant, db, config, authorization, h)).Any(l => l.Id == lesson)) return Results.NotFound();
        var row = await db.HelpVideoRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.LessonId == lesson && x.State == "Published");
        return row == null ? Results.NotFound() : FileResult(row, kind, settings, env, h.Response);
    }
    private static IResult FileResult(HelpVideoRevision row, string kind, IConfiguration config, IHostEnvironment env, HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store"; response.Headers.XContentTypeOptions = "nosniff";
        var name = kind == "video" ? row.VideoFile : kind == "thumbnail" ? row.ThumbnailFile : null;
        if (name == null || Path.GetFileName(name) != name) return Results.NotFound(); var path = Path.Combine(Root(config, env), name);
        return File.Exists(path) ? Results.File(path, kind == "video" ? "video/mp4" : "image/png", enableRangeProcessing: true) : Results.NotFound();
    }
}
public sealed record HelpVideoMetadata(string Title, string Description, string AlternativeText);
public sealed record HelpVideoCreate(string LessonId, HelpVideoMetadata Metadata);
public sealed record HelpVideoEdit(int Version, HelpVideoMetadata Metadata);
public sealed record HelpVideoTransition(int Version, string Target);
