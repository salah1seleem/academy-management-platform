using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;

public static class Slice8CDiscountDemoSeed
{
    private static readonly Guid PaidAuditId = Guid.Parse("57000000-0000-0000-0000-000000000001");
    private static readonly Guid PendingAuditId = Guid.Parse("57000000-0000-0000-0000-000000000002");
    private static readonly Guid FailedAuditId = Guid.Parse("57000000-0000-0000-0000-000000000003");

    public static async Task SeedAsync(FoundationDbContext db, Guid ownerUserId, DateTimeOffset now, CancellationToken ct)
    {
        await PaidPercentage(db, ownerUserId, now, ct);
        await IncompleteDiscount(db, "seed-54000000000000000000000000000002", PendingAuditId,
            RenewalDiscountType.FixedAmount, 150m, 150m, now, ownerUserId, ct);
        await IncompleteDiscount(db, "seed-54000000000000000000000000000001", FailedAuditId,
            RenewalDiscountType.Percentage, 5m, 45m, now.AddDays(-1), ownerUserId, ct);
    }

    private static async Task PaidPercentage(FoundationDbContext db, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.RenewalDiscountAdjustments.AnyAsync(x => x.Id == PaidAuditId, ct)) return;
        var renewal = await db.RenewalRequests.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.IdempotencyKey == "seed-53000000000000000000000000000001", ct);
        var payment = await db.PaymentRequests.SingleAsync(x => x.AcademyId == renewal.AcademyId && x.Id == renewal.PaymentRequestId, ct);
        var collection = await db.Collections.SingleAsync(x => x.AcademyId == renewal.AcademyId && x.RenewalRequestId == renewal.Id, ct);
        var receipt = await db.Receipts.SingleAsync(x => x.AcademyId == renewal.AcademyId && x.CollectionId == collection.Id, ct);
        var period = await db.SubscriptionPeriods.SingleAsync(x => x.AcademyId == renewal.AcademyId && x.CollectionId == collection.Id, ct);
        renewal.OriginalAmount = 900m; renewal.DiscountType = RenewalDiscountType.Percentage; renewal.DiscountValue = 10m;
        renewal.DiscountAmount = 90m; renewal.FinalAmount = 810m; renewal.AmountExpected = 810m;
        renewal.DiscountReason = "خصم عرض تجريبي معتمد يدويًا"; renewal.DiscountAppliedByUserId = actor; renewal.DiscountAppliedAtUtc = payment.ConfirmedAtUtc?.AddMinutes(-5); renewal.UpdatedAtUtc = now;
        payment.Amount = 810m; collection.Amount = 810m; period.PriceSnapshot = 810m;
        receipt.OriginalAmount = 900m; receipt.DiscountType = RenewalDiscountType.Percentage; receipt.DiscountValue = 10m;
        receipt.DiscountAmount = 90m; receipt.FinalAmount = 810m; receipt.Amount = 810m;
        db.RenewalDiscountAdjustments.Add(Audit(PaidAuditId, renewal, actor, null, null, 0m, 900m,
            RenewalDiscountType.Percentage, 10m, 90m, 810m, "خصم عرض تجريبي معتمد يدويًا", payment.ConfirmedAtUtc?.AddMinutes(-5) ?? now));
        await db.SaveChangesAsync(ct);
    }

    private static async Task IncompleteDiscount(FoundationDbContext db, string seedKey, Guid auditId,
        RenewalDiscountType type, decimal value, decimal discount, DateTimeOffset at, Guid actor, CancellationToken ct)
    {
        if (await db.RenewalDiscountAdjustments.AnyAsync(x => x.Id == auditId, ct)) return;
        var renewal = await db.RenewalRequests.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.IdempotencyKey == seedKey, ct);
        var payment = await db.PaymentRequests.SingleAsync(x => x.AcademyId == renewal.AcademyId && x.Id == renewal.PaymentRequestId, ct);
        var original = renewal.OriginalAmount;
        var final = original - discount;
        renewal.DiscountType = type; renewal.DiscountValue = value; renewal.DiscountAmount = discount; renewal.FinalAmount = final;
        renewal.AmountExpected = final; renewal.DiscountReason = "خصم تجريبي يدوي"; renewal.DiscountAppliedByUserId = actor;
        renewal.DiscountAppliedAtUtc = at; renewal.UpdatedAtUtc = at; payment.Amount = final; payment.UpdatedAtUtc = at;
        db.RenewalDiscountAdjustments.Add(Audit(auditId, renewal, actor, null, null, 0m, original,
            type, value, discount, final, "خصم تجريبي يدوي", at));
        await db.SaveChangesAsync(ct);
    }

    private static RenewalDiscountAdjustment Audit(Guid id, RenewalRequest renewal, Guid actor,
        RenewalDiscountType? previousType, decimal? previousValue, decimal previousDiscount, decimal previousFinal,
        RenewalDiscountType? nextType, decimal? nextValue, decimal nextDiscount, decimal nextFinal, string reason, DateTimeOffset at)
        => new()
        {
            Id = id, AcademyId = renewal.AcademyId, RenewalRequestId = renewal.Id,
            PreviousDiscountType = previousType, PreviousDiscountValue = previousValue,
            NewDiscountType = nextType, NewDiscountValue = nextValue,
            PreviousDiscountAmount = previousDiscount, NewDiscountAmount = nextDiscount,
            PreviousFinalAmount = previousFinal, NewFinalAmount = nextFinal,
            Reason = reason, PerformedByUserId = actor, IdempotencyKey = $"demo-slice8c-{id:N}",
            CreatedAtUtc = at, UpdatedAtUtc = at
        };
}
