using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static partial class GuardianFeedbackEndpoints
{
    private static async Task<IQueryable<GuardianFeedback>> InvestigationScope(FoundationDbContext db, Guid academy, Guid member)
    {
        // Independent, bounded identity lookup for DENIAL only. Do not compose
        // IgnoreQueryFilters into the resource query (EF can widen its roots).
        var user = await db.AcademyMemberships.IgnoreQueryFilters().Where(m => m.AcademyId == academy && m.Id == member).Select(m => m.UserId).SingleOrDefaultAsync();
        var samePerson = await db.AcademyMemberships.IgnoreQueryFilters().Where(m => m.AcademyId == academy && m.UserId == user).Select(m => m.Id).ToArrayAsync();
        return db.GuardianFeedbacks.Where(f =>
        f.AcademyId == academy && f.IsActive && f.Kind != "Rating" && db.TrainingGroups.Any(g => g.Id == f.TrainingGroupId && g.IsActive && g.BranchId == f.BranchId && g.Branch.IsActive && g.Sport.IsActive
        && db.AcademyMemberships.Any(m => m.AcademyId == academy && m.Id == member && m.IsActive && m.User.IsActive && m.Role == AcademyRole.AcademyAdmin && m.UseActionGrants
            && m.UserId != f.GuardianUserId && (!f.CoachMembershipId.HasValue || !samePerson.Contains(f.CoachMembershipId.Value))
            && (!m.PrimaryMembershipId.HasValue || db.AcademyMemberships.Any(p => p.Id == m.PrimaryMembershipId && p.IsActive && p.User.IsActive))
            && (m.AllBranches || db.MembershipBranchAccesses.Any(a => a.MembershipId == member && a.IsActive && a.BranchId == g.BranchId))
            && (m.AllSports || db.MembershipSportAccesses.Any(a => a.MembershipId == member && a.IsActive && a.SportId == g.SportId)))));
    }
    private static bool InvestigationOpen(GuardianFeedback f) => f.Status is not ("Resolved" or "Closed");
    private static async Task<IResult> Investigators(Guid id, CurrentTenant tenant, FoundationDbContext db, HttpContext h)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!;
        if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        if (!await db.GuardianFeedbacks.AnyAsync(f => f.Id == id && f.IsActive)) return Results.NotFound();
        var candidates = await db.AcademyMemberships.Where(m => m.Role == AcademyRole.AcademyAdmin && m.IsActive).OrderBy(m => m.User.DisplayName).Select(m => new { m.Id, name = m.User.DisplayName }).ToArrayAsync();
        var allowed = new List<object>();
        foreach (var m in candidates) if (await OwnerFollowUpEndpoints.DelegateAllowed(db, t.AcademyId, m.Id, true) && await (await InvestigationScope(db, t.AcademyId, m.Id)).AnyAsync(f => f.Id == id)) allowed.Add(m);
        return Results.Ok(new { items = allowed });
    }
    private static async Task<IResult> AssignInvestigation(Guid id, FeedbackAssignmentWrite b, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        var brief = b.Brief?.Trim();
        if (b.RequestId == Guid.Empty || b.Version == 0 || b.InvestigatorMembershipId.HasValue && (brief?.Length is not (>= 5 and <= 500)) || !b.InvestigatorMembershipId.HasValue && !string.IsNullOrEmpty(brief))
            return Results.UnprocessableEntity(new { message = "اختر مسؤولًا وموجزًا من 5 إلى 500 حرف دون هوية الأسرة، أو ألغِ الإسناد دون موجز." });
        await using var tx = await db.Database.BeginTransactionAsync();
        foreach (var member in new[] { t.MembershipId, b.InvestigatorMembershipId ?? t.MembershipId }.Distinct().Order())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{member}"},0))");
        if (!await LockOwner(db, t)) return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"feedback-case-request:{t.AcademyId}:{t.MembershipId}:{b.RequestId}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"guardian-feedback-resolve:{t.AcademyId}:{id}"},0))");
        var f = await db.GuardianFeedbacks.SingleOrDefaultAsync(f => f.Id == id && f.IsActive); if (f == null) return Results.NotFound();
        if (b.InvestigatorMembershipId is Guid assignee && (!await OwnerFollowUpEndpoints.DelegateAllowed(db, t.AcademyId, assignee, true) || !await (await InvestigationScope(db, t.AcademyId, assignee)).AnyAsync(x => x.Id == id)))
            return Results.UnprocessableEntity(new { message = "المسؤول غير مخول ضمن الفرع والرياضة، أو هو طرف في الملاحظة. لا تُمنح صلاحيات تلقائيًا." });
        var hash = InvestigationHash(new { id, b.Version, b.InvestigatorMembershipId, brief });
        var prior = await db.FeedbackChanges.SingleOrDefaultAsync(c => c.ActorMembershipId == t.MembershipId && c.RequestId == b.RequestId);
        if (prior != null) return prior.FeedbackId == id && prior.Action == "Assign" && prior.RequestHash == hash ? Results.Ok(new { id, replay = true }) : Results.Conflict(new { message = "مفتاح المحاولة مستخدم لتحديث مختلف." });
        if (f.Version != b.Version || !InvestigationOpen(f) || f.Kind == "Rating") return Results.Conflict(new { message = "تغيرت الملاحظة أو لم تعد قابلة للإسناد؛ حدّث التفاصيل." });
        f.InvestigatorMembershipId = b.InvestigatorMembershipId; f.InvestigationId = b.InvestigatorMembershipId.HasValue ? Guid.NewGuid() : null; f.InvestigationBrief = b.InvestigatorMembershipId.HasValue ? brief : null;
        var now = clock.GetUtcNow(); f.UpdatedAtUtc = now;
        var change = RecordChange(db, f, t, now, "Assign", f.Status, f.DueOn, f.InvestigationBrief, b.RequestId, hash);
        change.InvestigationId = f.InvestigationId; change.InvestigatorMembershipId = f.InvestigatorMembershipId;
        try { await db.SaveChangesAsync(); db.FeedbackChanges.Add(change); await db.SaveChangesAsync(); await tx.CommitAsync(); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { message = "تغيرت الملاحظة؛ حدّث التفاصيل قبل الإسناد." }); }
        return Results.Ok(new { id });
    }
    private static string InvestigationHash(object b) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(b))));
    private static async Task<IResult> MyInvestigations(CurrentTenant tenant, FoundationDbContext db, HttpContext h)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!;
        if (!await OwnerFollowUpEndpoints.DelegateAllowed(db, t.AcademyId, t.MembershipId, false)) return Results.Forbid();
        var items = await (await InvestigationScope(db, t.AcademyId, t.MembershipId)).Where(f => f.InvestigatorMembershipId == t.MembershipId && f.InvestigationId != null && f.Status != "Resolved" && f.Status != "Closed")
            .OrderBy(f => f.DueOn == null).ThenBy(f => f.DueOn).ThenByDescending(f => f.UpdatedAtUtc).Take(101)
            .Select(f => new { assignmentId = f.InvestigationId, brief = f.InvestigationBrief, f.Status, f.DueOn }).ToArrayAsync();
        return Results.Ok(new { items = items.Take(100), hasMore = items.Length > 100 });
    }
    private static async Task<IResult> InvestigationDetail(Guid assignmentId, CurrentTenant tenant, FoundationDbContext db, HttpContext h)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!;
        if (!await OwnerFollowUpEndpoints.DelegateAllowed(db, t.AcademyId, t.MembershipId, false)) return Results.Forbid();
        var f = await (await InvestigationScope(db, t.AcademyId, t.MembershipId)).AsNoTracking().SingleOrDefaultAsync(f => f.InvestigationId == assignmentId && f.InvestigatorMembershipId == t.MembershipId);
        if (f == null || !InvestigationOpen(f)) return Results.NotFound();
        var findings = await db.FeedbackChanges.Where(c => c.FeedbackId == f.Id && c.InvestigationId == assignmentId && c.ActorMembershipId == t.MembershipId && c.Action == "Findings").OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.Id).Take(20).Select(c => new { c.Id, note = c.InternalNote, c.CreatedAtUtc }).ToArrayAsync();
        // Explicit projection: no case/player/guardian/group/coach identifiers, raw text, public reply or owner history.
        return Results.Ok(new { assignmentId, f.Version, brief = f.InvestigationBrief, f.Status, f.DueOn, findings, canEdit = await OwnerFollowUpEndpoints.DelegateAllowed(db, t.AcademyId, t.MembershipId, true) });
    }
    private static async Task<IResult> SubmitFindings(Guid assignmentId, FeedbackFindingsWrite b, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!;
        var note = b.Note?.Trim(); if (b.RequestId == Guid.Empty || b.Version == 0 || note?.Length is not (>= 5 and <= 500)) return Results.UnprocessableEntity(new { message = "اكتب نتيجة التحقيق من 5 إلى 500 حرف." });
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"},0))");
        if (!await OwnerFollowUpEndpoints.DelegateAllowed(db, t.AcademyId, t.MembershipId, true) || !await h.RequestServices.GetRequiredService<Academy.Api.Configuration.AcademyConfigurationService>().Enabled(t.AcademyId, "training")) return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"feedback-case-request:{t.AcademyId}:{t.MembershipId}:{b.RequestId}"},0))");
        var f = await (await InvestigationScope(db, t.AcademyId, t.MembershipId)).SingleOrDefaultAsync(f => f.InvestigationId == assignmentId && f.InvestigatorMembershipId == t.MembershipId); if (f == null) return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"guardian-feedback-resolve:{t.AcademyId}:{f.Id}"},0))");
        await db.Entry(f).ReloadAsync();
        if (f.InvestigationId != assignmentId || f.InvestigatorMembershipId != t.MembershipId || !InvestigationOpen(f) || !await (await InvestigationScope(db, t.AcademyId, t.MembershipId)).AnyAsync(x => x.Id == f.Id)) return Results.NotFound();
        var hash = InvestigationHash(new { assignmentId, b.Version, note }); var prior = await db.FeedbackChanges.SingleOrDefaultAsync(c => c.ActorMembershipId == t.MembershipId && c.RequestId == b.RequestId);
        if (prior != null) return prior.InvestigationId == assignmentId && prior.Action == "Findings" && prior.RequestHash == hash ? Results.Ok(new { assignmentId, replay = true }) : Results.Conflict(new { message = "مفتاح المحاولة مستخدم لنتيجة أخرى." });
        if (f.Version != b.Version) return Results.Conflict(new { message = "تغير التكليف؛ حدّثه قبل حفظ النتيجة." });
        var now = clock.GetUtcNow(); f.UpdatedAtUtc = now; db.Entry(f).Property(x => x.UpdatedAtUtc).IsModified = true;
        var change = RecordChange(db, f, t, now, "Findings", f.Status, f.DueOn, note, b.RequestId, hash); change.InvestigationId = assignmentId; change.InvestigatorMembershipId = t.MembershipId;
        try { await db.SaveChangesAsync(); db.FeedbackChanges.Add(change); await db.SaveChangesAsync(); await tx.CommitAsync(); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { message = "تغير التكليف؛ حدّثه قبل حفظ النتيجة." }); }
        return Results.Ok(new { assignmentId });
    }
}
public sealed record FeedbackAssignmentWrite(Guid? InvestigatorMembershipId, string? Brief, uint Version, Guid RequestId);
public sealed record FeedbackFindingsWrite(string? Note, uint Version, Guid RequestId);
