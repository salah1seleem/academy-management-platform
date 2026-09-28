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
    public const string SubscriptionPlanManage = "subscription.plan.manage";
    public const string SubscriptionRead = "subscription.read";
    public const string GuardianOwnRenewal = "renewal.guardian.own";
    public const string PaymentRead = "payment.read";
    public const string CollectionRead = "collection.read";
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
            AcademyPermissions.SubscriptionPlanManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.SubscriptionRead => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.GuardianOwnRenewal => membership.Role == AcademyRole.Guardian,
            AcademyPermissions.PaymentRead => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.CollectionRead => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            _ => false
        };
        if (allowed) context.Succeed(requirement);
    }
}
