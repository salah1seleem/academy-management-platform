using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice5;

public static class Slice5Endpoints
{
    public static void MapSlice5Endpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        var evaluations = api.MapGroup("/evaluations").RequireAuthorization(AcademyPermissions.EvaluationRead);
        evaluations.MapGet("/criteria", ListCriteria);
        evaluations.MapPost("/criteria", CreateCriterion).RequireAuthorization(AcademyPermissions.EvaluationCriteriaManage).AddEndpointFilter<CsrfFilter>();
        evaluations.MapPut("/criteria/{id:guid}", UpdateCriterion).RequireAuthorization(AcademyPermissions.EvaluationCriteriaManage).AddEndpointFilter<CsrfFilter>();
        evaluations.MapPut("/criteria/{id:guid}/status", SetCriterionStatus).RequireAuthorization(AcademyPermissions.EvaluationCriteriaManage).AddEndpointFilter<CsrfFilter>();
        evaluations.MapGet("/options", Options);
        evaluations.MapGet("/", ListEvaluations);
        evaluations.MapPost("/", CreateEvaluation).RequireAuthorization(AcademyPermissions.EvaluationManage).AddEndpointFilter<CsrfFilter>();
        evaluations.MapGet("/{id:guid}", GetEvaluation);
        evaluations.MapPut("/{id:guid}", UpdateEvaluation).RequireAuthorization(AcademyPermissions.EvaluationManage).AddEndpointFilter<CsrfFilter>();
        evaluations.MapPost("/{id:guid}/publish", PublishEvaluation).RequireAuthorization(AcademyPermissions.EvaluationManage).AddEndpointFilter<CsrfFilter>();
        evaluations.MapGet("/{id:guid}/report", StaffReport);

        api.MapGet("/guardian/children/{playerId:guid}/evaluations", GuardianHistory).RequireAuthorization(AcademyPermissions.GuardianEvaluationRead);
        api.MapGet("/guardian/evaluations/{id:guid}/report", GuardianReport).RequireAuthorization(AcademyPermissions.GuardianEvaluationRead);
    }

    private static async Task<IResult> ListCriteria(Guid? sportId, bool? active, string? axis, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        var query = db.EvaluationCriteria.AsNoTracking().Where(x => x.AcademyId == t.AcademyId);
        if (t.Role == AcademyRole.Coach) { var user = UserId(principal); query = query.Where(x => db.StaffGroupAssignments.Any(a => a.AcademyId == t.AcademyId && a.TrainingGroup.SportId == x.SportId && a.AcademyMembership.UserId == user && a.IsActive)); }
        if (sportId.HasValue) query = query.Where(x => x.SportId == sportId);
        if (active.HasValue) query = query.Where(x => x.IsActive == active.Value);
        if (Enum.TryParse<FootballAxis>(axis, true, out var parsedAxis)) query = query.Where(x => x.FootballAxis == parsedAxis);
        var rows = await query.OrderBy(x => x.Sport.ArabicName).ThenBy(x => x.DisplayOrder).ThenBy(x => x.ArabicName)
            .Select(x => new { x.Id, x.SportId, sport = x.Sport.ArabicName, x.ArabicName, x.EnglishName, x.Description, x.DisplayOrder, x.IsActive, x.Weight, footballAxis = x.FootballAxis == null ? null : x.FootballAxis.ToString(), x.Version,
                referenced = db.EvaluationScores.Any(s => s.AcademyId == t.AcademyId && s.EvaluationCriterionId == x.Id) }).ToListAsync();
        return Results.Ok(rows);
    }

    private static async Task<IResult> CreateCriterion(CriterionRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!;
        var validation = ValidateCriterion(request); if (validation is not null) return validation;
        var sport = await db.Sports.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == request.SportId && x.IsActive);
        if (sport is null) return Results.NotFound();
        if (request.FootballAxis.HasValue && !IsFootball(sport)) return Validation("footballAxis", "المحور السداسي متاح لمعايير كرة القدم فقط.");
        if (await db.EvaluationCriteria.AnyAsync(x => x.AcademyId == t.AcademyId && x.SportId == sport.Id && x.ArabicName == request.ArabicName.Trim())) return Results.Conflict(new { message = "يوجد معيار بالاسم نفسه لهذه الرياضة." });
        var now = clock.GetUtcNow();
        var entity = new EvaluationCriterion { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SportId = sport.Id, ArabicName = request.ArabicName.Trim(), EnglishName = Clean(request.EnglishName), Description = Clean(request.Description), DisplayOrder = request.DisplayOrder, IsActive = request.IsActive, Weight = request.Weight, FootballAxis = request.FootballAxis, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.EvaluationCriteria.Add(entity); await db.SaveChangesAsync();
        return Results.Created($"/api/v1/evaluations/criteria/{entity.Id}", new { entity.Id });
    }

    private static async Task<IResult> UpdateCriterion(Guid id, CriterionRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var validation = ValidateCriterion(request); if (validation is not null) return validation;
        var entity = await db.EvaluationCriteria.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (entity is null) return Results.NotFound();
        var sport = await db.Sports.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == request.SportId && x.IsActive); if (sport is null) return Results.NotFound();
        if (request.FootballAxis.HasValue && !IsFootball(sport)) return Validation("footballAxis", "المحور السداسي متاح لمعايير كرة القدم فقط.");
        if (entity.SportId != request.SportId && await db.EvaluationScores.AnyAsync(x => x.AcademyId == t.AcademyId && x.EvaluationCriterionId == id)) return Results.Conflict(new { message = "لا يمكن نقل معيار مستخدم في التاريخ إلى رياضة أخرى." });
        if (await db.EvaluationCriteria.AnyAsync(x => x.AcademyId == t.AcademyId && x.SportId == request.SportId && x.ArabicName == request.ArabicName.Trim() && x.Id != id)) return Results.Conflict(new { message = "يوجد معيار بالاسم نفسه لهذه الرياضة." });
        db.Entry(entity).Property(x => x.Version).OriginalValue = request.Version;
        entity.SportId = request.SportId; entity.ArabicName = request.ArabicName.Trim(); entity.EnglishName = Clean(request.EnglishName); entity.Description = Clean(request.Description); entity.DisplayOrder = request.DisplayOrder; entity.IsActive = request.IsActive; entity.Weight = request.Weight; entity.FootballAxis = request.FootballAxis; entity.UpdatedAtUtc = clock.GetUtcNow();
        try { await db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { return ConflictReload(); }
        return Results.Ok(new { entity.Version });
    }

    private static async Task<IResult> SetCriterionStatus(Guid id, CriterionStatusRequest request, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var entity = await db.EvaluationCriteria.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (entity is null) return Results.NotFound();
        db.Entry(entity).Property(x => x.Version).OriginalValue = request.Version; entity.IsActive = request.IsActive; entity.UpdatedAtUtc = clock.GetUtcNow();
        try { await db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { return ConflictReload(); }
        return Results.Ok(new { entity.Version });
    }

    private static async Task<IResult> Options(CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal);
        var enrollments = db.SportEnrollments.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive);
        if (t.Role == AcademyRole.Coach) enrollments = enrollments.Where(x => db.StaffGroupAssignments.Any(a => a.AcademyId == t.AcademyId && a.TrainingGroupId == x.TrainingGroupId && a.AcademyMembership.UserId == user && a.IsActive));
        var rows = await enrollments.OrderBy(x => x.Player.ArabicName).Select(x => new { id = x.Id, playerId = x.PlayerId, player = x.Player.ArabicName, playerCode = x.Player.PlayerCode, sportId = x.SportId, sport = x.Sport.ArabicName, groupId = x.TrainingGroupId, group = x.TrainingGroup.ArabicName }).ToListAsync();
        var sportQuery = db.Sports.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive);
        if (t.Role == AcademyRole.Coach) sportQuery = sportQuery.Where(x => db.StaffGroupAssignments.Any(a => a.AcademyId == t.AcademyId && a.TrainingGroup.SportId == x.Id && a.AcademyMembership.UserId == user && a.IsActive));
        var sports = await sportQuery.OrderBy(x => x.ArabicName).Select(x => new { x.Id, name = x.ArabicName }).ToListAsync();
        return Results.Ok(new { enrollments = rows, sports });
    }

    private static async Task<IResult> ListEvaluations(string? search, Guid? sportId, Guid? groupId, string? status, DateOnly? from, DateOnly? to, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, IEvaluationReportCalculator calculator)
    {
        var t = (await tenant.ResolveAsync())!; var query = AccessibleEvaluations(t, principal, db).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.SportEnrollment.Player.ArabicName.Contains(search.Trim()) || x.SportEnrollment.Player.PlayerCode.Contains(search.Trim()));
        if (sportId.HasValue) query = query.Where(x => x.SportId == sportId); if (groupId.HasValue) query = query.Where(x => x.TrainingGroupId == groupId);
        if (Enum.TryParse<EvaluationStatus>(status, true, out var parsed)) query = query.Where(x => x.Status == parsed); if (from.HasValue) query = query.Where(x => x.EvaluationDate >= from); if (to.HasValue) query = query.Where(x => x.EvaluationDate <= to);
        var data = await query.Include(x => x.Scores).OrderByDescending(x => x.EvaluationDate).ThenByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync();
        return Results.Ok(data.Select(x => { var calc = calculator.Calculate(x.Scores); return new { x.Id, player = x.SportEnrollment.Player.ArabicName, sport = x.Sport.ArabicName, group = x.TrainingGroup.ArabicName, x.EvaluationDate, evaluator = x.EvaluatedByUser.DisplayName, status = x.Status.ToString(), overallScore = x.Status == EvaluationStatus.Published ? calc.OverallScore : null, calc.ScoredCriteria, calc.TotalApplicableCriteria, calc.CompletenessPercentage, x.Version }; }));
    }

    private static async Task<IResult> CreateEvaluation(CreateEvaluationRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var enrollment = await AccessibleEnrollment(request.SportEnrollmentId, t, principal, db); if (enrollment is null) return Results.NotFound();
        var criteria = await db.EvaluationCriteria.Where(x => x.AcademyId == t.AcademyId && x.SportId == enrollment.SportId && x.IsActive).OrderBy(x => x.DisplayOrder).ToListAsync();
        if (criteria.Count == 0) return Results.Conflict(new { message = "لا توجد معايير فعالة لهذه الرياضة." });
        if (request.EvaluationDate == default) return Validation("evaluationDate", "تاريخ التقييم مطلوب.");
        var now = clock.GetUtcNow(); var id = Guid.NewGuid();
        var entity = new PlayerEvaluation { Id = id, AcademyId = t.AcademyId, SportEnrollmentId = enrollment.Id, SportId = enrollment.SportId, TrainingGroupId = enrollment.TrainingGroupId, EvaluatedByUserId = UserId(principal), EvaluationDate = request.EvaluationDate, ReportingPeriod = string.IsNullOrWhiteSpace(request.ReportingPeriod) ? "تقييم دوري" : request.ReportingPeriod.Trim(), Status = EvaluationStatus.Draft, GeneralNotes = Clean(request.GeneralNotes), CreatedAtUtc = now, UpdatedAtUtc = now };
        entity.Scores = criteria.Select(c => new EvaluationScore { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerEvaluationId = id, EvaluationCriterionId = c.Id, SportId = c.SportId, Score = null, CriterionNameSnapshot = c.ArabicName, WeightSnapshot = c.Weight, FootballAxisSnapshot = c.FootballAxis, CreatedAtUtc = now, UpdatedAtUtc = now }).ToList();
        db.PlayerEvaluations.Add(entity); await db.SaveChangesAsync();
        return Results.Created($"/api/v1/evaluations/{id}", new { id });
    }

    private static async Task<IResult> GetEvaluation(Guid id, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var entity = await AccessibleEvaluations(t, principal, db).AsNoTracking().Include(x => x.Scores).ThenInclude(x => x.EvaluationCriterion).SingleOrDefaultAsync(x => x.Id == id); if (entity is null) return Results.NotFound();
        return Results.Ok(new { entity.Id, entity.SportEnrollmentId, player = entity.SportEnrollment.Player.ArabicName, sport = entity.Sport.ArabicName, group = entity.TrainingGroup.ArabicName, entity.EvaluationDate, entity.ReportingPeriod, status = entity.Status.ToString(), entity.GeneralNotes, entity.Version,
            scores = entity.Scores.OrderBy(x => x.EvaluationCriterion.DisplayOrder).Select(x => new { criterionId = x.EvaluationCriterionId, name = x.CriterionNameSnapshot, x.Score, x.Notes, axis = x.FootballAxisSnapshot == null ? null : x.FootballAxisSnapshot.ToString() }) });
    }

    private static async Task<IResult> UpdateEvaluation(Guid id, UpdateEvaluationRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var entity = await AccessibleEvaluations(t, principal, db).Include(x => x.Scores).SingleOrDefaultAsync(x => x.Id == id); if (entity is null) return Results.NotFound();
        if (entity.Status != EvaluationStatus.Draft) return Results.Conflict(new { message = "لا يمكن تعديل تقييم منشور. أنشئ مراجعة جديدة عند اعتماد مسار المراجعات." });
        if (request.Scores.GroupBy(x => x.CriterionId).Any(x => x.Count() > 1)) return Validation("scores", "لا يمكن تكرار المعيار داخل التقييم.");
        if (request.Scores.Any(x => x.Score is < 0 or > 100)) return Validation("scores", "يجب أن تكون الدرجة بين 0 و100.");
        var byId = entity.Scores.ToDictionary(x => x.EvaluationCriterionId);
        if (request.Scores.Any(x => !byId.ContainsKey(x.CriterionId))) return Validation("scores", "أحد المعايير لا ينتمي لهذا التقييم.");
        db.Entry(entity).Property(x => x.Version).OriginalValue = request.Version;
        entity.GeneralNotes = Clean(request.GeneralNotes); entity.ReportingPeriod = string.IsNullOrWhiteSpace(request.ReportingPeriod) ? entity.ReportingPeriod : request.ReportingPeriod.Trim(); entity.UpdatedAtUtc = clock.GetUtcNow();
        foreach (var item in request.Scores) { var score = byId[item.CriterionId]; score.Score = item.Score; score.Notes = Clean(item.Notes); score.UpdatedAtUtc = clock.GetUtcNow(); }
        try { await db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { return ConflictReload(); }
        return Results.Ok(new { entity.Id, entity.Version, message = "تم حفظ المسودة." });
    }

    private static async Task<IResult> PublishEvaluation(Guid id, PublishEvaluationRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var entity = await AccessibleEvaluations(t, principal, db).Include(x => x.Scores).SingleOrDefaultAsync(x => x.Id == id); if (entity is null) return Results.NotFound();
        if (entity.Status != EvaluationStatus.Draft) return Results.Conflict(new { message = "يمكن نشر المسودة فقط." });
        if (!entity.Scores.Any(x => x.Score.HasValue)) return Validation("scores", "أدخل درجة واحدة على الأقل قبل النشر.");
        db.Entry(entity).Property(x => x.Version).OriginalValue = request.Version; entity.Status = EvaluationStatus.Published; entity.PublishedAtUtc = clock.GetUtcNow(); entity.PublishedByUserId = UserId(principal); entity.UpdatedAtUtc = clock.GetUtcNow();
        try { await db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { return ConflictReload(); }
        return Results.Ok(new { entity.Id, status = entity.Status.ToString(), entity.PublishedAtUtc });
    }

    private static async Task<IResult> StaffReport(Guid id, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, IEvaluationReportCalculator calculator)
    {
        var t = (await tenant.ResolveAsync())!; var entity = await AccessibleEvaluations(t, principal, db).AsNoTracking().Include(x => x.Scores).ThenInclude(x => x.EvaluationCriterion).SingleOrDefaultAsync(x => x.Id == id && x.Status == EvaluationStatus.Published); if (entity is null) return Results.NotFound();
        return Results.Ok(BuildReport(entity, calculator));
    }

    private static async Task<IResult> GuardianHistory(Guid playerId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, IEvaluationReportCalculator calculator)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var linked = await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == t.AcademyId && x.PlayerId == playerId && x.Guardian.UserId == user && x.IsActive); if (!linked) return Results.NotFound();
        var rows = await db.PlayerEvaluations.AsNoTracking().Include(x => x.Scores).Include(x => x.Sport).Include(x => x.EvaluatedByUser).Where(x => x.AcademyId == t.AcademyId && x.SportEnrollment.PlayerId == playerId && x.Status == EvaluationStatus.Published).OrderByDescending(x => x.EvaluationDate).ThenByDescending(x => x.PublishedAtUtc).ToListAsync();
        return Results.Ok(rows.Select(x => { var calc = calculator.Calculate(x.Scores); return new { x.Id, sport = x.Sport.ArabicName, x.EvaluationDate, evaluator = x.EvaluatedByUser.DisplayName, calc.OverallScore, calc.CompletenessPercentage }; }));
    }

    private static async Task<IResult> GuardianReport(Guid id, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, IEvaluationReportCalculator calculator)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal);
        var entity = await db.PlayerEvaluations.AsNoTracking().Include(x => x.Scores).ThenInclude(x => x.EvaluationCriterion).Include(x => x.SportEnrollment).ThenInclude(x => x.Player).Include(x => x.Sport).Include(x => x.EvaluatedByUser).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.Status == EvaluationStatus.Published && db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.PlayerId == x.SportEnrollment.PlayerId && l.Guardian.UserId == user && l.IsActive));
        if (entity is null) return Results.NotFound(); return Results.Ok(BuildReport(entity, calculator));
    }

    private static object BuildReport(PlayerEvaluation entity, IEvaluationReportCalculator calculator)
    {
        var calc = calculator.Calculate(entity.Scores); var player = entity.SportEnrollment.Player;
        var age = entity.EvaluationDate.Year - player.DateOfBirth.Year; if (entity.EvaluationDate < player.DateOfBirth.AddYears(age)) age--;
        return new { entity.Id, player = player.ArabicName, player.PhotoReference, player.FootballPosition, sport = entity.Sport.ArabicName, entity.EvaluationDate, entity.ReportingPeriod, evaluator = entity.EvaluatedByUser.DisplayName, entity.GeneralNotes, age, player.HeightCm, player.WeightKg, preferredFoot = player.PreferredFoot?.ToString(),
            isFootballReport = IsFootball(entity.Sport), calc.OverallScore, calc.ScoredCriteria, calc.TotalApplicableCriteria, calc.CompletenessPercentage, calc.AvailableAxes,
            axes = calc.Axes.Select(x => new { axis = x.Axis.ToString(), x.Value }),
            criteria = entity.Scores.OrderBy(x => x.EvaluationCriterion.DisplayOrder).Select(x => new { name = x.CriterionNameSnapshot, x.Score, x.Notes, axis = x.FootballAxisSnapshot == null ? null : x.FootballAxisSnapshot.ToString() }) };
    }

    private static IQueryable<PlayerEvaluation> AccessibleEvaluations(TenantMembership t, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var user = UserId(principal); var query = db.PlayerEvaluations.Where(x => x.AcademyId == t.AcademyId);
        if (t.Role == AcademyRole.Coach) query = query.Where(x => db.StaffGroupAssignments.Any(a => a.AcademyId == t.AcademyId && a.TrainingGroupId == x.TrainingGroupId && a.AcademyMembership.UserId == user && a.IsActive));
        return query.Include(x => x.SportEnrollment).ThenInclude(x => x.Player).Include(x => x.Sport).Include(x => x.TrainingGroup).Include(x => x.EvaluatedByUser);
    }

    private static async Task<SportEnrollment?> AccessibleEnrollment(Guid id, TenantMembership t, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var user = UserId(principal); var query = db.SportEnrollments.Where(x => x.AcademyId == t.AcademyId && x.Id == id && x.IsActive);
        if (t.Role == AcademyRole.Coach) query = query.Where(x => db.StaffGroupAssignments.Any(a => a.AcademyId == t.AcademyId && a.TrainingGroupId == x.TrainingGroupId && a.AcademyMembership.UserId == user && a.IsActive));
        return await query.SingleOrDefaultAsync();
    }

    private static IResult? ValidateCriterion(CriterionRequest request)
    {
        if (request.SportId == Guid.Empty) return Validation("sportId", "الرياضة مطلوبة.");
        if (string.IsNullOrWhiteSpace(request.ArabicName)) return Validation("arabicName", "اسم المعيار مطلوب.");
        if (request.ArabicName.Trim().Length > 160) return Validation("arabicName", "اسم المعيار طويل جدًا.");
        if (request.Weight <= 0) return Validation("weight", "يجب أن يكون الوزن أكبر من صفر.");
        return null;
    }
    private static bool IsFootball(Sport sport) => string.Equals(sport.EnglishName, "Football", StringComparison.OrdinalIgnoreCase) || sport.ArabicName == "كرة القدم";
    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static IResult Validation(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
    private static IResult ConflictReload() => Results.Conflict(new { message = "تغيرت البيانات بواسطة مستخدم آخر. أعد تحميل الصفحة ثم حاول مجددًا." });
}

public sealed record CriterionRequest(Guid SportId, string ArabicName, string? EnglishName, string? Description, int DisplayOrder, bool IsActive, decimal Weight, FootballAxis? FootballAxis, uint Version = 0);
public sealed record CriterionStatusRequest(bool IsActive, uint Version);
public sealed record CreateEvaluationRequest(Guid SportEnrollmentId, DateOnly EvaluationDate, string ReportingPeriod, string? GeneralNotes);
public sealed record EvaluationScoreRequest(Guid CriterionId, int? Score, string? Notes);
public sealed record UpdateEvaluationRequest(string ReportingPeriod, string? GeneralNotes, uint Version, IReadOnlyList<EvaluationScoreRequest> Scores);
public sealed record PublishEvaluationRequest(uint Version);
