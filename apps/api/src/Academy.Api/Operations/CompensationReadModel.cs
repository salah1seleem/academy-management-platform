using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public sealed record CompensationBookingView(Guid Id, Guid TrainingSessionId, string Group, DateOnly Date, string State, int Units, int Consumed, uint Version);
public sealed record CompensationView(Guid Id, Guid SportEnrollmentId, string Sport, string Cause, string Unit, int Units, DateOnly ValidFrom, DateOnly ValidTo, DateOnly SourceDate, int Consumed, int Allocated, CompensationBookingView[] Bookings);
public static class CompensationReadModel
{
    public static async Task<CompensationView[]> Load(FoundationDbContext db, Guid academy, Guid[] enrollments)
    {
        var grants = await (from g in db.CompensationGrants.AsNoTracking()
            join s in db.TrainingSessions on g.SourceSessionId equals s.Id
            join sport in db.Sports on g.SportId equals sport.Id
            where g.AcademyId == academy && enrollments.Contains(g.SportEnrollmentId)
            orderby g.CreatedAtUtc descending, g.Id
            select new { g.Id, g.SportEnrollmentId, sport = sport.ArabicName, g.Cause, g.Unit, g.Units, g.ValidFrom, g.ValidTo, sourceDate = s.SessionDate }).Take(100).ToArrayAsync();
        var ids = grants.Select(g => g.Id).ToArray();
        var bookings = await (from b in db.CompensationBookings.AsNoTracking()
            join s in db.TrainingSessions on b.TrainingSessionId equals s.Id
            join trainingGroup in db.TrainingGroups on b.TrainingGroupId equals trainingGroup.Id
            where b.AcademyId == academy && ids.Contains(b.GrantId)
            orderby s.SessionDate, s.StartTime
            select new { b.Id, b.GrantId, b.TrainingSessionId, groupName = trainingGroup.ArabicName, date = s.SessionDate, b.State, b.Units, b.Version }).ToArrayAsync();
        var used = await db.CompensationMovements.AsNoTracking().Where(m => m.AcademyId == academy && ids.Contains(m.GrantId)).GroupBy(m => m.BookingId).Select(g => new { id = g.Key, units = g.Sum(m => m.Units) }).ToDictionaryAsync(g => g.id, g => g.units);
        return grants.Select(g => new CompensationView(g.Id, g.SportEnrollmentId, g.sport, g.Cause.ToString(), g.Unit.ToString(), g.Units, g.ValidFrom, g.ValidTo, g.sourceDate,
            bookings.Where(b => b.GrantId == g.Id).Sum(b => used.GetValueOrDefault(b.Id)), bookings.Where(b => b.GrantId == g.Id && b.State == CompensationBookingState.Reserved).Sum(b => b.Units),
            bookings.Where(b => b.GrantId == g.Id).Select(b => new CompensationBookingView(b.Id, b.TrainingSessionId, b.groupName, b.date, b.State.ToString(), b.Units, used.GetValueOrDefault(b.Id), b.Version)).ToArray())).ToArray();
    }
}
