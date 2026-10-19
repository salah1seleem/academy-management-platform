using System.Globalization;
using System.Security.Claims;
using System.Text;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Api.Slice4;
using Academy.Api.Operations;
using Academy.Api.Finance;
using Microsoft.AspNetCore.Authorization;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Finance;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;

public static partial class ReportingEndpoints
{
    private const int MaxInteractiveDays = 366;
    private const int MaxExportRows = 5000;

    public static void MapSlice8ReportingEndpoints(this WebApplication app)
    {
        var reports = app.MapGroup("/api/v1/reports");
        reports.MapGet("/owner-summary", OwnerSummary).RequireAuthorization(AcademyPermissions.OwnerDashboardRead);
        reports.MapGet("/owner-cockpit",OwnerCockpit).RequireAuthorization(AcademyPermissions.OwnerDashboardRead);
        reports.MapGet("/coach-performance/{coachId:guid}", CoachPerformance).RequireAuthorization(AcademyPermissions.OwnerDashboardRead);
        reports.MapGet("/staff-access-audit", StaffAccessAudit).RequireAuthorization(AcademyPermissions.OwnerDashboardRead);
        reports.MapGet("/financial-decisions", FinancialDecisions).RequireAuthorization(AcademyPermissions.OwnerDashboardRead);
        reports.MapGet("/operating-statement", OperatingStatement).RequireAuthorization(AcademyPermissions.ReportsFinancialRead);
        reports.MapGet("/financial", FinancialReport).RequireAuthorization(AcademyPermissions.ReportsFinancialRead);
        reports.MapGet("/financial/export", FinancialExport).RequireAuthorization(AcademyPermissions.ReportsFinancialRead).RequireAuthorization(AcademyPermissions.ReportsExport);
        reports.MapGet("/attendance", AttendanceReport).RequireAuthorization(AcademyPermissions.ReportsAttendanceRead);
        reports.MapGet("/attendance/export", AttendanceExport).RequireAuthorization(AcademyPermissions.ReportsAttendanceRead).RequireAuthorization(AcademyPermissions.ReportsExport);
        reports.MapGet("/receipts", ReceiptList).RequireAuthorization(AcademyPermissions.ReportsFinancialRead);
    }

    private static async Task<IResult> StaffAccessAudit(DateOnly? from, DateOnly? to, bool? allDates, int? page,
        CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!;
        if (current.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        if (page is < 1 or > 10000) return Validation("page", "رقم الصفحة غير صالح.");
        var range = await FinancialRange(current.AcademyId, from, to, db, clock, allDates == true);
        if (range.Error is not null) return range.Error;
        var query = db.StaffAccessAudits.Where(x => x.AcademyId == current.AcademyId && x.CreatedAtUtc >= range.StartUtc && x.CreatedAtUtc < range.EndUtcExclusive);
        var count = await query.CountAsync();
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Skip(((page ?? 1) - 1) * 50).Take(50)
            .Select(x => new { x.Id, x.MembershipId, x.CreatedAtUtc, x.After,
                actorName = db.Users.Where(u => u.Id == x.ActorUserId).Select(u => u.DisplayName).FirstOrDefault(),
                staffName = db.AcademyMemberships.Where(m => m.AcademyId == current.AcademyId && m.Id == x.MembershipId).Select(m => m.User.DisplayName).FirstOrDefault() })
            .ToArrayAsync();
        return Results.Ok(new { count, page = page ?? 1, from = range.From, to = range.To,
            items = rows.Select(x => new { x.Id, x.MembershipId, x.CreatedAtUtc, x.actorName, x.staffName,
                change = StaffAuditChange(x.After) }).ToArray() });
    }

    private static string StaffAuditChange(string after)
    {
        using var document = System.Text.Json.JsonDocument.Parse(after);
        if (document.RootElement.TryGetProperty("SensitivePermissionsChanged", out var changed) && changed.ValueKind == System.Text.Json.JsonValueKind.True)
            return "تفويضات الرواتب واتفاقات الأجر، مع مراجعة الوحدات والنطاق";
        return after.Contains("cashierEnabled", StringComparison.Ordinal) ? "صلاحية التحصيل النقدي" : "صلاحيات الوحدات أو نطاق الفروع";
    }

    private static async Task<IResult> CoachPerformance(Guid coachId, DateOnly? from, DateOnly? to, bool? allDates, Guid? branchId,
        CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!; var academyId = current.AcademyId;
        var range = await FinancialRange(academyId, from, to, db, clock, allDates == true);
        if (range.Error is not null) return range.Error;
        if (branchId.HasValue && !await db.Branches.AnyAsync(x => x.AcademyId == academyId && x.Id == branchId)) return Results.NotFound();
        var coach = await db.AcademyMemberships.Where(x => x.AcademyId == academyId && x.Id == coachId && x.Role == AcademyRole.Coach)
            .Select(x => new { x.Id, x.UserId, name = x.User.DisplayName, x.IsActive }).SingleOrDefaultAsync();
        if (coach is null) return Results.NotFound();
        var assigned = await db.StaffGroupAssignments.Where(x => x.AcademyId == academyId && x.AcademyMembershipId == coachId && x.IsActive && x.TrainingGroup.IsActive && (!branchId.HasValue || x.TrainingGroup.BranchId == branchId))
            .Select(x => new { x.TrainingGroupId, name = x.TrainingGroup.ArabicName, branch = x.TrainingGroup.Branch.ArabicName }).Distinct().ToArrayAsync();
        if (assigned.Length == 0 && (branchId.HasValue || db.BranchScopeRestricted)) return Results.NotFound();
        var groupIds = assigned.Select(x => x.TrainingGroupId).ToArray();
        var sessionCounts = await db.TrainingSessions.Where(x => x.AcademyId == academyId && groupIds.Contains(x.TrainingGroupId) && x.SessionDate >= range.From && x.SessionDate <= range.To && x.Status != TrainingSessionStatus.Cancelled)
            .GroupBy(x => x.TrainingGroupId).Select(x => new { groupId = x.Key, count = x.Count() }).ToDictionaryAsync(x => x.groupId, x => x.count);
        var evaluationCount = await db.PlayerEvaluations.CountAsync(x => x.AcademyId == academyId && x.EvaluatedByUserId == coach.UserId && x.Status == EvaluationStatus.Published && groupIds.Contains(x.TrainingGroupId) && x.EvaluationDate >= range.From && x.EvaluationDate <= range.To);
        var feedback = await db.GuardianFeedbacks.Where(x => x.AcademyId == academyId && x.CoachMembershipId == coachId && groupIds.Contains(x.TrainingGroupId) && x.CreatedAtUtc >= range.StartUtc && x.CreatedAtUtc < range.EndUtcExclusive)
            .GroupBy(x => x.Kind).Select(x => new { kind = x.Key, count = x.Count(), scoreTotal = x.Sum(y => y.Score ?? 0) }).ToArrayAsync();
        var ratings = feedback.FirstOrDefault(x => x.kind == "Rating");
        return Results.Ok(new { coach.Id, coach.name, coach.IsActive, from = range.From, to = range.To,
            groups = assigned.Select(x => new { id = x.TrainingGroupId, x.name, x.branch, sessions = sessionCounts.GetValueOrDefault(x.TrainingGroupId) }).OrderBy(x => x.name).ToArray(),
            sessionCount = sessionCounts.Values.Sum(), publishedEvaluations = evaluationCount,
            parentRating = ratings is null ? (decimal?)null : Math.Round((decimal)ratings.scoreTotal / ratings.count, 1), parentRatingCount = ratings?.count ?? 0,
            complaintCount = feedback.FirstOrDefault(x => x.kind == "Complaint")?.count ?? 0 });
    }

    private static async Task<IResult> OwnerSummary(DateOnly? from, DateOnly? to, bool? allDates, Guid? branchId, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock, TimeProvider realClock, HttpContext context, IAuthorizationService authorization)
    {
        var current = (await tenant.ResolveAsync())!; var academyId = current.AcademyId;
        var range = await FinancialRange(academyId, from, to, db, clock, allDates == true); if (range.Error is not null) return range.Error;
        var academy = await db.Academies.Where(x => x.Id == academyId).Select(x => new { x.TimeZone, x.DefaultCurrency }).SingleAsync();
        var today = clock.Today; var dayRange = UtcRange(range.To, range.To, academy.TimeZone);
        var collections = FinancialQuery(academyId, range.Currency, range.StartUtc, range.EndUtcExclusive, null, branchId, null, null, null, db);
        var products = ProductFinancialQuery(academyId, range.Currency, range.StartUtc, range.EndUtcExclusive, null, branchId, null, null, db);
        var tournaments = TournamentQuery(academyId, range.Currency, range.StartUtc, range.EndUtcExclusive, null, branchId, null, null, db);
        var dayPasses = DayPassFinancialQuery(academyId, range.Currency, range.StartUtc, range.EndUtcExclusive, null, branchId, null, null, null, db);
        var c = await collections.Select(x => new { x.Id, x.Amount, x.PaymentMethod, x.ConfirmedAtUtc }).ToListAsync();
        var p = await products.Select(x => new { x.Id, x.Amount, x.PaymentMethod, ConfirmedAtUtc = x.ConfirmedAtUtc!.Value }).ToListAsync();
        p.AddRange(await tournaments.Select(x => new { x.Id, x.Amount, x.PaymentMethod, ConfirmedAtUtc = x.ConfirmedAtUtc!.Value }).ToListAsync());
        p.AddRange(await ExternalFinancialQuery(academyId,range.Currency,range.StartUtc,range.EndUtcExclusive,null,branchId,null,null,null,db).Select(x=>new{x.Id,x.Amount,PaymentMethod=(string?)x.PaymentMethod,ConfirmedAtUtc=x.ReceivedAtUtc}).ToListAsync());
        p.AddRange(await dayPasses.Select(x => new { x.Id, x.Amount, PaymentMethod = (string?)"Online", ConfirmedAtUtc = x.ReviewedAtUtc!.Value }).ToListAsync());
        var refundRows = await RefundFinancialQuery(academyId,range.Currency,range.StartUtc,range.EndUtcExclusive,null,branchId,null,null,null,db)
            .Select(r=>new {amount=-r.Amount,PaymentMethod=r.PayoutMethod ?? (db.CashRefundPostings.Any(p=>p.RefundId==r.Id)?"Cash":"Unspecified"),ConfirmedAtUtc=r.CreatedAtUtc}).ToListAsync();
        var money = c.Select(x => new { amount = x.Amount, x.PaymentMethod, x.ConfirmedAtUtc }).Concat(p.Select(x => new { amount = x.Amount, PaymentMethod = x.PaymentMethod!, x.ConfirmedAtUtc })).Concat(refundRows).ToList();
        var daily = money.Where(x => x.ConfirmedAtUtc >= dayRange.Start && x.ConfirmedAtUtc < dayRange.EndExclusive).ToList();
        var periods = db.SubscriptionPeriods.Where(x => x.AcademyId == academyId && (!branchId.HasValue || x.SportEnrollment.BranchId == branchId) && x.SportEnrollment.IsActive && x.SportEnrollment.Player.IsActive && x.Status != SubscriptionPeriodStatus.Cancelled && x.Status != SubscriptionPeriodStatus.Frozen);
        var active = periods.Where(x => x.StartDate <= range.To && (x.EndDate == null || x.EndDate >= range.To) && ((x.PlanTypeSnapshot ?? x.SubscriptionPlan.PlanType) == SubscriptionPlanType.Duration || x.RemainingSessions > 0));
        var pendingRenewals = db.PaymentRequests.Where(x => x.AcademyId == academyId && x.CreatedAtUtc >= range.StartUtc && x.CreatedAtUtc < range.EndUtcExclusive && (!branchId.HasValue || x.RenewalRequest.SportEnrollment.BranchId == branchId) && x.Id == x.RenewalRequest.PaymentRequestId && (x.Status == PaymentRequestStatus.Pending || x.Status == PaymentRequestStatus.Created) && !db.NewEnrollmentRequests.Any(n => n.AcademyId == academyId && n.PaymentRequestId == x.Id));
        var pendingNew = db.NewEnrollmentRequests.Where(x => x.AcademyId == academyId && x.CreatedAtUtc >= range.StartUtc && x.CreatedAtUtc < range.EndUtcExclusive && (!branchId.HasValue || x.PreferredBranchId == branchId) && (x.Status == Academy.Infrastructure.People.NewEnrollmentRequestStatus.Pending || x.Status == Academy.Infrastructure.People.NewEnrollmentRequestStatus.UnderReview));
        var pendingProducts = db.ProductSales.Where(x => x.AcademyId == academyId && x.CreatedAtUtc >= range.StartUtc && x.CreatedAtUtc < range.EndUtcExclusive && (!branchId.HasValue || x.BranchId == branchId) && x.Status == "Pending");
        var nr = await pendingRenewals.CountAsync(); var nn = await pendingNew.CountAsync(); var np = await pendingProducts.CountAsync();
        nr += await db.FamilyRenewalRequests.CountAsync(x => x.AcademyId == academyId && x.Status == FamilyRenewalStatus.Pending && x.CreatedAtUtc >= range.StartUtc && x.CreatedAtUtc < range.EndUtcExclusive && (!branchId.HasValue || x.Lines.Any(l => l.BranchId == branchId)));
        var nt = await db.TournamentPayments.CountAsync(x => x.AcademyId == academyId && x.Status == "Pending" && x.CreatedAtUtc >= range.StartUtc && x.CreatedAtUtc < range.EndUtcExclusive && (!branchId.HasValue || db.SportEnrollments.Any(e => e.AcademyId == academyId && e.Id == x.EnrollmentId && e.BranchId == branchId)));
        var nd = await db.DayPassTickets.CountAsync(x => x.AcademyId == academyId && x.Status == DayPassTicketStatus.UnderReview && x.CreatedAtUtc >= range.StartUtc && x.CreatedAtUtc < range.EndUtcExclusive && (!branchId.HasValue || x.BranchId == branchId));
        // An actionable backlog is independent of the chart period: older requests must not disappear from Owner decisions.
        var pendingPaymentQueueBacklog =
            await db.PaymentRequests.CountAsync(x => x.AcademyId == academyId && (!branchId.HasValue || x.RenewalRequest.SportEnrollment.BranchId == branchId) && x.Id == x.RenewalRequest.PaymentRequestId && (x.Status == PaymentRequestStatus.Pending || x.Status == PaymentRequestStatus.Created) && !db.NewEnrollmentRequests.Any(n => n.AcademyId == academyId && n.PaymentRequestId == x.Id))
            + await db.FamilyRenewalRequests.CountAsync(x => x.AcademyId == academyId && x.Status == FamilyRenewalStatus.Pending && (!branchId.HasValue || x.Lines.Any(l => l.BranchId == branchId)))
            + await db.NewEnrollmentRequests.CountAsync(x => x.AcademyId == academyId && (!branchId.HasValue || x.PreferredBranchId == branchId) && (x.Status == Academy.Infrastructure.People.NewEnrollmentRequestStatus.Pending || x.Status == Academy.Infrastructure.People.NewEnrollmentRequestStatus.UnderReview))
            + await db.ProductSales.CountAsync(x => x.AcademyId == academyId && (!branchId.HasValue || x.BranchId == branchId) && x.Status == "Pending")
            + await db.TournamentPayments.CountAsync(x => x.AcademyId == academyId && x.Status == "Pending" && (!branchId.HasValue || db.SportEnrollments.Any(e => e.AcademyId == academyId && e.Id == x.EnrollmentId && e.BranchId == branchId)));
        // The native payment queue excludes the separate DayPass workflow.
        // Preserve the existing all-services KPI and expose the exact queue grain.
        var pendingPaymentBacklog = pendingPaymentQueueBacklog
            + await db.DayPassTickets.CountAsync(x => x.AcademyId == academyId && x.Status == DayPassTicketStatus.UnderReview && (!branchId.HasValue || x.BranchId == branchId));
        var subscriptionTotal = await db.SubscriptionPeriods.Where(x => x.AcademyId == academyId && (!branchId.HasValue || x.SportEnrollment.BranchId == branchId) && x.SportEnrollment.IsActive && x.SportEnrollment.Player.IsActive && x.Status != SubscriptionPeriodStatus.Cancelled).Select(x => x.SportEnrollment.PlayerId).Distinct().CountAsync();
        var todayAttendance = db.PlayerAttendances.Where(x => x.AcademyId == academyId && x.TrainingSession.SessionDate >= range.From && x.TrainingSession.SessionDate <= range.To && (!branchId.HasValue || x.TrainingSession.BranchId == branchId) && x.TrainingSession.Status != TrainingSessionStatus.Cancelled);
        var bySport = await collections.GroupBy(x => x.SportEnrollment.Sport.ArabicName).Select(x => new { name = x.Key, amount = x.Sum(y => y.Amount), count = x.Count() }).ToListAsync();
        var byBranch = await (from collection in collections
            join posting in db.CashReceiptPostings on collection.Id equals posting.CollectionId into postings
            from posting in postings.DefaultIfEmpty()
            join b in db.Branches on (posting == null ? collection.SportEnrollment.BranchId : posting.BranchId) equals b.Id
            group collection by new { b.Id, b.ArabicName } into g
            select new { id = g.Key.Id, name = g.Key.ArabicName, amount = g.Sum(c => c.Amount), count = g.Count() }).ToListAsync();
        var latest = await collections.OrderByDescending(x => x.ConfirmedAtUtc).Take(5).Select(x => new LatestCollection(db.Receipts.Where(r => r.AcademyId == academyId && r.CollectionId == x.Id).Select(r => r.Id).Single(), db.Receipts.Where(r => r.AcademyId == academyId && r.CollectionId == x.Id).Select(r => r.ReceiptNumber).Single(), x.SportEnrollment.Player.ArabicName, x.Amount, x.Currency, x.ConfirmedAtUtc)).ToListAsync();
        object? payroll = null;
        if ((await authorization.AuthorizeAsync(context.User, AcademyPermissions.PayrollRead)).Succeeded)
        {
            var realToday = await AttendanceCalendar.TodayAsync(academyId, db, realClock);
            var payrollDate = allDates == true && from is null && to is null ? realToday : range.To;
            if (payrollDate.Year is >= 2000 and <= 9998)
            {
                var payrollMonth = new DateOnly(payrollDate.Year, payrollDate.Month, 1);
                var salaries = await PayrollEndpoints.Rows(academyId, payrollMonth, branchId, db);
                var staff = await db.Employees.AsNoTracking().Where(x => x.AcademyId == academyId && x.IsActive && (!branchId.HasValue || x.BranchId == branchId)).ToArrayAsync();
                var contracts=await db.ContractVersions.AsNoTracking().Where(v=>v.AcademyId==academyId&&v.EffectiveFrom<=SalaryHistory.Month(realToday)).ToArrayAsync();
                var terms=staff.Select(e=>ContractHistory.Bounds(e,contracts,realToday)).Where(c=>c.ContractStart<=realToday&&(!c.ContractEnd.HasValue||c.ContractEnd>=realToday)).ToArray();
                // Current actionable payable backlog, not salary earned in the chart month.
                var financialCutoff=realClock.GetUtcNow();
                var finalDues=await db.PayrollStatements.AsNoTracking().Where(s=>s.AcademyId==academyId&&s.Kind=="PrivateFinal"&&s.CreatedAtUtc<=financialCutoff&&(!branchId.HasValue||s.BranchId==branchId)).Select(s=>new{s.EmployeeId,s.EmployeeName,s.Currency,remaining=s.NetDue-db.PayrollDisbursements.Where(p=>p.StatementId==s.Id&&p.CreatedAtUtc<=financialCutoff).Sum(p=>p.Amount)}).Where(s=>s.remaining>0).ToArrayAsync();
                payroll = new { month = payrollMonth, employeeCount = terms.Length, expiringContracts = terms.Count(c => c.ContractEnd.HasValue && c.ContractEnd <= realToday.AddDays(30)), baseDue = salaries.Sum(x => x.BaseDue), deductions = salaries.Sum(x => x.Deductions), additions = salaries.Sum(x => x.Additions), netDue = salaries.Sum(x => x.NetDue), unpaid = salaries.Sum(x=>x.RemainingAmount),
                    postContract=finalDues.GroupBy(s=>s.Currency).Select(g=>new{currency=g.Key,remaining=g.Sum(s=>s.remaining),count=g.Count()}),postContractEmployees=finalDues.GroupBy(s=>new{s.EmployeeId,s.EmployeeName,s.Currency}).Select(g=>new{employeeId=g.Key.EmployeeId,name=g.Key.EmployeeName,currency=g.Key.Currency,remaining=g.Sum(s=>s.remaining)}).OrderByDescending(s=>s.remaining).Take(20),postContractAsOf=financialCutoff };
            }
        }
        var coachMembers = await db.AcademyMemberships.Where(x => x.AcademyId == academyId && x.Role == AcademyRole.Coach && x.IsActive)
            .Select(x => new { x.Id, x.UserId, name = x.User.DisplayName }).ToListAsync();
        var groups = await db.TrainingGroups.Where(x => x.AcademyId == academyId && x.IsActive && (!branchId.HasValue || x.BranchId == branchId))
            .Select(x => new { x.Id, x.ArabicName, x.BranchId }).ToListAsync();
        var groupIds = groups.Select(x => x.Id).ToArray();
        var assignments = await db.StaffGroupAssignments.Where(x => x.AcademyId == academyId && x.IsActive && groupIds.Contains(x.TrainingGroupId))
            .Select(x => new { x.AcademyMembershipId, x.TrainingGroupId }).ToListAsync();
        var sessionCounts = await db.TrainingSessions.Where(x => x.AcademyId == academyId && groupIds.Contains(x.TrainingGroupId) && x.SessionDate >= range.From && x.SessionDate <= range.To && x.Status != TrainingSessionStatus.Cancelled)
            .GroupBy(x => x.TrainingGroupId).Select(x => new { groupId = x.Key, count = x.Count() }).ToDictionaryAsync(x => x.groupId, x => x.count);
        var groupAttendance = await db.PlayerAttendances.Where(x => x.AcademyId == academyId && groupIds.Contains(x.TrainingGroupId) && x.TrainingSession.SessionDate >= range.From && x.TrainingSession.SessionDate <= range.To && x.TrainingSession.Status != TrainingSessionStatus.Cancelled)
            .GroupBy(x => new { x.TrainingGroupId, x.Status }).Select(x => new { x.Key.TrainingGroupId, x.Key.Status, count = x.Count() }).ToListAsync();
        var evaluations = await db.PlayerEvaluations.Where(x => x.AcademyId == academyId && x.Status == EvaluationStatus.Published && groupIds.Contains(x.TrainingGroupId) && x.EvaluationDate >= range.From && x.EvaluationDate <= range.To)
            .GroupBy(x => x.EvaluatedByUserId).Select(x => new { userId = x.Key, count = x.Count() }).ToDictionaryAsync(x => x.userId, x => x.count);
        var feedback = await db.GuardianFeedbacks.Where(x => x.AcademyId == academyId && groupIds.Contains(x.TrainingGroupId) && x.CreatedAtUtc >= range.StartUtc && x.CreatedAtUtc < range.EndUtcExclusive)
            .GroupBy(x => new { x.CoachMembershipId, x.Kind }).Select(x => new { x.Key.CoachMembershipId, x.Key.Kind, count = x.Count(), scoreTotal = x.Sum(y => y.Score ?? 0) }).ToArrayAsync();
        var coachPerformance = coachMembers.Select(coach => {
            var assigned = assignments.Where(a => a.AcademyMembershipId == coach.Id).Select(a => a.TrainingGroupId).Distinct().ToArray();
            var ratings = feedback.FirstOrDefault(f => f.CoachMembershipId == coach.Id && f.Kind == "Rating");
            return new { coach.Id, coach.name, groupCount = assigned.Length, sessionCount = assigned.Sum(id => sessionCounts.GetValueOrDefault(id)), publishedEvaluations = evaluations.GetValueOrDefault(coach.UserId), parentRating = ratings is null ? (decimal?)null : Math.Round((decimal)ratings.scoreTotal / ratings.count, 1), parentRatingCount = ratings?.count ?? 0, complaintCount = feedback.FirstOrDefault(f => f.CoachMembershipId == coach.Id && f.Kind == "Complaint")?.count ?? 0 };
        }).Where(x => x.groupCount > 0 || !branchId.HasValue).OrderByDescending(x => x.groupCount).ThenByDescending(x => x.sessionCount).ThenBy(x => x.name).Take(8).ToArray();
        var groupAttention = groups.Select(group => {
            var attendanceRows = groupAttendance.Where(a => a.TrainingGroupId == group.Id).ToArray();
            var absent = attendanceRows.Where(a => a.Status == AttendanceStatus.Absent).Sum(a => a.count);
            var notRecorded = attendanceRows.Where(a => a.Status == AttendanceStatus.NotRecorded).Sum(a => a.count);
            var recorded = attendanceRows.Where(a => a.Status != AttendanceStatus.NotRecorded).Sum(a => a.count);
            var hasCoach = assignments.Any(a => a.TrainingGroupId == group.Id);
            return new { group.Id, name = group.ArabicName, group.BranchId, hasCoach, sessions = sessionCounts.GetValueOrDefault(group.Id), absent, notRecorded, recorded, needsAttention = !hasCoach || notRecorded >= 3 || recorded >= 5 && absent * 100 >= recorded * 30 };
        }).Where(x => x.needsAttention).OrderByDescending(x => !x.hasCoach).ThenByDescending(x => x.notRecorded).ThenByDescending(x => x.absent).Take(3).ToArray();
        var pendingExpenses = await db.OperatingExpenses.Where(x => x.AcademyId == academyId && x.Status == OperatingExpenseStatus.Approved && x.PeriodTo >= range.From && x.PeriodFrom <= range.To && (!branchId.HasValue || x.BranchId == branchId))
            .GroupBy(x => x.Currency).Select(x => new { currency = x.Key, count = x.Count(), amount = x.Sum(y => y.Amount) }).ToListAsync();
        var unpaidExpenses = await db.OperatingExpenses.Where(x => x.AcademyId == academyId && (x.Status == OperatingExpenseStatus.Approved || x.Status == OperatingExpenseStatus.Posted) && x.PaidOn == null && (!branchId.HasValue || x.BranchId == branchId))
            .GroupBy(x => x.Currency).Select(x => new { currency = x.Key, count = x.Count(), amount = x.Sum(y => y.Amount) }).ToListAsync();
        var receivableRows = await ReceivableEndpoints.Rows(db, academyId, clock.UtcNow, clock.Today, branchId, null, null).ToListAsync();
        var receivables = receivableRows.GroupBy(x => x.Currency).Select(x => new {
            currency = x.Key,
            outstanding = x.Sum(y => Math.Max(0, y.Outstanding)),
            overdue = x.Where(y => y.Overdue).Sum(y => y.Outstanding),
            openCount = x.Count(y => y.Outstanding > 0)
        }).OrderBy(x => x.currency).ToArray();
        var accessChanges = await db.StaffAccessAudits.CountAsync(x => x.AcademyId == academyId && x.CreatedAtUtc >= range.StartUtc && x.CreatedAtUtc < range.EndUtcExclusive);
        var feedbackToday = await AttendanceCalendar.TodayAsync(academyId, db, realClock);
        return Results.Ok(new {
            asOfDate = today, from = range.From, to = range.To, currency = academy.DefaultCurrency,
            collections = new { total = money.Sum(x => x.amount), today = daily.Sum(x => x.amount), month = money.Sum(x => x.amount), todayCash = daily.Where(x => x.PaymentMethod == "Cash").Sum(x => x.amount), todayOnline = daily.Where(x => x.PaymentMethod != "Cash" && x.PaymentMethod!="Unspecified").Sum(x => x.amount), todayUnspecified=daily.Where(x=>x.PaymentMethod=="Unspecified").Sum(x=>x.amount), monthCash = money.Where(x => x.PaymentMethod == "Cash").Sum(x => x.amount), monthOnline = money.Where(x => x.PaymentMethod != "Cash" && x.PaymentMethod!="Unspecified").Sum(x => x.amount), monthUnspecified=money.Where(x=>x.PaymentMethod=="Unspecified").Sum(x=>x.amount) },
            paymentRequests = new { total = nr + nn + np + nt + nd, newEnrollments = nn, renewals = nr, products = np, tournaments = nt, dayPasses = nd },
            payments = new { confirmed = c.Count + p.Count, pending = nr + nn + np + nt + nd },
            subscriptions = new { total = subscriptionTotal, active = await active.Select(x => x.SportEnrollment.PlayerId).Distinct().CountAsync(), expiring = await active.Where(x => x.EndDate <= range.To.AddDays(7)).Select(x => x.SportEnrollment.PlayerId).Distinct().CountAsync(), expired = await periods.Where(x => (x.EndDate < range.To || (x.PlanTypeSnapshot ?? x.SubscriptionPlan.PlanType) != SubscriptionPlanType.Duration && x.RemainingSessions <= 0) && !active.Any(a => a.SportEnrollment.PlayerId == x.SportEnrollment.PlayerId)).Select(x => x.SportEnrollment.PlayerId).Distinct().CountAsync() },
            activePlayers = await active.Select(x => x.SportEnrollment.PlayerId).Distinct().CountAsync(),
            coaches = await db.AcademyMemberships.CountAsync(x => x.AcademyId == academyId && x.Role == AcademyRole.Coach && x.IsActive && (!branchId.HasValue || db.StaffGroupAssignments.Any(a => a.AcademyId == academyId && a.AcademyMembershipId == x.Id && a.IsActive && a.TrainingGroup.IsActive && a.TrainingGroup.BranchId == branchId))),
            newSubscriptions = await db.SubscriptionPeriods.Where(x => x.AcademyId == academyId && x.CreatedAtUtc >= range.StartUtc && x.CreatedAtUtc < range.EndUtcExclusive && (!branchId.HasValue || x.SportEnrollment.BranchId == branchId) && !db.SubscriptionPeriods.Any(old => old.AcademyId == academyId && old.SportEnrollmentId == x.SportEnrollmentId && old.CreatedAtUtc < x.CreatedAtUtc)).CountAsync(),
            attendance = new { sessions = await db.TrainingSessions.CountAsync(x => x.AcademyId == academyId && x.SessionDate >= range.From && x.SessionDate <= range.To && (!branchId.HasValue || x.BranchId == branchId) && x.Status != TrainingSessionStatus.Cancelled), present = await todayAttendance.CountAsync(x => (x.Status == AttendanceStatus.Present || x.Status == AttendanceStatus.Late)), late = await todayAttendance.CountAsync(x => x.Status == AttendanceStatus.Late), excused = await todayAttendance.CountAsync(x => x.Status == AttendanceStatus.Excused), absent = await todayAttendance.CountAsync(x => x.Status == AttendanceStatus.Absent), notRecorded = await todayAttendance.CountAsync(x => x.Status == AttendanceStatus.NotRecorded) },
            bySport, byBranch, latest, payroll,
            operations = new { coachPerformance, groupAttention, pendingExpenses, unpaidExpenses, receivables, accessChanges, pendingPaymentBacklog, pendingPaymentQueueBacklog,
                openSafetyFeedback = current.Role != AcademyRole.AcademyOwner ? 0 : await db.GuardianFeedbacks.CountAsync(x => x.AcademyId == academyId && x.IsActive && x.Kind != "Rating" && x.Status != "Resolved" && x.Status != "Closed" && (x.ReportedUrgency == "Safety" || x.Urgency == "Safety") && (!branchId.HasValue || x.BranchId == branchId)),
                overdueFeedback = await db.GuardianFeedbacks.CountAsync(x => x.AcademyId == academyId && x.IsActive && x.Kind != "Rating" && x.Status != "Resolved" && x.Status != "Closed" && x.DueOn != null && x.DueOn < feedbackToday && (!branchId.HasValue || x.BranchId == branchId)),
                openComplaints = await db.GuardianFeedbacks.CountAsync(x => x.AcademyId == academyId && x.IsActive && x.Kind == "Complaint" && x.Status != "Resolved" && x.Status != "Closed" && (!branchId.HasValue || x.BranchId == branchId)),
                openSuggestions = await db.GuardianFeedbacks.CountAsync(x => x.AcademyId == academyId && x.IsActive && x.Kind == "Suggestion" && x.Status != "Resolved" && x.Status != "Closed" && (!branchId.HasValue || x.BranchId == branchId)),
                openRequests = current.Role != AcademyRole.AcademyOwner ? 0 : await db.GuardianFeedbacks.CountAsync(x => x.AcademyId == academyId && x.IsActive && x.Kind == "Request" && x.Status != "Resolved" && x.Status != "Closed" && (!branchId.HasValue || x.BranchId == branchId)),
                attentionRule = "مدرب غير مسند، أو 3 سجلات حضور غير مسجلة، أو غياب 30% فأكثر من 5 سجلات على الأقل" }
        });
    }

    private static async Task<IResult> FinancialReport(DateOnly? from, DateOnly? to, bool? allDates, Guid? sportId, Guid? branchId, Guid? planId, string? provider, string? search, string? collectionType, string? externalKind, int? page, int? pageSize, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!;
        var range = await FinancialRange(current.AcademyId, from, to, db, clock, allDates == true);
        if (range.Error is not null) return range.Error;
        var externalQuery=ExternalFinancialQuery(current.AcademyId,range.Currency,range.StartUtc,range.EndUtcExclusive,sportId,branchId,planId,provider,search,db);
        if(!string.IsNullOrEmpty(collectionType)&&collectionType!="External")externalQuery=externalQuery.Where(r=>false);
        if(externalKind=="PoolRental")externalQuery=externalQuery.Where(r=>r.Agreement.Kind==Academy.Infrastructure.Finance.ExternalAgreementKind.PoolRental);
        if(externalKind=="ExternalProgram")externalQuery=externalQuery.Where(r=>r.Agreement.Kind==Academy.Infrastructure.Finance.ExternalAgreementKind.ExternalProgram);
        if (collectionType is not (null or "" or "Subscription" or "Product" or "Tournament" or "DayPass" or "External")) return Validation("collectionType", "نوع التحصيل غير صالح.");
        if(externalKind is not(null or "" or "PoolRental" or "ExternalProgram") || !string.IsNullOrEmpty(externalKind)&&collectionType!="External")return Validation("externalKind","اختر نوع التحصيل الخارجي ونوع الاتفاق الصحيح.");
        var query = FinancialQuery(current.AcademyId, range.Currency, range.StartUtc, range.EndUtcExclusive, sportId, branchId, planId, provider, search, db);
        var productQuery = ProductFinancialQuery(current.AcademyId, range.Currency, range.StartUtc, range.EndUtcExclusive, sportId, branchId, provider, search, db);
        var tournamentQuery = TournamentQuery(current.AcademyId, range.Currency, range.StartUtc, range.EndUtcExclusive, sportId, branchId, provider, search, db);
        var dayPassQuery = DayPassFinancialQuery(current.AcademyId, range.Currency, range.StartUtc, range.EndUtcExclusive, sportId, branchId, planId, provider, search, db);
        var refundQuery=RefundFinancialQuery(current.AcademyId,range.Currency,range.StartUtc,range.EndUtcExclusive,sportId,branchId,planId,provider,search,db);
        if (collectionType is "Product" or "Tournament" or "DayPass" or "External") query = query.Where(x => false);
        if (collectionType is "Product" or "Tournament" or "DayPass" or "External") refundQuery=refundQuery.Where(x=>false);
        if (collectionType is "Subscription" or "Tournament" or "DayPass" or "External" || planId.HasValue) productQuery = productQuery.Where(x => false);
        if (collectionType is "Subscription" or "Product" or "DayPass" or "External" || planId.HasValue) tournamentQuery = tournamentQuery.Where(x => false);
        if (collectionType is "Subscription" or "Product" or "Tournament" or "External") dayPassQuery = dayPassQuery.Where(x => false);
        var count = await query.CountAsync() + await productQuery.CountAsync() + await tournamentQuery.CountAsync()+await refundQuery.CountAsync()+await dayPassQuery.CountAsync()+await externalQuery.CountAsync();
        var total = (await query.SumAsync(x => (decimal?)x.Amount) ?? 0) + (await productQuery.SumAsync(x => (decimal?)x.Amount) ?? 0) + (await tournamentQuery.SumAsync(x => (decimal?)x.Amount) ?? 0) + (await dayPassQuery.SumAsync(x => (decimal?)x.Amount) ?? 0)+(await externalQuery.SumAsync(x=>(decimal?)x.Amount)??0);
        var refunds = await refundQuery.SumAsync(r => (decimal?)r.Amount) ?? 0;
        var size = Math.Clamp(pageSize ?? 50, 1, 100); var currentPage = Math.Clamp(page ?? 1, 1, 100000);
        var ids = await query.Select(x => new { x.Id, Date = x.ConfirmedAtUtc }).Concat(productQuery.Select(x => new { x.Id, Date = x.ConfirmedAtUtc!.Value })).Concat(tournamentQuery.Select(x => new { x.Id, Date = x.ConfirmedAtUtc!.Value })).Concat(refundQuery.Select(x=>new{x.Id,Date=x.CreatedAtUtc})).Concat(dayPassQuery.Select(x=>new{x.Id,Date=x.ReviewedAtUtc!.Value})).Concat(externalQuery.Select(x=>new{x.Id,Date=x.ReceivedAtUtc})).OrderByDescending(x => x.Date).ThenBy(x => x.Id).Skip((currentPage - 1) * size).Take(size).Select(x => x.Id).ToListAsync();
        var rows = await FinancialRows(query.Where(x => ids.Contains(x.Id)), current.AcademyId, db).ToListAsync();
        rows.AddRange(await ProductFinancialRows(productQuery.Where(x => ids.Contains(x.Id)), db).ToListAsync());
        rows.AddRange(await TournamentRows(tournamentQuery.Where(x => ids.Contains(x.Id)), db).ToListAsync());
        rows.AddRange(await RefundFinancialRows(refundQuery.Where(x=>ids.Contains(x.Id)),db).ToListAsync());
        rows.AddRange(await DayPassFinancialRows(dayPassQuery.Where(x=>ids.Contains(x.Id)),db).ToListAsync());
        rows.AddRange(await ExternalFinancialRows(externalQuery.Where(x=>ids.Contains(x.Id)),db).ToListAsync());
        return Results.Ok(new { from = range.From, to = range.To, totalAmount = total, totalRefunds = refunds, netAmount = total - refunds, totalCount = count, currency = range.Currency, page = currentPage, pageSize = size, items = rows.OrderByDescending(x => x.ConfirmedAtUtc).ThenBy(x => x.CollectionId) });
    }

    private static async Task<IResult> FinancialExport(DateOnly? from, DateOnly? to, bool? allDates, Guid? sportId, Guid? branchId, Guid? planId, string? provider, string? search, string? collectionType, string? externalKind, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!;
        var range = await FinancialRange(current.AcademyId, from, to, db, clock, allDates == true);
        if (range.Error is not null) return range.Error;
        if (collectionType is not (null or "" or "Subscription" or "Product" or "Tournament" or "DayPass" or "External")) return Validation("collectionType", "نوع التحصيل غير صالح.");
        if(externalKind is not(null or "" or "PoolRental" or "ExternalProgram") || !string.IsNullOrEmpty(externalKind)&&collectionType!="External")return Validation("externalKind","اختر نوع التحصيل الخارجي ونوع الاتفاق الصحيح.");
        var query = FinancialQuery(current.AcademyId, range.Currency, range.StartUtc, range.EndUtcExclusive, sportId, branchId, planId, provider, search, db);
        var products = ProductFinancialQuery(current.AcademyId, range.Currency, range.StartUtc, range.EndUtcExclusive, sportId, branchId, provider, search, db);
        var tournaments = TournamentQuery(current.AcademyId, range.Currency, range.StartUtc, range.EndUtcExclusive, sportId, branchId, provider, search, db);
        var dayPasses = DayPassFinancialQuery(current.AcademyId, range.Currency, range.StartUtc, range.EndUtcExclusive, sportId, branchId, planId, provider, search, db);
        var refunds=RefundFinancialQuery(current.AcademyId,range.Currency,range.StartUtc,range.EndUtcExclusive,sportId,branchId,planId,provider,search,db);
        var externalQuery=ExternalFinancialQuery(current.AcademyId,range.Currency,range.StartUtc,range.EndUtcExclusive,sportId,branchId,planId,provider,search,db);
        if(!string.IsNullOrEmpty(collectionType)&&collectionType!="External")externalQuery=externalQuery.Where(r=>false);
        if(externalKind=="PoolRental")externalQuery=externalQuery.Where(r=>r.Agreement.Kind==Academy.Infrastructure.Finance.ExternalAgreementKind.PoolRental);
        if(externalKind=="ExternalProgram")externalQuery=externalQuery.Where(r=>r.Agreement.Kind==Academy.Infrastructure.Finance.ExternalAgreementKind.ExternalProgram);
        var count = (collectionType is "Product" or "Tournament" or "DayPass" or "External" ? 0 : await query.CountAsync()+await refunds.CountAsync()) + (collectionType is "Subscription" or "Tournament" or "DayPass" or "External" || planId.HasValue ? 0 : await products.CountAsync()) + (collectionType is "Subscription" or "Product" or "DayPass" or "External" || planId.HasValue ? 0 : await tournaments.CountAsync()) + (collectionType is "Subscription" or "Product" or "Tournament" or "External" ? 0 : await dayPasses.CountAsync())+await externalQuery.CountAsync();
        if (count > MaxExportRows) return Results.Problem(statusCode: 413, title: "نطاق التصدير كبير", detail: $"الحد الأقصى {MaxExportRows} سجل.");
        var rows = collectionType is "Product" or "Tournament" or "DayPass" or "External" ? new List<FinancialRow>() : await FinancialRows(query.OrderBy(x => x.ConfirmedAtUtc), current.AcademyId, db).ToListAsync();
        if (collectionType is not ("Subscription" or "Tournament" or "DayPass" or "External") && !planId.HasValue) rows.AddRange(await ProductFinancialRows(products, db).ToListAsync());
        if (collectionType is not ("Subscription" or "Product" or "DayPass" or "External") && !planId.HasValue) rows.AddRange(await TournamentRows(tournaments, db).ToListAsync());
        if(collectionType is not ("Product" or "Tournament" or "DayPass" or "External")) rows.AddRange(await RefundFinancialRows(refunds,db).ToListAsync());
        if(collectionType is not ("Subscription" or "Product" or "Tournament" or "External")) rows.AddRange(await DayPassFinancialRows(dayPasses,db).ToListAsync());
        rows.AddRange(await ExternalFinancialRows(externalQuery,db).ToListAsync());
        var csv = new List<string[]> { new[] { "رقم الإيصال", "اللاعب / الطرف", "الرياضة", "الفرع", "المجموعة", "الباقة", "المبلغ", "العملة", "طريقة الدفع", "مزود الدفع", "مرجع المزود", "وقت التحصيل", "الاسترداد", "الصافي", "نوع التحصيل" } };
        csv.AddRange(rows.OrderBy(x=>x.ConfirmedAtUtc).ThenBy(x=>x.CollectionId).Select(x => new[] { x.ReceiptNumber, x.Player, x.Sport, x.Branch, x.Group, x.Plan, x.Amount.ToString("0.00", CultureInfo.InvariantCulture), x.Currency, x.PaymentMethod, x.Provider, x.ProviderReference, x.ConfirmedAtUtc.ToString("O"), x.RefundAmount.ToString("0.00", CultureInfo.InvariantCulture), (x.Amount - x.RefundAmount).ToString("0.00", CultureInfo.InvariantCulture), x.CollectionType == "Product" ? "منتج رياضي" : x.CollectionType == "Tournament" ? "رسم بطولة" : x.CollectionType=="External"?"اتفاق خارجي":x.CollectionType=="DayPass"?"زيارة يومية":x.CollectionType=="Void"?"عكس إيصال نقدي":x.CollectionType=="Refund"?"استرداد اشتراك":"اشتراك" }));
        return Csv(csv, $"financial-collections-{range.From:yyyy-MM}.csv");
    }

    private static async Task<IResult> AttendanceReport(string? subject, int? month, int? year, DateOnly? from, DateOnly? to, bool? allDates, Guid? sportId, Guid? branchId, Guid? groupId, string? status, string? search, Guid? coachId, int? birthYear, int? page, int? pageSize, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!;
        var range = AttendanceRange(month, year, from, to, clock.Today, allDates == true);
        if (range.Error is not null) return range.Error;
        var staff = string.Equals(subject, "staff", StringComparison.OrdinalIgnoreCase);
        if (staff && birthYear.HasValue) return Validation("birthYear", "فلتر سنة الميلاد متاح للاعبين فقط.");
        var size = Math.Clamp(pageSize ?? 50, 1, 100); var currentPage = Math.Max(page ?? 1, 1); var userId = UserId(principal);
        if (staff)
        {
            var query = StaffAttendanceQuery(current, userId, range.From, range.To, sportId, branchId, groupId, status, search, coachId, db);
            var count = await query.CountAsync(); var rows = await StaffAttendanceRows(query.OrderByDescending(x => x.TrainingSession.SessionDate).ThenBy(x => x.AcademyMembership.User.DisplayName).Skip((currentPage - 1) * size).Take(size)).ToListAsync();
            return Results.Ok(new { subject = "staff", from = range.From, to = range.To, coverage = "StoredRecordsOnly", totalCount = count, page = currentPage, pageSize = size, items = rows });
        }
        var players = PlayerAttendanceQuery(current, userId, range.From, range.To, sportId, branchId, groupId, status, search, birthYear, db);
        var playerCount = await players.CountAsync(); var playerRows = await PlayerAttendanceRows(players.OrderByDescending(x => x.TrainingSession.SessionDate).ThenBy(x => x.SportEnrollment.Player.ArabicName).Skip((currentPage - 1) * size).Take(size)).ToListAsync();
        return Results.Ok(new { subject = "players", from = range.From, to = range.To, coverage = "StoredRecordsOnly", totalCount = playerCount, page = currentPage, pageSize = size, items = playerRows });
    }

    private static async Task<IResult> AttendanceExport(string? subject, int? month, int? year, DateOnly? from, DateOnly? to, bool? allDates, Guid? sportId, Guid? branchId, Guid? groupId, string? status, string? search, Guid? coachId, int? birthYear, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!; var range = AttendanceRange(month, year, from, to, clock.Today, allDates == true); if (range.Error is not null) return range.Error;
        var staff = string.Equals(subject, "staff", StringComparison.OrdinalIgnoreCase); var userId = UserId(principal);
        if (staff && birthYear.HasValue) return Validation("birthYear", "فلتر سنة الميلاد متاح للاعبين فقط.");
        if (staff)
        {
            var query = StaffAttendanceQuery(current, userId, range.From, range.To, sportId, branchId, groupId, status, search, coachId, db).OrderBy(x => x.TrainingSession.SessionDate);
            if (await query.CountAsync() > MaxExportRows) return Results.Problem(statusCode: 413, title: "نطاق التصدير كبير", detail: $"الحد الأقصى {MaxExportRows} سجل.");
            var rows = await StaffAttendanceRows(query).ToListAsync();
            var csv = new List<string[]> { new[] { "التاريخ", "الوقت", "الموظف", "الدور", "الرياضة", "الفرع", "المجموعة", "الحالة" } };
            csv.AddRange(rows.Select(x => new[] { x.Date.ToString("yyyy-MM-dd"), x.Time.ToString("HH:mm"), x.Staff, x.Role, x.Sport, x.Branch, x.Group, ArabicStatus(x.Status) }));
            return Csv(csv, $"attendance-staff-{range.From:yyyy-MM}.csv");
        }
        var players = PlayerAttendanceQuery(current, userId, range.From, range.To, sportId, branchId, groupId, status, search, birthYear, db).OrderBy(x => x.TrainingSession.SessionDate);
        if (await players.CountAsync() > MaxExportRows) return Results.Problem(statusCode: 413, title: "نطاق التصدير كبير", detail: $"الحد الأقصى {MaxExportRows} سجل.");
        var playerRows = await PlayerAttendanceRows(players).ToListAsync();
        var playerCsv = new List<string[]> { new[] { "التاريخ", "الوقت", "كود اللاعب", "اللاعب", "سنة الميلاد", "الرياضة", "الفرع", "المجموعة", "الحالة", "تم خصم حصة" } };
        playerCsv.AddRange(playerRows.Select(x => new[] { x.Date.ToString("yyyy-MM-dd"), x.Time.ToString("HH:mm"), x.PlayerCode, x.Player, x.BirthYear.ToString(CultureInfo.InvariantCulture), x.Sport, x.Branch, x.Group, ArabicStatus(x.Status), x.SessionConsumed ? "نعم" : "لا" }));
        return Csv(playerCsv, $"attendance-players-{range.From:yyyy-MM}.csv");
    }

    private static async Task<IResult> ReceiptList(DateOnly? from, DateOnly? to, int? page, int? pageSize, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!; var range = await FinancialRange(current.AcademyId, from, to, db, clock); if (range.Error is not null) return range.Error;
        var query = db.Receipts.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.PaidAtUtc >= range.StartUtc && x.PaidAtUtc < range.EndUtcExclusive);
        var size = Math.Clamp(pageSize ?? 50, 1, 100); var currentPage = Math.Max(page ?? 1, 1);
        var rows = await query.OrderByDescending(x => x.PaidAtUtc).Skip((currentPage - 1) * size).Take(size).Select(x => new { x.Id, x.ReceiptNumber, player = x.PlayerNameSnapshot, sport = x.SportNameSnapshot, plan = x.PlanNameSnapshot, x.Amount, x.Currency, x.PaidAtUtc }).ToListAsync();
        return Results.Ok(new { totalCount = await query.CountAsync(), page = currentPage, pageSize = size, items = rows });
    }

    private static IQueryable<PaymentCollection> FinancialQuery(Guid academyId, string currency, DateTimeOffset start, DateTimeOffset end, Guid? sportId, Guid? branchId, Guid? planId, string? provider, string? search, FoundationDbContext db)
    {
        var query = db.Collections.AsNoTracking().Where(x => x.AcademyId == academyId && x.Currency == currency && x.ConfirmedAtUtc >= start && x.ConfirmedAtUtc < end);
        var actualPostings=CashFinancialAttribution.ReceiptSources(db,academyId);
        // IgnoreQueryFilters in a composed subquery affects the query compilation,
        // not just that subquery. Therefore branch visibility is explicit here:
        // Cash financial facts belong to the authorized collecting branch. This
        // is not permission to read the foreign child's operational profile.
        query=query.Where(x=>!db.BranchScopeRestricted || actualPostings.Any(p=>p.CollectionId==x.Id&&db.RequestBranchIds.Contains(p.BranchId)) ||
            !actualPostings.Any(p=>p.CollectionId==x.Id)&&db.RequestBranchIds.Contains(x.SportEnrollment.BranchId));
        if (sportId.HasValue) query = query.Where(x => x.SportEnrollment.SportId == sportId);
        // Cash belongs to the immutable collecting shift, not the player's
        // operational branch. Legacy/non-cash rows retain their existing attribution.
        if (branchId.HasValue) query = query.Where(x => db.CashReceiptPostings.Any(p=>p.CollectionId==x.Id&&p.BranchId==branchId) || !db.CashReceiptPostings.Any(p=>p.CollectionId==x.Id)&&x.SportEnrollment.BranchId==branchId);
        if (planId.HasValue) query = query.Where(x => x.RenewalRequest.SubscriptionPlanId == planId);
        if (!string.IsNullOrWhiteSpace(provider)) query = query.Where(x => x.Provider == provider.Trim() || x.PaymentMethod == provider.Trim());
        if (!string.IsNullOrWhiteSpace(search)) { var value = search.Trim(); query = query.Where(x => x.SportEnrollment.Player.ArabicName.Contains(value) || x.SportEnrollment.Player.PlayerCode.Contains(value) || db.Receipts.Any(r => r.AcademyId == academyId && r.CollectionId == x.Id && r.ReceiptNumber.Contains(value))); }
        return query;
    }

    private static IQueryable<DayPassTicket> DayPassFinancialQuery(Guid academyId, string currency, DateTimeOffset start, DateTimeOffset end, Guid? sportId, Guid? branchId, Guid? planId, string? provider, string? search, FoundationDbContext db)
    {
        var query = db.DayPassTickets.AsNoTracking().Where(x => x.AcademyId == academyId && x.Status == DayPassTicketStatus.Confirmed && x.Currency == currency && x.ReviewedAtUtc >= start && x.ReviewedAtUtc < end);
        if (branchId.HasValue) query = query.Where(x => x.BranchId == branchId);
        if (planId.HasValue) query = query.Where(x => x.SubscriptionPlanId == planId);
        if (sportId.HasValue) query = query.Where(x => db.ResourceBookings.Any(b => b.Id == x.ResourceBookingId && db.FacilityResources.Any(r => r.Id == b.ResourceId && db.FacilitySpaces.Any(s => s.Id == r.FacilitySpaceId && s.SportId == sportId))));
        if (!string.IsNullOrWhiteSpace(provider) && provider is not ("Online" or "Manual")) query = query.Where(x => false);
        if (!string.IsNullOrWhiteSpace(search)) { var value = search.Trim(); query = query.Where(x => x.Player.ArabicName.Contains(value) || x.Player.PlayerCode.Contains(value)); }
        return query;
    }

    private static IQueryable<FinancialRow> DayPassFinancialRows(IQueryable<DayPassTicket> query, FoundationDbContext db) =>
        from ticket in query
        join booking in db.ResourceBookings on ticket.ResourceBookingId equals booking.Id
        join resource in db.FacilityResources on booking.ResourceId equals resource.Id
        join space in db.FacilitySpaces on resource.FacilitySpaceId equals space.Id
        join sport in db.Sports on space.SportId equals sport.Id
        join branch in db.Branches on ticket.BranchId equals branch.Id
        select new FinancialRow(ticket.Id, ticket.Id, "DP-" + ticket.Id.ToString(), ticket.Player.ArabicName, sport.ArabicName, branch.ArabicName, resource.Name, ticket.PlanName, ticket.Amount, ticket.Currency, "Online", "Manual", "proof-reviewed", ticket.ReviewedAtUtc!.Value, 0, "DayPass");

    private static IQueryable<FinancialRow> FinancialRows(IQueryable<PaymentCollection> query, Guid academyId, FoundationDbContext db) => query.Select(x => new FinancialRow(
        x.Id,
        db.Receipts.Where(r => r.AcademyId == academyId && r.CollectionId == x.Id).Select(r => r.Id).Single(),
        db.Receipts.Where(r => r.AcademyId == academyId && r.CollectionId == x.Id).Select(r => r.ReceiptNumber).Single(),
        db.Receipts.Where(r => r.AcademyId == academyId && r.CollectionId == x.Id).Select(r => r.PlayerNameSnapshot).Single(),
        db.Receipts.Where(r => r.AcademyId == academyId && r.CollectionId == x.Id).Select(r => r.SportNameSnapshot).Single(),
        db.CashReceiptPostings.Where(p=>p.CollectionId==x.Id).Select(p=>db.Branches.Where(b=>b.Id==p.BranchId).Select(b=>b.ArabicName).Single()).SingleOrDefault() ?? x.SportEnrollment.Branch.ArabicName,
        db.BranchScopeRestricted&&!db.RequestBranchIds.Contains(x.SportEnrollment.BranchId)?"خدمة في فرع تسجيل آخر":x.SportEnrollment.TrainingGroup == null ? "في انتظار المجموعة" : x.SportEnrollment.TrainingGroup.ArabicName,
        db.Receipts.Where(r => r.AcademyId == academyId && r.CollectionId == x.Id).Select(r => r.PlanNameSnapshot).Single(),
        x.Amount, x.Currency, x.PaymentMethod, x.Provider, x.ProviderReference, x.ConfirmedAtUtc,
        0, "Subscription"));

    // Refunds belong to their payout date, even when the original receipt lies
    // outside the period. Source queries still enforce tenant/resource/branch scope.
    private static IQueryable<CollectionRefund> RefundFinancialQuery(Guid academy,string currency,DateTimeOffset start,DateTimeOffset end,Guid? sport,Guid? branch,Guid? plan,string? method,string? search,FoundationDbContext db)
    {
        var sources=FinancialQuery(academy,currency,DateTimeOffset.MinValue,DateTimeOffset.MaxValue,sport,null,plan,null,search,db);
        var q=db.CollectionRefunds.AsNoTracking().Where(r=>r.AcademyId==academy&&r.Currency==currency&&r.CreatedAtUtc>=start&&r.CreatedAtUtc<end&&sources.Any(c=>c.Id==r.CollectionId));
        var actualPayouts=CashFinancialAttribution.RefundSources(db,academy);
        q=q.Where(r=>!db.BranchScopeRestricted || !actualPayouts.Any(p=>p.RefundId==r.Id)||actualPayouts.Any(p=>p.RefundId==r.Id&&db.RequestBranchIds.Contains(p.BranchId)));
        if(branch.HasValue) q=q.Where(r=>db.CashRefundPostings.Any(p=>p.RefundId==r.Id&&p.BranchId==branch) || !db.CashRefundPostings.Any(p=>p.RefundId==r.Id)&&db.Collections.Any(c=>c.Id==r.CollectionId&&(db.CashReceiptPostings.Any(p=>p.CollectionId==c.Id&&p.BranchId==branch)||!db.CashReceiptPostings.Any(p=>p.CollectionId==c.Id)&&c.SportEnrollment.BranchId==branch)));
        if(!string.IsNullOrWhiteSpace(method)) {
            var value=method.Trim();q=q.Where(r=>(r.PayoutMethod ?? (db.CashRefundPostings.Any(p=>p.RefundId==r.Id)?"Cash":"Unspecified"))==value);
        }
        return q;
    }
    private static IQueryable<FinancialRow> RefundFinancialRows(IQueryable<CollectionRefund> q,FoundationDbContext db) =>
        from r in q join c in db.Collections on r.CollectionId equals c.Id join receipt in db.Receipts on r.CollectionId equals receipt.CollectionId
        select new FinancialRow(r.Id,receipt.Id,"استرداد — "+receipt.ReceiptNumber,receipt.PlayerNameSnapshot,receipt.SportNameSnapshot,
            db.CashRefundPostings.Where(p=>p.RefundId==r.Id).Select(p=>db.Branches.Where(b=>b.Id==p.BranchId).Select(b=>b.ArabicName).Single()).SingleOrDefault()
            ?? db.CashReceiptPostings.Where(p=>p.CollectionId==c.Id).Select(p=>db.Branches.Where(b=>b.Id==p.BranchId).Select(b=>b.ArabicName).Single()).SingleOrDefault() ?? c.SportEnrollment.Branch.ArabicName,
            db.BranchScopeRestricted&&!db.RequestBranchIds.Contains(c.SportEnrollment.BranchId)?"خدمة في فرع تسجيل آخر":c.SportEnrollment.TrainingGroup==null?"في انتظار المجموعة":c.SportEnrollment.TrainingGroup.ArabicName,receipt.PlanNameSnapshot,0,r.Currency,
            r.PayoutMethod ?? (db.CashRefundPostings.Any(p=>p.RefundId==r.Id)?"Cash":"Unspecified"),"Manual",r.Id.ToString(),r.CreatedAtUtc,r.Amount,db.CashReceiptVoids.Any(v=>v.RefundId==r.Id)?"Void":"Refund");

    private static IQueryable<Academy.Infrastructure.Operations.ProductSale> ProductFinancialQuery(Guid academy, string currency, DateTimeOffset start, DateTimeOffset end, Guid? sport, Guid? branch, string? method, string? search, FoundationDbContext db)
    {
        var q = db.ProductSales.AsNoTracking().Where(x => x.AcademyId == academy && x.Status == "Confirmed" && x.Currency == currency && x.ConfirmedAtUtc >= start && x.ConfirmedAtUtc < end);
        if (sport.HasValue) q = q.Where(x => x.SportId == sport); if (branch.HasValue) q = q.Where(x => x.BranchId == branch);
        if (!string.IsNullOrWhiteSpace(method)) q = q.Where(x => x.PaymentMethod == method);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => db.Players.Any(p => p.AcademyId == academy && p.Id == x.PlayerId && p.ArabicName.Contains(search.Trim())));
        return q;
    }
    private static IQueryable<FinancialRow> ProductFinancialRows(IQueryable<Academy.Infrastructure.Operations.ProductSale> q, FoundationDbContext db) => q.Select(x => new FinancialRow(
        x.Id, Guid.Empty, "PR-" + x.Id.ToString(), db.Players.Where(p => p.AcademyId == x.AcademyId && p.Id == x.PlayerId).Select(p => p.ArabicName).FirstOrDefault() ?? db.Users.Where(p => p.Id == x.RequestedByUserId).Select(p => p.DisplayName).FirstOrDefault()!,
        x.SportName, x.BranchName ?? "غير محدد", "—", x.ProductName + " × " + x.Quantity, x.Amount, x.Currency, x.PaymentMethod!, "Manual", x.Id.ToString(), x.ConfirmedAtUtc!.Value, 0, "Product"));

    private static IQueryable<PlayerAttendance> PlayerAttendanceQuery(TenantMembership current, Guid userId, DateOnly from, DateOnly to, Guid? sportId, Guid? branchId, Guid? groupId, string? status, string? search, int? birthYear, FoundationDbContext db)
    {
        var query = db.PlayerAttendances.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.TrainingSession.SessionDate >= from && x.TrainingSession.SessionDate <= to && x.TrainingSession.Status != TrainingSessionStatus.Cancelled);
        if (current.Role == AcademyRole.Coach) query = query.Where(x => FoundationDbContext.IsSessionStaffAssigned(current.AcademyId,current.MembershipId,x.TrainingSessionId));
        if (sportId.HasValue) query = query.Where(x => x.TrainingSession.SportId == sportId); if (branchId.HasValue) query = query.Where(x => x.TrainingSession.BranchId == branchId); if (groupId.HasValue) query = query.Where(x => x.TrainingGroupId == groupId);
        if (Enum.TryParse<AttendanceStatus>(status, true, out var parsed)) query = query.Where(x => x.Status == parsed);
        if (!string.IsNullOrWhiteSpace(search)) { var value = search.Trim(); query = query.Where(x => x.SportEnrollment.Player.ArabicName.Contains(value) || x.SportEnrollment.Player.PlayerCode.Contains(value)); }
        if (birthYear.HasValue) { var start = new DateOnly(birthYear.Value, 1, 1); var end = start.AddYears(1); query = query.Where(x => x.SportEnrollment.Player.DateOfBirth >= start && x.SportEnrollment.Player.DateOfBirth < end); }
        return query;
    }

    private static IQueryable<PlayerAttendanceRow> PlayerAttendanceRows(IQueryable<PlayerAttendance> query) => query.Select(x => new PlayerAttendanceRow(x.Id, x.TrainingSession.SessionDate, x.TrainingSession.StartTime, x.SportEnrollment.Player.PlayerCode, x.SportEnrollment.Player.ArabicName, x.SportEnrollment.Player.DateOfBirth.Year, x.TrainingSession.Sport.ArabicName, x.TrainingSession.Branch.ArabicName, x.TrainingSession.TrainingGroup.ArabicName, x.Status.ToString(), x.ConsumedSubscriptionPeriodId != null));

    private static IQueryable<StaffAttendance> StaffAttendanceQuery(TenantMembership current, Guid userId, DateOnly from, DateOnly to, Guid? sportId, Guid? branchId, Guid? groupId, string? status, string? search, Guid? coachId, FoundationDbContext db)
    {
        var query = db.StaffAttendances.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.TrainingSession.SessionDate >= from && x.TrainingSession.SessionDate <= to && x.TrainingSession.Status != TrainingSessionStatus.Cancelled);
        if (coachId.HasValue) query = query.Where(x => x.AcademyMembershipId == coachId);
        if (current.Role == AcademyRole.Coach) query = query.Where(x => x.AcademyMembershipId == current.MembershipId);
        if (sportId.HasValue) query = query.Where(x => x.TrainingSession.SportId == sportId); if (branchId.HasValue) query = query.Where(x => x.TrainingSession.BranchId == branchId); if (groupId.HasValue) query = query.Where(x => x.TrainingGroupId == groupId);
        if (Enum.TryParse<AttendanceStatus>(status, true, out var parsed)) query = query.Where(x => x.Status == parsed);
        if (!string.IsNullOrWhiteSpace(search)) { var value = search.Trim(); query = query.Where(x => x.AcademyMembership.User.DisplayName.Contains(value)); }
        return query;
    }

    private static IQueryable<StaffAttendanceRow> StaffAttendanceRows(IQueryable<StaffAttendance> query) => query.Select(x => new StaffAttendanceRow(x.Id, x.TrainingSession.SessionDate, x.TrainingSession.StartTime, x.AcademyMembership.User.DisplayName, x.AcademyMembership.Role.ToString(), x.TrainingSession.Sport.ArabicName, x.TrainingSession.Branch.ArabicName, x.TrainingSession.TrainingGroup.ArabicName, x.Status.ToString()));

    private static async Task<FinancialRangeResult> FinancialRange(Guid academyId, DateOnly? from, DateOnly? to, FoundationDbContext db, ISubscriptionClock clock, bool allDates = false)
    {
        var academy = await db.Academies.AsNoTracking().Where(x => x.Id == academyId).Select(x => new { x.TimeZone, x.DefaultCurrency }).SingleAsync();
        if (allDates && from is null && to is null) return new(DateOnly.MinValue, clock.Today, DateTimeOffset.MinValue, DateTimeOffset.MaxValue, academy.DefaultCurrency, null);
        var end = to ?? clock.Today; var start = from ?? new DateOnly(end.Year, end.Month, 1);
        if (end < start || end.DayNumber - start.DayNumber > MaxInteractiveDays) return new(start, end, default, default, academy.DefaultCurrency, Validation("range", $"نطاق التاريخ يجب ألا يتجاوز {MaxInteractiveDays} يومًا."));
        var range = UtcRange(start, end, academy.TimeZone); return new(start, end, range.Start, range.EndExclusive, academy.DefaultCurrency, null);
    }

    private static AttendanceRangeResult AttendanceRange(int? month, int? year, DateOnly? from, DateOnly? to, DateOnly today, bool allDates = false)
    {
        if (allDates && from is null && to is null && month is null && year is null) return new(DateOnly.MinValue, DateOnly.MaxValue, null);
        DateOnly start; DateOnly end;
        try
        {
            if (from.HasValue || to.HasValue) { start = from ?? to!.Value; end = to ?? from!.Value; }
            else { var selectedYear = year ?? today.Year; var selectedMonth = month ?? today.Month; start = new DateOnly(selectedYear, selectedMonth, 1); end = month.HasValue || year.HasValue ? start.AddMonths(1).AddDays(-1) : today; }
        }
        catch (ArgumentOutOfRangeException) { return new(default, default, Validation("date", "الشهر أو السنة غير صالحين.")); }
        if (end < start || end.DayNumber - start.DayNumber > MaxInteractiveDays) return new(start, end, Validation("range", $"نطاق التاريخ يجب ألا يتجاوز {MaxInteractiveDays} يومًا."));
        return new(start, end, null);
    }

    internal static (DateTimeOffset Start, DateTimeOffset EndExclusive) UtcRange(DateOnly from, DateOnly to, string timeZoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var localStart = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var localEnd = DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return (new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, zone)), new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, zone)));
    }

    internal static IResult Csv(IEnumerable<string[]> rows, string filename)
    {
        var text = string.Join("\r\n", rows.Select(row => string.Join(',', row.Select(CsvCell)))) + "\r\n";
        var body = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        return Results.File(body, "text/csv; charset=utf-8", filename);
    }

    internal static string CsvCell(string? value)
    {
        var safe = value ?? string.Empty;
        if (safe.Length > 0 && safe[0] is '=' or '+' or '-' or '@') safe = "'" + safe;
        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }

    private static string ArabicStatus(string value) => value switch { "Present" => "حاضر", "Late" => "متأخر", "Excused" => "معتذر", "Absent" => "غائب", "NotRecorded" => "لم يُسجل", _ => value };
    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static IResult Validation(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
}

public sealed record ReportBreakdown(string Name, decimal Amount, int Count);
public sealed record LatestCollection(Guid ReceiptId, string ReceiptNumber, string Player, decimal Amount, string Currency, DateTimeOffset ConfirmedAtUtc);
public sealed record FinancialRow(Guid CollectionId, Guid ReceiptId, string ReceiptNumber, string Player, string Sport, string Branch, string Group, string Plan, decimal Amount, string Currency, string PaymentMethod, string Provider, string ProviderReference, DateTimeOffset ConfirmedAtUtc, decimal RefundAmount, string CollectionType = "Subscription")
{
    public decimal NetAmount => Amount - RefundAmount;
}
public sealed record PlayerAttendanceRow(Guid Id, DateOnly Date, TimeOnly Time, string PlayerCode, string Player, int BirthYear, string Sport, string Branch, string Group, string Status, bool SessionConsumed);
public sealed record StaffAttendanceRow(Guid Id, DateOnly Date, TimeOnly Time, string Staff, string Role, string Sport, string Branch, string Group, string Status);
internal sealed record FinancialRangeResult(DateOnly From, DateOnly To, DateTimeOffset StartUtc, DateTimeOffset EndUtcExclusive, string Currency, IResult? Error);
internal sealed record AttendanceRangeResult(DateOnly From, DateOnly To, IResult? Error);
