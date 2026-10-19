using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static partial class GuardianFeedbackEndpoints
{
    public static void MapGuardianFeedback(this WebApplication app)
    {
        var guardian = app.MapGroup("/api/v1/guardian/children/{playerId:guid}").RequireAuthorization(AcademyPermissions.GuardianChildrenRead);
        guardian.MapGet("/feedback-options", Options);
        guardian.MapGet("/feedback-incidents", IncidentOptions);
        guardian.MapPost("/feedback", Submit).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("guardian-feedback");
        var owner = app.MapGroup("/api/v1/manage/feedback").RequireAuthorization(AcademyPermissions.OwnerDashboardRead);
        owner.MapGet("/", List);
        owner.MapGet("/{id:guid}", Detail);
        owner.MapPost("/{id:guid}/review", Review).AddEndpointFilter<CsrfFilter>();
        owner.MapPost("/{id:guid}/due", SetDue).AddEndpointFilter<CsrfFilter>();
        owner.MapPost("/{id:guid}/resolve", Resolve).AddEndpointFilter<CsrfFilter>();
        owner.MapPost("/{id:guid}/transition", Transition).AddEndpointFilter<CsrfFilter>();
        owner.MapPost("/{id:guid}/triage", Triage).AddEndpointFilter<CsrfFilter>();
        owner.MapGet("/{id:guid}/investigators", Investigators);
        owner.MapPost("/{id:guid}/assignment", AssignInvestigation).AddEndpointFilter<CsrfFilter>();
        var investigations = app.MapGroup("/api/v1/my-followups/investigations").RequireAuthorization(AcademyPermissions.FollowUpRead);
        investigations.MapGet("/", MyInvestigations);
        investigations.MapGet("/{assignmentId:guid}", InvestigationDetail);
        investigations.MapPost("/{assignmentId:guid}/transition", SubmitFindings).RequireAuthorization(AcademyPermissions.FollowUpManage).AddEndpointFilter<CsrfFilter>();
    }

    private static Guid Actor(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static async Task<bool> Linked(FoundationDbContext db, Guid academy, Guid actor, Guid player) =>
        await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == academy && x.PlayerId == player && x.IsActive && x.Guardian.IsActive && x.Guardian.UserId == actor && x.Player.IsActive);

    private static async Task<IResult> Options(Guid playerId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock, HttpContext http)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        var t = (await tenant.ResolveAsync())!; var actor = Actor(principal);
        if (t.Role != AcademyRole.Guardian) return Results.Forbid();
        if (!await Linked(db, t.AcademyId, actor, playerId)) return Results.NotFound();
        var academyZone = await db.Academies.Where(x => x.Id == t.AcademyId).Select(x => x.TimeZone).SingleAsync();
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(academyZone));
        var periodMonth = new DateOnly(local.Year, local.Month, 1);
        var groupRows = await db.SportEnrollments.Where(x => x.AcademyId == t.AcademyId && x.PlayerId == playerId && x.IsActive && x.Status == EnrollmentStatus.Active && x.TrainingGroupId != null && x.TrainingGroup!.IsActive)
            .Select(x => new { id = x.TrainingGroupId!.Value, name = x.TrainingGroup!.ArabicName, sport = x.Sport.ArabicName }).Distinct().ToArrayAsync();
        var groupIds = groupRows.Select(x => x.id).ToArray();
        var assigned = await db.StaffGroupAssignments.Where(a => a.AcademyId == t.AcademyId && groupIds.Contains(a.TrainingGroupId) && a.IsActive && a.AcademyMembership.IsActive && a.AcademyMembership.Role == AcademyRole.Coach)
            .Select(a => new { a.TrainingGroupId, id = a.AcademyMembershipId, name = a.AcademyMembership.User.DisplayName }).ToArrayAsync();
        var groups = groupRows.Select(g => new { g.id, g.name, g.sport, coaches = assigned.Where(a => a.TrainingGroupId == g.id).Select(a => new { a.id, a.name }).Distinct().ToArray() }).ToArray();
        var mine = await db.GuardianFeedbacks.Where(x => x.AcademyId == t.AcademyId && x.PlayerId == playerId && x.GuardianUserId == actor)
            .OrderByDescending(x => x.CreatedAtUtc).Take(30).Select(x => new { x.Id, x.Kind, x.CoachMembershipId, x.TrainingGroupId, x.Score, x.Message, x.Status, x.OwnerResponse, x.RespondedAtUtc, x.PeriodMonth, x.CreatedAtUtc, x.ReportedSubject, x.ReportedUrgency, x.IncidentOn, x.IncidentTrainingSessionId }).ToArrayAsync();
        return Results.Ok(new { periodMonth, asOfDate = DateOnly.FromDateTime(local.DateTime), groups, mine });
    }

    private static async Task<IResult> Submit(Guid playerId, FeedbackWrite body, HttpContext context, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var actor = Actor(context.User);
        if (t.Role != AcademyRole.Guardian) return Results.Forbid();
        if (!await Linked(db, t.AcademyId, actor, playerId)) return Results.NotFound();
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        var message = body.Message?.Trim() ?? "";
        var subject = body.Kind == "Rating" ? null : body.Subject ?? "Other";
        var urgency = body.Kind == "Rating" ? null : body.Urgency ?? "Routine";
        if (body.IncidentSessionId.HasValue && (body.Kind == "Rating" || !body.IncidentOn.HasValue) || body.Kind == "Rating" && (body.Subject != null || body.Urgency != null || body.IncidentOn != null) || body.Kind != "Rating" && (!FeedbackSubjects.Contains(subject!) || !FeedbackUrgencies.Contains(urgency!)) || body.IncidentOn.HasValue && (body.IncidentOn == DateOnly.MinValue || body.IncidentOn > await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock)))
            return Results.UnprocessableEntity(new { message = "اختر موضوعًا واستعجالًا صحيحين، وتاريخ واقعة لا يتجاوز اليوم الفعلي." });
        if (key.Length is < 8 or > 100 || body.Kind is not ("Rating" or "Complaint" or "Suggestion") || message.Length > 500 ||
            body.Kind == "Rating" && (body.CoachMembershipId is null || body.Score is < 1 or > 5 || body.Score is null) ||
            body.Kind != "Rating" && (body.Score is not null || message.Length < 5))
            return Results.UnprocessableEntity(new { message = "اختر نوع الملاحظة ومجموعة الطفل، مع تقييم 1–5 أو نص واضح للشكوى والاقتراح." });
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"guardian-feedback:{t.AcademyId}:{actor}:{key}"}, 0))");
        var previous = await db.GuardianFeedbacks.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.GuardianUserId == actor && x.IdempotencyKey == key);
        if (previous is not null)
            return previous.PlayerId == playerId && previous.Kind == body.Kind && previous.TrainingGroupId == body.TrainingGroupId && previous.CoachMembershipId == body.CoachMembershipId && previous.Score == body.Score && previous.Message == message && previous.IncidentOn == body.IncidentOn && previous.IncidentTrainingSessionId == body.IncidentSessionId
                && (previous.ReportedSubject == subject || previous.ReportedSubject == null && body.Subject == null) && (previous.ReportedUrgency == urgency || previous.ReportedUrgency == null && body.Urgency == null)
                ? Results.Ok(new { previous.Id, replay = true }) : Results.Conflict(new { message = "هذا المفتاح استُخدم لطلب مختلف." });
        if (body.IncidentSessionId.HasValue) {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"timetable:{t.AcademyId}:{body.TrainingGroupId}"},0))");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"group-capacity:{t.AcademyId}:{body.TrainingGroupId}"},0))");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"GuardianPlayerLinks\" WHERE \"AcademyId\"={t.AcademyId} AND \"PlayerId\"={playerId} AND \"IsActive\" FOR SHARE");
            if (!await Linked(db,t.AcademyId,actor,playerId)) return Results.NotFound();
        }
        var incident = body.IncidentSessionId.HasValue ? (await IncidentSessions(db, t.AcademyId, playerId, body.IncidentOn!.Value)).SingleOrDefault(s => s.Id == body.IncidentSessionId && s.GroupId == body.TrainingGroupId) : null;
        if (body.IncidentSessionId.HasValue && (incident == null || body.CoachMembershipId.HasValue && !incident.Coaches.Any(c => c.Id == body.CoachMembershipId))) return Results.NotFound();
        var enrollment = await db.SportEnrollments.Where(x => x.AcademyId == t.AcademyId && x.PlayerId == playerId && x.IsActive && x.Status == EnrollmentStatus.Active && x.TrainingGroupId == body.TrainingGroupId && x.TrainingGroup!.IsActive)
            .Select(x => new { x.BranchId }).FirstOrDefaultAsync();
        if (incident == null && enrollment is null) return Results.NotFound();
        if (incident == null && body.CoachMembershipId.HasValue && !await db.StaffGroupAssignments.AnyAsync(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == body.TrainingGroupId && x.AcademyMembershipId == body.CoachMembershipId && x.IsActive && x.AcademyMembership.IsActive && x.AcademyMembership.Role == AcademyRole.Coach)) return Results.NotFound();
        var zone = await db.Academies.Where(x => x.Id == t.AcademyId).Select(x => x.TimeZone).SingleAsync();
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(zone));
        var period = new DateOnly(local.Year, local.Month, 1);
        if (body.Kind == "Rating" && await db.GuardianFeedbacks.AnyAsync(x => x.AcademyId == t.AcademyId && x.Kind == "Rating" && x.GuardianUserId == actor && x.PlayerId == playerId && x.CoachMembershipId == body.CoachMembershipId && x.PeriodMonth == period))
            return Results.Conflict(new { message = "أرسلت تقييمًا لهذا المدرب وهذا الطفل خلال الشهر الحالي بالفعل." });
        var now = clock.GetUtcNow();
        var evidence = incident == null ? null : IncidentSnapshot(incident, body.CoachMembershipId);
        if (evidence?.Length > 10000) return Results.UnprocessableEntity(new { message = "سجل الحصة يتجاوز حد المراجعة؛ اختر ملاحظة عامة لتراجع الإدارة التفاصيل." });
        var feedback = new GuardianFeedback { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerId = playerId, GuardianUserId = actor,
            BranchId = incident?.BranchId ?? enrollment!.BranchId, TrainingGroupId = body.TrainingGroupId, CoachMembershipId = body.CoachMembershipId,
            Kind = body.Kind, Score = body.Score, Message = message, PeriodMonth = period, Status = "Open", IdempotencyKey = key,
            Subject = subject, ReportedSubject = subject, Urgency = urgency, ReportedUrgency = urgency, IncidentOn = body.IncidentOn,
            IncidentTrainingSessionId = body.IncidentSessionId, IncidentSessionEvidence = evidence,
            CreatedAtUtc = now, UpdatedAtUtc = now };
        db.GuardianFeedbacks.Add(feedback);
        if (urgency == "Safety")
        {
            var owners = await db.AcademyMemberships.Where(m => m.AcademyId == t.AcademyId && m.IsActive && m.User.IsActive && m.Role == AcademyRole.AcademyOwner).Select(m => m.UserId).Distinct().ToArrayAsync();
            foreach (var ownerId in owners) db.AcademyNotices.Add(new AcademyNotice { Id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"academy/feedback-safety/{feedback.Id}/{ownerId}"))[..16]), AcademyId = t.AcademyId, UserId = ownerId, Message = "بلاغ سلامة جديد يحتاج مراجعة بشرية. افتح ملاحظات الأسر من حساب المالك.", CreatedAtUtc = now, UpdatedAtUtc = now });
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Created($"/api/v1/guardian/children/{playerId}/feedback-options", new { feedback.Id });
    }

    private static async Task<IResult> Detail(Guid id, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock, HttpContext http)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        http.Response.Headers.CacheControl = "private, no-store";
        var item = await db.GuardianFeedbacks.AsNoTracking().SingleOrDefaultAsync(f => f.AcademyId == t.AcademyId && f.Id == id && f.IsActive);
        if (item == null) return Results.NotFound();
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        var history = await db.FeedbackChanges.AsNoTracking().Where(c => c.AcademyId == t.AcademyId && c.FeedbackId == id)
            .OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.Id).Take(51)
            .Select(c => new { c.Id, c.Action, c.FromStatus, c.ToStatus, c.BeforeDueOn, c.AfterDueOn, c.InternalNote, c.PublicResponse, c.CreatedAtUtc,
                c.InvestigationId, c.InvestigatorMembershipId, c.BeforeSubject, c.AfterSubject, c.BeforeUrgency, c.AfterUrgency,
                investigatorName = db.AcademyMemberships.Where(m => m.Id == c.InvestigatorMembershipId).Select(m => m.User.DisplayName).FirstOrDefault(),
                actorName = db.AcademyMemberships.Where(m => m.AcademyId == t.AcademyId && m.Id == c.ActorMembershipId).Select(m => m.User.DisplayName).FirstOrDefault() }).ToArrayAsync();
        var timeZone = await db.Academies.Where(a => a.Id == t.AcademyId).Select(a => a.TimeZone).SingleAsync();
        return Results.Ok(new { asOfDate = today, timeZone, item.Id, item.Version, item.Kind, item.Score, item.Message, item.Status, item.DueOn,
            item.Subject, item.Urgency, item.ReportedSubject, item.ReportedUrgency, item.IncidentOn, item.TriagedAtUtc, item.IncidentTrainingSessionId, item.IncidentSessionEvidence,
            safety = item.Urgency == "Safety" || item.ReportedUrgency == "Safety",
            item.InvestigationId, item.InvestigatorMembershipId, item.InvestigationBrief,
            investigatorName = await db.AcademyMemberships.Where(m => m.Id == item.InvestigatorMembershipId).Select(m => m.User.DisplayName).FirstOrDefaultAsync(),
            history = history.Take(50), historyHasMore = history.Length > 50, historyLimit = 50,
            overdue = item.Status != "Resolved" && item.Status != "Closed" && item.DueOn < today, item.OwnerResponse, item.RespondedAtUtc,
            item.CreatedAtUtc, item.ReviewedAtUtc, item.PlayerId, item.TrainingGroupId,
            playerName = await db.Players.Where(p => p.AcademyId == t.AcademyId && p.Id == item.PlayerId).Select(p => p.ArabicName).FirstOrDefaultAsync(),
            groupName = await db.TrainingGroups.Where(g => g.AcademyId == t.AcademyId && g.Id == item.TrainingGroupId).Select(g => g.ArabicName).FirstOrDefaultAsync(),
            branchName = await db.Branches.Where(b => b.AcademyId == t.AcademyId && b.Id == item.BranchId).Select(b => b.ArabicName).FirstOrDefaultAsync(),
            coachName = await db.AcademyMemberships.Where(m => m.AcademyId == t.AcademyId && m.Id == item.CoachMembershipId).Select(m => m.User.DisplayName).FirstOrDefaultAsync() });
    }

    private static async Task<IResult> List(Guid? branchId, Guid? coachId, Guid? groupId, string? kind, string? status, bool? overdue, string? subject, string? urgency, bool? safetyOnly, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock, HttpContext h)
    {
        h.Response.Headers.CacheControl = "private, no-store";
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        if (kind is not (null or "Rating" or "Complaint" or "Suggestion") || status != null && status != "Unresolved" && !CaseStatuses.Contains(status)) return Results.BadRequest(new { message = "فلتر غير صالح." });
        if (subject != null && !FeedbackSubjects.Contains(subject) || urgency != null && !FeedbackUrgencies.Contains(urgency)) return Results.BadRequest(new { message = "راجع فلتر الموضوع والاستعجال." });
        if (branchId.HasValue && !await db.Branches.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == branchId)) return Results.NotFound();
        if (groupId.HasValue && !await db.TrainingGroups.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == groupId)) return Results.NotFound();
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        return Results.Ok(await db.GuardianFeedbacks.Where(x => x.IsActive && x.AcademyId == t.AcademyId && (!branchId.HasValue || x.BranchId == branchId) && (!groupId.HasValue || x.TrainingGroupId == groupId) && (!coachId.HasValue || x.CoachMembershipId == coachId) && (kind == null || x.Kind == kind) && (status == null || status == "Unresolved" && x.Status != "Resolved" && x.Status != "Closed" || x.Status == status)
            && (subject == null || x.Subject == subject) && (urgency == null || x.Urgency == urgency) && (safetyOnly != true || x.Urgency == "Safety" || x.ReportedUrgency == "Safety")
            && (overdue != true || x.Kind != "Rating" && x.Status != "Resolved" && x.Status != "Closed" && x.DueOn != null && x.DueOn < today))
            .OrderByDescending(x => x.Status != "Resolved" && x.Status != "Closed" && (x.Urgency == "Safety" || x.ReportedUrgency == "Safety")).ThenByDescending(x => x.Status != "Resolved" && x.Status != "Closed" && x.Urgency == "Urgent").ThenByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(200)
            .Select(x => new { x.Id, x.Kind, x.Score, x.Message, x.Status, x.DueOn, x.Subject, x.Urgency, x.TriagedAtUtc, safety = x.Urgency == "Safety" || x.ReportedUrgency == "Safety", overdue = x.Status != "Resolved" && x.Status != "Closed" && x.DueOn != null && x.DueOn < today, x.OwnerResponse, x.RespondedAtUtc, x.CreatedAtUtc, x.ReviewedAtUtc, x.PlayerId, playerName = db.Players.Where(p => p.AcademyId == t.AcademyId && p.Id == x.PlayerId).Select(p => p.ArabicName).FirstOrDefault(), x.BranchId, branchName = db.Branches.Where(b => b.AcademyId == t.AcademyId && b.Id == x.BranchId).Select(b => b.ArabicName).FirstOrDefault(), x.CoachMembershipId, coachName = db.AcademyMemberships.Where(c => c.AcademyId == t.AcademyId && c.Id == x.CoachMembershipId).Select(c => c.User.DisplayName).FirstOrDefault() }).ToArrayAsync());
    }

    private static async Task<IResult> SetDue(Guid id, FeedbackDueDate body, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        if (body.DueOn == default) return Results.UnprocessableEntity(new { message = "اختر تاريخ متابعة صالحًا." });
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await LockOwner(db, t)) return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"guardian-feedback-resolve:{t.AcademyId}:{id}"}, 0))");
        var item = await db.GuardianFeedbacks.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.IsActive);
        if (item is null) return Results.NotFound();
        if (item.Kind == "Rating" || item.Status is "Resolved" or "Closed") return Results.UnprocessableEntity(new { message = "الموعد للشكاوى والاقتراحات المفتوحة فقط." });
        if (item.DueOn.HasValue) return item.DueOn == body.DueOn ? Results.Ok(new { item.Id, replay = true }) : Results.Conflict(new { message = "حُدد موعد المتابعة بالفعل؛ لا يُبدل بصمت." });
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        if (body.DueOn < today) return Results.UnprocessableEntity(new { message = "موعد المتابعة لا يسبق اليوم." });
        item.DueOn = body.DueOn; item.DueSetByUserId = Actor(principal); item.DueSetAtUtc = clock.GetUtcNow(); item.UpdatedAtUtc = clock.GetUtcNow();
        var change = RecordChange(db, item, t, clock.GetUtcNow(), "Due", item.Status, null);
        await db.SaveChangesAsync(); db.FeedbackChanges.Add(change); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { item.Id, item.DueOn });
    }

    private static async Task<IResult> Review(Guid id, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await LockOwner(db, t)) return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"guardian-feedback-resolve:{t.AcademyId}:{id}"}, 0))");
        var item = await db.GuardianFeedbacks.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.IsActive);
        if (item is null) return Results.NotFound();
        if (item.Status != "Open") return Results.Ok(new { item.Id, replay = true });
        item.Status = "Reviewed"; item.ReviewedByUserId = Actor(principal); item.ReviewedAtUtc = clock.GetUtcNow(); item.UpdatedAtUtc = clock.GetUtcNow();
        var change = RecordChange(db, item, t, clock.GetUtcNow(), "Review", "Open", item.DueOn);
        await db.SaveChangesAsync(); db.FeedbackChanges.Add(change); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { item.Id });
    }

    private static async Task<IResult> Resolve(Guid id, FeedbackResolution body, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        var response = body.Response?.Trim() ?? "";
        if (response.Length is < 5 or > 500) return Results.UnprocessableEntity(new { message = "اكتب ردًا واضحًا من 5 إلى 500 حرف." });
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await LockOwner(db, t)) return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"guardian-feedback-resolve:{t.AcademyId}:{id}"}, 0))");
        var item = await db.GuardianFeedbacks.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.IsActive);
        if (item is null) return Results.NotFound();
        if (item.Kind == "Rating") return Results.UnprocessableEntity(new { message = "الردّ متاح للشكاوى والاقتراحات فقط." });
        if (item.Status == "Resolved") return item.OwnerResponse == response
            ? Results.Ok(new { item.Id, replay = true }) : Results.Conflict(new { message = "حُلّت الملاحظة برد مختلف بالفعل." });
        if (item.Status == "Closed") return Results.Conflict(new { message = "الملاحظة مغلقة؛ أعد فتحها صراحة قبل إرسال رد جديد." });
        if ((item.Urgency == "Safety" || item.ReportedUrgency == "Safety") && item.TriagedAtUtc == null) return Results.Conflict(new { message = "بلاغ السلامة يحتاج تأكيد الفرز البشري من صفحة الملاحظة قبل الرد والحل." });
        var now = clock.GetUtcNow(); var actor = Actor(principal);
        var before = item.Status;
        item.Status = "Resolved"; item.OwnerResponse = response; item.RespondedByUserId = actor; item.RespondedAtUtc = now;
        // Completion revokes the opaque delegation permanently; reopening needs a
        // fresh owner-reviewed assignment rather than reviving an old private link.
        item.InvestigationId = null; item.InvestigatorMembershipId = null; item.InvestigationBrief = null;
        item.ReviewedByUserId ??= actor; item.ReviewedAtUtc ??= now; item.UpdatedAtUtc = now;
        var change = RecordChange(db, item, t, now, "Resolve", before, item.DueOn);
        if (await Linked(db, t.AcademyId, item.GuardianUserId, item.PlayerId))
        {
            var noticeId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"academy/feedback-response/v2/{t.AcademyId:N}/{change.Id:N}"))[..16]);
            db.AcademyNotices.Add(new AcademyNotice { Id = noticeId, AcademyId = t.AcademyId, UserId = item.GuardianUserId,
                Message = "ردت إدارة الأكاديمية على ملاحظتك. افتح ملاحظاتك لقراءة الرد.", CreatedAtUtc = now, UpdatedAtUtc = now });
        }
        await db.SaveChangesAsync(); db.FeedbackChanges.Add(change); await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Ok(new { item.Id });
    }
}

public sealed record FeedbackWrite(Guid TrainingGroupId, Guid? CoachMembershipId, string Kind, int? Score, string? Message, string? Subject = null, string? Urgency = null, DateOnly? IncidentOn = null, Guid? IncidentSessionId = null);
public sealed record FeedbackResolution(string? Response);
public sealed record FeedbackDueDate(DateOnly DueOn);
