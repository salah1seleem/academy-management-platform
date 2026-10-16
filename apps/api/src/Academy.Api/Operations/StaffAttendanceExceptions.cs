using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Academy.Api.Operations;

public static class StaffAttendanceExceptions
{
    public const string SelfPath = "/api/v1/staff/attendance-exceptions";
    public const string ManagePath = "/api/v1/manage/staff-attendance-exceptions";
    // Only these self routes bypass academy-wide CRUD. The employee is resolved server-side.
    public static bool IsOwnPath(string path) => path.TrimEnd('/') is SelfPath or SelfPath + "/options" or StaffWorkPeriods.OwnPath;
    public static void MapStaffAttendanceExceptions(this WebApplication app)
    {
        var self = app.MapGroup(SelfPath).RequireAuthorization(AcademyPermissions.TenantAccess);
        self.MapGet("/options", Options); self.MapGet("/", Own);
        self.MapPost("/", Request).AddEndpointFilter<CsrfFilter>();
        var manage = app.MapGroup(ManagePath).RequireAuthorization(AcademyPermissions.StaffProvision);
        manage.MapGet("/", List); manage.MapGet("/{id:guid}", Detail);
        manage.MapPost("/{id:guid}/review", Review).AddEndpointFilter<CsrfFilter>();
    }
    private static Guid Actor(HttpContext h) => Guid.Parse(h.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static string? Key(HttpContext h) { var key = h.Request.Headers["Idempotency-Key"].ToString(); return key.Length is >= 8 and <= 100 ? key : null; }
    private static IResult Invalid(string message) => Results.UnprocessableEntity(new { message });
    private static IResult Conflict(string message) => Results.Conflict(new { message });
    private static bool DuplicateCommand(DbUpdateException e) => e.InnerException is PostgresException { SqlState: "23505" } pg && pg.ConstraintName?.StartsWith("IX_StaffAttendanceExceptions_", StringComparison.Ordinal) == true;
    private static Task Lock(FoundationDbContext db, string key) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))");
    private static Task<Employee?> Employee(CurrentTenant tenant, FoundationDbContext db, TenantMembership t) => db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.AcademyId == t.AcademyId && e.MembershipId == t.MembershipId && e.IsActive);
    private static async Task<TimeZoneInfo> Zone(FoundationDbContext db, Guid academy) => TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a => a.Id == academy).Select(a => a.TimeZone).SingleAsync());
    private static async Task<Guid[]> Sites(FoundationDbContext db, TenantMembership t, Employee employee)
    {
        var assigned = await db.StaffGroupAssignments.Where(a => a.AcademyId == t.AcademyId && a.AcademyMembershipId == t.MembershipId && a.IsActive && a.TrainingGroup.IsActive).Select(a => a.TrainingGroup.BranchId).ToArrayAsync();
        return await db.Branches.Where(b => b.AcademyId == t.AcademyId && b.IsActive && (b.Id == employee.BranchId || assigned.Contains(b.Id))).Select(b => b.Id).ToArrayAsync();
    }
    private static object View(StaffAttendanceException r) => new { r.Id, r.BranchId, r.Operation, r.WorkDate, r.ClaimedAtUtc, receivedAtUtc = r.CreatedAtUtc, r.Reason, r.Status, r.ReviewNote, r.ReviewedAtUtc, r.AttendanceId, r.TargetAttendanceId, r.TargetWorkPeriodId, r.TargetWorkPeriodVersion, targetPeriod = r.TargetPeriodSnapshot is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(r.TargetPeriodSnapshot), r.AppliedWorkPeriodId, r.RequestTimeZoneId, r.Version, trustSource = "AdministrativeException" };
    private static async Task<IResult> Options(CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!;
        if (t.Role == AcademyRole.Guardian) return Results.Forbid();
        var e = await Employee(tenant, db, t); if (e is null) return Results.Ok(new { configured = false });
        var ids = await Sites(db, t, e);
        var open = await db.StaffDailyAttendances.AsNoTracking().Where(a => a.AcademyId == t.AcademyId && a.MembershipId == t.MembershipId && a.Status == "Present" && a.CheckIn != null && a.CheckOut == null).OrderBy(a => a.WorkDate).Take(2).Select(a => new { a.Id, a.WorkDate, a.CheckIn }).ToArrayAsync();
        var openIds=open.Select(a=>a.Id).ToArray();var periods=await db.StaffWorkPeriods.AsNoTracking().Where(p=>openIds.Contains(p.AttendanceId)).ToArrayAsync();
        var child=periods.Where(p=>p.EndTime is null).ToArray();
        var valid=open.Length==1&&(periods.Length==0||child.Length==1&&child[0].StartUtc.HasValue);
        object? target=valid?new {open[0].Id,open[0].WorkDate,checkIn=child.Length==1?child[0].StartTime:open[0].CheckIn,targetWorkPeriodId=child.Length==1?(Guid?)child[0].Id:null,targetWorkPeriodVersion=child.Length==1?(uint?)child[0].Version:null}:null;
        return Results.Ok(new { configured = true, nowUtc = clock.GetUtcNow(), timeZone = (await Zone(db, t.AcademyId)).Id, openAttendance = target, requiresSourceReview = open.Length > 0&&!valid, branches = await db.Branches.Where(b => ids.Contains(b.Id)).Select(b => new { b.Id, name = b.ArabicName }).ToArrayAsync() });
    }
    private static async Task<IResult> Own(CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role == AcademyRole.Guardian) return Results.Forbid();
        return Results.Ok((await db.StaffAttendanceExceptions.AsNoTracking().Where(r => r.AcademyId == t.AcademyId && r.MembershipId == t.MembershipId).OrderByDescending(r => r.CreatedAtUtc).Take(100).ToArrayAsync()).Select(View));
    }
    private static async Task<IResult> Request(StaffExceptionWrite body, HttpContext h, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role == AcademyRole.Guardian) return Results.Forbid();
        var key = Key(h); var reason = body.Reason?.Trim(); var now = clock.GetUtcNow();
        if (key is null || body.Operation is not ("CheckIn" or "CheckOut") || body.Operation == "CheckIn" && (body.TargetAttendanceId.HasValue||body.TargetWorkPeriodId.HasValue) || body.TargetWorkPeriodId.HasValue!=body.TargetWorkPeriodVersion.HasValue || reason?.Length is not (>= 5 and <= 500) || body.ClaimedAtUtc.Year < 2000 || body.ClaimedAtUtc > now) return Invalid("أدخل سببًا واضحًا ووقتًا مدعى غير مستقبلي، ثم أعد إرسال الطلب.");
        var zone=await Zone(db,t.AcademyId);
        var local = TimeZoneInfo.ConvertTime(body.ClaimedAtUtc, zone);
        var date = DateOnly.FromDateTime(local.DateTime);
        // Preserve keys from the preceding version when an explicit source was not supplied.
        var hash = body.TargetWorkPeriodId.HasValue ? Hash(new {body.BranchId,body.Operation,claimed=body.ClaimedAtUtc.ToUniversalTime(),reason,body.TargetAttendanceId,body.TargetWorkPeriodId,body.TargetWorkPeriodVersion}) : body.TargetAttendanceId.HasValue
            ? Hash(new { body.BranchId, body.Operation, claimed = body.ClaimedAtUtc.ToUniversalTime(), reason, body.TargetAttendanceId })
            : Hash(new { body.BranchId, body.Operation, claimed = body.ClaimedAtUtc.ToUniversalTime(), reason });
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(db, $"grants:{t.AcademyId}:{t.MembershipId}");
        if (!await db.AcademyMemberships.AsNoTracking().AnyAsync(m => m.Id == t.MembershipId && m.AcademyId == t.AcademyId && m.IsActive && m.UserId == Actor(h) && m.User.IsActive)) return Results.Forbid();
        var e = await Employee(tenant, db, t); if (e is null) return Invalid("لا يوجد ملف موظف مرتبط بحسابك.");
        if (t.Role == AcademyRole.AcademyAdmin && !await db.AcademyMemberships.AnyAsync(m => m.Id == t.MembershipId && (m.AllBranches || db.MembershipBranchAccesses.Any(a => a.MembershipId == m.Id && a.IsActive && a.BranchId == body.BranchId) && db.MembershipBranchAccesses.Any(a => a.MembershipId == m.Id && a.IsActive && a.BranchId == e.BranchId)))) return Results.Forbid();
        if (!(await Sites(db, t, e)).Contains(body.BranchId)) return Results.NotFound();
        await Lock(db, $"staff-exception-key:{t.AcademyId}:{Actor(h)}:{key}");
        var old = await db.StaffAttendanceExceptions.SingleOrDefaultAsync(r => r.AcademyId == t.AcademyId && r.RequestedByUserId == Actor(h) && r.RequestKey == key);
        if (old is not null) return old.MembershipId == t.MembershipId && old.RequestHash == hash ? Results.Ok(View(old)) : Conflict("مفتاح الطلب مستخدم لبيانات أو سياق مختلف؛ افتح طلبًا جديدًا.");
        StaffDailyAttendance? target = null;
        StaffWorkPeriod? targetPeriod=null;
        await Lock(db, $"staff-open:{t.AcademyId}:{t.MembershipId}");
        if (body.Operation == "CheckOut")
        {
            var open = await db.StaffDailyAttendances.AsNoTracking().Where(a => a.AcademyId == t.AcademyId && a.MembershipId == t.MembershipId && a.Status == "Present" && a.CheckIn != null && a.CheckOut == null).Take(2).ToArrayAsync();
            if (open.Length != 1) return Conflict("الانصراف يحتاج سجل حضور مفتوحًا واحدًا؛ راجع الإدارة عند غيابه أو تعدده.");
            target = open[0];
            if (body.TargetAttendanceId.HasValue && body.TargetAttendanceId != target.Id) return Results.NotFound();
            var children=await db.StaffWorkPeriods.AsNoTracking().Where(p=>p.AttendanceId==target.Id).ToArrayAsync();
            if(children.Length>0)
            {
                if(!body.TargetWorkPeriodId.HasValue)return Conflict("أعد قراءة الفترة المفتوحة قبل طلب الانصراف؛ يجب تحديد الفترة نفسها.");
                targetPeriod=children.SingleOrDefault(p=>p.Id==body.TargetWorkPeriodId);
                if(targetPeriod is null)return Results.NotFound();
                if(targetPeriod.Version!=body.TargetWorkPeriodVersion||targetPeriod.EndTime.HasValue||targetPeriod.StartUtc is null||targetPeriod.TimeZoneId!=zone.Id)return Conflict("تغيرت الفترة أو توقيتها؛ أعد قراءة المصدر قبل طلب الانصراف.");
                if(body.ClaimedAtUtc<=targetPeriod.StartUtc)return Invalid("وقت الانصراف يجب أن يكون بعد بداية الفترة المحددة.");
            }
            else if(body.TargetWorkPeriodId.HasValue)return Results.NotFound();
            if (date < target.WorkDate || date == target.WorkDate && TimeOnly.FromDateTime(local.DateTime) < target.CheckIn) return Invalid("وقت الانصراف المدعى يسبق الحضور المسجل.");
            date = target.WorkDate; // An overnight checkout belongs to the original workday.
        }
        else
        {
            target=await db.StaffDailyAttendances.AsNoTracking().SingleOrDefaultAsync(a=>a.AcademyId==t.AcademyId&&a.MembershipId==t.MembershipId&&a.WorkDate==date&&a.Status=="Present"&&a.CheckIn!=null&&a.CheckOut!=null);
        }
        var terms = await ContractHistory.Bounds(db, e, date);
        if (date < terms.ContractStart || terms.ContractEnd.HasValue && date > terms.ContractEnd) return Invalid("يوم بدء الحضور خارج مدة العمل المسجلة.");
        var r = new StaffAttendanceException { Id = Guid.NewGuid(), AcademyId = t.AcademyId, EmployeeId = e.Id, MembershipId = t.MembershipId, BranchId = body.BranchId, RequestedByUserId = Actor(h), Operation = body.Operation, WorkDate = date, ClaimedAtUtc = body.ClaimedAtUtc.ToUniversalTime(), Reason = reason!, RequestKey = key, RequestHash = hash, TargetAttendanceId = target?.Id, TargetAttendanceVersion = target?.Version, TargetWorkPeriodId=targetPeriod?.Id,TargetWorkPeriodVersion=targetPeriod?.Version,TargetPeriodSnapshot=targetPeriod is null?null:JsonSerializer.Serialize(StaffWorkPeriods.View(targetPeriod),new JsonSerializerOptions(JsonSerializerDefaults.Web)),RequestTimeZoneId=zone.Id,CreatedAtUtc = now, UpdatedAtUtc = now };
        db.StaffAttendanceExceptions.Add(r);
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); }
        catch (DbUpdateException ex) when (DuplicateCommand(ex)) { return Conflict("مفتاح الطلب مستخدم من قبل؛ راجع طلباتك أو أعد تحميل السياق."); }
        return Results.Created(SelfPath, View(r));
    }
    private static bool Reviewer(TenantMembership t) => t.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin;
    private static async Task<bool> ManagementAllowed(FoundationDbContext db, TenantMembership t, bool edit)
    {
        if (!Reviewer(t)) return false;
        var m = await db.AcademyMemberships.AsNoTracking().SingleOrDefaultAsync(m => m.Id == t.MembershipId && m.AcademyId == t.AcademyId && m.IsActive && m.User.IsActive);
        if (m is null || m.Role != t.Role) return false;
        if (t.Role == AcademyRole.AcademyOwner) return true;
        if (!m.AllSports || m.PrimaryMembershipId.HasValue || await db.StaffGrants.AnyAsync(g => g.MembershipId == m.Id && g.Permission == AcademyPermissions.StaffProvision && g.IsActive && !g.Allowed)) return false;
        return !m.UseActionGrants || await db.StaffActionGrants.AnyAsync(g => g.MembershipId == m.Id && g.IsActive && g.Module == "staff" && g.Read && (!edit || g.Edit));
    }
    private static async Task<IResult> List(string? status, Guid? branchId, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; if (!await ManagementAllowed(db, t, false)) return Results.Forbid();
        if (status is not (null or "" or "Pending" or "Approved" or "Rejected")) return Invalid("اختر حالة طلب صحيحة.");
        var q = db.StaffAttendanceExceptions.AsNoTracking().Where(r => r.AcademyId == t.AcademyId && (string.IsNullOrEmpty(status) || r.Status == status) && (!branchId.HasValue || r.BranchId == branchId));
        return Results.Ok(new { total = await q.CountAsync(), items = await q.OrderByDescending(r => r.CreatedAtUtc).Take(200).Select(r => new { r.Id, r.Status, r.Operation, r.WorkDate, r.ClaimedAtUtc, receivedAtUtc = r.CreatedAtUtc, name = db.Employees.Where(e => e.Id == r.EmployeeId).Select(e => e.Name).FirstOrDefault(), branch = db.Branches.Where(b => b.Id == r.BranchId).Select(b => b.ArabicName).FirstOrDefault() }).ToArrayAsync() });
    }
    private static async Task<IResult> Detail(Guid id, HttpContext h, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; if (!await ManagementAllowed(db, t, false)) return Results.Forbid();
        var r = await db.StaffAttendanceExceptions.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.AcademyId == t.AcademyId); if (r is null) return Results.NotFound();
        var currentAttendance = await db.StaffDailyAttendances.AsNoTracking().Where(a => a.AcademyId == t.AcademyId && a.MembershipId == r.MembershipId && a.WorkDate == r.WorkDate).Select(a => new { a.Status, a.CheckIn, a.CheckOut, a.CheckOutDate, a.UpdatedAtUtc }).SingleOrDefaultAsync();
        var currentPeriod=r.TargetWorkPeriodId.HasValue?await db.StaffWorkPeriods.AsNoTracking().SingleOrDefaultAsync(p=>p.Id==r.TargetWorkPeriodId&&p.AcademyId==t.AcademyId):null;
        return Results.Ok(new { request = View(r), currentAttendance,currentPeriod=currentPeriod is null?null:StaffWorkPeriods.View(currentPeriod), timeZone = r.RequestTimeZoneId??(await Zone(db, t.AcademyId)).Id, canReview = r.Status == "Pending" && r.RequestedByUserId != Actor(h) && await ManagementAllowed(db, t, true), name = await db.Employees.Where(e => e.Id == r.EmployeeId).Select(e => e.Name).SingleAsync(), branch = await db.Branches.Where(b => b.Id == r.BranchId).Select(b => b.ArabicName).SingleAsync(), r.BeforeSnapshot, r.AfterSnapshot });
    }
    private static string Snapshot(StaffDailyAttendance? r) => JsonSerializer.Serialize(r is null ? null : new { r.Id, r.WorkDate, r.Status, r.CheckIn, r.CheckOut, r.CheckOutDate, r.RecordedByUserId, r.UpdatedAtUtc });
    private static async Task<IResult> Review(Guid id, StaffExceptionReview body, HttpContext h, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock, IAuthorizationService authorization)
    {
        var t = (await tenant.ResolveAsync())!; if (!Reviewer(t)) return Results.Forbid();
        var key = Key(h); var note = body.Note?.Trim();
        if (key is null || note?.Length > 500 || !body.Approve && note?.Length is not (>= 5 and <= 500)) return Invalid("اكتب سبب الرفض من خمسة أحرف على الأقل؛ الملاحظة عند القبول اختيارية.");
        var hash = Hash(new { id, body.Approve, note });
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(db, $"grants:{t.AcademyId}:{t.MembershipId}");
        if (!await ManagementAllowed(db, t, true)) return Results.Forbid();
        if (!await db.AcademyMemberships.AsNoTracking().AnyAsync(m => m.Id == t.MembershipId && m.AcademyId == t.AcademyId && m.Role == t.Role && m.IsActive && m.User.IsActive) || !(await authorization.AuthorizeAsync(h.User, h, AcademyPermissions.StaffProvision)).Succeeded) return Results.Forbid();
        if (t.Role == AcademyRole.AcademyAdmin && !await db.StaffActionGrants.AnyAsync(g => g.MembershipId == t.MembershipId && g.IsActive && g.Module == "staff" && g.Read && g.Edit) && await db.AcademyMemberships.AnyAsync(m => m.Id == t.MembershipId && m.UseActionGrants)) return Results.Forbid();
        await Lock(db, $"staff-exception-review-key:{t.AcademyId}:{Actor(h)}:{key}");
        if (await db.StaffAttendanceExceptions.AnyAsync(r => r.AcademyId == t.AcademyId && r.ReviewedByUserId == Actor(h) && r.ReviewKey == key && r.Id != id)) return Conflict("مفتاح المراجعة مستخدم لطلب آخر.");
        await Lock(db, $"staff-exception:{t.AcademyId}:{id}");
        var r = await db.StaffAttendanceExceptions.SingleOrDefaultAsync(r => r.AcademyId == t.AcademyId && r.Id == id); if (r is null) return Results.NotFound();
        if (r.RequestedByUserId == Actor(h)) return Results.Forbid();
        if (r.Status != "Pending") return r.ReviewKey == key && r.ReviewHash == hash && r.ReviewedByUserId == Actor(h) ? Results.Ok(View(r)) : Conflict("اتُخذ قرار لهذا الطلب؛ أعد تحميل التفاصيل.");
        if (r.Version != body.Version) return Conflict("تغير الطلب؛ أعد تحميل التفاصيل قبل المراجعة.");
        var e = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.AcademyId == t.AcademyId && e.Id == r.EmployeeId && e.MembershipId == r.MembershipId);
        if (e is null) return Conflict("تغير ربط ملف الموظف؛ راجع الإدارة.");
        if (body.Approve && (!e.IsActive || !await db.Branches.AnyAsync(b => b.Id == r.BranchId && b.IsActive)||!await db.AcademyMemberships.AnyAsync(m=>m.Id==r.MembershipId&&m.AcademyId==t.AcademyId&&m.UserId==r.RequestedByUserId&&m.IsActive&&m.User.IsActive))) return Conflict("الموظف أو حسابه أو الفرع غير فعال؛ يمكنك رفض الطلب دون إعادة تفعيله.");
        // Recheck live branch grants under the same lock used to change them.
        if (t.Role == AcademyRole.AcademyAdmin && !await db.AcademyMemberships.AnyAsync(m => m.Id == t.MembershipId && (m.AllBranches || db.MembershipBranchAccesses.Any(a => a.MembershipId == m.Id && a.IsActive && a.BranchId == r.BranchId) && db.MembershipBranchAccesses.Any(a => a.MembershipId == m.Id && a.IsActive && a.BranchId == e.BranchId)))) return Results.Forbid();
        var now = clock.GetUtcNow();
        if (body.Approve)
        {
            if (r.ClaimedAtUtc > now) return Conflict("وقت الطلب مستقبلي ولا يمكن اعتماده.");
            await Lock(db, $"staff-open:{t.AcademyId}:{r.MembershipId}");
            await Lock(db, $"staff-day:{t.AcademyId}:{r.MembershipId}:{r.WorkDate}");
            await PayrollEndpoints.Lock(db, t.AcademyId, e.Id, r.WorkDate);
            e = await db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.Id == e.Id && x.IsActive && x.MembershipId == r.MembershipId);
            if (e is null) return Conflict("تغير ملف الموظف؛ أعد تحميل الطلب.");
            var terms = await ContractHistory.Bounds(db, e, r.WorkDate);
            if (r.WorkDate < terms.ContractStart || terms.ContractEnd.HasValue && r.WorkDate > terms.ContractEnd) return Conflict("وقت الطلب غير صالح وفق مدة العمل المعتمدة الحالية.");
            if (await PayrollEndpoints.Finalized(db, t.AcademyId, e.Id, r.WorkDate)) return Conflict("شهر الموظف معتمد أو مسدد؛ لا يعدّل الحضور صامتًا. اطلب مراجعة إدارية مستقلة.");
            if(r.RequestTimeZoneId is not null&&r.RequestTimeZoneId!=(await Zone(db,t.AcademyId)).Id)return Conflict("تغير توقيت الأكاديمية؛ راجع أصل الطلب بدل إعادة تفسيره.");
            var row = await db.StaffDailyAttendances.SingleOrDefaultAsync(a => a.AcademyId == t.AcademyId && a.MembershipId == r.MembershipId && a.WorkDate == r.WorkDate);
            if(r.TargetWorkPeriodId.HasValue||r.Operation=="CheckIn"&&r.TargetAttendanceId.HasValue)
            {
                if(row is null)return Conflict("لم يعد مصدر الطلب موجودًا؛ راجع الإدارة.");
                if(r.Operation=="CheckIn"&&await db.StaffDailyAttendances.AnyAsync(a=>a.MembershipId==r.MembershipId&&a.AcademyId==t.AcademyId&&a.Id!=row.Id&&a.CheckIn!=null&&a.CheckOut==null&&a.Status=="Present"))return Conflict("توجد فترة مفتوحة أخرى؛ لا يعتمد حضور جديد فوقها.");
                var failure=await StaffExceptionPeriods.Apply(r,row,t,Actor(h),db,clock);if(failure is not null)return failure;
                now=row.UpdatedAtUtc;
            }
            else
            {
            if(row is not null&&await db.StaffWorkPeriods.AnyAsync(p=>p.AttendanceId==row.Id))return Conflict("أضيفت فترات محفوظة إلى هذا اليوم؛ أعد قراءة الفترة المحددة وأرسل طلبًا جديدًا.");
            if (r.Operation == "CheckIn" && await db.StaffDailyAttendances.AnyAsync(a => a.AcademyId == t.AcademyId && a.MembershipId == r.MembershipId && a.WorkDate != r.WorkDate && a.Status == "Present" && a.CheckIn != null && a.CheckOut == null)) return Conflict("توجد فترة حضور مفتوحة في يوم آخر؛ راجعها أولًا دون اختلاق انصراف.");
            var localClaim = TimeZoneInfo.ConvertTime(r.ClaimedAtUtc, await Zone(db, t.AcademyId));
            var checkoutDate = DateOnly.FromDateTime(localClaim.DateTime);
            if (r.Operation == "CheckIn" && checkoutDate != r.WorkDate || r.Operation == "CheckOut" && checkoutDate < r.WorkDate) return Conflict("تغير توقيت الأكاديمية ويوم الطلب؛ راجع السياق قبل تسجيل الحضور.");
            var time = TimeOnly.FromDateTime(localClaim.DateTime);
            if (r.Operation == "CheckIn" && row is not null && (row.Status != "NotRecorded" || row.CheckIn.HasValue || row.CheckOut.HasValue)) return Conflict("لهذا اليوم سجل قائم؛ لا يستبدله طلب استثناء. راجع السجل الإداري.");
            if (r.Operation == "CheckOut" && (row?.Status != "Present" || row.CheckIn is null || row.CheckOut is not null || checkoutDate == r.WorkDate && time < row.CheckIn)) return Conflict("الانصراف يحتاج حضورًا مفتوحًا ووقتًا لاحقًا للحضور.");
            if (r.Operation == "CheckOut" && r.TargetAttendanceId.HasValue && (row!.Id != r.TargetAttendanceId || row.Version != r.TargetAttendanceVersion)) return Conflict("تغير سجل الحضور الذي بُني عليه الطلب؛ أعد قراءته وأرسل طلبًا جديدًا بدل تعديل مصدر مختلف.");
            if (await StaffAttendanceIntervals.Overlaps(db,t.AcademyId,r.MembershipId,row?.Id,r.WorkDate,r.Operation == "CheckIn" ? time : row!.CheckIn!.Value,r.Operation == "CheckOut" ? checkoutDate : null,r.Operation == "CheckOut" ? time : null)) return Conflict("تتداخل الفترة مع سجل حضور آخر؛ لا يعتمد الطلب قبل مراجعة المصدر.");
            var before = Snapshot(row);
            now = clock.GetUtcNow(); // Decision/application time after the source locks, not the time spent waiting for them.
            if (row is null) { row = new StaffDailyAttendance { Id = Guid.NewGuid(), AcademyId = t.AcademyId, MembershipId = r.MembershipId, WorkDate = r.WorkDate, Status = "Present", CreatedAtUtc = now }; db.StaffDailyAttendances.Add(row); }
            row.Status = "Present"; if (r.Operation == "CheckIn") row.CheckIn = time; else { row.CheckOut = time; row.CheckOutDate = checkoutDate; }
            row.RecordedByUserId = Actor(h); row.UpdatedAtUtc = now;
            // Never approve a deduction, create a commission, or mark a training session held.
            // Persist the attendance first inside this transaction so the DB decision guard
            // can verify its exact applied source; a later failure rolls both writes back.
            await db.SaveChangesAsync();
            r.BeforeSnapshot = before; r.AttendanceId = row.Id; r.AfterSnapshot = Snapshot(row);
            }
        }
        r.Status = body.Approve ? "Approved" : "Rejected"; r.ReviewNote = note; r.ReviewedByUserId = Actor(h); r.ReviewedByMembershipId = t.MembershipId; r.ReviewedAtUtc = now; r.ReviewKey = key; r.ReviewHash = hash; r.UpdatedAtUtc = now;
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); }
        catch (DbUpdateException ex) when (DuplicateCommand(ex)) { return Conflict("مفتاح المراجعة مستخدم من قبل؛ لم يسجل قرار جديد."); }
        catch(DbUpdateConcurrencyException){return Conflict("تغير المصدر أثناء القرار؛ أعد قراءة الطلب.");}
        catch(DbUpdateException ex)when(ex.InnerException is PostgresException{SqlState:"23514" or "23505"}){return Conflict("تعارض في الفترة أو دليل القرار؛ لم يحفظ حضور جديد.");}
        catch(PostgresException ex)when(ex.SqlState=="23514"){return Conflict("تعارض في الفترة أو دليل القرار؛ لم يحفظ حضور جديد.");}
        return Results.Ok(View(r));
    }
}
public sealed record StaffExceptionWrite(Guid BranchId, string Operation, DateTimeOffset ClaimedAtUtc, string? Reason, Guid? TargetAttendanceId = null,Guid? TargetWorkPeriodId=null,uint? TargetWorkPeriodVersion=null);
public sealed record StaffExceptionReview(bool Approve, string? Note, uint Version);
