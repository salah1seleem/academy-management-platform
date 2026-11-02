using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice3;

// Only explicit Demo/LegacyRegression caller; does not grant a waiver automatically.
public static class FreeTrainingDemoSeed
{
    public const string Phone = "+201000000015";
    public static readonly Guid UserId = Guid.Parse("84000015-0000-0000-0000-000000000001");
    public static readonly Guid PlayerId = Guid.Parse("84000015-0000-0000-0000-000000000004");
    public static readonly Guid EnrollmentId = Guid.Parse("84000015-0000-0000-0000-000000000006");
    public static async Task Seed(FoundationDbContext db, UserManager<ApplicationUser> users)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"v2-demo-free-training"},0))");
        var at = new DateTimeOffset(2026, 9, 28, 6, 0, 0, TimeSpan.Zero); var a = DemoSeed.NogoomAcademyId; var guardian = Guid.Parse("84000015-0000-0000-0000-000000000003");
        if (await users.FindByIdAsync(UserId.ToString()) is null) { var user = new ApplicationUser { Id = UserId, UserName = Phone, PhoneNumber = Phone, PhoneNumberConfirmed = true, DisplayName = "ولي إعفاء اصطناعي", CreatedAtUtc = at, UpdatedAtUtc = at }; if (!(await users.CreateAsync(user)).Succeeded) throw new InvalidOperationException("Could not create synthetic waiver fixture"); }
        if (!await db.AcademyMemberships.AnyAsync(m => m.AcademyId == a && m.UserId == UserId)) db.AcademyMemberships.Add(new() { Id = Guid.Parse("84000015-0000-0000-0000-000000000002"), AcademyId = a, UserId = UserId, Role = AcademyRole.Guardian, CreatedAtUtc = at, UpdatedAtUtc = at });
        if (!await db.GuardianProfiles.AnyAsync(g => g.Id == guardian)) db.GuardianProfiles.Add(new() { Id = guardian, AcademyId = a, UserId = UserId, DisplayName = "ولي إعفاء اصطناعي", ContactPhone = Phone, CreatedAtUtc = at, UpdatedAtUtc = at });
        if (!await db.Players.AnyAsync(p => p.Id == PlayerId)) db.Players.Add(new() { Id = PlayerId, AcademyId = a, PlayerCode = "WAIVER-DEMO-1", ArabicName = "ياسين — إعفاء اصطناعي", DateOfBirth = new(2016, 1, 1), CreatedAtUtc = at, UpdatedAtUtc = at });
        if (!await db.GuardianPlayerLinks.AnyAsync(l => l.GuardianId == guardian && l.PlayerId == PlayerId)) db.GuardianPlayerLinks.Add(new() { Id = Guid.Parse("84000015-0000-0000-0000-000000000005"), AcademyId = a, GuardianId = guardian, PlayerId = PlayerId, CreatedByUserId = UserId, CreatedAtUtc = at, UpdatedAtUtc = at });
        if (!await db.SportEnrollments.AnyAsync(e => e.Id == EnrollmentId)) db.SportEnrollments.Add(new() { Id = EnrollmentId, AcademyId = a, PlayerId = PlayerId, SportId = Slice2DemoSeed.FootballId, BranchId = Slice2DemoSeed.CityBranchId, CreatedAtUtc = at, UpdatedAtUtc = at });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
