using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Facilities;
public sealed record FacilityRosterCounts(Dictionary<Guid,int> Sessions, Dictionary<Guid,int> Groups)
{
    // Restrict candidates before invoking the dated membership functions. A correlated
    // full-academy scan for every booking times out with a thousand-player import.
    public static async Task<FacilityRosterCounts> Read(FoundationDbContext db, Guid academy, Guid[] groups, Guid[] sessions, DateOnly day)
    {
        var sets=groups.ToDictionary(g=>g,_=>new HashSet<Guid>());
        var baseMembers=await db.SportEnrollments.AsNoTracking().Where(e=>e.AcademyId==academy && e.IsActive && e.Status==EnrollmentStatus.Active && groups.Contains(e.TrainingGroupId ?? Guid.Empty)).Select(e=>new{e.Id,e.TrainingGroupId}).ToArrayAsync();
        foreach(var e in baseMembers) sets[e.TrainingGroupId!.Value].Add(e.Id);
        var transfers=await db.EnrollmentGroupTransfers.AsNoTracking().Where(t=>t.AcademyId==academy && groups.Contains(t.TargetGroupId) && t.EffectiveDate<=day).Select(t=>new{t.SportEnrollmentId,t.TargetGroupId}).ToArrayAsync(); foreach(var t in transfers) sets[t.TargetGroupId].Add(t.SportEnrollmentId);
        var moves=await db.EnrollmentSessionMoves.AsNoTracking().Where(m=>m.AcademyId==academy && sessions.Contains(m.TargetSessionId)).Select(m=>new{m.SportEnrollmentId,m.TargetSessionId}).ToArrayAsync();
        var makeup=await db.CompensationBookings.AsNoTracking().Where(b=>b.AcademyId==academy && sessions.Contains(b.TrainingSessionId) && b.State==CompensationBookingState.Reserved).Select(b=>new{b.SportEnrollmentId,b.TrainingSessionId}).ToArrayAsync();
        var dates=await db.TrainingSessions.AsNoTracking().Where(s=>s.AcademyId==academy && sessions.Contains(s.Id)).Select(s=>new{s.Id,s.TrainingGroupId}).ToArrayAsync();
        var bySession=new Dictionary<Guid,int>(); var byGroup=new Dictionary<Guid,int>();
        foreach(var s in dates) {
            var ids=sets.GetValueOrDefault(s.TrainingGroupId,[]).Concat(moves.Where(m=>m.TargetSessionId==s.Id).Select(m=>m.SportEnrollmentId)).Concat(makeup.Where(m=>m.TrainingSessionId==s.Id).Select(m=>m.SportEnrollmentId)).Distinct().ToArray();
            bySession[s.Id]=await db.SportEnrollments.CountAsync(e=>e.AcademyId==academy && ids.Contains(e.Id) && e.IsActive && e.Player.IsActive && e.Status==EnrollmentStatus.Active && FoundationDbContext.IsSessionAssigned(e.AcademyId,e.Id,s.Id));
        }
        foreach(var g in groups) {
            var ids=sets[g].ToArray(); byGroup[g]=await db.SportEnrollments.CountAsync(e=>e.AcademyId==academy && ids.Contains(e.Id) && e.IsActive && e.Player.IsActive && e.Status==EnrollmentStatus.Active && FoundationDbContext.AssignedGroupAt(e.AcademyId,e.Id,day)==g);
        }
        return new(bySession,byGroup);
    }
}
