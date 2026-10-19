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
                var sports = await MembershipScope.Sports(db, t, context.RequestAborted);
                var actionGrants=t.Role==AcademyRole.AcademyAdmin&&await db.AcademyMemberships.Where(x=>x.Id==t.MembershipId).Select(x=>x.UseActionGrants).SingleAsync(context.RequestAborted);
                db.BindRequestScope(t.AcademyId, Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!), t.MembershipId, branches, sports,
                    StaffAccessRules.ForPath(path), StaffAccessRules.Operation(context.Request.Method, path), t.AdditionalContext, workflowRequirements:actionGrants?StaffWorkflowAccess.Requirements(path,context.Request.Method):[]);
                if ((sports is not null || t.AdditionalContext) && !MembershipScope.AllowsNarrowSportPath(path, context.Request.Method))
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new { title = t.AdditionalContext ? "هذا السياق التشغيلي لا يفتح الرواتب أو المستخدمين أو العمليات المشتركة؛ استخدم التفويض الأوسع المخول" : "هذه العملية تشمل بيانات مشتركة بين الرياضات وتحتاج تفويضًا أوسع من المالك", status = 403 });
                    return;
                }
                if (actionGrants && !Academy.Api.Operations.StaffAttendanceExceptions.IsOwnPath(path))
                {
                    if (!await StaffWorkflowAccess.Allows(db,t.MembershipId,path,context.Request.Method,context.RequestAborted)) { await Deny(context); return; }
                    var module = StaffAccessRules.ForPath(path);
                    if (module is not null)
                    {
                        var grant = await db.StaffActionGrants.AsNoTracking().SingleOrDefaultAsync(x => x.MembershipId == t.MembershipId && x.Module == module && x.IsActive, context.RequestAborted);
                        var operation = StaffAccessRules.Operation(context.Request.Method, path);
                        var allowed = grant?.Read == true && (operation == "Read" || operation == "Add" && grant.Add || operation == "Edit" && grant.Edit || operation == "Delete" && grant.Delete);
                        // A scoped photo/proof download is authorized by its actual
                        // purpose in AssetEndpoints, not by a blanket media grant.
                        if (sports is not null && MembershipScope.IsAssetRead(path, context.Request.Method)) allowed = true;
                        if (!allowed && context.Request.Method == "GET" && path.TrimEnd('/') == "/api/v1/manage/structure/options")
                            allowed = await db.StaffActionGrants.AnyAsync(x => x.MembershipId == t.MembershipId && x.IsActive && x.Read && (x.Module == "players" || x.Module == "subscriptions" || x.Module == "attendance" || x.Module == "evaluations"), context.RequestAborted);
                        if (!allowed) { await Deny(context); return; }
                    }
                    else if (path.StartsWith("/api/v1/", StringComparison.Ordinal) && !Academy.Api.Help.HelpEndpoints.IsReadPath(path, context.Request.Method) && path is not ("/api/v1/me" or "/api/v1/my-permissions" or "/api/v1/my-access" or "/api/v1/academy/features" or "/api/v1/academy/branding/logo")) { await Deny(context); return; }
                }
            }
        }
        try { await next(context); }
        catch (ResourceScopeException) { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { title = "العملية خارج نطاق الأكاديمية أو الفروع المسموح بها", status = 403 }); }
    }
    private static async Task Deny(HttpContext context) { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { title = "لا تملك صلاحية هذه العملية", status = 403 }); }
}
