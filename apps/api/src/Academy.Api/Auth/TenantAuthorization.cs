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
    public const string SubscriptionAdjustmentManage = "subscription.adjustment.manage";
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
    public const string SportCatalogRead = "content.catalog.read";
    public const string SportCatalogManage = "content.catalog.manage";
    public const string NutritionRead = "content.nutrition.read";
    public const string NutritionManage = "content.nutrition.manage";
    public const string MedicalRecordRead = "content.medical.read";
    public const string MedicalRecordManage = "content.medical.manage";
    public const string PlayerMediaRead = "content.media.read";
    public const string PlayerMediaManage = "content.media.manage";
    public const string ReportsFinancialRead = "reports.financial.read";
    public const string ReportsAttendanceRead = "reports.attendance.read";
    public const string ReportsExport = "reports.export";
    public const string OwnerDashboardRead = "dashboard.owner.read";
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
            AcademyPermissions.SubscriptionAdjustmentManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
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
            AcademyPermissions.SportCatalogRead => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin or AcademyRole.Guardian,
            AcademyPermissions.SportCatalogManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.NutritionRead => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin or AcademyRole.Guardian,
            AcademyPermissions.NutritionManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.MedicalRecordRead => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin or AcademyRole.Guardian,
            AcademyPermissions.MedicalRecordManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.PlayerMediaRead => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin or AcademyRole.Guardian,
            AcademyPermissions.PlayerMediaManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.ReportsFinancialRead => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.ReportsAttendanceRead => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin or AcademyRole.Coach,
            AcademyPermissions.ReportsExport => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin or AcademyRole.Coach,
            AcademyPermissions.OwnerDashboardRead => membership.Role == AcademyRole.AcademyOwner,
            _ => false
        };
        if (allowed) context.Succeed(requirement);
    }
}
