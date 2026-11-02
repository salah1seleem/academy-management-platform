using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static partial class GuardianFeedbackEndpoints
{
    // Only occurrence evidence is suggested. Current group assignment is not historical evidence.
    internal sealed record IncidentCoach(Guid Id, string Name, string Source, Guid EvidenceId);
    internal sealed record IncidentSession(Guid Id, Guid GroupId, Guid BranchId, string GroupName, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, IncidentCoach[] Coaches);
    internal static async Task<IncidentSession[]> IncidentSessions(FoundationDbContext db, Guid academy, Guid child, DateOnly day)
    {
        var end = await GroupMediaPrivacy.EventEnd(db, academy, day);
        var candidates = db.SportEnrollments.AsNoTracking().Where(e => e.AcademyId == academy && e.PlayerId == child && e.IsActive && e.Player.IsActive && e.Player.DateOfBirth <= day && e.Branch.IsActive && e.Sport.IsActive && (e.GroupAssignedFrom != null || e.CreatedAtUtc > DateTimeOffset.MinValue && e.CreatedAtUtc < end));
        var sessions = await db.TrainingSessions.AsNoTracking().Where(s => s.AcademyId == academy && s.IsActive && s.SessionDate == day && s.Status != TrainingSessionStatus.Cancelled
            && candidates.Any(e => FoundationDbContext.IsSessionAssigned(academy, e.Id, s.Id)))
            .OrderBy(s => s.StartTime).ThenBy(s => s.Id).Take(51)
            .Select(s => new { s.Id, s.TrainingGroupId, s.BranchId, name = s.TrainingGroup.ArabicName, s.StartTime, s.EndTime }).ToArrayAsync();
        var ids = sessions.Select(s => s.Id).ToArray();
        var decisions = await db.SessionCoachOverrides.AsNoTracking().Where(o => o.AcademyId == academy && ids.Contains(o.TrainingSessionId)
            && !db.SessionCoachOverrides.Any(n => n.AcademyId == academy && n.TrainingSessionId == o.TrainingSessionId && n.OriginalCoachMembershipId == o.OriginalCoachMembershipId && n.Sequence > o.Sequence))
            .Select(o => new { o.Id, o.TrainingSessionId, o.OriginalCoachMembershipId, o.ActualCoachMembershipId }).ToArrayAsync();
        var recorded = await db.StaffAttendances.AsNoTracking().Where(a => a.AcademyId == academy && ids.Contains(a.TrainingSessionId) && a.IsActive
            && a.Status == AttendanceStatus.Present && a.AcademyMembership.Role == AcademyRole.Coach)
            .Select(a => new { a.Id, a.TrainingSessionId, a.AcademyMembershipId }).ToArrayAsync();
        var members = decisions.Select(d => d.ActualCoachMembershipId).Concat(recorded.Select(a => a.AcademyMembershipId)).Distinct().ToArray();
        var names = await db.AcademyMemberships.AsNoTracking().Where(m => m.AcademyId == academy && members.Contains(m.Id) && m.Role == AcademyRole.Coach).Select(m => new { m.Id, m.User.DisplayName }).ToDictionaryAsync(m => m.Id, m => m.DisplayName);
        return sessions.Select(s => {
            var replacements = decisions.Where(d => d.TrainingSessionId == s.Id).ToArray();
            var coaches = replacements.Where(d => names.ContainsKey(d.ActualCoachMembershipId)).Select(d => new IncidentCoach(d.ActualCoachMembershipId, names[d.ActualCoachMembershipId], "OccurrenceDecision", d.Id))
                .Concat(recorded.Where(a => a.TrainingSessionId == s.Id && names.ContainsKey(a.AcademyMembershipId) && !replacements.Any(d => d.OriginalCoachMembershipId == a.AcademyMembershipId))
                    .Select(a => new IncidentCoach(a.AcademyMembershipId, names[a.AcademyMembershipId], "OccurrenceAttendance", a.Id))).DistinctBy(c => c.Id).ToArray();
            return new IncidentSession(s.Id, s.TrainingGroupId, s.BranchId, s.name, day, s.StartTime, s.EndTime, coaches);
        }).ToArray();
    }
    private static async Task<IResult> IncidentOptions(Guid playerId, DateOnly incidentOn, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock, HttpContext h)
    {
        h.Response.Headers.CacheControl = "private, no-store";
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.Guardian) return Results.Forbid();
        if (!await Linked(db, t.AcademyId, Actor(principal), playerId)) return Results.NotFound();
        if (incidentOn == DateOnly.MinValue || incidentOn > await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock)) return Results.UnprocessableEntity(new { message = "اختر تاريخ الواقعة حتى اليوم الفعلي." });
        var sessions = await IncidentSessions(db, t.AcademyId, playerId, incidentOn);
        return Results.Ok(new { sessions = sessions.Take(50).Select(s => new { s.Id, s.GroupId, s.GroupName, s.Date, s.StartTime, s.EndTime, coaches = s.Coaches.Select(c => new { c.Id, c.Name, c.Source }) }), hasMore = sessions.Length > 50, notice = "اقتراح من قرار بديل أو سجل حصة، وليس إثبات حضور فعلي أو اتهامًا. عند غياب دليل لا نخمن المدرب الحالي؛ يمكنك اختيار لا أعرف." });
    }
    internal static string IncidentSnapshot(IncidentSession s, Guid? selected) => JsonSerializer.Serialize(new { sessionId = s.Id, groupName = s.GroupName, date = s.Date, startTime = s.StartTime, endTime = s.EndTime, selectedCoachId = selected, coaches = s.Coaches.Select(c => new { id = c.Id, name = c.Name, source = c.Source, evidenceId = c.EvidenceId }) });
}
