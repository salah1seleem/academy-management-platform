using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class StaffAccessEndpoints
{
    public static void MapStaffAccess(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/manage/staff").RequireAuthorization(AcademyPermissions.TenantAccess);
        api.MapGet("/{id:guid}/access", Get);
        api.MapPut("/{id:guid}/access", Save).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/my-access", async (CurrentTenant tenant, FoundationDbContext db) => {
            var t = (await tenant.ResolveAsync())!; var m = await db.AcademyMemberships.SingleAsync(x => x.Id == t.MembershipId && x.AcademyId == t.AcademyId);
            return Results.Ok(new { m.AllBranches, m.AllSports, m.UseActionGrants, branches = await db.MembershipBranchAccesses.Where(x => x.MembershipId == m.Id && x.IsActive).Select(x => x.BranchId).ToListAsync(), sports = await db.MembershipSportAccesses.Where(x => x.MembershipId == m.Id && x.IsActive).Select(x => x.SportId).ToListAsync(), actions = await db.StaffActionGrants.Where(x => x.MembershipId == m.Id && x.IsActive).Select(x => new { x.Module, x.Read, x.Add, x.Edit, x.Delete }).ToListAsync() });
        }).RequireAuthorization(AcademyPermissions.TenantAccess);
    }
    private static async Task<IResult> Get(Guid id, CurrentTenant tenant, FoundationDbContext db, HttpResponse response)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        var m = await db.AcademyMemberships.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.Role == AcademyRole.AcademyAdmin);
        if (m is null) return Results.NotFound();
        response.Headers.CacheControl = "no-store";
        var custom = await db.StaffRoleTemplates.AsNoTracking().Where(r => r.AcademyId == t.AcademyId && r.IsActive).OrderBy(r => r.Name).Take(100).ToArrayAsync();
        var source = await db.StaffRoleTemplates.AsNoTracking().Where(r => r.Id == m.PermissionTemplateId && r.AcademyId == t.AcademyId).Select(r => new { r.Id, r.Name, r.IsActive, currentRevision = r.Revision, appliedRevision = m.PermissionTemplateRevision, customized = m.PermissionTemplateCustomized }).SingleOrDefaultAsync();
        return Results.Ok(new { m.AllBranches, m.AllSports, m.UseActionGrants, m.PrimaryMembershipId, m.AccessContextName, source, modules = StaffAccessRules.Modules, templates = StaffAccessTemplates.All.Concat(custom.Select(StaffRoleTemplateEndpoints.View)),
            sensitivePermissions = PayrollSensitiveAccess.Permissions,
            selectedSensitivePermissions = await db.StaffGrants.Where(g => g.MembershipId == id && g.IsActive && g.Allowed && PayrollSensitiveAccess.Permissions.Contains(g.Permission)).Select(g => g.Permission).ToArrayAsync(),
            branches = await db.Branches.Where(x => x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, name = x.ArabicName }).ToListAsync(),
            sports = await db.Sports.Where(x => x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, name = x.ArabicName }).ToListAsync(),
            selectedSports = await db.MembershipSportAccesses.Where(x => x.MembershipId == id && x.IsActive).Select(x => x.SportId).ToListAsync(),
            selectedBranches = await db.MembershipBranchAccesses.Where(x => x.MembershipId == id && x.IsActive).Select(x => x.BranchId).ToListAsync(),
            actions = await db.StaffActionGrants.Where(x => x.MembershipId == id && x.IsActive).Select(x => new { x.Module, x.Read, x.Add, x.Edit, x.Delete }).ToListAsync(),
            history = await db.StaffAccessAudits.Where(x => x.MembershipId == id).OrderByDescending(x => x.CreatedAtUtc).Take(20).Select(x => new { x.CreatedAtUtc, x.Before, x.After }).ToListAsync() });
    }
    private static async Task<IResult> Save(Guid id, StaffAccessWrite body, CurrentTenant tenant, FoundationDbContext db, ClaimsPrincipal actor, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        var isCustom = StaffRoleTemplateEndpoints.CustomId(body.TemplateKey, out var templateId);
        if (body.SensitivePermissions is not null && (body.SensitivePermissions.Length > PayrollSensitiveAccess.Permissions.Length
            || body.SensitivePermissions.Distinct().Count() != body.SensitivePermissions.Length
            || body.SensitivePermissions.Any(p => !PayrollSensitiveAccess.Contains(p))))
            return Results.Problem(statusCode: 422, title: "اختر تفويضات الرواتب المتاحة دون تكرار");
        if (body.TemplateKey is not null && !isCustom && !StaffAccessTemplates.All.Any(x => x.Key == body.TemplateKey) || !isCustom && body.TemplateRevision.HasValue || body.Branches is null || !StaffRoleTemplateEndpoints.Valid(body.Actions) || body.Branches.Length > 100 || body.Branches.Distinct().Count() != body.Branches.Length || !body.AllBranches && body.Branches.Length == 0)
            return Results.Problem(statusCode: 422, title: "راجع الفروع والصلاحيات؛ الإضافة والتعديل والحذف تتطلب القراءة");
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await StaffRoleTemplateEndpoints.LockOwner(db, t, actor)) return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{id}"}, 0))");
        var m = await db.AcademyMemberships.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.Role == AcademyRole.AcademyAdmin);
        if (m is null) return Results.NotFound();
        if (body.SensitivePermissions is { Length: > 0 } && (m.PrimaryMembershipId.HasValue || (body.AllSports ?? m.AllSports) == false
            || !body.Actions.Any(a => a.Module == "payroll" && a.Read
                && (!body.SensitivePermissions.Contains(AcademyPermissions.PayrollPay) || a.Add)
                && (!body.SensitivePermissions.Contains(AcademyPermissions.PayrollAdditionReview) || a.Edit))))
            return Results.Problem(statusCode: 422, title: "تفويض الرواتب يحتاج نطاق كل الرياضات وقراءة الوحدة، مع الإضافة للصرف والتعديل للمراجعة؛ لا يمنح لسياق تشغيل إضافي");
        if (m.PrimaryMembershipId is Guid primary)
        {
            await StaffAccessContextEndpoints.Lock(db, t.AcademyId, primary);
            if (!await db.AcademyMemberships.AnyAsync(p => p.Id == primary && p.PrimaryMembershipId == null && p.IsActive && p.User.IsActive)) return Results.Problem(statusCode: 409, title: "عضوية الإداري الأساسية موقوفة؛ لا يمكن تغيير سياقها");
        }
        StaffRoleTemplate? template = null;
        if (isCustom)
        {
            await StaffRoleTemplateEndpoints.Lock(db, t.AcademyId, templateId);
            template = await db.StaffRoleTemplates.AsNoTracking().SingleOrDefaultAsync(r => r.AcademyId == t.AcademyId && r.Id == templateId);
            if (template is null) return Results.NotFound();
            if (!template.IsActive || body.TemplateRevision != template.Revision) return Results.Problem(statusCode: 409, title: "القالب تغير أو أُوقف؛ أعد تحميله وراجع الصلاحيات قبل الاعتماد");
        }
        // Old callers omit both fields: preserve the current sport scope rather
        // than silently widening an existing restricted grant.
        if (body.AllSports.HasValue != (body.Sports is not null) || body.Sports is { Length: > 100 }
            || body.Sports is not null && (body.Sports.Distinct().Count() != body.Sports.Length
                || body.AllSports == false && body.Sports.Length == 0
                || await db.Sports.CountAsync(x => body.Sports.Contains(x.Id) && x.IsActive) != body.Sports.Length))
            return Results.Problem(statusCode: 422, title: "اختر رياضات صالحة من الأكاديمية ولا تترك النطاق المحدد فارغًا");
        if (await db.Branches.CountAsync(x => body.Branches.Contains(x.Id) && x.IsActive) != body.Branches.Length) return Results.Problem(statusCode: 422, title: "فرع غير صالح أو خارج الأكاديمية");
        var branches = await db.MembershipBranchAccesses.Where(x => x.MembershipId == id).ToListAsync(); var actions = await db.StaffActionGrants.Where(x => x.MembershipId == id).ToListAsync();
        var sports = await db.MembershipSportAccesses.Where(x => x.MembershipId == id).ToListAsync();
        var previous = actions.Where(x => x.IsActive).Select(x => new StaffActionValue(x.Module, x.Read, x.Add, x.Edit, x.Delete)).ToArray();
        var previousSensitive = await db.StaffGrants.Where(g => g.MembershipId == id && g.IsActive && g.Allowed && PayrollSensitiveAccess.Permissions.Contains(g.Permission)).Select(g => g.Permission).ToArrayAsync();
        var before = JsonSerializer.Serialize(new { m.AllBranches, m.AllSports, m.UseActionGrants, m.PermissionTemplateId, m.PermissionTemplateRevision, m.PermissionTemplateCustomized, branches = branches.Where(x => x.IsActive).Select(x => x.BranchId), sports = sports.Where(x => x.IsActive).Select(x => x.SportId), actions = previous, sensitivePermissions = previousSensitive });
        var now = clock.GetUtcNow(); m.AllBranches = body.AllBranches; m.UseActionGrants = true; m.UpdatedAtUtc = now; m.ContextRevision++;
        if (template is not null)
        {
            m.PermissionTemplateId = template.Id; m.PermissionTemplateRevision = template.Revision;
            m.PermissionTemplateCustomized = !StaffRoleTemplateEndpoints.Same(body.Actions, StaffRoleTemplateEndpoints.View(template).Actions);
        }
        else if (body.TemplateKey is not null) { m.PermissionTemplateId = null; m.PermissionTemplateRevision = null; m.PermissionTemplateCustomized = false; }
        else if (m.PermissionTemplateId.HasValue && !StaffRoleTemplateEndpoints.Same(previous, body.Actions)) m.PermissionTemplateCustomized = true;
        if (body.AllSports.HasValue)
        {
            m.AllSports = body.AllSports.Value;
            foreach (var row in sports) { row.IsActive = !m.AllSports && body.Sports!.Contains(row.SportId); row.UpdatedAtUtc = now; }
            foreach (var sport in body.Sports!.Where(x => !m.AllSports && sports.All(s => s.SportId != x)))
                db.MembershipSportAccesses.Add(new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, MembershipId = id, SportId = sport, CreatedAtUtc = now, UpdatedAtUtc = now });
        }
        foreach (var row in branches) { row.IsActive = !body.AllBranches && body.Branches.Contains(row.BranchId); row.UpdatedAtUtc = now; }
        foreach (var branch in body.Branches.Where(x => !body.AllBranches && branches.All(b => b.BranchId != x))) db.MembershipBranchAccesses.Add(new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, MembershipId = id, BranchId = branch, CreatedAtUtc = now, UpdatedAtUtc = now });
        foreach (var a in body.Actions)
        {
            var row = actions.SingleOrDefault(x => x.Module == a.Module);
            if (row is null) { row = new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, MembershipId = id, Module = a.Module, CreatedAtUtc = now }; db.StaffActionGrants.Add(row); }
            row.Read = a.Read; row.Add = a.Add; row.Edit = a.Edit; row.Delete = a.Delete; row.IsActive = true; row.UpdatedAtUtc = now;
        }
        var legacy = await db.StaffGrants.Where(x => x.MembershipId == id).ToListAsync();
        // Omission preserves explicit grants for old callers. An empty array
        // revokes both. No template can silently grant these operations.
        if (body.SensitivePermissions is not null)
            foreach (var permission in PayrollSensitiveAccess.Permissions)
            {
                var row = legacy.SingleOrDefault(g => g.Permission == permission);
                if (row is null) { row = new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, MembershipId = id, Permission = permission, CreatedAtUtc = now }; db.StaffGrants.Add(row); }
                row.Allowed = body.SensitivePermissions.Contains(permission); row.IsActive = true; row.UpdatedAtUtc = now;
            }
        foreach (var permission in StaffManagement.Permissions)
        {
            var module = StaffAccessRules.ForPermission(permission); var a = body.Actions.SingleOrDefault(x => x.Module == module);
            var row = legacy.SingleOrDefault(x => x.Permission == permission);
            if (row is null) { row = new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, MembershipId = id, Permission = permission, CreatedAtUtc = now }; db.StaffGrants.Add(row); }
            row.Allowed = a?.Read == true; row.IsActive = true; row.UpdatedAtUtc = now;
        }
        var effective = body with { AllSports = m.AllSports, Sports = m.AllSports ? [] : body.Sports ?? sports.Where(x => x.IsActive).Select(x => x.SportId).ToArray() };
        var after = JsonSerializer.SerializeToNode(effective)!;
        after["SensitivePermissions"] = JsonSerializer.SerializeToNode(body.SensitivePermissions ?? previousSensitive);
        after["SensitivePermissionsChanged"] = !previousSensitive.Order().SequenceEqual((body.SensitivePermissions ?? previousSensitive).Order());
        after["AppliedTemplate"] = JsonSerializer.SerializeToNode(new { m.PermissionTemplateId, m.PermissionTemplateRevision, m.PermissionTemplateCustomized });
        db.StaffAccessAudits.Add(new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, MembershipId = id, ActorUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!), Before = before, After = after.ToJsonString(), CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
    }
}
public sealed record StaffActionValue(string Module, bool Read, bool Add, bool Edit, bool Delete);
public sealed record StaffAccessWrite(bool AllBranches, Guid[] Branches, StaffActionValue[] Actions, string? TemplateKey = null, bool? AllSports = null, Guid[]? Sports = null, int? TemplateRevision = null, string[]? SensitivePermissions = null);
