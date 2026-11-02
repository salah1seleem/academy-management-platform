using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;

public static class Slice8BAdjustmentDemoSeed
{
    public static readonly Guid HistoricalDaysAddedId = Guid.Parse("56000000-0000-0000-0000-000000000001");
    private static readonly Guid MariamHistoricalPeriodId = Guid.Parse("53000000-0000-0000-0000-000000000504");

    public static async Task SeedAsync(FoundationDbContext db, Guid ownerUserId, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.SubscriptionAdjustments.AnyAsync(x => x.Id == HistoricalDaysAddedId, ct)) return;
        var period = await db.SubscriptionPeriods.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Id == MariamHistoricalPeriodId, ct);
        var oldEnd = period.EndDate ?? throw new InvalidOperationException("Slice 8B demo adjustment requires a duration period.");
        var newEnd = oldEnd.AddDays(2);
        period.EndDate = newEnd;
        period.UpdatedAtUtc = now;
        db.SubscriptionAdjustments.Add(new SubscriptionAdjustment
        {
            Id = HistoricalDaysAddedId, AcademyId = DemoSeed.NogoomAcademyId, SubscriptionPeriodId = period.Id,
            AdjustmentType = SubscriptionAdjustmentType.DaysAdded, EffectiveDate = new DateOnly(2026, 9, 1), DaysDelta = 2,
            OldEndDate = oldEnd, NewEndDate = newEnd, Reason = "تعويض يومي تدريب ملغيين — بيانات عرض",
            PerformedByUserId = ownerUserId, IdempotencyKey = "demo-slice8b-history", PerformedAtUtc = now.AddDays(-10),
            CreatedAtUtc = now.AddDays(-10), UpdatedAtUtc = now.AddDays(-10)
        });
        await db.SaveChangesAsync(ct);
    }
}
