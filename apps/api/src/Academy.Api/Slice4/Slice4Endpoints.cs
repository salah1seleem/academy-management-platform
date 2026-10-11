using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Facilities;
using Academy.Api.Slice3;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace Academy.Api.Slice4;

public static class Slice4Endpoints
{
    public static void MapSlice4Endpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        var attendance = api.MapGroup("/attendance").RequireAuthorization(AcademyPermissions.AttendanceRead);
        attendance.MapGet("/sessions", ListSessions);
        api.MapGet("/manage/sessions", ManageSessions).RequireAuthorization(AcademyPermissions.StructureManage, AcademyPermissions.PeopleManage, AcademyPermissions.AttendanceRead);
        api.MapGet("/manage/sessions/options", SessionOptions).RequireAuthorization(AcademyPermissions.StructureManage, AcademyPermissions.PeopleManage, AcademyPermissions.AttendanceRead);
        attendance.MapGet("/sessions/{id:guid}", GetSession);
        attendance.MapPost("/sessions", CreateSession).RequireAuthorization(AcademyPermissions.StructureManage).AddEndpointFilter<CsrfFilter>();
        attendance.MapPost("/sessions/generate", GenerateSessions).RequireAuthorization(AcademyPermissions.StructureManage).AddEndpointFilter<CsrfFilter>();
        attendance.MapPut("/sessions/{id:guid}/status", SetSessionStatus).RequireAuthorization(AcademyPermissions.StructureManage).AddEndpointFilter<CsrfFilter>();
        attendance.MapGet("/sessions/{id:guid}/players", PlayerRoster);
        attendance.MapPut("/sessions/{id:guid}/players", SavePlayerAttendance).RequireAuthorization(AcademyPermissions.AttendanceManage).AddEndpointFilter<CsrfFilter>();
        attendance.MapGet("/sessions/{id:guid}/staff", StaffRoster);
        attendance.MapPut("/sessions/{id:guid}/staff", SaveStaffAttendance).RequireAuthorization(AcademyPermissions.AttendanceManage).AddEndpointFilter<CsrfFilter>();
        attendance.MapGet("/history", History);
        api.MapGet("/guardian/children/{playerId:guid}/attendance", GuardianAttendance).RequireAuthorization(AcademyPermissions.GuardianAttendanceRead);
    }

    private static async Task<IResult> ListSessions(DateOnly? from, DateOnly? to, bool? allDates, Guid? branchId, Guid? sportId, Guid? groupId, string? status, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        var unfiltered = allDates == true && from is null && to is null;
        var start = from ?? today; var end = to ?? today;
        if (!unfiltered && (end < start || end.DayNumber - start.DayNumber > 366)) return Validation("range", "نطاق التاريخ يجب ألا يتجاوز سنة.");
        var query = db.TrainingSessions.AsNoTracking().Where(x => x.AcademyId == t.AcademyId);
        if (!unfiltered) query = query.Where(x => x.SessionDate >= start && x.SessionDate <= end);
        if (t.Role == AcademyRole.Coach) query = query.Where(x => db.StaffGroupAssignments.Any(a => a.AcademyId == t.AcademyId && a.TrainingGroupId == x.TrainingGroupId && a.AcademyMembership.UserId == user && a.IsActive));
        if (branchId.HasValue) query = query.Where(x => x.BranchId == branchId); if (sportId.HasValue) query = query.Where(x => x.SportId == sportId); if (groupId.HasValue) query = query.Where(x => x.TrainingGroupId == groupId);
        if (Enum.TryParse<TrainingSessionStatus>(status, true, out var parsed)) query = query.Where(x => x.Status == parsed);
        return await SessionRows(query, t.AcademyId, db);
    }

    private static async Task<IResult> ManageSessions(DateOnly? from, DateOnly? to, Guid? groupId, Guid? coachId, Guid? playerId, Guid? guardianId, Guid? branchId, Guid? sportId, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        if (!IsAdmin(t.Role)) return Results.Forbid();
        if (from.HasValue && to.HasValue && to < from) return Validation("range", "تاريخ النهاية يجب ألا يسبق البداية.");
        var query = db.TrainingSessions.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Status != TrainingSessionStatus.Cancelled);
        if (from.HasValue) query = query.Where(x => x.SessionDate >= from);
        if (to.HasValue) query = query.Where(x => x.SessionDate <= to);
        if (groupId.HasValue) query = query.Where(x => x.TrainingGroupId == groupId);
        if (branchId.HasValue) query = query.Where(x => x.BranchId == branchId);
        if (sportId.HasValue) query = query.Where(x => x.SportId == sportId);
        if (coachId.HasValue) query = query.Where(x => db.StaffGroupAssignments.Any(a => a.AcademyId == t.AcademyId && a.TrainingGroupId == x.TrainingGroupId && a.AcademyMembershipId == coachId && a.IsActive && a.AcademyMembership.IsActive));
        if (playerId.HasValue) query = query.Where(x =>
            db.SportEnrollments.Any(e => e.AcademyId == t.AcademyId && FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, x.SessionDate) == x.TrainingGroupId && e.PlayerId == playerId && e.IsActive) ||
            db.PlayerAttendances.Any(a => a.AcademyId == t.AcademyId && a.TrainingSessionId == x.Id && a.SportEnrollment.PlayerId == playerId));
        if (guardianId.HasValue) query = query.Where(x => db.GuardianPlayerLinks.Any(l =>
            l.AcademyId == t.AcademyId && l.GuardianId == guardianId && l.IsActive && l.Guardian.IsActive &&
            (db.SportEnrollments.Any(e => e.AcademyId == t.AcademyId && FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, x.SessionDate) == x.TrainingGroupId && e.PlayerId == l.PlayerId && e.IsActive) ||
             db.PlayerAttendances.Any(a => a.AcademyId == t.AcademyId && a.TrainingSessionId == x.Id && a.SportEnrollment.PlayerId == l.PlayerId))));
        return await SessionRows(query, t.AcademyId, db);
    }

    private static async Task<IResult> SessionOptions(CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        if (!IsAdmin(t.Role)) return Results.Forbid();
        return Results.Ok(new {
            groups = await db.TrainingGroups.Where(x => x.AcademyId == t.AcademyId).OrderBy(x => x.ArabicName).Select(x => new { x.Id, name = x.ArabicName }).ToListAsync(),
            coaches = await db.AcademyMemberships.Where(x => x.AcademyId == t.AcademyId && x.Role == AcademyRole.Coach && x.IsActive).OrderBy(x => x.User.DisplayName).Select(x => new { x.Id, name = x.User.DisplayName }).ToListAsync(),
            players = await db.Players.Where(x => x.AcademyId == t.AcademyId).OrderBy(x => x.ArabicName).Select(x => new { x.Id, name = x.ArabicName }).ToListAsync(),
            guardians = await db.GuardianProfiles.Where(x => x.AcademyId == t.AcademyId && x.IsActive).OrderBy(x => x.DisplayName).Select(x => new { x.Id, name = x.DisplayName }).ToListAsync(),
            branches = await db.Branches.Where(x => x.AcademyId == t.AcademyId).OrderBy(x => x.ArabicName).Select(x => new { x.Id, name = x.ArabicName }).ToListAsync(),
            sports = await db.Sports.Where(x => x.AcademyId == t.AcademyId).OrderBy(x => x.ArabicName).Select(x => new { x.Id, name = x.ArabicName }).ToListAsync()
        });
    }

    private static async Task<IResult> SessionRows(IQueryable<TrainingSession> query, Guid academyId, FoundationDbContext db)
    {
        var rows = await query.OrderBy(x => x.SessionDate).ThenBy(x => x.StartTime).Select(x => new
        {
            x.Id, x.SessionDate, x.StartTime, x.EndTime, status = x.Status.ToString(), source = x.Source.ToString(), x.TrainingGroupId,
            group = x.TrainingGroup.ArabicName, sport = x.Sport.ArabicName, branch = x.Branch.ArabicName,
            resource = (from booking in db.ResourceBookings join resource in db.FacilityResources on booking.ResourceId equals resource.Id join space in db.FacilitySpaces on resource.FacilitySpaceId equals space.Id where booking.TrainingSessionId == x.Id && booking.State == Academy.Infrastructure.Facilities.ResourceBookingState.Reserved select space.Name + " · " + resource.Name).FirstOrDefault(),
            coaches = db.StaffGroupAssignments.Where(a => a.AcademyId == academyId && a.TrainingGroupId == x.TrainingGroupId && a.IsActive && a.AcademyMembership.IsActive).OrderBy(a => a.AcademyMembership.User.DisplayName).Select(a => a.AcademyMembership.User.DisplayName).ToList(),
            playerCount = db.SportEnrollments.Count(e => e.AcademyId == academyId && FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, x.SessionDate) == x.TrainingGroupId && e.IsActive),
            recorded = db.PlayerAttendances.Count(a => a.AcademyId == academyId && a.TrainingSessionId == x.Id && a.Status != AttendanceStatus.NotRecorded),
            present = db.PlayerAttendances.Count(a => a.AcademyId == academyId && a.TrainingSessionId == x.Id && a.Status == AttendanceStatus.Present),
            absent = db.PlayerAttendances.Count(a => a.AcademyId == academyId && a.TrainingSessionId == x.Id && a.Status == AttendanceStatus.Absent),
            staff = db.StaffGroupAssignments.Where(a => a.AcademyId == academyId && a.TrainingGroupId == x.TrainingGroupId && a.IsActive && a.AcademyMembership.IsActive).Select(a => new { id = a.AcademyMembershipId, name = a.AcademyMembership.User.DisplayName, attendanceStatus = db.StaffAttendances.Where(s => s.AcademyId == academyId && s.TrainingSessionId == x.Id && s.AcademyMembershipId == a.AcademyMembershipId).Select(s => s.Status.ToString()).FirstOrDefault() ?? "NotRecorded" }).ToList()
        }).ToListAsync();
        return Results.Ok(rows);
    }

    private static async Task<IResult> GetSession(Guid id, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var session = await AccessibleSession(id, tenant, principal, db, true); if (session is null) return Results.NotFound();
        return Results.Ok(new { session.Id, session.SessionDate, session.StartTime, session.EndTime, status = session.Status.ToString(), source = session.Source.ToString(), session.Notes, group = session.TrainingGroup.ArabicName, sport = session.Sport.ArabicName, branch = session.Branch.ArabicName });
    }

    private static async Task<IResult> CreateSession(ManualSessionRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock, ResourceSessionCoordinator resources)
    {
        var t = (await tenant.ResolveAsync())!; if (!IsAdmin(t.Role)) return Results.Forbid(); if (request.StartTime >= request.EndTime) return Validation("time", "وقت النهاية يجب أن يكون بعد وقت البداية.");
        if (request.Notes?.Trim().Length > 500) return Validation("notes", "الملاحظات يجب ألا تتجاوز 500 حرف.");
        try {
        await using var tx = await db.Database.BeginTransactionAsync(); await resources.LockCalendar(t.AcademyId, [request.TrainingGroupId]);
        var group = await db.TrainingGroups.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == request.TrainingGroupId && x.IsActive); if (group is null) return Results.NotFound();
        if (await db.TrainingSessions.AnyAsync(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == group.Id && x.SessionDate == request.SessionDate && x.StartTime == request.StartTime)) return Results.Conflict(new { message = "توجد حصة للمجموعة في الموعد نفسه." });
        var session = new TrainingSession { Id = Guid.NewGuid(), AcademyId = t.AcademyId, TrainingGroupId = group.Id, BranchId = group.BranchId, SportId = group.SportId, AgeCategoryId = group.AgeCategoryId, SessionDate = request.SessionDate, StartTime = request.StartTime, EndTime = request.EndTime, Status = TrainingSessionStatus.Scheduled, Source = TrainingSessionSource.Manual, Notes = Clean(request.Notes), CreatedByUserId = UserId(principal), CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
        db.TrainingSessions.Add(session); await db.SaveChangesAsync(); await resources.Reserve(session, UserId(principal)); await tx.CommitAsync(); return Results.Created($"/api/v1/attendance/sessions/{session.Id}", new { session.Id });
        } catch (FacilityRejected e) { return Results.Problem(statusCode: e.Status, title: e.Message); }
    }

    private static async Task<IResult> GenerateSessions(GenerateSessionsRequest request, CurrentTenant tenant, ClaimsPrincipal principal, TrainingSessionGenerator generator)
    {
        var t = (await tenant.ResolveAsync())!; if (!IsAdmin(t.Role)) return Results.Forbid();
        try { var created = await generator.GenerateAsync(t.AcademyId, UserId(principal), request.From, request.To); return Results.Ok(new { created, message = created == 0 ? "لا توجد حصص جديدة ضمن النطاق." : $"تم إنشاء {created} حصة." }); }
        catch (ArgumentOutOfRangeException) { return Validation("range", "اختر نطاقًا صحيحًا لا يزيد على 31 يومًا."); }
        catch (FacilityRejected e) { return Results.Problem(statusCode: e.Status, title: e.Message); }
    }

    private static async Task<IResult> SetSessionStatus(Guid id, SessionStatusRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock, ResourceSessionCoordinator resources)
    {
        var t = (await tenant.ResolveAsync())!; if (!IsAdmin(t.Role)) return Results.Forbid();
        try {
        await using var tx = await db.Database.BeginTransactionAsync(); var group = await db.TrainingSessions.Where(x => x.Id == id).Select(x => (Guid?)x.TrainingGroupId).SingleOrDefaultAsync(); if (group is null) return Results.NotFound();
        await resources.LockCalendar(t.AcademyId, [group.Value], "Edit"); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"attendance:{t.AcademyId:N}:{id:N}:{Guid.Empty:N}"}, 0))");
        var session = await db.TrainingSessions.SingleAsync(x => x.Id == id);
        if (!Enum.TryParse<TrainingSessionStatus>(request.Status, true, out var next) || !Enum.IsDefined(next)) return Validation("status", "حالة الحصة غير صالحة.");
        if (next == TrainingSessionStatus.Cancelled && (await db.PlayerAttendances.AnyAsync(x => x.AcademyId == t.AcademyId && x.TrainingSessionId == id && x.Status != AttendanceStatus.NotRecorded) || await db.StaffAttendances.AnyAsync(x => x.AcademyId == t.AcademyId && x.TrainingSessionId == id && x.Status != AttendanceStatus.NotRecorded))) return Results.Conflict(new { message = "لا يمكن إلغاء حصة بعد تسجيل حضور فعلي. صحح الحضور أولًا." });
        if (next == TrainingSessionStatus.Cancelled) await resources.CancelBookings(session, UserId(principal));
        var previous = session.Status; session.Status = next; session.UpdatedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync();
        if (previous == TrainingSessionStatus.Cancelled && next != TrainingSessionStatus.Cancelled) await resources.Reserve(session, UserId(principal));
        await tx.CommitAsync(); return Results.NoContent();
        } catch (FacilityRejected e) { return Results.Problem(statusCode: e.Status, title: e.Message); }
    }

    private static async Task<IResult> PlayerRoster(Guid id, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, Microsoft.AspNetCore.Authorization.IAuthorizationService authorization)
    {
        var t = (await tenant.ResolveAsync())!; var session = await AccessibleSession(id, tenant, principal, db); if (session is null) return Results.NotFound();
        var enrollments = await db.SportEnrollments.AsNoTracking().Include(x => x.Player).Where(x => x.AcademyId == t.AcademyId && FoundationDbContext.AssignedGroupAt(x.AcademyId, x.Id, session.SessionDate) == session.TrainingGroupId && x.IsActive).OrderBy(x => x.Player.ArabicName).ToListAsync();
        var enrollmentIds = enrollments.Select(x => x.Id).ToArray();
        var attendance = await db.PlayerAttendances.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.TrainingSessionId == id && enrollmentIds.Contains(x.SportEnrollmentId)).ToDictionaryAsync(x => x.SportEnrollmentId);
        var periods = await db.SubscriptionPeriods.AsNoTracking().Include(x => x.SubscriptionPlan).Where(x => x.AcademyId == t.AcademyId && enrollmentIds.Contains(x.SportEnrollmentId) && x.Status != SubscriptionPeriodStatus.Cancelled && x.Status != SubscriptionPeriodStatus.Frozen && x.StartDate <= session.SessionDate && (x.EndDate == null || x.EndDate >= session.SessionDate)).OrderBy(x => x.StartDate).ThenBy(x => x.CreatedAtUtc).ToListAsync();
        var rows = enrollments.Select(e =>
        {
            attendance.TryGetValue(e.Id, out var record); var candidates = periods.Where(x => x.SportEnrollmentId == e.Id && (x.ServiceKindSnapshot ?? x.SubscriptionPlan.ServiceKind) == AcademyServiceKind.Training && PlanCatalog.FormatMatches(e.RegistrationFormat, x.TrainingFormatSnapshot ?? x.SubscriptionPlan.TrainingFormat)).ToList(); var period = candidates.FirstOrDefault(x => (x.PlanTypeSnapshot ?? x.SubscriptionPlan.PlanType) == SubscriptionPlanType.Duration || x.RemainingSessions is > 0) ?? candidates.FirstOrDefault(); var type = period?.SubscriptionPlan.PlanType;
            return new { sportEnrollmentId = e.Id, playerId = e.PlayerId, e.Player.PlayerCode, e.Player.ArabicName, e.Player.PhotoReference, attendanceStatus = (record?.Status ?? AttendanceStatus.NotRecorded).ToString(), subscriptionPlan = period?.SubscriptionPlan.ArabicName, subscriptionType = type?.ToString(), remainingSessions = type is SubscriptionPlanType.Sessions or SubscriptionPlanType.Combined ? period?.RemainingSessions : null, sessionConsumed = record?.ConsumedSubscriptionPeriodId is not null, warning = period is null ? "لا يوجد اشتراك مؤهل" : type is SubscriptionPlanType.Sessions or SubscriptionPlanType.Combined && period.RemainingSessions == 0 ? "الرصيد غير كافٍ" : null };
        });
        var canManageAttendance = (await authorization.AuthorizeAsync(principal, AcademyPermissions.AttendanceManage)).Succeeded;
        return Results.Ok(new { canManageAttendance, session = new { session.Id, session.SessionDate, session.StartTime, session.EndTime, status = session.Status.ToString(), group = session.TrainingGroup.ArabicName, sport = session.Sport.ArabicName, branch = session.Branch.ArabicName }, players = rows });
    }

    private static async Task<IResult> SavePlayerAttendance(Guid id, PlayerAttendanceBatch request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, AttendanceService service)
    {
        var session = await AccessibleSession(id, tenant, principal, db); if (session is null) return Results.NotFound(); var t = (await tenant.ResolveAsync())!;
        if (request.Items is null) return Validation("items", "قائمة الحضور مطلوبة.");
        try { return Results.Ok(new { items = await service.SavePlayersAsync(t.AcademyId, id, UserId(principal), request.Items) }); }
        catch (AttendanceValidationException e) { return Validation("items", e.Message); } catch (AttendanceConflictException e) { return Results.Conflict(new { message = e.Message }); } catch (AttendanceNotFoundException) { return Results.NotFound(); }
    }

    private static async Task<IResult> StaffRoster(Guid id, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var session = await AccessibleSession(id, tenant, principal, db); if (session is null) return Results.NotFound(); var user = UserId(principal);
        var query = db.StaffGroupAssignments.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == session.TrainingGroupId && x.IsActive && x.AcademyMembership.IsActive);
        if (t.Role == AcademyRole.Coach) query = query.Where(x => x.AcademyMembership.UserId == user);
        return Results.Ok(await query.OrderBy(x => x.AcademyMembership.User.DisplayName).Select(x => new { academyMembershipId = x.AcademyMembershipId, name = x.AcademyMembership.User.DisplayName, role = x.AcademyMembership.Role.ToString(), attendanceStatus = db.StaffAttendances.Where(a => a.AcademyId == t.AcademyId && a.TrainingSessionId == id && a.AcademyMembershipId == x.AcademyMembershipId).Select(a => a.Status.ToString()).FirstOrDefault() ?? AttendanceStatus.NotRecorded.ToString() }).ToListAsync());
    }

    private static async Task<IResult> SaveStaffAttendance(Guid id, StaffAttendanceBatch request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, AttendanceService service)
    {
        var session = await AccessibleSession(id, tenant, principal, db); if (session is null) return Results.NotFound(); var t = (await tenant.ResolveAsync())!; var user = UserId(principal);
        if (request.Items is null) return Validation("items", "قائمة حضور الجهاز الفني مطلوبة.");
        if (t.Role == AcademyRole.Coach)
        {
            var own = await db.AcademyMemberships.Where(x => x.AcademyId == t.AcademyId && x.UserId == user && x.IsActive && x.Role == AcademyRole.Coach).Select(x => x.Id).SingleAsync(); if (request.Items.Any(x => x.AcademyMembershipId != own)) return Results.NotFound();
        }
        try { return Results.Ok(new { items = await service.SaveStaffAsync(t.AcademyId, id, user, request.Items) }); }
        catch (AttendanceValidationException e) { return Validation("items", e.Message); } catch (AttendanceConflictException e) { return Results.Conflict(new { message = e.Message }); } catch (AttendanceNotFoundException) { return Results.NotFound(); }
    }

    private static async Task<IResult> History(string? search, Guid? playerId, Guid? branchId, Guid? sportId, Guid? groupId, DateOnly? from, DateOnly? to, string? status, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var query = db.PlayerAttendances.AsNoTracking().Where(x => x.AcademyId == t.AcademyId);
        if (t.Role == AcademyRole.Coach) query = query.Where(x => db.StaffGroupAssignments.Any(a => a.AcademyId == t.AcademyId && a.TrainingGroupId == x.TrainingGroupId && a.AcademyMembership.UserId == user && a.IsActive));
        if (playerId.HasValue) query = query.Where(x => x.SportEnrollment.PlayerId == playerId);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.SportEnrollment.Player.ArabicName.Contains(search.Trim()) || x.SportEnrollment.Player.PlayerCode.Contains(search.Trim()));
        if (branchId.HasValue) query = query.Where(x => x.TrainingSession.BranchId == branchId); if (sportId.HasValue) query = query.Where(x => x.TrainingSession.SportId == sportId); if (groupId.HasValue) query = query.Where(x => x.TrainingGroupId == groupId); if (from.HasValue) query = query.Where(x => x.TrainingSession.SessionDate >= from); if (to.HasValue) query = query.Where(x => x.TrainingSession.SessionDate <= to); if (Enum.TryParse<AttendanceStatus>(status, true, out var parsed)) query = query.Where(x => x.Status == parsed);
        return Results.Ok(await query.OrderByDescending(x => x.TrainingSession.SessionDate).ThenBy(x => x.SportEnrollment.Player.ArabicName).Take(500).Select(x => new { x.Id, x.TrainingSessionId, x.SportEnrollment.Player.PlayerCode, player = x.SportEnrollment.Player.ArabicName, group = x.TrainingSession.TrainingGroup.ArabicName, sport = x.TrainingSession.Sport.ArabicName, branch = x.TrainingSession.Branch.ArabicName, x.TrainingSession.SessionDate, x.TrainingSession.StartTime, status = x.Status.ToString(), sessionConsumed = x.ConsumedSubscriptionPeriodId != null }).ToListAsync());
    }

    private static async Task<IResult> GuardianAttendance(Guid playerId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var linked = await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == t.AcademyId && x.Guardian.UserId == user && x.PlayerId == playerId && x.IsActive); if (!linked) return Results.NotFound();
        var rows = await db.PlayerAttendances.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.SportEnrollment.PlayerId == playerId && x.Status != AttendanceStatus.NotRecorded && x.TrainingSession.Status != TrainingSessionStatus.Cancelled).OrderByDescending(x => x.TrainingSession.SessionDate).Take(50).Select(x => new { x.Id, x.TrainingSession.SessionDate, x.TrainingSession.StartTime, group = x.TrainingSession.TrainingGroup.ArabicName, sport = x.TrainingSession.Sport.ArabicName, status = x.Status.ToString() }).ToListAsync();
        return Results.Ok(rows);
    }

    private static async Task<TrainingSession?> AccessibleSession(Guid id, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, bool include = false)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); IQueryable<TrainingSession> query = db.TrainingSessions.Where(x => x.AcademyId == t.AcademyId && x.Id == id);
        if (t.Role == AcademyRole.Coach) query = query.Where(x => db.StaffGroupAssignments.Any(a => a.AcademyId == t.AcademyId && a.TrainingGroupId == x.TrainingGroupId && a.AcademyMembership.UserId == user && a.IsActive));
        if (include) query = query.Include(x => x.TrainingGroup).Include(x => x.Sport).Include(x => x.Branch); else query = query.Include(x => x.TrainingGroup).Include(x => x.Sport).Include(x => x.Branch);
        return await query.SingleOrDefaultAsync();
    }
    private static bool IsAdmin(AcademyRole role) => role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin;
    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static IResult Validation(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
}

public sealed record ManualSessionRequest(Guid TrainingGroupId, DateOnly SessionDate, TimeOnly StartTime, TimeOnly EndTime, string? Notes);
public sealed record GenerateSessionsRequest(DateOnly From, DateOnly To);
public sealed record SessionStatusRequest(string Status);
public sealed record PlayerAttendanceBatch(IReadOnlyList<PlayerAttendanceChange>? Items);
public sealed record StaffAttendanceBatch(IReadOnlyList<StaffAttendanceChange>? Items);
