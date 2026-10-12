using System.Globalization;
using System.Security.Claims;
using System.Text;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Api.Slice4;
using Academy.Api.Operations;
using Microsoft.AspNetCore.Authorization;
using Academy.Infrastructure.Attendance;
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
        reports.MapGet("/financial", FinancialReport).RequireAuthorization(AcademyPermissions.ReportsFinancialRead);
        reports.MapGet("/financial/export", FinancialExport).RequireAuthorization(AcademyPermissions.ReportsFinancialRead).RequireAuthorization(AcademyPermissions.ReportsExport);
        reports.MapGet("/attendance", AttendanceReport).RequireAuthorization(AcademyPermissions.ReportsAttendanceRead);
        reports.MapGet("/attendance/export", AttendanceExport).RequireAuthorization(AcademyPermissions.ReportsAttendanceRead).RequireAuthorization(AcademyPermissions.ReportsExport);
        reports.MapGet("/receipts", ReceiptList).RequireAuthorization(AcademyPermissions.ReportsFinancialRead);
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
        var c = await collections.Select(x => new { x.Id, x.Amount, x.PaymentMethod, x.ConfirmedAtUtc }).ToListAsync();
        var p = await products.Select(x => new { x.Id, x.Amount, x.PaymentMethod, ConfirmedAtUtc = x.ConfirmedAtUtc!.Value }).ToListAsync();
        p.AddRange(await tournaments.Select(x => new { x.Id, x.Amount, x.PaymentMethod, ConfirmedAtUtc = x.ConfirmedAtUtc!.Value }).ToListAsync());
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
        var nt = await db.TournamentPayments.CountAsync(x => x.AcademyId == academyId && x.Status == "Pending" && x.CreatedAtUtc >= range.StartUtc && x.CreatedAtUtc < range.EndUtcExclusive && (!branchId.HasValue || db.SportEnrollments.Any(e => e.AcademyId == academyId && e.Id == x.EnrollmentId && e.BranchId == branchId)));
        var subscriptionTotal = await db.SubscriptionPeriods.Where(x => x.AcademyId == academyId && (!branchId.HasValue || x.SportEnrollment.BranchId == branchId) && x.SportEnrollment.IsActive && x.SportEnrollment.Player.IsActive && x.Status != SubscriptionPeriodStatus.Cancelled).Select(x => x.SportEnrollment.PlayerId).Distinct().CountAsync();
        var todayAttendance = db.PlayerAttendances.Where(x => x.AcademyId == academyId && x.TrainingSession.SessionDate >= range.From && x.TrainingSession.SessionDate <= range.To && (!branchId.HasValue || x.TrainingSession.BranchId == branchId) && x.TrainingSession.Status != TrainingSessionStatus.Cancelled);
        var bySport = await collections.GroupBy(x => x.SportEnrollment.Sport.ArabicName).Select(x => new { name = x.Key, amount = x.Sum(y => y.Amount), count = x.Count() }).ToListAsync();
        var byBranch = await collections.GroupBy(x => new { x.SportEnrollment.BranchId, x.SportEnrollment.Branch.ArabicName }).Select(x => new { id = x.Key.BranchId, name = x.Key.ArabicName, amount = x.Sum(y => y.Amount), count = x.Count() }).ToListAsync();
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
                var staff = db.Employees.Where(x => x.AcademyId == academyId && x.IsActive && x.ContractStart <= realToday && (x.ContractEnd == null || x.ContractEnd >= realToday) && (!branchId.HasValue || x.BranchId == branchId));
                payroll = new { month = payrollMonth, employeeCount = await staff.CountAsync(), expiringContracts = await staff.CountAsync(x => x.ContractEnd != null && x.ContractEnd <= realToday.AddDays(30)), baseDue = salaries.Sum(x => x.BaseDue), deductions = salaries.Sum(x => x.Deductions), additions = salaries.Sum(x => x.Additions), netDue = salaries.Sum(x => x.NetDue), unpaid = salaries.Where(x => !x.IsPaid).Sum(x => x.NetDue) };
            }
        }
        return Results.Ok(new {
            asOfDate = today, from = range.From, to = range.To, currency = academy.DefaultCurrency,
            collections = new { total = money.Sum(x => x.amount), today = daily.Sum(x => x.amount), month = money.Sum(x => x.amount), todayCash = daily.Where(x => x.PaymentMethod == "Cash").Sum(x => x.amount), todayOnline = daily.Where(x => x.PaymentMethod != "Cash" && x.PaymentMethod!="Unspecified").Sum(x => x.amount), todayUnspecified=daily.Where(x=>x.PaymentMethod=="Unspecified").Sum(x=>x.amount), monthCash = money.Where(x => x.PaymentMethod == "Cash").Sum(x => x.amount), monthOnline = money.Where(x => x.PaymentMethod != "Cash" && x.PaymentMethod!="Unspecified").Sum(x => x.amount), monthUnspecified=money.Where(x=>x.PaymentMethod=="Unspecified").Sum(x=>x.amount) },
            paymentRequests = new { total = nr + nn + np + nt, newEnrollments = nn, renewals = nr, products = np, tournaments = nt },
            payments = new { confirmed = c.Count + p.Count, pending = nr + nn + np + nt },
            subscriptions = new { total = subscriptionTotal, active = await active.Select(x => x.SportEnrollment.PlayerId).Distinct().CountAsync(), expiring = await active.Where(x => x.EndDate <= range.To.AddDays(7)).Select(x => x.SportEnrollment.PlayerId).Distinct().CountAsync(), expired = await periods.Where(x => (x.EndDate < range.To || (x.PlanTypeSnapshot ?? x.SubscriptionPlan.PlanType) != SubscriptionPlanType.Duration && x.RemainingSessions <= 0) && !active.Any(a => a.SportEnrollment.PlayerId == x.SportEnrollment.PlayerId)).Select(x => x.SportEnrollment.PlayerId).Distinct().CountAsync() },
            activePlayers = await active.Select(x => x.SportEnrollment.PlayerId).Distinct().CountAsync(),
            coaches = await db.AcademyMemberships.CountAsync(x => x.AcademyId == academyId && x.Role == AcademyRole.Coach && x.IsActive && (!branchId.HasValue || db.StaffGroupAssignments.Any(a => a.AcademyId == academyId && a.AcademyMembershipId == x.Id && a.IsActive && a.TrainingGroup.IsActive && a.TrainingGroup.BranchId == branchId))),
            newSubscriptions = await db.SubscriptionPeriods.Where(x => x.AcademyId == academyId && x.CreatedAtUtc >= range.StartUtc && x.CreatedAtUtc < range.EndUtcExclusive && (!branchId.HasValue || x.SportEnrollment.BranchId == branchId) && !db.SubscriptionPeriods.Any(old => old.AcademyId == academyId && old.SportEnrollmentId == x.SportEnrollmentId && old.CreatedAtUtc < x.CreatedAtUtc)).CountAsync(),
            attendance = new { sessions = await db.TrainingSessions.CountAsync(x => x.AcademyId == academyId && x.SessionDate >= range.From && x.SessionDate <= range.To && (!branchId.HasValue || x.BranchId == branchId) && x.Status != TrainingSessionStatus.Cancelled), present = await todayAttendance.CountAsync(x => (x.Status == AttendanceStatus.Present || x.Status == AttendanceStatus.Late)), late = await todayAttendance.CountAsync(x => x.Status == AttendanceStatus.Late), excused = await todayAttendance.CountAsync(x => x.Status == AttendanceStatus.Excused), absent = await todayAttendance.CountAsync(x => x.Status == AttendanceStatus.Absent), notRecorded = await todayAttendance.CountAsync(x => x.Status == AttendanceStatus.NotRecorded) },
            bySport, byBranch, latest, payroll
        });
    }

    private static async Task<IResult> FinancialReport(DateOnly? from, DateOnly? to, bool? allDates, Guid? sportId, Guid? branchId, Guid? planId, string? provider, string? search, string? collectionType, int? page, int? pageSize, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!;
        var range = await FinancialRange(current.AcademyId, from, to, db, clock, allDates == true);
        if (range.Error is not null) return range.Error;
        if (collectionType is not (null or "" or "Subscription" or "Product" or "Tournament")) return Validation("collectionType", "نوع التحصيل غير صالح.");
        var query = FinancialQuery(current.AcademyId, range.Currency, range.StartUtc, range.EndUtcExclusive, sportId, branchId, planId, provider, search, db);
        var productQuery = ProductFinancialQuery(current.AcademyId, range.Currency, range.StartUtc, range.EndUtcExclusive, sportId, branchId, provider, search, db);
        var tournamentQuery = TournamentQuery(current.AcademyId, range.Currency, range.StartUtc, range.EndUtcExclusive, sportId, branchId, provider, search, db);
        var refundQuery=RefundFinancialQuery(current.AcademyId,range.Currency,range.StartUtc,range.EndUtcExclusive,sportId,branchId,planId,provider,search,db);
        if (collectionType is "Product" or "Tournament") query = query.Where(x => false);
        if (collectionType is "Product" or "Tournament") refundQuery=refundQuery.Where(x=>false);
        if (collectionType is "Subscription" or "Tournament" || planId.HasValue) productQuery = productQuery.Where(x => false);
        if (collectionType is "Subscription" or "Product" || planId.HasValue) tournamentQuery = tournamentQuery.Where(x => false);
        var count = await query.CountAsync() + await productQuery.CountAsync() + await tournamentQuery.CountAsync()+await refundQuery.CountAsync();
        var total = (await query.SumAsync(x => (decimal?)x.Amount) ?? 0) + (await productQuery.SumAsync(x => (decimal?)x.Amount) ?? 0) + (await tournamentQuery.SumAsync(x => (decimal?)x.Amount) ?? 0);
        var refunds = await refundQuery.SumAsync(r => (decimal?)r.Amount) ?? 0;
        var size = Math.Clamp(pageSize ?? 50, 1, 100); var currentPage = Math.Clamp(page ?? 1, 1, 100000);
        var ids = await query.Select(x => new { x.Id, Date = x.ConfirmedAtUtc }).Concat(productQuery.Select(x => new { x.Id, Date = x.ConfirmedAtUtc!.Value })).Concat(tournamentQuery.Select(x => new { x.Id, Date = x.ConfirmedAtUtc!.Value })).Concat(refundQuery.Select(x=>new{x.Id,Date=x.CreatedAtUtc})).OrderByDescending(x => x.Date).ThenBy(x => x.Id).Skip((currentPage - 1) * size).Take(size).Select(x => x.Id).ToListAsync();
        var rows = await FinancialRows(query.Where(x => ids.Contains(x.Id)), current.AcademyId, db).ToListAsync();
        rows.AddRange(await ProductFinancialRows(productQuery.Where(x => ids.Contains(x.Id)), db).ToListAsync());
        rows.AddRange(await TournamentRows(tournamentQuery.Where(x => ids.Contains(x.Id)), db).ToListAsync());
        rows.AddRange(await RefundFinancialRows(refundQuery.Where(x=>ids.Contains(x.Id)),db).ToListAsync());
        return Results.Ok(new { from = range.From, to = range.To, totalAmount = total, totalRefunds = refunds, netAmount = total - refunds, totalCount = count, currency = range.Currency, page = currentPage, pageSize = size, items = rows.OrderByDescending(x => x.ConfirmedAtUtc).ThenBy(x => x.CollectionId) });
    }

    private static async Task<IResult> FinancialExport(DateOnly? from, DateOnly? to, bool? allDates, Guid? sportId, Guid? branchId, Guid? planId, string? provider, string? search, string? collectionType, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!;
        var range = await FinancialRange(current.AcademyId, from, to, db, clock, allDates == true);
        if (range.Error is not null) return range.Error;
        if (collectionType is not (null or "" or "Subscription" or "Product" or "Tournament")) return Validation("collectionType", "نوع التحصيل غير صالح.");
        var query = FinancialQuery(current.AcademyId, range.Currency, range.StartUtc, range.EndUtcExclusive, sportId, branchId, planId, provider, search, db);
        var products = ProductFinancialQuery(current.AcademyId, range.Currency, range.StartUtc, range.EndUtcExclusive, sportId, branchId, provider, search, db);
        var tournaments = TournamentQuery(current.AcademyId, range.Currency, range.StartUtc, range.EndUtcExclusive, sportId, branchId, provider, search, db);
        var refunds=RefundFinancialQuery(current.AcademyId,range.Currency,range.StartUtc,range.EndUtcExclusive,sportId,branchId,planId,provider,search,db);
        var count = (collectionType is "Product" or "Tournament" ? 0 : await query.CountAsync()+await refunds.CountAsync()) + (collectionType is "Subscription" or "Tournament" || planId.HasValue ? 0 : await products.CountAsync()) + (collectionType is "Subscription" or "Product" || planId.HasValue ? 0 : await tournaments.CountAsync());
        if (count > MaxExportRows) return Results.Problem(statusCode: 413, title: "نطاق التصدير كبير", detail: $"الحد الأقصى {MaxExportRows} سجل.");
        var rows = collectionType is "Product" or "Tournament" ? new List<FinancialRow>() : await FinancialRows(query.OrderBy(x => x.ConfirmedAtUtc), current.AcademyId, db).ToListAsync();
        if (collectionType is not ("Subscription" or "Tournament") && !planId.HasValue) rows.AddRange(await ProductFinancialRows(products, db).ToListAsync());
        if (collectionType is not ("Subscription" or "Product") && !planId.HasValue) rows.AddRange(await TournamentRows(tournaments, db).ToListAsync());
        if(collectionType is not ("Product" or "Tournament")) rows.AddRange(await RefundFinancialRows(refunds,db).ToListAsync());
        var csv = new List<string[]> { new[] { "رقم الإيصال", "اللاعب", "الرياضة", "الفرع", "المجموعة", "الباقة", "المبلغ", "العملة", "طريقة الدفع", "مزود الدفع", "مرجع المزود", "وقت التحصيل", "الاسترداد", "الصافي", "نوع التحصيل" } };
        csv.AddRange(rows.OrderBy(x=>x.ConfirmedAtUtc).ThenBy(x=>x.CollectionId).Select(x => new[] { x.ReceiptNumber, x.Player, x.Sport, x.Branch, x.Group, x.Plan, x.Amount.ToString("0.00", CultureInfo.InvariantCulture), x.Currency, x.PaymentMethod, x.Provider, x.ProviderReference, x.ConfirmedAtUtc.ToString("O"), x.RefundAmount.ToString("0.00", CultureInfo.InvariantCulture), (x.Amount - x.RefundAmount).ToString("0.00", CultureInfo.InvariantCulture), x.CollectionType == "Product" ? "منتج رياضي" : x.CollectionType == "Tournament" ? "رسم بطولة" : x.CollectionType=="Refund"?"استرداد اشتراك":"اشتراك" }));
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
        if (sportId.HasValue) query = query.Where(x => x.SportEnrollment.SportId == sportId);
        if (branchId.HasValue) query = query.Where(x => x.SportEnrollment.BranchId == branchId);
        if (planId.HasValue) query = query.Where(x => x.RenewalRequest.SubscriptionPlanId == planId);
        if (!string.IsNullOrWhiteSpace(provider)) query = query.Where(x => x.Provider == provider.Trim() || x.PaymentMethod == provider.Trim());
        if (!string.IsNullOrWhiteSpace(search)) { var value = search.Trim(); query = query.Where(x => x.SportEnrollment.Player.ArabicName.Contains(value) || x.SportEnrollment.Player.PlayerCode.Contains(value) || db.Receipts.Any(r => r.AcademyId == academyId && r.CollectionId == x.Id && r.ReceiptNumber.Contains(value))); }
        return query;
    }

    private static IQueryable<FinancialRow> FinancialRows(IQueryable<PaymentCollection> query, Guid academyId, FoundationDbContext db) => query.Select(x => new FinancialRow(
        x.Id,
        db.Receipts.Where(r => r.AcademyId == academyId && r.CollectionId == x.Id).Select(r => r.Id).Single(),
        db.Receipts.Where(r => r.AcademyId == academyId && r.CollectionId == x.Id).Select(r => r.ReceiptNumber).Single(),
        db.Receipts.Where(r => r.AcademyId == academyId && r.CollectionId == x.Id).Select(r => r.PlayerNameSnapshot).Single(),
        db.Receipts.Where(r => r.AcademyId == academyId && r.CollectionId == x.Id).Select(r => r.SportNameSnapshot).Single(),
        x.SportEnrollment.Branch.ArabicName,
        x.SportEnrollment.TrainingGroup == null ? "في انتظار المجموعة" : x.SportEnrollment.TrainingGroup.ArabicName,
        db.Receipts.Where(r => r.AcademyId == academyId && r.CollectionId == x.Id).Select(r => r.PlanNameSnapshot).Single(),
        x.Amount, x.Currency, x.PaymentMethod, x.Provider, x.ProviderReference, x.ConfirmedAtUtc,
        0, "Subscription"));

    // Refunds belong to their payout date, even when the original receipt lies
    // outside the period. Source queries still enforce tenant/resource/branch scope.
    private static IQueryable<CollectionRefund> RefundFinancialQuery(Guid academy,string currency,DateTimeOffset start,DateTimeOffset end,Guid? sport,Guid? branch,Guid? plan,string? method,string? search,FoundationDbContext db)
    {
        var sources=FinancialQuery(academy,currency,DateTimeOffset.MinValue,DateTimeOffset.MaxValue,sport,branch,plan,null,search,db);
        var q=db.CollectionRefunds.AsNoTracking().Where(r=>r.AcademyId==academy&&r.Currency==currency&&r.CreatedAtUtc>=start&&r.CreatedAtUtc<end&&sources.Any(c=>c.Id==r.CollectionId));
        if(!string.IsNullOrWhiteSpace(method)) {
            var value=method.Trim();q=q.Where(r=>(r.PayoutMethod ?? (db.CashRefundPostings.Any(p=>p.RefundId==r.Id)?"Cash":"Unspecified"))==value);
        }
        return q;
    }
    private static IQueryable<FinancialRow> RefundFinancialRows(IQueryable<CollectionRefund> q,FoundationDbContext db) =>
        from r in q join c in db.Collections on r.CollectionId equals c.Id join receipt in db.Receipts on r.CollectionId equals receipt.CollectionId
        select new FinancialRow(r.Id,receipt.Id,"استرداد — "+receipt.ReceiptNumber,receipt.PlayerNameSnapshot,receipt.SportNameSnapshot,c.SportEnrollment.Branch.ArabicName,
            c.SportEnrollment.TrainingGroup==null?"في انتظار المجموعة":c.SportEnrollment.TrainingGroup.ArabicName,receipt.PlanNameSnapshot,0,r.Currency,
            r.PayoutMethod ?? (db.CashRefundPostings.Any(p=>p.RefundId==r.Id)?"Cash":"Unspecified"),"Manual",r.Id.ToString(),r.CreatedAtUtc,r.Amount,"Refund");

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
