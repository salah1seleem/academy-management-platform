using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice4;

// Attendance follows the real academy day, independently of the frozen subscription demo clock.
public static class AttendanceCalendar
{
    public static async Task<DateOnly> TodayAsync(Guid academyId, FoundationDbContext db, TimeProvider clock, CancellationToken ct = default)
    {
        var zone = await db.Academies.Where(x => x.Id == academyId).Select(x => x.TimeZone).SingleAsync(ct);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);
    }
}
