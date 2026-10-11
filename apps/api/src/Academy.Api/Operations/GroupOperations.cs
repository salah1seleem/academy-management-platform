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
    private static async Task<IResult> Details(Guid id, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        if (!await db.TrainingGroups.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == id)) return Results.NotFound();
        return Results.Ok(new {
            group = await db.TrainingGroups.Where(x => x.Id == id).Select(x => new { x.Id, x.BranchId, x.SportId, x.Capacity, x.ArabicName, x.TrainingLevelId, registrationFormat = x.RegistrationFormat.ToString(), x.FormatReviewedAtUtc }).SingleAsync(),
            coaches = await db.StaffGroupAssignments.Where(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == id && x.IsActive && x.AcademyMembership.IsActive && x.AcademyMembership.User.IsActive).OrderBy(x => x.AcademyMembership.User.DisplayName).Select(x => new { id = x.AcademyMembershipId, name = x.AcademyMembership.User.DisplayName }).ToListAsync(),
            players = await db.SportEnrollments.Where(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == id && x.IsActive).OrderBy(x => x.Player.ArabicName).Select(x => new { x.Id, x.PlayerId, x.Player.ArabicName, x.Player.PhotoReference }).ToListAsync(),
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
    private static async Task<IResult> Move(Guid id, MoveGroupWrite body, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock, GroupPlacementService placement, TimeProvider actualClock)
    {
        var t = (await tenant.ResolveAsync())!; await using var transaction = await db.Database.BeginTransactionAsync();
        try { await placement.Authority("players", "Edit"); } catch (FacilityRejected e) { return Results.Problem(statusCode: e.Status, title: e.Message); }
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"group-capacity:{t.AcademyId}:{body.GroupId}"}, 0))");
        var enrollment = await db.SportEnrollments.FromSqlInterpolated($"SELECT *, xmin FROM \"SportEnrollments\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\"={id} FOR UPDATE").Include(x => x.Player).SingleOrDefaultAsync(x => x.IsActive);
        var group = await db.TrainingGroups.FromSqlInterpolated($"SELECT *, xmin FROM \"TrainingGroups\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\"={body.GroupId} FOR UPDATE").Include(x => x.AgeCategory).SingleOrDefaultAsync(x => x.IsActive && x.Branch.IsActive && x.Sport.IsActive);
        if (enrollment is null || group is null) return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Players\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\"={enrollment.PlayerId} FOR UPDATE");
        try { await placement.Authority("players", "Edit", group.BranchId); } catch (FacilityRejected e) { return Results.Problem(statusCode: e.Status, title: e.Message); }
        if (enrollment.TrainingGroupId is null) return Results.Conflict(new { message = "استخدم مسار التسكين المراجع؛ تعديل المجموعة مخصص للتسجيل المسكن بالفعل." });
        if (enrollment.RegistrationFormat != group.RegistrationFormat) return Results.Conflict(new { message = "نمط تدريب المجموعة لا يطابق نوع التسجيل." });
        if (group.RegistrationFormat != Academy.Infrastructure.People.TrainingRegistrationFormat.Group && group.Id != enrollment.TrainingGroupId) return Results.Conflict(new { message = "نقل التدريب الخاص وشبه الخاص يحتاج معاينة النقل المؤرخة؛ لا يتاح عبر مسار النقل القديم." });
        if (group.SportId != enrollment.SportId || group.BranchId != enrollment.BranchId || group.AgeCategory.MinimumBirthYear > enrollment.Player.DateOfBirth.Year || group.AgeCategory.MaximumBirthYear < enrollment.Player.DateOfBirth.Year) return Results.UnprocessableEntity(new { message = "المجموعة لا تطابق رياضة وفرع وعمر اللاعب." });
        if (group.TrainingLevelId.HasValue && (enrollment.TrainingLevelId != group.TrainingLevelId || enrollment.LevelReviewedByUserId is null || enrollment.LevelReviewedAtUtc is null)) return Results.Conflict(new { message = "مستوى اللاعب المعتمد لا يطابق المجموعة." });
        if (group.Id != enrollment.TrainingGroupId && group.Capacity.HasValue && await db.SportEnrollments.CountAsync(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == group.Id && x.IsActive) >= group.Capacity) return Results.Conflict(new { message = "المجموعة مكتملة العدد." });
        if (group.Id != enrollment.TrainingGroupId)
        {
            var reserved = await db.GroupResourceSchedules.Where(x => x.TrainingGroupId == group.Id && x.IsActive).Select(x => (int?)x.ReservedSeats).SingleOrDefaultAsync();
            if (reserved.HasValue && await db.SportEnrollments.CountAsync(x => x.TrainingGroupId == group.Id && x.IsActive) >= reserved) return Results.Conflict(new { message = "المقاعد المحجوزة في الحارة لا تكفي." });
            var today = await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, actualClock);
            var slots = await db.TrainingSessions.Where(x => x.TrainingGroupId == group.Id && x.SessionDate >= today && x.Status == TrainingSessionStatus.Scheduled).ToArrayAsync();
            var otherGroups = await db.SportEnrollments.IgnoreQueryFilters(["trusted-branches"]).Where(x => x.AcademyId == t.AcademyId && x.PlayerId == enrollment.PlayerId && x.Id != id && x.IsActive && x.TrainingGroupId != null).Select(x => x.TrainingGroupId!.Value).ToArrayAsync();
            var otherSlots = await db.TrainingSessions.IgnoreQueryFilters(["trusted-branches"]).Where(x => x.AcademyId == t.AcademyId && otherGroups.Contains(x.TrainingGroupId) && x.SessionDate >= today && x.Status == TrainingSessionStatus.Scheduled).ToArrayAsync();
            if (slots.Any(s => otherSlots.Any(o => o.SessionDate == s.SessionDate && o.StartTime < s.EndTime && s.StartTime < o.EndTime))) return Results.Conflict(new { message = "موعد اللاعب يتعارض مع حصة أخرى." });
        }
        enrollment.TrainingGroupId = group.Id; enrollment.UpdatedAtUtc = clock.UtcNow; await db.SaveChangesAsync(); await transaction.CommitAsync(); return Results.NoContent();
    }
}
public sealed record TimetableWrite(DateOnly From, DateOnly To, DayOfWeek[] Days, TimeOnly StartTime, TimeOnly EndTime, ResourceScheduleWrite? Resource = null, bool RemoveResource = false, string? PreviewFingerprint = null);
public sealed record MoveGroupWrite(Guid GroupId);
