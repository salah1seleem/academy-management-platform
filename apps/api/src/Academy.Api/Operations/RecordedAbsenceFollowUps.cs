using System.Text.Json.Serialization;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static partial class OwnerFollowUpEndpoints
{
    private const string RecordedAbsenceCondition = "GroupRecordedAbsences";
    internal sealed record RecordedAbsenceEvidence(Guid AttendanceId, Guid SessionId, Guid SportEnrollmentId,
        Guid PlayerId, DateOnly SessionDate, TimeOnly StartTime, AttendanceStatus Status);
    internal sealed record RecordedAbsenceSummary(int PlayerCount, int EnrollmentCount, int SessionCount,
        DateOnly? FromDate, DateOnly? ToDate, DateTimeOffset AsOfUtc,
        [property: JsonIgnore] RecordedAbsenceEvidence[] Evidence);

    private static IQueryable<PlayerAttendance> RecordedAbsenceAttendance(
        FoundationDbContext db, Guid academy, Guid[] groups, DateOnly today, TimeOnly localTime)
    {
        // Actual recorded eligible attendance only. Never turn a missing register
        // into absence. A cancelled, archived, future or unassigned session is not evidence.
        return db.PlayerAttendances.AsNoTracking().Where(a => a.AcademyId == academy
            && groups.Contains(a.TrainingGroupId) && a.IsActive && a.Status != AttendanceStatus.NotRecorded
            && a.SportEnrollment.IsActive && a.SportEnrollment.Player.IsActive
            && a.TrainingSession.IsActive && a.TrainingSession.Status != TrainingSessionStatus.Cancelled
            && a.TrainingGroupId == a.TrainingSession.TrainingGroupId
            && (a.TrainingSession.SessionDate < today || a.TrainingSession.SessionDate == today && a.TrainingSession.StartTime <= localTime)
            && FoundationDbContext.IsSessionAssigned(academy, a.SportEnrollmentId, a.TrainingSessionId));
    }

    internal static async Task<int> RecordedAbsenceGroupCount(
        FoundationDbContext db, Guid academy, Guid[] groups, DateOnly today, TimeOnly localTime)
    {
        if(groups.Length==0)return 0;
        var eligible=RecordedAbsenceAttendance(db,academy,groups,today,localTime);
        // Home needs counts only; never load child names, notes or evidence IDs.
        var recent=await eligible.TagWith("OwnerHome.RecordedAbsenceCounts")
            .Select(a=>new{a.TrainingGroupId,a.SportEnrollmentId}).Distinct()
            .Select(p=>new{p.TrainingGroupId,Statuses=eligible.Where(a=>a.TrainingGroupId==p.TrainingGroupId&&a.SportEnrollmentId==p.SportEnrollmentId)
                .OrderByDescending(a=>a.TrainingSession.SessionDate).ThenByDescending(a=>a.TrainingSession.StartTime).ThenByDescending(a=>a.TrainingSessionId)
                .Take(3).Select(a=>a.Status).ToArray()}).ToArrayAsync();
        return recent.Where(r=>r.Statuses.Length==3&&r.Statuses.All(s=>s==AttendanceStatus.Absent)).Select(r=>r.TrainingGroupId).Distinct().Count();
    }

    private static async Task<Dictionary<Guid, RecordedAbsenceSummary>> RecordedAbsenceSources(
        FoundationDbContext db, Guid academy, Guid[] groups, DateOnly today, TimeOnly localTime, DateTimeOffset now)
    {
        var eligible=RecordedAbsenceAttendance(db,academy,groups,today,localTime);
        // Bounded per enrollment/group at the database, not an in-memory scan of
        // every historical register. The historical group owns the evidence.
        var recent = await eligible.Select(a => new { a.TrainingGroupId, a.SportEnrollmentId }).Distinct()
            .Select(p => new {
                p.TrainingGroupId,
                Records = eligible.Where(a => a.TrainingGroupId == p.TrainingGroupId && a.SportEnrollmentId == p.SportEnrollmentId)
                    .OrderByDescending(a => a.TrainingSession.SessionDate).ThenByDescending(a => a.TrainingSession.StartTime).ThenByDescending(a => a.TrainingSessionId)
                    .Take(3).Select(a => new RecordedAbsenceEvidence(a.Id, a.TrainingSessionId, a.SportEnrollmentId,
                        a.SportEnrollment.PlayerId, a.TrainingSession.SessionDate, a.TrainingSession.StartTime, a.Status)).ToArray()
            }).ToArrayAsync();
        return groups.ToDictionary(g => g, g => {
            var rows = recent.Where(r => r.TrainingGroupId == g && r.Records.Length == 3 && r.Records.All(a => a.Status == AttendanceStatus.Absent))
                .SelectMany(r => r.Records).ToArray();
            return new RecordedAbsenceSummary(rows.Select(r => r.PlayerId).Distinct().Count(), rows.Select(r => r.SportEnrollmentId).Distinct().Count(),
                rows.Select(r => r.SessionId).Distinct().Count(), rows.Select(r => (DateOnly?)r.SessionDate).Min(), rows.Select(r => (DateOnly?)r.SessionDate).Max(), now, rows);
        });
    }
}
