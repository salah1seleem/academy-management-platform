using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice4;

// Presentation only: no financial delivery, attendance or permanent assignment is created.
public sealed record SessionPresentation(string TrainingFormat, bool IsSubstitute)
{
    // Explicitly current group configuration, not an immutable historical service snapshot.
    public string TrainingFormatSource => "CurrentGroup";
}

public static class SessionPresentationReadModel
{
    // Callers supply sessions that already passed their attendance/occurrence authorization.
    public static async Task<Dictionary<Guid, SessionPresentation>> Read(FoundationDbContext db, Guid academy, Guid[] ids)
    {
        var sessions = await db.TrainingSessions.AsNoTracking()
            .Where(s => s.AcademyId == academy && ids.Contains(s.Id))
            .Select(s => new { s.Id, Format = s.TrainingGroup.RegistrationFormat }).ToArrayAsync();
        var decisions = await db.SessionCoachOverrides.AsNoTracking()
            .Where(o => o.AcademyId == academy && ids.Contains(o.TrainingSessionId))
            .Select(o => new { o.TrainingSessionId, o.Sequence, o.OriginalCoachMembershipId, o.ActualCoachMembershipId }).ToArrayAsync();
        var latest = decisions.GroupBy(o => o.TrainingSessionId).ToDictionary(g => g.Key, g => g.MaxBy(o => o.Sequence)!);
        return sessions.ToDictionary(s => s.Id, s => new SessionPresentation(s.Format.ToString(),
            latest.TryGetValue(s.Id, out var decision) && decision.ActualCoachMembershipId != decision.OriginalCoachMembershipId));
    }
}
