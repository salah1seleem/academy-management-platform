using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Facilities;

public sealed record FacilityWrite(string Name, Guid BranchId);
public sealed record SpaceWrite(string Name, Guid FacilityId, Guid SportId, FacilitySpaceKind Kind);
public sealed record ResourceWrite(string Name, Guid SpaceId, int Capacity, bool Shared, int[] Days, TimeOnly OpensAt, TimeOnly ClosesAt, string[] ServiceKinds);
public sealed record CapacityApproval(string Evidence, bool QualifiedResponsibilityConfirmed);
public sealed record BookingWrite(Guid ResourceId, DateOnly Date, TimeOnly Start, TimeOnly End, int Seats, bool Exclusive, string ServiceKind, Guid? GroupId = null, Guid? CoachId = null, Guid? SessionId = null);
public sealed record ClosureWrite(DateOnly From, DateOnly To, TimeOnly Start, TimeOnly End, string? Reason = null);
public sealed class FacilityRejected(string message, int status = 409) : Exception(message) { public int Status { get; } = status; }

public static class FacilityEndpoints
{
    public static void MapFacilities(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/facilities").RequireAuthorization(AcademyPermissions.FacilitiesRead);
        api.MapGet("/options", async (FoundationDbContext db) => Results.Ok(new {
            branches = await db.Branches.Where(x => x.IsActive).Select(x => new { x.Id, name = x.ArabicName }).ToListAsync(),
            sports = await db.Sports.Where(x => x.IsActive).Select(x => new { x.Id, name = x.ArabicName }).ToListAsync(),
            groups = await db.TrainingGroups.Where(x => x.IsActive && x.Branch.IsActive && x.Sport.IsActive).Select(x => new { x.Id, name = x.ArabicName, x.BranchId, x.SportId }).ToListAsync(),
            coaches = await db.StaffGroupAssignments.Where(x => x.IsActive && x.TrainingGroup.IsActive && x.AcademyMembership.IsActive && x.AcademyMembership.Role == AcademyRole.Coach && x.AcademyMembership.User.IsActive).Select(x => new { id = x.AcademyMembershipId, name = x.AcademyMembership.User.DisplayName, groupId = x.TrainingGroupId }).Distinct().ToListAsync()
        }));
        api.MapGet("/", async (FoundationDbContext db) => Results.Ok(new {
            facilities = await db.Facilities.AsNoTracking().OrderBy(x => x.Name).ToListAsync(),
            spaces = await db.FacilitySpaces.AsNoTracking().OrderBy(x => x.Name).ToListAsync(),
            resources = await db.FacilityResources.AsNoTracking().OrderBy(x => x.Name).ToListAsync()
        }));
        api.MapGet("/board", Board);
        api.MapPost("/", async (FacilityWrite body, FacilityService service) => await Run(() => service.Create(body))).RequireAuthorization(AcademyPermissions.FacilitiesManage).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/spaces", async (SpaceWrite body, FacilityService service) => await Run(() => service.Create(body))).RequireAuthorization(AcademyPermissions.FacilitiesManage).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/resources", async (ResourceWrite body, FacilityService service) => await Run(() => service.Create(body))).RequireAuthorization(AcademyPermissions.FacilitiesManage).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/resources/{id:guid}/approve", async (Guid id, CapacityApproval body, FacilityService service) => await Run(() => service.Approve(id, body))).RequireAuthorization(AcademyPermissions.FacilitiesManage).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/bookings", async (BookingWrite body, HttpRequest request, FacilityService service) => await Run(() => service.Book(body, request.Headers["Idempotency-Key"].ToString()))).RequireAuthorization(AcademyPermissions.FacilitiesManage).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/bookings/{id:guid}/cancel", async (Guid id, FacilityService service) => await Run(() => service.Cancel(id))).RequireAuthorization(AcademyPermissions.FacilitiesManage).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/resources/{id:guid}/closures", async (Guid id, ClosureWrite body, FacilityService service) => await Run(() => service.Close(id, body))).RequireAuthorization(AcademyPermissions.FacilitiesManage).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Run(Func<Task<object>> operation)
    {
        try { return Results.Ok(await operation()); }
        catch (FacilityRejected e) { return Results.Problem(statusCode: e.Status, title: e.Message); }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Problem(statusCode: 409, title: "الاسم أو معرف العملية مستخدم. راجع السجل الموجود بدل تكراره."); }
    }
    private static async Task<IResult> Board(DateOnly? date, Guid? branchId, FoundationDbContext db, CurrentTenant tenant, TimeProvider time)
    {
        var t = (await tenant.ResolveAsync())!; var a = await db.Academies.SingleAsync(x => x.Id == t.AcademyId);
        var day = date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(a.TimeZone)).DateTime);
        var query = db.ResourceBookings.AsNoTracking().Where(b => b.Date == day && b.State == ResourceBookingState.Reserved);
        if (branchId.HasValue) query = query.Where(b => b.BranchId == branchId);
        var bookings = await (from b in query
            join r in db.FacilityResources on b.ResourceId equals r.Id
            join s in db.FacilitySpaces on r.FacilitySpaceId equals s.Id
            join f in db.Facilities on s.FacilityId equals f.Id
            join g in db.TrainingGroups on b.TrainingGroupId equals g.Id into groups from g in groups.DefaultIfEmpty()
            join c in db.AcademyMemberships on b.CoachMembershipId equals c.Id into coaches from c in coaches.DefaultIfEmpty()
            orderby b.StartTime, r.Name
            select new { b.Id, b.ResourceId, resource = r.Name, space = s.Name, facility = f.Name, b.Date, b.StartTime, b.EndTime, b.ReservedSeats, r.Capacity, b.Exclusive, b.ServiceKind, b.TrainingGroupId, groupName = g == null ? null : g.ArabicName, coach = c == null ? null : c.User.DisplayName, b.TrainingSessionId,
                expectedPlayers = b.TrainingGroupId.HasValue ? db.SportEnrollments.Count(e => e.IsActive && e.Status == EnrollmentStatus.Active && e.TrainingGroupId == b.TrainingGroupId) : 0,
                present = b.TrainingSessionId.HasValue ? db.PlayerAttendances.Count(p => p.TrainingSessionId == b.TrainingSessionId && p.Status == AttendanceStatus.Present) : 0,
                absent = b.TrainingSessionId.HasValue ? db.PlayerAttendances.Count(p => p.TrainingSessionId == b.TrainingSessionId && p.Status == AttendanceStatus.Absent) : 0
            }).ToListAsync();
        return Results.Ok(new { date = day, refreshedAtUtc = time.GetUtcNow(), bookings, closures = await db.ResourceClosures.AsNoTracking().Where(c => c.FromDate <= day && c.ToDate >= day && (!branchId.HasValue || c.BranchId == branchId)).ToListAsync() });
    }
}

public sealed class FacilityService(FoundationDbContext db, CurrentTenant tenant, IHttpContextAccessor http, TimeProvider time)
{
    private async Task<(TenantMembership T, Guid Actor)> Authority(string action)
    {
        var t = await tenant.ResolveAsync() ?? throw new FacilityRejected("يلزم تسجيل الدخول من جديد.", 401);
        var actor = Guid.Parse(http.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"}, 0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + t.AcademyId}, 0))");
        var member = await db.AcademyMemberships.SingleOrDefaultAsync(x => x.Id == t.MembershipId && x.IsActive && x.User.IsActive && x.Academy.IsActive);
        var payload = await db.AcademyConfigurationVersions.AsNoTracking().Where(x => x.State == ConfigurationState.Active).Select(x => x.Payload).SingleOrDefaultAsync();
        if (member is null || member.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin) || payload is null || !AcademyRuntimeSettings.Read(payload).Features.GetValueOrDefault("facilities") || !await db.AcademyFeatureEntitlements.AnyAsync(x => x.Feature == "facilities" && x.IsActive && x.Enabled)) throw new FacilityRejected("العملية غير متاحة بصلاحياتك وإعدادات الأكاديمية الحالية.", 403);
        if (member.Role == AcademyRole.AcademyAdmin)
        {
            var grant = await db.StaffActionGrants.SingleOrDefaultAsync(x => x.MembershipId == member.Id && x.Module == "facilities" && x.IsActive);
            var allowed = member.UseActionGrants ? grant?.Read == true && (action == "Add" ? grant.Add : grant.Edit)
                : await db.StaffGrants.AnyAsync(x => x.MembershipId == member.Id && x.Permission == AcademyPermissions.FacilitiesManage && x.IsActive && x.Allowed);
            if (!allowed) throw new FacilityRejected("صلاحية إدارة المرافق غير متاحة.", 403);
        }
        return (t, actor);
    }
    private static void Name(string value, int max = 120) { if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max) throw new FacilityRejected("أدخل اسمًا واضحًا ضمن الحد المسموح.", 422); }
    private async Task Branch(Guid id)
    {
        var member = await db.AcademyMemberships.AsNoTracking().SingleAsync(x => x.Id == db.RequestMembershipId);
        if (member.Role == AcademyRole.AcademyAdmin && !member.AllBranches && !await db.MembershipBranchAccesses.AnyAsync(x => x.MembershipId == member.Id && x.BranchId == id && x.IsActive)) throw new FacilityRejected("الفرع خارج صلاحياتك الحالية.", 403);
    }
    private async Task<DateOnly> Today() { var academy = await db.Academies.SingleAsync(x => x.Id == db.RequestAcademyId); return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(academy.TimeZone)).DateTime); }
    private async Task<FacilityResource> Lock(Guid id)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"facility-resource:{db.RequestAcademyId}:{id}"}, 0))");
        var r = await db.FacilityResources.FromSqlInterpolated($"SELECT *, xmin FROM \"FacilityResources\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync() ?? throw new FacilityRejected("المورد غير متاح.", 404);
        await Branch(r.BranchId); return r;
    }
    public async Task<object> Create(FacilityWrite b)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var (t, _) = await Authority("Add"); Name(b.Name);
        if (!await db.Branches.AnyAsync(x => x.Id == b.BranchId && x.IsActive)) throw new FacilityRejected("اختر فرعًا متاحًا.", 422);
        await Branch(b.BranchId);
        var row = new Facility { Id = Guid.NewGuid(), AcademyId = t.AcademyId, BranchId = b.BranchId, Name = b.Name.Trim(), CreatedAtUtc = time.GetUtcNow(), UpdatedAtUtc = time.GetUtcNow() };
        db.Facilities.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return row;
    }
    public async Task<object> Create(SpaceWrite b)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var (t, _) = await Authority("Add"); Name(b.Name);
        var f = await db.Facilities.SingleOrDefaultAsync(x => x.Id == b.FacilityId && x.IsActive) ?? throw new FacilityRejected("اختر مرفقًا متاحًا.", 422);
        await Branch(f.BranchId);
        if (!Enum.IsDefined(b.Kind) || !await db.Sports.AnyAsync(x => x.Id == b.SportId && x.IsActive)) throw new FacilityRejected("اختر الرياضة ونوع المساحة.", 422);
        var row = new FacilitySpace { Id = Guid.NewGuid(), AcademyId = t.AcademyId, FacilityId = f.Id, BranchId = f.BranchId, SportId = b.SportId, Name = b.Name.Trim(), Kind = b.Kind, CreatedAtUtc = time.GetUtcNow(), UpdatedAtUtc = time.GetUtcNow() };
        db.FacilitySpaces.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return row;
    }
    public async Task<object> Create(ResourceWrite b)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var (t, _) = await Authority("Add"); Name(b.Name, 60);
        var space = await db.FacilitySpaces.SingleOrDefaultAsync(x => x.Id == b.SpaceId && x.IsActive && db.Facilities.Any(f => f.Id == x.FacilityId && f.IsActive)) ?? throw new FacilityRejected("اختر مساحة متاحة.", 422);
        await Branch(space.BranchId);
        if (b.Capacity is < 1 or > 10000 || b.OpensAt >= b.ClosesAt || b.Days is null || b.Days.Length is < 1 or > 7 || b.Days.Any(d => d is < 0 or > 6) || b.Days.Distinct().Count() != b.Days.Length || b.ServiceKinds is null || b.ServiceKinds.Length is < 1 or > 5 || b.ServiceKinds.Distinct().Count() != b.ServiceKinds.Length || b.ServiceKinds.Any(k => !Enum.GetNames<AcademyServiceKind>().Contains(k))) throw new FacilityRejected("راجع السعة وأيام التشغيل والتوقيت وأنواع الخدمة.", 422);
        var row = new FacilityResource { Id = Guid.NewGuid(), AcademyId = t.AcademyId, BranchId = space.BranchId, FacilitySpaceId = space.Id, Name = b.Name.Trim(), Capacity = b.Capacity, SharedBookingsAllowed = b.Shared, OperatingDays = b.Days, OpensAt = b.OpensAt, ClosesAt = b.ClosesAt, ServiceKinds = b.ServiceKinds, CreatedAtUtc = time.GetUtcNow(), UpdatedAtUtc = time.GetUtcNow() };
        db.FacilityResources.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return row;
    }
    public async Task<object> Approve(Guid id, CapacityApproval b)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var (t, actor) = await Authority("Edit");
        if (t.Role != AcademyRole.AcademyOwner) throw new FacilityRejected("اعتماد سياسة السعة للمسؤول المالك فقط.", 403);
        if (!b.QualifiedResponsibilityConfirmed || string.IsNullOrWhiteSpace(b.Evidence) || b.Evidence.Length > 500) throw new FacilityRejected("أكد مراجعة المسؤول المؤهل وأدخل مرجع اعتماد السعة والإشراف.", 422);
        var r = await Lock(id); if (!r.ApprovedByUserId.HasValue) { r.ApprovedByUserId = actor; r.ApprovedAtUtc = time.GetUtcNow(); r.ApprovalEvidence = b.Evidence.Trim(); r.UpdatedAtUtc = time.GetUtcNow(); await db.SaveChangesAsync(); }
        await tx.CommitAsync(); return r;
    }
    public async Task<object> Book(BookingWrite b, string key)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var (t, actor) = await Authority("Add");
        if (key.Length is < 8 or > 100) throw new FacilityRejected("تعذر تحديد عملية الحجز. حاول من نفس الصفحة.", 422);
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(b)));
        var prior = await db.ResourceBookings.SingleOrDefaultAsync(x => x.ActorUserId == actor && x.RequestKey == key);
        if (prior is not null) { if (prior.RequestHash != hash) throw new FacilityRejected("معرف الحجز مرتبط بتفاصيل أخرى."); return prior; }
        var r = await Lock(b.ResourceId); var space = await db.FacilitySpaces.SingleAsync(x => x.Id == r.FacilitySpaceId);
        var today = await Today();
        if (!r.IsActive || !space.IsActive || !await db.Facilities.AnyAsync(x => x.Id == space.FacilityId && x.IsActive) || !await db.Branches.AnyAsync(x => x.Id == r.BranchId && x.IsActive) || !await db.Sports.AnyAsync(x => x.Id == space.SportId && x.IsActive) || !r.ApprovedByUserId.HasValue) throw new FacilityRejected("المورد غير مفعل أو سياسة سعته لم تعتمد بعد.");
        if (b.Date < today || b.Date > today.AddDays(366) || b.Start < r.OpensAt || b.End > r.ClosesAt || b.Start >= b.End || !r.OperatingDays.Contains((int)b.Date.DayOfWeek) || !r.ServiceKinds.Contains(b.ServiceKind)) throw new FacilityRejected("الحجز خارج فترة أو نوع تشغيل المورد المسموح.", 422);
        if (await db.ResourceClosures.AnyAsync(c => c.ResourceId == r.Id && c.IsActive && c.FromDate <= b.Date && c.ToDate >= b.Date && c.StartTime < b.End && b.Start < c.EndTime)) throw new FacilityRejected("المورد مغلق في الموعد المختار.");
        if (b.ServiceKind == "Training")
        {
            var group = await db.TrainingGroups.SingleOrDefaultAsync(g => g.Id == b.GroupId && g.IsActive && g.BranchId == r.BranchId && g.SportId == space.SportId) ?? throw new FacilityRejected("اختر مجموعة نشطة من نفس الفرع والرياضة.", 422);
            if (await db.SportEnrollments.CountAsync(e => e.TrainingGroupId == group.Id && e.IsActive && e.Status == EnrollmentStatus.Active) > b.Seats) throw new FacilityRejected("المقاعد المحجوزة أقل من أعضاء المجموعة؛ راجع توزيع الحارات.");
            if (!b.CoachId.HasValue || !await db.StaffGroupAssignments.AnyAsync(a => a.TrainingGroupId == group.Id && a.AcademyMembershipId == b.CoachId && a.IsActive && a.AcademyMembership.IsActive && a.AcademyMembership.Role == AcademyRole.Coach && a.AcademyMembership.User.IsActive)) throw new FacilityRejected("اختر مدربًا مسندًا لهذه المجموعة.", 422);
        }
        else if (b.GroupId.HasValue || b.SessionId.HasValue) throw new FacilityRejected("حجز الخدمة غير التدريبية لا يرتبط بتسجيل تدريب.", 422);
        if (b.SessionId.HasValue && !await db.TrainingSessions.AnyAsync(s => s.Id == b.SessionId && s.TrainingGroupId == b.GroupId && s.Status != TrainingSessionStatus.Cancelled && s.SessionDate == b.Date && s.StartTime == b.Start && s.EndTime == b.End)) throw new FacilityRejected("بيانات الحصة لا تطابق الحجز.", 422);
        if (b.CoachId.HasValue)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"facility-coach:{t.AcademyId}:{b.CoachId}"}, 0))");
            if (!await db.AcademyMemberships.AnyAsync(m => m.Id == b.CoachId && m.IsActive && m.User.IsActive && m.Role == AcademyRole.Coach)) throw new FacilityRejected("المدرب غير متاح.", 422);
            if (await db.ResourceBookings.AnyAsync(x => x.CoachMembershipId == b.CoachId && x.State == ResourceBookingState.Reserved && x.Date == b.Date && x.StartTime < b.End && b.Start < x.EndTime)
                || await db.TrainingSessions.AnyAsync(s => s.Id != b.SessionId && s.SessionDate == b.Date && s.Status != TrainingSessionStatus.Cancelled && s.StartTime < b.End && b.Start < s.EndTime && db.StaffGroupAssignments.Any(a => a.TrainingGroupId == s.TrainingGroupId && a.AcademyMembershipId == b.CoachId && a.IsActive))) throw new FacilityRejected("للمدرب حصة أو حجز آخر في هذا الموعد.");
        }
        var slots = await db.ResourceBookings.Where(x => x.ResourceId == r.Id && x.Date == b.Date && x.State == ResourceBookingState.Reserved && x.StartTime < b.End && b.Start < x.EndTime).Select(x => new BookingCapacity.Slot(x.StartTime, x.EndTime, x.ReservedSeats, x.Exclusive)).ToListAsync();
        if (!BookingCapacity.Fits(r.Capacity, r.SharedBookingsAllowed, new(b.Start, b.End, b.Seats, b.Exclusive), slots)) throw new FacilityRejected("الموعد متعارض أو السعة المتبقية لا تكفي. اختر موردًا أو موعدًا آخر.");
        var row = new ResourceBooking { Id = Guid.NewGuid(), AcademyId = t.AcademyId, BranchId = r.BranchId, ResourceId = r.Id, TrainingGroupId = b.GroupId, TrainingSessionId = b.SessionId, CoachMembershipId = b.CoachId, Date = b.Date, StartTime = b.Start, EndTime = b.End, ReservedSeats = b.Seats, ApprovedCapacitySnapshot = r.Capacity, Exclusive = b.Exclusive, ServiceKind = b.ServiceKind, State = ResourceBookingState.Reserved, ActorUserId = actor, RequestKey = key, RequestHash = hash, CreatedAtUtc = time.GetUtcNow(), UpdatedAtUtc = time.GetUtcNow() };
        db.ResourceBookings.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return row;
    }
    public async Task<object> Cancel(Guid id)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var (_, actor) = await Authority("Edit");
        var row = await db.ResourceBookings.SingleOrDefaultAsync(x => x.Id == id) ?? throw new FacilityRejected("الحجز غير متاح.", 404); await Lock(row.ResourceId);
        // Re-read after the resource lock; retries do not rewrite the first cancellation.
        await db.Entry(row).ReloadAsync(); if (row.Date < await Today()) throw new FacilityRejected("الحجوزات التاريخية لا تعدل من هذه الشاشة.");
        if (row.State != ResourceBookingState.Cancelled) { row.State = ResourceBookingState.Cancelled; row.CancelledByUserId = actor; row.CancelledAtUtc = time.GetUtcNow(); row.UpdatedAtUtc = time.GetUtcNow(); await db.SaveChangesAsync(); }
        await tx.CommitAsync(); return row;
    }
    public async Task<object> Close(Guid id, ClosureWrite b)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var (t, actor) = await Authority("Add"); var r = await Lock(id);
        if (b.From < await Today() || b.From > b.To || b.To.DayNumber - b.From.DayNumber > 366 || b.Start >= b.End || b.Reason?.Length > 500) throw new FacilityRejected("راجع فترة وتوقيت الإغلاق.", 422);
        if (await db.ResourceBookings.AnyAsync(x => x.ResourceId == id && x.State == ResourceBookingState.Reserved && x.Date >= b.From && x.Date <= b.To && x.StartTime < b.End && b.Start < x.EndTime)) throw new FacilityRejected("توجد حجوزات متأثرة. انقلها أو ألغها صراحة أولًا؛ لا نسجل غياب أطفال بسبب إغلاق المورد.");
        var row = new ResourceClosure { Id = Guid.NewGuid(), AcademyId = t.AcademyId, BranchId = r.BranchId, ResourceId = r.Id, FromDate = b.From, ToDate = b.To, StartTime = b.Start, EndTime = b.End, Reason = b.Reason?.Trim(), ActorUserId = actor, CreatedAtUtc = time.GetUtcNow(), UpdatedAtUtc = time.GetUtcNow() };
        db.ResourceClosures.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return row;
    }
}
