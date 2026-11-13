using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Api.Slice8;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static partial class OwnerFollowUpEndpoints
{
    internal sealed record DecisionPlayer(Guid PlayerId, string Name, Guid BranchId, string Branch,
        Guid SportId, string Sport, Guid? GroupId, Guid EnrollmentId, DateOnly? Date)
    {
        public Guid? TaskId { get; init; }
    }

    public static void MapDecisionCenter(this WebApplication app)
    {
        app.MapGet("/api/v1/manage/decision-center", DecisionCenter)
            .RequireAuthorization(AcademyPermissions.OwnerDashboardRead);
    }

    private static IQueryable<SportEnrollment> DecisionEnrollments(FoundationDbContext db, Guid academy, Guid? branch) =>
        db.SportEnrollments.AsNoTracking().Where(e => e.AcademyId == academy && e.IsActive
            && e.Status == EnrollmentStatus.Active && e.Player.IsActive && e.Branch.IsActive && e.Sport.IsActive
            && (!branch.HasValue || e.BranchId == branch));

    internal static async Task<DecisionPlayer[]> DecisionPlayers(FoundationDbContext db, Guid academy,
        Guid? branch, string kind, DateOnly today, DateTimeOffset now)
    {
        var enrollments = DecisionEnrollments(db, academy, branch);
        var periods = SubscriptionHomeEndpoints.Periods(db, academy, today, branch).Where(p => p.IsActive);
        var active = SubscriptionHomeEndpoints.Active(periods, today);
        var matching = periods;
        if (kind == "Expiring")
        {
            var days = await db.NoticeTemplates.Where(n => n.AcademyId == academy && n.Key == "Expiring")
                .Select(n => n.TimingDays).SingleOrDefaultAsync() ?? 7;
            var cutoff = today.AddDays(days);
            matching = active.Where(p => p.EndDate <= cutoff && !db.SubscriptionPeriods.Any(next =>
                next.AcademyId == academy && next.SportEnrollmentId == p.SportEnrollmentId && next.IsActive
                && next.StartDate > p.StartDate && next.Status != SubscriptionPeriodStatus.Cancelled));
        }
        else if (kind == "Expired")
        {
            matching = SubscriptionHomeEndpoints.Select(periods, today, "expired").Where(p => !db.SubscriptionPeriods.Any(next =>
                next.AcademyId == academy && next.SportEnrollmentId == p.SportEnrollmentId && next.IsActive
                && next.StartDate > today && next.Status != SubscriptionPeriodStatus.Cancelled));
        }
        else if (kind == "Unassigned")
        {
            enrollments = enrollments.Where(e => FoundationDbContext.AssignedGroupAt(academy, e.Id, today) == null
                && e.RegistrationFormat == TrainingRegistrationFormat.Group);
            matching = active;
        }
        else if (kind == "Absence")
        {
            var zone = await db.Academies.Where(a => a.Id == academy).Select(a => a.TimeZone).SingleAsync();
            var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(zone));
            var time = TimeOnly.FromDateTime(local.DateTime);
            var eligible = db.TrainingSessions.AsNoTracking().Where(s => s.AcademyId == academy && s.IsActive
                && s.Status != TrainingSessionStatus.Cancelled
                && (s.SessionDate < today || s.SessionDate == today && s.EndTime <= time));
            var candidates = await enrollments.Where(e => active.Any(p => p.SportEnrollmentId == e.Id))
                .Select(e => new
                {
                    Player = new DecisionPlayer(e.PlayerId, e.Player.ArabicName, e.BranchId, e.Branch.ArabicName,
                        e.SportId, e.Sport.ArabicName, FoundationDbContext.AssignedGroupAt(academy, e.Id, today), e.Id, null),
                    Recent = eligible.Where(s => FoundationDbContext.IsSessionAssigned(academy, e.Id, s.Id))
                        .OrderByDescending(s => s.SessionDate).ThenByDescending(s => s.StartTime).ThenByDescending(s => s.Id)
                        .Take(3).Select(s => new { s.SessionDate, Absent = db.PlayerAttendances.Any(a => a.AcademyId == academy
                            && a.IsActive && a.TrainingSessionId == s.Id && a.SportEnrollmentId == e.Id && a.Status == AttendanceStatus.Absent) })
                        .ToArray()
                }).ToArrayAsync();
            return candidates.Where(c => c.Recent.Length == 3 && c.Recent.All(r => r.Absent))
                .Select(c => c.Player with { Date = c.Recent[0].SessionDate }).ToArray();
        }

        return await enrollments.Where(e => matching.Any(p => p.SportEnrollmentId == e.Id))
            .Select(e => new DecisionPlayer(e.PlayerId, e.Player.ArabicName, e.BranchId, e.Branch.ArabicName,
                e.SportId, e.Sport.ArabicName, FoundationDbContext.AssignedGroupAt(academy, e.Id, today), e.Id,
                matching.Where(p => p.SportEnrollmentId == e.Id).OrderByDescending(p => p.StartDate)
                    .ThenByDescending(p => p.Id).Select(p => p.EndDate).FirstOrDefault())).ToArrayAsync();
    }

    private static async Task<IResult> DecisionCenter(Guid? branchId, string? kind, int? page, CurrentTenant tenant,
        FoundationDbContext db, TimeProvider clock, HttpContext http)
    {
        var t = (await tenant.ResolveAsync())!;
        if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        http.Response.Headers.CacheControl = "private, no-store";
        if (branchId.HasValue && !await db.Branches.AnyAsync(b => b.AcademyId == t.AcademyId && b.IsActive && b.Id == branchId))
            return Results.NotFound();
        if (page is < 1 or > 10000 || kind is not (null or "Expiring" or "Expired" or "Absence" or "Unassigned" or "Capacity"))
            return Invalid("اختر قرارًا وصفحة صالحين.");
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        if (kind is "Expiring" or "Expired" or "Absence" or "Unassigned")
        {
            var players = await DecisionPlayers(db, t.AcademyId, branchId, kind, today, clock.GetUtcNow());
            var distinct = players.OrderBy(p => p.Date).ThenBy(p => p.Name).ThenBy(p => p.EnrollmentId).DistinctBy(p => p.PlayerId).ToArray();
            var visible = distinct.Skip(((page ?? 1) - 1) * 50).Take(50).ToArray();
            var ids = visible.Select(p => p.PlayerId).ToArray();
            var open = await db.OwnerFollowUps.AsNoTracking().Where(f => f.AcademyId == t.AcademyId
                    && f.SourceKind == "Player" && f.SourceId.HasValue && ids.Contains(f.SourceId.Value)
                    && f.State != "Closed" && f.State != "Rejected")
                .Select(f => new { f.SourceId, f.SourceBranchId, f.SourceSportId, f.Id }).ToArrayAsync();
            return Results.Ok(new { total = distinct.Length, items = visible.Select(p => p with
            {
                TaskId = open.FirstOrDefault(f => f.SourceId == p.PlayerId && f.SourceBranchId == p.BranchId
                    && f.SourceSportId == p.SportId)?.Id
            }) });
        }

        var groupIds = branchId.HasValue
            ? await db.TrainingGroups.Where(g => g.AcademyId == t.AcademyId && g.BranchId == branchId).Select(g => g.Id).ToArrayAsync()
            : null;
        var signals = (await Signals(db, t.AcademyId, today, groupIds: groupIds, includeEvidence: false))
            .Where(g => g.SourceActive && g.Capacity > 0).ToArray();
        var groups = signals.Select(g => new { g.GroupId, g.Name, g.BranchId, g.Branch, g.SportId, g.Sport, g.Capacity,
            occupied = g.Enrollments, available = Math.Max(0, g.Capacity!.Value - g.Enrollments) }).ToArray();
        if (kind == "Capacity") return Results.Ok(new { today, groups });

        async Task<int> Count(string decision) =>
            (await DecisionPlayers(db, t.AcademyId, branchId, decision, today, clock.GetUtcNow())).DistinctBy(p => p.PlayerId).Count();
        var tasks = OperationalTasks(db).Where(f => f.AcademyId == t.AcademyId && f.State != "Closed" && f.State != "Rejected"
            && (!branchId.HasValue || f.SourceBranchId == branchId
                || f.SourceKind == "Group" && db.TrainingGroups.Any(g => g.Id == f.TrainingGroupId && g.BranchId == branchId)));
        return Results.Ok(new { today, expiring = await Count("Expiring"), expired = await Count("Expired"),
            absence = await Count("Absence"), unassigned = await Count("Unassigned"),
            seats = groups.Sum(g => g.available), openTasks = await tasks.CountAsync() });
    }
}
