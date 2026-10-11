using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class DailyStaffAttendance
{
    public static void MapDailyStaffAttendance(this WebApplication app)
    {
        var manage = app.MapGroup("/api/v1/manage/staff-attendance");
        manage.MapGet("/", Day).RequireAuthorization(AcademyPermissions.AttendanceManage);
        manage.MapGet("/{id:guid}", Detail).RequireAuthorization(AcademyPermissions.AttendanceManage);
        manage.MapPut("/{id:guid}", Record).RequireAuthorization(AcademyPermissions.AttendanceManage).AddEndpointFilter<CsrfFilter>();
        manage.MapGet("/{id:guid}/schedule", Schedule).RequireAuthorization(AcademyPermissions.StructureManage);
        manage.MapPut("/{id:guid}/schedule", SaveSchedule).RequireAuthorization(AcademyPermissions.StructureManage).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/reports/staff-daily", Report).RequireAuthorization(AcademyPermissions.ReportsAttendanceRead);
        app.MapGet("/api/v1/reports/staff-daily/export", Export).RequireAuthorization(AcademyPermissions.ReportsAttendanceRead).RequireAuthorization(AcademyPermissions.ReportsExport);
    }
    private static async Task<IResult> Day(DateOnly? date, bool? allDates, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin)) return Results.Forbid(); var day = date ?? await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        var all = allDates == true && date is null;
        var rows = await db.StaffDailyAttendances.Where(x => x.AcademyId == t.AcademyId && (all || x.WorkDate == day)).OrderBy(x => x.WorkDate).ToListAsync();
        var coaches = await db.AcademyMemberships.Where(x => x.AcademyId == t.AcademyId && x.Role == AcademyRole.Coach && x.IsActive).OrderBy(x => x.User.DisplayName).Select(x => new { x.Id, name = x.User.DisplayName }).ToListAsync();
        return Results.Ok(new { date = all ? (DateOnly?)null : day, items = coaches.SelectMany(c => {
            var records = rows.Where(x => x.MembershipId == c.Id).Cast<StaffDailyAttendance?>().ToList();
            if (records.Count == 0) records.Add(null);
            return records.Select(r => new { c.Id, c.name, workDate = r?.WorkDate ?? day, status = r?.Status ?? "NotRecorded", r?.CheckIn, r?.CheckOut, lateMinutes = r?.LateMinutes ?? 0, suggestedDeduction = r?.SuggestedDeduction ?? 0, approvedDeduction = r?.ApprovedDeduction ?? 0, r?.DeductionReason });
        }).OrderBy(x => x.workDate).ThenBy(x => x.CheckIn).ThenBy(x => x.name) });
    }
    private static async Task<IResult> Detail(Guid id, DateOnly? date, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin)) return Results.Forbid();
        var coach = await db.AcademyMemberships.Where(x => x.AcademyId == t.AcademyId && x.Id == id && x.Role == AcademyRole.Coach && x.IsActive).Select(x => new { x.Id, name = x.User.DisplayName }).SingleOrDefaultAsync();
        if (coach is null) return Results.NotFound();
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock); var day = date ?? today;
        var r = await db.StaffDailyAttendances.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.MembershipId == id && x.WorkDate == day);
        return Results.Ok(new { coach.Id, coach.name, workDate = day, today, status = r?.Status ?? "NotRecorded", r?.CheckIn, r?.CheckOut, lateMinutes = r?.LateMinutes ?? 0, suggestedDeduction = r?.SuggestedDeduction ?? 0, approvedDeduction = r?.ApprovedDeduction ?? 0, r?.DeductionReason });
    }
    private static async Task<IResult> Record(Guid id, DailyWrite body, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin)) return Results.Forbid();
        if (body.Date.Year < 2000 || body.Date > await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock)) return Results.UnprocessableEntity(new { message = "أدخل تاريخ حضور صحيحًا غير مستقبلي." });
        if (body.Status is not ("Present" or "Absent" or "NotRecorded") || body.Status == "Present" && body.CheckIn is null || body.CheckOut < body.CheckIn || body.ApprovedDeduction < 0 || decimal.Round(body.ApprovedDeduction, 2) != body.ApprovedDeduction || body.ApprovedDeduction > 0 && string.IsNullOrWhiteSpace(body.Reason) || body.Reason?.Length > 500) return Results.UnprocessableEntity(new { message = "أدخل حالة وتوقيتًا صحيحين. سبب الخصم المعتمد مطلوب." });
        if (!await db.AcademyMemberships.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.IsActive && x.Role == AcademyRole.Coach)) return Results.NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"staff-day:{t.AcademyId}:{id}:{body.Date}"}, 0))");
        var row = await db.StaffDailyAttendances.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.MembershipId == id && x.WorkDate == body.Date);
        var employee = await db.Employees.Where(x => x.AcademyId == t.AcademyId && x.MembershipId == id).Select(x => (Guid?)x.Id).SingleOrDefaultAsync();
        if (employee.HasValue)
        {
            await PayrollEndpoints.Lock(db, t.AcademyId, employee.Value, body.Date);
            if (body.ApprovedDeduction != (row?.ApprovedDeduction ?? 0) && await PayrollEndpoints.Locked(db, t.AcademyId, employee.Value, body.Date)) return Results.Conflict(new { message = "راتب هذا الشهر سُدد؛ لا يمكن تعديل خصمه." });
        }
        if (row is null) { row = new StaffDailyAttendance { Id = Guid.NewGuid(), AcademyId = t.AcademyId, MembershipId = id, WorkDate = body.Date, Status = body.Status, CreatedAtUtc = clock.GetUtcNow() }; db.StaffDailyAttendances.Add(row); }
        var schedule = await db.StaffWorkSchedules.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.MembershipId == id && x.DayOfWeek == body.Date.DayOfWeek && x.IsActive);
        row.Status = body.Status; row.CheckIn = body.Status == "Present" ? body.CheckIn : null; row.CheckOut = body.Status == "Present" ? body.CheckOut : null;
        row.LateMinutes = row.CheckIn.HasValue && schedule is not null ? Math.Max(0, (int)(row.CheckIn.Value.ToTimeSpan() - schedule.StartTime.ToTimeSpan()).TotalMinutes) : 0;
        row.SuggestedDeduction = schedule is not null && row.LateMinutes > 0 && row.LateMinutes >= schedule.LateThresholdMinutes ? schedule.LateDeduction : 0;
        row.ApprovedDeduction = body.ApprovedDeduction; row.DeductionReason = body.Reason?.Trim(); row.RecordedByUserId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!); row.UpdatedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { row.Status, row.LateMinutes, row.SuggestedDeduction, row.ApprovedDeduction });
    }
    private static async Task<IResult> Schedule(Guid id, CurrentTenant tenant, FoundationDbContext db)
    { var t = (await tenant.ResolveAsync())!; if (!await db.AcademyMemberships.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.Role == AcademyRole.Coach)) return Results.NotFound(); return Results.Ok(await db.StaffWorkSchedules.Where(x => x.AcademyId == t.AcademyId && x.MembershipId == id && x.IsActive).Select(x => new { x.DayOfWeek, x.StartTime, x.EndTime, x.LateThresholdMinutes, x.LateDeduction }).ToListAsync()); }
    private static async Task<IResult> SaveSchedule(Guid id, WorkScheduleWrite body, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!;
        if (!await db.AcademyMemberships.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.Role == AcademyRole.Coach && x.IsActive)) return Results.NotFound();
        if (body.Days is null || body.Days.Length is < 1 or > 7 || body.Days.Distinct().Count() != body.Days.Length || body.Days.Any(x => !Enum.IsDefined(x)) || body.Start >= body.End || body.ThresholdMinutes is < 0 or > 480 || body.Deduction < 0 || decimal.Round(body.Deduction, 2) != body.Deduction) return Results.UnprocessableEntity(new { message = "اختر أيامًا وأوقاتًا وقاعدة تأخير صحيحة." });
        await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"staff-schedule:{t.AcademyId}:{id}"}, 0))");
        var rows = await db.StaffWorkSchedules.Where(x => x.AcademyId == t.AcademyId && x.MembershipId == id).ToListAsync(); foreach (var r in rows) { r.IsActive = false; r.UpdatedAtUtc = clock.UtcNow; }
        foreach (var d in body.Days) { var row = rows.SingleOrDefault(x => x.DayOfWeek == d); if (row is null) { row = new StaffWorkSchedule { Id = Guid.NewGuid(), AcademyId = t.AcademyId, MembershipId = id, DayOfWeek = d, CreatedAtUtc = clock.UtcNow }; db.StaffWorkSchedules.Add(row); } row.IsActive = true; row.StartTime = body.Start; row.EndTime = body.End; row.LateThresholdMinutes = body.ThresholdMinutes; row.LateDeduction = body.Deduction; row.UpdatedAtUtc = clock.UtcNow; }
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
    }
    private static async Task<IResult> Report(DateOnly? from, DateOnly? to, bool? allDates, Guid? coachId, string? status, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin)) return Results.Forbid(); var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock); var all = allDates == true && from is null && to is null;
        var start = all ? DateOnly.MinValue : from ?? new DateOnly(today.Year, today.Month, 1); var end = all ? DateOnly.MaxValue : to ?? today;
        if ((!all && (start > end || end.DayNumber - start.DayNumber > 366)) || status is not (null or "" or "Present" or "Absent" or "NotRecorded")) return Results.UnprocessableEntity();
        var q = db.StaffDailyAttendances.Where(x => x.AcademyId == t.AcademyId && x.WorkDate >= start && x.WorkDate <= end && (!coachId.HasValue || x.MembershipId == coachId) && (string.IsNullOrEmpty(status) || x.Status == status));
        var total = await q.SumAsync(x => (decimal?)x.ApprovedDeduction) ?? 0;
        var currency = await db.Academies.Where(x => x.Id == t.AcademyId).Select(x => x.DefaultCurrency).SingleAsync();
        return Results.Ok(new { from = start, to = end, totalDeductions = total, totalCount = await q.CountAsync(), currency, items = await q.OrderByDescending(x => x.WorkDate).Take(1000).Select(x => new { x.Id, x.MembershipId, name = db.AcademyMemberships.Where(m => m.AcademyId == t.AcademyId && m.Id == x.MembershipId).Select(m => m.User.DisplayName).FirstOrDefault(), x.WorkDate, x.Status, x.CheckIn, x.CheckOut, x.LateMinutes, x.ApprovedDeduction, x.DeductionReason }).ToListAsync() });
    }
    private static async Task<IResult> Export(DateOnly? from, DateOnly? to, bool? allDates, Guid? coachId, string? status, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin)) return Results.Forbid();
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock); var all = allDates == true && from is null && to is null;
        var start = all ? DateOnly.MinValue : from ?? new DateOnly(today.Year, today.Month, 1); var end = all ? DateOnly.MaxValue : to ?? today;
        if ((!all && (start > end || end.DayNumber - start.DayNumber > 366)) || status is not (null or "" or "Present" or "Absent" or "NotRecorded")) return Results.UnprocessableEntity();
        var q = db.StaffDailyAttendances.Where(x => x.AcademyId == t.AcademyId && x.WorkDate >= start && x.WorkDate <= end && (!coachId.HasValue || x.MembershipId == coachId) && (string.IsNullOrEmpty(status) || x.Status == status));
        if (await q.CountAsync() > 5000) return Results.Problem(statusCode: 413, title: "نطاق التصدير كبير", detail: "اختر فترة أصغر؛ الحد 5000 سجل.");
        var rows = await q.OrderBy(x => x.WorkDate).Select(x => new { name = db.AcademyMemberships.Where(m => m.AcademyId == t.AcademyId && m.Id == x.MembershipId).Select(m => m.User.DisplayName).FirstOrDefault(), x.WorkDate, x.Status, x.CheckIn, x.CheckOut, x.LateMinutes, x.ApprovedDeduction, x.DeductionReason }).ToListAsync();
        var csv = new List<string[]> { new[] { "المدرب", "التاريخ", "الحالة", "الحضور", "الانصراف", "دقائق التأخير", "الخصم المعتمد", "سبب الخصم" } };
        csv.AddRange(rows.Select(x => new[] { x.name ?? "", x.WorkDate.ToString("yyyy-MM-dd"), x.Status == "Present" ? "حاضر" : x.Status == "Absent" ? "غائب" : "لم يُسجل", x.CheckIn?.ToString("HH:mm") ?? "", x.CheckOut?.ToString("HH:mm") ?? "", x.LateMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture), x.ApprovedDeduction.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), x.DeductionReason ?? "" }));
        return Academy.Api.Slice8.ReportingEndpoints.Csv(csv, $"coach-daily-{start:yyyy-MM}.csv");
    }
}
public sealed record DailyWrite(DateOnly Date, string Status, TimeOnly? CheckIn, TimeOnly? CheckOut, decimal ApprovedDeduction, string? Reason);
public sealed record WorkScheduleWrite(DayOfWeek[] Days, TimeOnly Start, TimeOnly End, int ThresholdMinutes, decimal Deduction);
