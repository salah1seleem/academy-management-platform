using System.Security.Cryptography;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Facilities;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice4;
public sealed record AttendanceCorrectionItem(Guid SubjectId, AttendanceStatus Status, uint? ExpectedVersion);
public sealed record AttendanceCorrectionWrite(AttendanceCorrectionKind Kind, AttendanceCorrectionItem[] Items, string? Reason);
public sealed record AttendanceCorrectionDecision(uint ExpectedVersion, string? Reason);
public sealed record AttendanceCorrectionSnapshot(Guid SubjectId, AttendanceStatus Before, AttendanceStatus After, uint Version);
public sealed record AttendanceCorrectionContext(bool IsPast, bool CanManageAttendance, bool CanRequestCorrection, bool CanApproveCorrection);

public sealed class AttendanceCorrectionService(FoundationDbContext db, CurrentTenant tenant, IHttpContextAccessor http, IAuthorizationService authorization, AttendanceService attendance, TimeProvider clock)
{
    private static string Hash(object body)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(body)));
    private async Task<TenantMembership> Authority(bool write, bool approve=false)
    {
        var t=await tenant.ResolveAsync()??throw new FacilityRejected("يلزم تسجيل الدخول.",401);
        if(write){await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"},0))");await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:"+t.AcademyId},0))");t=await tenant.ResolveAsync()??throw new FacilityRejected("الجلسة لم تعد مخولة.",403);}
        var permission=write?AcademyPermissions.AttendanceManage:AcademyPermissions.AttendanceRead;
        if(t.Role is not(AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin)||(approve&&t.Role!=AcademyRole.AcademyOwner)||!(await authorization.AuthorizeAsync(http.HttpContext!.User,http.HttpContext,permission)).Succeeded)throw new FacilityRejected("تصحيح الأيام السابقة للإداري المصرح، واعتماده للمالك فقط.",403);
        if(write&&t.Role==AcademyRole.AcademyAdmin&&await db.AcademyMemberships.AnyAsync(m=>m.Id==t.MembershipId&&m.UseActionGrants)&&!await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.Module=="attendance"&&g.IsActive&&g.Read&&g.Edit))throw new FacilityRejected("صلاحية تعديل الحضور غير متاحة حاليًا.",403);
        return t;
    }
    private Task Lock(Guid academy,Guid session)=>db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"attendance:{academy:N}:{session:N}:{Guid.Empty:N}"},0))");
    private async Task<TrainingSession> Session(Guid id)=>await db.TrainingSessions.AsNoTracking().SingleOrDefaultAsync(s=>s.Id==id)??throw new FacilityRejected("الحصة غير متاحة في نطاقك.",404);
    private async Task Past(TrainingSession session)
    {
        if(session.Status==TrainingSessionStatus.Cancelled||session.SessionDate>=await AttendanceCalendar.TodayAsync(session.AcademyId,db,clock))throw new FacilityRejected("هذا المسار لتصحيح يوم سابق فقط، وليس المستقبل أو حصة ملغاة.",422);
    }
    private async Task<AttendanceCorrectionSnapshot[]> Snapshot(TrainingSession session,AttendanceCorrectionKind kind,AttendanceCorrectionItem[] items)
    {
        if(!Enum.IsDefined(kind)||items is null||items.Length is <1 or >100||items.Select(i=>i.SubjectId).Distinct().Count()!=items.Length||items.Any(i=>i.SubjectId==Guid.Empty||!i.ExpectedVersion.HasValue||!Enum.IsDefined(i.Status)))throw new FacilityRejected("اختر حتى100عضو مختلف وحالة صالحة من كشف محدث بنسخته.",422);
        var ids=items.Select(i=>i.SubjectId).ToArray();var records=new Dictionary<Guid,(AttendanceStatus Status,uint Version)>();
        if(kind==AttendanceCorrectionKind.Players){
            if(await db.SportEnrollments.CountAsync(e=>ids.Contains(e.Id)&&e.IsActive&&e.Player.IsActive&&FoundationDbContext.IsSessionAssigned(e.AcademyId,e.Id,session.Id))!=ids.Length)throw new FacilityRejected("عضو غير متاح في الحصة ونطاقك الحالي.",404);
            records=(await db.PlayerAttendances.AsNoTracking().Where(a=>a.TrainingSessionId==session.Id&&ids.Contains(a.SportEnrollmentId)).ToArrayAsync()).ToDictionary(a=>a.SportEnrollmentId,a=>(a.Status,a.Version));
        }else{
            if(await db.AcademyMemberships.CountAsync(m=>ids.Contains(m.Id)&&FoundationDbContext.IsSessionStaffAssigned(m.AcademyId,m.Id,session.Id))!=ids.Length)throw new FacilityRejected("عضو غير متاح في الحصة ونطاقك الحالي.",404);
            records=(await db.StaffAttendances.AsNoTracking().Where(a=>a.TrainingSessionId==session.Id&&ids.Contains(a.AcademyMembershipId)).ToArrayAsync()).ToDictionary(a=>a.AcademyMembershipId,a=>(a.Status,a.Version));
        }
        return items.OrderBy(i=>i.SubjectId).Select(i=>{var before=records.GetValueOrDefault(i.SubjectId,(AttendanceStatus.NotRecorded,0u));if(before.Item2!=i.ExpectedVersion)throw new FacilityRejected("تغير الكشف منذ آخر قراءة؛ أعد تحميله وراجع التغييرات قبل طلب التصحيح.");return new AttendanceCorrectionSnapshot(i.SubjectId,before.Item1,i.Status,before.Item2);}).ToArray();
    }
    public async Task<AttendanceCorrectionContext> Context(Guid id)
    {
        var t=await tenant.ResolveAsync()??throw new FacilityRejected("يلزم تسجيل الدخول.",401);var s=await Session(id);var past=s.SessionDate<await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock);var manage=(await authorization.AuthorizeAsync(http.HttpContext!.User,http.HttpContext,AcademyPermissions.AttendanceManage)).Succeeded;
        if(t.Role==AcademyRole.Coach&&!await db.TrainingSessions.AnyAsync(x=>x.Id==id&&FoundationDbContext.IsSessionStaffAssigned(t.AcademyId,t.MembershipId,id)))throw new FacilityRejected("الحصة غير متاحة.",404);
        if(t.Role==AcademyRole.AcademyAdmin&&await db.AcademyMemberships.AnyAsync(m=>m.Id==t.MembershipId&&m.UseActionGrants))manage=manage&&await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.Module=="attendance"&&g.IsActive&&g.Read&&g.Edit);
        return new(past,!past&&s.SessionDate==await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock)&&s.Status!=TrainingSessionStatus.Cancelled&&manage,past&&s.Status!=TrainingSessionStatus.Cancelled&&manage&&t.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin,t.Role==AcademyRole.AcademyOwner);
    }
    public async Task<object> Request(Guid id,AttendanceCorrectionWrite body,string key)
    {
        if(string.IsNullOrWhiteSpace(key)||key.Length>100||string.IsNullOrWhiteSpace(body.Reason)||body.Reason.Length>500)throw new FacilityRejected("سبب تصحيح الماضي مطلوب حتى500حرف ومعرف العملية غير فارغ.",422);
        await using var tx=await db.Database.BeginTransactionAsync();var t=await Authority(true);var s=await Session(id);await Lock(t.AcademyId,id);s=await Session(id);
        var hash=Hash(new{id,body.Kind,body.Items,reason=body.Reason.Trim()});var prior=await db.AttendanceCorrections.AsNoTracking().SingleOrDefaultAsync(c=>c.RequestedByUserId==db.RequestUserId&&c.RequestKey==key);
        if(prior is not null){if(prior.RequestHash!=hash)throw new FacilityRejected("معرف العملية مرتبط بطلب آخر.");await tx.CommitAsync();return new{prior.Id,state=prior.State.ToString()};}
        await Past(s);var snapshot=await Snapshot(s,body.Kind,body.Items);if(snapshot.All(i=>i.Before==i.After))throw new FacilityRejected("لم تختر تغييرًا في الحضور.",422);
        var now=clock.GetUtcNow();var row=new AttendanceCorrection{Id=Guid.NewGuid(),AcademyId=t.AcademyId,TrainingSessionId=id,TrainingSessionVersion=s.Version,TrainingGroupId=s.TrainingGroupId,BranchId=s.BranchId,SessionDate=s.SessionDate,Kind=body.Kind,Payload=JsonSerializer.Serialize(snapshot),Reason=body.Reason.Trim(),RequestedByUserId=db.RequestUserId,RequestedByMembershipId=t.MembershipId,RequestKey=key,RequestHash=hash,ExpiresAtUtc=now.AddDays(7),CreatedAtUtc=now,UpdatedAtUtc=now};db.AttendanceCorrections.Add(row);await db.SaveChangesAsync();await tx.CommitAsync();return new{row.Id,state=row.State.ToString()};
    }
    public async Task<object> Read(Guid? id=null)
    {
        var t=await Authority(false);var query=db.AttendanceCorrections.AsNoTracking().Where(c=>(t.Role==AcademyRole.AcademyOwner||c.RequestedByUserId==db.RequestUserId)&&(!id.HasValue||c.Id==id));
        var requests=await query.OrderByDescending(c=>c.CreatedAtUtc).Take(id.HasValue?1:100).ToArrayAsync();if(id.HasValue&&requests.Length==0)throw new FacilityRejected("الطلب غير متاح.",404);
        var result=new List<object>();foreach(var row in requests){var items=JsonSerializer.Deserialize<AttendanceCorrectionSnapshot[]>(row.Payload)!;var ids=items.Select(i=>i.SubjectId).ToArray();var names=row.Kind==AttendanceCorrectionKind.Players?await db.SportEnrollments.Where(e=>ids.Contains(e.Id)).Select(e=>new{e.Id,name=e.Player.ArabicName}).ToDictionaryAsync(e=>e.Id,e=>e.name):await db.AcademyMemberships.Where(m=>ids.Contains(m.Id)).Select(m=>new{m.Id,name=m.User.DisplayName}).ToDictionaryAsync(m=>m.Id,m=>m.name);var group=await db.TrainingGroups.Where(g=>g.Id==row.TrainingGroupId).Select(g=>g.ArabicName).SingleAsync();result.Add(new{row.Id,row.Version,row.TrainingSessionId,row.SessionDate,group,kind=row.Kind.ToString(),state=row.State.ToString(),row.Reason,row.DecisionReason,row.CreatedAtUtc,row.DecidedAtUtc,row.AppliedAtUtc,row.ExpiresAtUtc,canApprove=t.Role==AcademyRole.AcademyOwner&&row.State==AttendanceCorrectionState.Pending&&row.ExpiresAtUtc>clock.GetUtcNow(),items=items.Select(i=>new{i.SubjectId,name=names.GetValueOrDefault(i.SubjectId,"عضو مسجل سابقًا"),before=i.Before.ToString(),after=i.After.ToString(),version=i.Version})});}
        return id.HasValue?result.Single():result;
    }
    public async Task<object> Decide(Guid id,AttendanceCorrectionDecision body,bool approve)
    {
        if(body.Reason?.Length>500)throw new FacilityRejected("الملاحظة حتى500حرف.",422);await using var tx=await db.Database.BeginTransactionAsync();var t=await Authority(true,true);var row=await db.AttendanceCorrections.SingleOrDefaultAsync(c=>c.Id==id)??throw new FacilityRejected("الطلب غير متاح.",404);await Lock(t.AcademyId,row.TrainingSessionId);await db.Entry(row).ReloadAsync();var target=approve?AttendanceCorrectionState.Approved:AttendanceCorrectionState.Rejected;
        if(row.State==target&&row.DecisionReason==body.Reason?.Trim()){await tx.CommitAsync();return new{row.Id,state=row.State.ToString()};}
        if(row.State!=AttendanceCorrectionState.Pending||row.Version!=body.ExpectedVersion||row.ExpiresAtUtc<=clock.GetUtcNow())throw new FacilityRejected("انتهى الطلب أو تغير منذ مراجعته؛ أعد تحميل القائمة.");
        var s=await Session(row.TrainingSessionId);await Past(s);if(s.Version!=row.TrainingSessionVersion||s.SessionDate!=row.SessionDate)throw new FacilityRejected("تغيرت الحصة بعد طلب التصحيح؛ أنشئ طلبًا جديدًا من كشف محدث.");
        var now=clock.GetUtcNow();row.State=approve?AttendanceCorrectionState.Applying:AttendanceCorrectionState.Rejected;row.DecidedByUserId=db.RequestUserId;row.DecidedByMembershipId=t.MembershipId;row.DecidedAtUtc=now;row.DecisionReason=body.Reason?.Trim();row.UpdatedAtUtc=now;
        if(approve){var snapshot=JsonSerializer.Deserialize<AttendanceCorrectionSnapshot[]>(row.Payload)!;await Snapshot(s,row.Kind,snapshot.Select(i=>new AttendanceCorrectionItem(i.SubjectId,i.After,i.Version)).ToArray());await db.SaveChangesAsync();if(row.Kind==AttendanceCorrectionKind.Players)await attendance.SavePlayersAsync(t.AcademyId,s.Id,db.RequestUserId,snapshot.Select(i=>new PlayerAttendanceChange(i.SubjectId,i.After,i.Version)).ToArray(),approvedCorrectionId:row.Id);else await attendance.SaveStaffAsync(t.AcademyId,s.Id,db.RequestUserId,snapshot.Select(i=>new StaffAttendanceChange(i.SubjectId,i.After,i.Version)).ToArray(),approvedCorrectionId:row.Id);row.State=AttendanceCorrectionState.Approved;row.AppliedAtUtc=clock.GetUtcNow();row.UpdatedAtUtc=clock.GetUtcNow();}
        db.AcademyNotices.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,UserId=row.RequestedByUserId,Message=approve?"اعتمد المالك تصحيح حضور اليوم السابق وتم تطبيقه على الكشف.":"لم يعتمد المالك طلب تصحيح الحضور. راجع الطلب للتفاصيل.",CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();await tx.CommitAsync();return new{row.Id,state=row.State.ToString()};
    }
}
