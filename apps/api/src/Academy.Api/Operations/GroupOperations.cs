using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class GroupOperations
{
    public static void MapGroupOperations(this WebApplication app)
    {
        var manage = app.MapGroup("/api/v1/manage").RequireAuthorization(AcademyPermissions.StructureManage);
        manage.MapGet("/groups/{id:guid}/details", Details);
        manage.MapPost("/groups/{id:guid}/timetable", Timetable).AddEndpointFilter<CsrfFilter>();
        app.MapPut("/api/v1/people/enrollments/{id:guid}/group", Move).RequireAuthorization(AcademyPermissions.PeopleManage).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Details(Guid id, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        if (!await db.TrainingGroups.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == id)) return Results.NotFound();
        return Results.Ok(new {
            coaches = await db.StaffGroupAssignments.Where(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == id && x.IsActive && x.AcademyMembership.IsActive && x.AcademyMembership.User.IsActive).OrderBy(x => x.AcademyMembership.User.DisplayName).Select(x => new { id = x.AcademyMembershipId, name = x.AcademyMembership.User.DisplayName }).ToListAsync(),
            players = await db.SportEnrollments.Where(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == id && x.IsActive).OrderBy(x => x.Player.ArabicName).Select(x => new { x.Id, x.PlayerId, x.Player.ArabicName, x.Player.PhotoReference }).ToListAsync(),
            schedules = await db.RecurringSchedules.Where(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == id && x.IsActive).OrderBy(x => ((int)x.DayOfWeek + 1) % 7).Select(x => new { x.DayOfWeek, x.StartTime, x.EndTime, x.ValidFrom, x.ValidTo }).ToListAsync(),
            sessions = await db.TrainingSessions.Where(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == id && x.Status != TrainingSessionStatus.Cancelled).OrderByDescending(x => x.SessionDate).ThenBy(x => x.StartTime).Take(200).Select(x => new { x.Id, x.SessionDate, x.StartTime, x.EndTime }).ToListAsync()
        });
    }
    private static async Task<IResult> Timetable(Guid id, TimetableWrite body, CurrentTenant tenant, FoundationDbContext db, ClaimsPrincipal principal, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!;
        if (body.To < body.From || body.To.DayNumber - body.From.DayNumber > 366 || body.From < clock.Today || body.Days.Length is < 1 or > 7 || body.Days.Distinct().Count() != body.Days.Length || body.Days.Any(x => !Enum.IsDefined(x)) || body.StartTime >= body.EndTime) return Results.UnprocessableEntity(new { message = "اختر أيامًا وتوقيتًا صحيحًا، وفترة مستقبلية لا تتجاوز سنة." });
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"timetable:{t.AcademyId}:{id}"}, 0))");
        var group = await db.TrainingGroups.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.IsActive); if (group is null) return Results.NotFound();
        var actor = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var oldSchedules = await db.RecurringSchedules.Where(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == id).ToListAsync();
        foreach (var row in oldSchedules) { row.IsActive = false; row.UpdatedAtUtc = clock.UtcNow; }
        // Keep any recorded session, even when later edited. Never rewrite attendance history.
        var sessions = await db.TrainingSessions.Where(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == id && x.SessionDate >= body.From).ToListAsync();
        var recorded = await db.PlayerAttendances.Where(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == id && x.Status != AttendanceStatus.NotRecorded).Select(x => x.TrainingSessionId).Union(db.StaffAttendances.Where(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == id && x.Status != AttendanceStatus.NotRecorded).Select(x => x.TrainingSessionId)).ToListAsync();
        foreach (var row in sessions.Where(x => !recorded.Contains(x.Id))) { row.Status = TrainingSessionStatus.Cancelled; row.UpdatedAtUtc = clock.UtcNow; }
        var created = 0;
        foreach (var day in body.Days)
        {
            var schedule = oldSchedules.SingleOrDefault(x => x.DayOfWeek == day && x.StartTime == body.StartTime);
            if (schedule is null) { schedule = new RecurringSchedule { Id = Guid.NewGuid(), AcademyId = t.AcademyId, TrainingGroupId = id, DayOfWeek = day, StartTime = body.StartTime, CreatedAtUtc = clock.UtcNow }; db.RecurringSchedules.Add(schedule); }
            schedule.EndTime = body.EndTime; schedule.ValidFrom = body.From; schedule.ValidTo = body.To; schedule.IsActive = true; schedule.UpdatedAtUtc = clock.UtcNow;
            for (var date = body.From; date <= body.To; date = date.AddDays(1))
            {
                if (date.DayOfWeek != day) continue;
                var row = sessions.SingleOrDefault(x => x.SessionDate == date && x.StartTime == body.StartTime);
                if (row is not null && recorded.Contains(row.Id)) continue;
                if (row is null) { row = new TrainingSession { Id = Guid.NewGuid(), AcademyId = t.AcademyId, TrainingGroupId = id, BranchId = group.BranchId, SportId = group.SportId, AgeCategoryId = group.AgeCategoryId, CreatedByUserId = actor, CreatedAtUtc = clock.UtcNow }; db.TrainingSessions.Add(row); }
                row.SessionDate = date; row.StartTime = body.StartTime; row.EndTime = body.EndTime; row.Status = TrainingSessionStatus.Scheduled; row.Source = TrainingSessionSource.RecurringSchedule; row.RecurringScheduleId = schedule.Id; row.UpdatedAtUtc = clock.UtcNow; created++;
            }
        }
        await db.SaveChangesAsync(); await transaction.CommitAsync(); return Results.Ok(new { created, preserved = sessions.Count(x => recorded.Contains(x.Id)) });
    }
    private static async Task<IResult> Move(Guid id, MoveGroupWrite body, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"group-capacity:{t.AcademyId}:{body.GroupId}"}, 0))");
        var enrollment = await db.SportEnrollments.Include(x => x.Player).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.IsActive);
        var group = await db.TrainingGroups.Include(x => x.AgeCategory).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == body.GroupId && x.IsActive && x.Branch.IsActive && x.Sport.IsActive);
        if (enrollment is null || group is null) return Results.NotFound();
        if (group.SportId != enrollment.SportId || group.BranchId != enrollment.BranchId || group.AgeCategory.MinimumBirthYear > enrollment.Player.DateOfBirth.Year || group.AgeCategory.MaximumBirthYear < enrollment.Player.DateOfBirth.Year) return Results.UnprocessableEntity(new { message = "المجموعة لا تطابق رياضة وفرع وعمر اللاعب." });
        if (group.Id != enrollment.TrainingGroupId && group.Capacity.HasValue && await db.SportEnrollments.CountAsync(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == group.Id && x.IsActive) >= group.Capacity) return Results.Conflict(new { message = "المجموعة مكتملة العدد." });
        enrollment.TrainingGroupId = group.Id; enrollment.UpdatedAtUtc = clock.UtcNow; await db.SaveChangesAsync(); await transaction.CommitAsync(); return Results.NoContent();
    }
}
public sealed record TimetableWrite(DateOnly From, DateOnly To, DayOfWeek[] Days, TimeOnly StartTime, TimeOnly EndTime);
public sealed record MoveGroupWrite(Guid GroupId);
