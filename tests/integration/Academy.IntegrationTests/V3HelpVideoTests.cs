using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Help;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact]
    public async Task Help_video_database_rejects_published_revision_without_measured_duration()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var invalid = new Academy.Infrastructure.Content.HelpVideoRevision { Id = Guid.NewGuid(), LessonId = "invalid-published-fixture", Revision = 1, State = "Published", Title = "اختبار قيد النشر", Description = "سجل اصطناعي غير صالح لا يجب حفظه.", AlternativeText = "اختبار قاعدة البيانات", VideoFile = "unpublished.mp4", ThumbnailFile = "unpublished.png", DurationSeconds = null, CreatedBy = Academy.Api.Platform.PlatformIdentity.DemoUserId, CreatedAtUtc = DateTimeOffset.UtcNow, PublishedAtUtc = DateTimeOffset.UtcNow };
        db.HelpVideoRevisions.Add(invalid); var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23514", Assert.IsType<Npgsql.PostgresException>(error.InnerException).SqlState); db.Entry(invalid).State = EntityState.Detached;
        Assert.False(await db.HelpVideoRevisions.AnyAsync(x => x.Id == invalid.Id));
    }
    [Fact]
    public async Task Help_video_explicit_synthetic_seed_is_idempotent_preserves_archive_and_rejects_production()
    {
        await using var demo = factory.WithWebHostBuilder(b => b.UseSetting("Demo:SeedProfile", "Football")); using var client = demo.CreateClient();
        await using var scope = demo.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>(); var options = scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:HelpVideoSeedDirectory"] = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Help"), ["Media:PrivateRoot"] = Path.Combine(Path.GetTempPath(), "academity-help-video-seed-20261017") }).Build();
        var before = await db.Collections.CountAsync();
        await HelpVideoDemoSeed.Seed(db, env, options, config); var first = await db.HelpVideoRevisions.AsNoTracking().SingleAsync(x => x.Id == HelpVideoDemoSeed.Id); var snapshot = JsonSerializer.Serialize(first);
        await HelpVideoDemoSeed.Seed(db, env, options, config); Assert.Equal(snapshot, JsonSerializer.Serialize(await db.HelpVideoRevisions.AsNoTracking().SingleAsync(x => x.Id == first.Id)));
        Assert.Equal(2, first.DurationSeconds); Assert.Contains("اصطناعي", first.Title);
        Assert.Equal(before, await db.Collections.CountAsync()); Assert.Equal(1, await db.PlatformAudits.CountAsync(x => x.Action == "help-video.demo-seed"));
        // No production access occurs: the environment guard rejects before querying/writing.
        var prod = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Production", ContentRootPath = env.ContentRootPath };
        await Assert.ThrowsAsync<InvalidOperationException>(() => HelpVideoDemoSeed.Seed(db, prod, options, config));
        if (first.State == "Published") { var row = await db.HelpVideoRevisions.SingleAsync(x => x.Id == first.Id); row.State = "Archived"; row.EditVersion++; await db.SaveChangesAsync(); }
        await HelpVideoDemoSeed.Seed(db, env, options, config); Assert.Equal("Archived", (await db.HelpVideoRevisions.AsNoTracking().SingleAsync(x => x.Id == first.Id)).State);
    }
    private static readonly object HelpVideoMetadata = new { title = "شرح تجريبي اصطناعي — تجديد الأسرة", description = "فيديو ألوان اختبار مصطنع للتحقق من دورة النشر، وليس درسًا إنتاجيًا.", alternativeText = "ألوان متحركة لا تتضمن أطفالًا أو بيانات فعلية. اتبع خطوات الدليل المكتوب للتجديد." };
    private static async Task<HttpResponseMessage> VideoWrite(HttpClient client, string path, object body, bool put = false)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf");
        var request = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); return await client.SendAsync(request);
    }
    private static async Task<JsonElement> VideoOk(Task<HttpResponseMessage> task) { var response = await task; var text = await response.Content.ReadAsStringAsync(); Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}"); return JsonDocument.Parse(text).RootElement.Clone(); }
    private static async Task<HttpResponseMessage> VideoUpload(HttpClient client, JsonElement row, string kind, byte[]? bytes = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf"); using var form = new MultipartFormDataContent();
        var file = kind == "video" ? "synthetic-guide.mp4" : "synthetic-thumbnail.png";
        form.Add(new StringContent(row.GetProperty("editVersion").ToString()), "version"); form.Add(new ByteArrayContent(bytes ?? await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Help", file))), "file", file);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/platform/help-videos/{row.GetProperty("id").GetGuid()}/upload/{kind}") { Content = form }; request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); return await client.SendAsync(request);
    }
    private static string VideoRoute(JsonElement row) => $"/api/v1/platform/help-videos/{row.GetProperty("id").GetGuid()}";
    private static Task<JsonElement> VideoTransition(HttpClient platform, JsonElement row, string target) => VideoOk(VideoWrite(platform, VideoRoute(row) + "/transition", new { version = row.GetProperty("editVersion").GetInt32(), target }));
    [Fact]
    public async Task Help_video_platform_mfa_csrf_validation_private_ranges_versions_and_archive() => await HelpFixture(async (owner, platform) =>
    {
        using var guardian = await Mobile(AcademyRole.Guardian); using var coach = await Mobile(AcademyRole.Coach); using var other = await Staff(DemoSeed.FutureOwnerEmail); using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/platform/help-videos/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/v1/platform/help-videos/")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await platform.PostAsJsonAsync("/api/v1/platform/help-videos/", new { lessonId = "family-renewal", metadata = HelpVideoMetadata })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await VideoWrite(platform, "/api/v1/platform/help-videos/", new { lessonId = "unknown", metadata = HelpVideoMetadata })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await VideoWrite(platform, "/api/v1/platform/help-videos/", new { lessonId = "family-renewal", metadata = (object?)null })).StatusCode);
        var report = await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28");
        var row = await VideoOk(VideoWrite(platform, "/api/v1/platform/help-videos/", new { lessonId = "family-renewal", metadata = HelpVideoMetadata }));
        try
        {
            Assert.Equal(HttpStatusCode.Conflict, (await VideoWrite(platform, "/api/v1/platform/help-videos/", new { lessonId = "family-renewal", metadata = HelpVideoMetadata })).StatusCode);
            var path = $"/api/v1/help/lessons/family-renewal/media/{row.GetProperty("id").GetGuid()}/video";
            Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync(path)).StatusCode);
            Assert.Equal("AwaitingUpload", (await guardian.GetFromJsonAsync<JsonElement>("/api/v1/help/lessons/family-renewal")).GetProperty("videoStatus").GetString());
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await VideoWrite(platform, VideoRoute(row) + "/transition", new { version = row.GetProperty("editVersion").GetInt32(), target = "Published" })).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await VideoUpload(platform, row, "video", new byte[100])).StatusCode);
            row = await VideoOk(VideoUpload(platform, row, "video")); Assert.Equal(2, row.GetProperty("durationSeconds").GetInt32());
            row = await VideoOk(VideoUpload(platform, row, "thumbnail"));
            var preview = await platform.GetAsync(VideoRoute(row) + "/preview/video"); preview.EnsureSuccessStatusCode(); Assert.Equal("video/mp4", preview.Content.Headers.ContentType!.MediaType);
            var previous = row; row = await VideoTransition(platform, row, "Published");
            Assert.Equal(HttpStatusCode.Conflict, (await VideoWrite(platform, VideoRoute(previous), new { version = previous.GetProperty("editVersion").GetInt32(), metadata = HelpVideoMetadata }, true)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await VideoUpload(platform, row, "video")).StatusCode);
            var detail = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/help/lessons/family-renewal"); Assert.Equal("Published", detail.GetProperty("videoStatus").GetString()); Assert.Equal(path, detail.GetProperty("videoUrl").GetString());
            Assert.Equal("Published", (await HelpRead(guardian)).GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetString() == "family-renewal").GetProperty("videoStatus").GetString());
            using var request = new HttpRequestMessage(HttpMethod.Get, path); request.Headers.Range = new RangeHeaderValue(0, 31);
            var range = await guardian.SendAsync(request); Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode); Assert.Equal(32, (await range.Content.ReadAsByteArrayAsync()).Length); Assert.True(range.Headers.CacheControl!.Private); Assert.True(range.Headers.CacheControl.NoStore); Assert.Equal("nosniff", range.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(path + "?academyId=" + DemoSeed.NogoomAcademyId)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await coach.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync(path.Replace("family-renewal", "owner-dashboard"))).StatusCode);
            await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var persisted = await db.HelpVideoRevisions.SingleAsync(x => x.Id == row.GetProperty("id").GetGuid()); persisted.Title = "forbidden rewrite";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.Entry(persisted).State = EntityState.Unchanged;
            var config = await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/configuration/"); var settings = config.GetProperty("versions").EnumerateArray().Single(x => x.GetProperty("state").GetString() == "Active").GetProperty("settings");
            var policy = System.Text.Json.Nodes.JsonNode.Parse(settings.GetRawText())!; policy["helpAudiences"] = System.Text.Json.Nodes.JsonNode.Parse("{\"family-renewal\":[]}"); await HelpActivate(owner, JsonSerializer.SerializeToElement(policy));
            Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync(path)).StatusCode); await HelpActivate(owner, settings);
            var license = await db.AcademyFeatureEntitlements.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Feature == "learning_hub");
            try { license.Enabled = false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync(path)).StatusCode); }
            finally { license.Enabled = true; await db.SaveChangesAsync(); }
            row = await VideoTransition(platform, row, "Archived"); Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync(path)).StatusCode);
            Assert.Equal("AwaitingUpload", (await guardian.GetFromJsonAsync<JsonElement>("/api/v1/help/lessons/family-renewal")).GetProperty("videoStatus").GetString());
            Assert.Equal(report, await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
            var audits = await db.PlatformAudits.Where(x => x.Action == "help-video.publish").Select(x => x.Details).ToListAsync();
            Assert.Contains(audits, details => JsonDocument.Parse(details).RootElement.GetProperty("Id").GetGuid() == row.GetProperty("id").GetGuid());
        }
        finally { if (row.GetProperty("state").GetString() != "Archived") await VideoTransition(platform, row, "Archived"); }
    });
    [Fact]
    public async Task Help_video_new_version_replaces_published_atomically_and_stale_or_duplicate_actions_conflict() => await HelpFixture(async (_, platform) =>
    {
        using var guardian = await Mobile(AcademyRole.Guardian);
        async Task<JsonElement> Draft() { var r = await VideoOk(VideoWrite(platform, "/api/v1/platform/help-videos/", new { lessonId = "family-progress", metadata = HelpVideoMetadata })); r = await VideoOk(VideoUpload(platform, r, "video")); return await VideoOk(VideoUpload(platform, r, "thumbnail")); }
        var first = await Draft(); JsonElement second = default;
        try
        {
            first = await VideoTransition(platform, first, "Published"); var firstPath = (await guardian.GetFromJsonAsync<JsonElement>("/api/v1/help/lessons/family-progress")).GetProperty("videoUrl").GetString()!;
            second = await Draft(); Assert.True(second.GetProperty("revision").GetInt32() > first.GetProperty("revision").GetInt32());
            Assert.Equal(HttpStatusCode.OK, (await guardian.GetAsync(firstPath)).StatusCode);
            var stale = second; second = await VideoOk(VideoWrite(platform, VideoRoute(second), new { version = second.GetProperty("editVersion").GetInt32(), metadata = HelpVideoMetadata }, true));
            Assert.Equal(HttpStatusCode.Conflict, (await VideoWrite(platform, VideoRoute(stale) + "/transition", new { version = stale.GetProperty("editVersion").GetInt32(), target = "Published" })).StatusCode);
            second = await VideoTransition(platform, second, "Published"); Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync(firstPath)).StatusCode);
            var lesson = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/help/lessons/family-progress"); Assert.Equal(second.GetProperty("id").GetGuid(), lesson.GetProperty("video").GetProperty("id").GetGuid());
            var rows = (await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/help-videos/")).GetProperty("revisions"); first = rows.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == first.GetProperty("id").GetGuid()).Clone(); Assert.Equal("Archived", first.GetProperty("state").GetString());
        }
        finally { if (second.ValueKind != JsonValueKind.Undefined && second.GetProperty("state").GetString() != "Archived") await VideoTransition(platform, second, "Archived"); if (first.GetProperty("state").GetString() != "Archived") { var current = (await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/help-videos/")).GetProperty("revisions").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == first.GetProperty("id").GetGuid()); if (current.GetProperty("state").GetString() != "Archived") await VideoTransition(platform, current, "Archived"); } }
    });
}
