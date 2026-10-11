using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Slice2;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Facilities;
public static class ResourceTimetableDemoSeed
{
    public static readonly Guid GroupId = Guid.Parse("58000000-0000-0000-0000-000000000100");
    // Explicit opt-in in Demo/LegacyRegression ONLY. Never update/reset the owner's edits.
    public static async Task Seed(FoundationDbContext db)
    {
        var academy = DemoSeed.NogoomAcademyId;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + academy}, 0))");
        if (await db.TrainingGroups.AnyAsync(x => x.Id == GroupId) || !await db.Academies.AnyAsync(x => x.Id == academy && x.Slug == "nogoom-demo")) return;
        var resourceId = Guid.Parse("58000000-0000-0000-0000-000000000010");
        var resource = await db.FacilityResources.SingleOrDefaultAsync(x => x.Id == resourceId && x.AcademyId == academy && x.IsActive && x.ApprovedAtUtc != null);
        if (resource is null || resource.Capacity < 4) return;
        var config = await db.AcademyConfigurationVersions.Where(x => x.AcademyId == academy && x.State == ConfigurationState.Active).Select(x => x.Payload).SingleAsync(); var settings = AcademyRuntimeSettings.Read(config);
        if (!settings.Features.GetValueOrDefault("facilities") || !settings.Features.GetValueOrDefault("training") || await db.AcademyFeatureEntitlements.CountAsync(x => x.AcademyId == academy && (x.Feature == "facilities" || x.Feature == "training") && x.Enabled && x.IsActive) != 2) return;
        var coach = await db.AcademyMemberships.Where(x => x.AcademyId == academy && x.User.Email == DemoSeed.CoachEmail && x.IsActive).Select(x => x.Id).SingleAsync();
        var owner = await db.Users.Where(x => x.Email == DemoSeed.OwnerEmail).Select(x => x.Id).SingleAsync();
        var guardian = await db.GuardianProfiles.Where(x => x.AcademyId == academy && x.User.PhoneNumber == DemoSeed.GuardianPhone).Select(x => x.Id).SingleAsync();
        var dates = new[] { new DateOnly(2026, 11, 14), new(2026, 11, 16), new(2026, 11, 21), new(2026, 11, 23) }; var start = new TimeOnly(19, 0); var end = new TimeOnly(20, 0);
        // Do not introduce a synthetic clash if the reviewer has changed this demo resource/coach.
        if (await db.ResourceBookings.AnyAsync(x => x.ResourceId == resourceId && dates.Contains(x.Date) && x.State == ResourceBookingState.Reserved && x.StartTime < end && start < x.EndTime) || await db.TrainingSessions.AnyAsync(x => x.AcademyId == academy && dates.Contains(x.SessionDate) && x.Status != TrainingSessionStatus.Cancelled && x.StartTime < end && start < x.EndTime && db.StaffGroupAssignments.Any(a => a.TrainingGroupId == x.TrainingGroupId && a.AcademyMembershipId == coach && a.IsActive))) return;
        if (dates.Any(d => !resource.OperatingDays.Contains((int)d.DayOfWeek)) || start < resource.OpensAt || end > resource.ClosesAt || !resource.ServiceKinds.Contains("Training")) return;
        var firstDate = dates[0]; var lastDate = dates[^1];
        if (await db.ResourceClosures.AnyAsync(x => x.ResourceId == resourceId && x.IsActive && x.FromDate <= lastDate && x.ToDate >= firstDate && x.StartTime < end && start < x.EndTime)) return;
        var now = DateTimeOffset.Parse("2026-09-28T06:00:00Z"); var branch = Slice2DemoSeed.FifthBranchId;
        db.TrainingGroups.Add(new() { Id = GroupId, AcademyId = academy, ArabicName = "حارة A — جدول تجريبي مرتبط", BranchId = branch, SportId = Slice2DemoSeed.SwimmingId, AgeCategoryId = Slice2DemoSeed.U10Id, Capacity = 4, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.StaffGroupAssignments.Add(new() { Id = Guid.Parse("58000000-0000-0000-0000-000000000101"), AcademyId = academy, AcademyMembershipId = coach, TrainingGroupId = GroupId, CreatedAtUtc = now, UpdatedAtUtc = now });
        var player = Guid.Parse("58000000-0000-0000-0000-000000000102");
        db.Players.Add(new() { Id = player, AcademyId = academy, PlayerCode = "DEMO-LANE-001", ArabicName = "لاعب الحارة التجريبي", DateOfBirth = new(2018, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now });
        db.SportEnrollments.Add(new() { Id = Guid.Parse("58000000-0000-0000-0000-000000000103"), AcademyId = academy, PlayerId = player, SportId = Slice2DemoSeed.SwimmingId, BranchId = branch, TrainingGroupId = GroupId, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.GuardianPlayerLinks.Add(new() { Id = Guid.Parse("58000000-0000-0000-0000-000000000104"), AcademyId = academy, PlayerId = player, GuardianId = guardian, CreatedByUserId = owner, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.GroupResourceSchedules.Add(new() { Id = Guid.Parse("58000000-0000-0000-0000-000000000105"), AcademyId = academy, BranchId = branch, TrainingGroupId = GroupId, ResourceId = resourceId, CoachMembershipId = coach, ReservedSeats = 4, Exclusive = false, ValidFrom = dates[0], ValidTo = dates[^1], CreatedAtUtc = now, UpdatedAtUtc = now });
        foreach (var weekday in new[] { DayOfWeek.Saturday, DayOfWeek.Monday })
        {
            var schedule = new RecurringSchedule { Id = Guid.Parse($"58000000-0000-0000-0000-{110 + (int)weekday:D12}"), AcademyId = academy, TrainingGroupId = GroupId, DayOfWeek = weekday, StartTime = start, EndTime = end, ValidFrom = dates[0], ValidTo = dates[^1], CreatedAtUtc = now, UpdatedAtUtc = now }; db.RecurringSchedules.Add(schedule);
            foreach (var date in dates.Where(d => d.DayOfWeek == weekday))
            {
                var session = Guid.Parse($"58000000-0000-0000-0000-{120 + date.Day:D12}");
                db.TrainingSessions.Add(new() { Id = session, AcademyId = academy, TrainingGroupId = GroupId, BranchId = branch, SportId = Slice2DemoSeed.SwimmingId, AgeCategoryId = Slice2DemoSeed.U10Id, SessionDate = date, StartTime = start, EndTime = end, Source = TrainingSessionSource.RecurringSchedule, RecurringScheduleId = schedule.Id, CreatedByUserId = owner, CreatedAtUtc = now, UpdatedAtUtc = now });
                db.ResourceBookings.Add(new() { Id = Guid.Parse($"58000000-0000-0000-0000-{150 + date.Day:D12}"), AcademyId = academy, BranchId = branch, ResourceId = resourceId, TrainingGroupId = GroupId, TrainingSessionId = session, CoachMembershipId = coach, Date = date, StartTime = start, EndTime = end, ReservedSeats = 4, ApprovedCapacitySnapshot = resource.Capacity, ServiceKind = "Training", ActorUserId = owner, RequestKey = "demo-resource-timetable-" + date, RequestHash = AcademyConfigurationService.Hash("demo-resource-timetable-" + date), CreatedAtUtc = now, UpdatedAtUtc = now });
            }
        }
        db.TrainingScheduleRevisions.Add(new() { Id = Guid.Parse("58000000-0000-0000-0000-000000000180"), AcademyId = academy, BranchId = branch, TrainingGroupId = GroupId, ActorUserId = owner, RequestKey = "demo-resource-timetable-v1", RequestHash = AcademyConfigurationService.Hash("demo-resource-timetable-v1"), Snapshot = "{\"demo\":true,\"paid\":false,\"note\":\"synthetic resource schedule; no collection or attendance\"}", CreatedSessions = 4, PreservedSessions = 0, CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
