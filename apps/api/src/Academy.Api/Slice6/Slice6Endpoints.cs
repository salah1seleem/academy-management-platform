using System.Data;
using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice6;

public static class Slice6Endpoints
{
    public static void MapSlice6Endpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        var guardian = api.MapGroup("/guardian").RequireAuthorization(AcademyPermissions.GuardianEnrollmentRequestCreate);
        guardian.MapGet("/home", GuardianHome);
        guardian.MapGet("/enrollment-request-options", GuardianOptions);
        guardian.MapGet("/enrollment-requests", GuardianRequests);
        guardian.MapPost("/enrollment-requests", CreateRequest).AddEndpointFilter<CsrfFilter>();
        guardian.MapGet("/children/{playerId:guid}/profile", ChildProfile);

        var admin = api.MapGroup("/enrollment-requests").RequireAuthorization(AcademyPermissions.EnrollmentRequestRead);
        admin.MapGet("/", AdminRequests);
        admin.MapGet("/{id:guid}", AdminRequest);
        admin.MapPost("/{id:guid}/review", StartReview).RequireAuthorization(AcademyPermissions.EnrollmentRequestManage).AddEndpointFilter<CsrfFilter>();
        admin.MapPost("/{id:guid}/approve", Approve).RequireAuthorization(AcademyPermissions.EnrollmentRequestManage).AddEndpointFilter<CsrfFilter>();
        admin.MapPost("/{id:guid}/reject", Reject).RequireAuthorization(AcademyPermissions.EnrollmentRequestManage).AddEndpointFilter<CsrfFilter>();
    }

    private static async Task<IResult> GuardianHome(CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var today = clock.Today; var expiring = today.AddDays(7);
        var guardianName = await db.GuardianProfiles.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.UserId == user && x.IsActive).Select(x => x.DisplayName).SingleOrDefaultAsync();
        if (guardianName is null) return Results.NotFound();
        var linkedIds = db.GuardianPlayerLinks.Where(x => x.AcademyId == t.AcademyId && x.Guardian.UserId == user && x.IsActive && x.Player.IsActive).Select(x => x.PlayerId);
        var players = await db.Players.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && linkedIds.Contains(x.Id)).OrderBy(x => x.ArabicName)
            .Select(x => new { x.Id, x.ArabicName, x.DateOfBirth, x.PhotoReference }).ToListAsync();
        var enrollments = await db.SportEnrollments.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && linkedIds.Contains(x.PlayerId) && x.Status == EnrollmentStatus.Active)
            .OrderBy(x => x.Sport.ArabicName).Select(x => new
            {
                x.Id, x.PlayerId, x.SportId, sport = x.Sport.ArabicName, group = x.TrainingGroup.ArabicName, branch = x.Branch.ArabicName,
                period = db.SubscriptionPeriods.Where(p => p.AcademyId == t.AcademyId && p.SportEnrollmentId == x.Id && p.Status != SubscriptionPeriodStatus.Cancelled)
                    .OrderByDescending(p => p.EndDate ?? DateOnly.MaxValue).Select(p => new { plan = p.SubscriptionPlan.ArabicName, p.StartDate, p.EndDate, p.RemainingSessions, status = p.EndDate < today ? "Expired" : p.StartDate > today ? "Scheduled" : p.EndDate <= expiring ? "Expiring" : "Active" }).FirstOrDefault()
            }).ToListAsync();
        var children = players.Select(p => new { p.Id, p.ArabicName, p.DateOfBirth, p.PhotoReference, sports = enrollments.Where(e => e.PlayerId == p.Id).ToList() }).ToList();
        var sportContexts = enrollments.GroupBy(x => new { x.SportId, x.sport }).Select(g => new { g.Key.SportId, sport = g.Key.sport, children = g.Select(x => players.Single(p => p.Id == x.PlayerId).ArabicName).Distinct().Order().ToArray(), childCount = g.Select(x => x.PlayerId).Distinct().Count() }).OrderBy(x => x.sport).ToList();
        return Results.Ok(new { academyName = t.AcademyName, guardianName, today, children, sportContexts });
    }

    private static async Task<IResult> GuardianOptions(CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal);
        var children = await db.GuardianPlayerLinks.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Guardian.UserId == user && x.IsActive && x.Player.IsActive).OrderBy(x => x.Player.ArabicName).Select(x => new { id = x.PlayerId, name = x.Player.ArabicName }).ToListAsync();
        var sports = await db.Sports.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, name = x.ArabicName }).ToListAsync();
        var branches = await db.Branches.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, name = x.ArabicName }).ToListAsync();
        return Results.Ok(new { children, sports, branches });
    }

    private static async Task<IResult> GuardianRequests(CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal);
        return Results.Ok(await db.NewEnrollmentRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.RequestedByGuardianUserId == user)
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => new { x.Id, requestType = x.RequestType.ToString(), child = x.ExistingPlayer != null ? x.ExistingPlayer.ArabicName : x.NewChildArabicName, sport = x.Sport.ArabicName, branch = x.PreferredBranch.ArabicName, status = x.Status.ToString(), x.GuardianVisibleReason, x.CreatedAtUtc, x.CreatedSportEnrollmentId }).ToListAsync());
    }

    private static async Task<IResult> CreateRequest(NewEnrollmentCreateRequest request, HttpContext http, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var key = http.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) return Validation("idempotencyKey", "تعذر تأمين الطلب. أعد المحاولة.");
        var existingByKey = await db.NewEnrollmentRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.RequestedByGuardianUserId == user && x.IdempotencyKey == key).Select(x => new { x.Id, status = x.Status.ToString() }).SingleOrDefaultAsync();
        if (existingByKey is not null) return Results.Ok(existingByKey);
        if (!await db.Sports.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == request.SportId && x.IsActive) || !await db.Branches.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == request.PreferredBranchId && x.IsActive)) return Validation("selection", "الرياضة أو الفرع غير متاح.");
        var now = clock.GetUtcNow();
        var entity = new NewEnrollmentRequest { Id = Guid.NewGuid(), AcademyId = t.AcademyId, RequestedByGuardianUserId = user, RequestType = request.RequestType, SportId = request.SportId, PreferredBranchId = request.PreferredBranchId, Notes = Clean(request.Notes), IdempotencyKey = key, CreatedAtUtc = now, UpdatedAtUtc = now };
        if (request.RequestType == NewEnrollmentRequestType.ExistingChildNewSport)
        {
            if (request.ExistingPlayerId is not Guid playerId || !await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == t.AcademyId && x.Guardian.UserId == user && x.PlayerId == playerId && x.IsActive && x.Player.IsActive)) return Results.NotFound();
            if (await db.SportEnrollments.AnyAsync(x => x.AcademyId == t.AcademyId && x.PlayerId == playerId && x.SportId == request.SportId && x.Status == EnrollmentStatus.Active)) return Results.Conflict(new { message = "الطفل مشترك بالفعل في هذه الرياضة." });
            var duplicate = await db.NewEnrollmentRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.RequestedByGuardianUserId == user && x.ExistingPlayerId == playerId && x.SportId == request.SportId && (x.Status == NewEnrollmentRequestStatus.Pending || x.Status == NewEnrollmentRequestStatus.UnderReview)).Select(x => new { x.Id, status = x.Status.ToString() }).FirstOrDefaultAsync();
            if (duplicate is not null) return Results.Conflict(new { message = "يوجد طلب قائم للطفل والرياضة نفسيهما.", existingRequestId = duplicate.Id });
            entity.ExistingPlayerId = playerId;
        }
        else if (request.RequestType == NewEnrollmentRequestType.NewChild)
        {
            if (string.IsNullOrWhiteSpace(request.NewChildArabicName) || request.NewChildDateOfBirth is null || request.NewChildDateOfBirth > DateOnly.FromDateTime(now.UtcDateTime)) return Validation("child", "اسم الطفل وتاريخ ميلاد صحيح مطلوبان.");
            entity.NewChildArabicName = request.NewChildArabicName.Trim(); entity.NewChildDateOfBirth = request.NewChildDateOfBirth; entity.NewChildGender = request.NewChildGender;
            if (await db.NewEnrollmentRequests.AnyAsync(x => x.AcademyId == t.AcademyId && x.RequestedByGuardianUserId == user && x.NewChildArabicName == entity.NewChildArabicName && x.NewChildDateOfBirth == entity.NewChildDateOfBirth && x.SportId == request.SportId && (x.Status == NewEnrollmentRequestStatus.Pending || x.Status == NewEnrollmentRequestStatus.UnderReview))) return Results.Conflict(new { message = "يوجد طلب قائم بهذه البيانات." });
        }
        else return Validation("requestType", "نوع الطلب غير صالح.");
        db.NewEnrollmentRequests.Add(entity);
        try { await db.SaveChangesAsync(); } catch (DbUpdateException) { return Results.Conflict(new { message = "تم إرسال هذا الطلب بالفعل." }); }
        return Results.Created($"/api/v1/guardian/enrollment-requests/{entity.Id}", new { entity.Id, status = entity.Status.ToString(), message = "تم إرسال طلب الاشتراك بنجاح" });
    }

    private static async Task<IResult> AdminRequests(string? status, Guid? sportId, Guid? branchId, DateOnly? from, DateOnly? to, string? search, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var query = db.NewEnrollmentRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId);
        if (Enum.TryParse<NewEnrollmentRequestStatus>(status, true, out var parsed)) query = query.Where(x => x.Status == parsed); if (sportId.HasValue) query = query.Where(x => x.SportId == sportId); if (branchId.HasValue) query = query.Where(x => x.PreferredBranchId == branchId); if (from.HasValue) query = query.Where(x => x.CreatedAtUtc >= from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); if (to.HasValue) query = query.Where(x => x.CreatedAtUtc < to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.NewChildArabicName!.Contains(search.Trim()) || x.ExistingPlayer!.ArabicName.Contains(search.Trim()) || x.RequestedByGuardianUser.DisplayName.Contains(search.Trim()));
        return Results.Ok(await query.OrderByDescending(x => x.CreatedAtUtc).Select(x => new { x.Id, guardian = x.RequestedByGuardianUser.DisplayName, child = x.ExistingPlayer != null ? x.ExistingPlayer.ArabicName : x.NewChildArabicName, requestType = x.RequestType.ToString(), sport = x.Sport.ArabicName, branch = x.PreferredBranch.ArabicName, status = x.Status.ToString(), x.CreatedAtUtc }).ToListAsync());
    }

    private static async Task<IResult> AdminRequest(Guid id, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var row = await db.NewEnrollmentRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == id).Select(x => new { x.Id, x.RequestType, x.ExistingPlayerId, child = x.ExistingPlayer != null ? x.ExistingPlayer.ArabicName : x.NewChildArabicName, x.NewChildArabicName, x.NewChildDateOfBirth, x.NewChildGender, x.SportId, sport = x.Sport.ArabicName, x.PreferredBranchId, branch = x.PreferredBranch.ArabicName, guardian = x.RequestedByGuardianUser.DisplayName, x.Notes, x.Status, x.AdminNotes, x.GuardianVisibleReason, x.ApprovedPlayerId, x.CreatedSportEnrollmentId, x.CreatedAtUtc, x.Version }).SingleOrDefaultAsync();
        if (row is null) return Results.NotFound();
        var groups = await db.TrainingGroups.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.SportId == row.SportId && x.BranchId == row.PreferredBranchId && x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, name = x.ArabicName }).ToListAsync();
        var playerCandidates = row.RequestType == NewEnrollmentRequestType.NewChild
            ? await db.Players.AsNoTracking()
                .Where(x => x.AcademyId == t.AcademyId && x.IsActive && x.ArabicName == row.NewChildArabicName && x.DateOfBirth == row.NewChildDateOfBirth)
                .OrderBy(x => x.PlayerCode)
                .Select(x => new { x.Id, name = x.ArabicName, x.PlayerCode, x.DateOfBirth })
                .Take(20)
                .ToListAsync()
            : [];
        return Results.Ok(new { request = row, groups, playerCandidates });
    }

    private static async Task<IResult> StartReview(Guid id, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var entity = await db.NewEnrollmentRequests.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (entity is null) return Results.NotFound();
        if (entity.Status == NewEnrollmentRequestStatus.UnderReview) return Results.Ok(new { status = entity.Status.ToString() }); if (entity.Status != NewEnrollmentRequestStatus.Pending) return Results.Conflict(new { message = "لا يمكن بدء مراجعة طلب منتهٍ." });
        entity.Status = NewEnrollmentRequestStatus.UnderReview; entity.UpdatedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(); return Results.Ok(new { status = entity.Status.ToString() });
    }

    private static async Task<IResult> Reject(Guid id, RejectEnrollmentRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var entity = await db.NewEnrollmentRequests.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (entity is null) return Results.NotFound();
        if (entity.Status is not (NewEnrollmentRequestStatus.Pending or NewEnrollmentRequestStatus.UnderReview)) return Results.Conflict(new { message = "تم إنهاء هذا الطلب بالفعل." });
        if (string.IsNullOrWhiteSpace(request.GuardianVisibleReason)) return Validation("reason", "سبب الرفض مطلوب.");
        entity.Status = NewEnrollmentRequestStatus.Rejected; entity.GuardianVisibleReason = request.GuardianVisibleReason.Trim(); entity.AdminNotes = Clean(request.AdminNotes); entity.ReviewedByUserId = UserId(principal); entity.ReviewedAtUtc = clock.GetUtcNow(); entity.UpdatedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(); return Results.Ok(new { status = entity.Status.ToString() });
    }

    private static async Task<IResult> Approve(Guid id, ApproveEnrollmentRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var entity = await db.NewEnrollmentRequests.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (entity is null) return Results.NotFound();
        if (entity.Status == NewEnrollmentRequestStatus.Approved) return Results.Ok(new { status = entity.Status.ToString(), enrollmentId = entity.CreatedSportEnrollmentId });
        if (entity.Status is not (NewEnrollmentRequestStatus.Pending or NewEnrollmentRequestStatus.UnderReview)) return Results.Conflict(new { message = "لا يمكن اعتماد هذا الطلب." });
        var group = await db.TrainingGroups.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == request.TrainingGroupId && x.IsActive && x.SportId == entity.SportId && x.BranchId == entity.PreferredBranchId);
        if (group is null) return Results.UnprocessableEntity(new { message = "المجموعة لا تطابق أكاديمية الطلب ورياضته وفرعه." });
        Player player; var guardian = await db.GuardianProfiles.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.UserId == entity.RequestedByGuardianUserId && x.IsActive); if (guardian is null) return Results.UnprocessableEntity(new { message = "حساب ولي الأمر غير صالح." });
        if (entity.RequestType == NewEnrollmentRequestType.ExistingChildNewSport)
        {
            player = await db.Players.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == entity.ExistingPlayerId && x.IsActive) ?? null!;
            if (player is null || !await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == t.AcademyId && x.GuardianId == guardian.Id && x.PlayerId == player.Id && x.IsActive)) return Results.UnprocessableEntity(new { message = "رابط الطفل بولي الأمر لم يعد صالحًا." });
        }
        else if (request.MatchExistingPlayerId is Guid matchId)
        {
            player = await db.Players.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == matchId && x.IsActive) ?? null!; if (player is null) return Results.NotFound();
            if (!await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == t.AcademyId && x.GuardianId == guardian.Id && x.PlayerId == player.Id)) db.GuardianPlayerLinks.Add(new GuardianPlayerLink { Id = Guid.NewGuid(), AcademyId = t.AcademyId, GuardianId = guardian.Id, PlayerId = player.Id, RelationshipType = "طفل", CreatedByUserId = UserId(principal), CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() });
        }
        else
        {
            player = new Player { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerCode = $"PLR-{Guid.NewGuid():N}"[..12].ToUpperInvariant(), ArabicName = entity.NewChildArabicName!, DateOfBirth = entity.NewChildDateOfBirth!.Value, Gender = entity.NewChildGender, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() }; db.Players.Add(player);
            db.GuardianPlayerLinks.Add(new GuardianPlayerLink { Id = Guid.NewGuid(), AcademyId = t.AcademyId, GuardianId = guardian.Id, PlayerId = player.Id, RelationshipType = "طفل", CreatedByUserId = UserId(principal), CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() });
        }
        if (await db.SportEnrollments.AnyAsync(x => x.AcademyId == t.AcademyId && x.PlayerId == player.Id && x.SportId == entity.SportId && x.Status == EnrollmentStatus.Active)) return Results.Conflict(new { message = "يوجد تسجيل نشط للطفل في هذه الرياضة." });
        var enrollment = new SportEnrollment { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerId = player.Id, SportId = entity.SportId, BranchId = entity.PreferredBranchId, TrainingGroupId = group.Id, Status = EnrollmentStatus.Active, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() }; db.SportEnrollments.Add(enrollment);
        entity.Status = NewEnrollmentRequestStatus.Approved; entity.ApprovedPlayerId = player.Id; entity.CreatedSportEnrollmentId = enrollment.Id; entity.AdminNotes = Clean(request.AdminNotes); entity.ReviewedByUserId = UserId(principal); entity.ReviewedAtUtc = clock.GetUtcNow(); entity.UpdatedAtUtc = clock.GetUtcNow();
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); } catch (DbUpdateException) { await tx.RollbackAsync(); return Results.Conflict(new { message = "تم اعتماد الطلب أو إنشاء التسجيل بالفعل." }); }
        return Results.Ok(new { status = entity.Status.ToString(), playerId = player.Id, enrollmentId = enrollment.Id });
    }

    private static async Task<IResult> ChildProfile(Guid playerId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var linked = await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == t.AcademyId && x.Guardian.UserId == user && x.PlayerId == playerId && x.IsActive && x.Player.IsActive); if (!linked) return Results.NotFound();
        var player = await db.Players.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == playerId).Select(x => new { x.Id, x.ArabicName, x.DateOfBirth, x.PhotoReference }).SingleAsync(); var today = clock.Today; var expiring = today.AddDays(7);
        var enrollments = await db.SportEnrollments.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.PlayerId == playerId && x.Status == EnrollmentStatus.Active).OrderBy(x => x.Sport.ArabicName).Select(x => new { x.Id, x.SportId, sport = x.Sport.ArabicName, group = x.TrainingGroup.ArabicName, branch = x.Branch.ArabicName, recurring = db.RecurringSchedules.Where(s => s.AcademyId == t.AcademyId && s.TrainingGroupId == x.TrainingGroupId && s.IsActive).OrderBy(s => s.DayOfWeek).Select(s => new { s.DayOfWeek, s.StartTime, s.EndTime }).ToList(), upcoming = db.TrainingSessions.Where(s => s.AcademyId == t.AcademyId && s.TrainingGroupId == x.TrainingGroupId && s.SessionDate >= today && s.Status == TrainingSessionStatus.Scheduled).OrderBy(s => s.SessionDate).Take(3).Select(s => new { s.SessionDate, s.StartTime, s.EndTime }).ToList(), period = db.SubscriptionPeriods.Where(p => p.AcademyId == t.AcademyId && p.SportEnrollmentId == x.Id && p.Status != SubscriptionPeriodStatus.Cancelled).OrderByDescending(p => p.EndDate ?? DateOnly.MaxValue).Select(p => new { plan = p.SubscriptionPlan.ArabicName, p.StartDate, p.EndDate, p.RemainingSessions, status = p.EndDate < today ? "Expired" : p.StartDate > today ? "Scheduled" : p.EndDate <= expiring ? "Expiring" : "Active" }).FirstOrDefault() }).ToListAsync();
        var attendance = await db.PlayerAttendances.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.SportEnrollment.PlayerId == playerId && x.Status != AttendanceStatus.NotRecorded && x.TrainingSession.Status != TrainingSessionStatus.Cancelled).OrderByDescending(x => x.TrainingSession.SessionDate).Take(20).Select(x => new { x.Id, x.TrainingSession.SessionDate, sport = x.TrainingSession.Sport.ArabicName, group = x.TrainingSession.TrainingGroup.ArabicName, status = x.Status.ToString() }).ToListAsync();
        var evaluations = await db.PlayerEvaluations.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.SportEnrollment.PlayerId == playerId && x.Status == EvaluationStatus.Published).OrderByDescending(x => x.EvaluationDate).Select(x => new { x.Id, sport = x.Sport.ArabicName, x.EvaluationDate, x.ReportingPeriod }).ToListAsync();
        return Results.Ok(new { player, enrollments, attendance, attendanceSummary = new { present = attendance.Count(x => x.status == "Present"), absent = attendance.Count(x => x.status == "Absent") }, evaluations });
    }

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static IResult Validation(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
}

public sealed record NewEnrollmentCreateRequest(NewEnrollmentRequestType RequestType, Guid? ExistingPlayerId, string? NewChildArabicName, DateOnly? NewChildDateOfBirth, Gender? NewChildGender, Guid SportId, Guid PreferredBranchId, string? Notes);
public sealed record ApproveEnrollmentRequest(Guid TrainingGroupId, Guid? MatchExistingPlayerId, string? AdminNotes);
public sealed record RejectEnrollmentRequest(string GuardianVisibleReason, string? AdminNotes);
