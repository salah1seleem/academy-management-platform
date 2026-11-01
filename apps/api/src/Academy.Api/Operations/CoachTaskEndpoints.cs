using System.Security.Cryptography;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

// Human follow-ups only. Attendance and evaluation writes retain their existing policies.
public static class CoachTaskEndpoints
{
    public const string AssignPermission = "workflow.coach-tasks.assign";
    public static readonly string[] Conditions = ["CoachAttendanceTask", "CoachEvaluationTask"];
    public static void MapCoachTasks(this WebApplication app)
    {
        var staff = app.MapGroup("/api/v1/manage/coach-tasks").RequireAuthorization(AcademyPermissions.TenantAccess);
        staff.MapGet("/options", Options);
        staff.MapGet("/", List);
        staff.MapPost("/", Create).AddEndpointFilter<CsrfFilter>();
        staff.MapGet("/{id:guid}", Details);
        staff.MapPost("/{id:guid}", Change).AddEndpointFilter<CsrfFilter>();
        var mine = app.MapGroup("/api/v1/my-coach-tasks").RequireAuthorization(AcademyPermissions.CoachGroupsRead);
        mine.MapGet("/", List);
        mine.MapGet("/{id:guid}", Details);
        mine.MapPost("/{id:guid}", Change).AddEndpointFilter<CsrfFilter>();
    }

    private static async Task<bool> Manager(FoundationDbContext db, TenantMembership t) =>
        t.Role == AcademyRole.AcademyOwner || t.Role == AcademyRole.AcademyAdmin &&
        await db.AcademyMemberships.AnyAsync(m => m.Id == t.MembershipId && m.IsActive && m.UseActionGrants) &&
        await db.StaffActionGrants.AnyAsync(g => g.MembershipId == t.MembershipId && g.IsActive && g.Module == "followups" && g.Read && g.Edit) &&
        await db.StaffGrants.AnyAsync(g => g.MembershipId == t.MembershipId && g.IsActive && g.Permission == AssignPermission && g.Allowed);

    private static IQueryable<Academy.Infrastructure.Structure.StaffGroupAssignment> Assigned(FoundationDbContext db) =>
        db.StaffGroupAssignments.Where(a => a.IsActive && a.TrainingGroup.IsActive && a.TrainingGroup.Branch.IsActive && a.TrainingGroup.Sport.IsActive &&
            a.AcademyMembership.IsActive && a.AcademyMembership.User.IsActive && a.AcademyMembership.Role == AcademyRole.Coach &&
            (a.AcademyMembership.PrimaryMembershipId == null || db.AcademyMemberships.Any(p => p.Id == a.AcademyMembership.PrimaryMembershipId && p.IsActive && p.PrimaryMembershipId == null && p.UserId == a.AcademyMembership.UserId)) &&
            (a.AcademyMembership.AllBranches || db.MembershipBranchAccesses.Any(b => b.MembershipId == a.AcademyMembershipId && b.BranchId == a.TrainingGroup.BranchId && b.IsActive)) &&
            (a.AcademyMembership.AllSports || db.MembershipSportAccesses.Any(s => s.MembershipId == a.AcademyMembershipId && s.SportId == a.TrainingGroup.SportId && s.IsActive)));

    private static async Task<IQueryable<OwnerFollowUp>?> Readable(FoundationDbContext db, TenantMembership t)
    {
        var q = db.OwnerFollowUps.Where(f => f.AcademyId == t.AcademyId && Conditions.Contains(f.Condition));
        var assignments = Assigned(db);
        if (t.Role == AcademyRole.Coach)
            return q.Where(f => f.AssignedToMembershipId == t.MembershipId && assignments.Any(a => a.AcademyMembershipId == t.MembershipId && a.TrainingGroupId == f.TrainingGroupId));
        return await Manager(db, t) ? q.Where(f => db.TrainingGroups.Any(g => g.Id == f.TrainingGroupId)) : null;
    }
    private static async Task<bool> Enabled(HttpContext h, Guid academy) =>
        await h.RequestServices.GetRequiredService<Academy.Api.Configuration.AcademyConfigurationService>().Enabled(academy, "training");
    private static IResult Invalid(string message) => Results.UnprocessableEntity(new { message });
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static object Snapshot(OwnerFollowUp f) => new { f.State, f.AssignedToMembershipId, f.DueOn, f.Note, f.ResolutionReason };
    private static async Task Lock(FoundationDbContext db, string key) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))");

    private static async Task<IResult> Options(CurrentTenant tenant, FoundationDbContext db, HttpContext h, int page = 1, int pageSize = 50, string? search = null, Guid? groupId = null)
    {
        h.Response.Headers.CacheControl = "private, no-store";
        var t = (await tenant.ResolveAsync())!;
        if (!await Manager(db, t) || !await Enabled(h, t.AcademyId)) return Results.Forbid();
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100 || search?.Length > 120) return Invalid("راجع البحث والصفحة المطلوبة.");
        var q = Assigned(db).AsNoTracking();
        if (groupId.HasValue) q = q.Where(a => a.TrainingGroupId == groupId);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); q = q.Where(a => a.TrainingGroup.ArabicName.Contains(term) || a.AcademyMembership.User.DisplayName.Contains(term)); }
        var count = await q.CountAsync();
        var rows = await q.OrderBy(a => a.TrainingGroup.ArabicName).ThenBy(a => a.AcademyMembership.User.DisplayName).ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(a => new { groupId = a.TrainingGroupId, group = a.TrainingGroup.ArabicName, a.TrainingGroup.BranchId, branch = a.TrainingGroup.Branch.ArabicName,
                a.TrainingGroup.SportId, sport = a.TrainingGroup.Sport.ArabicName, coachId = a.AcademyMembershipId, coach = a.AcademyMembership.User.DisplayName }).ToArrayAsync();
        return Results.Ok(new { items = rows, count, page, pageSize, hasMore = page * pageSize < count, kinds = new[] { "Attendance", "Evaluation" } });
    }
    private static async Task<IResult> List(CurrentTenant tenant, FoundationDbContext db, HttpContext h, int page = 1, int pageSize = 25, string? state = null)
    {
        h.Response.Headers.CacheControl = "private, no-store";
        var t = (await tenant.ResolveAsync())!; var q = await Readable(db, t);
        if (q is null || !await Enabled(h, t.AcademyId)) return Results.Forbid();
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100 || state is not (null or "" or "Open" or "Assigned" or "InProgress" or "Snoozed" or "Closed" or "Rejected")) return Invalid("راجع حالة المهام والصفحة المطلوبة.");
        if (state == "Open") q = q.Where(f => f.State != "Closed" && f.State != "Rejected");
        else if (!string.IsNullOrEmpty(state)) q = q.Where(f => f.State == state);
        var count = await q.CountAsync();
        var items = await q.AsNoTracking().OrderBy(f => f.State == "Closed" || f.State == "Rejected").ThenBy(f => f.DueOn ?? DateOnly.MaxValue).ThenBy(f => f.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(f => new { f.Id, kind = f.Condition == "CoachAttendanceTask" ? "Attendance" : "Evaluation",
                f.TrainingGroupId, group = db.TrainingGroups.Where(g => g.Id == f.TrainingGroupId).Select(g => g.ArabicName).FirstOrDefault(), f.AssignedToMembershipId,
                coach = db.AcademyMemberships.Where(m => m.Id == f.AssignedToMembershipId).Select(m => m.User.DisplayName).FirstOrDefault(), f.State, f.DueOn, f.Note, f.Version }).ToArrayAsync();
        return Results.Ok(new { items, count, page, pageSize, hasMore = page * pageSize < count });
    }
    private static async Task<IResult> Details(Guid id, CurrentTenant tenant, FoundationDbContext db, HttpContext h)
    {
        h.Response.Headers.CacheControl = "private, no-store";
        var t = (await tenant.ResolveAsync())!; var q = await Readable(db, t);
        if (q is null || !await Enabled(h, t.AcademyId)) return Results.Forbid();
        var f = await q.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id); if (f is null) return Results.NotFound();
        var group = await db.TrainingGroups.AsNoTracking().Where(g => g.Id == f.TrainingGroupId).Select(g => new { g.Id, g.ArabicName, g.BranchId, g.SportId }).SingleAsync();
        var history = await db.OwnerFollowUpChanges.AsNoTracking().Where(c => c.FollowUpId == id).OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.CreatedAtUtc, after = c.AfterJson }).ToArrayAsync();
        return Results.Ok(new { f.Id, kind = f.Condition == "CoachAttendanceTask" ? "Attendance" : "Evaluation", group,
            f.AssignedToMembershipId, f.State, f.Note, f.DueOn, f.ResolutionReason, f.Version, history,
            notice = "حالة المهمة متابعة بشرية؛ تسجيل الحضور ونشر التقييم من إجراءاتهما المعتادة." });
    }
    private static async Task<IResult> Create(CoachTaskCreate body, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        h.Response.Headers.CacheControl = "private, no-store";
        var t = (await tenant.ResolveAsync())!;
        await using var tx = await db.Database.BeginTransactionAsync();
        foreach (var member in new[] { t.MembershipId, body.CoachId }.Distinct().Order()) await Lock(db, $"grants:{t.AcademyId}:{member}");
        if (!await Manager(db, t) || !await Enabled(h, t.AcademyId)) return Results.Forbid();
        if (body.Kind is not ("Attendance" or "Evaluation") || body.RequestId == Guid.Empty || body.Note?.Length > 500 || body.DueOn < await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock)) return Invalid("اختر الحضور أو التقييم ومجموعة ومدربًا مخولًا وملاحظة حتى 500 حرف وموعدًا غير سابق لليوم.");
        await Lock(db, $"followup-create:{t.AcademyId}:{t.MembershipId}:{body.RequestId}");
        var hash = Hash(body);
        var prior = await db.OwnerFollowUps.AsNoTracking().SingleOrDefaultAsync(f => f.CreatedByMembershipId == t.MembershipId && f.RequestId == body.RequestId);
        if (prior is not null) return Conditions.Contains(prior.Condition) && prior.RequestHash == hash ? Results.Ok(new { prior.Id, replay = true }) : Results.Conflict(new { message = "مفتاح المحاولة مستخدم لبيانات أخرى." });
        if (!await Assigned(db).AnyAsync(a => a.TrainingGroupId == body.GroupId && a.AcademyMembershipId == body.CoachId)) return Results.NotFound();
        var condition = body.Kind == "Attendance" ? Conditions[0] : Conditions[1];
        await Lock(db, $"followup-source:{t.AcademyId}:{body.GroupId}:{condition}");
        var duplicate = await db.OwnerFollowUps.Where(f => f.TrainingGroupId == body.GroupId && f.Condition == condition && f.State != "Closed" && f.State != "Rejected").Select(f => (Guid?)f.Id).FirstOrDefaultAsync();
        if (duplicate.HasValue) return Results.Conflict(new { id = duplicate, message = "لهذه المجموعة مهمة مفتوحة من النوع نفسه بالفعل." });
        var now = clock.GetUtcNow();
        var f = new OwnerFollowUp { Id = Guid.NewGuid(), AcademyId = t.AcademyId, TrainingGroupId = body.GroupId, CreatedByMembershipId = t.MembershipId,
            AssignedToMembershipId = body.CoachId, Condition = condition, State = "Assigned", CreatedOn = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock),
            DueOn = body.DueOn, Note = body.Note?.Trim(), RequestId = body.RequestId, RequestHash = hash, EvidenceJson = JsonSerializer.Serialize(new { body.GroupId, body.CoachId, body.Kind }), CreatedAtUtc = now, UpdatedAtUtc = now };
        db.OwnerFollowUps.Add(f);
        db.OwnerFollowUpChanges.Add(new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, FollowUpId = f.Id, ActorMembershipId = t.MembershipId,
            RequestId = body.RequestId, RequestHash = hash, BeforeJson = "{}", AfterJson = JsonSerializer.Serialize(Snapshot(f)), CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Created($"/api/v1/manage/coach-tasks/{f.Id}", new { f.Id });
    }
    private static async Task<IResult> Change(Guid id, CoachTaskChange body, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        h.Response.Headers.CacheControl = "private, no-store";
        var t = (await tenant.ResolveAsync())!; await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(db, $"grants:{t.AcademyId}:{t.MembershipId}");
        var q = await Readable(db, t); if (q is null || !await Enabled(h, t.AcademyId)) return Results.Forbid();
        if (body.RequestId == Guid.Empty || body.State is not ("InProgress" or "Snoozed" or "Closed" or "Rejected") || body.Note?.Length > 500 || body.ResolutionReason?.Length > 500 || body.State is "Closed" or "Rejected" && string.IsNullOrWhiteSpace(body.ResolutionReason)) return Invalid("راجع الحالة واذكر سبب إغلاق أو رفض المهمة، بحد أقصى 500 حرف.");
        await Lock(db, $"followup-change:{t.AcademyId}:{t.MembershipId}:{body.RequestId}");
        await Lock(db, $"coach-task:{t.AcademyId}:{id}");
        var f = await q.SingleOrDefaultAsync(f => f.Id == id); if (f is null) return Results.NotFound();
        var hash = Hash(new { id, body });
        var prior = await db.OwnerFollowUpChanges.SingleOrDefaultAsync(c => c.ActorMembershipId == t.MembershipId && c.RequestId == body.RequestId);
        if (prior is not null) return prior.FollowUpId == id && prior.RequestHash == hash ? Results.Ok(new { id, replay = true }) : Results.Conflict(new { message = "مفتاح المحاولة مستخدم لبيانات أخرى." });
        if (f.Version != body.Version || f.State is "Closed" or "Rejected") return Results.Conflict(new { message = "تغيرت المهمة أو أغلقت؛ حدّث التفاصيل." });
        if (body.State == "Snoozed" && (!body.DueOn.HasValue || body.DueOn <= await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock))) return Invalid("اختر موعدًا لاحقًا لليوم عند تأجيل المهمة.");
        var before = JsonSerializer.Serialize(Snapshot(f)); var now = clock.GetUtcNow();
        f.State = body.State; f.Note = body.Note?.Trim(); f.DueOn = body.DueOn; f.ResolutionReason = body.State is "Closed" or "Rejected" ? body.ResolutionReason?.Trim() : null; f.UpdatedAtUtc = now;
        await db.SaveChangesAsync();
        db.OwnerFollowUpChanges.Add(new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, FollowUpId = id, ActorMembershipId = t.MembershipId,
            RequestId = body.RequestId, RequestHash = hash, BeforeJson = before, AfterJson = JsonSerializer.Serialize(Snapshot(f)), CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { id });
    }
}
public sealed record CoachTaskCreate(Guid GroupId, Guid CoachId, string Kind, Guid RequestId, DateOnly? DueOn = null, string? Note = null);
public sealed record CoachTaskChange(string State, uint Version, Guid RequestId, DateOnly? DueOn = null, string? Note = null, string? ResolutionReason = null);
