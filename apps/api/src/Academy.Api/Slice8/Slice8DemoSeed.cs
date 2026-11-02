using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;

public static class Slice8DemoSeed
{
    public static readonly Guid ReportPlayerId = Guid.Parse("91000000-0000-0000-0000-000000000001");
    public static readonly Guid ReportEnrollmentId = Guid.Parse("91000000-0000-0000-0000-000000000002");
    public static readonly Guid ReportAttendanceId = Guid.Parse("91000000-0000-0000-0000-000000000003");

    public static async Task SeedAsync(FoundationDbContext db, Guid ownerUserId, DateTimeOffset now, CancellationToken ct)
    {
        if (!await db.Players.AnyAsync(x => x.Id == ReportPlayerId, ct))
        {
            db.Players.Add(new Player { Id = ReportPlayerId, AcademyId = DemoSeed.NogoomAcademyId, PlayerCode = "NG-2019", ArabicName = "يوسف خالد سمير", DateOfBirth = new DateOnly(2019, 3, 18), Gender = Gender.Male, CreatedAtUtc = now, UpdatedAtUtc = now });
            await db.SaveChangesAsync(ct);
        }
        if (!await db.SportEnrollments.AnyAsync(x => x.Id == ReportEnrollmentId, ct))
        {
            db.SportEnrollments.Add(new SportEnrollment { Id = ReportEnrollmentId, AcademyId = DemoSeed.NogoomAcademyId, PlayerId = ReportPlayerId, SportId = Slice2DemoSeed.FootballId, BranchId = Slice2DemoSeed.CityBranchId, TrainingGroupId = Slice2DemoSeed.FootballGroupId, Status = EnrollmentStatus.Active, CreatedAtUtc = now, UpdatedAtUtc = now });
            await db.SaveChangesAsync(ct);
        }
        if (!await db.PlayerAttendances.AnyAsync(x => x.Id == ReportAttendanceId, ct))
        {
            db.PlayerAttendances.Add(new PlayerAttendance { Id = ReportAttendanceId, AcademyId = DemoSeed.NogoomAcademyId, TrainingSessionId = Slice4DemoSeed.YesterdayFootballSessionId, TrainingGroupId = Slice2DemoSeed.FootballGroupId, SportEnrollmentId = ReportEnrollmentId, Status = AttendanceStatus.Present, RecordedByUserId = ownerUserId, CreatedAtUtc = now.AddDays(-1), UpdatedAtUtc = now.AddDays(-1) });
            await db.SaveChangesAsync(ct);
        }
    }
}
