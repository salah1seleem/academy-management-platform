using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice4;

public sealed class TrainingSessionGenerator(FoundationDbContext db, TimeProvider clock)
{
    public async Task<int> GenerateAsync(Guid academyId, Guid actorId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from || to.DayNumber - from.DayNumber > 30) throw new ArgumentOutOfRangeException(nameof(to));
        var schedules = await db.RecurringSchedules.AsNoTracking().Include(x => x.TrainingGroup)
            .Where(x => x.AcademyId == academyId && x.IsActive && x.TrainingGroup.IsActive).ToListAsync(ct);
        var existing = await db.TrainingSessions.AsNoTracking().Where(x => x.AcademyId == academyId && x.SessionDate >= from && x.SessionDate <= to)
            .Select(x => new { x.TrainingGroupId, x.SessionDate, x.StartTime }).ToListAsync(ct);
        var keys = existing.Select(x => (x.TrainingGroupId, x.SessionDate, x.StartTime)).ToHashSet();
        var now = clock.GetUtcNow(); var created = 0;
        foreach (var schedule in schedules)
        {
            for (var date = from; date <= to; date = date.AddDays(1))
            {
                if (date.DayOfWeek != schedule.DayOfWeek || !keys.Add((schedule.TrainingGroupId, date, schedule.StartTime))) continue;
                db.TrainingSessions.Add(new TrainingSession
                {
                    Id = Guid.NewGuid(), AcademyId = academyId, TrainingGroupId = schedule.TrainingGroupId,
                    BranchId = schedule.TrainingGroup.BranchId, SportId = schedule.TrainingGroup.SportId, AgeCategoryId = schedule.TrainingGroup.AgeCategoryId,
                    SessionDate = date, StartTime = schedule.StartTime, EndTime = schedule.EndTime, Status = TrainingSessionStatus.Scheduled,
                    Source = TrainingSessionSource.RecurringSchedule, RecurringScheduleId = schedule.Id, CreatedByUserId = actorId,
                    CreatedAtUtc = now, UpdatedAtUtc = now
                });
                created++;
            }
        }
        await db.SaveChangesAsync(ct);
        return created;
    }
}
