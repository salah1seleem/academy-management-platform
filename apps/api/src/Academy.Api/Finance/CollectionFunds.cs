using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

// Every consumer locks this same pool within its transaction. The collection
// must first be resolved through the authorized tenant/resource-scoped query.
public static class CollectionFunds
{
    public static Task Lock(FoundationDbContext db, Guid academy, Guid collection) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"collection-funds:{academy}:{collection}"},0))");
    public static decimal Available(decimal collected, decimal refunded, decimal allocated) => collected-refunded-allocated;
    public static async Task<(decimal Refunded, decimal Allocated)> Used(FoundationDbContext db, Guid academy, Guid collection)
    {
        // Financial conservation includes historical documents from other branches.
        // Aggregate a single already-authorized collection only; never expose their rows.
        var refunded=await db.CollectionRefunds.IgnoreQueryFilters().Where(r=>r.AcademyId==academy&&r.CollectionId==collection).SumAsync(r=>(decimal?)r.Amount)??0;
        var allocated=await db.ReceivableMovements.IgnoreQueryFilters().Where(m=>m.AcademyId==academy&&m.CollectionId==collection&&m.Kind==ReceivableMovementKind.PaymentAllocation).SumAsync(m=>(decimal?)m.Amount)??0;
        return(refunded,allocated);
    }
}
