using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Academy.Api.Operations;

public static class StaffWorkPeriods
{
    public const string OwnPath="/api/v1/staff/work-periods";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public static void MapStaffWorkPeriods(this WebApplication app)
    {
        var g=app.MapGroup("/api/v1/manage/staff-attendance").RequireAuthorization(AcademyPermissions.AttendanceManage);
        g.MapGet("/{id:guid}/periods",Read);
        g.MapPost("/{id:guid}/periods",(Guid id,WorkPeriodWrite body,HttpContext h,CurrentTenant t,FoundationDbContext db,TimeProvider c,IAuthorizationService a)=>Write(id,null,body,h,t,db,c,a)).AddEndpointFilter<CsrfFilter>();
        g.MapPut("/{id:guid}/periods/{periodId:guid}",(Guid id,Guid periodId,WorkPeriodWrite body,HttpContext h,CurrentTenant t,FoundationDbContext db,TimeProvider c,IAuthorizationService a)=>Write(id,periodId,body,h,t,db,c,a)).AddEndpointFilter<CsrfFilter>();
        app.MapGet(OwnPath,Own).RequireAuthorization(AcademyPermissions.TenantAccess);
    }
    private static IResult Conflict(string message)=>Results.Conflict(new{message});
    private static IResult Invalid(string message)=>Results.UnprocessableEntity(new{message});
    private static Task Lock(FoundationDbContext db,string key)=>db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))");
    internal static bool TryUtc(DateOnly day,TimeOnly time,TimeZoneInfo zone,out DateTimeOffset value)
    {
        var local=day.ToDateTime(time,DateTimeKind.Unspecified); value=default;
        if(zone.IsInvalidTime(local)||zone.IsAmbiguousTime(local)) return false;
        value=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local,zone),TimeSpan.Zero); return true;
    }
    internal static long? Seconds(StaffWorkPeriod p)=>p.StartUtc.HasValue&&p.EndUtc.HasValue?(long)(p.EndUtc.Value-p.StartUtc.Value).TotalSeconds:null;
    internal static object Statistics(StaffWorkPeriod[] periods)=>new{periodCount=periods.Length,knownWorkedSeconds=periods.Sum(p=>Seconds(p)??0),totalWorkedSeconds=periods.Length>0&&periods.All(p=>Seconds(p).HasValue)?periods.Sum(p=>Seconds(p)):(long?)null};
    internal static object View(StaffWorkPeriod p)=>new {id=p.Id==Guid.Empty?(Guid?)null:p.Id,p.Version,p.StartDate,p.StartTime,p.EndDate,p.EndTime,p.StartUtc,p.EndUtc,p.TimeZoneId,p.TrustSource,workedSeconds=Seconds(p)};
    internal static StaffWorkPeriod Legacy(StaffDailyAttendance r)=>new(){AcademyId=r.AcademyId,AttendanceId=r.Id,MembershipId=r.MembershipId,StartDate=r.WorkDate,StartTime=r.CheckIn!.Value,EndDate=r.CheckOut.HasValue?r.CheckOutDate??r.WorkDate:null,EndTime=r.CheckOut,TrustSource="LegacyLocal",RecordedByUserId=r.RecordedByUserId,CreatedAtUtc=r.CreatedAtUtc,UpdatedAtUtc=r.UpdatedAtUtc};
    internal static string Snapshot(StaffDailyAttendance row,IReadOnlyCollection<StaffWorkPeriod> periods,string? reason=null)=>JsonSerializer.Serialize(new {row.Id,row.Version,row.WorkDate,row.Status,row.CheckIn,row.CheckOut,row.CheckOutDate,row.LateMinutes,row.SuggestedDeduction,row.ApprovedDeduction,row.DeductionReason,row.RecordedByUserId,row.UpdatedAtUtc,periods=periods.OrderBy(p=>p.StartTime).ThenBy(p=>p.Id).Select(View),changeReason=reason},Json);
    private static async Task<IResult> Read(Guid id,DateOnly? date,CurrentTenant tenant,FoundationDbContext db,TimeProvider clock)
    {
        var t=(await tenant.ResolveAsync())!; if(t.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin))return Results.Forbid();
        if(!await db.AcademyMemberships.AnyAsync(m=>m.Id==id&&m.AcademyId==t.AcademyId&&m.Role==AcademyRole.Coach&&m.IsActive))return Results.NotFound();
        return await ReadDay(id,date,t,db,clock,true);
    }
    private static async Task<IResult> Own(DateOnly? date,CurrentTenant tenant,FoundationDbContext db,TimeProvider clock)
    {
        var t=(await tenant.ResolveAsync())!; if(t.Role==AcademyRole.Guardian)return Results.Forbid();
        if(!await db.Employees.AnyAsync(e=>e.AcademyId==t.AcademyId&&e.MembershipId==t.MembershipId&&e.IsActive))return Results.Ok(new{configured=false});
        return await ReadDay(t.MembershipId,date,t,db,clock,false);
    }
    private static async Task<IResult> ReadDay(Guid member,DateOnly? date,TenantMembership t,FoundationDbContext db,TimeProvider clock,bool management)
    {
        var today=await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock);var day=date??today;
        if(day.Year<2000||day>today)return Invalid("اختر يومًا صحيحًا غير مستقبلي.");
        var row=await db.StaffDailyAttendances.AsNoTracking().SingleOrDefaultAsync(r=>r.AcademyId==t.AcademyId&&r.MembershipId==member&&r.WorkDate==day);
        var periods=row is null?[]:await db.StaffWorkPeriods.AsNoTracking().Where(p=>p.AcademyId==t.AcademyId&&p.AttendanceId==row.Id).OrderBy(p=>p.StartTime).ThenBy(p=>p.Id).ToArrayAsync();
        var managed=periods.Length>0;
        if(!managed&&row?.Status=="Present"&&row.CheckIn.HasValue)periods=[Legacy(row)];
        var seconds=periods.Select(Seconds).ToArray();
        var zone=await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>a.TimeZone).SingleAsync();
        var canEdit=management&&(t.Role==AcademyRole.AcademyOwner||!await db.AcademyMemberships.AnyAsync(m=>m.Id==t.MembershipId&&m.UseActionGrants)||await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.IsActive&&g.Module=="staff"&&g.Read&&g.Edit));
        return Results.Ok(new{configured=true,workDate=day,today,timeZone=zone,version=row?.Version??0,hasPreservedPeriods=managed,canEdit,items=periods.Select(View),knownWorkedSeconds=seconds.Sum(s=>s??0),totalWorkedSeconds=seconds.Length>0&&seconds.All(s=>s.HasValue)?seconds.Sum():(long?)null});
    }
    private static bool Overlap(StaffWorkPeriod a,StaffWorkPeriod b)
    {
        if(a.StartUtc.HasValue&&b.StartUtc.HasValue)return a.StartUtc<(b.EndUtc??DateTimeOffset.MaxValue)&&b.StartUtc<(a.EndUtc??DateTimeOffset.MaxValue);
        var af=a.StartDate.ToDateTime(a.StartTime);var bf=b.StartDate.ToDateTime(b.StartTime);
        var ae=a.EndTime.HasValue?a.EndDate!.Value.ToDateTime(a.EndTime.Value):DateTime.MaxValue;var be=b.EndTime.HasValue?b.EndDate!.Value.ToDateTime(b.EndTime.Value):DateTime.MaxValue;
        return af<be&&bf<ae;
    }
    private static async Task<IResult> Write(Guid member,Guid? periodId,WorkPeriodWrite body,HttpContext h,CurrentTenant tenant,FoundationDbContext db,TimeProvider clock,IAuthorizationService authorization)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role is not(AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin))return Results.Forbid();
        var actor=Guid.Parse(h.User.FindFirstValue(ClaimTypes.NameIdentifier)!);var key=h.Request.Headers["Idempotency-Key"].ToString();var reason=body.Reason?.Trim();
        if(key.Length is <8 or >100 || body.Date.Year<2000 || body.EndTime.HasValue!=body.EndDate.HasValue || reason?.Length>500 || periodId.HasValue&&reason?.Length is not(>=5 and <=500))return Invalid("أدخل فترة صحيحة؛ سبب تصحيح الفترة المحفوظة مطلوب من خمسة أحرف على الأقل.");
        var zoneId=await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>a.TimeZone).SingleAsync();var zone=TimeZoneInfo.FindSystemTimeZoneById(zoneId);var now=clock.GetUtcNow();
        if(!TryUtc(body.Date,body.StartTime,zone,out var start)||start>now)return Invalid("وقت البداية مستقبلي أو ملتبس بسبب تغيير الساعة؛ اختر وقتًا واضحًا بتوقيت الأكاديمية.");
        DateTimeOffset? end=null;
        if(body.EndTime.HasValue){if(body.EndDate<body.Date||!TryUtc(body.EndDate!.Value,body.EndTime.Value,zone,out var until)||until<=start||until>now)return Invalid("وقت النهاية غير صحيح أو مستقبلي أو ملتبس بتوقيت الأكاديمية.");end=until;}
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{action="WorkPeriod",member,periodId,membership=t.MembershipId,body},Json))));
        await using var tx=await db.Database.BeginTransactionAsync();await Lock(db,$"grants:{t.AcademyId}:{t.MembershipId}");
        var live=await db.AcademyMemberships.AsNoTracking().SingleOrDefaultAsync(m=>m.AcademyId==t.AcademyId&&m.Id==t.MembershipId&&m.UserId==actor&&m.IsActive&&m.User.IsActive&&m.Role==t.Role);
        if(live is null||!(await authorization.AuthorizeAsync(h.User,h,AcademyPermissions.AttendanceManage)).Succeeded||t.Role==AcademyRole.AcademyAdmin&&live.UseActionGrants&&!await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==live.Id&&g.IsActive&&g.Module=="staff"&&g.Read&&g.Edit))return Results.Forbid();
        if(!await db.AcademyMemberships.AnyAsync(m=>m.AcademyId==t.AcademyId&&m.Id==member&&m.Role==AcademyRole.Coach&&m.IsActive))return Results.NotFound();
        if(t.Role==AcademyRole.AcademyAdmin&&!live.AllBranches&&!await db.Employees.AnyAsync(e=>e.MembershipId==member&&db.MembershipBranchAccesses.Any(a=>a.MembershipId==t.MembershipId&&a.IsActive&&a.BranchId==e.BranchId))&&!await db.StaffGroupAssignments.AnyAsync(s=>s.AcademyMembershipId==member&&s.IsActive&&s.TrainingGroup.IsActive&&db.MembershipBranchAccesses.Any(a=>a.MembershipId==t.MembershipId&&a.IsActive&&a.BranchId==s.TrainingGroup.BranchId)))return Results.NotFound();
        await Lock(db,$"staff-manual-key:{t.AcademyId}:{actor}:{key}");
        var replay=await db.StaffAttendanceChanges.AsNoTracking().SingleOrDefaultAsync(c=>c.AcademyId==t.AcademyId&&c.ActorUserId==actor&&c.RequestKey==key);
        if(replay is not null)return replay.RequestHash==hash?Results.Ok(JsonSerializer.Deserialize<JsonElement>(replay.AfterSnapshot)):Conflict("مفتاح الحفظ مستخدم لأمر مختلف؛ أعد تحميل المصدر.");
        await Lock(db,$"staff-open:{t.AcademyId}:{member}");await Lock(db,$"staff-day:{t.AcademyId}:{member}:{body.Date}");
        var row=await db.StaffDailyAttendances.SingleOrDefaultAsync(r=>r.AcademyId==t.AcademyId&&r.MembershipId==member&&r.WorkDate==body.Date);
        if(row is null||row.Status!="Present"||row.CheckIn is null)return Conflict("سجل الحضور الأول لهذا اليوم من نموذج الحضور، ثم أضف الفترات التالية.");
        if(row.Version!=body.Version)return Conflict("تغير يوم الحضور؛ أعد تحميل الفترات قبل الحفظ.");
        var employee=await db.Employees.SingleOrDefaultAsync(e=>e.AcademyId==t.AcademyId&&e.MembershipId==member);
        if(employee is not null){await PayrollEndpoints.Lock(db,t.AcademyId,employee.Id,body.Date);var terms=await ContractHistory.Bounds(db,employee,body.Date);if(!employee.IsActive||body.Date<terms.ContractStart||terms.ContractEnd.HasValue&&body.Date>terms.ContractEnd)return Conflict("الفترة خارج مدة عمل الموظف الفعال.");if(await PayrollEndpoints.Finalized(db,t.AcademyId,employee.Id,body.Date))return Conflict("شهر الموظف معتمد أو مسدد؛ لا يصحح مصدر حضوره صامتًا.");}
        var periods=await db.StaffWorkPeriods.Where(p=>p.AcademyId==t.AcademyId&&p.AttendanceId==row.Id).OrderBy(p=>p.StartTime).ThenBy(p=>p.Id).ToListAsync();
        if(periods.Any(p=>p.TimeZoneId is not null&&p.TimeZoneId!=zoneId))return Conflict("تغيرت منطقة توقيت الأكاديمية عن الفترات المحفوظة؛ راجع الإدارة قبل إعادة تفسير اليوم بتوقيت جديد.");
        var before=Snapshot(row,periods);var beforeVersion=row.Version;
        var period=periodId.HasValue?periods.SingleOrDefault(p=>p.Id==periodId):null;
        if(periodId.HasValue&&(period is null||period.Version!=body.PeriodVersion))return period is null?Results.NotFound():Conflict("تغيرت الفترة المحددة؛ أعد قراءتها قبل التصحيح.");
        if(!periodId.HasValue&&periods.Count>=24)return Invalid("بلغ اليوم الحد التشغيلي للفترات؛ راجع الإدارة بدل إنشاء سجلات مكررة.");
        if(periods.Count==0){if(row.CheckOut is null)return Conflict("الفترة الأولى مفتوحة؛ سجل انصرافها قبل إضافة فترة أخرى.");var original=Legacy(row);original.Id=Guid.NewGuid();periods.Add(original);db.StaffWorkPeriods.Add(original);}
        var candidate=new StaffWorkPeriod{StartDate=body.Date,StartTime=body.StartTime,EndDate=body.EndDate,EndTime=body.EndTime,StartUtc=start,EndUtc=end,TimeZoneId=zoneId};
        if(periods.Any(p=>p.Id!=periodId&&Overlap(candidate,p))||await StaffAttendanceIntervals.Overlaps(db,t.AcademyId,member,row.Id,body.Date,body.StartTime,body.EndDate,body.EndTime))return Conflict("الفترة تتداخل مع حضور محفوظ؛ لا يسمح بفترتين مفتوحتين أو ساعات مضاعفة.");
        now=clock.GetUtcNow();
        if(period is null){period=new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,AttendanceId=row.Id,MembershipId=member,StartDate=body.Date,CreatedAtUtc=now};periods.Add(period);db.StaffWorkPeriods.Add(period);}
        period.StartTime=body.StartTime;period.EndDate=body.EndDate;period.EndTime=body.EndTime;period.StartUtc=start;period.EndUtc=end;period.TimeZoneId=zoneId;period.TrustSource="AdminDeclared";period.RecordedByUserId=actor;period.UpdatedAtUtc=now;
        var first=periods.OrderBy(p=>p.StartTime).First();var last=periods.OrderBy(p=>p.EndDate??DateOnly.MaxValue).ThenBy(p=>p.EndTime??TimeOnly.MaxValue).Last();
        row.CheckIn=first.StartTime;row.CheckOut=last.EndTime;row.CheckOutDate=last.EndDate;row.RecordedByUserId=actor;row.UpdatedAtUtc=now;db.Entry(row).Property(r=>r.UpdatedAtUtc).IsModified=true;
        var schedule=await db.StaffWorkSchedules.SingleOrDefaultAsync(s=>s.AcademyId==t.AcademyId&&s.MembershipId==member&&s.DayOfWeek==body.Date.DayOfWeek&&s.IsActive);
        row.LateMinutes=schedule is null?0:Math.Max(0,(int)(first.StartTime.ToTimeSpan()-schedule.StartTime.ToTimeSpan()).TotalMinutes);row.SuggestedDeduction=schedule is not null&&row.LateMinutes>0&&row.LateMinutes>=schedule.LateThresholdMinutes?schedule.LateDeduction:0;
        try{await db.SaveChangesAsync();var after=Snapshot(row,periods,reason);db.StaffAttendanceChanges.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,AttendanceId=row.Id,MembershipId=member,ActorUserId=actor,ActorMembershipId=t.MembershipId,WorkDate=row.WorkDate,BeforeVersion=beforeVersion,AfterVersion=row.Version,BeforeSnapshot=before,AfterSnapshot=after,RequestKey=key,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(JsonSerializer.Deserialize<JsonElement>(after));}
        catch(DbUpdateConcurrencyException){return Conflict("تغير مصدر الحضور أثناء الحفظ؛ أعد تحميل اليوم.");}
        catch(DbUpdateException e)when(e.InnerException is PostgresException{SqlState:"23505"}){return Conflict("تعارض مفتاح أو فترة مفتوحة؛ لم تحفظ تغييرات جديدة.");}
        catch(DbUpdateException e)when(e.InnerException is PostgresException{SqlState:"23514"}){return Conflict("تعارض في مصدر الفترة أو دليلها؛ لم تحفظ تغييرات جديدة.");}
        catch(PostgresException e)when(e.SqlState=="23514"){return Conflict("تعارض في مصدر الفترة أو دليلها؛ لم تحفظ تغييرات جديدة.");}
    }
}
public sealed record WorkPeriodWrite(DateOnly Date,uint Version,TimeOnly StartTime,TimeOnly? EndTime,DateOnly? EndDate,uint? PeriodVersion=null,string? Reason=null);
