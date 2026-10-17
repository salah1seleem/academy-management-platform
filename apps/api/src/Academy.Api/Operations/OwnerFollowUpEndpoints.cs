using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class OwnerFollowUpEndpoints
{
    private static readonly string[] States = ["New", "Assigned", "InProgress", "Snoozed", "Closed", "Rejected"];
    public static void MapOwnerFollowUps(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/manage/owner-followups").RequireAuthorization(AcademyPermissions.OwnerDashboardRead);
        g.MapGet("/options", Options); g.MapGet("/", List); g.MapGet("/{id:guid}", Details);
        g.MapPost("/", Create).AddEndpointFilter<CsrfFilter>();
        g.MapPost("/{id:guid}", Change).AddEndpointFilter<CsrfFilter>();
    }
    private static IResult Invalid(string message) => Results.UnprocessableEntity(new { message });
    private static string Hash(object body) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body))));
    private static async Task<bool> Owner(CurrentTenant tenant, FoundationDbContext db, HttpContext http, Guid? assignee = null, bool write = false)
    {
        var t = (await tenant.ResolveAsync())!;
        http.Response.Headers.CacheControl = "private, no-store";
        if (write)
            foreach (var id in new[] { t.MembershipId, assignee ?? t.MembershipId }.Distinct().Order())
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{id}"},0))");
        return t.Role == AcademyRole.AcademyOwner && await http.RequestServices.GetRequiredService<Academy.Api.Configuration.AcademyConfigurationService>().Enabled(t.AcademyId, "training") && await db.AcademyMemberships.AnyAsync(m => m.Id == t.MembershipId && m.AcademyId == t.AcademyId && m.IsActive && m.User.IsActive && m.Role == AcademyRole.AcademyOwner)
            && (await http.RequestServices.GetRequiredService<IAuthorizationService>().AuthorizeAsync(http.User, http, AcademyPermissions.OwnerDashboardRead)).Succeeded;
    }
    internal sealed record GroupSignal(Guid GroupId, Guid BranchId, Guid SportId, string Name, string Branch, string Sport, int Enrollments, int? Capacity, bool HasCoach, bool SourceActive)
    {
        public bool Applies(string condition) => SourceActive && (condition == "GroupCoachMissing" ? !HasCoach : condition == "GroupLowOccupancy" && Capacity > 0 && Enrollments * 2 < Capacity);
    }
    internal static async Task<GroupSignal[]> Signals(FoundationDbContext db, Guid academy, DateOnly today, Guid? groupId = null)
    {
        return await db.TrainingGroups.Where(g => g.AcademyId == academy && (!groupId.HasValue || g.Id == groupId))
            .OrderBy(g => g.ArabicName).Select(g => new GroupSignal(g.Id, g.BranchId, g.SportId, g.ArabicName, g.Branch.ArabicName, g.Sport.ArabicName,
                db.SportEnrollments.Count(e => e.AcademyId == academy && e.IsActive && e.Player.IsActive && e.Status == EnrollmentStatus.Active && FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, today) == g.Id), g.Capacity,
                db.StaffGroupAssignments.Any(a => a.TrainingGroupId == g.Id && a.IsActive && a.AcademyMembership.IsActive && a.AcademyMembership.User.IsActive && a.AcademyMembership.Role == AcademyRole.Coach
                    && (a.AcademyMembership.AllBranches || db.MembershipBranchAccesses.Any(x => x.MembershipId == a.AcademyMembershipId && x.BranchId == g.BranchId && x.IsActive))
                    && (a.AcademyMembership.AllSports || db.MembershipSportAccesses.Any(x => x.MembershipId == a.AcademyMembershipId && x.SportId == g.SportId && x.IsActive))),
                g.IsActive && g.Branch.IsActive && g.Sport.IsActive)).ToArrayAsync();
    }
    private static object Snapshot(OwnerFollowUp f) => new { f.State, f.AssignedToMembershipId, f.DueOn, f.Note, f.ResolutionReason };
    private static object Row(OwnerFollowUp f, GroupSignal? source, DateOnly today) => new { f.Id, f.Version, f.TrainingGroupId, f.Condition, f.State, f.AssignedToMembershipId, f.DueOn, f.Note, f.ResolutionReason, f.CreatedOn, f.CreatedAtUtc, f.UpdatedAtUtc,
        evidence = JsonSerializer.Deserialize<JsonElement>(f.EvidenceJson), source, conditionActive = source is { SourceActive: true, Capacity: null or <= 0 } && f.Condition == "GroupLowOccupancy" ? (bool?)null : source?.Applies(f.Condition), overdue = f.State is not ("Closed" or "Rejected") && f.DueOn < today,
        sourceUrl = $"/dashboard/academy/groups/{f.TrainingGroupId}" };
    private static async Task<object[]> Owners(FoundationDbContext db, Guid academy) => (await db.AcademyMemberships.Where(m => m.AcademyId == academy && m.Role == AcademyRole.AcademyOwner && m.IsActive && m.User.IsActive)
        .OrderBy(m => m.User.DisplayName).Select(m => new { m.Id, name = m.User.DisplayName }).ToArrayAsync()).Cast<object>().ToArray();
    private static async Task<IResult> Options(CurrentTenant tenant, FoundationDbContext db, TimeProvider clock, HttpContext http)
    {
        if (!await Owner(tenant, db, http)) return Results.Forbid();
        var a = (await tenant.ResolveAsync())!.AcademyId; var today = await AttendanceCalendar.TodayAsync(a, db, clock);
        var sources = await Signals(db, a, today);
        var existing = await db.OwnerFollowUps.Where(f => f.State != "Closed" && f.State != "Rejected").Select(f => new { f.TrainingGroupId, f.Condition, f.Id }).ToArrayAsync();
        return Results.Ok(new { asOfDate = today, owners = await Owners(db, a), branches = await db.Branches.OrderBy(b=>b.ArabicName).Select(b=>new{b.Id,name=b.ArabicName}).ToArrayAsync(), candidates = sources.SelectMany(s => new[] { "GroupCoachMissing", "GroupLowOccupancy" }.Where(s.Applies).Select(condition => new { source = s, condition, activeFollowUpId = existing.FirstOrDefault(f => f.TrainingGroupId == s.GroupId && f.Condition == condition)?.Id })).ToArray(),
            rule = "تسجيلات فعالة مسندة للمجموعة بتاريخ اليوم ÷ السعة المعروفة، وليست عدد اشتراكات مدفوعة أو تقدير ربح. انخفاض الإشغال أقل من 50%." });
    }
    private static async Task<IResult> List(CurrentTenant tenant, FoundationDbContext db, TimeProvider clock, HttpContext http, DateOnly? from, DateOnly? to, Guid? branchId, string? state, bool allDates = false)
    {
        if (!await Owner(tenant, db, http)) return Results.Forbid();
        var a = (await tenant.ResolveAsync())!.AcademyId; var today = await AttendanceCalendar.TodayAsync(a, db, clock);
        if (state is not (null or "" or "Open") && !States.Contains(state)) return Invalid("اختر حالة متابعة صحيحة.");
        if (!allDates && from == null && to == null) { from = new DateOnly(today.Year, today.Month, 1); to = today; }
        if (from > to) return Invalid("بداية الفترة يجب ألا تتجاوز نهايتها.");
        var q = db.OwnerFollowUps.AsNoTracking().Where(f => (!from.HasValue || f.CreatedOn >= from) && (!to.HasValue || f.CreatedOn <= to) && (!branchId.HasValue || db.TrainingGroups.Any(g => g.Id == f.TrainingGroupId && g.BranchId == branchId)));
        if (state == "Open") q = q.Where(f => f.State != "Closed" && f.State != "Rejected"); else if (!string.IsNullOrEmpty(state)) q = q.Where(f => f.State == state);
        var rows = await q.OrderByDescending(f => f.UpdatedAtUtc).ThenBy(f => f.Id).Take(101).ToArrayAsync();
        var signals = (await Signals(db, a, today)).ToDictionary(g => g.GroupId);
        return Results.Ok(new { from, to, asOfDate = today, hasMore = rows.Length > 100, limit = 100, owners = await Owners(db, a), items = rows.Take(100).Select(f => Row(f, signals.GetValueOrDefault(f.TrainingGroupId), today)).ToArray() });
    }
    private static async Task<IResult> Details(Guid id, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock, HttpContext http)
    {
        if (!await Owner(tenant, db, http)) return Results.Forbid();
        var a = (await tenant.ResolveAsync())!.AcademyId; var f = await db.OwnerFollowUps.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id);
        if (f == null) return Results.NotFound();
        var today = await AttendanceCalendar.TodayAsync(a, db, clock);
        var history = await db.OwnerFollowUpChanges.Where(c => c.FollowUpId == id).OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id).Select(c => new { c.Id, c.ActorMembershipId, actorName=db.AcademyMemberships.Where(m=>m.Id==c.ActorMembershipId).Select(m=>m.User.DisplayName).FirstOrDefault(), c.CreatedAtUtc, before = c.BeforeJson, after = c.AfterJson }).ToArrayAsync();
        return Results.Ok(new { item = Row(f, (await Signals(db, a, today, f.TrainingGroupId)).SingleOrDefault(), today), history, owners = await Owners(db, a) });
    }
    private static async Task<IResult> Create(OwnerFollowUpCreate body, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock, HttpContext http)
    {
        var t = (await tenant.ResolveAsync())!;
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await Owner(tenant, db, http, body.AssignedToMembershipId, true)) return Results.Forbid();
        if (body.RequestId == Guid.Empty || body.Note?.Length > 500 || body.Condition is not ("GroupCoachMissing" or "GroupLowOccupancy")) return Invalid("اختر مجموعة وحالة فعلية وملاحظة لا تتجاوز 500 حرف.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"followup-create:{t.AcademyId}:{t.MembershipId}:{body.RequestId}"},0))");
        var hash = Hash(body); var prior = await db.OwnerFollowUps.SingleOrDefaultAsync(f => f.CreatedByMembershipId == t.MembershipId && f.RequestId == body.RequestId);
        if (prior != null) return prior.RequestHash == hash ? Results.Ok(new { prior.Id, replay = true }) : Results.Conflict(new { message = "مفتاح المحاولة استُخدم لبيانات أخرى." });
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"followup-source:{t.AcademyId}:{body.TrainingGroupId}:{body.Condition}"},0))");
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        var source = (await Signals(db, t.AcademyId, today, body.TrainingGroupId)).SingleOrDefault();
        if (source == null) return Results.NotFound();
        if (!source.Applies(body.Condition)) return Results.Conflict(new { message = "الحالة لم تعد تنطبق على المجموعة؛ حدّث مصادر المتابعة." });
        var existing = await db.OwnerFollowUps.Where(f => f.TrainingGroupId == body.TrainingGroupId && f.Condition == body.Condition && f.State != "Closed" && f.State != "Rejected").Select(f => (Guid?)f.Id).SingleOrDefaultAsync();
        if (existing.HasValue) return Results.Conflict(new { message = "للمجموعة متابعة مفتوحة لهذه الحالة بالفعل.", id = existing });
        if (body.AssignedToMembershipId.HasValue && !await EligibleOwner(db, t.AcademyId, body.AssignedToMembershipId.Value, source)) return Invalid("اختر مالكًا فعالًا مخولًا لنطاق المجموعة.");
        var now = clock.GetUtcNow();
        var f = new OwnerFollowUp { Id = Guid.NewGuid(), AcademyId = t.AcademyId, TrainingGroupId = body.TrainingGroupId, CreatedByMembershipId = t.MembershipId, AssignedToMembershipId = body.AssignedToMembershipId, Condition = body.Condition, State = body.AssignedToMembershipId.HasValue ? "Assigned" : "New", CreatedOn = today, DueOn = body.DueOn, Note = body.Note?.Trim(), RequestId = body.RequestId, RequestHash = hash, EvidenceJson = JsonSerializer.Serialize(new { source, observedAtUtc = now, asOfDate = today }), CreatedAtUtc = now, UpdatedAtUtc = now };
        db.OwnerFollowUps.Add(f); db.OwnerFollowUpChanges.Add(new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, FollowUpId = f.Id, ActorMembershipId = t.MembershipId, RequestId = body.RequestId, RequestHash = hash, BeforeJson = "{}", AfterJson = JsonSerializer.Serialize(Snapshot(f)), CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Created($"/api/v1/manage/owner-followups/{f.Id}", new { f.Id });
    }
    private static Task<bool> EligibleOwner(FoundationDbContext db, Guid academy, Guid id, GroupSignal s) => db.AcademyMemberships.AnyAsync(m => m.Id == id && m.AcademyId == academy && m.Role == AcademyRole.AcademyOwner && m.IsActive && m.User.IsActive && (m.AllBranches || db.MembershipBranchAccesses.Any(x => x.MembershipId == id && x.BranchId == s.BranchId && x.IsActive)) && (m.AllSports || db.MembershipSportAccesses.Any(x => x.MembershipId == id && x.SportId == s.SportId && x.IsActive)));
    private static async Task<IResult> Change(Guid id, OwnerFollowUpWrite body, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock, HttpContext http)
    {
        var t = (await tenant.ResolveAsync())!;
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await Owner(tenant, db, http, body.AssignedToMembershipId, true)) return Results.Forbid();
        if (body.RequestId == Guid.Empty || !States.Contains(body.State) || body.Note?.Length > 500 || body.ResolutionReason?.Length > 500 || body.State is "Closed" or "Rejected" && string.IsNullOrWhiteSpace(body.ResolutionReason) || body.State is "Assigned" or "InProgress" && !body.AssignedToMembershipId.HasValue || body.State == "New" && body.AssignedToMembershipId.HasValue) return Invalid("راجع الحالة والمسؤول، واذكر سبب إغلاق أو رفض المتابعة.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"followup-change:{t.AcademyId}:{t.MembershipId}:{body.RequestId}"},0))");
        var hash = Hash(new { id, body }); var prior = await db.OwnerFollowUpChanges.SingleOrDefaultAsync(c => c.ActorMembershipId == t.MembershipId && c.RequestId == body.RequestId);
        if (prior != null) return prior.RequestHash == hash && prior.FollowUpId == id ? Results.Ok(new { id, replay = true }) : Results.Conflict(new { message = "مفتاح المحاولة استُخدم لبيانات أخرى." });
        var f = await db.OwnerFollowUps.SingleOrDefaultAsync(f => f.Id == id); if (f == null) return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"followup-source:{t.AcademyId}:{f.TrainingGroupId}:{f.Condition}"},0))");
        await db.Entry(f).ReloadAsync();
        if (f.Version != body.Version) return Results.Conflict(new { message = "المتابعة تغيرت؛ حدّث التفاصيل قبل الحفظ." });
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        if (body.State == "Snoozed" && (!body.DueOn.HasValue || body.DueOn <= today)) return Invalid("اختر موعد متابعة لاحقًا لليوم عند التأجيل.");
        var source = (await Signals(db, t.AcademyId, today, f.TrainingGroupId)).SingleOrDefault();
        if (source == null) return Results.NotFound();
        if (body.AssignedToMembershipId.HasValue && (body.AssignedToMembershipId != f.AssignedToMembershipId || body.State is "Assigned" or "InProgress") && !await EligibleOwner(db, t.AcademyId, body.AssignedToMembershipId.Value, source)) return Invalid("اختر مالكًا فعالًا مخولًا لنطاق المجموعة.");
        if (body.State is not ("Closed" or "Rejected") && await db.OwnerFollowUps.AnyAsync(o => o.Id != id && o.TrainingGroupId == f.TrainingGroupId && o.Condition == f.Condition && o.State != "Closed" && o.State != "Rejected")) return Results.Conflict(new { message = "توجد متابعة مفتوحة أخرى لهذه الحالة؛ افتحها بدل تكرارها." });
        var before = JsonSerializer.Serialize(Snapshot(f)); var now = clock.GetUtcNow();
        f.State = body.State; f.AssignedToMembershipId = body.AssignedToMembershipId; f.DueOn = body.DueOn; f.Note = body.Note?.Trim(); f.ResolutionReason = body.State is "Closed" or "Rejected" ? body.ResolutionReason?.Trim() : null; f.UpdatedAtUtc = now;
        db.OwnerFollowUpChanges.Add(new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, FollowUpId = id, ActorMembershipId = t.MembershipId, RequestId = body.RequestId, RequestHash = hash, BeforeJson = before, AfterJson = JsonSerializer.Serialize(Snapshot(f)), CreatedAtUtc = now, UpdatedAtUtc = now });
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); } catch (DbUpdateConcurrencyException) { return Results.Conflict(new { message = "المتابعة تغيرت؛ حدّث التفاصيل قبل الحفظ." }); }
        return Results.Ok(new { id });
    }
}
public sealed record OwnerFollowUpCreate(Guid TrainingGroupId, string Condition, Guid RequestId, Guid? AssignedToMembershipId = null, DateOnly? DueOn = null, string? Note = null);
public sealed record OwnerFollowUpWrite(string State, uint Version, Guid RequestId, Guid? AssignedToMembershipId = null, DateOnly? DueOn = null, string? Note = null, string? ResolutionReason = null);
