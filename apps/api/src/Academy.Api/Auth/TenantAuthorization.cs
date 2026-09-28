using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;

namespace Academy.Api.Auth;

public static class AcademyPermissions
{
    public const string TenantAccess = "tenant.access";
    public const string StaffProvision = "staff.provision";
    public const string StructureManage = "structure.manage";
    public const string PeopleManage = "people.manage";
    public const string GuardianChildrenRead = "guardian.children.read";
    public const string CoachGroupsRead = "coach.groups.read";
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
            AcademyPermissions.StructureManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.PeopleManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.GuardianChildrenRead => membership.Role == AcademyRole.Guardian,
            AcademyPermissions.CoachGroupsRead => membership.Role == AcademyRole.Coach,
            _ => false
        };
        if (allowed) context.Succeed(requirement);
    }
}
