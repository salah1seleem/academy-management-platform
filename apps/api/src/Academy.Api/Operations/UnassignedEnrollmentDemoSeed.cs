using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public static class UnassignedEnrollmentDemoSeed
{
    public static readonly Guid PlayerId = Guid.Parse("59000000-0000-0000-0000-000000000001");
    public static async Task Seed(FoundationDbContext db)
    {
        var academy = DemoSeed.NogoomAcademyId;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + academy}, 0))");
        if (await db.Players.AnyAsync(x => x.Id == PlayerId) || !await db.Academies.AnyAsync(x => x.Id == academy && x.Slug == "nogoom-demo")) return;
        var group = await db.TrainingGroups.Where(x => x.AcademyId == academy && x.SportId == Slice2DemoSeed.FootballId && x.AgeCategoryId == Slice2DemoSeed.U10Id && x.IsActive && x.Branch.IsActive).OrderBy(x => x.Id).FirstOrDefaultAsync();
        var guardian = await db.GuardianProfiles.Where(x => x.AcademyId == academy && x.User.PhoneNumber == DemoSeed.GuardianPhone && x.IsActive).Select(x => (Guid?)x.Id).SingleOrDefaultAsync();
        var owner = await db.Users.Where(x => x.Email == DemoSeed.OwnerEmail).Select(x => x.Id).SingleAsync();
        if (group is null || guardian is null || !await db.Sports.AnyAsync(x => x.Id == Slice2DemoSeed.SwimmingId && x.IsActive)) return;
        // Respect edited demo capacity; never reset a roster or invent a financial period.
        if (group.Capacity.HasValue && await db.SportEnrollments.CountAsync(x => x.TrainingGroupId == group.Id && x.IsActive) >= group.Capacity) return;
        var now = DateTimeOffset.Parse("2026-09-28T06:00:00Z");
        db.Players.Add(new() { Id = PlayerId, AcademyId = academy, PlayerCode = "DEMO-PLACE-001", ArabicName = "لاعب انتظار السباحة التجريبي", DateOfBirth = new(2017, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now });
        db.GuardianPlayerLinks.Add(new() { Id = Guid.Parse("59000000-0000-0000-0000-000000000002"), AcademyId = academy, GuardianId = guardian.Value, PlayerId = PlayerId, CreatedByUserId = owner, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.SportEnrollments.Add(new() { Id = Guid.Parse("59000000-0000-0000-0000-000000000003"), AcademyId = academy, PlayerId = PlayerId, SportId = group.SportId, BranchId = group.BranchId, TrainingGroupId = group.Id, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.SportEnrollments.Add(new() { Id = Guid.Parse("59000000-0000-0000-0000-000000000004"), AcademyId = academy, PlayerId = PlayerId, SportId = Slice2DemoSeed.SwimmingId, BranchId = group.BranchId, TrainingGroupId = null, CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
