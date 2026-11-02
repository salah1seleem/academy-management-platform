using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

// Called only after independent-review, live grants, staff/day and payroll-source locks.
internal static class StaffExceptionPeriods
{
    internal static async Task<IResult?> Apply(StaffAttendanceException claim,StaffDailyAttendance row,TenantMembership reviewer,Guid actor,FoundationDbContext db,TimeProvider clock)
    {
        IResult Conflict(string message)=>Results.Conflict(new{message});
        if(row.Id!=claim.TargetAttendanceId||row.Version!=claim.TargetAttendanceVersion||row.Status!="Present"||row.CheckIn is null)
            return Conflict("تغير اليوم الذي بُني عليه الطلب؛ أعد قراءة المصدر واطلب مراجعة جديدة.");
        var zoneId=await db.Academies.Where(a=>a.Id==claim.AcademyId).Select(a=>a.TimeZone).SingleAsync();
        if(claim.RequestTimeZoneId!=zoneId)return Conflict("تغير توقيت الأكاديمية؛ راجع أصل الطلب بدل إعادة تفسيره.");
        var zone=TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var local=TimeZoneInfo.ConvertTime(claim.ClaimedAtUtc,zone);
        var date=DateOnly.FromDateTime(local.DateTime);var time=TimeOnly.FromDateTime(local.DateTime);
        if(!StaffWorkPeriods.TryUtc(date,time,zone,out var utc)||utc!=claim.ClaimedAtUtc)
            return Conflict("الوقت المدعى ملتبس بسبب تغيير الساعة؛ يلزم تصحيح إداري واضح.");
        var periods=await db.StaffWorkPeriods.Where(p=>p.AttendanceId==row.Id&&p.AcademyId==claim.AcademyId).OrderBy(p=>p.StartTime).ToListAsync();
        if(periods.Any(p=>p.TimeZoneId is not null&&p.TimeZoneId!=zoneId))return Conflict("توقيت الفترات المحفوظ مختلف؛ يلزم مراجعة إدارية.");
        var before=StaffWorkPeriods.Snapshot(row,periods);var version=row.Version;
        var now=clock.GetUtcNow();StaffWorkPeriod period;
        if(claim.Operation=="CheckOut")
        {
            var target=periods.SingleOrDefault(p=>p.Id==claim.TargetWorkPeriodId);
            if(target is null||target.Version!=claim.TargetWorkPeriodVersion||target.EndTime.HasValue||target.StartUtc is null||utc<=target.StartUtc)
                return Conflict("تغيرت الفترة المحددة أو أُغلقت؛ لا يمكن للطلب إغلاق فترة أخرى.");
            if(await StaffAttendanceIntervals.Overlaps(db,claim.AcademyId,claim.MembershipId,row.Id,target.StartDate,target.StartTime,date,time))
                return Conflict("الانصراف يتداخل مع مصدر آخر؛ راجع الفترة المحددة.");
            period=target;period.EndDate=date;period.EndTime=time;period.EndUtc=utc;
        }
        else
        {
            if(date!=row.WorkDate||row.CheckOut is null||periods.Any(p=>p.EndTime is null)||periods.Count>=24)
                return Conflict("لا يمكن إضافة وردية فوق فترة مفتوحة أو يوم مختلف؛ راجع المصدر.");
            if(periods.Count==0){var original=StaffWorkPeriods.Legacy(row);original.Id=Guid.NewGuid();periods.Add(original);db.StaffWorkPeriods.Add(original);}
            // New check-in means the next shift after all existing shifts, never an insertion into a gap.
            if(periods.Any(p=>p.EndDate!.Value.ToDateTime(p.EndTime!.Value)>date.ToDateTime(time))||
                await StaffAttendanceIntervals.Overlaps(db,claim.AcademyId,claim.MembershipId,row.Id,date,time,null,null))
                return Conflict("وقت الحضور يتداخل مع فترة قائمة؛ لا تستبدل الوردية السابقة.");
            period=new(){Id=Guid.NewGuid(),AcademyId=claim.AcademyId,AttendanceId=row.Id,MembershipId=claim.MembershipId,StartDate=row.WorkDate,StartTime=time,StartUtc=utc,TimeZoneId=zoneId,TrustSource="AdminDeclared",CreatedAtUtc=now};
            periods.Add(period);db.StaffWorkPeriods.Add(period);
        }
        period.RecordedByUserId=actor;period.UpdatedAtUtc=now;
        var last=periods.OrderBy(p=>p.EndDate??DateOnly.MaxValue).ThenBy(p=>p.EndTime??TimeOnly.MaxValue).Last();
        row.CheckIn=periods.Min(p=>p.StartTime);row.CheckOut=last.EndTime;row.CheckOutDate=last.EndDate;
        row.RecordedByUserId=actor;row.UpdatedAtUtc=now;db.Entry(row).Property(x=>x.UpdatedAtUtc).IsModified=true;
        // No automatic financial deduction, salary calculation or session completion.
        await db.SaveChangesAsync();
        var after=StaffWorkPeriods.Snapshot(row,periods,claim.Reason);
        db.StaffAttendanceChanges.Add(new(){Id=Guid.NewGuid(),AcademyId=row.AcademyId,AttendanceId=row.Id,MembershipId=row.MembershipId,ActorMembershipId=reviewer.MembershipId,ActorUserId=actor,WorkDate=row.WorkDate,BeforeVersion=version,AfterVersion=row.Version,BeforeSnapshot=before,AfterSnapshot=after,RequestKey=$"exception:{claim.Id}",RequestHash=claim.RequestHash,CreatedAtUtc=now,UpdatedAtUtc=now});
        claim.BeforeSnapshot=before;claim.AfterSnapshot=after;claim.AttendanceId=row.Id;claim.AppliedWorkPeriodId=period.Id;
        return null;
    }
}
