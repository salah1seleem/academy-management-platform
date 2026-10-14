using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public sealed record StaffRoleTemplateWrite(string Name, string? Description, StaffActionValue[] Actions, bool IsActive = true, int Revision = 0);

public static class StaffRoleTemplateEndpoints
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static bool Valid(StaffActionValue[]? actions) => actions is not null && actions.Length == StaffAccessRules.Modules.Length
        && actions.All(a => a is not null && StaffAccessRules.Modules.Contains(a.Module) && (a.Read || !a.Add && !a.Edit && !a.Delete))
        && actions.Select(a => a.Module).Distinct().Count() == actions.Length;
    internal static bool Same(StaffActionValue[] a, StaffActionValue[] b) => a.OrderBy(x => x.Module).SequenceEqual(b.OrderBy(x => x.Module));
    internal static StaffAccessTemplate View(StaffRoleTemplate r) => new($"custom:{r.Id}", r.Name, r.Description ?? "قالب مخصص للأكاديمية؛ راجع النطاق قبل الاعتماد.",
        JsonSerializer.Deserialize<StaffActionValue[]>(r.ActionsJson, Json)!, r.Revision, true);
    internal static bool CustomId(string? key, out Guid id)
    {
        id = default; return key?.StartsWith("custom:", StringComparison.Ordinal) == true && Guid.TryParse(key[7..], out id);
    }
    internal static async Task<bool> LockOwner(FoundationDbContext db, TenantMembership t, ClaimsPrincipal principal)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var user)) return false;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"}, 0))");
        return await db.AcademyMemberships.AsNoTracking().AnyAsync(m => m.Id == t.MembershipId && m.AcademyId == t.AcademyId && m.UserId == user
            && m.Role == AcademyRole.AcademyOwner && m.IsActive && m.User.IsActive && m.Academy.IsActive);
    }
    internal static Task Lock(FoundationDbContext db, Guid academy, Guid id) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"role-template:{academy}:{id}"}, 0))");
    public static void MapStaffRoleTemplates(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/manage/staff/role-templates").RequireAuthorization(AcademyPermissions.TenantAccess);
        api.MapGet("/", List);
        api.MapGet("/{id:guid}", Detail);
        api.MapPost("/", Create).AddEndpointFilter<CsrfFilter>();
        api.MapPut("/{id:guid}", Update).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> List(CurrentTenant tenant, FoundationDbContext db, HttpResponse response)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        response.Headers.CacheControl = "no-store";
        var rows = await db.StaffRoleTemplates.AsNoTracking().Where(r => r.AcademyId == t.AcademyId).OrderByDescending(r => r.IsActive).ThenBy(r => r.Name).Take(100).ToArrayAsync();
        var uses = await db.AcademyMemberships.Where(m => m.AcademyId == t.AcademyId && m.PermissionTemplateId != null).GroupBy(m => m.PermissionTemplateId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count);
        return Results.Ok(new { modules = StaffAccessRules.Modules, presets = StaffAccessTemplates.All, items = rows.Select(r => new { r.Id, r.IsActive, template = View(r), appliedCopies = uses.GetValueOrDefault(r.Id) }) });
    }
    private static async Task<IResult> Detail(Guid id, CurrentTenant tenant, FoundationDbContext db, HttpResponse response)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        response.Headers.CacheControl = "no-store";
        var row = await db.StaffRoleTemplates.AsNoTracking().SingleOrDefaultAsync(r => r.AcademyId == t.AcademyId && r.Id == id);
        if (row is null) return Results.NotFound();
        return Results.Ok(new { row.Id, row.IsActive, template = View(row),
            applied = await db.AcademyMemberships.Where(m => m.PermissionTemplateId == id && m.AcademyId == t.AcademyId).OrderBy(m => m.User.DisplayName)
                .Select(m => new { m.Id, name = m.User.DisplayName, m.IsActive, revision = m.PermissionTemplateRevision, customized = m.PermissionTemplateCustomized }).ToArrayAsync(),
            history = await db.StaffRoleTemplateAudits.Where(a => a.TemplateId == id && a.AcademyId == t.AcademyId).OrderByDescending(a => a.CreatedAtUtc).Take(20)
                .Select(a => new { a.Id, a.ActorUserId, a.CreatedAtUtc, a.Before, a.After }).ToArrayAsync() });
    }
    private static bool ValidBody(StaffRoleTemplateWrite b) => !string.IsNullOrWhiteSpace(b.Name) && b.Name.Trim().Length <= 80
        && !b.Name.Any(char.IsControl) && b.Name.Trim().Normalize(NormalizationForm.FormKC).Length <= 160
        && b.Description?.Length is not > 300 && b.Description?.Contains('\0') != true && Valid(b.Actions);
    private static async Task<IResult> Create(StaffRoleTemplateWrite body, CurrentTenant tenant, FoundationDbContext db, ClaimsPrincipal actor, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        if (!ValidBody(body) || body.Revision != 0 || !body.IsActive) return Results.Problem(statusCode: 422, title: "أدخل اسم القالب وصلاحيات صحيحة؛ الكتابة تتطلب القراءة");
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await LockOwner(db, t, actor)) return Results.Forbid();
        if (await db.StaffRoleTemplates.CountAsync(r => r.AcademyId == t.AcademyId) >= 100) return Results.Problem(statusCode: 422, title: "وصلت إلى الحد المتاح للقوالب؛ راجع القوالب القائمة بدل تكرارها");
        var now = clock.GetUtcNow(); var row = new StaffRoleTemplate { Id = Guid.NewGuid(), AcademyId = t.AcademyId, Name = body.Name.Trim(), NormalizedName = body.Name.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant(), Description = body.Description?.Trim(), ActionsJson = JsonSerializer.Serialize(body.Actions.OrderBy(a => a.Module), Json), CreatedAtUtc = now, UpdatedAtUtc = now };
        db.StaffRoleTemplates.Add(row); Audit(db, row, actor, "{}", now);
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Created($"/api/v1/manage/staff/role-templates/{row.Id}", new { row.Id, row.IsActive, template = View(row) }); }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Problem(statusCode: 409, title: "يوجد قالب بهذا الاسم؛ اختر اسمًا مميزًا"); }
    }
    private static async Task<IResult> Update(Guid id, StaffRoleTemplateWrite body, CurrentTenant tenant, FoundationDbContext db, ClaimsPrincipal actor, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        if (!ValidBody(body)) return Results.Problem(statusCode: 422, title: "راجع اسم القالب والصلاحيات؛ الكتابة تتطلب القراءة");
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await LockOwner(db, t, actor)) return Results.Forbid(); await Lock(db, t.AcademyId, id);
        var row = await db.StaffRoleTemplates.SingleOrDefaultAsync(r => r.Id == id && r.AcademyId == t.AcademyId);
        if (row is null) return Results.NotFound();
        if (body.Revision != row.Revision) return Results.Problem(statusCode: 409, title: "تغير القالب؛ أعد تحميله وراجع النسخة الجديدة قبل الحفظ");
        var before = JsonSerializer.Serialize(new { row.IsActive, template = View(row) }, Json);
        row.Name = body.Name.Trim(); row.NormalizedName = row.Name.Normalize(NormalizationForm.FormKC).ToUpperInvariant(); row.Description = body.Description?.Trim(); row.ActionsJson = JsonSerializer.Serialize(body.Actions.OrderBy(a => a.Module), Json);
        row.IsActive = body.IsActive; row.Revision++; row.UpdatedAtUtc = clock.GetUtcNow(); Audit(db, row, actor, before, row.UpdatedAtUtc);
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { row.Id, row.IsActive, template = View(row) }); }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Problem(statusCode: 409, title: "يوجد قالب بهذا الاسم؛ اختر اسمًا مميزًا"); }
    }
    private static void Audit(FoundationDbContext db, StaffRoleTemplate row, ClaimsPrincipal actor, string before, DateTimeOffset now) => db.StaffRoleTemplateAudits.Add(new() {
        Id = Guid.NewGuid(), AcademyId = row.AcademyId, TemplateId = row.Id, ActorUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!), Before = before,
        After = JsonSerializer.Serialize(new { row.IsActive, template = View(row) }, Json), CreatedAtUtc = now, UpdatedAtUtc = now });
}
