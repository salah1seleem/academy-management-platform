using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public sealed record StaffAccessContextWrite(string Name, bool IsActive = false, int Revision = 0);

public static class StaffAccessContextEndpoints
{
    public static void MapStaffAccessContexts(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/manage/staff").RequireAuthorization(AcademyPermissions.TenantAccess);
        api.MapGet("/{id:guid}/contexts", List);
        api.MapPost("/{id:guid}/contexts", Create).AddEndpointFilter<CsrfFilter>();
        api.MapPut("/{id:guid}/context", Update).AddEndpointFilter<CsrfFilter>();
    }
    private static bool Valid(StaffAccessContextWrite b) => !string.IsNullOrWhiteSpace(b.Name) && b.Name.Trim().Length <= 80
        && !b.Name.Any(char.IsControl) && b.Name.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant().Length <= 160;
    private static async Task<IResult> List(Guid id, CurrentTenant tenant, FoundationDbContext db, HttpResponse response)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        var primary = await db.AcademyMemberships.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id && m.AcademyId == t.AcademyId && m.PrimaryMembershipId == null && m.Role == AcademyRole.AcademyAdmin);
        if (primary is null) return Results.NotFound();
        response.Headers.CacheControl = "no-store";
        return Results.Ok(new { primaryId = id, items = await db.AcademyMemberships.Where(m => m.PrimaryMembershipId == id && m.AcademyId == t.AcademyId).OrderBy(m => m.AccessContextName)
            .Select(m => new { m.Id, name = m.AccessContextName, m.IsActive, revision = m.ContextRevision, m.AllBranches, m.AllSports,
                branches = db.MembershipBranchAccesses.Where(a => a.MembershipId == m.Id && a.IsActive).Select(a => a.BranchId).ToArray(),
                sports = db.MembershipSportAccesses.Where(a => a.MembershipId == m.Id && a.IsActive).Select(a => a.SportId).ToArray() }).ToArrayAsync() });
    }
    private static async Task<IResult> Create(Guid id, StaffAccessContextWrite body, CurrentTenant tenant, FoundationDbContext db, ClaimsPrincipal actor, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        if (!Valid(body) || body.IsActive || body.Revision != 0) return Results.Problem(statusCode: 422, title: "سمِّ السياق الجديد؛ يبدأ موقوفًا حتى مراجعة نطاقه وصلاحياته");
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await StaffRoleTemplateEndpoints.LockOwner(db, t, actor)) return Results.Forbid();
        await Lock(db, t.AcademyId, id);
        var primary = await db.AcademyMemberships.SingleOrDefaultAsync(m => m.Id == id && m.AcademyId == t.AcademyId && m.PrimaryMembershipId == null && m.Role == AcademyRole.AcademyAdmin && m.IsActive && m.User.IsActive);
        if (primary is null) return Results.NotFound();
        if (await db.AcademyMemberships.CountAsync(m => m.PrimaryMembershipId == id) >= 10) return Results.Problem(statusCode: 422, title: "راجع السياقات الموجودة بدل إنشاء أكثر من عشرة سياقات لهذا الإداري");
        var now = clock.GetUtcNow(); var row = new AcademyMembership { Id = Guid.NewGuid(), AcademyId = t.AcademyId, UserId = primary.UserId, Role = AcademyRole.AcademyAdmin, PrimaryMembershipId = primary.Id, AccessContextName = body.Name.Trim(), NormalizedContextName = Normalize(body.Name), AllBranches = false, AllSports = false, UseActionGrants = true, IsActive = false, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.AcademyMemberships.Add(row); Audit(db, row, actor, "{}", now);
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Created($"/api/v1/manage/staff/{row.Id}/access", new { row.Id }); }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Problem(statusCode: 409, title: "يوجد سياق بهذا الاسم لهذا المستخدم"); }
    }
    private static async Task<IResult> Update(Guid id, StaffAccessContextWrite body, CurrentTenant tenant, FoundationDbContext db, ClaimsPrincipal actor, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        if (!Valid(body)) return Results.Problem(statusCode: 422, title: "أدخل اسمًا واضحًا للسياق");
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await StaffRoleTemplateEndpoints.LockOwner(db, t, actor)) return Results.Forbid();
        await Lock(db, t.AcademyId, id);
        var row = await db.AcademyMemberships.SingleOrDefaultAsync(m => m.Id == id && m.AcademyId == t.AcademyId && m.PrimaryMembershipId != null);
        if (row is null) return Results.NotFound();
        await Lock(db, t.AcademyId, row.PrimaryMembershipId!.Value);
        if (body.Revision != row.ContextRevision) return Results.Problem(statusCode: 409, title: "تغير السياق؛ أعد تحميله قبل تعديل حالته");
        if (body.IsActive && (!await db.AcademyMemberships.AnyAsync(m => m.Id == row.PrimaryMembershipId && m.PrimaryMembershipId == null && m.Role == AcademyRole.AcademyAdmin && m.IsActive && m.User.IsActive)
            || !await db.StaffActionGrants.AnyAsync(g => g.MembershipId == id && g.IsActive && g.Read)
            || !row.AllBranches && !await db.MembershipBranchAccesses.AnyAsync(a => a.MembershipId == id && a.IsActive && db.Branches.Any(b => b.Id == a.BranchId && b.IsActive))
            || !row.AllSports && !await db.MembershipSportAccesses.AnyAsync(a => a.MembershipId == id && a.IsActive && db.Sports.Any(s => s.Id == a.SportId && s.IsActive))))
            return Results.Problem(statusCode: 422, title: "احفظ صلاحيات ونطاقًا صالحين أولًا؛ يلزم بقاء عضوية الإداري الأساسية فعالة");
        var before = JsonSerializer.Serialize(new { row.AccessContextName, row.IsActive, row.ContextRevision });
        row.AccessContextName = body.Name.Trim(); row.NormalizedContextName = Normalize(body.Name); row.IsActive = body.IsActive; row.ContextRevision++; row.UpdatedAtUtc = clock.GetUtcNow(); Audit(db, row, actor, before, row.UpdatedAtUtc);
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent(); }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Problem(statusCode: 409, title: "يوجد سياق بهذا الاسم لهذا المستخدم"); }
    }
    private static string Normalize(string name) => name.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
    internal static Task Lock(FoundationDbContext db, Guid academy, Guid member) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{academy}:{member}"}, 0))");
    private static void Audit(FoundationDbContext db, AcademyMembership row, ClaimsPrincipal actor, string before, DateTimeOffset now) => db.StaffAccessAudits.Add(new() { Id = Guid.NewGuid(), AcademyId = row.AcademyId, MembershipId = row.Id, ActorUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!), Before = before, After = JsonSerializer.Serialize(new { row.AccessContextName, row.PrimaryMembershipId, row.IsActive, row.ContextRevision, kind = "operational-access-context" }), CreatedAtUtc = now, UpdatedAtUtc = now });
}
