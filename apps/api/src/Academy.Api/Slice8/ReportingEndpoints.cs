using System.Globalization;
using System.Security.Claims;
using System.Text;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;

public static class ReportingEndpoints
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

    private static async Task<IResult> OwnerSummary(CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!;
        var academy = await db.Academies.AsNoTracking().Where(x => x.Id == current.AcademyId).Select(x => new { x.TimeZone, x.DefaultCurrency }).SingleAsync();
        var today = clock.Today;
        var todayRange = UtcRange(today, today, academy.TimeZone);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthRange = UtcRange(monthStart, today, academy.TimeZone);
        var collections = db.Collections.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.Currency == academy.DefaultCurrency);
        var total = await collections.SumAsync(x => (decimal?)x.Amount) ?? 0;
        var todayTotal = await collections.Where(x => x.ConfirmedAtUtc >= todayRange.Start && x.ConfirmedAtUtc < todayRange.EndExclusive).SumAsync(x => (decimal?)x.Amount) ?? 0;
        var monthTotal = await collections.Where(x => x.ConfirmedAtUtc >= monthRange.Start && x.ConfirmedAtUtc < monthRange.EndExclusive).SumAsync(x => (decimal?)x.Amount) ?? 0;
        var payments = db.PaymentRequests.AsNoTracking().Where(x => x.AcademyId == current.AcademyId);
        var periods = db.SubscriptionPeriods.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.Status != SubscriptionPeriodStatus.Cancelled);
        var expiringEnd = today.AddDays(7);
        var todayAttendance = db.PlayerAttendances.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.TrainingSession.SessionDate == today && x.TrainingSession.Status != TrainingSessionStatus.Cancelled);

        var bySport = await collections.GroupBy(x => x.SportEnrollment.Sport.ArabicName).Select(x => new { name = x.Key, amount = x.Sum(y => y.Amount), count = x.Count() }).OrderByDescending(x => x.amount).ToListAsync();
        var byBranch = await collections.GroupBy(x => x.SportEnrollment.Branch.ArabicName).Select(x => new { name = x.Key, amount = x.Sum(y => y.Amount), count = x.Count() }).OrderByDescending(x => x.amount).ToListAsync();
        var latest = await collections.OrderByDescending(x => x.ConfirmedAtUtc).Take(5).Select(x => new LatestCollection(
            db.Receipts.Where(r => r.AcademyId == current.AcademyId && r.CollectionId == x.Id).Select(r => r.Id).Single(),
            db.Receipts.Where(r => r.AcademyId == current.AcademyId && r.CollectionId == x.Id).Select(r => r.ReceiptNumber).Single(),
            db.Receipts.Where(r => r.AcademyId == current.AcademyId && r.CollectionId == x.Id).Select(r => r.PlayerNameSnapshot).Single(),
            x.Amount, x.Currency, x.ConfirmedAtUtc)).ToListAsync();

        return Results.Ok(new
        {
            asOfDate = today, currency = academy.DefaultCurrency,
            collections = new { total, today = todayTotal, month = monthTotal },
            payments = new
            {
                confirmed = await payments.CountAsync(x => x.Status == PaymentRequestStatus.Confirmed),
                failed = await payments.CountAsync(x => x.Status == PaymentRequestStatus.Failed),
                pending = await payments.CountAsync(x => x.Status == PaymentRequestStatus.Pending || x.Status == PaymentRequestStatus.Created),
                cancelled = await payments.CountAsync(x => x.Status == PaymentRequestStatus.Cancelled),
                expired = await payments.CountAsync(x => x.Status == PaymentRequestStatus.Expired)
            },
            subscriptions = new
            {
                active = await periods.CountAsync(x => x.StartDate <= today && (x.EndDate == null || x.EndDate >= today) && x.Status != SubscriptionPeriodStatus.Frozen),
                expiring = await periods.CountAsync(x => x.EndDate >= today && x.EndDate <= expiringEnd && x.Status != SubscriptionPeriodStatus.Frozen),
                expired = await periods.CountAsync(x => x.EndDate < today)
            },
            activePlayers = await db.Players.CountAsync(x => x.AcademyId == current.AcademyId && x.IsActive),
            attendance = new
            {
                sessions = await db.TrainingSessions.CountAsync(x => x.AcademyId == current.AcademyId && x.SessionDate == today && x.Status != TrainingSessionStatus.Cancelled),
                present = await todayAttendance.CountAsync(x => x.Status == AttendanceStatus.Present),
                absent = await todayAttendance.CountAsync(x => x.Status == AttendanceStatus.Absent),
                notRecorded = await todayAttendance.CountAsync(x => x.Status == AttendanceStatus.NotRecorded)
            },
            bySport, byBranch, latest
        });
    }

    private static async Task<IResult> FinancialReport(DateOnly? from, DateOnly? to, Guid? sportId, Guid? branchId, Guid? planId, string? provider, string? search, int? page, int? pageSize, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!;
        var range = await FinancialRange(current.AcademyId, from, to, db, clock);
        if (range.Error is not null) return range.Error;
        var query = FinancialQuery(current.AcademyId, range.Currency, range.StartUtc, range.EndUtcExclusive, sportId, branchId, planId, provider, search, db);
        var count = await query.CountAsync();
        var total = await query.SumAsync(x => (decimal?)x.Amount) ?? 0;
        var size = Math.Clamp(pageSize ?? 50, 1, 100);
        var currentPage = Math.Max(page ?? 1, 1);
        var rows = await FinancialRows(query.OrderByDescending(x => x.ConfirmedAtUtc).Skip((currentPage - 1) * size).Take(size), current.AcademyId, db).ToListAsync();
        return Results.Ok(new { from = range.From, to = range.To, totalAmount = total, totalCount = count, currency = range.Currency, page = currentPage, pageSize = size, items = rows });
    }

    private static async Task<IResult> FinancialExport(DateOnly? from, DateOnly? to, Guid? sportId, Guid? branchId, Guid? planId, string? provider, string? search, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!;
        var range = await FinancialRange(current.AcademyId, from, to, db, clock);
        if (range.Error is not null) return range.Error;
        var query = FinancialQuery(current.AcademyId, range.Currency, range.StartUtc, range.EndUtcExclusive, sportId, branchId, planId, provider, search, db).OrderBy(x => x.ConfirmedAtUtc);
        if (await query.CountAsync() > MaxExportRows) return Results.Problem(statusCode: 413, title: "نطاق التصدير كبير", detail: $"الحد الأقصى {MaxExportRows} سجل.");
        var rows = await FinancialRows(query, current.AcademyId, db).ToListAsync();
        var csv = new List<string[]> { new[] { "رقم الإيصال", "اللاعب", "الرياضة", "الفرع", "المجموعة", "الباقة", "المبلغ", "العملة", "طريقة الدفع", "مزود الدفع", "مرجع المزود", "وقت التحصيل" } };
        csv.AddRange(rows.Select(x => new[] { x.ReceiptNumber, x.Player, x.Sport, x.Branch, x.Group, x.Plan, x.Amount.ToString("0.00", CultureInfo.InvariantCulture), x.Currency, x.PaymentMethod, x.Provider, x.ProviderReference, x.ConfirmedAtUtc.ToString("O") }));
        return Csv(csv, $"financial-collections-{range.From:yyyy-MM}.csv");
    }

    private static async Task<IResult> AttendanceReport(string? subject, int? month, int? year, DateOnly? from, DateOnly? to, Guid? sportId, Guid? branchId, Guid? groupId, string? status, string? search, int? birthYear, int? page, int? pageSize, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!;
        var range = AttendanceRange(month, year, from, to, clock.Today);
        if (range.Error is not null) return range.Error;
        var staff = string.Equals(subject, "staff", StringComparison.OrdinalIgnoreCase);
        if (staff && birthYear.HasValue) return Validation("birthYear", "فلتر سنة الميلاد متاح للاعبين فقط.");
        var size = Math.Clamp(pageSize ?? 50, 1, 100); var currentPage = Math.Max(page ?? 1, 1); var userId = UserId(principal);
        if (staff)
        {
            var query = StaffAttendanceQuery(current, userId, range.From, range.To, sportId, branchId, groupId, status, search, db);
            var count = await query.CountAsync(); var rows = await StaffAttendanceRows(query.OrderByDescending(x => x.TrainingSession.SessionDate).ThenBy(x => x.AcademyMembership.User.DisplayName).Skip((currentPage - 1) * size).Take(size)).ToListAsync();
            return Results.Ok(new { subject = "staff", from = range.From, to = range.To, coverage = "StoredRecordsOnly", totalCount = count, page = currentPage, pageSize = size, items = rows });
        }
        var players = PlayerAttendanceQuery(current, userId, range.From, range.To, sportId, branchId, groupId, status, search, birthYear, db);
        var playerCount = await players.CountAsync(); var playerRows = await PlayerAttendanceRows(players.OrderByDescending(x => x.TrainingSession.SessionDate).ThenBy(x => x.SportEnrollment.Player.ArabicName).Skip((currentPage - 1) * size).Take(size)).ToListAsync();
        return Results.Ok(new { subject = "players", from = range.From, to = range.To, coverage = "StoredRecordsOnly", totalCount = playerCount, page = currentPage, pageSize = size, items = playerRows });
    }

    private static async Task<IResult> AttendanceExport(string? subject, int? month, int? year, DateOnly? from, DateOnly? to, Guid? sportId, Guid? branchId, Guid? groupId, string? status, string? search, int? birthYear, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!; var range = AttendanceRange(month, year, from, to, clock.Today); if (range.Error is not null) return range.Error;
        var staff = string.Equals(subject, "staff", StringComparison.OrdinalIgnoreCase); var userId = UserId(principal);
        if (staff && birthYear.HasValue) return Validation("birthYear", "فلتر سنة الميلاد متاح للاعبين فقط.");
        if (staff)
        {
            var query = StaffAttendanceQuery(current, userId, range.From, range.To, sportId, branchId, groupId, status, search, db).OrderBy(x => x.TrainingSession.SessionDate);
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
        x.SportEnrollment.TrainingGroup.ArabicName,
        db.Receipts.Where(r => r.AcademyId == academyId && r.CollectionId == x.Id).Select(r => r.PlanNameSnapshot).Single(),
        x.Amount, x.Currency, x.PaymentMethod, x.Provider, x.ProviderReference, x.ConfirmedAtUtc));

    private static IQueryable<PlayerAttendance> PlayerAttendanceQuery(TenantMembership current, Guid userId, DateOnly from, DateOnly to, Guid? sportId, Guid? branchId, Guid? groupId, string? status, string? search, int? birthYear, FoundationDbContext db)
    {
        var query = db.PlayerAttendances.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.TrainingSession.SessionDate >= from && x.TrainingSession.SessionDate <= to && x.TrainingSession.Status != TrainingSessionStatus.Cancelled);
        if (current.Role == AcademyRole.Coach) query = query.Where(x => db.StaffGroupAssignments.Any(a => a.AcademyId == current.AcademyId && a.TrainingGroupId == x.TrainingGroupId && a.AcademyMembership.UserId == userId && a.IsActive));
        if (sportId.HasValue) query = query.Where(x => x.TrainingSession.SportId == sportId); if (branchId.HasValue) query = query.Where(x => x.TrainingSession.BranchId == branchId); if (groupId.HasValue) query = query.Where(x => x.TrainingGroupId == groupId);
        if (Enum.TryParse<AttendanceStatus>(status, true, out var parsed)) query = query.Where(x => x.Status == parsed);
        if (!string.IsNullOrWhiteSpace(search)) { var value = search.Trim(); query = query.Where(x => x.SportEnrollment.Player.ArabicName.Contains(value) || x.SportEnrollment.Player.PlayerCode.Contains(value)); }
        if (birthYear.HasValue) { var start = new DateOnly(birthYear.Value, 1, 1); var end = start.AddYears(1); query = query.Where(x => x.SportEnrollment.Player.DateOfBirth >= start && x.SportEnrollment.Player.DateOfBirth < end); }
        return query;
    }

    private static IQueryable<PlayerAttendanceRow> PlayerAttendanceRows(IQueryable<PlayerAttendance> query) => query.Select(x => new PlayerAttendanceRow(x.Id, x.TrainingSession.SessionDate, x.TrainingSession.StartTime, x.SportEnrollment.Player.PlayerCode, x.SportEnrollment.Player.ArabicName, x.SportEnrollment.Player.DateOfBirth.Year, x.TrainingSession.Sport.ArabicName, x.TrainingSession.Branch.ArabicName, x.TrainingSession.TrainingGroup.ArabicName, x.Status.ToString(), x.ConsumedSubscriptionPeriodId != null));

    private static IQueryable<StaffAttendance> StaffAttendanceQuery(TenantMembership current, Guid userId, DateOnly from, DateOnly to, Guid? sportId, Guid? branchId, Guid? groupId, string? status, string? search, FoundationDbContext db)
    {
        var query = db.StaffAttendances.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && x.TrainingSession.SessionDate >= from && x.TrainingSession.SessionDate <= to && x.TrainingSession.Status != TrainingSessionStatus.Cancelled);
        if (current.Role == AcademyRole.Coach) query = query.Where(x => x.AcademyMembership.UserId == userId && db.StaffGroupAssignments.Any(a => a.AcademyId == current.AcademyId && a.TrainingGroupId == x.TrainingGroupId && a.AcademyMembershipId == x.AcademyMembershipId && a.IsActive));
        if (sportId.HasValue) query = query.Where(x => x.TrainingSession.SportId == sportId); if (branchId.HasValue) query = query.Where(x => x.TrainingSession.BranchId == branchId); if (groupId.HasValue) query = query.Where(x => x.TrainingGroupId == groupId);
        if (Enum.TryParse<AttendanceStatus>(status, true, out var parsed)) query = query.Where(x => x.Status == parsed);
        if (!string.IsNullOrWhiteSpace(search)) { var value = search.Trim(); query = query.Where(x => x.AcademyMembership.User.DisplayName.Contains(value)); }
        return query;
    }

    private static IQueryable<StaffAttendanceRow> StaffAttendanceRows(IQueryable<StaffAttendance> query) => query.Select(x => new StaffAttendanceRow(x.Id, x.TrainingSession.SessionDate, x.TrainingSession.StartTime, x.AcademyMembership.User.DisplayName, x.AcademyMembership.Role.ToString(), x.TrainingSession.Sport.ArabicName, x.TrainingSession.Branch.ArabicName, x.TrainingSession.TrainingGroup.ArabicName, x.Status.ToString()));

    private static async Task<FinancialRangeResult> FinancialRange(Guid academyId, DateOnly? from, DateOnly? to, FoundationDbContext db, ISubscriptionClock clock)
    {
        var academy = await db.Academies.AsNoTracking().Where(x => x.Id == academyId).Select(x => new { x.TimeZone, x.DefaultCurrency }).SingleAsync();
        var end = to ?? clock.Today; var start = from ?? new DateOnly(end.Year, end.Month, 1);
        if (end < start || end.DayNumber - start.DayNumber > MaxInteractiveDays) return new(start, end, default, default, academy.DefaultCurrency, Validation("range", $"نطاق التاريخ يجب ألا يتجاوز {MaxInteractiveDays} يومًا."));
        var range = UtcRange(start, end, academy.TimeZone); return new(start, end, range.Start, range.EndExclusive, academy.DefaultCurrency, null);
    }

    private static AttendanceRangeResult AttendanceRange(int? month, int? year, DateOnly? from, DateOnly? to, DateOnly today)
    {
        DateOnly start; DateOnly end;
        try
        {
            if (from.HasValue || to.HasValue) { start = from ?? to!.Value; end = to ?? from!.Value; }
            else { var selectedYear = year ?? today.Year; var selectedMonth = month ?? today.Month; start = new DateOnly(selectedYear, selectedMonth, 1); end = start.AddMonths(1).AddDays(-1); }
        }
        catch (ArgumentOutOfRangeException) { return new(default, default, Validation("date", "الشهر أو السنة غير صالحين.")); }
        if (end < start || end.DayNumber - start.DayNumber > MaxInteractiveDays) return new(start, end, Validation("range", $"نطاق التاريخ يجب ألا يتجاوز {MaxInteractiveDays} يومًا."));
        return new(start, end, null);
    }

    private static (DateTimeOffset Start, DateTimeOffset EndExclusive) UtcRange(DateOnly from, DateOnly to, string timeZoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var localStart = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var localEnd = DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return (new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, zone)), new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, zone)));
    }

    private static IResult Csv(IEnumerable<string[]> rows, string filename)
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

    private static string ArabicStatus(string value) => value switch { "Present" => "حاضر", "Absent" => "غائب", "NotRecorded" => "لم يُسجل", _ => value };
    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static IResult Validation(string key, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
}

public sealed record ReportBreakdown(string Name, decimal Amount, int Count);
public sealed record LatestCollection(Guid ReceiptId, string ReceiptNumber, string Player, decimal Amount, string Currency, DateTimeOffset ConfirmedAtUtc);
public sealed record FinancialRow(Guid CollectionId, Guid ReceiptId, string ReceiptNumber, string Player, string Sport, string Branch, string Group, string Plan, decimal Amount, string Currency, string PaymentMethod, string Provider, string ProviderReference, DateTimeOffset ConfirmedAtUtc);
public sealed record PlayerAttendanceRow(Guid Id, DateOnly Date, TimeOnly Time, string PlayerCode, string Player, int BirthYear, string Sport, string Branch, string Group, string Status, bool SessionConsumed);
public sealed record StaffAttendanceRow(Guid Id, DateOnly Date, TimeOnly Time, string Staff, string Role, string Sport, string Branch, string Group, string Status);
internal sealed record FinancialRangeResult(DateOnly From, DateOnly To, DateTimeOffset StartUtc, DateTimeOffset EndUtcExclusive, string Currency, IResult? Error);
internal sealed record AttendanceRangeResult(DateOnly From, DateOnly To, IResult? Error);
