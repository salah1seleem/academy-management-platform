using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class PrivateServiceEndpoints
{
    public static void MapPrivateServices(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/manage/payroll/coaches/{coachId:guid}/private-services").RequireAuthorization(AcademyPermissions.PayrollRead);
        api.MapGet("/", Read);
        api.MapGet("/options", Options);
        api.MapPost("/packages", Bind).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/{packageId:guid}/deliveries", Deliver).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/staff/payroll/private-services", Own).RequireAuthorization(AcademyPermissions.TenantAccess);
    }
    // Financial service review is Owner-only until a distinct delegated permission is added.
    // Contract editing/attendance/CRUD alone MUST NOT silently grant this action.
    internal static async Task<bool> Authority(HttpContext http, CurrentTenant current, FoundationDbContext db)
    {
        var t = (await current.ResolveAsync())!;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"},0))");
        return t.Role == AcademyRole.AcademyOwner && await db.AcademyMemberships.AnyAsync(m => m.Id == t.MembershipId && m.AcademyId == t.AcademyId && m.Role == AcademyRole.AcademyOwner && m.IsActive && m.User.IsActive && m.Academy.IsActive)
            && (await http.RequestServices.GetRequiredService<IAuthorizationService>().AuthorizeAsync(http.User, AcademyPermissions.PayrollManage)).Succeeded;
    }
    private static Task<Employee?> Employee(FoundationDbContext db, Guid coach,bool active=true) => db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.MembershipId == coach && (!active||e.IsActive) && db.AcademyMemberships.Any(m => m.Id == coach && m.Role == AcademyRole.Coach && (!active||m.IsActive&&m.User.IsActive)));
    private static async Task<IResult> Read(Guid coachId, CurrentTenant current, FoundationDbContext db, TimeProvider time, HttpResponse response)
    {
        var t = (await current.ResolveAsync())!; var employee = await Employee(db, coachId,false); if (employee is null) return Results.NotFound();
        response.Headers.CacheControl = "no-store";
        var rows = await Assessments(db, coachId);
        return Results.Ok(new { canReview = t.Role == AcademyRole.AcademyOwner, canRecord=await Employee(db,coachId) is not null, currency = employee.Currency, asOf = time.GetUtcNow(), postedToPayroll = rows.Any(r => r.PostingCount > 0), items = rows });
    }
    private static async Task<IResult> Own(CurrentTenant current, FoundationDbContext db, Academy.Api.Configuration.AcademyConfigurationService config, HttpResponse response)
    {
        var t = (await current.ResolveAsync())!; if (t.Role != AcademyRole.Coach || !await config.Enabled(t.AcademyId, "payroll")) return Results.Forbid();
        response.Headers.CacheControl = "no-store";
        // Only the coach's delivery/rate/compensation, no children, family balances or invoice total.
        var rows = await Assessments(db, t.MembershipId);
        var corrections = await db.PrivateCompensationEntries.AsNoTracking().Where(e => e.Kind == "Correction" && db.PrivateServiceDeliveries.Any(d => d.Id == e.DeliveryId && d.CoachMembershipId == t.MembershipId)).OrderByDescending(e => e.Revision).Select(e => new { e.DeliveryId, e.DueDate, e.Delta }).ToArrayAsync();
        return Results.Ok(new { postedToPayroll = rows.Any(r => r.PostingCount > 0), items = rows.Select(r => new { r.Id, r.ServiceDate, r.Branch, r.Sport, r.Eligible, r.Earned, r.Contingent, r.RequiresReview, r.PostedAmount, r.PendingDelta, r.PostingCount, r.Currency, r.AgreementId, r.VariableValue, r.VariableMethod, corrections = corrections.Where(c => c.DeliveryId == r.Id).Select(c => new { c.DueDate, c.Delta }) }) });
    }
    private static async Task<IResult> Options(Guid coachId, CurrentTenant current, FoundationDbContext db, TimeProvider time)
    {
        var t = (await current.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        if (await Employee(db, coachId) is null) return Results.NotFound();
        var packages = await db.PrivateServicePackages.AsNoTracking().Select(p => new { p.Id, p.DocumentId, p.SessionCount, p.SportEnrollmentId, player = db.ReceivableDocuments.Where(d => d.Id == p.DocumentId).Select(d => d.PlayerNameSnapshot).First(), plan = db.ReceivableDocuments.Where(d => d.Id == p.DocumentId).Select(d => d.PlanNameSnapshot).First(), delivered = db.PrivateServiceDeliveries.Count(d => d.PackageId == p.Id) }).ToArrayAsync();
        var documents = await (from d in db.ReceivableDocuments.AsNoTracking()
            join plan in db.SubscriptionPlans on d.PlanId equals plan.Id
            join e in db.SportEnrollments on d.SportEnrollmentId equals e.Id
            where d.Source == ReceivableSource.ManualCharge && d.Amount > 0 && plan.ServiceKind == AcademyServiceKind.Training && plan.PlanType == SubscriptionPlanType.Sessions && plan.SessionCount > 0
                && (plan.TrainingFormat == TrainingFormat.Private && e.RegistrationFormat == TrainingRegistrationFormat.Private || plan.TrainingFormat == TrainingFormat.SemiPrivate && e.RegistrationFormat == TrainingRegistrationFormat.SemiPrivate)
                && !db.PrivateServicePackages.Any(p => p.DocumentId == d.Id)
            orderby d.CreatedAtUtc descending select new { d.Id, player = d.PlayerNameSnapshot, plan = d.PlanNameSnapshot, d.Amount, d.Currency, plan.SessionCount }).Take(100).ToArrayAsync();
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
        var sessions = await db.TrainingSessions.Where(s => s.SessionDate <= today && s.Status != TrainingSessionStatus.Cancelled && FoundationDbContext.IsSessionStaffAssigned(s.AcademyId, coachId, s.Id) && s.TrainingGroup.RegistrationFormat != TrainingRegistrationFormat.Group)
            .OrderByDescending(s => s.SessionDate).ThenBy(s => s.StartTime).Take(200).Select(s => new { s.Id, s.SessionDate, s.StartTime, group = s.TrainingGroup.ArabicName }).ToArrayAsync();
        return Results.Ok(new { packages, documents, sessions });
    }
    private static async Task<IResult> Bind(Guid coachId, PrivatePackageWrite body, HttpContext http, CurrentTenant current, FoundationDbContext db, TimeProvider time)
    {
        var t = (await current.ResolveAsync())!; if (body.SessionCount is < 1 or > 1000) return Results.Problem(statusCode: 422, title: "راجع عدد الحصص المتجانسة في المطالبة.");
        await using var tx = await db.Database.BeginTransactionAsync(); if (!await Authority(http, current, db)) return Results.Forbid();
        if (await Employee(db, coachId) is null) return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"receivable-document:{t.AcademyId}:{body.DocumentId}"},0))");
        var doc = await db.ReceivableDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == body.DocumentId && d.Source == ReceivableSource.ManualCharge && d.Amount > 0); if (doc is null) return Results.NotFound();
        var replay = await db.PrivateServicePackages.SingleOrDefaultAsync(p => p.DocumentId == doc.Id);
        if (replay is not null) return replay.SessionCount == body.SessionCount ? Results.Ok(new { replay.Id, replay = true }) : Results.Problem(statusCode: 409, title: "عدد الحصص مثبت لهذه المطالبة؛ لا تعدّل توزيع خدمة معتمدة.");
        var e = await db.SportEnrollments.SingleOrDefaultAsync(e => e.Id == doc.SportEnrollmentId && e.IsActive);
        var plan = await db.SubscriptionPlans.SingleOrDefaultAsync(p => p.Id == doc.PlanId && p.IsActive);
        if (e is null || plan is null || e.BranchId != doc.BranchId || e.SportId != plan.SportId || plan.ServiceKind != AcademyServiceKind.Training || plan.PlanType != SubscriptionPlanType.Sessions || plan.SessionCount != body.SessionCount
            || plan.TrainingFormat is not (TrainingFormat.Private or TrainingFormat.SemiPrivate) || plan.TrainingFormat.ToString() != e.RegistrationFormat.ToString()) return Results.Problem(statusCode: 422, title: "اختر مطالبة مرتبطة بباقة حصص خاصة متجانسة وبنفس الفرع والرياضة؛ راجع عدد الحصص الحالي صراحة.");
        var row = new PrivateServicePackage { Id = Guid.NewGuid(), AcademyId = t.AcademyId, DocumentId = doc.Id, SportEnrollmentId = e.Id, BranchId = doc.BranchId, SportId = e.SportId, SessionCount = body.SessionCount, TrainingFormat = plan.TrainingFormat.ToString(), Currency = doc.Currency, ReviewedByMembershipId = t.MembershipId, CreatedAtUtc = time.GetUtcNow(), UpdatedAtUtc = time.GetUtcNow() };
        db.PrivateServicePackages.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Created(http.Request.Path, new { row.Id });
    }
    private static async Task<IResult> Deliver(Guid coachId, Guid packageId, PrivateDeliveryWrite body, HttpContext http, CurrentTenant current, FoundationDbContext db, TimeProvider time)
    {
        var t = (await current.ResolveAsync())!; await using var tx = await db.Database.BeginTransactionAsync(); if (!await Authority(http, current, db)) return Results.Forbid();
        var employee = await Employee(db, coachId); var package = await db.PrivateServicePackages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == packageId);
        if (employee is null || package is null) return Results.NotFound();
        // Same employee lock as agreement approval, then package -> session. No attendance lock inversion.
        await PayrollEndpoints.Lock(db, t.AcademyId, employee.Id, new(2000, 1, 1));
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"private-package:{t.AcademyId}:{packageId}"},0))");
        var session = await db.TrainingSessions.FromSqlInterpolated($"SELECT *, xmin FROM \"TrainingSessions\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\"={body.TrainingSessionId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(); if (session is null) return Results.NotFound();
        var replay = await db.PrivateServiceDeliveries.SingleOrDefaultAsync(d => d.TrainingSessionId == session.Id && d.SportEnrollmentId == package.SportEnrollmentId);
        if (replay is not null) return replay.PackageId == packageId && replay.CoachMembershipId == coachId && replay.Ordinal == body.Ordinal ? Results.Ok(new { replay.Id, replay = true }) : Results.Problem(statusCode: 409, title: "الحصة مرتبطة بخدمة مالية بالفعل؛ لا تكررها أو تنقلها إلى مدرب آخر.");
        if (body.Ordinal < 1 || body.Ordinal > package.SessionCount || await db.PrivateServiceDeliveries.AnyAsync(d => d.PackageId == packageId && d.Ordinal == body.Ordinal)) return Results.Problem(statusCode: 409, title: "اختر رقم حصة غير مستخدم داخل عدد حصص الباقة.");
        var zone = await db.Academies.Where(a => a.Id == t.AcademyId).Select(a => a.TimeZone).SingleAsync(); var local = TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(zone));
        if (session.SessionDate.ToDateTime(session.EndTime) > local.DateTime || session.Status == TrainingSessionStatus.Cancelled
            || session.BranchId != package.BranchId || session.SportId != package.SportId
            || !await db.TrainingGroups.AnyAsync(g => g.Id == session.TrainingGroupId && g.RegistrationFormat.ToString() == package.TrainingFormat)
            || !await db.SportEnrollments.AnyAsync(e => e.Id == package.SportEnrollmentId && e.RegistrationFormat.ToString() == package.TrainingFormat && FoundationDbContext.IsSessionAssigned(t.AcademyId, e.Id, session.Id))
            || !await db.AcademyMemberships.AnyAsync(m => m.Id == coachId && FoundationDbContext.IsSessionStaffAssigned(t.AcademyId, m.Id, session.Id))
            || await db.PlayerAttendances.AnyAsync(a => a.TrainingSessionId == session.Id && a.SportEnrollmentId == package.SportEnrollmentId && (a.Status == AttendanceStatus.Absent || a.Status == AttendanceStatus.Excused))
            || await db.CompensationBookings.AnyAsync(b => b.TrainingSessionId == session.Id && b.SportEnrollmentId == package.SportEnrollmentId && b.State == CompensationBookingState.Reserved)) return Results.Problem(statusCode: 409, title: "راجع انتهاء الحصة وإسناد اللاعب والمدرب الفعلي ونمط التدريب؛ الحصة الملغاة أو التعويضية لا تولد عمولة بيع.");
        var agreement = await CoachAgreementEndpoints.Resolve(db, t.AcademyId, coachId, package.BranchId, package.SportId, session.SessionDate, time.GetUtcNow());
        if (agreement is null || agreement.Currency != package.Currency || agreement.EmployeeId != employee.Id) return Results.Problem(statusCode: 409, title: "لا اتفاق أجر معتمد صالح لتاريخ الحصة؛ لا نفترض نسبة أو نعيد كتابة اتفاق سابق.");
        var row = new PrivateServiceDelivery { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PackageId = packageId, TrainingSessionId = session.Id, SportEnrollmentId = package.SportEnrollmentId, EmployeeId = employee.Id, CoachMembershipId = coachId, AgreementId = agreement.Id, BranchId = package.BranchId, SportId = package.SportId, Ordinal = body.Ordinal, ServiceDate = session.SessionDate, StartTime = session.StartTime, EndTime = session.EndTime, ApprovedByMembershipId = t.MembershipId, CreatedAtUtc = time.GetUtcNow(), UpdatedAtUtc = time.GetUtcNow() };
        db.PrivateServiceDeliveries.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Created(http.Request.Path, new { row.Id });
    }
    internal static async Task<PrivateAssessment[]> Assessments(FoundationDbContext db, Guid coach, Guid? deliveryId = null)
    {
        // One statement/snapshot for sources and allocations; an unrelated payment to the
        // same guardian/enrollment is deliberately NOT considered collected for this invoice.
        var sources = await (from d in db.PrivateServiceDeliveries.AsNoTracking()
            join p in db.PrivateServicePackages on d.PackageId equals p.Id
            join doc in db.ReceivableDocuments on p.DocumentId equals doc.Id
            join a in db.CoachCompensationAgreements on d.AgreementId equals a.Id
            where d.CoachMembershipId == coach && (!deliveryId.HasValue || d.Id == deliveryId)
            orderby d.ServiceDate descending, d.Ordinal
            select new { Delivery = d, p.SessionCount, doc.Amount, doc.PlayerNameSnapshot, doc.Id, p.Currency, a.VariableMethod, a.VariableValue, a.Eligibility,
                branch = db.Branches.Where(b => b.Id == d.BranchId).Select(b => b.ArabicName).First(), sport = db.Sports.Where(s => s.Id == d.SportId).Select(s => s.ArabicName).First(),
                requiresReview = db.PlayerAttendances.Any(x => x.TrainingSessionId == d.TrainingSessionId && x.SportEnrollmentId == d.SportEnrollmentId && (x.Status == AttendanceStatus.Absent || x.Status == AttendanceStatus.Excused))
                    || db.CompensationBookings.Any(x => x.TrainingSessionId == d.TrainingSessionId && x.SportEnrollmentId == d.SportEnrollmentId && x.State == CompensationBookingState.Reserved),
                posted = db.PrivateCompensationEntries.Where(e => e.DeliveryId == d.Id).Sum(e => (decimal?)e.Delta) ?? 0,
                postingCount = db.PrivateCompensationEntries.Count(e => e.DeliveryId == d.Id && e.PayrollAdjustmentId.HasValue),
                credits = db.ReceivableMovements.Where(m => m.DocumentId == doc.Id && m.Kind == ReceivableMovementKind.CreditNote).Sum(m => (decimal?)m.Amount) ?? 0,
                paid = db.ReceivableMovements.Where(m => m.DocumentId == doc.Id && m.Kind == ReceivableMovementKind.PaymentAllocation).Sum(m => (decimal?)m.Amount) ?? 0,
                used = db.ReceivableCreditApplications.Where(c => c.SourceDocumentId == doc.Id).Sum(c => (decimal?)c.Amount) ?? 0,
                received = db.ReceivableCreditApplications.Where(c => c.TargetDocumentId == doc.Id).Sum(c => (decimal?)c.Amount) ?? 0 }).ToArrayAsync();
        return sources.Select(s => { var net = Math.Max(0, s.Amount - s.credits); var paid = Math.Clamp(s.paid + s.received - s.used, 0, net);
            var portion = PrivateCompensationCalculator.Portion(net, s.SessionCount, s.Delivery.Ordinal); var c = PrivateCompensationCalculator.Assess(portion, net, paid, s.VariableMethod, s.VariableValue, s.Eligibility);
            return new PrivateAssessment(s.Delivery.Id, s.Delivery.PackageId, s.Id, s.Delivery.TrainingSessionId, s.PlayerNameSnapshot, s.branch!, s.sport!, s.Delivery.ServiceDate, s.Delivery.Ordinal, s.SessionCount, portion, s.requiresReview ? null : c.Eligible, s.requiresReview ? null : c.Earned, s.requiresReview ? null : c.Contingent, s.Currency, s.Delivery.AgreementId, s.VariableMethod, s.VariableValue, s.requiresReview) { PostedAmount = s.posted, PendingDelta = s.requiresReview ? null : c.Earned - s.posted, PostingCount = s.postingCount }; }).ToArray();
    }
}
public sealed record PrivatePackageWrite(Guid DocumentId, int SessionCount);
public sealed record PrivateDeliveryWrite(Guid TrainingSessionId, int Ordinal);
public sealed record PrivateAssessment(Guid Id, Guid PackageId, Guid DocumentId, Guid TrainingSessionId, string Player, string Branch, string Sport, DateOnly ServiceDate, int Ordinal, int SessionCount, decimal ServiceNet, decimal? Eligible, decimal? Earned, decimal? Contingent, string Currency, Guid AgreementId, string VariableMethod, decimal VariableValue, bool RequiresReview)
{
    public decimal PostedAmount { get; init; }
    public decimal? PendingDelta { get; init; }
    public int PostingCount { get; init; }
}
