using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Facilities;
internal static class SwimmingPolicyGate
{
    public static async Task<bool> AllowsSyntheticPoolSeed(FoundationDbContext db, Guid sportId, Guid branchId, string service)
    {
        try { await Require(db, sportId, branchId, FacilitySpaceKind.Pool, service); return true; }
        catch (FacilityRejected e) when (e.Status == 403) { return false; }
    }
    // Shared by reservations, timetable generation, entry and observations.
    // No Arabic-name inference and no customer/global defaults.
    public static async Task Require(FoundationDbContext db, Guid sportId, Guid branchId, FacilitySpaceKind spaceKind, string? service = null, Guid? groupId = null)
    {
        if (spaceKind != FacilitySpaceKind.Pool)
        {
            if (service == "OpenSwim") throw new FacilityRejected("السباحة الحرة تحتاج مسبحًا ورياضة سباحة مفعلة.", 422);
            return;
        }
        if (!await db.Sports.AnyAsync(x => x.Id == sportId && x.Kind == SportKind.Swimming && x.IsActive)) throw new FacilityRejected("المسبح يتطلب رياضة من نوع السباحة؛ راجع خصائص الرياضات.", 403);
        var policy = await db.SwimmingBranchPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.SportId == sportId && x.BranchId == branchId && x.IsActive);
        if (policy?.Enabled != true || service == "OpenSwim" && !policy.OpenSwimEnabled || service == "DayPass" && !policy.DayPassEnabled) throw new FacilityRejected("خدمة السباحة غير مفعلة لهذا الفرع؛ راجع إعدادات السباحة.", 403);
        if (service == "Training" && groupId.HasValue && !policy.PrivateTrainingEnabled && await db.TrainingGroups.AnyAsync(x => x.Id == groupId && x.RegistrationFormat != TrainingRegistrationFormat.Group)) throw new FacilityRejected("التدريب الخاص غير مفعل في إعدادات السباحة لهذا الفرع.", 403);
    }
    public static async Task ForResource(FoundationDbContext db, Guid resourceId, string service, Guid? groupId = null)
    {
        var space = await (from r in db.FacilityResources join s in db.FacilitySpaces on r.FacilitySpaceId equals s.Id where r.Id == resourceId select s).SingleOrDefaultAsync() ?? throw new FacilityRejected("المورد غير متاح.", 404);
        await Require(db, space.SportId, space.BranchId, space.Kind, service, groupId);
    }
}
