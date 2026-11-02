using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

// Calendar overlap validation for the existing daily source, not hours/payroll calculation.
// Writers hold staff-open for the member before checking and saving.
internal static class StaffAttendanceIntervals
{
    public static async Task<bool> Overlaps(FoundationDbContext db, Guid academy, Guid member, Guid? exclude,
        DateOnly day, TimeOnly start, DateOnly? endDay, TimeOnly? end)
    {
        var from = day.ToDateTime(start);
        var until = end.HasValue ? (endDay ?? day).ToDateTime(end.Value) : DateTime.MaxValue;
        var rows = await db.StaffDailyAttendances.AsNoTracking().Where(a => a.AcademyId == academy && a.MembershipId == member
            && a.Id != exclude && a.Status == "Present" && a.CheckIn != null && !db.StaffWorkPeriods.Any(p=>p.AttendanceId==a.Id) && (!endDay.HasValue || a.WorkDate <= endDay))
            .Select(a => new { a.WorkDate, a.CheckIn, a.CheckOut, a.CheckOutDate }).ToArrayAsync();
        if(rows.Any(a => {
            var otherFrom = a.WorkDate.ToDateTime(a.CheckIn!.Value);
            var otherUntil = a.CheckOut.HasValue ? (a.CheckOutDate ?? a.WorkDate).ToDateTime(a.CheckOut.Value) : DateTime.MaxValue;
            return otherUntil < otherFrom || from < otherUntil && otherFrom < until;
        }))return true;
        var periods=await db.StaffWorkPeriods.AsNoTracking().Where(p=>p.AcademyId==academy&&p.MembershipId==member&&p.AttendanceId!=exclude).ToArrayAsync();
        var zone=TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a=>a.Id==academy).Select(a=>a.TimeZone).SingleAsync());
        var knownStart=StaffWorkPeriods.TryUtc(day,start,zone,out var utcStart);DateTimeOffset? utcEnd=null;
        if(end.HasValue){if(!StaffWorkPeriods.TryUtc(endDay??day,end.Value,zone,out var finish))knownStart=false;else utcEnd=finish;}
        return periods.Any(p=>p.StartUtc.HasValue ? !knownStart||utcStart<(p.EndUtc??DateTimeOffset.MaxValue)&&p.StartUtc<(utcEnd??DateTimeOffset.MaxValue) : from<(p.EndTime.HasValue?p.EndDate!.Value.ToDateTime(p.EndTime.Value):DateTime.MaxValue)&&p.StartDate.ToDateTime(p.StartTime)<until);
    }
}
