using Academy.Api.Auth;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class NewsAudience
{
    public static readonly string[] Roles = ["AcademyAdmin", "Coach", "Guardian"];
    public static readonly string[] Kinds = ["News", "Event", "Announcement"];
    public static IQueryable<AcademyNews> Published(FoundationDbContext db, Guid academy, DateTimeOffset now) =>
        db.AcademyNews.Where(n => n.AcademyId == academy && n.IsActive && n.PublishedAtUtc != null && n.PublishedAtUtc <= now &&
            (n.StartsAtUtc ?? n.PublishedAtUtc) <= now && (n.EndsAtUtc ?? n.PublishedAtUtc.Value.AddDays(n.VisibleDays)) > now);

    public static async Task<IQueryable<AcademyNews>> Feed(FoundationDbContext db, TenantMembership t, Guid user, DateTimeOffset now)
    {
        var q = Published(db, t.AcademyId, now);
        if (t.Role == AcademyRole.AcademyOwner) return q;
        q = q.Where(n => n.AudienceRoles.Contains(t.Role.ToString()));
        Guid[]? branches;
        if (t.Role == AcademyRole.Guardian)
            branches = await db.SportEnrollments.Where(e => e.AcademyId == t.AcademyId && e.IsActive && e.Player.IsActive &&
                db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.PlayerId == e.PlayerId && l.IsActive && l.Guardian.IsActive && l.Guardian.UserId == user))
                .Select(e => e.BranchId).Distinct().ToArrayAsync();
        else if (t.Role == AcademyRole.Coach)
            branches = await db.StaffGroupAssignments.Where(a => a.AcademyId == t.AcademyId && a.AcademyMembershipId == t.MembershipId && a.IsActive && a.TrainingGroup.IsActive)
                .Select(a => a.TrainingGroup.BranchId).Distinct().ToArrayAsync();
        else if (t.Role == AcademyRole.AcademyAdmin)
            branches = await db.AcademyMemberships.AnyAsync(m => m.Id == t.MembershipId && m.AllBranches) ? null :
                await db.MembershipBranchAccesses.Where(a => a.AcademyId == t.AcademyId && a.MembershipId == t.MembershipId && a.IsActive).Select(a => a.BranchId).ToArrayAsync();
        else return q.Where(_ => false);
        return branches is null ? q : q.Where(n => n.TargetBranchIds.Length == 0 || n.TargetBranchIds.Any(b => branches.Contains(b)));
    }

    public static IQueryable<AcademyNews> Managed(FoundationDbContext db, Guid academy) => db.BranchScopeRestricted
        ? db.AcademyNews.Where(n => n.AcademyId == academy && n.TargetBranchIds.Length > 0 && n.TargetBranchIds.All(b => db.RequestBranchIds.Contains(b)))
        : db.AcademyNews.Where(n => n.AcademyId == academy);

    public static async Task<bool> ManageAllowed(FoundationDbContext db, TenantMembership t, string operation)
    {
        if (t.Role == AcademyRole.AcademyOwner) return true;
        if (t.Role != AcademyRole.AcademyAdmin || db.SportScopeRestricted || t.AdditionalContext) return false;
        var member = await db.AcademyMemberships.SingleAsync(m => m.Id == t.MembershipId && m.AcademyId == t.AcademyId);
        if (!member.UseActionGrants && await db.StaffGrants.AnyAsync(g => g.MembershipId == t.MembershipId && g.IsActive && g.Permission == AcademyPermissions.PlayerMediaManage && !g.Allowed)) return false;
        if (operation != "Read" && !await StaffWorkflowAccess.Allows(db, t.MembershipId, "/api/v1/content/news/", operation == "Add" ? "POST" : operation == "Delete" ? "DELETE" : "PUT")) return false;
        return !member.UseActionGrants || await db.StaffActionGrants.AnyAsync(g => g.MembershipId == t.MembershipId && g.IsActive && g.Module == "news" && g.Read &&
            (operation == "Read" || operation == "Add" && g.Add || operation == "Edit" && g.Edit || operation == "Delete" && g.Delete));
    }

    public static string State(AcademyNews n, DateTimeOffset now) => !n.IsActive ? "Archived" : n.PublishedAtUtc == null ? "Draft" :
        (n.StartsAtUtc ?? n.PublishedAtUtc) > now ? "Scheduled" : (n.EndsAtUtc ?? n.PublishedAtUtc.Value.AddDays(n.VisibleDays)) <= now ? "Expired" : "Published";
}
