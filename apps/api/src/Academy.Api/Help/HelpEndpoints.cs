using System.Text;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Help;

public static class HelpEndpoints
{
    // Public to the existing isolation guards: only these authenticated reads
    // are shared infrastructure. Each guide still checks its live permission.
    public static bool IsReadPath(string path, string method) => method == "GET" &&
        (path is "/api/v1/help/availability" or "/api/v1/help/lessons" ||
        path.StartsWith("/api/v1/help/lessons/", StringComparison.Ordinal) &&
        path["/api/v1/help/lessons/".Length..] is { Length: > 0 } tail && !tail.Contains('/'));
    public static void MapHelp(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/help").RequireAuthorization(AcademyPermissions.TenantAccess);
        g.MapGet("/availability", async (CurrentTenant tenant, AcademyConfigurationService config, HttpResponse r) => { r.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!; return Results.Ok(new { available = await config.Enabled(t.AcademyId, "learning_hub") }); });
        g.MapGet("/lessons", List); g.MapGet("/lessons/{id}", Details);
        app.MapGet("/api/v1/manage/configuration/help-options", (HttpResponse response) =>
        {
            response.Headers.CacheControl = "private, no-store"; return Results.Ok(new
            {
                lessons = HelpCatalog.All.Select(g => new { g.Id, g.Title, roles = g.Roles.Select(r => r.ToString()), g.Feature }),
                notice = "اختيار الجمهور يضيّق الإتاحة ولا يمنح صلاحية تنفيذ. التغييرات تسري بعد تفعيل إصدار إعدادات التشغيل."
            });
        }).RequireAuthorization(AcademyPermissions.AcademySettingsManage);
    }
    private static string Normalize(string value) => new(value.Normalize(NormalizationForm.FormD).Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark && c != 'ـ').Select(c => c switch { 'أ' or 'إ' or 'آ' => 'ا', 'ى' => 'ي', _ => char.ToLowerInvariant(c) }).ToArray());
    private static async Task<HelpGuide[]> Allowed(CurrentTenant tenant, FoundationDbContext db, AcademyConfigurationService config, IAuthorizationService authorization, HttpContext h)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!;
        if (!await config.Enabled(t.AcademyId, "learning_hub")) return [];
        var active = await config.Active(t.AcademyId); var result = new List<HelpGuide>();
        foreach (var guide in HelpCatalog.All.Where(l => l.Roles.Contains(t.Role)))
        {
            if (!HelpAudiencePolicy.Allows(active?.HelpAudiences, guide.Id, t.Role.ToString())) continue;
            if (guide.Feature != null && !await config.Enabled(t.AcademyId, guide.Feature) || guide.CashOnly && active?.CashEnabled != true) continue;
            if (guide.Sport.HasValue && !await db.Sports.AnyAsync(s => s.IsActive && s.Kind == guide.Sport)) continue;
            var allowed = true; foreach (var permission in guide.Permissions) if (!(await authorization.AuthorizeAsync(h.User, h, permission)).Succeeded) { allowed = false; break; }
            if (allowed) result.Add(guide);
        }
        return result.ToArray();
    }
    private static object View(HelpGuide l, bool detail) => new { l.Id, l.Section, l.Title, l.Summary, version = 1, contentKind = l.AwaitingContent ? "AwaitingContent" : "WrittenGuide", videoStatus = "AwaitingUpload", videoUrl = (string?)null, steps = detail ? l.Steps : null };
    private static async Task<IResult> List(CurrentTenant tenant, FoundationDbContext db, AcademyConfigurationService config, IAuthorizationService authorization, HttpContext h, string? search, string? section)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!; if (!await config.Enabled(t.AcademyId, "learning_hub")) return Results.Forbid();
        if (search?.Length > 100 || section?.Length > 40) return Results.UnprocessableEntity(new { message = "اختصر البحث إلى 100 حرف واختر قسمًا صحيحًا." });
        var guides = await Allowed(tenant, db, config, authorization, h); var q = Normalize(search?.Trim() ?? "");
        return Results.Ok(new { version = 1, role = t.Role.ToString(), items = guides.Where(l => (string.IsNullOrEmpty(section) || l.Section == section) && (q.Length == 0 || Normalize(l.Title.Ar + " " + l.Title.En + " " + l.Summary.Ar + " " + l.Summary.En + " " + string.Join(" ", l.Steps.Select(s => s.Ar + " " + s.En))).Contains(q))).Select(l => View(l, false)), notice = "أدلة مكتوبة للمسارات الحالية؛ الفيديوهات بانتظار رفع المحتوى ومراجعته. قراءة الدليل لا تمنح صلاحية تنفيذ." });
    }
    private static async Task<IResult> Details(string id, CurrentTenant tenant, FoundationDbContext db, AcademyConfigurationService config, IAuthorizationService authorization, HttpContext h)
    {
        var guide = (await Allowed(tenant, db, config, authorization, h)).SingleOrDefault(l => l.Id == id);
        return guide == null ? Results.NotFound() : Results.Ok(View(guide, true));
    }
}
