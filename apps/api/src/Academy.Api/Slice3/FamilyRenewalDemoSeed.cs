using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice3;

// Explicit LegacyRegression opt-in only; never resets requests, receipts or periods.
public static class FamilyRenewalDemoSeed
{
    public const string Phone = "+201000000014";
    public static readonly Guid UserId = Guid.Parse("84000014-0000-0000-0000-000000000001");
    public static readonly Guid GuardianId = Guid.Parse("84000014-0000-0000-0000-000000000002");
    public static async Task Seed(FoundationDbContext db, UserManager<ApplicationUser> users)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"v2-demo-family-renewal"},0))");
        var at = new DateTimeOffset(2026, 9, 28, 6, 0, 0, TimeSpan.Zero); var academy = DemoSeed.NogoomAcademyId;
        var user = await users.FindByIdAsync(UserId.ToString());
        if (user is null)
        {
            user = new() { Id = UserId, UserName = Phone, PhoneNumber = Phone, PhoneNumberConfirmed = true, DisplayName = "أسرة التجديد الاصطناعية", CreatedAtUtc = at, UpdatedAtUtc = at };
            if (!(await users.CreateAsync(user)).Succeeded) throw new InvalidOperationException("Could not create synthetic family fixture");
        }
        if (!await db.AcademyMemberships.AnyAsync(m => m.AcademyId == academy && m.UserId == UserId)) db.AcademyMemberships.Add(new() { Id = Guid.Parse("84000014-0000-0000-0000-000000000003"), AcademyId = academy, UserId = UserId, Role = AcademyRole.Guardian, CreatedAtUtc = at, UpdatedAtUtc = at });
        if (!await db.GuardianProfiles.AnyAsync(g => g.Id == GuardianId)) db.GuardianProfiles.Add(new() { Id = GuardianId, AcademyId = academy, UserId = UserId, DisplayName = user.DisplayName, ContactPhone = Phone, CreatedAtUtc = at, UpdatedAtUtc = at });
        for (var i = 1; i <= 2; i++)
        {
            var player = Guid.Parse($"84000014-0000-0000-0001-{i:D12}"); var link = Guid.Parse($"84000014-0000-0000-0002-{i:D12}"); var enrollment = Guid.Parse($"84000014-0000-0000-0003-{i:D12}");
            if (!await db.Players.AnyAsync(p => p.Id == player)) db.Players.Add(new() { Id = player, AcademyId = academy, PlayerCode = $"FAMILY-DEMO-{i}", ArabicName = i == 1 ? "آدم — أسرة اصطناعية" : "لينا — أسرة اصطناعية", DateOfBirth = new(2016, i, 1), CreatedAtUtc = at, UpdatedAtUtc = at });
            if (!await db.GuardianPlayerLinks.AnyAsync(l => l.Id == link)) db.GuardianPlayerLinks.Add(new() { Id = link, AcademyId = academy, GuardianId = GuardianId, PlayerId = player, CreatedByUserId = UserId, CreatedAtUtc = at, UpdatedAtUtc = at });
            if (!await db.SportEnrollments.AnyAsync(e => e.Id == enrollment)) db.SportEnrollments.Add(new() { Id = enrollment, AcademyId = academy, PlayerId = player, SportId = Slice2DemoSeed.FootballId, BranchId = Slice2DemoSeed.CityBranchId, TrainingGroupId = Slice2DemoSeed.FootballGroupId, CreatedAtUtc = at, UpdatedAtUtc = at });
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
