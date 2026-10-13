using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Finance;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

// Opt-in synthetic cost example. Never modifies an existing expense or customer record.
public static class OperatingExpenseDemoSeed
{
    public static readonly Guid ExpenseId = Guid.Parse("a8000000-0000-0000-0000-000000000001");
    public static readonly Guid AllocationId = Guid.Parse("a8000000-0000-0000-0000-000000000002");
    public static async Task Seed(FoundationDbContext db, DateTimeOffset at)
    {
        var academy = DemoSeed.NogoomAcademyId;
        if (await db.OperatingExpenses.AnyAsync(x => x.Id == ExpenseId)) return;
        if (!await db.Academies.AnyAsync(a => a.Id == academy && a.Slug == "nogoom-demo" && a.IsActive)) return;
        var group = await db.TrainingGroups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == GroupPlacementDemoSeed.Id(1) && g.AcademyId == academy && g.IsActive);
        if (group is null) return;
        var owner = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"demo-operating-expense:{academy}"},0))");
        if (await db.OperatingExpenses.AnyAsync(x => x.Id == ExpenseId)) return;
        var hash = StatementComparisonRules.Hash(new { demo = "operating-expense", academy, group.Id });
        db.OperatingExpenses.Add(new() { Id = ExpenseId, AcademyId = academy, BranchId = group.BranchId, Scope = OperatingExpenseScope.Group,
            TrainingGroupId = group.Id, Category = "Supplies", PeriodFrom = new(2026, 9, 1), PeriodTo = new(2026, 9, 30), PaidOn = new(2026, 9, 28),
            Amount = 200m, Currency = "EGP", Note = "مستلزمات تدريب اصطناعية للديمو فقط", Status = OperatingExpenseStatus.Posted,
            CreatedByUserId = owner, ApprovedByUserId = owner, ApprovedAtUtc = at, PostedByUserId = owner, PostedAtUtc = at,
            RequestKey = "demo-operating-expense-2026-09", RequestHash = hash, PostRequestKey = "demo-operating-post-2026-09", PostQuoteHash = hash,
            CreatedAtUtc = at, UpdatedAtUtc = at });
        db.OperatingExpenseAllocations.Add(new() { Id = AllocationId, AcademyId = academy, ExpenseId = ExpenseId, TrainingGroupId = group.Id,
            Weight = 1, Amount = 200m, Currency = "EGP", CreatedAtUtc = at, UpdatedAtUtc = at });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
