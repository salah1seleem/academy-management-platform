using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Academy.Api.Operations;

// Own staff attendance only. Neither an admin CRUD permission nor player attendance.
public static class StaffVerifiedAttendance
{
    private const string Path=StaffPasskeys.Path+"/attendance";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static Guid Actor(HttpContext h)=>Guid.Parse(h.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    // PostgreSQL timestamps preserve microseconds; use the same instant in JSON and source columns.
    private static DateTimeOffset Now(TimeProvider clock){var n=clock.GetUtcNow().ToUniversalTime();return new(n.Ticks-n.Ticks%10,TimeSpan.Zero);}
    private static string Hash(object v)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(v,Json))));
    private static string IntentHash(StaffAttendanceIntent b)=>Hash(b with {CapturedAtUtc=b.CapturedAtUtc.ToUniversalTime()});
    private static IResult Conflict(string s)=>Results.Conflict(new{message=s,attendanceRecorded=false,reviewAvailable=true});
    private static bool Staff(TenantMembership t)=>t.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin or AcademyRole.Coach;
    public static void MapStaffVerifiedAttendance(this WebApplication app)
    {
        var g=app.MapGroup(Path).RequireAuthorization(AcademyPermissions.TenantAccess);
        g.MapGet("",Read);
        g.MapPost("/options",Options).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("staff-passkey");
        g.MapPost("/complete",Complete).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("staff-passkey");
    }
    private static async Task<bool> Allowed(FoundationDbContext db,TenantMembership t,Employee e,Guid branch)
    {
        if(t.Role==AcademyRole.AcademyAdmin&&!await db.AcademyMemberships.AnyAsync(m=>m.Id==t.MembershipId&&(m.AllBranches||db.MembershipBranchAccesses.Any(a=>a.MembershipId==m.Id&&a.IsActive&&a.BranchId==branch)&&db.MembershipBranchAccesses.Any(a=>a.MembershipId==m.Id&&a.IsActive&&a.BranchId==e.BranchId))))return false;
        return e.BranchId==branch||await db.StaffGroupAssignments.AnyAsync(a=>a.AcademyId==t.AcademyId&&a.AcademyMembershipId==t.MembershipId&&a.IsActive&&a.TrainingGroup.IsActive&&a.TrainingGroup.BranchId==branch);
    }
    private static async Task<IResult> Read(CurrentTenant tenant,FoundationDbContext db,TimeProvider clock,IConfiguration configuration,IHostEnvironment env)
    {
        var t=(await tenant.ResolveAsync())!;if(!Staff(t))return Results.Forbid();
        var e=await db.Employees.AsNoTracking().SingleOrDefaultAsync(x=>x.AcademyId==t.AcademyId&&x.MembershipId==t.MembershipId&&x.IsActive);
        if(e is null)return Results.Ok(new{configured=false,profileMissing=true});
        var assigned=await db.StaffGroupAssignments.Where(a=>a.AcademyMembershipId==t.MembershipId&&a.IsActive&&a.TrainingGroup.IsActive).Select(a=>a.TrainingGroup.BranchId).ToArrayAsync();
        var candidates=await db.Branches.AsNoTracking().Where(b=>b.IsActive&&(b.Id==e.BranchId||assigned.Contains(b.Id))).ToArrayAsync();var sites=new List<object>();
        foreach(var b in candidates)if(await Allowed(db,t,e,b.Id))sites.Add(new{id=b.Id,name=b.ArabicName,enabled=b.StaffCheckInEnabled,configured=b.StaffCheckInEnabled&&b.CheckInLatitude.HasValue&&b.CheckInLongitude.HasValue,radiusMeters=b.StaffCheckInRadiusMeters,maxAccuracyMeters=b.StaffCheckInMaxAccuracyMeters,maxAgeSeconds=b.StaffCheckInMaxAgeSeconds,challengeSeconds=b.StaffCheckInChallengeSeconds,attendanceRequired=StaffAttendancePolicy.Read(b).Required(t.Role)});
        var open=await db.StaffDailyAttendances.AsNoTracking().Where(a=>a.MembershipId==t.MembershipId&&a.Status=="Present"&&a.CheckIn!=null&&a.CheckOut==null).Take(2).ToArrayAsync();
        var ids=open.Select(a=>a.Id).ToArray();var periods=await db.StaffWorkPeriods.AsNoTracking().Where(p=>ids.Contains(p.AttendanceId)&&p.EndTime==null).ToArrayAsync();
        var recent=await db.StaffPasskeyChallenges.AsNoTracking().Where(c=>c.MembershipId==t.MembershipId&&c.ResultJson!=null).OrderByDescending(c=>c.CreatedAtUtc).Take(5).Select(c=>c.ResultJson!).ToArrayAsync();
        var cfg=StaffPasskeys.Configuration(configuration,env);
        return Results.Ok(new{configured=cfg!=null,rpId=cfg?.RPID,nativeIosOriginConfigured=StaffPasskeys.NativeIosOriginConfigured(cfg),hasCredential=cfg!=null&&await db.StaffPasskeys.AnyAsync(k=>k.MembershipId==t.MembershipId&&k.IsActive&&k.RpId==cfg.RPID),nowUtc=clock.GetUtcNow(),branches=sites,hasOpenAttendance=open.Length>0,requiresSourceReview=open.Length>1||open.Length==1&&(periods.Length!=1||periods[0].StartUtc==null),recent=recent.Select(s=>PublicResult(JsonSerializer.Deserialize<AttendanceReceipt>(s,Json)!))});
    }
    private static string? LocationError(StaffAttendanceIntent b,Branch site,DateTimeOffset now)
    {
        if(b.Operation is not("CheckIn" or "CheckOut")||!double.IsFinite(b.Latitude)||!double.IsFinite(b.Longitude)||!double.IsFinite(b.AccuracyMeters)||b.Latitude is < -90 or >90||b.Longitude is < -180 or >180||b.AccuracyMeters<0)return "قراءة الموقع أو نوع العملية غير صالح؛ أعد التقاط الموقع.";
        if(!site.StaffCheckInEnabled)return "التسجيل الذاتي موقوف لهذا الفرع؛ اطلب مراجعة الإدارة، دون إغلاق الفترة تلقائيًا.";
        if(site.CheckInLatitude is null||site.CheckInLongitude is null)return "يلزم إعداد موقع الفرع؛ يمكنك طلب إثبات حضور للمراجعة.";
        if(b.CapturedAtUtc>now.AddSeconds(2)||now-b.CapturedAtUtc>TimeSpan.FromSeconds(site.StaffCheckInMaxAgeSeconds))return "قراءة الموقع قديمة أو ساعة الجهاز مختلفة؛ أعد التقاط موقع حديث.";
        if(b.AccuracyMeters>site.StaffCheckInMaxAccuracyMeters)return "دقة الموقع ضعيفة؛ أعد المحاولة أو اطلب مراجعة، دون توسيع نطاق الفرع.";
        var distance=StaffDeviceCheckIn.DistanceMeters(b.Latitude,b.Longitude,site.CheckInLatitude.Value,site.CheckInLongitude.Value);
        return distance+b.AccuracyMeters<=site.StaffCheckInRadiusMeters?null:distance-b.AccuracyMeters>site.StaffCheckInRadiusMeters?"الموقع خارج نطاق الفرع؛ لم يسجل حضور. يمكنك طلب مراجعة.":"الموقع قريب من حدود النطاق ولا يكفي للقبول؛ أعد المحاولة أو اطلب مراجعة.";
    }
    private static async Task<(Employee? Employee,Branch? Site)> Scope(FoundationDbContext db,TenantMembership t,Guid actor,Guid branch)
    {
        if(!await StaffPasskeys.Live(db,t,actor))return(null,null);
        var e=await db.Employees.AsNoTracking().SingleOrDefaultAsync(x=>x.AcademyId==t.AcademyId&&x.MembershipId==t.MembershipId&&x.IsActive);
        var site=await db.Branches.AsNoTracking().SingleOrDefaultAsync(b=>b.AcademyId==t.AcademyId&&b.Id==branch&&b.IsActive);
        return e is not null&&site is not null&&await Allowed(db,t,e,branch)?(e,site):(null,null);
    }
    private static async Task<(StaffDailyAttendance? Row,StaffWorkPeriod[] Periods,DateOnly Day,string Zone,string? Error)> Target(FoundationDbContext db,TenantMembership t,StaffAttendanceIntent b,TimeProvider clock)
    {
        var zoneId=await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>a.TimeZone).SingleAsync();var local=TimeZoneInfo.ConvertTime(clock.GetUtcNow(),TimeZoneInfo.FindSystemTimeZoneById(zoneId));var day=DateOnly.FromDateTime(local.DateTime);
        var open=await db.StaffDailyAttendances.Where(a=>a.MembershipId==t.MembershipId&&a.Status=="Present"&&a.CheckIn!=null&&a.CheckOut==null).Take(2).ToArrayAsync();
        var row=b.Operation=="CheckOut"?open.SingleOrDefaultIfOne():await db.StaffDailyAttendances.SingleOrDefaultAsync(a=>a.MembershipId==t.MembershipId&&a.WorkDate==day);
        if(b.Operation=="CheckOut"&&row is not null)day=row.WorkDate;
        var periods=row is null?[]:await db.StaffWorkPeriods.Where(p=>p.AttendanceId==row.Id).OrderBy(p=>p.StartTime).ToArrayAsync();
        string? error=null;
        if(b.Operation=="CheckIn"&&(open.Length>0||row is not null&&row.Status is not("Present" or "NotRecorded")))error="يوجد حضور مفتوح أو حالة إدارية مختلفة؛ راجع المصدر قبل حضور جديد.";
        if(b.Operation=="CheckIn"&&periods.Length>=24)error="بلغ اليوم الحد التشغيلي للفترات؛ راجع الإدارة.";
        if(b.Operation=="CheckOut"&&(open.Length!=1||periods.Count(p=>p.EndTime==null)!=1||periods.Any(p=>p.EndTime==null&&p.StartUtc==null)))error="الانصراف يحتاج فترة محفوظة مفتوحة واحدة؛ اطلب مراجعة المصدر الإداري أولًا.";
        if(b.Operation=="CheckOut"&&error is null&&row is not null)
        {
            var p=periods.Single(p=>p.EndTime==null);
            if(p.TrustSource=="ServerVerified")
            {
                var ids=await db.StaffAttendanceChanges.Where(a=>a.AttendanceId==row.Id&&a.VerificationChallengeId!=null).Select(a=>a.VerificationChallengeId!.Value).ToArrayAsync();
                var starts=await db.StaffPasskeyChallenges.AsNoTracking().Where(c=>ids.Contains(c.Id)&&c.Purpose=="CheckIn"&&c.ResultJson!=null).Select(c=>new{c.OperationJson,c.ResultJson}).ToArrayAsync();
                var start=starts.SingleOrDefault(c=>JsonSerializer.Deserialize<AttendanceReceipt>(c.ResultJson!,Json)!.WorkPeriodId==p.Id);
                if(start is null||JsonSerializer.Deserialize<AttendanceBinding>(start.OperationJson!,Json)!.BranchId!=b.BranchId)error="اختر فرع بداية الفترة نفسها للانصراف؛ راجع الإدارة إذا تغير مكان العمل.";
            }
        }
        if(periods.Any(p=>p.TimeZoneId!=null&&p.TimeZoneId!=zoneId))error="تغير توقيت الأكاديمية؛ يلزم مراجعة الفترات قبل تسجيل حضور جديد.";
        return(row,periods,day,zoneId,error);
    }
    private static StaffDailyAttendance? SingleOrDefaultIfOne(this StaffDailyAttendance[] rows)=>rows.Length==1?rows[0]:null;
    private static async Task<IResult> Options(StaffAttendanceIntent b,HttpContext h,CurrentTenant tenant,FoundationDbContext db,TimeProvider clock,IConfiguration configuration,IHostEnvironment env)
    {
        var t=(await tenant.ResolveAsync())!;if(!Staff(t))return Results.Forbid();var cfg=StaffPasskeys.Configuration(configuration,env);if(cfg is null)return Conflict("التحقق الآمن غير مهيأ؛ اطلب إثبات حضور للمراجعة.");
        await using var tx=await db.Database.BeginTransactionAsync();await StaffPasskeys.Lock(db,$"grants:{t.AcademyId}:{t.MembershipId}");await StaffPasskeys.Lock(db,$"staff-passkeys:{t.AcademyId}:{t.MembershipId}");await StaffPasskeys.Lock(db,$"check-in-site:{t.AcademyId}:{b.BranchId}");
        var(e,site)=await Scope(db,t,Actor(h),b.BranchId);if(e is null||site is null)return Results.NotFound();
        var now=Now(clock);var error=LocationError(b,site,now);if(error is not null)return Conflict(error);
        var keys=await db.StaffPasskeys.Where(k=>k.IsActive&&k.RpId==cfg.RPID).ToArrayAsync();if(keys.Length==0)return Conflict("سجل وسيلة تحقق فعالة أولًا؛ لم يسجل حضور.");
        if(await db.StaffPasskeyChallenges.CountAsync(c=>c.ConsumedAtUtc==null&&c.ExpiresAtUtc>now)>=5)return Conflict("توجد محاولات مفتوحة؛ انتظر انتهاءها قبل محاولة جديدة.");
        await StaffPasskeys.Lock(db,$"staff-open:{t.AcademyId}:{t.MembershipId}");var target=await Target(db,t,b,clock);if(target.Error is not null)return Conflict(target.Error);
        await PayrollEndpoints.Lock(db,t.AcademyId,e.Id,target.Day);var terms=await ContractHistory.Bounds(db,e,target.Day);if(target.Day<terms.ContractStart||terms.ContractEnd.HasValue&&target.Day>terms.ContractEnd||await PayrollEndpoints.Finalized(db,t.AcademyId,e.Id,target.Day))return Conflict("اليوم خارج العقد أو شهر الموظف معتمد؛ اطلب مراجعة الإدارة.");
        var p=target.Periods.SingleOrDefault(p=>p.EndTime==null);
        var state=new AttendanceBinding(IntentHash(b),e.Id,site.Id,site.Version,target.Day,target.Zone,target.Row?.Id,target.Row?.Version??0,p?.Id,p?.Version);
        var options=new Fido2(cfg).GetAssertionOptions(new GetAssertionOptionsParams{AllowedCredentials=keys.Select(k=>new PublicKeyCredentialDescriptor(k.CredentialId)).ToArray(),UserVerification=UserVerificationRequirement.Required});
        var challenge=new StaffPasskeyChallenge{Id=Guid.NewGuid(),AcademyId=t.AcademyId,MembershipId=t.MembershipId,UserId=Actor(h),Purpose=b.Operation,Name="حضور موظف موقع",OptionsJson=options.ToJson(),RpId=cfg.RPID,OriginsJson=JsonSerializer.Serialize(cfg.Origins.Order()),OperationJson=JsonSerializer.Serialize(state,Json),ExpiresAtUtc=now.AddSeconds(site.StaffCheckInChallengeSeconds),CreatedAtUtc=now,UpdatedAtUtc=now};
        db.StaffPasskeyChallenges.Add(challenge);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{challengeId=challenge.Id,challenge.ExpiresAtUtc,purpose=b.Operation,workDate=target.Day,targetWorkPeriodId=p?.Id,options=JsonSerializer.Deserialize<JsonElement>(challenge.OptionsJson)});
    }
    private static object PublicResult(AttendanceReceipt r)=>new{r.AttendanceId,r.WorkPeriodId,r.Operation,r.WorkDate,r.ConfirmedAtUtc,r.AfterVersion,attendanceRecorded=true,trustSource="ServerVerified"};
    private static async Task<IResult> Complete(StaffAttendanceProof b,HttpContext h,CurrentTenant tenant,FoundationDbContext db,TimeProvider clock,IConfiguration configuration,IHostEnvironment env)
    {
        var t=(await tenant.ResolveAsync())!;if(!Staff(t))return Results.Forbid();var cfg=StaffPasskeys.Configuration(configuration,env);if(cfg is null)return Conflict("التحقق الآمن غير مهيأ؛ لم يسجل حضور.");
        if(b.Intent is null||b.Credential.ValueKind!=JsonValueKind.Object||b.Credential.GetRawText().Length>24000)return Conflict("استجابة وسيلة التحقق غير صالحة؛ لم يسجل حضور.");
        await using var tx=await db.Database.BeginTransactionAsync();await StaffPasskeys.Lock(db,$"grants:{t.AcademyId}:{t.MembershipId}");await StaffPasskeys.Lock(db,$"staff-passkeys:{t.AcademyId}:{t.MembershipId}");await StaffPasskeys.Lock(db,$"check-in-site:{t.AcademyId}:{b.Intent.BranchId}");
        var(e,site)=await Scope(db,t,Actor(h),b.Intent.BranchId);if(e is null||site is null)return Results.NotFound();
        var c=await db.StaffPasskeyChallenges.SingleOrDefaultAsync(c=>c.Id==b.ChallengeId&&c.AcademyId==t.AcademyId&&c.MembershipId==t.MembershipId&&c.UserId==Actor(h));if(c is null)return Results.NotFound();
        var intentHash=IntentHash(b.Intent);var proofHash=Hash(b.Credential);
        if(c.ResultJson is not null){var saved=JsonSerializer.Deserialize<AttendanceReceipt>(c.ResultJson,Json)!;return saved.IntentHash==intentHash&&saved.ProofHash==proofHash?Results.Ok(PublicResult(saved)):Conflict("المحاولة مستخدمة لبيانات مختلفة؛ لم يغير الحضور.");}
        if(c.ConsumedAtUtc.HasValue)return Conflict("المحاولة انتهت أو استُخدمت؛ أعد قراءة المصدر.");
        var now=Now(clock);c.ConsumedAtUtc=now;c.UpdatedAtUtc=now;
        StaffPasskey? key=null;
        async Task<IResult> Fail(string s){if(key is not null){db.Entry(key).CurrentValues.SetValues(db.Entry(key).OriginalValues);db.Entry(key).State=EntityState.Unchanged;}await db.SaveChangesAsync();await tx.CommitAsync();return Conflict(s);}
        var binding=c.OperationJson is null?null:JsonSerializer.Deserialize<AttendanceBinding>(c.OperationJson,Json);
        if(binding is null||c.Purpose!=b.Intent.Operation||binding.IntentHash!=intentHash||binding.EmployeeId!=e.Id||binding.BranchId!=site.Id||binding.BranchVersion!=site.Version||c.ExpiresAtUtc<=now||!StaffPasskeys.SameConfig(c,cfg))return await Fail("تغيرت العملية أو الفرع أو الإعداد أو انتهت المحاولة؛ أعد التقاط الموقع.");
        var error=LocationError(b.Intent,site,now);if(error is not null)return await Fail(error);
        try
        {
            var response=b.Credential.Deserialize<AuthenticatorAssertionRawResponse>()!;var hash=Convert.ToHexString(SHA256.HashData(response.RawId));key=await db.StaffPasskeys.SingleOrDefaultAsync(k=>k.CredentialHash==hash&&k.IsActive&&k.RpId==cfg.RPID);
            if(key is null)return await Fail("وسيلة التحقق غير فعالة؛ لم يسجل حضور.");
            var verified=await new Fido2(cfg).MakeAssertionAsync(new(){AssertionResponse=response,OriginalOptions=AssertionOptions.FromJson(c.OptionsJson),StoredPublicKey=key.PublicKey,StoredSignatureCounter=checked((uint)key.SignCount),IsUserHandleOwnerOfCredentialIdCallback=(args,ct)=>Task.FromResult(args.CredentialId.SequenceEqual(key.CredentialId)&&args.UserHandle.SequenceEqual(key.UserHandle))});
            // Update the counter only with the attendance source in this transaction.
            key.SignCount=verified.SignCount;key.BackedUp=verified.IsBackedUp;
        }
        catch(Exception ex)when(ex is Fido2VerificationException or JsonException or FormatException or ArgumentException or NullReferenceException or CryptographicException or OverflowException or IndexOutOfRangeException){return await Fail("لم يثبت توقيع وسيلة التحقق؛ لم يسجل حضور. اطلب مراجعة عند التعذر.");}
        await StaffPasskeys.Lock(db,$"staff-open:{t.AcademyId}:{t.MembershipId}");await StaffPasskeys.Lock(db,$"staff-day:{t.AcademyId}:{t.MembershipId}:{binding.WorkDate}");await PayrollEndpoints.Lock(db,t.AcademyId,e.Id,binding.WorkDate);
        var target=await Target(db,t,b.Intent,clock);now=Now(clock);
        if(target.Error is not null)return await Fail(target.Error);
        if(binding.TimeZone!=target.Zone||binding.WorkDate!=target.Day||binding.AttendanceId!=target.Row?.Id||binding.AttendanceVersion!=(target.Row?.Version??0)||binding.WorkPeriodId!=target.Periods.SingleOrDefault(p=>p.EndTime==null)?.Id||binding.WorkPeriodVersion!=target.Periods.SingleOrDefault(p=>p.EndTime==null)?.Version)return await Fail("تغيرت الفترة أو اليوم؛ أعد قراءة المصدر بدل تعديل فترة أخرى.");
        error=LocationError(b.Intent,site,now);if(error is not null||c.ExpiresAtUtc<=now)return await Fail(error??"انتهت المحاولة؛ أعد التقاط الموقع.");
        // Refresh the employee under its source lock; an archived/ended contract cannot check in.
        e=await db.Employees.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==e.Id&&x.IsActive&&x.MembershipId==t.MembershipId);if(e is null)return await Fail("تغير ملف الموظف؛ لم يسجل حضور.");
        var terms=await ContractHistory.Bounds(db,e,binding.WorkDate);if(binding.WorkDate<terms.ContractStart||terms.ContractEnd.HasValue&&binding.WorkDate>terms.ContractEnd||await PayrollEndpoints.Finalized(db,t.AcademyId,e.Id,binding.WorkDate))return await Fail("العقد أو شهر الموظف لا يسمح بتعديل الحضور.");
        var local=TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById(target.Zone));var date=DateOnly.FromDateTime(local.DateTime);var time=TimeOnly.FromDateTime(local.DateTime);
        var row=target.Row;var periods=target.Periods.ToList();var before=row is null?"null":StaffWorkPeriods.Snapshot(row,periods);var beforeVersion=row?.Version??0;
        StaffWorkPeriod period;
        if(b.Intent.Operation=="CheckIn")
        {
            if(periods.Any(p=>p.EndDate!.Value.ToDateTime(p.EndTime!.Value)>local.DateTime)||await StaffAttendanceIntervals.Overlaps(db,t.AcademyId,t.MembershipId,row?.Id,date,time,null,null))return await Fail("توجد فترة متداخلة؛ اطلب مراجعة المصدر.");
            if(row is null){row=new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,MembershipId=t.MembershipId,WorkDate=date,Status="Present",CreatedAtUtc=now};db.StaffDailyAttendances.Add(row);}
            else if(periods.Count==0&&row.CheckIn.HasValue){var original=StaffWorkPeriods.Legacy(row);original.Id=Guid.NewGuid();periods.Add(original);db.StaffWorkPeriods.Add(original);}
            period=new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,AttendanceId=row.Id,MembershipId=t.MembershipId,StartDate=date,StartTime=time,StartUtc=now,TimeZoneId=target.Zone,TrustSource="ServerVerified",RecordedByUserId=Actor(h),CreatedAtUtc=now,UpdatedAtUtc=now};
            row.Status="Present";row.CheckIn=periods.Count==0?time:periods.Min(p=>p.StartTime);row.CheckOut=null;row.CheckOutDate=null;
            periods.Add(period);
        }
        else
        {
            period=periods.Single(p=>p.Id==binding.WorkPeriodId);if(now<=period.StartUtc||await StaffAttendanceIntervals.Overlaps(db,t.AcademyId,t.MembershipId,row!.Id,period.StartDate,period.StartTime,date,time))return await Fail("الانصراف يسبق البداية أو يتداخل مع مصدر آخر؛ اطلب المراجعة.");
            period.EndDate=date;period.EndTime=time;period.EndUtc=now;if(period.TrustSource=="AdminDeclared")period.TrustSource="MixedVerifiedEnd";period.RecordedByUserId=Actor(h);period.UpdatedAtUtc=now;
            row!.CheckOut=time;row.CheckOutDate=date;
        }
        row!.RecordedByUserId=Actor(h);row.UpdatedAtUtc=now;db.Entry(row).Property(a=>a.UpdatedAtUtc).IsModified=true;
        key!.LastVerifiedAtUtc=now;key.UpdatedAtUtc=now;
        // The challenge is finalized once, after source versions exist, in this same transaction.
        c.ConsumedAtUtc=null;c.UpdatedAtUtc=c.CreatedAtUtc;
        try
        {
            // Save the daily parent before a new period's immediate FK/source checks.
            await db.SaveChangesAsync();if(b.Intent.Operation=="CheckIn"){db.StaffWorkPeriods.Add(period);await db.SaveChangesAsync();}
            var receipt=new AttendanceReceipt(row.Id,period.Id,b.Intent.Operation,row.WorkDate,now,row.Version,intentHash,proofHash,key.Id);
            c.ConsumedAtUtc=now;c.VerifiedAtUtc=now;c.UpdatedAtUtc=now;c.ResultJson=JsonSerializer.Serialize(receipt,Json);
            db.StaffAttendanceChanges.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,AttendanceId=row.Id,MembershipId=t.MembershipId,ActorMembershipId=t.MembershipId,ActorUserId=Actor(h),VerificationChallengeId=c.Id,WorkDate=row.WorkDate,BeforeVersion=beforeVersion,AfterVersion=row.Version,BeforeSnapshot=before,AfterSnapshot=StaffWorkPeriods.Snapshot(row,periods),RequestKey=$"device:{c.Id}",RequestHash=intentHash,CreatedAtUtc=now,UpdatedAtUtc=now});
            await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(PublicResult(receipt));
        }
        catch(DbUpdateConcurrencyException){return Conflict("تغير مصدر الحضور؛ لم تحفظ كتابة جزئية. أعد قراءته.");}
        catch(DbUpdateException ex)when(ex.InnerException is PostgresException{SqlState:"23514" or "23505"}){return Conflict("تعارض في الفترة أو إثباتها؛ لم تحفظ كتابة جزئية.");}
        catch(PostgresException ex)when(ex.SqlState=="23514"){return Conflict("تعارض في دليل المصدر؛ لم يسجل حضور جديد.");}
    }
    private sealed record AttendanceBinding(string IntentHash,Guid EmployeeId,Guid BranchId,uint BranchVersion,DateOnly WorkDate,string TimeZone,Guid? AttendanceId,uint AttendanceVersion,Guid? WorkPeriodId,uint? WorkPeriodVersion);
    private sealed record AttendanceReceipt(Guid AttendanceId,Guid WorkPeriodId,string Operation,DateOnly WorkDate,DateTimeOffset ConfirmedAtUtc,uint AfterVersion,string IntentHash,string ProofHash,Guid CredentialId);
}
public sealed record StaffAttendanceIntent(Guid BranchId,string Operation,double Latitude,double Longitude,double AccuracyMeters,DateTimeOffset CapturedAtUtc);
public sealed record StaffAttendanceProof(Guid ChallengeId,StaffAttendanceIntent Intent,JsonElement Credential);
