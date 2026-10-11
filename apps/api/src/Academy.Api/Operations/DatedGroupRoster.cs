using Academy.Api.Facilities;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public sealed record DatedRosterCount(DateOnly Date, int Count);
public static class DatedGroupRoster
{
    public static async Task<DatedRosterCount[]> Forecast(FoundationDbContext db, Guid group, DateOnly from)
    {
        var dates = db.TrainingSessions.Where(s => s.TrainingGroupId == group && s.SessionDate >= from).Select(s => s.SessionDate)
            .Union(db.EnrollmentGroupTransfers.Where(t => (t.SourceGroupId == group || t.TargetGroupId == group) && t.EffectiveDate >= from).Select(t => t.EffectiveDate))
            .Union(db.TrainingGroups.Where(g => g.Id == group).Select(g => from));
        var counts = await dates.OrderBy(d => d).Take(1001).Select(date => new DatedRosterCount(date, db.SportEnrollments.Count(e => e.IsActive && FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, date) == group))).ToArrayAsync();
        if (counts.Length > 1000) throw new FacilityRejected("نطاق جدول المجموعة أكبر من حد فحص المقاعد؛ راجعه قبل الإضافة.");
        return counts;
    }
    public static async Task<bool> CanAdd(FoundationDbContext db, Guid group, DateOnly from, int? capacity)
    {
        var maximum = (await Forecast(db, group, from)).Select(c => c.Count).DefaultIfEmpty(0).Max();
        var seats = await db.GroupResourceSchedules.Where(x => x.TrainingGroupId == group && x.IsActive).Select(x => (int?)x.ReservedSeats).SingleOrDefaultAsync();
        return (!capacity.HasValue || maximum < capacity) && (!seats.HasValue || maximum < seats);
    }
}
