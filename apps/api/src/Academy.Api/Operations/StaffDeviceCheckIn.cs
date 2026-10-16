using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

// Location and biometric success are client assertions, not hardware attestation.
// This is a Demo/operational convenience; payroll deductions still require an administrator.
public static class StaffDeviceCheckIn
{
    private const double MaxAccuracyMeters = 25;

    public static void MapStaffDeviceCheckIn(this WebApplication app)
    {
        var sites = app.MapGroup("/api/v1/manage/branches").RequireAuthorization(AcademyPermissions.StructureManage);
        sites.MapGet("/{id:guid}/check-in-site", Site);
        sites.MapPut("/{id:guid}/check-in-site", SaveSite).AddEndpointFilter<CsrfFilter>();
        var self = app.MapGroup("/api/v1/staff/check-in").RequireAuthorization(AcademyPermissions.TenantAccess);
        self.MapGet("/sites", AssignedSites);
        self.MapPost("/", CheckIn).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("device-check-in");
    }

    private static async Task<IResult> Site(Guid id, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        var branch = await db.Branches.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id);
        if (branch is null) return Results.NotFound();
        var history = await db.BranchCheckInSiteAudits.Where(x => x.AcademyId == t.AcademyId && x.BranchId == id)
            .OrderByDescending(x => x.CreatedAtUtc).Take(10).Select(x => new { x.Id, x.CreatedAtUtc, x.ActorUserId, x.BeforeLatitude, x.BeforeLongitude, x.AfterLatitude, x.AfterLongitude, x.BeforePolicy, x.AfterPolicy }).ToListAsync();
        var candidates=await db.AcademyMemberships.Where(m=>m.AcademyId==t.AcademyId&&m.IsActive&&m.User.IsActive&&(m.Role==AcademyRole.AcademyOwner||m.Role==AcademyRole.AcademyAdmin)).Select(m=>new{m.Id,name=m.User.DisplayName}).ToArrayAsync();
        var reviewers=new List<object>();foreach(var m in candidates)if(await StaffAttendancePolicy.EligibleReviewer(db,t.AcademyId,id,m.Id))reviewers.Add(new{id=m.Id,m.name});
        return Results.Ok(new { branch.Id, branch.ArabicName, branch.CheckInLatitude, branch.CheckInLongitude, branch.Version, policy=StaffAttendancePolicy.Read(branch), reviewers, history });
    }

    private static async Task<IResult> SaveSite(Guid id, CheckInSiteWrite body, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!;
        if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        if (body.Latitude is null != (body.Longitude is null) ||
            body.Latitude.HasValue && (!double.IsFinite(body.Latitude.Value) || body.Latitude is < -90 or > 90) ||
            body.Longitude.HasValue && (!double.IsFinite(body.Longitude.Value) || body.Longitude is < -180 or > 180))
            return Results.UnprocessableEntity(new { message = "أدخل إحداثيات فرع صحيحة، أو امسح القيمتين معًا لإيقاف التسجيل الذاتي." });
        if(body.Policy is not null&&(!body.Policy.Valid||body.Version is null))return Results.UnprocessableEntity(new{message="راجع سياسة الحضور: نطاق10–500م، دقة1–100م لا تتجاوز النطاق، حداثة5–60ث، ومحاولة30–120ث. حدّث الصفحة قبل الحفظ."});
        await using var tx = await db.Database.BeginTransactionAsync();
        foreach(var member in new[]{t.MembershipId,body.Policy?.ReviewerMembershipId??t.MembershipId}.Distinct().Order())await StaffPasskeys.Lock(db,$"grants:{t.AcademyId}:{member}");
        if(!await StaffPasskeys.Live(db,t,Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!)))return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"check-in-site:{t.AcademyId}:{id}"}, 0))");
        var branch = await db.Branches.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id);
        if (branch is null) return Results.NotFound();
        var before=StaffAttendancePolicy.Read(branch);var policy=body.Policy??before;
        if (branch.CheckInLatitude == body.Latitude && branch.CheckInLongitude == body.Longitude&&policy==before) return Results.Ok(new { branch.Id, branch.Version, policy, replay = true });
        if(body.Version.HasValue&&branch.Version!=body.Version)return Results.Conflict(new{message="تغير الفرع أو سياسته؛ حدّث البيانات قبل الحفظ. لم نستبدل إعدادًا أحدث."});
        if(body.Policy?.ReviewerMembershipId is Guid reviewerId)
        {
            if(!await StaffAttendancePolicy.EligibleReviewer(db,t.AcademyId,id,reviewerId))return Results.UnprocessableEntity(new{message="اختر مسؤولًا فعالًا مخولًا بمراجعة موظفي هذا الفرع؛ المالك يبقى بديلًا للمراجعة."});
        }
        var now = clock.GetUtcNow();
        db.BranchCheckInSiteAudits.Add(new BranchCheckInSiteAudit { Id = Guid.NewGuid(), AcademyId = t.AcademyId, BranchId = id,
            ActorUserId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!), BeforeLatitude = branch.CheckInLatitude,
            BeforeLongitude = branch.CheckInLongitude, AfterLatitude = body.Latitude, AfterLongitude = body.Longitude,
            BeforePolicy=JsonSerializer.Serialize(before,new JsonSerializerOptions(JsonSerializerDefaults.Web)),AfterPolicy=JsonSerializer.Serialize(policy,new JsonSerializerOptions(JsonSerializerDefaults.Web)),CreatedAtUtc = now, UpdatedAtUtc = now });
        branch.CheckInLatitude = body.Latitude; branch.CheckInLongitude = body.Longitude; branch.UpdatedAtUtc = now;
        policy.Apply(branch);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Ok(new { branch.Id, branch.Version, policy });
    }

    private static async Task<IResult> AssignedSites(CurrentTenant tenant, FoundationDbContext db, IHostEnvironment environment)
    {
        var t = (await tenant.ResolveAsync())!;
        if (t.Role != AcademyRole.Coach) return Results.Forbid();
        if (!environment.IsEnvironment("Demo")) return Results.Problem("الحضور الذاتي بالجهاز غير مُفعّل خارج بيئة العرض حتى اكتمال بوابة الأمان.", statusCode: 503);
        return Results.Ok(await db.StaffGroupAssignments.Where(x => x.AcademyId == t.AcademyId && x.AcademyMembershipId == t.MembershipId && x.IsActive && x.TrainingGroup.IsActive && x.TrainingGroup.Branch.IsActive && x.TrainingGroup.Branch.StaffCheckInEnabled && x.TrainingGroup.Branch.CheckInLatitude != null && x.TrainingGroup.Branch.CheckInLongitude != null)
            .Select(x => new { id = x.TrainingGroup.BranchId, name = x.TrainingGroup.Branch.ArabicName, radiusMeters = x.TrainingGroup.Branch.StaffCheckInRadiusMeters }).Distinct().OrderBy(x => x.name).ToListAsync());
    }

    private static async Task<IResult> CheckIn(DeviceCheckInWrite body, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock, IHostEnvironment environment,IConfiguration configuration)
    {
        var t = (await tenant.ResolveAsync())!;
        if (t.Role != AcademyRole.Coach) return Results.Forbid();
        if (!environment.IsEnvironment("Demo")) return Results.Problem("الحضور الذاتي بالجهاز غير مُفعّل خارج بيئة العرض حتى اكتمال بوابة الأمان.", statusCode: 503);
        if(configuration.GetValue<bool>("StaffPasskeys:Enabled"))return Results.Conflict(new{message="يلزم توقيع يتحقق منه الخادم؛ التأكيد المحلي وحده غير مقبول. استخدم مسار التحقق الآمن أو اطلب إثبات حضور للمراجعة.",attendanceRecorded=false});
        if (!body.ClientBiometricAcknowledged || !double.IsFinite(body.Latitude) || !double.IsFinite(body.Longitude) || !double.IsFinite(body.AccuracyMeters) ||
            body.Latitude is < -90 or > 90 || body.Longitude is < -180 or > 180 || body.AccuracyMeters is < 0 or > MaxAccuracyMeters)
            return Results.UnprocessableEntity(new { message = "يلزم تأكيد بصمة الجهاز وموقع دقيق قبل تسجيل الحضور." });
        var branch = await db.Branches.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == body.BranchId && x.IsActive);
        if (branch is null || branch.CheckInLatitude is null || branch.CheckInLongitude is null ||
            !await db.StaffGroupAssignments.AnyAsync(x => x.AcademyId == t.AcademyId && x.AcademyMembershipId == t.MembershipId && x.IsActive && x.TrainingGroup.IsActive && x.TrainingGroup.BranchId == branch.Id)) return Results.NotFound();
        if(!branch.StaffCheckInEnabled)return Results.Conflict(new{message="التسجيل الذاتي موقوف لهذا الفرع؛ اطلب مراجعة الإدارة."});
        var distance = DistanceMeters(body.Latitude, body.Longitude, branch.CheckInLatitude.Value, branch.CheckInLongitude.Value);
        if (body.AccuracyMeters>branch.StaffCheckInMaxAccuracyMeters||distance + body.AccuracyMeters > branch.StaffCheckInRadiusMeters) return Results.Conflict(new { message = "الموقع خارج نطاق الفرع المحفوظ أو دقته غير كافية. اقترب من الفرع وأعد المحاولة." });
        var zoneId = await db.Academies.Where(x => x.Id == t.AcademyId).Select(x => x.TimeZone).SingleAsync();
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(zoneId));
        var date = DateOnly.FromDateTime(local.DateTime); var checkIn = TimeOnly.FromDateTime(local.DateTime);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"staff-open:{t.AcademyId}:{t.MembershipId}"}, 0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"staff-day:{t.AcademyId}:{t.MembershipId}:{date}"}, 0))");
        if (await db.StaffDailyAttendances.AnyAsync(x => x.AcademyId == t.AcademyId && x.MembershipId == t.MembershipId && x.WorkDate != date && x.Status == "Present" && x.CheckIn != null && x.CheckOut == null)) return Results.Conflict(new { message = "توجد فترة حضور مفتوحة في يوم آخر؛ اطلب مراجعتها من الإدارة أولًا." });
        var row = await db.StaffDailyAttendances.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.MembershipId == t.MembershipId && x.WorkDate == date);
        if(row is not null&&await db.StaffWorkPeriods.AnyAsync(p=>p.AttendanceId==row.Id))return Results.Conflict(new{message="اليوم له فترات عمل محفوظة؛ راجع الإدارة لتسجيل الفترة المحددة. لم يسجل حضور جديد."});
        if (row is not null && row.Status == "Present") return Results.Ok(new { row.Id, row.WorkDate, row.CheckIn, replay = true });
        if (row is not null && row.Status != "NotRecorded") return Results.Conflict(new { message = "الحضور مسجل بحالة مختلفة؛ راجع الإدارة." });
        if (await StaffAttendanceIntervals.Overlaps(db,t.AcademyId,t.MembershipId,row?.Id,date,checkIn,null,null)) return Results.Conflict(new {message="تتداخل الفترة مع سجل حضور آخر؛ اطلب مراجعة الإدارة."});
        var actor = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (row is null) { row = new StaffDailyAttendance { Id = Guid.NewGuid(), AcademyId = t.AcademyId, MembershipId = t.MembershipId, WorkDate = date, Status = "Present", CreatedAtUtc = clock.GetUtcNow() }; db.StaffDailyAttendances.Add(row); }
        row.Status = "Present"; row.CheckIn = checkIn; row.RecordedByUserId = actor; row.UpdatedAtUtc = clock.GetUtcNow();
        var schedule = await db.StaffWorkSchedules.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.MembershipId == t.MembershipId && x.DayOfWeek == date.DayOfWeek && x.IsActive);
        row.LateMinutes = schedule is null ? 0 : Math.Max(0, (int)(checkIn.ToTimeSpan() - schedule.StartTime.ToTimeSpan()).TotalMinutes);
        row.SuggestedDeduction = schedule is not null && row.LateMinutes >= schedule.LateThresholdMinutes && row.LateMinutes > 0 ? schedule.LateDeduction : 0;
        // Never self-approve a financial deduction.
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Created("/api/v1/staff/check-in/", new { row.Id, row.WorkDate, row.CheckIn, distanceMeters = Math.Round(distance), row.LateMinutes });
    }

    internal static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double rad = Math.PI / 180;
        var dLat = (lat2 - lat1) * rad; var dLon = (lon2 - lon1) * rad;
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(lat1 * rad) * Math.Cos(lat2 * rad) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 6371000 * 2 * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}

public sealed record CheckInSiteWrite(double? Latitude, double? Longitude,StaffAttendancePolicy? Policy=null,uint? Version=null);
public sealed record DeviceCheckInWrite(Guid BranchId, double Latitude, double Longitude, double AccuracyMeters, bool ClientBiometricAcknowledged);
