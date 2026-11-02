using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

// Explicit opt-in from enabled LegacyRegression Demo only. Reserved synthetic
// records, never a reset, real payment, or rewrite of an existing financial row.
public static class CollectionFundsDemoSeed
{
    public static readonly Guid Root = Guid.Parse("83000010-0000-0000-0000-000000000001");
    public static readonly Guid DocumentId = Guid.Parse("83000011-0000-0000-0000-000000000001");
    public static async Task Seed(FoundationDbContext db)
    {
        var enrollment = await db.SportEnrollments.SingleAsync(e => e.AcademyId == DemoSeed.NogoomAcademyId && e.PlayerId == Slice2DemoSeed.OmarPlayerId && e.SportId == Slice2DemoSeed.FootballId);
        var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
        var now = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"v2-demo-collection-funds"},0))");
        if (await db.ReceivableDocuments.AnyAsync(d => d.Id == DocumentId && d.AcademyId == enrollment.AcademyId)) return;
        // Refuse to reconstruct a partially altered fixture.
        var bytes = Root.ToByteArray(); bytes[14] = 3; var collectionId = new Guid(bytes);
        if (await db.Collections.AnyAsync(c => c.Id == collectionId)) return;
        await Slice3DemoSeed.Confirmed(db, Root, enrollment, Slice3DemoSeed.FootballMonthlyId, actor, new(2026, 12, 1), new(2026, 12, 30), now, now, default);
        db.ReceivableDocuments.Add(new() { Id = DocumentId, AcademyId = enrollment.AcademyId, SportEnrollmentId = enrollment.Id, PlayerId = enrollment.PlayerId, PlayerNameSnapshot = "عمر — مثال استرداد اصطناعي", BranchId = enrollment.BranchId, Description = "مثال متاح الاسترداد — دفع تجريبي فقط", Amount = 100, Currency = "EGP", DueDate = new(2026, 9, 28), Source = ReceivableSource.OpeningBalance, SourceKey = "v2-demo-collection-funds-document", ContentHash = "DEMO-COLLECTION-FUNDS-V1", ActorUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.ReceivableMovements.Add(new() { Id = Guid.Parse("83000012-0000-0000-0000-000000000001"), AcademyId = enrollment.AcademyId, DocumentId = DocumentId, Kind = ReceivableMovementKind.PaymentAllocation, Amount = 100, Currency = "EGP", CollectionId = collectionId, Reason = "تخصيص اصطناعي للديمو", SourceKey = "v2-demo-collection-funds-allocation", ContentHash = "DEMO-COLLECTION-FUNDS-V1", ActorUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.CollectionRefunds.Add(new() { Id = Guid.Parse("83000013-0000-0000-0000-000000000001"), AcademyId = enrollment.AcademyId, CollectionId = collectionId, PlayerId = enrollment.PlayerId, Amount = 50, Currency = "EGP", Reason = "استرداد اصطناعي للديمو — لا دفع فعلي", PerformedByUserId = actor, IdempotencyKey = "v2-demo-collection-funds-refund", CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
