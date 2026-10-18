using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public static partial class OwnerFollowUpEndpoints
{
    public static Task<bool> DelegateAllowed(FoundationDbContext db, Guid academy, Guid member, bool edit) => db.AcademyMemberships.AsNoTracking().AnyAsync(m => m.AcademyId == academy && m.Id == member && m.Role == AcademyRole.AcademyAdmin && m.IsActive && m.User.IsActive && m.UseActionGrants
        && (!m.PrimaryMembershipId.HasValue || db.AcademyMemberships.Any(p => p.Id == m.PrimaryMembershipId && p.IsActive && p.User.IsActive))
        && db.StaffActionGrants.Any(g => g.AcademyId == academy && g.MembershipId == member && g.IsActive && g.Module == "followups" && g.Read && (!edit || g.Edit)));
    private static async Task<bool> CanOpenGroup(FoundationDbContext db, TenantMembership t) => await db.StaffActionGrants.AnyAsync(g => g.MembershipId == t.MembershipId && g.IsActive && g.Module == "academy" && g.Read);
    private static async Task<IResult> MyList(CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock, DateOnly? from, DateOnly? to, string? state, bool allDates = false)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!; var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        if (!allDates && from == null && to == null) { from = new(today.Year, today.Month, 1); to = today; }
        if (from > to || state is not (null or "" or "Open") && !States.Contains(state)) return Invalid("راجع الفترة وحالة المتابعة.");
        var q = db.OwnerFollowUps.AsNoTracking().Where(f => f.AssignedToMembershipId == t.MembershipId && db.TrainingGroups.Any(g => g.Id == f.TrainingGroupId)
            && (!from.HasValue || f.CreatedOn >= from) && (!to.HasValue || f.CreatedOn <= to));
        if (state == "Open") q = q.Where(f => f.State != "Closed" && f.State != "Rejected"); else if (!string.IsNullOrEmpty(state)) q = q.Where(f => f.State == state);
        var rows = await q.OrderBy(f => f.DueOn == null).ThenBy(f => f.DueOn).ThenByDescending(f => f.UpdatedAtUtc).ThenBy(f => f.Id).Take(101).ToArrayAsync();
        var ids = rows.Select(x => x.TrainingGroupId).Distinct().ToArray();
        // Do not compute signals or disclose names for unassigned groups.
        var sources = new Dictionary<Guid, GroupSignal>(); foreach (var id in ids) { var s = (await Signals(db, t.AcademyId, today, id, clock.GetUtcNow())).SingleOrDefault(); if (s != null) sources[id] = s; }
        return Results.Ok(new { from, to, asOfDate = today, canEdit = await DelegateAllowed(db, t.AcademyId, t.MembershipId, true), canOpenSource = await CanOpenGroup(db, t), limit = 100, hasMore = rows.Length > 100, items = rows.Take(100).Select(f => Row(f, sources.GetValueOrDefault(f.TrainingGroupId), today)) });
    }
    private static async Task<IResult> MyDetails(Guid id, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!;
        var f = await db.OwnerFollowUps.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id && f.AssignedToMembershipId == t.MembershipId && db.TrainingGroups.Any(g => g.Id == f.TrainingGroupId)); if (f == null) return Results.NotFound();
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        var history = await db.OwnerFollowUpChanges.Where(c => c.FollowUpId == id).OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id).Select(c => new { c.Id, c.CreatedAtUtc, after = c.AfterJson }).ToArrayAsync();
        return Results.Ok(new { item = Row(f, (await Signals(db, t.AcademyId, today, f.TrainingGroupId, clock.GetUtcNow())).SingleOrDefault(), today), history, canEdit = f.State is not ("Closed" or "Rejected") && await DelegateAllowed(db, t.AcademyId, t.MembershipId, true), canOpenSource = await CanOpenGroup(db, t) });
    }
    private static async Task<IResult> MyChange(Guid id, AssignedFollowUpWrite b, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"},0))");
        if (!await DelegateAllowed(db, t.AcademyId, t.MembershipId, true) || !await h.RequestServices.GetRequiredService<Academy.Api.Configuration.AcademyConfigurationService>().Enabled(t.AcademyId, "training")) return Results.Forbid();
        if (b.RequestId == Guid.Empty || b.State is not ("InProgress" or "Snoozed" or "Closed" or "Rejected") || b.Note?.Length > 500 || b.ResolutionReason?.Length > 500 || b.State is "Closed" or "Rejected" && string.IsNullOrWhiteSpace(b.ResolutionReason)) return Invalid("اختر حالة صحيحة، واذكر سبب الإغلاق أو الرفض، بحد أقصى 500 حرف.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"followup-change:{t.AcademyId}:{t.MembershipId}:{b.RequestId}"},0))");
        var f = await db.OwnerFollowUps.SingleOrDefaultAsync(f => f.Id == id && f.AssignedToMembershipId == t.MembershipId && db.TrainingGroups.Any(g => g.Id == f.TrainingGroupId)); if (f == null) return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"followup-source:{t.AcademyId}:{f.TrainingGroupId}:{f.Condition}"},0))"); await db.Entry(f).ReloadAsync();
        // Assignment/scope may have changed while waiting; recheck before replay as well.
        var source = (await Signals(db, t.AcademyId, await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock), f.TrainingGroupId, clock.GetUtcNow())).SingleOrDefault();
        if (f.AssignedToMembershipId != t.MembershipId || source == null || !await EligibleOwner(db, t.AcademyId, t.MembershipId, source)) return Results.NotFound();
        var hash = Hash(new { id, b }); var prior = await db.OwnerFollowUpChanges.SingleOrDefaultAsync(c => c.ActorMembershipId == t.MembershipId && c.RequestId == b.RequestId);
        if (prior != null) return prior.FollowUpId == id && prior.RequestHash == hash ? Results.Ok(new { id, replay = true }) : Results.Conflict(new { message = "مفتاح المحاولة مستخدم لبيانات أخرى." });
        if (f.Version != b.Version || f.State is "Closed" or "Rejected") return Results.Conflict(new { message = "تغيرت المتابعة أو أغلقت؛ حدّث التفاصيل. إعادة الفتح والإسناد للمالك فقط." });
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock); if (b.State == "Snoozed" && (!b.DueOn.HasValue || b.DueOn <= today)) return Invalid("اختر موعد متابعة لاحقًا لليوم عند التأجيل.");
        var before = JsonSerializer.Serialize(Snapshot(f)); var now = clock.GetUtcNow(); f.State = b.State; f.DueOn = b.DueOn; f.Note = b.Note?.Trim(); f.ResolutionReason = b.State is "Closed" or "Rejected" ? b.ResolutionReason?.Trim() : null; f.UpdatedAtUtc = now;
        try
        {
            // Persist the task before its immutable history: the database guard verifies
            // the history snapshot against the updated row. Both saves remain atomic.
            await db.SaveChangesAsync();
            db.OwnerFollowUpChanges.Add(new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, FollowUpId = id, ActorMembershipId = t.MembershipId, RequestId = b.RequestId, RequestHash = hash, BeforeJson = before, AfterJson = JsonSerializer.Serialize(Snapshot(f)), CreatedAtUtc = now, UpdatedAtUtc = now });
            await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { message = "تغيرت المتابعة؛ حدّث التفاصيل." }); }
        return Results.Ok(new { id });
    }
}
public sealed record AssignedFollowUpWrite(string State, uint Version, Guid RequestId, DateOnly? DueOn = null, string? Note = null, string? ResolutionReason = null);
