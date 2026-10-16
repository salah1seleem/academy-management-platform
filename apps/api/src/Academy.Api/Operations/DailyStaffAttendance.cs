using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Npgsql;

namespace Academy.Api.Operations;

public static class DailyStaffAttendance
{
    public static void MapDailyStaffAttendance(this WebApplication app)
    {
        app.MapStaffWorkPeriods();
        var manage = app.MapGroup("/api/v1/manage/staff-attendance");
        manage.MapGet("/", Day).RequireAuthorization(AcademyPermissions.AttendanceManage);
        manage.MapGet("/{id:guid}", Detail).RequireAuthorization(AcademyPermissions.AttendanceManage);
        manage.MapGet("/{id:guid}/history", History).RequireAuthorization(AcademyPermissions.AttendanceManage);
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
            return records.Select(r => new { c.Id, c.name, workDate = r?.WorkDate ?? day, status = r?.Status ?? "NotRecorded", r?.CheckIn, r?.CheckOut, r?.CheckOutDate, lateMinutes = r?.LateMinutes ?? 0, suggestedDeduction = r?.SuggestedDeduction ?? 0, approvedDeduction = r?.ApprovedDeduction ?? 0, r?.DeductionReason });
        }).OrderBy(x => x.workDate).ThenBy(x => x.CheckIn).ThenBy(x => x.name) });
    }
    private static async Task<IResult> Detail(Guid id, DateOnly? date, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin)) return Results.Forbid();
        var coach = await db.AcademyMemberships.Where(x => x.AcademyId == t.AcademyId && x.Id == id && x.Role == AcademyRole.Coach && x.IsActive).Select(x => new { x.Id, name = x.User.DisplayName }).SingleOrDefaultAsync();
        if (coach is null) return Results.NotFound();
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock); var day = date ?? today;
        var r = await db.StaffDailyAttendances.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.MembershipId == id && x.WorkDate == day);
        var periodCount=r is null?0:await db.StaffWorkPeriods.CountAsync(p=>p.AttendanceId==r.Id);
        return Results.Ok(new { coach.Id, coach.name, workDate = day, today, periodCount, version = r?.Version ?? 0, status = r?.Status ?? "NotRecorded", r?.CheckIn, r?.CheckOut, r?.CheckOutDate, lateMinutes = r?.LateMinutes ?? 0, suggestedDeduction = r?.SuggestedDeduction ?? 0, approvedDeduction = r?.ApprovedDeduction ?? 0, r?.DeductionReason });
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Snapshot(StaffDailyAttendance? row) => JsonSerializer.Serialize(row is null ? null : new { row.Id, row.Version, row.WorkDate, row.Status, row.CheckIn, row.CheckOut, row.CheckOutDate, row.LateMinutes, row.SuggestedDeduction, row.ApprovedDeduction, row.DeductionReason, row.RecordedByUserId, row.UpdatedAtUtc }, Json);
    private static async Task<IResult> History(Guid id, DateOnly date, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        if (t.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin)) return Results.Forbid();
        if (!await db.AcademyMemberships.AnyAsync(m => m.Id == id && m.AcademyId == t.AcademyId && m.Role == AcademyRole.Coach && m.IsActive)) return Results.NotFound();
        var q = db.StaffAttendanceChanges.AsNoTracking().Where(c => c.AcademyId == t.AcademyId && c.MembershipId == id && c.WorkDate == date);
        var rows = await q.OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.AfterVersion).Take(100).Select(c => new { c.Id, c.CreatedAtUtc, c.BeforeVersion, c.AfterVersion, c.BeforeSnapshot, c.AfterSnapshot, actor = db.Users.Where(u => u.Id == c.ActorUserId).Select(u => u.DisplayName).FirstOrDefault() }).ToArrayAsync();
        var zone = await db.Academies.Where(a => a.Id == t.AcademyId).Select(a => a.TimeZone).SingleAsync();
        return Results.Ok(new { total = await q.CountAsync(), timeZone = zone, items = rows.Select(c => new { c.Id, c.CreatedAtUtc, c.BeforeVersion, c.AfterVersion, c.actor, before = JsonSerializer.Deserialize<JsonElement>(c.BeforeSnapshot), after = JsonSerializer.Deserialize<JsonElement>(c.AfterSnapshot) }) });
    }
    private static async Task<IResult> Record(Guid id, DailyWrite body, CurrentTenant tenant, HttpContext http, FoundationDbContext db, TimeProvider clock, IAuthorizationService authorization)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin)) return Results.Forbid();
        var actor = Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var key = http.Request.Headers["Idempotency-Key"].ToString();
        if (key.Length != 0 && key.Length is < 8 or > 100) return Results.UnprocessableEntity(new { message = "مفتاح الحفظ غير صالح؛ افتح السجل مجددًا." });
        // Legacy create callers remain safe: no key means no replay guarantee, never a blind update.
        if (key.Length == 0) key = Guid.NewGuid().ToString();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id, membership = t.MembershipId, body }, Json))));
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        var checkoutDate = body.Status == "Present" && body.CheckOut.HasValue ? body.CheckOutDate ?? body.Date : (DateOnly?)null;
        if (body.Date.Year < 2000 || body.Date > today || checkoutDate < body.Date || checkoutDate > today) return Results.UnprocessableEntity(new { message = "أدخل تاريخ حضور وانصراف صحيحين غير مستقبليين؛ الانصراف لا يسبق يوم الحضور." });
        var zone = TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a => a.Id == t.AcademyId).Select(a => a.TimeZone).SingleAsync());
        var localNow = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        if (body.Status == "Present" && (body.Date == today && body.CheckIn > localNow || checkoutDate == today && body.CheckOut > localNow)) return Results.UnprocessableEntity(new { message = "لا يمكن تسجيل حضور أو انصراف في وقت مستقبلي بتوقيت الأكاديمية." });
        if (body.Status is not ("Present" or "Absent" or "NotRecorded") || body.Status == "Present" && body.CheckIn is null || checkoutDate == body.Date && body.CheckOut < body.CheckIn || body.ApprovedDeduction < 0 || decimal.Round(body.ApprovedDeduction, 2) != body.ApprovedDeduction || body.ApprovedDeduction > 0 && string.IsNullOrWhiteSpace(body.Reason) || body.Reason?.Length > 500) return Results.UnprocessableEntity(new { message = "أدخل حالة وتوقيتًا صحيحين. سبب الخصم المعتمد مطلوب." });
        if (!await db.AcademyMemberships.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.IsActive && x.Role == AcademyRole.Coach)) return Results.NotFound();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"}, 0))");
        var live = await db.AcademyMemberships.AsNoTracking().SingleOrDefaultAsync(m => m.AcademyId == t.AcademyId && m.Id == t.MembershipId && m.UserId == actor && m.IsActive && m.User.IsActive && m.Role == t.Role);
        var module = StaffAccessRules.ForPath(http.Request.Path.Value ?? "");
        if (live is null || !(await authorization.AuthorizeAsync(http.User, http, AcademyPermissions.AttendanceManage)).Succeeded || live.UseActionGrants && t.Role == AcademyRole.AcademyAdmin && !await db.StaffActionGrants.AnyAsync(g => g.MembershipId == live.Id && g.IsActive && g.Module == module && g.Read && g.Edit)) return Results.Forbid();
        if (t.Role == AcademyRole.AcademyAdmin && !live.AllBranches && !await db.Employees.AnyAsync(e => e.MembershipId == id && db.MembershipBranchAccesses.Any(a => a.MembershipId == t.MembershipId && a.IsActive && a.BranchId == e.BranchId)) && !await db.StaffGroupAssignments.AnyAsync(s => s.AcademyMembershipId == id && s.IsActive && s.TrainingGroup.IsActive && db.MembershipBranchAccesses.Any(a => a.MembershipId == t.MembershipId && a.IsActive && a.BranchId == s.TrainingGroup.BranchId))) return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"staff-manual-key:{t.AcademyId}:{actor}:{key}"}, 0))");
        var replay = await db.StaffAttendanceChanges.AsNoTracking().SingleOrDefaultAsync(c => c.AcademyId == t.AcademyId && c.ActorUserId == actor && c.RequestKey == key);
        if (replay is not null) return replay.RequestHash == hash ? Results.Ok(JsonSerializer.Deserialize<JsonElement>(replay.AfterSnapshot)) : Results.Conflict(new { message = "مفتاح الحفظ مستخدم لبيانات مختلفة؛ أعد تحميل السجل." });
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"staff-open:{t.AcademyId}:{id}"}, 0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"staff-day:{t.AcademyId}:{id}:{body.Date}"}, 0))");
        if (body.Status == "Present" && await db.StaffDailyAttendances.AnyAsync(a => a.AcademyId == t.AcademyId && a.MembershipId == id && a.WorkDate != body.Date && a.Status == "Present" && a.CheckIn != null && a.CheckOut == null)) return Results.Conflict(new { message = "توجد فترة حضور مفتوحة في يوم آخر؛ راجعها أولًا دون اختلاق انصراف." });
        var row = await db.StaffDailyAttendances.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.MembershipId == id && x.WorkDate == body.Date);
        if ((row?.Version ?? 0) != body.Version) return Results.Conflict(new { message = "تغير سجل الحضور منذ فتحه؛ أعد تحميل السجل وراجع التعديل قبل الحفظ." });
        var periods=row is null?[]:await db.StaffWorkPeriods.Where(p=>p.AttendanceId==row.Id).ToArrayAsync();
        if(periods.Length>0&&(body.Status!=row!.Status||body.CheckIn!=row.CheckIn||body.CheckOut!=row.CheckOut||checkoutDate!=row.CheckOutDate))return Results.Conflict(new{message="هذا اليوم يضم فترات عمل محفوظة؛ صحح الفترة المحددة بدل استبدال الحضور اليومي."});
        var before = periods.Length>0?StaffWorkPeriods.Snapshot(row!,periods):Snapshot(row); var beforeVersion = row?.Version ?? 0;
        if (periods.Length==0 && body.Status == "Present" && await StaffAttendanceIntervals.Overlaps(db,t.AcademyId,id,row?.Id,body.Date,body.CheckIn!.Value,checkoutDate,body.CheckOut)) return Results.Conflict(new {message="تتداخل الفترة مع سجل حضور آخر؛ راجع السجل الأصلي قبل الحفظ."});
        var employee = await db.Employees.Where(x => x.AcademyId == t.AcademyId && x.MembershipId == id).Select(x => (Guid?)x.Id).SingleOrDefaultAsync();
        if (employee.HasValue)
        {
            await PayrollEndpoints.Lock(db, t.AcademyId, employee.Value, body.Date);
            // Even a reason-only/no-op save would replace source metadata/version.
            // A genuine retry was already answered above without touching this row.
            if (await PayrollEndpoints.Finalized(db, t.AcademyId, employee.Value, body.Date)) return Results.Conflict(new { message = "كشف هذا الشهر معتمد أو مسدد؛ لا يمكن تعديل مصدر حضوره أو خصمه صامتًا." });
        }
        if (row is null) { row = new StaffDailyAttendance { Id = Guid.NewGuid(), AcademyId = t.AcademyId, MembershipId = id, WorkDate = body.Date, Status = body.Status, CreatedAtUtc = clock.GetUtcNow() }; db.StaffDailyAttendances.Add(row); }
        var schedule = await db.StaffWorkSchedules.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.MembershipId == id && x.DayOfWeek == body.Date.DayOfWeek && x.IsActive);
        row.Status = body.Status; row.CheckIn = body.Status == "Present" ? body.CheckIn : null; row.CheckOut = body.Status == "Present" ? body.CheckOut : null; row.CheckOutDate = checkoutDate;
        row.LateMinutes = row.CheckIn.HasValue && schedule is not null ? Math.Max(0, (int)(row.CheckIn.Value.ToTimeSpan() - schedule.StartTime.ToTimeSpan()).TotalMinutes) : 0;
        row.SuggestedDeduction = schedule is not null && row.LateMinutes > 0 && row.LateMinutes >= schedule.LateThresholdMinutes ? schedule.LateDeduction : 0;
        row.ApprovedDeduction = body.ApprovedDeduction; row.DeductionReason = body.Reason?.Trim(); row.RecordedByUserId = actor; row.UpdatedAtUtc = clock.GetUtcNow();
        try
        {
            await db.SaveChangesAsync();
            var after = periods.Length>0?StaffWorkPeriods.Snapshot(row,periods):Snapshot(row);
            db.StaffAttendanceChanges.Add(new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, AttendanceId = row.Id, MembershipId = id, WorkDate = body.Date, ActorMembershipId = t.MembershipId, ActorUserId = actor, BeforeVersion = beforeVersion, AfterVersion = row.Version, BeforeSnapshot = before, AfterSnapshot = after, RequestKey = key, RequestHash = hash, CreatedAtUtc = row.UpdatedAtUtc, UpdatedAtUtc = row.UpdatedAtUtc });
            await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(JsonSerializer.Deserialize<JsonElement>(after));
        }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { message = "تغير مصدر الحضور أثناء الحفظ؛ أعد تحميل السجل." }); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505", ConstraintName: "IX_StaffAttendanceChanges_AcademyId_ActorUserId_RequestKey" })
        { return Results.Conflict(new { message = "مفتاح الحفظ مستخدم في سجل آخر؛ لم تسجل تغييرات جديدة." }); }
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
        var items=await q.OrderByDescending(x => x.WorkDate).Take(1000).Select(x => new { x.Id, x.MembershipId, name = db.AcademyMemberships.Where(m => m.AcademyId == t.AcademyId && m.Id == x.MembershipId).Select(m => m.User.DisplayName).FirstOrDefault(), x.WorkDate, x.Status, x.CheckIn, x.CheckOut, x.CheckOutDate, x.LateMinutes, x.ApprovedDeduction, x.DeductionReason }).ToArrayAsync();
        var ids=items.Select(x=>x.Id).ToArray();var periods=await db.StaffWorkPeriods.AsNoTracking().Where(p=>ids.Contains(p.AttendanceId)).ToArrayAsync();
        return Results.Ok(new { from = start, to = end, totalDeductions = total, totalCount = await q.CountAsync(), currency, items = items.Select(x=>new{x.Id,x.MembershipId,x.name,x.WorkDate,x.Status,x.CheckIn,x.CheckOut,x.CheckOutDate,x.LateMinutes,x.ApprovedDeduction,x.DeductionReason,workPeriods=StaffWorkPeriods.Statistics(periods.Where(p=>p.AttendanceId==x.Id).ToArray())}) });
    }
    private static async Task<IResult> Export(DateOnly? from, DateOnly? to, bool? allDates, Guid? coachId, string? status, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin)) return Results.Forbid();
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock); var all = allDates == true && from is null && to is null;
        var start = all ? DateOnly.MinValue : from ?? new DateOnly(today.Year, today.Month, 1); var end = all ? DateOnly.MaxValue : to ?? today;
        if ((!all && (start > end || end.DayNumber - start.DayNumber > 366)) || status is not (null or "" or "Present" or "Absent" or "NotRecorded")) return Results.UnprocessableEntity();
        var q = db.StaffDailyAttendances.Where(x => x.AcademyId == t.AcademyId && x.WorkDate >= start && x.WorkDate <= end && (!coachId.HasValue || x.MembershipId == coachId) && (string.IsNullOrEmpty(status) || x.Status == status));
        if (await q.CountAsync() > 5000) return Results.Problem(statusCode: 413, title: "نطاق التصدير كبير", detail: "اختر فترة أصغر؛ الحد 5000 سجل.");
        var rows = await q.OrderBy(x => x.WorkDate).Select(x => new {x.Id, name = db.AcademyMemberships.Where(m => m.AcademyId == t.AcademyId && m.Id == x.MembershipId).Select(m => m.User.DisplayName).FirstOrDefault(), x.WorkDate, x.Status, x.CheckIn, x.CheckOut, x.CheckOutDate, x.LateMinutes, x.ApprovedDeduction, x.DeductionReason }).ToListAsync();
        var ids=rows.Select(r=>r.Id).ToArray();var periods=await db.StaffWorkPeriods.AsNoTracking().Where(p=>ids.Contains(p.AttendanceId)).ToArrayAsync();
        var csv = new List<string[]> { new[] { "المدرب", "يوم بدء الحضور", "الحالة", "الحضور", "الانصراف", "تاريخ الانصراف", "دقائق التأخير", "الخصم المعتمد", "سبب الخصم", "عدد الفترات المحفوظة", "دقائق الفترات المعلومة", "إجمالي دقائق العمل (فارغ إن لم تكتمل أو لم تعرف)" } };
        csv.AddRange(rows.Select(x => {var p=periods.Where(p=>p.AttendanceId==x.Id).ToArray();var known=p.Sum(s=>StaffWorkPeriods.Seconds(s)??0)/60m;return new[] { x.name ?? "", x.WorkDate.ToString("yyyy-MM-dd"), x.Status == "Present" ? "حاضر" : x.Status == "Absent" ? "غائب" : "لم يُسجل", x.CheckIn?.ToString("HH:mm") ?? "", x.CheckOut?.ToString("HH:mm") ?? "", (x.CheckOutDate ?? (x.CheckOut.HasValue ? x.WorkDate : (DateOnly?)null))?.ToString("yyyy-MM-dd") ?? "", x.LateMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture), x.ApprovedDeduction.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), x.DeductionReason ?? "",p.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),p.Length>0?known.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture):"",p.Length>0&&p.All(s=>StaffWorkPeriods.Seconds(s).HasValue)?known.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture):"" };}));
        return Academy.Api.Slice8.ReportingEndpoints.Csv(csv, $"coach-daily-{start:yyyy-MM}.csv");
    }
}
public sealed record DailyWrite(DateOnly Date, string Status, TimeOnly? CheckIn, TimeOnly? CheckOut, decimal ApprovedDeduction, string? Reason, DateOnly? CheckOutDate = null, uint Version = 0);
public sealed record WorkScheduleWrite(DayOfWeek[] Days, TimeOnly Start, TimeOnly End, int ThresholdMinutes, decimal Deduction);
