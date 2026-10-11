using System.Security.Claims;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Auth;

public sealed class RequestIsolationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CurrentTenant tenant, FoundationDbContext db)
    {
        var path = context.Request.Path.Value ?? "";
        // Identity discovery and explicit membership selection already constrain user IDs.
        // They must not be reduced to the currently selected academy.
        if (!path.StartsWith("/api/v1/platform/", StringComparison.Ordinal) && !path.StartsWith("/api/v1/auth/", StringComparison.Ordinal)
            && !path.StartsWith("/api/v1/mobile/auth/", StringComparison.Ordinal) && path is not ("/api/v1/my-academies" or "/api/v1/session/academy"))
        {
            var t = await tenant.ResolveAsync(context.RequestAborted);
            if (t is not null)
            {
                Guid[]? branches = null;
                if (t.Role == AcademyRole.AcademyAdmin && !await db.AcademyMemberships.Where(x => x.Id == t.MembershipId && x.AcademyId == t.AcademyId).Select(x => x.AllBranches).SingleAsync(context.RequestAborted))
                    branches = await db.MembershipBranchAccesses.Where(x => x.AcademyId == t.AcademyId && x.MembershipId == t.MembershipId && x.IsActive && db.Branches.Any(b => b.AcademyId == t.AcademyId && b.Id == x.BranchId && b.IsActive)).Select(x => x.BranchId).ToArrayAsync(context.RequestAborted);
                db.BindRequestScope(t.AcademyId, Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!), t.MembershipId, branches);
                if (t.Role == AcademyRole.AcademyAdmin && await db.AcademyMemberships.Where(x => x.Id == t.MembershipId).Select(x => x.UseActionGrants).SingleAsync(context.RequestAborted))
                {
                    var module = StaffAccessRules.ForPath(path);
                    if (module is not null)
                    {
                        var grant = await db.StaffActionGrants.AsNoTracking().SingleOrDefaultAsync(x => x.MembershipId == t.MembershipId && x.Module == module && x.IsActive, context.RequestAborted);
                        var operation = StaffAccessRules.Operation(context.Request.Method, path);
                        var allowed = grant?.Read == true && (operation == "Read" || operation == "Add" && grant.Add || operation == "Edit" && grant.Edit || operation == "Delete" && grant.Delete);
                        if (!allowed && context.Request.Method == "GET" && path.TrimEnd('/') == "/api/v1/manage/structure/options")
                            allowed = await db.StaffActionGrants.AnyAsync(x => x.MembershipId == t.MembershipId && x.IsActive && x.Read && (x.Module == "players" || x.Module == "subscriptions" || x.Module == "attendance" || x.Module == "evaluations"), context.RequestAborted);
                        if (!allowed) { await Deny(context); return; }
                    }
                    else if (path.StartsWith("/api/v1/", StringComparison.Ordinal) && path is not ("/api/v1/me" or "/api/v1/my-permissions" or "/api/v1/my-access" or "/api/v1/academy/features" or "/api/v1/academy/branding/logo")) { await Deny(context); return; }
                }
            }
        }
        try { await next(context); }
        catch (ResourceScopeException) { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { title = "العملية خارج نطاق الأكاديمية أو الفروع المسموح بها", status = 403 }); }
    }
    private static async Task Deny(HttpContext context) { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { title = "لا تملك صلاحية هذه العملية", status = 403 }); }
}
