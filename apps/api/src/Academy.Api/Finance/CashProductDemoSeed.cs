using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public static class CashProductDemoSeed
{
    public static readonly Guid ProductId = Guid.Parse("84000012-0000-0000-0000-000000000001");
    public static async Task Seed(FoundationDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"v2-demo-cash-product"},0))");
        if (await db.SportCatalogItems.AnyAsync(p => p.Id == ProductId)) return;
        if (!await db.Sports.AnyAsync(s => s.AcademyId == DemoSeed.NogoomAcademyId && s.Id == Slice2DemoSeed.FootballId)) return;
        var at = new DateTimeOffset(2026, 9, 28, 6, 0, 0, TimeSpan.Zero);
        db.SportCatalogItems.Add(new() { Id = ProductId, AcademyId = DemoSeed.NogoomAcademyId, SportId = Slice2DemoSeed.FootballId,
            ArabicName = "طقم كرة قدم — تجربة الصندوق الاصطناعية", ImageReference = "/demo-assets/catalog-football.svg", DisplayPrice = 100,
            DiscountPercentage = 10, Currency = "EGP", StockQuantity = 30, CreatedAtUtc = at, UpdatedAtUtc = at });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
