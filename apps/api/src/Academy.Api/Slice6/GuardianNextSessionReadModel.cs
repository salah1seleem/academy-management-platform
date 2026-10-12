using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice6;

public sealed record GuardianNextSession(Guid Id, string Group, DateOnly SessionDate, TimeOnly StartTime, TimeOnly EndTime, string? Resource);
public static class GuardianNextSessionReadModel
{
    // Candidate groups/occurrences are derived only from this guardian's authorized
    // enrollment IDs. The authoritative function still decides actual membership.
    public static async Task<Dictionary<Guid,GuardianNextSession?>> Read(FoundationDbContext db, Guid academy, Guid[] enrollmentIds, DateOnly today)
    {
        var enrollments=await db.SportEnrollments.AsNoTracking().Where(e=>e.AcademyId==academy&&enrollmentIds.Contains(e.Id)).Select(e=>new{e.Id,e.TrainingGroupId}).ToArrayAsync();
        var transfers=await db.EnrollmentGroupTransfers.AsNoTracking().Where(t=>t.AcademyId==academy&&enrollmentIds.Contains(t.SportEnrollmentId)).Select(t=>new{t.SportEnrollmentId,t.TargetGroupId}).ToArrayAsync();
        var moves=await db.EnrollmentSessionMoves.AsNoTracking().Where(m=>m.AcademyId==academy&&enrollmentIds.Contains(m.SportEnrollmentId)).Select(m=>new{m.SportEnrollmentId,m.TargetSessionId}).ToArrayAsync();
        var makeup=await db.CompensationBookings.AsNoTracking().Where(b=>b.AcademyId==academy&&enrollmentIds.Contains(b.SportEnrollmentId)&&b.State==Academy.Infrastructure.People.CompensationBookingState.Reserved).Select(b=>new{b.SportEnrollmentId,b.TrainingSessionId}).ToArrayAsync();
        var result=new Dictionary<Guid,GuardianNextSession?>();
        foreach(var e in enrollments) {
            var groups=transfers.Where(t=>t.SportEnrollmentId==e.Id).Select(t=>t.TargetGroupId).Concat(e.TrainingGroupId.HasValue?[e.TrainingGroupId.Value]:Array.Empty<Guid>()).Distinct().ToArray();
            var sessions=moves.Where(m=>m.SportEnrollmentId==e.Id).Select(m=>m.TargetSessionId).Concat(makeup.Where(m=>m.SportEnrollmentId==e.Id).Select(m=>m.TrainingSessionId)).Distinct().ToArray();
            result[e.Id]=await db.TrainingSessions.AsNoTracking().Where(s=>s.AcademyId==academy&&(groups.Contains(s.TrainingGroupId)||sessions.Contains(s.Id))&&s.TrainingGroup.IsActive&&s.SessionDate>=today&&s.Status==TrainingSessionStatus.Scheduled&&FoundationDbContext.IsSessionAssigned(academy,e.Id,s.Id)).OrderBy(s=>s.SessionDate).ThenBy(s=>s.StartTime).ThenBy(s=>s.Id).Select(s=>new GuardianNextSession(s.Id,s.TrainingGroup.ArabicName,s.SessionDate,s.StartTime,s.EndTime,(from b in db.ResourceBookings join r in db.FacilityResources on b.ResourceId equals r.Id join p in db.FacilitySpaces on r.FacilitySpaceId equals p.Id where b.TrainingSessionId==s.Id&&b.State==Academy.Infrastructure.Facilities.ResourceBookingState.Reserved select p.Name+" · "+r.Name).FirstOrDefault())).FirstOrDefaultAsync();
        }
        return result;
    }
}
