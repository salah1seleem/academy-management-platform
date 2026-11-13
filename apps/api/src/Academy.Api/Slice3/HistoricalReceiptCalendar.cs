using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice3;

internal static class HistoricalReceiptCalendar
{
    internal const string Provider = "HistoricalImport";

    internal static async Task<TimeZoneInfo> AcademyZone(FoundationDbContext db, Guid academy) =>
        TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.AsNoTracking()
            .Where(a => a.Id == academy).Select(a => a.TimeZone).SingleAsync());

    internal static DateOnly PaidOn(DateTimeOffset paidAt, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(paidAt, zone).DateTime);
}
