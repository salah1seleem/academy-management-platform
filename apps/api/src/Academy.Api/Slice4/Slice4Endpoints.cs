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

    private static async Task<IResult> ListSessions(DateOnly? from, DateOnly? to, bool? allDates, Guid? branchId, Guid? sportId, Guid? groupId, string? status, bool? upcomingOnly, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        var unfiltered = allDates == true && from is null && to is null;
        var start = from ?? today; var end = to ?? today;
        if (!unfiltered && (end < start || end.DayNumber - start.DayNumber > 366)) return Validation("range", "نطاق التاريخ يجب ألا يتجاوز سنة.");
        var query = db.TrainingSessions.AsNoTracking().Where(x => x.AcademyId == t.AcademyId);
        if (upcomingOnly == true)
        {
            if (from.HasValue || to.HasValue || allDates == true)
                return Validation("range", "الحصص القادمة لها نطاق مستقل؛ لا تجمعها مع فلتر تاريخ آخر.");
            var zoneId = await db.Academies.Where(a => a.Id == t.AcademyId).Select(a => a.TimeZone).SingleAsync();
            var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(zoneId));
            var now = TimeOnly.FromDateTime(local.DateTime);
            var horizon = today.AddDays(14);
            query = query.Where(x => x.Status == TrainingSessionStatus.Scheduled && x.SessionDate >= today && x.SessionDate <= horizon && (x.SessionDate > today || x.EndTime > now));
        }
        else if (!unfiltered) query = query.Where(x => x.SessionDate >= start && x.SessionDate <= end);
        if (t.Role == AcademyRole.Coach) query = ForCoach(query,t,db);
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
        if (coachId.HasValue) query = query.Where(x => FoundationDbContext.IsSessionStaffAssigned(t.AcademyId, coachId.Value, x.Id));
        if (playerId.HasValue) query = await SessionsForPlayers(query, [playerId.Value], t.AcademyId, db);
        if (guardianId.HasValue)
        {
            var children = await db.GuardianPlayerLinks.AsNoTracking().Where(l =>
                l.AcademyId == t.AcademyId && l.GuardianId == guardianId && l.IsActive && l.Guardian.IsActive)
                .Select(l => l.PlayerId).Distinct().ToArrayAsync();
            query = await SessionsForPlayers(query, children, t.AcademyId, db);
        }
        return await SessionRows(query, t.AcademyId, db);
    }

    private static async Task<IQueryable<TrainingSession>> SessionsForPlayers(IQueryable<TrainingSession> query, Guid[] players, Guid academy, FoundationDbContext db)
    {
        // Materialize only authorized candidates before invoking the dated SQL
        // function. A correlated guardian join otherwise evaluates it against
        // every enrollment per session. Keep historical attendance even when
        // the enrollment is no longer active, and retain all tenant/branch filters.
        var enrollments = await db.SportEnrollments.AsNoTracking().Where(e => e.AcademyId == academy && players.Contains(e.PlayerId))
            .Select(e => new { e.Id, e.IsActive, e.TrainingGroupId }).ToArrayAsync();
        var all = enrollments.Select(e => e.Id).ToArray();
        var active = enrollments.Where(e => e.IsActive).Select(e => e.Id).ToArray();
        var transfers = await db.EnrollmentGroupTransfers.Where(t => active.Contains(t.SportEnrollmentId)).Select(t => new { t.SourceGroupId, t.TargetGroupId }).ToArrayAsync();
        var moves = await db.EnrollmentSessionMoves.Where(m => active.Contains(m.SportEnrollmentId)).Select(m => new { m.SourceGroupId, m.TargetGroupId }).ToArrayAsync();
        var compensation = await db.CompensationBookings.Where(b => active.Contains(b.SportEnrollmentId) && b.State == CompensationBookingState.Reserved).Select(b => b.TrainingGroupId).ToArrayAsync();
        var groups = enrollments.Where(e => e.IsActive && e.TrainingGroupId.HasValue).Select(e => e.TrainingGroupId!.Value)
            .Concat(transfers.SelectMany(t => new[] { t.SourceGroupId, t.TargetGroupId }))
            .Concat(moves.SelectMany(m => new[] { m.SourceGroupId, m.TargetGroupId })).Concat(compensation).Distinct().ToArray();
        var historical = await db.PlayerAttendances.Where(a => a.AcademyId == academy && all.Contains(a.SportEnrollmentId)).Select(a => a.TrainingSessionId).Distinct().ToArrayAsync();
        var candidates = await query.Where(s => groups.Contains(s.TrainingGroupId) || historical.Contains(s.Id)).Select(s => s.Id).ToArrayAsync();
        return query.Where(s => candidates.Contains(s.Id) && (db.SportEnrollments.Any(e => e.AcademyId == academy && active.Contains(e.Id) &&
            // CASE keeps the expensive function behind the cheap per-enrollment
            // candidate check even if PostgreSQL reorders other WHERE clauses.
            ((e.TrainingGroupId == s.TrainingGroupId ||
              db.EnrollmentGroupTransfers.Any(t => t.SportEnrollmentId == e.Id && (t.SourceGroupId == s.TrainingGroupId || t.TargetGroupId == s.TrainingGroupId)) ||
              db.EnrollmentSessionMoves.Any(m => m.SportEnrollmentId == e.Id && (m.SourceGroupId == s.TrainingGroupId || m.TargetGroupId == s.TrainingGroupId)) ||
              db.CompensationBookings.Any(b => b.SportEnrollmentId == e.Id && b.TrainingGroupId == s.TrainingGroupId && b.State == CompensationBookingState.Reserved))
                ? FoundationDbContext.IsSessionAssigned(e.AcademyId, e.Id, s.Id) : false)) ||
            db.PlayerAttendances.Any(a => a.AcademyId == academy && a.TrainingSessionId == s.Id && all.Contains(a.SportEnrollmentId))));
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
            group = x.TrainingGroup.ArabicName, sport = x.Sport.ArabicName, branch = x.Branch.ArabicName
        }).ToListAsync();
        if(rows.Count==0)return Results.Ok(Array.Empty<object>());
        var ids=rows.Select(s=>s.Id).ToArray();var groups=rows.Select(s=>s.TrainingGroupId).Distinct().ToArray();
        var roster=await FacilityRosterCounts.Read(db,academyId,groups,ids,rows.Max(s=>s.SessionDate));
        var records=await db.PlayerAttendances.AsNoTracking().Where(a=>ids.Contains(a.TrainingSessionId)).GroupBy(a=>a.TrainingSessionId).Select(g=>new{session=g.Key,recorded=g.Count(a=>a.Status!=AttendanceStatus.NotRecorded),present=g.Count(a=>a.Status==AttendanceStatus.Present||a.Status==AttendanceStatus.Late),late=g.Count(a=>a.Status==AttendanceStatus.Late),excused=g.Count(a=>a.Status==AttendanceStatus.Excused),absent=g.Count(a=>a.Status==AttendanceStatus.Absent)}).ToDictionaryAsync(x=>x.session);
        var resources=await(from b in db.ResourceBookings join r in db.FacilityResources on b.ResourceId equals r.Id join s in db.FacilitySpaces on r.FacilitySpaceId equals s.Id where ids.Contains(b.TrainingSessionId??Guid.Empty)&&b.State==Academy.Infrastructure.Facilities.ResourceBookingState.Reserved select new{session=b.TrainingSessionId!.Value,resource=s.Name+" · "+r.Name}).ToArrayAsync();
        var assignedStaff=await SessionStaffReadModel.Read(db,academyId,ids,groups);
        var presentation=await SessionPresentationReadModel.Read(db,academyId,ids);
        var staffAttendance=await db.StaffAttendances.AsNoTracking().Where(a=>ids.Contains(a.TrainingSessionId)).ToDictionaryAsync(a=>(a.TrainingSessionId,a.AcademyMembershipId));var result=new List<object>();
        foreach(var row in rows){
            var staff=assignedStaff[row.Id];records.TryGetValue(row.Id,out var counts);
            var display=presentation[row.Id];
            result.Add(new{row.Id,row.SessionDate,row.StartTime,row.EndTime,row.status,row.source,row.TrainingGroupId,row.group,row.sport,row.branch,display.TrainingFormat,display.TrainingFormatSource,display.IsSubstitute,resource=resources.FirstOrDefault(r=>r.session==row.Id)?.resource,coaches=staff.Where(m=>m.Role==AcademyRole.Coach).Select(m=>m.Name).ToArray(),playerCount=roster.Sessions.GetValueOrDefault(row.Id),recorded=counts?.recorded??0,present=counts?.present??0,late=counts?.late??0,excused=counts?.excused??0,absent=counts?.absent??0,staff=staff.Select(m=>new{id=m.Id,name=m.Name,attendanceStatus=staffAttendance.GetValueOrDefault((row.Id,m.Id))?.Status.ToString()??"NotRecorded"}).ToArray()});
        }
        return Results.Ok(result);
    }
    private static IQueryable<TrainingSession> ForCoach(IQueryable<TrainingSession> query,TenantMembership t,FoundationDbContext db)
    {
        var groups=db.StaffGroupAssignments.Where(a=>a.AcademyId==t.AcademyId&&a.AcademyMembershipId==t.MembershipId&&a.IsActive).Select(a=>a.TrainingGroupId);
        var replacements=db.SessionCoachOverrides.Where(o=>o.AcademyId==t.AcademyId&&o.ActualCoachMembershipId==t.MembershipId).Select(o=>o.TrainingSessionId);
        return query.Where(s=>(groups.Contains(s.TrainingGroupId)||replacements.Contains(s.Id))&&FoundationDbContext.IsSessionStaffAssigned(t.AcademyId,t.MembershipId,s.Id));
    }

    private static async Task<IResult> GetSession(Guid id, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var session = await AccessibleSession(id, tenant, principal, db, true); if (session is null) return Results.NotFound();
        var t = (await tenant.ResolveAsync())!;
        var display = (await SessionPresentationReadModel.Read(db, t.AcademyId, [id]))[id];
        return Results.Ok(new { session.Id, session.SessionDate, session.StartTime, session.EndTime, status = session.Status.ToString(), source = session.Source.ToString(), session.Notes, group = session.TrainingGroup.ArabicName, sport = session.Sport.ArabicName, branch = session.Branch.ArabicName, display.TrainingFormat, display.TrainingFormatSource, display.IsSubstitute });
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
        if (next == TrainingSessionStatus.Cancelled && await db.PrivateServiceDeliveries.AnyAsync(d => d.TrainingSessionId == id)) return Results.Conflict(new { message = "الحصة لها تنفيذ خاص معتمد؛ يلزم تصحيح مالي موثق، لا إلغاء مصدر الخدمة." });
        if (next == TrainingSessionStatus.Cancelled && (await db.PlayerAttendances.AnyAsync(x => x.AcademyId == t.AcademyId && x.TrainingSessionId == id && x.Status != AttendanceStatus.NotRecorded) || await db.StaffAttendances.AnyAsync(x => x.AcademyId == t.AcademyId && x.TrainingSessionId == id && x.Status != AttendanceStatus.NotRecorded))) return Results.Conflict(new { message = "لا يمكن إلغاء حصة بعد تسجيل حضور فعلي. صحح الحضور أولًا." });
        if (next == TrainingSessionStatus.Cancelled) await resources.CancelBookings(session, UserId(principal));
        var previous = session.Status; session.Status = next; session.UpdatedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync();
        if (previous == TrainingSessionStatus.Cancelled && next != TrainingSessionStatus.Cancelled) await resources.Reserve(session, UserId(principal));
        await tx.CommitAsync(); return Results.NoContent();
        } catch (FacilityRejected e) { return Results.Problem(statusCode: e.Status, title: e.Message); }
    }

    private static async Task<IResult> PlayerRoster(Guid id, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, Microsoft.AspNetCore.Authorization.IAuthorizationService authorization, TimeProvider clock, AttendanceCorrectionService corrections)
    {
        var t = (await tenant.ResolveAsync())!; var session = await AccessibleSession(id, tenant, principal, db); if (session is null) return Results.NotFound();
        var enrollments = await db.SportEnrollments.AsNoTracking().Include(x => x.Player).Where(x => x.AcademyId == t.AcademyId && FoundationDbContext.IsSessionAssigned(x.AcademyId, x.Id, session.Id) && x.IsActive).OrderBy(x => x.Player.ArabicName).ToListAsync();
        var enrollmentIds = enrollments.Select(x => x.Id).ToArray();
        var compensation = await db.CompensationBookings.AsNoTracking().Where(b => b.TrainingSessionId == id && b.State == Academy.Infrastructure.People.CompensationBookingState.Reserved).Select(b => b.SportEnrollmentId).ToArrayAsync();
        var attendance = await db.PlayerAttendances.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.TrainingSessionId == id && enrollmentIds.Contains(x.SportEnrollmentId)).ToDictionaryAsync(x => x.SportEnrollmentId);
        var periods = await db.SubscriptionPeriods.AsNoTracking().Include(x => x.SubscriptionPlan).Where(x => x.AcademyId == t.AcademyId && enrollmentIds.Contains(x.SportEnrollmentId) && x.Status != SubscriptionPeriodStatus.Cancelled && x.Status != SubscriptionPeriodStatus.Frozen && x.StartDate <= session.SessionDate && (x.EndDate == null || x.EndDate >= session.SessionDate)).OrderBy(x => x.StartDate).ThenBy(x => x.CreatedAtUtc).ToListAsync();
        var rows = enrollments.Select(e =>
        {
            attendance.TryGetValue(e.Id, out var record); var candidates = periods.Where(x => x.SportEnrollmentId == e.Id && (x.ServiceKindSnapshot ?? x.SubscriptionPlan.ServiceKind) == AcademyServiceKind.Training && PlanCatalog.FormatMatches(e.RegistrationFormat, x.TrainingFormatSnapshot ?? x.SubscriptionPlan.TrainingFormat)).ToList(); var period = candidates.FirstOrDefault(x => (x.PlanTypeSnapshot ?? x.SubscriptionPlan.PlanType) == SubscriptionPlanType.Duration || x.RemainingSessions is > 0) ?? candidates.FirstOrDefault(); var type = period?.SubscriptionPlan.PlanType;
            return new { sportEnrollmentId = e.Id, playerId = e.PlayerId, e.Player.PlayerCode, e.Player.ArabicName, e.Player.PhotoReference, attendanceStatus = (record?.Status ?? AttendanceStatus.NotRecorded).ToString(), version = record?.Version ?? 0, subscriptionPlan = period?.SubscriptionPlan.ArabicName, subscriptionType = type?.ToString(), remainingSessions = type is SubscriptionPlanType.Sessions or SubscriptionPlanType.Combined ? period?.RemainingSessions : null, sessionConsumed = record?.ConsumedSubscriptionPeriodId is not null, compensatory = compensation.Contains(e.Id), warning = compensation.Contains(e.Id) ? "حصة تعويضية معتمدة؛ لا خصم من الرصيد الأصلي" : period is null ? "لا يوجد اشتراك مؤهل" : type is SubscriptionPlanType.Sessions or SubscriptionPlanType.Combined && period.RemainingSessions == 0 ? "الرصيد غير كافٍ" : null };
        });
        var context = await corrections.Context(id);
        var canEvaluate = (await authorization.AuthorizeAsync(principal, AcademyPermissions.EvaluationManage)).Succeeded;
        var display = (await SessionPresentationReadModel.Read(db, t.AcademyId, [id]))[id];
        return Results.Ok(new { canManageAttendance=context.CanManageAttendance, canRequestCorrection=context.CanRequestCorrection, isPast=context.IsPast, context.DraftLease, context.DraftScope, context.DraftExpiresAtUtc, canEvaluate, lastSynchronizedAtUtc = clock.GetUtcNow(), session = new { session.Id, session.SessionDate, session.StartTime, session.EndTime, status = session.Status.ToString(), group = session.TrainingGroup.ArabicName, sport = session.Sport.ArabicName, branch = session.Branch.ArabicName, display.TrainingFormat, display.TrainingFormatSource, display.IsSubstitute }, players = rows });
    }

    private static async Task<IResult> SavePlayerAttendance(Guid id, PlayerAttendanceBatch request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, AttendanceService service, AttendanceDraftGuard drafts)
    {
        var session = await AccessibleSession(id, tenant, principal, db); if (session is null) return Results.NotFound(); var t = (await tenant.ResolveAsync())!;
        if (request.Items is null) return Validation("items", "قائمة الحضور مطلوبة.");
        if (!drafts.Valid(t, principal, id, request.DraftLease)) return Results.Conflict(new { message = "انتهت صلاحية المسودة أو تغير حساب الدخول؛ افتح كشفًا جديدًا." });
        try { return Results.Ok(new { items = await service.SavePlayersAsync(t.AcademyId, id, UserId(principal), request.Items) }); }
        catch (AttendanceValidationException e) { return Validation("items", e.Message); } catch (AttendanceConflictException e) { return Results.Conflict(new { message = e.Message }); } catch (AttendanceNotFoundException) { return Results.NotFound(); }
    }

    private static async Task<IResult> StaffRoster(Guid id, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var session = await AccessibleSession(id, tenant, principal, db); if (session is null) return Results.NotFound(); var user = UserId(principal);
        var query = db.AcademyMemberships.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && FoundationDbContext.IsSessionStaffAssigned(t.AcademyId, x.Id, session.Id));
        if (t.Role == AcademyRole.Coach) query = query.Where(x => x.UserId == user);
        var members = await query.OrderBy(x => x.User.DisplayName).Select(x => new { academyMembershipId = x.Id, name = x.User.DisplayName, role = x.Role.ToString() }).ToListAsync();
        var records = await db.StaffAttendances.AsNoTracking().Where(a => a.AcademyId == t.AcademyId && a.TrainingSessionId == id).ToDictionaryAsync(a => a.AcademyMembershipId);
        return Results.Ok(members.Select(x => { records.TryGetValue(x.academyMembershipId, out var record); return new { x.academyMembershipId, x.name, x.role, version = record?.Version ?? 0, attendanceStatus = (record?.Status ?? AttendanceStatus.NotRecorded).ToString() }; }));
    }

    private static async Task<IResult> SaveStaffAttendance(Guid id, StaffAttendanceBatch request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, AttendanceService service, AttendanceDraftGuard drafts)
    {
        var session = await AccessibleSession(id, tenant, principal, db); if (session is null) return Results.NotFound(); var t = (await tenant.ResolveAsync())!; var user = UserId(principal);
        if (request.Items is null) return Validation("items", "قائمة حضور الجهاز الفني مطلوبة.");
        if (!drafts.Valid(t, principal, id, request.DraftLease)) return Results.Conflict(new { message = "انتهت صلاحية المسودة أو تغير حساب الدخول؛ افتح كشفًا جديدًا." });
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
        if (t.Role == AcademyRole.Coach) query = query.Where(x => db.AcademyMemberships.Any(m => m.AcademyId == t.AcademyId && m.UserId == user && FoundationDbContext.IsSessionStaffAssigned(t.AcademyId, m.Id, x.TrainingSessionId)));
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
        if (t.Role == AcademyRole.Coach) query = ForCoach(query,t,db);
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
public sealed record PlayerAttendanceBatch(IReadOnlyList<PlayerAttendanceChange>? Items, string? DraftLease = null);
public sealed record StaffAttendanceBatch(IReadOnlyList<StaffAttendanceChange>? Items, string? DraftLease = null);
