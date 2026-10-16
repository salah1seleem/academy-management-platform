using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Auth;

public static class MembershipScope
{
    public static async Task<Guid[]?> Sports(FoundationDbContext db, TenantMembership t, CancellationToken ct = default)
    {
        if (t.Role != AcademyRole.AcademyAdmin || await db.AcademyMemberships.AsNoTracking().Where(m => m.Id == t.MembershipId && m.AcademyId == t.AcademyId).Select(m => m.AllSports).SingleAsync(ct)) return null;
        return await db.MembershipSportAccesses.AsNoTracking().Where(a => a.AcademyId == t.AcademyId && a.MembershipId == t.MembershipId && a.IsActive
            && db.Sports.Any(s => s.Id == a.SportId && s.AcademyId == t.AcademyId && s.IsActive)).Select(a => a.SportId).ToArrayAsync(ct);
    }
    // Shared academy/staff/child-global records and mixed-sport cash balances need
    // a broader reviewed grant. Fail closed until their narrower workflows exist;
    // a module checkbox must never override this resource boundary.
    public static bool AllowsNarrowSportPath(string path, string method)
    {
        if (path is "/api/v1/me" or "/api/v1/my-permissions" or "/api/v1/my-access" or "/api/v1/academy/features" or "/api/v1/academy/branding/logo") return true;
        if (IsAssetRead(path, method)) return true; // Download rechecks purpose-specific permission.
        if (Academy.Api.Operations.StaffAttendanceExceptions.IsOwnPath(path)) return true; // Own employee only; no staff list, salary or review.
        if (path.StartsWith("/api/v1/manage/staff-attendance", StringComparison.Ordinal) || path.StartsWith("/api/v1/staff/", StringComparison.Ordinal)) return false;
        if (path.StartsWith("/api/v1/people/import", StringComparison.Ordinal)) return false;
        var module = StaffAccessRules.ForPath(path);
        if (module is "staff" or "settings" or "payroll" or "nutrition" or "news" or "medical" or "media") return false;
        if (module == "finance") return path.StartsWith("/api/v1/finance/receivables", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/finance/renewal-opportunities", StringComparison.Ordinal)
            || path.StartsWith("/api/v1/finance/operating-expenses", StringComparison.Ordinal);
        if (module == "reports") return path.StartsWith("/api/v1/reports/attendance", StringComparison.Ordinal);
        if (module == "academy") return path.TrimEnd('/') == "/api/v1/manage/structure/options"
            || path.Contains("/groups", StringComparison.Ordinal) || path.Contains("/sessions", StringComparison.Ordinal)
            || path.Contains("/schedules", StringComparison.Ordinal) || path.Contains("/training-levels", StringComparison.Ordinal);
        // Standalone assets have shared purposes and are not an operational read.
        if (path.Contains("/assets", StringComparison.Ordinal)) return false;
        return module is "players" or "subscriptions" or "payments" or "attendance" or "evaluations" or "catalog" or "matches" or "facilities";
    }
    public static bool IsAssetRead(string path, string method) => method == "GET" && path.StartsWith("/api/v1/assets/", StringComparison.Ordinal) && Guid.TryParse(path.TrimEnd('/').Split('/').Last(), out _);
}
