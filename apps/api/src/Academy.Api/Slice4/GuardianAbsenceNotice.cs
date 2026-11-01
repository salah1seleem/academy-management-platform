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
        var activePayload=await db.AcademyConfigurationVersions.AsNoTracking().Where(c=>c.AcademyId==academyId&&c.State==Academy.Infrastructure.Tenancy.ConfigurationState.Active).Select(c=>c.Payload).SingleOrDefaultAsync(ct);
        var threshold=activePayload is null?3:Academy.Infrastructure.Tenancy.AcademyRuntimeSettings.Read(activePayload).ConsecutiveAbsenceAlert;
        var sessionDate=await db.TrainingSessions.Where(s=>s.AcademyId==academyId&&s.Id==sessionId).Select(s=>s.SessionDate).SingleAsync(ct);
        foreach (var enrollmentId in enrollmentIds.Distinct())
        {
            await Academy.Api.Operations.GuardianLifecycleNotice.Add(db,academyId,enrollmentId,"RecordedAbsence",sessionId,"Attendance",null,clock.GetUtcNow(),date:sessionDate,token:ct);
            var eligible = db.TrainingSessions.AsNoTracking()
                .Where(s => s.AcademyId == academyId && s.Status != TrainingSessionStatus.Cancelled &&
                    (s.SessionDate < today || s.SessionDate == today && s.StartTime <= time) &&
                    FoundationDbContext.IsSessionAssigned(academyId, enrollmentId, s.Id));
            // Missing, unrecorded, present, late or excused sessions break the streak.
            // Find that boundary in SQL, then fetch only the first bounded threshold.
            var boundary = await eligible.Where(s=>!db.PlayerAttendances.Any(a=>a.AcademyId==academyId&&a.SportEnrollmentId==enrollmentId&&a.TrainingSessionId==s.Id&&a.Status==AttendanceStatus.Absent))
                .OrderByDescending(s=>s.SessionDate).ThenByDescending(s=>s.StartTime).ThenByDescending(s=>s.Id)
                .Select(s=>new{s.SessionDate,s.StartTime,s.Id}).FirstOrDefaultAsync(ct);
            if(boundary is not null)eligible=eligible.Where(s=>s.SessionDate>boundary.SessionDate||s.SessionDate==boundary.SessionDate&&(s.StartTime>boundary.StartTime||s.StartTime==boundary.StartTime&&s.Id.CompareTo(boundary.Id)>0));
            var streak=await eligible.OrderBy(s=>s.SessionDate).ThenBy(s=>s.StartTime).ThenBy(s=>s.Id).Take(Math.Max(threshold,3)).Select(s=>s.Id).ToArrayAsync(ct);
            if(streak.Length<threshold)continue;
            var child = await db.SportEnrollments.Where(e => e.AcademyId == academyId && e.Id == enrollmentId)
                .Select(e => new { e.PlayerId, player = e.Player.ArabicName, sport = e.Sport.ArabicName }).SingleAsync(ct);
            var guardians = await db.GuardianPlayerLinks.Where(l => l.AcademyId == academyId && l.PlayerId == child.PlayerId && l.IsActive && l.Guardian.IsActive && l.Guardian.User.IsActive)
                .Select(l => l.Guardian.UserId).Distinct().ToArrayAsync(ct);
            foreach (var guardian in guardians)
            {
                var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"academy/absence-streak/v2/{academyId:N}/{enrollmentId:N}/{streak[0]:N}/{guardian:N}"))[..16]);
                var legacy=streak.Take(3).Select(source=>new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"academy/absence-three/v1/{academyId:N}/{enrollmentId:N}/{source:N}/{guardian:N}"))[..16])).Append(id).ToArray();
                if (await db.AcademyNotices.AnyAsync(n => n.AcademyId == academyId && legacy.Contains(n.Id), ct)) continue;
                var now = clock.GetUtcNow();
                db.AcademyNotices.Add(new AcademyNotice { EventKey="Absence", Id = id, AcademyId = academyId, UserId = guardian, AudienceRole="Guardian",TargetKind="Contact",TargetId=child.PlayerId,
                    Message = await Academy.Api.Operations.NoticeText.Render(db,academyId,"Absence",$"تغيب {child.player} عن آخر {threshold} حصص متتالية في {child.sport}. للتواصل أو تقديم شكوى أو اقتراح افتح التواصل مع الأكاديمية.",child.player,count:threshold),
                    CreatedAtUtc = now, UpdatedAtUtc = now });
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
