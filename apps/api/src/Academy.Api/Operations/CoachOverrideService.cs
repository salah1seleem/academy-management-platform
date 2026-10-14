using System.Security.Cryptography;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Facilities;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public sealed record CoachOverrideWrite(Guid OriginalCoachId, Guid ActualCoachId, int ExpectedSequence, string? Reason, bool QualificationConfirmed, string? QualificationEvidence, string? Fingerprint = null);
public sealed record CoachOverridePreview(string Fingerprint, string[] Conflicts, string[] Effects, int CurrentSequence);
public sealed class CoachOverrideService(FoundationDbContext db, GroupPlacementService placement, ResourceSessionCoordinator coordinator, TimeProvider clock)
{
    private static string Hash(object body) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(body)));
    private async Task<TrainingSession> Session(Guid id) => await db.TrainingSessions.AsNoTracking().Include(s=>s.TrainingGroup).SingleOrDefaultAsync(s=>s.Id==id) ?? throw new FacilityRejected("الحصة غير متاحة في نطاقك.",404);
    private IQueryable<AcademyMembership> Candidates(Guid branch) => db.AcademyMemberships.AsNoTracking().Where(m=>m.IsActive && m.User.IsActive && m.Role==AcademyRole.Coach && (!db.BranchScopeRestricted || db.MembershipBranchAccesses.Any(a=>a.MembershipId==m.Id&&a.BranchId==branch&&a.IsActive) || db.StaffGroupAssignments.Any(a=>a.AcademyMembershipId==m.Id&&a.IsActive&&a.TrainingGroup.BranchId==branch&&a.TrainingGroup.IsActive)));
    public async Task<object> Read(Guid id)
    {
        await using var tx=await db.Database.BeginTransactionAsync(); await placement.Authority("academy","Read"); var s=await Session(id); await placement.Authority("academy","Read",s.BranchId);
        var originals=await db.StaffGroupAssignments.AsNoTracking().Where(a=>a.TrainingGroupId==s.TrainingGroupId&&a.IsActive&&a.AcademyMembership.IsActive&&a.AcademyMembership.User.IsActive&&a.AcademyMembership.Role==AcademyRole.Coach).Select(a=>new{id=a.AcademyMembershipId,name=a.AcademyMembership.User.DisplayName}).ToArrayAsync();
        var history=await db.SessionCoachOverrides.AsNoTracking().Where(o=>o.TrainingSessionId==id).OrderByDescending(o=>o.CreatedAtUtc).ThenByDescending(o=>o.Sequence).ToArrayAsync(); var latest=history.GroupBy(o=>o.OriginalCoachMembershipId).ToDictionary(g=>g.Key,g=>g.MaxBy(o=>o.Sequence)!);
        var coaches=await Candidates(s.BranchId).OrderBy(m=>m.User.DisplayName).Select(m=>new{m.Id,name=m.User.DisplayName}).ToArrayAsync(); await tx.CommitAsync();
        return new { sessionId=s.Id, group=s.TrainingGroup.ArabicName,s.SessionDate,s.StartTime,s.EndTime,originals=originals.Select(o=>new{o.id,o.name,actualCoachId=latest.TryGetValue(o.id,out var v)?v.ActualCoachMembershipId:o.id,sequence=latest.GetValueOrDefault(o.id)?.Sequence??0}),coaches,history=history.Take(100).Select(o=>new{o.Id,o.OriginalCoachMembershipId,o.ActualCoachMembershipId,o.Sequence,o.Reason,o.QualificationEvidence,o.PayBasis,o.CreatedAtUtc}),message="بديل لهذه الحصة فقط؛ لا تغيير لإسناد المجموعة، ولا أجر أو خصم تلقائي دون عقد وسجل عمل معتمد." };
    }
    private async Task<CoachOverridePreview> Inspect(TrainingSession s,CoachOverrideWrite body)
    {
        var conflicts=new List<string>(); var today=await AttendanceCalendar.TodayAsync(s.AcademyId,db,clock); var zone=TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a=>a.Id==s.AcademyId).Select(a=>a.TimeZone).SingleAsync()); var localTime=TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),zone).DateTime);
        if(!s.TrainingGroup.IsActive || !await db.Branches.AnyAsync(b=>b.Id==s.BranchId&&b.IsActive) || !await db.Sports.AnyAsync(p=>p.Id==s.SportId&&p.IsActive) || s.Status!=TrainingSessionStatus.Scheduled || s.SessionDate<today || s.SessionDate==today&&s.EndTime<=localTime) conflicts.Add("اختر حصة قائمة لم تنتهِ؛ الماضي وتعديل العمل المنفذ يحتاجان مسار اعتماد منفصل.");
        var source=await db.StaffGroupAssignments.AsNoTracking().Where(a=>a.TrainingGroupId==s.TrainingGroupId&&a.AcademyMembershipId==body.OriginalCoachId&&a.IsActive&&a.AcademyMembership.IsActive&&a.AcademyMembership.Role==AcademyRole.Coach&&a.AcademyMembership.User.IsActive).Select(a=>new{a.Id,a.Version}).SingleOrDefaultAsync(); if(source is null) conflicts.Add("اختر المدرب الأصلي المسند للمجموعة.");
        var actual=await Candidates(s.BranchId).Where(m=>m.Id==body.ActualCoachId).Select(m=>new{m.Id,m.UpdatedAtUtc,userUpdated=m.User.UpdatedAtUtc}).SingleOrDefaultAsync(); if(actual is null) conflicts.Add("المدرب الفعلي غير متاح ضمن نطاق الفرع.");
        var latest=await db.SessionCoachOverrides.AsNoTracking().Where(o=>o.TrainingSessionId==s.Id&&o.OriginalCoachMembershipId==body.OriginalCoachId).OrderByDescending(o=>o.Sequence).FirstOrDefaultAsync(); var sequence=latest?.Sequence??0; var previous=latest?.ActualCoachMembershipId??body.OriginalCoachId;
        if(sequence!=body.ExpectedSequence) conflicts.Add("تغير قرار البديل؛ أعد تحميله قبل المراجعة.");
        if(previous==body.ActualCoachId) conflicts.Add("هذا هو المدرب الفعلي الحالي؛ لا حاجة إلى قرار مكرر.");
        if(body.ActualCoachId!=body.OriginalCoachId&&await db.TrainingSessions.AnyAsync(x=>x.Id==s.Id&&FoundationDbContext.IsSessionStaffAssigned(x.AcademyId,body.ActualCoachId,x.Id))) conflicts.Add("المدرب المختار مسند بالفعل لهذه الحصة؛ لا نكرر وحدة عمله.");
        if(await db.SessionCoachOverrides.AnyAsync(o=>o.TrainingSessionId==s.Id&&o.OriginalCoachMembershipId!=body.OriginalCoachId&&o.ActualCoachMembershipId==body.ActualCoachId&&!db.SessionCoachOverrides.Any(n=>n.TrainingSessionId==o.TrainingSessionId&&n.OriginalCoachMembershipId==o.OriginalCoachMembershipId&&n.Sequence>o.Sequence))) conflicts.Add("المدرب يغطي بالفعل مدربًا آخر في الواقعة؛ لا نكرر وحدة عمله.");
        if(string.IsNullOrWhiteSpace(body.Reason)||body.Reason.Length>500) conflicts.Add("أدخل تبرير التغيير ضمن500حرف.");
        if(!body.QualificationConfirmed||string.IsNullOrWhiteSpace(body.QualificationEvidence)||body.QualificationEvidence.Length>500) conflicts.Add("أكد مراجعة أهلية المدرب للرياضة والمجموعة وأدخل مرجع المراجعة البشرية.");
        var involved=new[]{body.OriginalCoachId,body.ActualCoachId,previous}; var recorded=await db.StaffAttendances.AsNoTracking().Where(a=>a.TrainingSessionId==s.Id&&involved.Contains(a.AcademyMembershipId)).Select(a=>new{a.AcademyMembershipId,a.Status,a.Version}).ToArrayAsync();
        if(recorded.Any(a=>AttendanceRules.Attended(a.Status))) conflicts.Add("للواقعة عمل منفذ مسجل؛ لا نعيد نسبته لمدرب آخر من هذه الشاشة.");
        var bookings=await db.ResourceBookings.AsNoTracking().Where(b=>b.TrainingSessionId==s.Id&&b.State==ResourceBookingState.Reserved).OrderBy(b=>b.Id).ToArrayAsync();
        if(bookings.Length>0) {
            var payload=await db.AcademyConfigurationVersions.Where(c=>c.State==ConfigurationState.Active).Select(c=>c.Payload).SingleOrDefaultAsync();
            if(payload is null || !AcademyRuntimeSettings.Read(payload).Features.GetValueOrDefault("facilities") || !await db.AcademyFeatureEntitlements.AnyAsync(e=>e.Feature=="facilities"&&e.Enabled&&e.IsActive)) conflicts.Add("ميزة المرافق المرتبطة بالحصة غير متاحة حاليًا.");
        }
        if(await db.GroupResourceSchedules.AnyAsync(b=>b.TrainingGroupId==s.TrainingGroupId&&b.IsActive)&&bookings.Length==0) conflicts.Add("حجز المورد المرتبط بالحصة غير مكتمل.");
        var resourceIds=bookings.Select(b=>b.ResourceId).Distinct().ToArray();
        var resources=await (from r in db.FacilityResources.AsNoTracking() join p in db.FacilitySpaces on r.FacilitySpaceId equals p.Id join f in db.Facilities on p.FacilityId equals f.Id where resourceIds.Contains(r.Id) select new {r.Id,r.Version,r.BranchId,r.IsActive,r.Capacity,r.SharedBookingsAllowed,r.OperatingDays,r.OpensAt,r.ClosesAt,r.ServiceKinds,r.ApprovedAtUtc,r.ApprovedByUserId,spaceVersion=p.Version,spaceActive=p.IsActive,p.SportId,facilityVersion=f.Version,facilityActive=f.IsActive}).ToArrayAsync();
        foreach(var booking in bookings) {
            var resource=resources.SingleOrDefault(r=>r.Id==booking.ResourceId);
            if(resource is null || !resource.IsActive || !resource.spaceActive || !resource.facilityActive || resource.BranchId!=s.BranchId || resource.SportId!=s.SportId || resource.ApprovedAtUtc is null || resource.ApprovedByUserId is null || booking.ServiceKind!="Training" || booking.TrainingGroupId!=s.TrainingGroupId || booking.Date!=s.SessionDate || booking.StartTime!=s.StartTime || booking.EndTime!=s.EndTime || !resource.OperatingDays.Contains((int)s.SessionDate.DayOfWeek) || !resource.ServiceKinds.Contains("Training") || resource.OpensAt>s.StartTime || resource.ClosesAt<s.EndTime) {conflicts.Add("المورد غير متاح أو مواعيد تشغيله ونوع خدمته يحتاجان مراجعة.");continue;}
            var others=await db.ResourceBookings.AsNoTracking().Where(b=>b.ResourceId==booking.ResourceId&&b.Id!=booking.Id&&b.State==ResourceBookingState.Reserved&&b.Date==s.SessionDate&&b.StartTime<s.EndTime&&s.StartTime<b.EndTime).Select(b=>new BookingCapacity.Slot(b.StartTime,b.EndTime,b.ReservedSeats,b.Exclusive)).ToArrayAsync();
            if(!BookingCapacity.Fits(resource.Capacity,resource.SharedBookingsAllowed,new(booking.StartTime,booking.EndTime,booking.ReservedSeats,booking.Exclusive),others) || await db.ResourceClosures.AnyAsync(c=>c.ResourceId==booking.ResourceId&&c.IsActive&&c.FromDate<=s.SessionDate&&c.ToDate>=s.SessionDate&&c.StartTime<s.EndTime&&s.StartTime<c.EndTime)) conflicts.Add("المورد غير متاح أو سياسة سعته تحتاج مراجعة.");
        }
        if(actual is not null && (await db.TrainingSessions.IgnoreQueryFilters(["trusted-branches", "trusted-sports"]).AnyAsync(other=>other.AcademyId==s.AcademyId&&other.Id!=s.Id&&other.Status!=TrainingSessionStatus.Cancelled&&other.SessionDate==s.SessionDate&&other.StartTime<s.EndTime&&s.StartTime<other.EndTime&&FoundationDbContext.IsSessionStaffAssigned(other.AcademyId,actual.Id,other.Id))
            || await db.ResourceBookings.IgnoreQueryFilters(["trusted-branches", "trusted-sports"]).AnyAsync(b=>b.AcademyId==s.AcademyId&&b.TrainingSessionId==null&&b.CoachMembershipId==actual.Id&&b.State==ResourceBookingState.Reserved&&b.Date==s.SessionDate&&b.StartTime<s.EndTime&&s.StartTime<b.EndTime))) conflicts.Add("للمدرب الفعلي موعد آخر متعارض؛ اختر مدربًا أو توقيتًا آخر.");
        var fingerprint=Hash(new{s.Id,s.Version,groupVersion=s.TrainingGroup.Version,source,actual,sequence,bookings=bookings.Select(b=>new{b.Id,b.Version}),resources=resources.OrderBy(r=>r.Id),recorded,body.OriginalCoachId,body.ActualCoachId,body.ExpectedSequence,body.QualificationConfirmed,reason=body.Reason?.Trim(),evidence=body.QualificationEvidence?.Trim()});
        return new(fingerprint,conflicts.Distinct().ToArray(),["التغيير لهذه الحصة فقط؛ المدرب الدائم والجداول الأخرى لا يتغيران.","لا حضور مفترض أو خصم على الأصلي أو أجر للبديل؛ يلزم سجل عمل وعقد معتمد.","إشعار أسرة داخلي عن التغيير التشغيلي فقط؛ لا رسائل خارجية."],sequence);
    }
    public async Task<object> Preview(Guid id,CoachOverrideWrite body)
    {
        await using var tx=await db.Database.BeginTransactionAsync(); await placement.Authority("academy","Read"); var s=await Session(id); await placement.Authority("academy","Read",s.BranchId); var preview=await Inspect(s,body); await tx.CommitAsync(); return preview;
    }
    public async Task<object> Apply(Guid id,CoachOverrideWrite body,string key)
    {
        if(string.IsNullOrWhiteSpace(key)||key.Length>100||string.IsNullOrWhiteSpace(body.Fingerprint)) throw new FacilityRejected("راجع التغيير قبل تأكيده بمعرف عملية صالح.",422);
        await using var tx=await db.Database.BeginTransactionAsync(); var t=await placement.Authority("academy","Edit"); var s=await Session(id); await placement.Authority("academy","Edit",s.BranchId); await coordinator.LockCalendar(t.AcademyId,[s.TrainingGroupId],"Edit");
        foreach(var resource in await db.ResourceBookings.Where(b=>b.TrainingSessionId==id&&b.State==ResourceBookingState.Reserved).Select(b=>b.ResourceId).Distinct().Order().ToArrayAsync()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"facility-resource:{t.AcademyId}:{resource}"},0))");
        foreach(var coach in new[]{body.OriginalCoachId,body.ActualCoachId}.Distinct().Order()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"facility-coach:{t.AcademyId}:{coach}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"attendance:{t.AcademyId:N}:{id:N}:{Guid.Empty:N}"},0))");
        var hash=Hash(new{id,body.OriginalCoachId,body.ActualCoachId,body.ExpectedSequence,reason=body.Reason?.Trim(),body.QualificationConfirmed,evidence=body.QualificationEvidence?.Trim(),body.Fingerprint}); var actor=db.RequestUserId;
        var prior=await db.SessionCoachOverrides.SingleOrDefaultAsync(o=>o.ActorUserId==actor&&o.RequestKey==key); if(prior is not null){if(prior.RequestHash!=hash)throw new FacilityRejected("معرف العملية مرتبط بتغيير آخر.");await tx.CommitAsync();return new{prior.Id,prior.Sequence};}
        s=await Session(id); var preview=await Inspect(s,body); if(preview.Conflicts.Length>0)throw new FacilityRejected(string.Join(" ",preview.Conflicts)); if(preview.Fingerprint!=body.Fingerprint)throw new FacilityRejected("تغيرت الحصة أو الإسناد منذ المعاينة؛ راجع من جديد.");
        var latest=await db.SessionCoachOverrides.Where(o=>o.TrainingSessionId==id&&o.OriginalCoachMembershipId==body.OriginalCoachId).OrderByDescending(o=>o.Sequence).FirstOrDefaultAsync(); var now=clock.GetUtcNow();
        var row=new SessionCoachOverride{Id=Guid.NewGuid(),AcademyId=t.AcademyId,TrainingSessionId=id,TrainingGroupId=s.TrainingGroupId,BranchId=s.BranchId,SportId=s.SportId,OriginalCoachMembershipId=body.OriginalCoachId,ActualCoachMembershipId=body.ActualCoachId,Sequence=preview.CurrentSequence+1,PreviousDecisionId=latest?.Id,SessionDate=s.SessionDate,StartTime=s.StartTime,EndTime=s.EndTime,ActorUserId=actor,Reason=body.Reason!.Trim(),QualificationEvidence=body.QualificationEvidence!.Trim(),RequestKey=key,RequestHash=hash,Snapshot=JsonSerializer.Serialize(new{preview.Effects,s.SessionDate,s.StartTime,s.EndTime}),CreatedAtUtc=now,UpdatedAtUtc=now}; db.SessionCoachOverrides.Add(row);
        await db.SaveChangesAsync();
        var actualName=await db.AcademyMemberships.Where(m=>m.Id==body.ActualCoachId).Select(m=>m.User.DisplayName).SingleAsync();
        var guardians=await db.GuardianPlayerLinks.Where(l=>l.IsActive&&l.Guardian.IsActive&&db.SportEnrollments.Any(e=>e.PlayerId==l.PlayerId&&e.IsActive&&FoundationDbContext.IsSessionAssigned(e.AcademyId,e.Id,id))).Select(l=>l.Guardian.UserId).Distinct().ToArrayAsync();
        foreach(var guardian in guardians) db.AcademyNotices.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,UserId=guardian,Message=$"تحديث حصة {s.TrainingGroup.ArabicName} يوم {s.SessionDate:yyyy-MM-dd} الساعة {s.StartTime:HH:mm}: المدرب الفعلي {actualName}. بقية مواعيد المجموعة لم تتغير.",CreatedAtUtc=now,UpdatedAtUtc=now});
        await db.SaveChangesAsync(); await tx.CommitAsync(); return new{row.Id,row.Sequence};
    }
}
