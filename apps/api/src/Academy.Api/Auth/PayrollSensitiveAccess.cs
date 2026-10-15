using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Auth;

// Reuse StaffGrants. CRUD and starting templates never imply sensitive grants.
public static class PayrollSensitiveAccess
{
    public static readonly string[] Permissions = [AcademyPermissions.PayrollAdditionReview, AcademyPermissions.PayrollPay, AcademyPermissions.CoachAgreementManage,AcademyPermissions.SalaryManage,AcademyPermissions.PayrollStatementApprove,AcademyPermissions.PayrollAdvanceManage];
    public static bool Contains(string permission) => Permissions.Contains(permission);

    public static async Task<bool> Allowed(FoundationDbContext db, TenantMembership tenant, string permission)
    {
        if (!Contains(permission)) return false;
        if (tenant.Role == AcademyRole.AcademyOwner) return true;
        if (tenant.Role != AcademyRole.AcademyAdmin || db.SportScopeRestricted || db.AdditionalContextScope) return false;
        var member = await db.AcademyMemberships.AsNoTracking().SingleOrDefaultAsync(m => m.Id == tenant.MembershipId && m.AcademyId == tenant.AcademyId);
        if (member is null || !member.IsActive || member.PrimaryMembershipId.HasValue || !member.AllSports) return false;
        if (!await db.StaffGrants.AnyAsync(g => g.AcademyId == tenant.AcademyId && g.MembershipId == member.Id && g.Permission == permission && g.IsActive && g.Allowed)) return false;
        // Review also needs Edit; recording payment also needs Add. Legacy
        // accounts require payroll.manage as well as the explicit new grant.
        return member.UseActionGrants
            ? await db.StaffActionGrants.AnyAsync(g => g.MembershipId == member.Id && g.IsActive && g.Module == "payroll" && g.Read && (permission == AcademyPermissions.PayrollPay ? g.Add : permission == AcademyPermissions.CoachAgreementManage || permission == AcademyPermissions.SalaryManage || permission == AcademyPermissions.PayrollAdvanceManage ? g.Add && g.Edit : g.Edit))
            : await db.StaffGrants.CountAsync(g => g.MembershipId == member.Id && (g.Permission == AcademyPermissions.PayrollManage || g.Permission == AcademyPermissions.PayrollRead) && g.IsActive && g.Allowed) == 2;
    }

    public static async Task<bool> LockAndAuthorize(HttpContext context, CurrentTenant current, FoundationDbContext db, string permission)
    {
        var tenant = await current.ResolveAsync();
        if (tenant is null || db.Database.CurrentTransaction is null) return false;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{tenant.AcademyId}:{tenant.MembershipId}"}, 0))");
        if (!await db.AcademyMemberships.AsNoTracking().AnyAsync(m => m.Id == tenant.MembershipId && m.AcademyId == tenant.AcademyId && m.Role == tenant.Role && m.IsActive && m.User.IsActive)) return false;
        var authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
        return (await authorization.AuthorizeAsync(context.User, AcademyPermissions.PayrollRead)).Succeeded
            && (await authorization.AuthorizeAsync(context.User, AcademyPermissions.PayrollManage)).Succeeded
            && (await authorization.AuthorizeAsync(context.User, permission)).Succeeded;
    }

    public static async Task<bool> BranchAllowed(FoundationDbContext db, TenantMembership tenant, Guid branch)
        => tenant.Role == AcademyRole.AcademyOwner
            || await db.AcademyMemberships.AnyAsync(m => m.Id == tenant.MembershipId && m.IsActive && (m.AllBranches
                || db.MembershipBranchAccesses.Any(a => a.MembershipId == m.Id && a.IsActive && a.BranchId == branch && db.Branches.Any(b => b.Id == branch && b.IsActive))));

    public static Task<bool> IsSelf(FoundationDbContext db,TenantMembership tenant,Guid? employeeMembership)
        => db.AcademyMemberships.AnyAsync(m=>m.AcademyId==tenant.AcademyId&&m.Id==employeeMembership&&db.AcademyMemberships.Any(a=>a.Id==tenant.MembershipId&&a.AcademyId==tenant.AcademyId&&a.UserId==m.UserId));
}
