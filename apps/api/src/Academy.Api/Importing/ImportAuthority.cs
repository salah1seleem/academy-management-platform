using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Importing;

public static class ImportAuthority
{
    public static async Task CheckPlatform(FoundationDbContext db, Guid academy, Guid actor, bool bind = false, bool requireTraining = true)
    {
        // Rechecked by the worker: a queued upload cannot survive revoked platform access.
        if (!await db.PlatformAdministrators.AnyAsync(p => p.UserId == actor && p.IsActive && db.Users.Any(u => u.Id == actor && u.IsActive)) ||
            !await db.Academies.AnyAsync(a => a.Id == academy)) throw new ImportRejected("authority");
        // Setup imports are allowed for a draft academy, but only with an explicit training licence.
        if (requireTraining && !await db.AcademyFeatureEntitlements.IgnoreQueryFilters().AnyAsync(e => e.AcademyId == academy && e.IsActive && e.Feature == "training" && e.Enabled)) throw new ImportRejected("feature");
        if (bind) db.BindRequestScope(academy, actor, Guid.Empty, null);
    }
    public static async Task CheckPlatformCalendar(FoundationDbContext db, Guid academy, Guid jobId)
    {
        if (!db.RequestScopeEnabled || db.RequestAcademyId != academy || db.RequestMembershipId != Guid.Empty || db.Database.CurrentTransaction is null ||
            !await db.AcademyImportJobs.AnyAsync(j => j.Id == jobId && j.AcademyId == academy && j.ActorUserId == db.RequestUserId && j.PlatformInitiated && (j.State == Academy.Infrastructure.People.ImportJobState.Running || j.State == Academy.Infrastructure.People.ImportJobState.RunningV3) && j.StagedFile != null)) throw new ImportRejected("authority");
        await CheckPlatform(db, academy, db.RequestUserId);
    }
    public static async Task CheckConnected(FoundationDbContext db, Guid academy, Guid membership)
    {
        var m = await db.AcademyMemberships.SingleOrDefaultAsync(m=>m.Id==membership&&m.AcademyId==academy&&m.IsActive);
        if (m?.Role == AcademyRole.AcademyOwner) return;
        if (m is null || m.Role != AcademyRole.AcademyAdmin || !m.AllBranches || !m.AllSports || !m.UseActionGrants) throw new ImportRejected("authority");
        var modules = new[] { "academy", "staff", "players", "subscriptions", "matches" };
        if (await db.StaffActionGrants.CountAsync(g=>g.MembershipId==membership&&g.IsActive&&g.Read&&g.Add&&modules.Contains(g.Module)) != modules.Length) throw new ImportRejected("authority");
    }
    // Called again in the worker, under the same lock used to revoke staff grants.
    public static async Task<Guid[]?> Check(FoundationDbContext db, Guid academy, Guid actor, Guid membership, bool balances, bool bind = false)
    {
        var m = await db.AcademyMemberships.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == membership && x.AcademyId == academy && x.UserId == actor && x.IsActive && x.User.IsActive && x.Academy.IsActive);
        if (m is null || m.PrimaryMembershipId.HasValue || m.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin)) throw new ImportRejected("authority");
        if (m.Role == AcademyRole.AcademyAdmin)
        {
            // A queued job must not bypass a newly narrowed sport grant. The
            // current import format provisions shared identities; keep it owner/
            // all-sport only until its shared-record review is implemented.
            if (!m.AllSports) throw new ImportRejected("authority");
            var grants = await db.StaffActionGrants.IgnoreQueryFilters().Where(g => g.AcademyId == academy && g.MembershipId == membership && g.IsActive && g.Read && g.Add).Select(g => g.Module).ToArrayAsync();
            if (!m.UseActionGrants || !grants.Contains("players") || !grants.Contains("subscriptions") || balances && !grants.Contains("finance")) throw new ImportRejected("authority");
        }
        if (!await new AcademyConfigurationService(db, TimeProvider.System).Enabled(academy, "training")) throw new ImportRejected("feature");
        Guid[]? branches = m.Role == AcademyRole.AcademyAdmin && !m.AllBranches
            ? await db.MembershipBranchAccesses.IgnoreQueryFilters().Where(x => x.AcademyId == academy && x.MembershipId == membership && x.IsActive && db.Branches.IgnoreQueryFilters().Any(b => b.AcademyId == academy && b.Id == x.BranchId && b.IsActive)).Select(x => x.BranchId).ToArrayAsync() : null;
        if (bind) db.BindRequestScope(academy, actor, membership, branches);
        return branches;
    }
}
public sealed class ImportRejected(string code, Exception? inner = null) : Exception(code, inner)
{
    public string Code { get; } = code;
    public string ArabicMessage => Code switch {
        "authority" => "تغيّرت صلاحيات الاستيراد أو الفروع. يلزم قراءة وإضافة اللاعبين والاشتراكات، وإذن مالي للأرصدة.",
        "feature" => "خدمة التدريب غير متاحة في إعدادات الأكاديمية الحالية.",
        "stale" => "تغيّرت المراجع أو الأسعار أو السعة. أعد تنزيل القالب والمعاينة قبل الاعتماد.",
        "identity" => "توجد هوية أو علاقة ولي أمر تحتاج مراجعة صريحة. لم تُنشر الدفعة ولم ندمج بالهاتف.",
        "conflict" => "أحد أكواد المصدر مستخدم ببيانات مختلفة. لا نغيّر الاشتراك التاريخي تلقائيًا.",
        "resource" => "راجع اعتماد سعة المجموعة الخاصة ومستواها وحصصها وحجوزات مواردها. لم تُنشر الدفعة.",
        "expired" => "انتهت مدة الملف المؤقت. ارفع الملف وأعد المعاينة.",
        _ => "تعذر اعتماد الدفعة كاملة. لم تُنشر بيانات جزئية؛ راجع الملف وأعد المعاينة."
    };
}
