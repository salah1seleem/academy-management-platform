using System.Security.Cryptography;
using System.Text;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice4;

// An in-app notice only: no SMS, push delivery, or medical inference.
internal static class GuardianAbsenceNotice
{
    internal static async Task AddForNewAbsencesAsync(FoundationDbContext db, TimeProvider clock, Guid academyId,
        Guid sessionId, IReadOnlyList<Guid> enrollmentIds, CancellationToken ct)
    {
        if (enrollmentIds.Count == 0) return;
        var zone = await db.Academies.Where(x => x.Id == academyId).Select(x => x.TimeZone).SingleAsync(ct);
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(zone));
        var today = DateOnly.FromDateTime(local.DateTime);
        var time = TimeOnly.FromDateTime(local.DateTime);
        foreach (var enrollmentId in enrollmentIds.Distinct())
        {
            var recent = await db.TrainingSessions.AsNoTracking()
                .Where(s => s.AcademyId == academyId && s.Status != TrainingSessionStatus.Cancelled &&
                    (s.SessionDate < today || s.SessionDate == today && s.StartTime <= time) &&
                    FoundationDbContext.IsSessionAssigned(academyId, enrollmentId, s.Id))
                .OrderByDescending(s => s.SessionDate).ThenByDescending(s => s.StartTime).ThenByDescending(s => s.Id)
                .Take(4).Select(s => s.Id).ToArrayAsync(ct);
            if (recent.Length < 3 || recent[0] != sessionId) continue;
            var absent = await db.PlayerAttendances.AsNoTracking()
                .Where(a => a.AcademyId == academyId && a.SportEnrollmentId == enrollmentId && recent.Contains(a.TrainingSessionId) && a.Status == AttendanceStatus.Absent)
                .Select(a => a.TrainingSessionId).ToArrayAsync(ct);
            if (!recent.Take(3).All(absent.Contains) || recent.Length == 4 && absent.Contains(recent[3])) continue;
            var child = await db.SportEnrollments.Where(e => e.AcademyId == academyId && e.Id == enrollmentId)
                .Select(e => new { e.PlayerId, player = e.Player.ArabicName, sport = e.Sport.ArabicName }).SingleAsync(ct);
            var guardians = await db.GuardianPlayerLinks.Where(l => l.AcademyId == academyId && l.PlayerId == child.PlayerId && l.IsActive && l.Guardian.IsActive)
                .Select(l => l.Guardian.UserId).Distinct().ToArrayAsync(ct);
            foreach (var guardian in guardians)
            {
                var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"academy/absence-three/v1/{academyId:N}/{enrollmentId:N}/{sessionId:N}/{guardian:N}"))[..16]);
                if (await db.AcademyNotices.AnyAsync(n => n.AcademyId == academyId && n.Id == id, ct)) continue;
                var now = clock.GetUtcNow();
                db.AcademyNotices.Add(new AcademyNotice { Id = id, AcademyId = academyId, UserId = guardian, AudienceRole="Guardian",TargetKind="Contact",TargetId=child.PlayerId,
                    Message = await Academy.Api.Operations.NoticeText.Render(db,academyId,"Absence",$"تغيب {child.player} عن آخر 3 حصص متتالية في {child.sport}. للتواصل أو تقديم شكوى أو اقتراح افتح التواصل مع الأكاديمية.",child.player),
                    CreatedAtUtc = now, UpdatedAtUtc = now });
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
