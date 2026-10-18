using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice5;

public static class PlayerGoalEndpoints
{
    public static void MapPlayerGoalEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/evaluations/development-goals").RequireAuthorization(AcademyPermissions.EvaluationRead);
        g.MapGet("/enrollments/{enrollmentId:guid}", List);
        g.MapPost("/enrollments/{enrollmentId:guid}", Create).RequireAuthorization(AcademyPermissions.EvaluationManage).AddEndpointFilter<CsrfFilter>();
        g.MapGet("/{id:guid}", Detail);
        g.MapPut("/{id:guid}", Edit).RequireAuthorization(AcademyPermissions.EvaluationManage).AddEndpointFilter<CsrfFilter>();
        g.MapPost("/{id:guid}/transition", Transition).RequireAuthorization(AcademyPermissions.EvaluationManage).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/guardian/children/{playerId:guid}/development-goals", GuardianList).RequireAuthorization(AcademyPermissions.GuardianEvaluationRead);
    }

    private sealed record Context(Guid Id, Guid PlayerId, Guid SportId, string Player, string Sport, string? Level);
    private static async Task<Context?> Enrollment(Guid id, TenantMembership t, FoundationDbContext db, TimeProvider clock)
    {
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        var q = db.SportEnrollments.AsNoTracking().Where(e => e.AcademyId == t.AcademyId && e.Id == id && e.IsActive && e.Player.IsActive && e.Sport.IsActive && e.Branch.IsActive);
        if (t.Role == AcademyRole.Coach) q = q.Where(e => db.StaffGroupAssignments.Any(a => a.AcademyId == t.AcademyId && a.AcademyMembershipId == t.MembershipId && a.IsActive
            && a.TrainingGroupId == FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, today) && a.TrainingGroup.IsActive && a.AcademyMembership.IsActive && a.AcademyMembership.User.IsActive));
        return await q.Select(e => new Context(e.Id, e.PlayerId, e.SportId, e.Player.ArabicName, e.Sport.ArabicName, db.TrainingLevels.Where(l => l.Id == e.TrainingLevelId).Select(l => l.ArabicName).FirstOrDefault())).SingleOrDefaultAsync();
    }
    private static async Task<bool> CanWrite(TenantMembership t, FoundationDbContext db, HttpContext h, bool add)
    {
        if (!(await h.RequestServices.GetRequiredService<IAuthorizationService>().AuthorizeAsync(h.User, h, AcademyPermissions.EvaluationManage)).Succeeded) return false;
        if (t.Role != AcademyRole.AcademyAdmin) return true;
        if (await db.AcademyMemberships.AnyAsync(m => m.Id == t.MembershipId && m.UseActionGrants)) return await db.StaffActionGrants.AnyAsync(g => g.MembershipId == t.MembershipId && g.IsActive && g.Module == "evaluations" && g.Read && (add ? g.Add : g.Edit));
        return !await db.StaffGrants.AnyAsync(g => g.MembershipId == t.MembershipId && g.IsActive && g.Permission == AcademyPermissions.EvaluationManage && !g.Allowed);
    }
    private static async Task<bool> LockAuthority(TenantMembership t, FoundationDbContext db, HttpContext h, Guid enrollment, Guid request, bool add)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AcademyMemberships\" WHERE \"Id\"={t.MembershipId} AND \"AcademyId\"={t.AcademyId} FOR UPDATE");
        if (!db.RequestScopeEnabled || db.RequestAcademyId != t.AcademyId || db.RequestMembershipId != t.MembershipId || !await db.AcademyMemberships.AsNoTracking().AnyAsync(m => m.Id == t.MembershipId && m.AcademyId == t.AcademyId && m.UserId == db.RequestUserId && m.IsActive && m.User.IsActive && m.Academy.IsActive
            && (!m.PrimaryMembershipId.HasValue || db.AcademyMemberships.Any(p => p.Id == m.PrimaryMembershipId && p.IsActive && p.User.IsActive)))) return false;
        if (!await CanWrite(t, db, h, add)) return false;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"player-goal-command:{t.AcademyId}:{db.RequestUserId}:{request}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SportEnrollments\" WHERE \"Id\"={enrollment} AND \"AcademyId\"={t.AcademyId} FOR UPDATE");
        if (t.Role == AcademyRole.Coach) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"StaffGroupAssignments\" WHERE \"AcademyId\"={t.AcademyId} AND \"AcademyMembershipId\"={t.MembershipId} FOR SHARE");
        return true;
    }
    private static object Row(PlayerDevelopmentGoal g) => new { g.Id, g.SportEnrollmentId, g.SportId, g.Title, g.Target, g.ReviewOn, g.State, g.Revision, g.Version, g.PublishedAtUtc, g.CreatedAtUtc, g.UpdatedAtUtc };
    private static IResult Invalid(string message) => Results.BadRequest(new { message });
    private static IResult Conflict() => Results.Conflict(new { message = "تغير الهدف أو الطلب؛ أعد تحميله قبل المحاولة." });
    private static bool Valid(GoalDraft r, DateOnly today) => r.RequestId != Guid.Empty && !string.IsNullOrWhiteSpace(r.Title) && r.Title.Trim().Length <= 100 && !string.IsNullOrWhiteSpace(r.Target) && r.Target.Trim().Length <= 500 && r.ReviewOn >= today && r.ReviewOn <= today.AddYears(1);
    private static string Hash(object body, string action) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(action + JsonSerializer.Serialize(body))));
    private static Task<PlayerGoalChange?> Replay(FoundationDbContext db, TenantMembership t, Guid request) => db.PlayerGoalChanges.AsNoTracking().SingleOrDefaultAsync(c => c.AcademyId == t.AcademyId && c.ActorUserId == db.RequestUserId && c.RequestId == request);
    private static async Task Record(FoundationDbContext db, TenantMembership t, PlayerDevelopmentGoal g, Guid request, string hash, string action, string? note, DateTimeOffset now)
    {
        var actor = await db.Users.Where(u => u.Id == db.RequestUserId).Select(u => u.DisplayName).SingleAsync();
        db.PlayerGoalChanges.Add(new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, GoalId = g.Id, SportId = g.SportId, ActorUserId = db.RequestUserId, ActorName = actor,
            RequestId = request, RequestHash = hash, Revision = g.Revision, Action = action, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(), SnapshotJson = JsonSerializer.Serialize(new { g.Id, g.SportEnrollmentId, g.SportId, g.Title, g.Target, g.ReviewOn, g.State, g.Revision, g.PublishedAtUtc }), CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync();
    }
    private static async Task<IResult> List(Guid enrollmentId, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!; var e = await Enrollment(enrollmentId, t, db, clock); if (e == null) return Results.NotFound();
        var rows = await db.PlayerDevelopmentGoals.AsNoTracking().Where(g => g.AcademyId == t.AcademyId && g.SportEnrollmentId == enrollmentId).OrderByDescending(g => g.UpdatedAtUtc).ThenBy(g => g.Id).Take(101).ToArrayAsync();
        return Results.Ok(new { enrollment = e, asOfDate = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock), canCreate = await CanWrite(t, db, h, true), hasMore = rows.Length > 100, items = rows.Take(100).Select(Row) });
    }
    private static async Task<IResult> Detail(Guid id, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!; var g = await db.PlayerDevelopmentGoals.AsNoTracking().SingleOrDefaultAsync(g => g.AcademyId == t.AcademyId && g.Id == id);
        if (g == null || await Enrollment(g.SportEnrollmentId, t, db, clock) is not Context e) return Results.NotFound();
        var history = await db.PlayerGoalChanges.AsNoTracking().Where(c => c.AcademyId == t.AcademyId && c.GoalId == id).OrderByDescending(c => c.Revision).Take(101).Select(c => new { c.Revision, c.Action, c.Note, c.ActorName, c.CreatedAtUtc }).ToArrayAsync();
        return Results.Ok(new { item = Row(g), enrollment = e, canEdit = await CanWrite(t, db, h, false), asOfDate = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock), history = history.Take(100), historyHasMore = history.Length > 100 });
    }
    private static async Task<IResult> Create(Guid enrollmentId, GoalDraft request, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        if (request.RequestId == Guid.Empty) return Invalid("معرف طلب الحفظ مطلوب.");
        await using var tx = await db.Database.BeginTransactionAsync(); if (!await LockAuthority(t, db, h, enrollmentId, request.RequestId, true)) return Results.Forbid();
        var e = await Enrollment(enrollmentId, t, db, clock); if (e == null) return Results.NotFound();
        var hash = Hash(request, $"create:{enrollmentId}"); var replay = await Replay(db, t, request.RequestId);
        if (replay != null) return replay.RequestHash == hash ? Results.Ok(new { id = replay.GoalId, replay = true }) : Conflict();
        if (!Valid(request, today)) return Invalid("أدخل اسم الهدف وما نعمل عليه وموعد مراجعة خلال سنة من اليوم.");
        var now = clock.GetUtcNow(); var goal = new PlayerDevelopmentGoal { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SportEnrollmentId = enrollmentId, SportId = e.SportId, CreatedByUserId = db.RequestUserId, Title = request.Title.Trim(), Target = request.Target.Trim(), ReviewOn = request.ReviewOn, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.PlayerDevelopmentGoals.Add(goal); await Record(db, t, goal, request.RequestId, hash, "Created", null, now); await tx.CommitAsync();
        return Results.Created($"/api/v1/evaluations/development-goals/{goal.Id}", new { id = goal.Id });
    }
    private static Task<IResult> Edit(Guid id, GoalDraft request, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock) => Change(id, request, null, tenant, db, h, clock);
    private static Task<IResult> Transition(Guid id, GoalTransition request, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock) => Change(id, null, request, tenant, db, h, clock);
    private static async Task<IResult> Change(Guid id, GoalDraft? draft, GoalTransition? transition, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var request = draft?.RequestId ?? transition!.RequestId; var version = draft?.Version ?? transition!.Version;
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        if (request == Guid.Empty || transition != null && (transition.Action is not ("Published" or "Reviewed" or "Achieved" or "Withdrawn") || transition.Note?.Length > 500)) return Invalid("راجع بيانات الهدف والإجراء المطلوب.");
        var source = await db.PlayerDevelopmentGoals.AsNoTracking().SingleOrDefaultAsync(g => g.AcademyId == t.AcademyId && g.Id == id); if (source == null) return Results.NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(); if (!await LockAuthority(t, db, h, source.SportEnrollmentId, request, false)) return Results.Forbid();
        if (await Enrollment(source.SportEnrollmentId, t, db, clock) == null) return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"PlayerDevelopmentGoals\" WHERE \"Id\"={id} AND \"AcademyId\"={t.AcademyId} FOR UPDATE");
        var hash = draft == null ? Hash(transition!, $"transition:{id}") : Hash(draft, $"edit:{id}"); var replay = await Replay(db, t, request);
        if (replay != null) return replay.GoalId == id && replay.RequestHash == hash ? Results.Ok(new { id, replay = true }) : Conflict();
        if (draft != null && !Valid(draft, today)) return Invalid("راجع عنوان الهدف وموعد مراجعته.");
        var g = await db.PlayerDevelopmentGoals.SingleAsync(g => g.Id == id && g.AcademyId == t.AcademyId);
        if (g.Version != version) return Conflict();
        if (draft != null && g.State != "Draft" || transition?.Action == "Published" && (g.State != "Draft" || g.ReviewOn < today)
            || transition?.Action is "Reviewed" or "Achieved" && g.State != "Published" || transition?.Action == "Withdrawn" && g.State is not ("Draft" or "Published")) return Conflict();
        var now = clock.GetUtcNow();
        if (draft != null) { g.Title = draft.Title.Trim(); g.Target = draft.Target.Trim(); g.ReviewOn = draft.ReviewOn; }
        else if (transition!.Action != "Reviewed") { g.State = transition.Action; if (g.State == "Published") g.PublishedAtUtc = now; }
        g.Revision++; g.UpdatedAtUtc = now;
        await Record(db, t, g, request, hash, draft == null ? transition!.Action : "Edited", transition?.Note, now); await tx.CommitAsync(); return Results.Ok(new { id, g.Version });
    }
    private static async Task<IResult> GuardianList(Guid playerId, CurrentTenant tenant, FoundationDbContext db, HttpContext h)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!;
        if (!await db.GuardianPlayerLinks.AnyAsync(l => l.AcademyId == t.AcademyId && l.PlayerId == playerId && l.IsActive && l.Guardian.IsActive && l.Guardian.UserId == db.RequestUserId && l.Player.IsActive)) return Results.NotFound();
        var goals = await db.PlayerDevelopmentGoals.AsNoTracking().Where(g => g.AcademyId == t.AcademyId && (g.State == "Published" || g.State == "Achieved")
            && db.SportEnrollments.Any(e => e.Id == g.SportEnrollmentId && e.PlayerId == playerId && e.IsActive)).OrderByDescending(g => g.UpdatedAtUtc).ThenBy(g => g.Id).Take(101).ToArrayAsync();
        var ids = goals.Take(100).Select(g => g.Id).ToArray();
        // Bound each child's published history in SQL, not after loading its whole archive.
        var reviews = await db.PlayerDevelopmentGoals.AsNoTracking().Where(g => g.AcademyId == t.AcademyId && ids.Contains(g.Id))
            .SelectMany(g => db.PlayerGoalChanges.Where(c => c.AcademyId == t.AcademyId && c.GoalId == g.Id && (c.Action == "Published" || c.Action == "Reviewed" || c.Action == "Achieved"))
                .OrderByDescending(c => c.Revision).Take(21).Select(c => new { c.GoalId, c.Revision, c.Action, c.Note, c.ActorName, c.CreatedAtUtc })).ToArrayAsync();
        var sports = await db.Sports.Where(s => s.AcademyId == t.AcademyId).ToDictionaryAsync(s => s.Id, s => s.ArabicName);
        return Results.Ok(new { hasMore = goals.Length > 100, items = goals.Take(100).Select(g => new { goal = Row(g), sport = sports.GetValueOrDefault(g.SportId), reviews = reviews.Where(c => c.GoalId == g.Id).OrderByDescending(c => c.Revision).Take(20), reviewsHasMore = reviews.Count(c => c.GoalId == g.Id) > 20 }) });
    }
}
public sealed record GoalDraft(string Title, string Target, DateOnly ReviewOn, Guid RequestId, uint Version = 0);
public sealed record GoalTransition(string Action, string? Note, Guid RequestId, uint Version);
