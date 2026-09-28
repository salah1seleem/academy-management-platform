using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;

namespace Academy.Api.Auth;

public static class AcademyPermissions
{
    public const string TenantAccess = "tenant.access";
    public const string StaffProvision = "staff.provision";
}

public sealed record TenantPermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class TenantPermissionHandler(CurrentTenant currentTenant)
    : AuthorizationHandler<TenantPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantPermissionRequirement requirement)
    {
        var membership = await currentTenant.ResolveAsync();
        if (membership is null) return;

        var allowed = requirement.Permission switch
        {
            AcademyPermissions.TenantAccess => true,
            AcademyPermissions.StaffProvision => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            _ => false
        };
        if (allowed) context.Succeed(requirement);
    }
}
