using System.Data;
using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Api.Slice5;
using Academy.Api.Operations;
using Academy.Infrastructure.Operations;
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

    private static async Task<IResult> GuardianHome(CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock, IEvaluationReportCalculator calculator, TimeProvider time)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var today = clock.Today; var expiring = today.AddDays(7);
        var trainingToday = await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
        var guardianName = await db.GuardianProfiles.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.UserId == user && x.IsActive).Select(x => x.DisplayName).SingleOrDefaultAsync();
        if (guardianName is null) return Results.NotFound();
        var linkedIds = db.GuardianPlayerLinks.Where(x => x.AcademyId == t.AcademyId && x.Guardian.UserId == user && x.IsActive && x.Player.IsActive).Select(x => x.PlayerId);
        var players = await db.Players.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && linkedIds.Contains(x.Id)).OrderBy(x => x.ArabicName)
            .Select(x => new { x.Id, x.ArabicName, x.DateOfBirth, x.PhotoReference }).ToListAsync();
        var enrollments = await db.SportEnrollments.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && linkedIds.Contains(x.PlayerId) && x.Status == EnrollmentStatus.Active)
            .OrderBy(x => x.Sport.ArabicName).Select(x => new
            {
                x.Id, x.PlayerId, x.SportId, sport = x.Sport.ArabicName, registrationFormat = x.RegistrationFormat.ToString(), group = db.TrainingGroups.Where(g => g.Id == FoundationDbContext.AssignedGroupAt(x.AcademyId, x.Id, trainingToday)).Select(g => g.ArabicName).FirstOrDefault() ?? "في انتظار المجموعة", branch = x.Branch.ArabicName,
                nextSession = db.TrainingSessions.Where(s => s.AcademyId == t.AcademyId && s.TrainingGroupId == FoundationDbContext.AssignedGroupAt(x.AcademyId, x.Id, s.SessionDate) && s.TrainingGroup.IsActive && s.SessionDate >= trainingToday && s.Status == TrainingSessionStatus.Scheduled).OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime).Select(s => new { s.Id, group = s.TrainingGroup.ArabicName, s.SessionDate, s.StartTime, s.EndTime, resource = (from booking in db.ResourceBookings join resource in db.FacilityResources on booking.ResourceId equals resource.Id join space in db.FacilitySpaces on resource.FacilitySpaceId equals space.Id where booking.TrainingSessionId == s.Id && booking.State == Academy.Infrastructure.Facilities.ResourceBookingState.Reserved select space.Name + " · " + resource.Name).FirstOrDefault() }).FirstOrDefault(),
                period = db.SubscriptionPeriods.Where(p => p.AcademyId == t.AcademyId && p.SportEnrollmentId == x.Id)
                    .OrderByDescending(p => p.UpdatedAtUtc).Select(p => new { p.Id, plan = (p.PlanNameSnapshot ?? p.SubscriptionPlan.ArabicName), p.StartDate, p.EndDate, p.FrozenFromDate, p.RemainingSessions, status = p.Status == SubscriptionPeriodStatus.Frozen ? "Frozen" : p.Status == SubscriptionPeriodStatus.Cancelled ? "Cancelled" : p.EndDate < today || ((p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) != SubscriptionPlanType.Duration && p.RemainingSessions <= 0) ? "Expired" : p.StartDate > today ? "Scheduled" : p.EndDate <= expiring ? "Expiring" : "Active" }).FirstOrDefault()
            }).ToListAsync();
        var published = await db.PlayerEvaluations.AsNoTracking().Include(x => x.Scores).Where(x => x.AcademyId == t.AcademyId && linkedIds.Contains(x.SportEnrollment.PlayerId) && x.Status == EvaluationStatus.Published && (x.Sport.EnglishName == "Football" || x.Sport.ArabicName == "كرة القدم")).OrderByDescending(x => x.EvaluationDate).ThenByDescending(x => x.PublishedAtUtc).Select(x => new { playerId = x.SportEnrollment.PlayerId, evaluation = x }).ToListAsync();
        var children = players.Select(p => { var evaluation = published.FirstOrDefault(e => e.playerId == p.Id)?.evaluation; var report = evaluation is null ? null : calculator.Calculate(evaluation.Scores); return new { p.Id, p.ArabicName, p.DateOfBirth, p.PhotoReference, power = report is null ? null : new { evaluationId = evaluation!.Id, report.OverallScore, axes = report.Axes.Select(a => new { axis = a.Axis.ToString(), a.Value }) }, sports = enrollments.Where(e => e.PlayerId == p.Id).ToList() }; }).ToList();
        var sportContexts = enrollments.GroupBy(x => new { x.SportId, x.sport }).Select(g => new { g.Key.SportId, sport = g.Key.sport, children = g.Select(x => players.Single(p => p.Id == x.PlayerId).ArabicName).Distinct().Order().ToArray(), childCount = g.Select(x => x.PlayerId).Distinct().Count() }).OrderBy(x => x.sport).ToList();
        return Results.Ok(new { academyName = t.AcademyName, guardianName, today, children, sportContexts });
    }

    private static async Task<IResult> GuardianOptions(CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal);
        var children = await db.GuardianPlayerLinks.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Guardian.UserId == user && x.IsActive && x.Player.IsActive).OrderBy(x => x.Player.ArabicName).Select(x => new { id = x.PlayerId, name = x.Player.ArabicName }).ToListAsync();
        var sports = await db.Sports.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, name = x.ArabicName }).ToListAsync();
        var branches = await db.Branches.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, name = x.ArabicName }).ToListAsync();
        var plans = await db.SubscriptionPlans.TrainingOnly().Where(x => x.AcademyId == t.AcademyId && x.IsActive).OrderBy(x => x.ArabicName).Select(x => new { x.Id, x.SportId, x.ArabicName, x.Price, x.Currency, x.DurationDays, x.DurationMonths, serviceKind = x.ServiceKind.ToString(), trainingFormat = x.TrainingFormat.ToString(), x.SessionCount, x.AllBranches, branchIds = x.BranchPublications.Where(p => p.IsActive && p.Branch.IsActive).Select(p => p.BranchId).ToArray() }).ToListAsync();
        return Results.Ok(new { children, sports, branches, plans });
    }

    private static async Task<IResult> GuardianRequests(CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal);
        return Results.Ok(await db.NewEnrollmentRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.RequestedByGuardianUserId == user)
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => new { x.Id, requestType = x.RequestType.ToString(), child = x.ExistingPlayer != null ? x.ExistingPlayer.ArabicName : x.NewChildArabicName, sport = x.Sport.ArabicName, branch = x.PreferredBranch.ArabicName, status = x.Status.ToString(), x.GuardianVisibleReason, x.CreatedAtUtc, x.CreatedSportEnrollmentId }).ToListAsync());
    }

    private static async Task<IResult> CreateRequest(NewEnrollmentCreateRequest request, HttpContext http, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock, DiscountRules discounts)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var key = http.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) return Validation("idempotencyKey", "تعذر تأمين الطلب. أعد المحاولة.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"new-enrollment:{t.AcademyId}:{user}:{key}"}, 0))");
        var existingByKey = await db.NewEnrollmentRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.RequestedByGuardianUserId == user && x.IdempotencyKey == key).Select(x => new { x.Id, status = x.Status.ToString() }).SingleOrDefaultAsync();
        if (existingByKey is not null) return Results.Ok(existingByKey);
        if (!await db.Sports.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == request.SportId && x.IsActive) || !await db.Branches.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == request.PreferredBranchId && x.IsActive)) return Validation("selection", "الرياضة أو الفرع غير متاح.");
        var now = clock.UtcNow;
        SubscriptionPlan? selectedPlan = null;
        if (request.SubscriptionPlanId.HasValue)
        {
            await PlanCatalog.LockAsync(db, t.AcademyId, request.SubscriptionPlanId.Value);
            selectedPlan = await db.SubscriptionPlans.TrainingOnly().ForBranch(request.PreferredBranchId).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == request.SubscriptionPlanId && x.SportId == request.SportId && x.IsActive);
            if (selectedPlan is null) return Validation("plan", "اختر باقة فعالة مطابقة للرياضة والفرع.");
        }
        var requestedFormat = selectedPlan?.TrainingFormat switch { TrainingFormat.Private => TrainingRegistrationFormat.Private, TrainingFormat.SemiPrivate => TrainingRegistrationFormat.SemiPrivate, _ => TrainingRegistrationFormat.Group };
        var entity = new NewEnrollmentRequest { Id = Guid.NewGuid(), AcademyId = t.AcademyId, RequestedByGuardianUserId = user, RequestType = request.RequestType, SportId = request.SportId, PreferredBranchId = request.PreferredBranchId, Notes = Clean(request.Notes), IdempotencyKey = key, CreatedAtUtc = now, UpdatedAtUtc = now };
        if (request.RequestType == NewEnrollmentRequestType.ExistingChildNewSport)
        {
            if (request.ExistingPlayerId is not Guid playerId || !await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == t.AcademyId && x.Guardian.UserId == user && x.PlayerId == playerId && x.IsActive && x.Player.IsActive)) return Results.NotFound();
            if (await db.SportEnrollments.AnyAsync(x => x.AcademyId == t.AcademyId && x.PlayerId == playerId && x.SportId == request.SportId && x.BranchId == request.PreferredBranchId && x.RegistrationFormat == requestedFormat && x.Status == EnrollmentStatus.Active)) return Results.Conflict(new { message = "يوجد تسجيل للطفل في هذه الرياضة والفرع ونمط التدريب." });
            var duplicate = await db.NewEnrollmentRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.RequestedByGuardianUserId == user && x.ExistingPlayerId == playerId && x.SportId == request.SportId && x.PreferredBranchId == request.PreferredBranchId && (requestedFormat == TrainingRegistrationFormat.Private ? x.TrainingFormatSnapshot == TrainingFormat.Private : requestedFormat == TrainingRegistrationFormat.SemiPrivate ? x.TrainingFormatSnapshot == TrainingFormat.SemiPrivate : x.TrainingFormatSnapshot == null || x.TrainingFormatSnapshot == TrainingFormat.Group || x.TrainingFormatSnapshot == TrainingFormat.Season) && (x.Status == NewEnrollmentRequestStatus.Pending || x.Status == NewEnrollmentRequestStatus.UnderReview)).Select(x => new { x.Id, status = x.Status.ToString() }).FirstOrDefaultAsync();
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
        if (selectedPlan is not null) {
            var plan = selectedPlan;
            var guardianId = await db.GuardianProfiles.Where(x => x.AcademyId == t.AcademyId && x.UserId == user && x.IsActive).Select(x => (Guid?)x.Id).SingleOrDefaultAsync();
            var quote = await discounts.CalculateAsync(t.AcademyId, entity.ExistingPlayerId ?? Guid.Empty, plan.Price, user, guardianId, plan: plan, branchId: request.PreferredBranchId);
            entity.SubscriptionPlanId = plan.Id; entity.OriginalAmount = quote.OriginalAmount; entity.FinalAmount = quote.FinalAmount; entity.Currency = plan.Currency;
            entity.DiscountType = quote.DiscountType; entity.DiscountValue = quote.DiscountValue;
            entity.DiscountAmount = quote.DiscountAmount; entity.DiscountReason = quote.DiscountReason;
            entity.DiscountRuleIdSnapshot = quote.DiscountRuleId; entity.DiscountRuleRevisionSnapshot = quote.DiscountRuleRevision;
            PlanTerms.From(plan).Capture(entity);
        }
        db.NewEnrollmentRequests.Add(entity);
        try { await db.SaveChangesAsync(); } catch (DbUpdateException) { return Results.Conflict(new { message = "تم إرسال هذا الطلب بالفعل." }); }
        await tx.CommitAsync();
        return Results.Created($"/api/v1/guardian/enrollment-requests/{entity.Id}", new { entity.Id, entity.FinalAmount, entity.Currency, status = entity.Status.ToString(), message = "تم إرسال طلب الاشتراك بنجاح" });
    }

    private static async Task<IResult> AdminRequests(string? status, Guid? sportId, Guid? branchId, DateOnly? from, DateOnly? to, string? search, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var query = db.NewEnrollmentRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId);
        if (Enum.TryParse<NewEnrollmentRequestStatus>(status, true, out var parsed)) query = query.Where(x => x.Status == parsed); if (sportId.HasValue) query = query.Where(x => x.SportId == sportId); if (branchId.HasValue) query = query.Where(x => x.PreferredBranchId == branchId); if (from.HasValue) query = query.Where(x => x.CreatedAtUtc >= from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); if (to.HasValue) query = query.Where(x => x.CreatedAtUtc < to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)); if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.NewChildArabicName!.Contains(search.Trim()) || x.ExistingPlayer!.ArabicName.Contains(search.Trim()) || x.RequestedByGuardianUser.DisplayName.Contains(search.Trim()));
        return Results.Ok(await query.OrderByDescending(x => x.CreatedAtUtc).Select(x => new { x.Id, guardian = x.RequestedByGuardianUser.DisplayName, child = x.ExistingPlayer != null ? x.ExistingPlayer.ArabicName : x.NewChildArabicName, requestType = x.RequestType.ToString(), sport = x.Sport.ArabicName, branch = x.PreferredBranch.ArabicName, status = x.Status.ToString(), x.CreatedAtUtc }).ToListAsync());
    }

    private static async Task<IResult> AdminRequest(Guid id, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var row = await db.NewEnrollmentRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == id).Select(x => new { x.Id, x.RequestType, x.ExistingPlayerId, child = x.ExistingPlayer != null ? x.ExistingPlayer.ArabicName : x.NewChildArabicName, x.NewChildArabicName, x.NewChildDateOfBirth, x.NewChildGender, x.SportId, sport = x.Sport.ArabicName, x.PreferredBranchId, branch = x.PreferredBranch.ArabicName, guardian = x.RequestedByGuardianUser.DisplayName, x.Notes, x.Status, x.TrainingFormatSnapshot, x.AdminNotes, x.GuardianVisibleReason, x.ApprovedPlayerId, x.CreatedSportEnrollmentId, x.CreatedAtUtc, x.Version }).SingleOrDefaultAsync();
        if (row is null) return Results.NotFound();
        var birth = row.ExistingPlayerId.HasValue ? await db.Players.Where(x => x.AcademyId == t.AcademyId && x.Id == row.ExistingPlayerId).Select(x => (DateOnly?)x.DateOfBirth).SingleOrDefaultAsync() : row.NewChildDateOfBirth;
        var birthYear = birth?.Year;
        var groups = await db.TrainingGroups.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.SportId == row.SportId && (row.TrainingFormatSnapshot == TrainingFormat.Private ? x.RegistrationFormat == TrainingRegistrationFormat.Private : row.TrainingFormatSnapshot == TrainingFormat.SemiPrivate ? x.RegistrationFormat == TrainingRegistrationFormat.SemiPrivate : x.RegistrationFormat == TrainingRegistrationFormat.Group) && x.BranchId == row.PreferredBranchId && x.IsActive && x.Branch.IsActive && x.Sport.IsActive && x.AgeCategory.IsActive && (!birthYear.HasValue || (x.AgeCategory.MinimumBirthYear <= birthYear && x.AgeCategory.MaximumBirthYear >= birthYear))).OrderBy(x => x.ArabicName).Select(x => new { x.Id, x.TrainingLevelId, registrationFormat = x.RegistrationFormat.ToString(), trainingLevel = db.TrainingLevels.Where(l => l.Id == x.TrainingLevelId).Select(l => l.ArabicName).FirstOrDefault(), name = x.ArabicName }).ToListAsync();
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

    private static async Task<IResult> StartReview(Guid id, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var member = (await tenant.ResolveAsync())!;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"new-enrollment-review:{member.AcademyId}:{id}"}, 0))");
        var t = (await tenant.ResolveAsync())!; var entity = await db.NewEnrollmentRequests.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (entity is null) return Results.NotFound();
        if (entity.Status == NewEnrollmentRequestStatus.UnderReview) return Results.Ok(new { status = entity.Status.ToString() }); if (entity.Status != NewEnrollmentRequestStatus.Pending) return Results.Conflict(new { message = "لا يمكن بدء مراجعة طلب منتهٍ." });
        entity.Status = NewEnrollmentRequestStatus.UnderReview; entity.UpdatedAtUtc = clock.UtcNow; await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { status = entity.Status.ToString() });
    }

    private static async Task<IResult> Reject(Guid id, RejectEnrollmentRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        if (request.GuardianVisibleReason?.Length > 500 || request.AdminNotes?.Length > 1000) return Results.UnprocessableEntity();
        await using var tx = await db.Database.BeginTransactionAsync();
        var membership = (await tenant.ResolveAsync())!;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"new-enrollment-review:{membership.AcademyId}:{id}"}, 0))");
        var t = (await tenant.ResolveAsync())!; var entity = await db.NewEnrollmentRequests.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (entity is null) return Results.NotFound();
        if (entity.Status is not (NewEnrollmentRequestStatus.Pending or NewEnrollmentRequestStatus.UnderReview)) return Results.Conflict(new { message = "تم إنهاء هذا الطلب بالفعل." });
        entity.Status = NewEnrollmentRequestStatus.Rejected; entity.GuardianVisibleReason = Clean(request.GuardianVisibleReason); entity.AdminNotes = Clean(request.AdminNotes); entity.ReviewedByUserId = UserId(principal); entity.ReviewedAtUtc = clock.UtcNow; entity.UpdatedAtUtc = clock.UtcNow;
        db.AcademyNotices.Add(new AcademyNotice { Id = Guid.NewGuid(), AcademyId = t.AcademyId, UserId = entity.RequestedByGuardianUserId, Message = "لم يُقبل طلب الاشتراك الجديد. " + (entity.GuardianVisibleReason ?? "تواصل مع إدارة الأكاديمية للتوضيح."), CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { status = entity.Status.ToString() });
    }

    private static async Task<IResult> Approve(Guid id, ApproveEnrollmentRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock, CollectionService collections, Academy.Api.Operations.GroupPlacementService placement, TimeProvider time)
    {
        var t = (await tenant.ResolveAsync())!;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"new-enrollment-review:{t.AcademyId}:{id}"}, 0))");
        var entity = await db.NewEnrollmentRequests.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (entity is null) return Results.NotFound();
        if (entity.Status == NewEnrollmentRequestStatus.Approved) return Results.Ok(new { status = entity.Status.ToString(), enrollmentId = entity.CreatedSportEnrollmentId });
        if (entity.Status is not (NewEnrollmentRequestStatus.Pending or NewEnrollmentRequestStatus.UnderReview)) return Results.Conflict(new { message = "لا يمكن اعتماد هذا الطلب." });
        if (entity.SubscriptionPlanId.HasValue && !entity.ProofAssetId.HasValue) return Results.Conflict(new { message = "ارفع إثبات الدفع قبل قبول الطلب." });
        var group = await db.TrainingGroups.Include(x => x.AgeCategory).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == request.TrainingGroupId && x.IsActive && x.Branch.IsActive && x.Sport.IsActive && x.AgeCategory.IsActive && x.SportId == entity.SportId && x.BranchId == entity.PreferredBranchId);
        if (group is null) return Results.UnprocessableEntity(new { message = "المجموعة لا تطابق أكاديمية الطلب ورياضته وفرعه." });
        if (group.RegistrationFormat != TrainingRegistrationFormat.Group)
        {
            try { group = await placement.PrepareSpecialAdmission(group.Id); }
            catch (Academy.Api.Facilities.FacilityRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
            if (!entity.SubscriptionPlanId.HasValue || !request.LevelReviewed || !request.TrainingLevelId.HasValue || request.TrainingLevelId != group.TrainingLevelId) return Results.UnprocessableEntity(new { message = "راجع مستوى اللاعب واختر باقة مطابقة للتدريب الخاص أو شبه الخاص قبل الاعتماد." });
        }
        Player player; var guardian = await db.GuardianProfiles.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.UserId == entity.RequestedByGuardianUserId && x.IsActive); if (guardian is null) return Results.UnprocessableEntity(new { message = "حساب ولي الأمر غير صالح." });
        if (entity.RequestType == NewEnrollmentRequestType.ExistingChildNewSport)
        {
            player = await db.Players.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == entity.ExistingPlayerId && x.IsActive) ?? null!;
            if (player is null || !await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == t.AcademyId && x.GuardianId == guardian.Id && x.PlayerId == player.Id && x.IsActive)) return Results.UnprocessableEntity(new { message = "رابط الطفل بولي الأمر لم يعد صالحًا." });
        }
        else if (request.MatchExistingPlayerId is Guid matchId)
        {
            player = await db.Players.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == matchId && x.IsActive) ?? null!; if (player is null) return Results.NotFound();
            if (!await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == t.AcademyId && x.GuardianId == guardian.Id && x.PlayerId == player.Id)) db.GuardianPlayerLinks.Add(new GuardianPlayerLink { Id = Guid.NewGuid(), AcademyId = t.AcademyId, GuardianId = guardian.Id, PlayerId = player.Id, RelationshipType = "طفل", CreatedByUserId = UserId(principal), CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
        }
        else
        {
            player = new Player { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerCode = $"PLR-{Guid.NewGuid():N}"[..12].ToUpperInvariant(), ArabicName = entity.NewChildArabicName!, DateOfBirth = entity.NewChildDateOfBirth!.Value, Gender = entity.NewChildGender, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow }; db.Players.Add(player);
            db.GuardianPlayerLinks.Add(new GuardianPlayerLink { Id = Guid.NewGuid(), AcademyId = t.AcademyId, GuardianId = guardian.Id, PlayerId = player.Id, RelationshipType = "طفل", CreatedByUserId = UserId(principal), CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
        }
        if (group.AgeCategory.MinimumBirthYear > player.DateOfBirth.Year || group.AgeCategory.MaximumBirthYear < player.DateOfBirth.Year) return Results.UnprocessableEntity(new { message = "عمر اللاعب غير متوافق مع المجموعة." });
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"group-capacity:{t.AcademyId}:{group.Id}"}, 0))");
        var groupDate = await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
        if (!await Academy.Api.Operations.DatedGroupRoster.CanAdd(db, group.Id, groupDate, group.Capacity)) return Results.Conflict(new { message = "المجموعة مكتملة العدد في الفترة الحالية أو في نقل مجدول." });
        if (await db.SportEnrollments.AnyAsync(x => x.AcademyId == t.AcademyId && x.PlayerId == player.Id && x.SportId == entity.SportId && x.RegistrationFormat == group.RegistrationFormat && x.BranchId == group.BranchId && x.Status == EnrollmentStatus.Active)) return Results.Conflict(new { message = "يوجد تسجيل نشط للطفل في هذه الرياضة." });
        var enrollment = new SportEnrollment { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerId = player.Id, SportId = entity.SportId, BranchId = entity.PreferredBranchId, TrainingGroupId = group.RegistrationFormat == TrainingRegistrationFormat.Group ? group.Id : null, RegistrationFormat = group.RegistrationFormat, TrainingLevelId = group.RegistrationFormat != TrainingRegistrationFormat.Group ? request.TrainingLevelId : null, LevelReviewedByUserId = group.RegistrationFormat != TrainingRegistrationFormat.Group ? UserId(principal) : null, LevelReviewedAtUtc = group.RegistrationFormat != TrainingRegistrationFormat.Group ? clock.UtcNow : null, Status = EnrollmentStatus.Active, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow }; db.SportEnrollments.Add(enrollment);
        if (entity.SubscriptionPlanId.HasValue && !PlanCatalog.FormatMatches(group.RegistrationFormat, entity.TrainingFormatSnapshot ?? (await db.SubscriptionPlans.SingleAsync(p => p.Id == entity.SubscriptionPlanId)).TrainingFormat)) return Results.UnprocessableEntity(new { message = "الباقة المطلوبة لا تطابق نمط المجموعة المختارة." });
        entity.Status = NewEnrollmentRequestStatus.Approved; entity.ApprovedPlayerId = player.Id; entity.CreatedSportEnrollmentId = enrollment.Id; entity.AdminNotes = Clean(request.AdminNotes); entity.ReviewedByUserId = UserId(principal); entity.ReviewedAtUtc = clock.UtcNow; entity.UpdatedAtUtc = clock.UtcNow;
        try {
            await db.SaveChangesAsync();
            if (entity.SubscriptionPlanId.HasValue) {
                var original = entity.OriginalAmount!.Value; var amount = entity.FinalAmount!.Value; var discount = original - amount;
                // Pre-V2 requests captured only final money, not a percentage. Preserve that amount honestly.
                var snapshot = new RenewalFinancialSnapshot(original, entity.DiscountType ?? (discount > 0 ? RenewalDiscountType.FixedAmount : null),
                    entity.DiscountValue ?? (discount > 0 ? discount : null), discount, amount,
                    entity.DiscountReason ?? (discount > 0 ? "عرض محفوظ عند طلب الاشتراك" : null),
                    discount > 0 ? UserId(principal) : null, discount > 0 ? clock.UtcNow : null, entity.DiscountRuleIdSnapshot, entity.DiscountRuleRevisionSnapshot);
                var capturedPlan = await db.SubscriptionPlans.TrainingOnly().SingleAsync(x => x.AcademyId == t.AcademyId && x.Id == entity.SubscriptionPlanId);
                entity.PaymentRequestId = await collections.CreateCashAsync(t.AcademyId, UserId(principal), enrollment.Id, entity.SubscriptionPlanId.Value, "enroll-" + id.ToString("N"), "Online", entity.RequestedByGuardianUserId, snapshot, fixedTerms: PlanTerms.From(entity, capturedPlan));
                if (group.RegistrationFormat != TrainingRegistrationFormat.Group) await placement.FinishSpecialAdmission(group, enrollment);
                db.AcademyNotices.Add(new AcademyNotice { Id = Guid.NewGuid(), AcademyId = t.AcademyId, UserId = entity.RequestedByGuardianUserId, Message = "تم قبول طلب الاشتراك وتفعيل الباقة. مواعيد المجموعة والإيصال متاحة في ملف الطفل.", CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
                await db.SaveChangesAsync();
            }
            await tx.CommitAsync();
        } catch (Academy.Api.Facilities.FacilityRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
        catch (DbUpdateException) { await tx.RollbackAsync(); return Results.Conflict(new { message = "تم اعتماد الطلب أو إنشاء التسجيل بالفعل." }); }
        return Results.Ok(new { status = entity.Status.ToString(), playerId = player.Id, enrollmentId = enrollment.Id });
    }

    private static async Task<IResult> ChildProfile(Guid playerId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock, TimeProvider time)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var linked = await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == t.AcademyId && x.Guardian.UserId == user && x.PlayerId == playerId && x.IsActive && x.Player.IsActive); if (!linked) return Results.NotFound();
        var player = await db.Players.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == playerId).Select(x => new { x.Id, x.ArabicName, x.DateOfBirth, x.PhotoReference }).SingleAsync(); var today = clock.Today; var expiring = today.AddDays(7);
        var trainingToday = await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
        var enrollments = await db.SportEnrollments.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.PlayerId == playerId && x.Status == EnrollmentStatus.Active).OrderBy(x => x.Sport.ArabicName).Select(x => new {
            x.Id, x.SportId, sport = x.Sport.ArabicName, registrationFormat = x.RegistrationFormat.ToString(),
            group = db.TrainingGroups.Where(g => g.Id == FoundationDbContext.AssignedGroupAt(x.AcademyId, x.Id, trainingToday)).Select(g => g.ArabicName).FirstOrDefault() ?? "في انتظار المجموعة", branch = x.Branch.ArabicName,
            recurring = db.RecurringSchedules.Where(s => s.AcademyId == t.AcademyId && s.TrainingGroupId == FoundationDbContext.AssignedGroupAt(x.AcademyId, x.Id, trainingToday) && s.IsActive).OrderBy(s => ((int)s.DayOfWeek + 1) % 7).Select(s => new { s.DayOfWeek, s.StartTime, s.EndTime }).ToList(),
            upcoming = db.TrainingSessions.Where(s => s.AcademyId == t.AcademyId && s.TrainingGroupId == FoundationDbContext.AssignedGroupAt(x.AcademyId, x.Id, s.SessionDate) && s.SessionDate >= trainingToday && s.Status == TrainingSessionStatus.Scheduled).OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime).Take(3).Select(s => new { s.Id, group = s.TrainingGroup.ArabicName, s.SessionDate, s.StartTime, s.EndTime }).ToList(),
            transfers = db.EnrollmentGroupTransfers.Where(tr => tr.SportEnrollmentId == x.Id && tr.EffectiveDate >= trainingToday).OrderBy(tr => tr.EffectiveDate).Select(tr => new { tr.EffectiveDate, group = db.TrainingGroups.Where(g => g.Id == tr.TargetGroupId).Select(g => g.ArabicName).First() }).ToList(),
            period = db.SubscriptionPeriods.Where(p => p.AcademyId == t.AcademyId && p.SportEnrollmentId == x.Id).OrderByDescending(p => p.UpdatedAtUtc).Select(p => new { p.Id, plan = (p.PlanNameSnapshot ?? p.SubscriptionPlan.ArabicName), p.StartDate, p.EndDate, p.FrozenFromDate, p.RemainingSessions, status = p.Status == SubscriptionPeriodStatus.Frozen ? "Frozen" : p.Status == SubscriptionPeriodStatus.Cancelled ? "Cancelled" : p.EndDate < today || ((p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) != SubscriptionPlanType.Duration && p.RemainingSessions <= 0) ? "Expired" : p.StartDate > today ? "Scheduled" : p.EndDate <= expiring ? "Expiring" : "Active" }).FirstOrDefault()
        }).ToListAsync();
        var attendance = await db.PlayerAttendances.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.SportEnrollment.PlayerId == playerId && x.Status != AttendanceStatus.NotRecorded && x.TrainingSession.Status != TrainingSessionStatus.Cancelled).OrderByDescending(x => x.TrainingSession.SessionDate).Take(20).Select(x => new { x.Id, x.TrainingSession.SessionDate, sport = x.TrainingSession.Sport.ArabicName, group = x.TrainingSession.TrainingGroup.ArabicName, status = x.Status.ToString() }).ToListAsync();
        var evaluations = await db.PlayerEvaluations.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.SportEnrollment.PlayerId == playerId && x.Status == EvaluationStatus.Published).OrderByDescending(x => x.EvaluationDate).Select(x => new { x.Id, sport = x.Sport.ArabicName, x.EvaluationDate, x.ReportingPeriod }).ToListAsync();
        return Results.Ok(new { player, enrollments, attendance, attendanceSummary = new { present = attendance.Count(x => x.status == "Present"), absent = attendance.Count(x => x.status == "Absent") }, evaluations });
    }

    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static IResult Validation(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
}

public sealed record NewEnrollmentCreateRequest(NewEnrollmentRequestType RequestType, Guid? ExistingPlayerId, string? NewChildArabicName, DateOnly? NewChildDateOfBirth, Gender? NewChildGender, Guid SportId, Guid PreferredBranchId, string? Notes, Guid? SubscriptionPlanId = null);
public sealed record ApproveEnrollmentRequest(Guid TrainingGroupId, Guid? MatchExistingPlayerId, string? AdminNotes, Guid? TrainingLevelId = null, bool LevelReviewed = false);
public sealed record RejectEnrollmentRequest(string? GuardianVisibleReason, string? AdminNotes);
