using Academy.Infrastructure.Tenancy;
using Academy.Api.Configuration;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace Academy.Api.Auth;

public static class AcademyPermissions
{
    public const string TenantAccess = "tenant.access";
    public const string AcademySettingsManage = "academy.settings.manage";
    public const string StaffProvision = "staff.provision";
    public const string StructureManage = "structure.manage";
    public const string FacilitiesRead = "facilities.read";
    public const string FacilitiesManage = "facilities.manage";
    public const string PeopleManage = "people.manage";
    public const string GuardianChildrenRead = "guardian.children.read";
    public const string CoachGroupsRead = "coach.groups.read";
    public const string SubscriptionPlanManage = "subscription.plan.manage";
    public const string SubscriptionRead = "subscription.read";
    public const string SubscriptionRenewManage = "subscription.renew.manage";
    public const string SubscriptionAdjustmentManage = "subscription.adjustment.manage";
    public const string SubscriptionDiscountManage = "subscription.discount.manage";
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
    public const string PayrollRead = "payroll.read";
    public const string PayrollManage = "payroll.manage";
    public const string PayrollAdditionReview = "payroll.addition.review";
    public const string PayrollPay = "payroll.pay";
    public const string CoachAgreementManage = "payroll.agreement.manage";
    public const string SalaryManage = "payroll.salary.manage";
    public const string PayrollStatementApprove = "payroll.statement.approve";
}

public sealed record TenantPermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class TenantPermissionHandler(CurrentTenant currentTenant, FoundationDbContext db, AcademyConfigurationService configuration)
    : AuthorizationHandler<TenantPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantPermissionRequirement requirement)
    {
        var membership = await currentTenant.ResolveAsync();
        if (membership is null) return;

        if (PayrollSensitiveAccess.Contains(requirement.Permission))
        {
            if (await PayrollSensitiveAccess.Allowed(db, membership, requirement.Permission)
                && await configuration.Enabled(membership.AcademyId, "payroll")) context.Succeed(requirement);
            return;
        }

        var allowed = requirement.Permission switch
        {
            AcademyPermissions.TenantAccess => true,
            AcademyPermissions.AcademySettingsManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.StaffProvision => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.StructureManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.FacilitiesRead or AcademyPermissions.FacilitiesManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.PeopleManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.GuardianChildrenRead => membership.Role == AcademyRole.Guardian,
            AcademyPermissions.CoachGroupsRead => membership.Role == AcademyRole.Coach,
            AcademyPermissions.SubscriptionPlanManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.SubscriptionRead => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.SubscriptionRenewManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.SubscriptionAdjustmentManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.SubscriptionDiscountManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
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
            AcademyPermissions.OwnerDashboardRead => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            AcademyPermissions.PayrollRead or AcademyPermissions.PayrollManage => membership.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,
            _ => false
        };
        if (allowed && membership.Role == AcademyRole.AcademyAdmin) {
            var grant = await db.StaffGrants.AsNoTracking().Where(x => x.AcademyId == membership.AcademyId && x.MembershipId == membership.MembershipId && x.Permission == requirement.Permission && x.IsActive).Select(x => (bool?)x.Allowed).SingleOrDefaultAsync();
            allowed = grant ?? requirement.Permission is not (AcademyPermissions.ReportsFinancialRead or AcademyPermissions.ReportsAttendanceRead or AcademyPermissions.ReportsExport or AcademyPermissions.OwnerDashboardRead or AcademyPermissions.AcademySettingsManage or AcademyPermissions.PayrollRead or AcademyPermissions.PayrollManage or AcademyPermissions.FacilitiesRead or AcademyPermissions.FacilitiesManage);
            if (requirement.Permission != AcademyPermissions.TenantAccess && await db.AcademyMemberships.AnyAsync(x => x.Id == membership.MembershipId && x.UseActionGrants))
            {
                // Some legacy endpoints reused a broad policy (e.g. news used media).
                // Explicit V2 grants use the actual endpoint module, not that legacy alias.
                // Export is an additional permission, not an alias for the route's
                // read policy. Both requirements must pass for a financial export.
                var module = requirement.Permission == AcademyPermissions.ReportsExport || context.Resource is HttpContext assetRequest && MembershipScope.IsAssetRead(assetRequest.Request.Path.Value ?? "", assetRequest.Request.Method)
                    ? StaffAccessRules.ForPermission(requirement.Permission)
                    : context.Resource is HttpContext request ? StaffAccessRules.ForPath(request.Request.Path.Value ?? "") : null;
                module ??= StaffAccessRules.ForPermission(requirement.Permission);
                allowed = module is not null && await db.StaffActionGrants.AnyAsync(x => x.MembershipId == membership.MembershipId && x.IsActive && x.Module == module && x.Read);
            }
            if (!allowed && requirement.Permission == AcademyPermissions.StructureManage && context.Resource is HttpContext { Request.Method: "GET" } http && http.Request.Path.Value?.TrimEnd('/') == "/api/v1/manage/structure/options")
                allowed = await db.AcademyMemberships.AnyAsync(x => x.Id == membership.MembershipId && x.UseActionGrants)
                    && await db.StaffActionGrants.AnyAsync(x => x.AcademyId == membership.AcademyId && x.MembershipId == membership.MembershipId && x.IsActive && x.Read && (x.Module == "players" || x.Module == "subscriptions" || x.Module == "attendance" || x.Module == "evaluations"));
        }
        if (allowed && (db.SportScopeRestricted || db.AdditionalContextScope) && requirement.Permission is AcademyPermissions.OwnerDashboardRead or AcademyPermissions.ReportsFinancialRead
            or AcademyPermissions.AcademySettingsManage or AcademyPermissions.StaffProvision or AcademyPermissions.PayrollRead or AcademyPermissions.PayrollManage
            or AcademyPermissions.NutritionRead or AcademyPermissions.NutritionManage or AcademyPermissions.MedicalRecordRead or AcademyPermissions.MedicalRecordManage
            or AcademyPermissions.PlayerMediaRead or AcademyPermissions.PlayerMediaManage) allowed = false;
        if (allowed && membership.Role == AcademyRole.Coach && requirement.Permission == AcademyPermissions.AttendanceManage)
            allowed = await db.AcademyExperiences.AnyAsync(x => x.AcademyId == membership.AcademyId && x.CoachAttendancePolicyConfirmed && x.CoachAttendanceEnabled);
        var feature = requirement.Permission switch {
            AcademyPermissions.FacilitiesRead or AcademyPermissions.FacilitiesManage => "facilities",
            AcademyPermissions.AttendanceRead or AcademyPermissions.AttendanceManage or AcademyPermissions.GuardianAttendanceRead or AcademyPermissions.EvaluationRead or AcademyPermissions.EvaluationManage or AcademyPermissions.EvaluationCriteriaManage or AcademyPermissions.GuardianEvaluationRead or AcademyPermissions.SubscriptionPlanManage or AcademyPermissions.SubscriptionRenewManage or AcademyPermissions.GuardianOwnRenewal or AcademyPermissions.GuardianEnrollmentRequestCreate => "training",
            AcademyPermissions.NutritionRead or AcademyPermissions.NutritionManage => "nutrition",
            AcademyPermissions.SportCatalogRead or AcademyPermissions.SportCatalogManage => "catalog",
            AcademyPermissions.MedicalRecordRead or AcademyPermissions.MedicalRecordManage => "medical",
            AcademyPermissions.PlayerMediaRead or AcademyPermissions.PlayerMediaManage => "media",
            AcademyPermissions.PayrollRead or AcademyPermissions.PayrollManage => "payroll",
            _ => null
        };
        if (allowed && feature is not null) allowed = await configuration.Enabled(membership.AcademyId, feature);
        if (allowed) context.Succeed(requirement);
    }
}
