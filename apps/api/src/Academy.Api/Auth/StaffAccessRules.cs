namespace Academy.Api.Auth;

public static class StaffAccessRules
{
    public static readonly string[] Modules = ["academy", "players", "subscriptions", "payments", "attendance", "evaluations", "catalog", "nutrition", "medical", "media", "finance", "reports", "payroll", "settings", "staff", "news", "matches", "facilities"];
    public static string? ForPermission(string permission) => permission switch {
        AcademyPermissions.FacilitiesRead or AcademyPermissions.FacilitiesManage => "facilities",
        AcademyPermissions.StructureManage or AcademyPermissions.CoachGroupsRead => "academy",
        AcademyPermissions.PeopleManage => "players",
        AcademyPermissions.SubscriptionPlanManage or AcademyPermissions.SubscriptionRead or AcademyPermissions.SubscriptionRenewManage or AcademyPermissions.SubscriptionAdjustmentManage or AcademyPermissions.SubscriptionDiscountManage => "subscriptions",
        AcademyPermissions.PaymentRead or AcademyPermissions.CollectionRead or AcademyPermissions.EnrollmentRequestRead or AcademyPermissions.EnrollmentRequestManage => "payments",
        AcademyPermissions.AttendanceRead or AcademyPermissions.AttendanceManage => "attendance",
        AcademyPermissions.EvaluationRead or AcademyPermissions.EvaluationManage or AcademyPermissions.EvaluationCriteriaManage => "evaluations",
        AcademyPermissions.SportCatalogRead or AcademyPermissions.SportCatalogManage => "catalog",
        AcademyPermissions.NutritionRead or AcademyPermissions.NutritionManage => "nutrition",
        AcademyPermissions.MedicalRecordRead or AcademyPermissions.MedicalRecordManage => "medical",
        AcademyPermissions.PlayerMediaRead or AcademyPermissions.PlayerMediaManage => "media",
        AcademyPermissions.ReportsFinancialRead or AcademyPermissions.OwnerDashboardRead => "finance",
        AcademyPermissions.ReportsAttendanceRead or AcademyPermissions.ReportsExport => "reports",
        AcademyPermissions.PayrollRead or AcademyPermissions.PayrollManage => "payroll",
        AcademyPermissions.AcademySettingsManage => "settings",
        AcademyPermissions.StaffProvision => "staff",
        _ => null
    };
    public static string? ForPath(string path)
    {
        path = path.TrimEnd('/');
        if (path.StartsWith("/api/v1/manage/family-renewals", StringComparison.Ordinal)) return "payments";
        if (path.StartsWith("/api/v1/facilities")) return "facilities";
        if (path.StartsWith("/api/v1/manage/training-levels")) return "academy";
        if (path.StartsWith("/api/v1/finance/")) return "finance";
        if (path.StartsWith("/api/v1/people/players/") && (path.EndsWith("/refunds") || path.Contains("/refunds/"))) return "finance";
        if (path.StartsWith("/api/v1/reports/owner-summary") || path.StartsWith("/api/v1/reports/financial")) return "finance";
        if (path.StartsWith("/api/v1/reports/")) return "reports";
        if (path.Contains("/payroll") || path.Contains("/employees")) return "payroll";
        if (path.Contains("/attendance")) return "attendance";
        if (path.Contains("/staff") || path == "/api/v1/staff") return "staff";
        if (path.Contains("/academy/experience") || path.Contains("/academy/policy-audit") || path.Contains("/configuration") || path.Contains("/payment-settings")) return "settings";
        if (path.Contains("/nutrition")) return "nutrition";
        if (path.Contains("/medical")) return "medical";
        if (path.Contains("/catalog") || path.Contains("/product-sales")) return "catalog";
        if (path.Contains("/media") || path.Contains("/assets")) return "media";
        if (path.Contains("/news")) return "news";
        if (path.Contains("/matches") || path.Contains("/tournament")) return "matches";
        if (path.Contains("/evaluation")) return "evaluations";
        if (path.Contains("/attendance")) return "attendance";
        if (path.Contains("/payment") || path.Contains("/enrollment-request") || path.Contains("/collections")) return "payments";
        if (path.Contains("/subscription") || path.Contains("/renewal") || path.Contains("/discount")) return "subscriptions";
        if (path.Contains("/people")) return "players";
        if (path.Contains("/structure") || path.Contains("/branches") || path.Contains("/sports") || path.Contains("/age-categories") || path.Contains("/groups") || path.Contains("/coaches") || path.Contains("/sessions") || path.Contains("/schedules")) return "academy";
        return null;
    }
    public static string Operation(string method, string path)
    {
        if (method == "POST" && path.StartsWith("/api/v1/attendance/sessions/") && path.EndsWith("/corrections")) return "Edit";
        if (method == "POST" && path.StartsWith("/api/v1/manage/structure/sessions/") && path.EndsWith("/coach-override/preview")) return "Read";
        if (method == "POST" && path.StartsWith("/api/v1/facilities/visits/") && path.EndsWith("/exit")) return "Edit";
        // Timetable service rechecks Add for the first schedule or Edit for an existing one under locks.
        if (method is "GET" or "HEAD" || path.EndsWith("/collection-result") || path.EndsWith("/quote") || path.EndsWith("registration-quote") || path.EndsWith("/search") || path.EndsWith("/resolve") || path.EndsWith("/timetable/preview") || path.EndsWith("/timetable") || path.EndsWith("/placement/preview") || path.EndsWith("/placement") || path.EndsWith("/placement-recommendations")) return "Read";
        if (method == "DELETE" || path.EndsWith("/delete")) return "Delete";
        if (method is "PUT" or "PATCH") return "Edit";
        var last = path.TrimEnd('/').Split('/').Last();
        if (last is "review" or "confirm" or "approve" or "reject" or "publish" or "cancel" or "freeze" or "resume" or "proof" or "transition" || Guid.TryParse(last, out _)) return "Edit";
        return "Add";
    }
}
