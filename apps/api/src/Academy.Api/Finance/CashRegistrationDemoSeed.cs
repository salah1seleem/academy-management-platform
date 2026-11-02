using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public static class CashRegistrationDemoSeed
{
    public static readonly Guid GroupId = Guid.Parse("84000011-0000-0000-0000-000000000001");
    public static async Task Seed(FoundationDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"v2-demo-cash-registration"},0))");
        if (await db.TrainingGroups.AnyAsync(g => g.Id == GroupId)) return;
        var sample = await db.TrainingGroups.SingleOrDefaultAsync(g => g.AcademyId == DemoSeed.NogoomAcademyId && g.Id == Slice2DemoSeed.FootballGroupId);
        if (sample is null) return;
        var at = new DateTimeOffset(2026, 9, 28, 6, 0, 0, TimeSpan.Zero);
        db.TrainingGroups.Add(new() { Id = GroupId, AcademyId = sample.AcademyId, ArabicName = "تسجيل لاعب نقدي — مجموعة اصطناعية",
            BranchId = sample.BranchId, SportId = sample.SportId, AgeCategoryId = sample.AgeCategoryId, Capacity = 10, CreatedAtUtc = at, UpdatedAtUtc = at });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
