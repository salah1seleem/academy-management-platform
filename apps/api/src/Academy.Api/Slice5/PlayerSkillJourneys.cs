using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice5;
public static partial class SkillCurriculumEndpoints
{
    private static void MapSkillJourneys(WebApplication app)
    {
        var g = app.MapGroup(Root).RequireAuthorization(AcademyPermissions.EvaluationRead);
        g.MapGet("/enrollments/{enrollmentId:guid}/journey", Journey);
        g.MapPost("/enrollments/{enrollmentId:guid}/skill-plan", AssignPlan).RequireAuthorization(AcademyPermissions.EvaluationManage).AddEndpointFilter<CsrfFilter>();
        g.MapGet("/plans/{id:guid}", PlanDetail);
        g.MapPost("/plans/{id:guid}/reviews", RecordReview).RequireAuthorization(AcademyPermissions.EvaluationManage).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/guardian/children/{playerId:guid}/skills", FamilyJourney).RequireAuthorization(AcademyPermissions.GuardianEvaluationRead);
        app.MapGet("/api/v1/guardian/children/{playerId:guid}/skills/plans/{id:guid}", FamilyPlan).RequireAuthorization(AcademyPermissions.GuardianEvaluationRead);
    }
    private static object PlanRow(PlayerSkillPlan p, SkillCurriculum c) => new { p.Id, p.SportEnrollmentId, p.CurriculumId, p.IsActive, p.Version, p.AssignedOn, p.EndedAtUtc, p.CreatedAtUtc,
        curriculum = CurriculumRow(c) };
    private static object ReviewRow(PlayerSkillReview r) => new { r.Id, r.RequirementId, r.Sequence, r.State, r.MeasuredValue, r.Note, r.ObservedOn, r.ActorName, r.CreatedAtUtc };
    private static async Task<object> ViewPlan(PlayerSkillPlan p, PlayerGoalEndpoints.Context context, FoundationDbContext db, bool canReview, DateOnly today)
    {
        var c = await db.SkillCurricula.AsNoTracking().SingleAsync(c => c.AcademyId == p.AcademyId && c.Id == p.CurriculumId);
        var requirements = await db.SkillRequirements.AsNoTracking().Where(r => r.AcademyId == p.AcademyId && r.CurriculumId == c.Id).OrderBy(r => r.DisplayOrder).ToArrayAsync();
        var latest = await db.SkillRequirements.Where(r => r.AcademyId == p.AcademyId && r.CurriculumId == c.Id).SelectMany(r => db.PlayerSkillReviews.Where(v => v.AcademyId == p.AcademyId && v.PlanId == p.Id && v.RequirementId == r.Id).OrderByDescending(v => v.ObservedOn).ThenByDescending(v => v.Sequence).Take(1)).AsNoTracking().ToArrayAsync();
        var baseline = await db.SkillRequirements.Where(r => r.AcademyId == p.AcademyId && r.CurriculumId == c.Id).SelectMany(r => db.PlayerSkillReviews.Where(v => v.AcademyId == p.AcademyId && v.PlanId == p.Id && v.RequirementId == r.Id).OrderBy(v => v.ObservedOn).ThenBy(v => v.Sequence).Take(1)).AsNoTracking().ToArrayAsync();
        var history = await db.PlayerSkillReviews.AsNoTracking().Where(v => v.AcademyId == p.AcademyId && v.PlanId == p.Id).OrderByDescending(v => v.Sequence).Take(21).ToArrayAsync();
        var mastered = latest.Count(v => v.State == "Mastered"); var assessed = latest.Count(v => v.State != "NotAssessed");
        var changed = context.TrainingLevelId != c.TrainingLevelId || c.State != "Published" || !p.IsActive || !await db.TrainingLevels.AnyAsync(l => l.Id == c.TrainingLevelId && l.IsActive);
        decimal? percent = requirements.Length == 0 ? null : Math.Round(mastered * 100m / requirements.Length, 2, MidpointRounding.AwayFromZero);
        return new { plan = PlanRow(p, c), enrollment = context, asOfDate = today, contextChanged = changed, canReview = canReview && !changed,
            progress = new { mastered, assessed, total = requirements.Length, equalWeights = true, percentage = changed ? null : percent, sourcePercentage = percent, readyForTechnicalReview = !changed && requirements.Length > 0 && mastered == requirements.Length },
            skills = requirements.Select(r => new { requirement = RequirementRow(r), latest = latest.FirstOrDefault(v => v.RequirementId == r.Id) is {} last ? ReviewRow(last) : null,
                baseline = baseline.FirstOrDefault(v => v.RequirementId == r.Id) is {} first ? ReviewRow(first) : null }),
            history = history.Take(20).Select(ReviewRow), historyHasMore = history.Length > 20 };
    }
    private static async Task<object> ViewJourney(Guid enrollmentId, TenantMembership t, FoundationDbContext db, HttpContext h, TimeProvider clock, bool family)
    {
        var context = (await PlayerGoalEndpoints.Enrollment(enrollmentId, t, db, clock))!; var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        var active = await db.PlayerSkillPlans.AsNoTracking().SingleOrDefaultAsync(p => p.AcademyId == t.AcademyId && p.SportEnrollmentId == enrollmentId && p.IsActive);
        var old = await db.PlayerSkillPlans.AsNoTracking().Where(p => p.AcademyId == t.AcademyId && p.SportEnrollmentId == enrollmentId && !p.IsActive).OrderByDescending(p => p.CreatedAtUtc).ThenBy(p => p.Id).Take(21).ToArrayAsync();
        var ids = old.Take(20).Select(p => p.CurriculumId).ToArray(); var definitions = await db.SkillCurricula.AsNoTracking().Where(c => c.AcademyId == t.AcademyId && ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        var choices = family ? [] : await db.SkillCurricula.AsNoTracking().Where(c => c.AcademyId == t.AcademyId && c.TrainingLevelId == context.TrainingLevelId && c.State == "Published" && db.TrainingLevels.Any(l => l.Id == c.TrainingLevelId && l.IsActive)).OrderByDescending(c => c.RevisionNumber).Take(1).ToArrayAsync();
        var canAssign = !family && await PlayerGoalEndpoints.CanWrite(t, db, h, true) && (active == null || await PlayerGoalEndpoints.CanWrite(t, db, h, false));
        var canReview = !family && await PlayerGoalEndpoints.CanWrite(t, db, h, false);
        return new { enrollment = context, asOfDate = today, canAssign, availableCurricula = choices.Select(CurriculumRow), active = active == null ? null : await ViewPlan(active, context, db, canReview, today),
            history = old.Take(20).Select(p => PlanRow(p, definitions[p.CurriculumId])), historyHasMore = old.Length > 20 };
    }
    private static async Task<IResult> Journey(Guid enrollmentId, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!; if (await PlayerGoalEndpoints.Enrollment(enrollmentId, t, db, clock) == null) return Results.NotFound();
        return Results.Ok(await ViewJourney(enrollmentId, t, db, h, clock, false));
    }
    private static async Task<IResult> PlanDetail(Guid id, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!; var p = await db.PlayerSkillPlans.AsNoTracking().SingleOrDefaultAsync(p => p.AcademyId == t.AcademyId && p.Id == id); if (p == null) return Results.NotFound();
        var context = await PlayerGoalEndpoints.Enrollment(p.SportEnrollmentId, t, db, clock); if (context == null) return Results.NotFound();
        return Results.Ok(await ViewPlan(p, context, db, await PlayerGoalEndpoints.CanWrite(t, db, h, false), await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock)));
    }
    private static async Task<IResult> AssignPlan(Guid enrollmentId, SkillPlanWrite request, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (request.RequestId == Guid.Empty || request.CurriculumId == Guid.Empty) return Bad("اختر نسخة منهج منشورة ومعرف طلب صحيحًا.");
        await using var tx = await db.Database.BeginTransactionAsync(); if (!await PlayerGoalEndpoints.LockAuthority(t, db, h, enrollmentId, request.RequestId, true)) return Results.Forbid();
        var context = await PlayerGoalEndpoints.Enrollment(enrollmentId, t, db, clock); if (context == null) return Results.NotFound();
        var hash = Hash(request, $"skill-plan:{enrollmentId}"); var replay = await db.PlayerSkillPlans.SingleOrDefaultAsync(p => p.AcademyId == t.AcademyId && p.AssignedByUserId == db.RequestUserId && p.RequestId == request.RequestId);
        if (replay != null) return replay.RequestHash == hash && replay.SportEnrollmentId == enrollmentId ? Results.Ok(new { replay.Id, replay = true }) : Changed();
        var enrollment = await db.SportEnrollments.AsNoTracking().SingleAsync(e => e.Id == enrollmentId);
        if (enrollment.TrainingLevelId == null || enrollment.LevelReviewedAtUtc == null || enrollment.LevelReviewedByUserId == null) return Changed("راجع المستوى الفني للاعب من ملفه أولًا؛ إسناد الخطة لا يرقّي اللاعب.");
        await LockLevel(db, t.AcademyId, enrollment.TrainingLevelId.Value);
        var c = await db.SkillCurricula.AsNoTracking().SingleOrDefaultAsync(c => c.AcademyId == t.AcademyId && c.Id == request.CurriculumId && c.State == "Published"); if (c == null) return Results.NotFound();
        if (c.TrainingLevelId != enrollment.TrainingLevelId || c.SportId != enrollment.SportId || !await db.TrainingLevels.AnyAsync(l => l.Id == c.TrainingLevelId && l.IsActive) || await db.SkillCurricula.AnyAsync(x => x.TrainingLevelId == c.TrainingLevelId && x.State == "Published" && x.RevisionNumber > c.RevisionNumber)) return Changed("اختر أحدث نسخة منشورة للمستوى الحالي نفسه؛ لا يغيّر الإسناد مستوى اللاعب.");
        var old = await db.PlayerSkillPlans.SingleOrDefaultAsync(p => p.SportEnrollmentId == enrollmentId && p.IsActive);
        if (old?.Id != request.PreviousPlanId || old != null && old.Version != request.PreviousVersion) return Changed();
        if (old != null && !await PlayerGoalEndpoints.CanWrite(t, db, h, false)) return Results.Forbid();
        if (old?.CurriculumId == c.Id) return Changed("هذه النسخة مسندة بالفعل؛ لا نعيد تصفير سجل المهارات.");
        var now = clock.GetUtcNow(); if (old != null) { old.IsActive = false; old.EndedAtUtc = now; old.UpdatedAtUtc = now; await db.SaveChangesAsync(); }
        var row = new PlayerSkillPlan { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SportEnrollmentId = enrollmentId, CurriculumId = c.Id, SportId = enrollment.SportId, AssignedByUserId = db.RequestUserId, AssignedOn = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock), RequestId = request.RequestId, RequestHash = hash, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.PlayerSkillPlans.Add(row);
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); } catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Changed(); }
        return Results.Created($"{Root}/plans/{row.Id}", new { row.Id });
    }
    private static async Task<IResult> RecordReview(Guid id, SkillReviewWrite request, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!;
        if (request.RequestId == Guid.Empty || request.RequirementId == Guid.Empty || request.State is not ("NotAssessed" or "Learning" or "Mastered") || request.Note?.Length > 500 || request.MeasuredValue is < 0 or > 1000000 || request.MeasuredValue.HasValue && decimal.Round(request.MeasuredValue.Value, 3) != request.MeasuredValue) return Bad("راجع حالة المهارة والقيمة والملاحظة؛ القياس حتى3 منازل عشرية.");
        var source = await db.PlayerSkillPlans.AsNoTracking().SingleOrDefaultAsync(p => p.AcademyId == t.AcademyId && p.Id == id); if (source == null) return Results.NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(); if (!await PlayerGoalEndpoints.LockAuthority(t, db, h, source.SportEnrollmentId, request.RequestId, false)) return Results.Forbid();
        var context = await PlayerGoalEndpoints.Enrollment(source.SportEnrollmentId, t, db, clock); if (context == null) return Results.NotFound();
        var hash = Hash(request, $"skill-review:{id}"); var replay = await db.PlayerSkillReviews.AsNoTracking().SingleOrDefaultAsync(v => v.AcademyId == t.AcademyId && v.ActorUserId == db.RequestUserId && v.RequestId == request.RequestId);
        if (replay != null) return replay.PlanId == id && replay.RequestHash == hash ? Results.Ok(new { replay.Id, replay = true }) : Changed();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"PlayerSkillPlans\" WHERE \"Id\"={id} AND \"AcademyId\"={t.AcademyId} FOR SHARE");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SkillCurricula\" WHERE \"Id\"={source.CurriculumId} AND \"AcademyId\"={t.AcademyId} FOR SHARE");
        var plan = await db.PlayerSkillPlans.AsNoTracking().SingleAsync(p => p.Id == id); var c = await db.SkillCurricula.AsNoTracking().SingleAsync(c => c.Id == plan.CurriculumId);
        var requirement = await db.SkillRequirements.AsNoTracking().SingleOrDefaultAsync(r => r.Id == request.RequirementId && r.CurriculumId == c.Id); if (requirement == null) return Results.NotFound();
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        if (!plan.IsActive || c.State != "Published" || context.TrainingLevelId != c.TrainingLevelId || !await db.TrainingLevels.AnyAsync(l => l.Id == c.TrainingLevelId && l.IsActive)) return Changed("الخطة أو المستوى تغيرا؛ أسند نسخة مناسبة قبل تسجيل مراجعة جديدة.");
        if (request.ObservedOn < plan.AssignedOn || request.ObservedOn > today) return Bad("تاريخ الملاحظة من بداية الخطة وحتى يوم الأكاديمية الحالي فقط؛ لا تقييم مستقبلي.");
        if (request.State == "NotAssessed" && request.MeasuredValue != null || requirement.Unit == "Checklist" && request.MeasuredValue != null || request.State != "NotAssessed" && requirement.Unit != "Checklist" && request.MeasuredValue == null || requirement.Unit == "Count" && request.MeasuredValue.HasValue && decimal.Truncate(request.MeasuredValue.Value) != request.MeasuredValue) return Bad("القيمة يجب أن تطابق وحدة البروتوكول. لم يُقيّم ليست صفرًا، والعدد عدد صحيح.");
        var next = (await db.PlayerSkillReviews.Where(v => v.PlanId == id).Select(v => (int?)v.Sequence).MaxAsync() ?? 0) + 1; var now = clock.GetUtcNow();
        var row = new PlayerSkillReview { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlanId = id, CurriculumId = c.Id, RequirementId = requirement.Id, SportId = c.SportId, Sequence = next, State = request.State, MeasuredValue = request.MeasuredValue,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(), ObservedOn = request.ObservedOn, ActorUserId = db.RequestUserId, ActorName = await db.Users.Where(u => u.Id == db.RequestUserId).Select(u => u.DisplayName).SingleAsync(), RequestId = request.RequestId, RequestHash = hash, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.PlayerSkillReviews.Add(row);
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); } catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Changed(); }
        return Results.Created($"{Root}/plans/{id}", new { row.Id });
    }
    private static Task<bool> Linked(FoundationDbContext db, Guid academy, Guid player) => db.GuardianPlayerLinks.AnyAsync(l => l.AcademyId == academy && l.PlayerId == player && l.IsActive && l.Guardian.IsActive && l.Guardian.UserId == db.RequestUserId && l.Player.IsActive);
    private static async Task<IResult> FamilyJourney(Guid playerId, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!; if (!await Linked(db, t.AcademyId, playerId)) return Results.NotFound();
        var enrollments = await db.SportEnrollments.Where(e => e.AcademyId == t.AcademyId && e.PlayerId == playerId && e.IsActive && e.Player.IsActive && e.Sport.IsActive && e.Branch.IsActive).OrderBy(e => e.Sport.ArabicName).Select(e => e.Id).ToArrayAsync();
        var items = new List<object>(); foreach (var id in enrollments) items.Add(await ViewJourney(id, t, db, h, clock, true)); return Results.Ok(new { items });
    }
    private static async Task<IResult> FamilyPlan(Guid playerId, Guid id, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        h.Response.Headers.CacheControl = "private, no-store"; var t = (await tenant.ResolveAsync())!; if (!await Linked(db, t.AcademyId, playerId)) return Results.NotFound();
        var plan = await db.PlayerSkillPlans.AsNoTracking().SingleOrDefaultAsync(p => p.AcademyId == t.AcademyId && p.Id == id && db.SportEnrollments.Any(e => e.Id == p.SportEnrollmentId && e.PlayerId == playerId)); if (plan == null) return Results.NotFound();
        var context = await PlayerGoalEndpoints.Enrollment(plan.SportEnrollmentId, t, db, clock); if (context == null) return Results.NotFound();
        return Results.Ok(await ViewPlan(plan, context, db, false, await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock)));
    }
}
public sealed record SkillPlanWrite(Guid CurriculumId, Guid RequestId, Guid? PreviousPlanId = null, uint PreviousVersion = 0);
public sealed record SkillReviewWrite(Guid RequirementId, string State, decimal? MeasuredValue, DateOnly ObservedOn, string? Note, Guid RequestId);
