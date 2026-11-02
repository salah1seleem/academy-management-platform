using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

// Manual comparison examples only; no new payment or money movement is seeded.
public static class SettlementComparisonDemoSeed
{
    public static Guid Id(int n) => Guid.Parse($"85000020-0000-0000-0000-{n:D12}");
    public static async Task Seed(FoundationDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"v2-demo-statement-comparisons"},0))");
        if (await db.SettlementComparisons.AnyAsync(x => x.Id == Id(1))) return;
        var bytes = CollectionFundsDemoSeed.Root.ToByteArray(); bytes[14] = 3; var collectionId = new Guid(bytes);
        var c = await db.Collections.Include(c => c.SportEnrollment).SingleOrDefaultAsync(c => c.AcademyId == DemoSeed.NogoomAcademyId && c.Id == collectionId);
        if (c is null) return;
        var user = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync(); var at = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
        foreach (var n in new[] { 1, 2 }) db.SettlementComparisons.Add(new() { Id = Id(n), AcademyId = c.AcademyId, BranchId = c.SportEnrollment.BranchId, CollectionId = c.Id, SourceKind = "Provider", AccountTag = "DEMO-NOT-A-BANK", StatementReference = "كشف اصطناعي — لا حساب حقيقي", LineReference = n.ToString(), Provider = c.Provider, PaymentReference = c.ProviderReference, SettlementDate = new(2026, 9, 28), GrossAmount = c.Amount + (n == 2 ? 25 : 0), FeeAmount = 5, NetAmount = c.Amount - 5 + (n == 2 ? 25 : 0), Currency = c.Currency, ExpectedAmount = c.Amount, ExpectedCurrency = c.Currency, Result = n == 1 ? "Matched" : "AmountMismatch", ActorUserId = user, Note = "مقارنة يدوية اصطناعية؛ لا اتصال ببنك ولا حركة مالية", ContentHash = $"DEMO-STATEMENT-COMPARISON-{n}", CreatedAtUtc = at, UpdatedAtUtc = at });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
