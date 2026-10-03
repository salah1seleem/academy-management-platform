using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice4;

public static class Slice4DemoSeed
{
    public static readonly Guid SessionPackagePlanId = Guid.Parse("61000000-0000-0000-0000-000000000001");
    public static readonly Guid YesterdayFootballSessionId = Guid.Parse("62000000-0000-0000-0000-000000000001");
    public static readonly Guid TodayFootballSessionId = Guid.Parse("62000000-0000-0000-0000-000000000002");
    public static readonly Guid TomorrowFootballSessionId = Guid.Parse("62000000-0000-0000-0000-000000000003");
    public static readonly Guid CancelledFootballSessionId = Guid.Parse("62000000-0000-0000-0000-000000000004");
    public static readonly Guid TodaySwimmingSessionId = Guid.Parse("62000000-0000-0000-0000-000000000005");
    public static readonly Guid AcademyBSessionId = Guid.Parse("62000000-0000-0000-0000-000000000006");

    public static async Task SeedAsync(FoundationDbContext db, Guid ownerUserId, Guid coachUserId, Guid futureOwnerUserId, DateTimeOffset now, CancellationToken ct)
    {
        var referenceDate = DateOnly.FromDateTime(now.UtcDateTime);
        await Slice3DemoSeed.Plan(db, SessionPackagePlanId, DemoSeed.NogoomAcademyId, Slice2DemoSeed.FootballId,
            "باقة العرض — 5 حصص", SubscriptionPlanType.Sessions, 500m, null, 5, 5, now, ct);

        var sessionEnrollment = await db.SportEnrollments.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.PlayerId == Slice2DemoSeed.OtherPlayerId && x.SportId == Slice2DemoSeed.FootballId, ct);
        await Slice3DemoSeed.Confirmed(db, Guid.Parse("63000000-0000-0000-0000-000000000001"), sessionEnrollment,
            SessionPackagePlanId, ownerUserId, referenceDate.AddDays(-7), referenceDate.AddDays(60), now.AddDays(-7), now, ct);

        await Session(db, YesterdayFootballSessionId, DemoSeed.NogoomAcademyId, Slice2DemoSeed.FootballGroupId,
            referenceDate.AddDays(-1), new TimeOnly(18, 0), new TimeOnly(19, 30), TrainingSessionStatus.Held, ownerUserId, now, ct);
        await Session(db, TodayFootballSessionId, DemoSeed.NogoomAcademyId, Slice2DemoSeed.FootballGroupId,
            referenceDate, new TimeOnly(18, 0), new TimeOnly(19, 30), TrainingSessionStatus.Scheduled, ownerUserId, now, ct);
        await Session(db, TomorrowFootballSessionId, DemoSeed.NogoomAcademyId, Slice2DemoSeed.FootballGroupId,
            referenceDate.AddDays(1), new TimeOnly(18, 0), new TimeOnly(19, 30), TrainingSessionStatus.Scheduled, ownerUserId, now, ct);
        await Session(db, CancelledFootballSessionId, DemoSeed.NogoomAcademyId, Slice2DemoSeed.FootballGroupId,
            referenceDate.AddDays(2), new TimeOnly(18, 0), new TimeOnly(19, 30), TrainingSessionStatus.Cancelled, ownerUserId, now, ct);
        await Session(db, TodaySwimmingSessionId, DemoSeed.NogoomAcademyId, Slice2DemoSeed.SwimmingGroupId,
            referenceDate, new TimeOnly(16, 0), new TimeOnly(17, 0), TrainingSessionStatus.Scheduled, ownerUserId, now, ct);

        var academyBGroup = await db.TrainingGroups.SingleAsync(x => x.AcademyId == DemoSeed.FutureAcademyId, ct);
        await Session(db, AcademyBSessionId, DemoSeed.FutureAcademyId, academyBGroup.Id,
            referenceDate, new TimeOnly(18, 0), new TimeOnly(19, 0), TrainingSessionStatus.Scheduled, futureOwnerUserId, now, ct);

        var omarFootball = await db.SportEnrollments.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.PlayerId == Slice2DemoSeed.OmarPlayerId && x.SportId == Slice2DemoSeed.FootballId, ct);
        var otherFootball = await db.SportEnrollments.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.PlayerId == Slice2DemoSeed.OtherPlayerId && x.SportId == Slice2DemoSeed.FootballId, ct);
        await PlayerAttendance(db, Guid.Parse("64000000-0000-0000-0000-000000000001"), YesterdayFootballSessionId, Slice2DemoSeed.FootballGroupId, omarFootball.Id, AttendanceStatus.Present, ownerUserId, now, ct);
        await PlayerAttendance(db, Guid.Parse("64000000-0000-0000-0000-000000000002"), YesterdayFootballSessionId, Slice2DemoSeed.FootballGroupId, otherFootball.Id, AttendanceStatus.Absent, ownerUserId, now, ct);

        var coachMembership = await db.AcademyMemberships.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.UserId == coachUserId && x.Role == AcademyRole.Coach, ct);
        if (!await db.StaffAttendances.AnyAsync(x => x.Id == Guid.Parse("65000000-0000-0000-0000-000000000001"), ct))
        {
            db.StaffAttendances.Add(new StaffAttendance
            {
                Id = Guid.Parse("65000000-0000-0000-0000-000000000001"), AcademyId = DemoSeed.NogoomAcademyId,
                TrainingSessionId = YesterdayFootballSessionId, TrainingGroupId = Slice2DemoSeed.FootballGroupId,
                AcademyMembershipId = coachMembership.Id, Status = AttendanceStatus.Present, RecordedByUserId = ownerUserId,
                CreatedAtUtc = now.AddDays(-1), UpdatedAtUtc = now.AddDays(-1)
            });
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task Session(FoundationDbContext db, Guid id, Guid academyId, Guid groupId, DateOnly date,
        TimeOnly start, TimeOnly end, TrainingSessionStatus status, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.TrainingSessions.AnyAsync(x => x.Id == id, ct)) return;
        var group = await db.TrainingGroups.SingleAsync(x => x.AcademyId == academyId && x.Id == groupId, ct);
        db.TrainingSessions.Add(new TrainingSession
        {
            Id = id, AcademyId = academyId, TrainingGroupId = group.Id, BranchId = group.BranchId, SportId = group.SportId,
            AgeCategoryId = group.AgeCategoryId, SessionDate = date, StartTime = start, EndTime = end, Status = status,
            Source = TrainingSessionSource.Manual, Notes = "بيانات عرض تجريبية", CreatedByUserId = actor,
            CreatedAtUtc = now, UpdatedAtUtc = now
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task PlayerAttendance(FoundationDbContext db, Guid id, Guid sessionId, Guid groupId,
        Guid enrollmentId, AttendanceStatus status, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.PlayerAttendances.AnyAsync(x => x.Id == id, ct)) return;
        db.PlayerAttendances.Add(new PlayerAttendance
        {
            Id = id, AcademyId = DemoSeed.NogoomAcademyId, TrainingSessionId = sessionId, TrainingGroupId = groupId,
            SportEnrollmentId = enrollmentId, Status = status, RecordedByUserId = actor,
            CreatedAtUtc = now.AddDays(-1), UpdatedAtUtc = now.AddDays(-1)
        });
        await db.SaveChangesAsync(ct);
    }
}
