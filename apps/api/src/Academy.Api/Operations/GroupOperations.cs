using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Facilities;
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
        manage.MapPost("/groups/{id:guid}/timetable/preview", async (Guid id, TimetableWrite body, ResourceTimetableService service) => await TimetableResult(() => service.Preview(id, body))).AddEndpointFilter<CsrfFilter>();
        manage.MapPost("/groups/{id:guid}/timetable", Timetable).AddEndpointFilter<CsrfFilter>();
        app.MapPut("/api/v1/people/enrollments/{id:guid}/group", Move).RequireAuthorization(AcademyPermissions.PeopleManage).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Details(Guid id, CurrentTenant tenant, FoundationDbContext db, TimeProvider time)
    {
        var t = (await tenant.ResolveAsync())!;
        var day = await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
        if (!await db.TrainingGroups.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == id)) return Results.NotFound();
        return Results.Ok(new {
            group = await db.TrainingGroups.Where(x => x.Id == id).Select(x => new { x.Id, x.BranchId, x.SportId, x.Capacity, x.ArabicName, x.TrainingLevelId, registrationFormat = x.RegistrationFormat.ToString(), x.FormatReviewedAtUtc }).SingleAsync(),
            coaches = await db.StaffGroupAssignments.Where(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == id && x.IsActive && x.AcademyMembership.IsActive && x.AcademyMembership.User.IsActive).OrderBy(x => x.AcademyMembership.User.DisplayName).Select(x => new { id = x.AcademyMembershipId, name = x.AcademyMembership.User.DisplayName }).ToListAsync(),
            players = await db.SportEnrollments.Where(x => x.AcademyId == t.AcademyId && FoundationDbContext.AssignedGroupAt(x.AcademyId, x.Id, day) == id && x.IsActive).OrderBy(x => x.Player.ArabicName).Select(x => new { x.Id, x.PlayerId, x.Player.ArabicName, x.Player.PhotoReference }).ToListAsync(),
            resourcePlan = await db.GroupResourceSchedules.Where(x => x.TrainingGroupId == id && x.IsActive).Select(x => new { x.ResourceId, x.CoachMembershipId, seats = x.ReservedSeats, x.Exclusive }).SingleOrDefaultAsync(),
            schedules = await db.RecurringSchedules.Where(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == id && x.IsActive).OrderBy(x => ((int)x.DayOfWeek + 1) % 7).Select(x => new { x.DayOfWeek, x.StartTime, x.EndTime, x.ValidFrom, x.ValidTo }).ToListAsync(),
            sessions = await db.TrainingSessions.Where(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == id && x.Status != TrainingSessionStatus.Cancelled).OrderByDescending(x => x.SessionDate).ThenBy(x => x.StartTime).Take(200).Select(x => new { x.Id, x.SessionDate, x.StartTime, x.EndTime }).ToListAsync()
        });
    }
    private static Task<IResult> Timetable(Guid id, TimetableWrite body, ResourceTimetableService service, HttpRequest request) => TimetableResult(() => service.Save(id, body, request.Headers["Idempotency-Key"].ToString()));
    private static async Task<IResult> TimetableResult(Func<Task<object>> action)
    {
        try { return Results.Ok(await action()); } catch (FacilityRejected e) { return Results.Problem(statusCode: e.Status, title: e.Message); }
    }
    private static async Task<IResult> Move(Guid id, MoveGroupWrite body, CurrentTenant tenant, FoundationDbContext db, GroupPlacementService placement, TimeProvider actualClock)
    {
        var t = (await tenant.ResolveAsync())!; await using var tx = await db.Database.BeginTransactionAsync();
        try { await placement.Authority("players", "Edit"); await placement.Authority("academy", "Edit"); }
        catch (FacilityRejected e) { return Results.Problem(statusCode: e.Status, title: e.Message); }
        var day = await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, actualClock);
        var source = await db.SportEnrollments.Where(e => e.Id == id && e.IsActive).Select(e => FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, day)).SingleOrDefaultAsync();
        if (!await db.SportEnrollments.AnyAsync(e => e.Id == id) || !await db.TrainingGroups.AnyAsync(g => g.Id == body.GroupId)) return Results.NotFound();
        // Compatibility URL cannot bypass the reviewed, dated workflow.
        return source == body.GroupId ? Results.NoContent() : Results.Conflict(new { message = "استخدم تعديل المجموعة مع تاريخ النقل ومعاينته قبل التأكيد." });
    }
}
public sealed record TimetableWrite(DateOnly From, DateOnly To, DayOfWeek[] Days, TimeOnly StartTime, TimeOnly EndTime, ResourceScheduleWrite? Resource = null, bool RemoveResource = false, string? PreviewFingerprint = null);
public sealed record MoveGroupWrite(Guid GroupId);
