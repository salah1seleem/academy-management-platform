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
    public const string AttendanceRead = "attendance.read";
    public const string AttendanceManage = "attendance.manage";
    public const string GuardianAttendanceRead = "attendance.guardian.read";
    public const string EvaluationRead = "evaluation.read";
    public const string EvaluationManage = "evaluation.manage";
    public const string EvaluationCriteriaManage = "evaluation.criteria.manage";
    public const string GuardianEvaluationRead = "evaluation.guardian.read";
    public const string EnrollmentRequestRead = "enrollment.request.read";
    public const string EnrollmentRequestManage = "enrollment.request.manage";
    public const string GuardianEnrollmentRequestCreate = "enrollment.request.guardian.create";
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
            AcademyPermissions.AttendanceRead => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin or AcademyRole.Coach,
            AcademyPermissions.AttendanceManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin or AcademyRole.Coach,
            AcademyPermissions.GuardianAttendanceRead => membership.Role == AcademyRole.Guardian,
            AcademyPermissions.EvaluationRead => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin or AcademyRole.Coach,
            AcademyPermissions.EvaluationManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin or AcademyRole.Coach,
            AcademyPermissions.EvaluationCriteriaManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.GuardianEvaluationRead => membership.Role == AcademyRole.Guardian,
            AcademyPermissions.EnrollmentRequestRead => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.EnrollmentRequestManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.GuardianEnrollmentRequestCreate => membership.Role == AcademyRole.Guardian,
            _ => false
        };
        if (allowed) context.Succeed(requirement);
    }
}
