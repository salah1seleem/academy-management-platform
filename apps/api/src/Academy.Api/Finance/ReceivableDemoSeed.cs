using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

// Called only from the enabled Demo seed, for the reserved legacy synthetic academy.
// Never rewrites a posted document, creates cash, resets data or touches customer tenants.
public static class ReceivableDemoSeed
{
    public static async Task SeedAsync(FoundationDbContext db, CancellationToken ct = default)
    {
        const string marker = "v2-demo-fin02-";
        if (!await db.Academies.AnyAsync(x => x.Id == DemoSeed.NogoomAcademyId, ct)) return;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({marker},0))", ct);
        if (await db.ReceivableDocuments.AnyAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.SourceKey.StartsWith(marker), ct)) return;
        var enrollment = await db.SportEnrollments.Include(x => x.Player).SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.PlayerId == Slice2DemoSeed.OmarPlayerId && x.SportId == Slice2DemoSeed.FootballId, ct);
        var root = Guid.Parse("53000000-0000-0000-0000-000000000002").ToByteArray(); root[14] = 3;
        var collectionId = new Guid(root);
        var collection = await db.Collections.SingleOrDefaultAsync(x => x.Id == collectionId && x.SportEnrollmentId == enrollment.Id && x.Amount == 900m && x.Currency == "EGP", ct);
        if (collection is null || await db.ReceivableMovements.AnyAsync(x => x.CollectionId == collectionId, ct) || await db.CollectionRefunds.AnyAsync(x => x.CollectionId == collectionId, ct)) return;
        var actor = await db.Users.Where(x => x.Email == DemoSeed.OwnerEmail).Select(x => x.Id).SingleAsync(ct);
        var now = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
        var values = new[] { 540m, 700m, 1000m, 500m };
        var documents = values.Select((amount, i) => new ReceivableDocument {
            Id = Guid.Parse($"83000000-0000-0000-0000-{i + 1:000000000000}"), AcademyId = enrollment.AcademyId,
            SportEnrollmentId = enrollment.Id, PlayerId = enrollment.PlayerId, PlayerNameSnapshot = enrollment.Player.ArabicName,
            BranchId = enrollment.BranchId, Amount = amount, Currency = "EGP", DueDate = i == 3 ? new(2026, 10, 1) : new(2026, 9, 20),
            Source = ReceivableSource.OpeningBalance, SourceKey = marker + i, ContentHash = "DEMO-FIN-02-V1", ActorUserId = actor,
            Description = $"مطالبة افتتاحية تجريبية {i + 1} — ليست تحصيلًا جديدًا", CreatedAtUtc = now, UpdatedAtUtc = now,
        }).ToArray();
        db.ReceivableDocuments.AddRange(documents);
        db.ReceivableMovements.AddRange(new[] { (Document: 0, Amount: 200m), (Document: 1, Amount: 700m), (Document: 2, Amount: 100m) }.Select((x, i) => new ReceivableMovement {
            Id = Guid.Parse($"83000001-0000-0000-0000-{i + 1:000000000000}"), AcademyId = enrollment.AcademyId, DocumentId = documents[x.Document].Id,
            Kind = i == 2 ? ReceivableMovementKind.CreditNote : ReceivableMovementKind.PaymentAllocation, Amount = x.Amount, Currency = "EGP",
            CollectionId = i == 2 ? null : collectionId, Reason = "حركة مثال مالي اصطناعي موثق", SourceKey = marker + "movement-" + i,
            ContentHash = "DEMO-FIN-02-V1", ActorUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now,
        }));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
