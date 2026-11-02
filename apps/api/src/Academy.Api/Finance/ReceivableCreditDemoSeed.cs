using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

// Enabled only by Demo/LegacyRegression opt-in. Stored synthetic paid source;
// no automatic credit application, reset, live payment or customer data.
public static class ReceivableCreditDemoSeed
{
    public static readonly Guid SourceId = Guid.Parse("85000017-0000-0000-0000-000000000004");
    public static readonly Guid TargetId = Guid.Parse("85000017-0000-0000-0000-000000000005");
    public static async Task Seed(FoundationDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"v2-demo-credit-application"},0))");
        var academy = DemoSeed.NogoomAcademyId;
        if (await db.ReceivableDocuments.AnyAsync(d => d.AcademyId == academy && d.Id == SourceId)) return;
        var plan = await db.SubscriptionPlans.SingleAsync(p => p.Id == Slice3DemoSeed.FootballMonthlyId);
        if (plan.Price < 200 || plan.Currency != "EGP") return;
        var player = Guid.Parse("85000017-0000-0000-0000-000000000001"); var enrollmentId = Guid.Parse("85000017-0000-0000-0000-000000000002");
        if (await db.Players.AnyAsync(p => p.Id == player) || await db.SportEnrollments.AnyAsync(e => e.Id == enrollmentId)) return; // Never repair a partially altered fixture.
        var now = new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero);
        var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
        var guardian = await db.GuardianProfiles.Where(g => g.AcademyId == academy && g.User.PhoneNumber == DemoSeed.GuardianPhone).Select(g => g.Id).SingleAsync();
        db.Players.Add(new() { Id = player, AcademyId = academy, ArabicName = "زياد — رصيد دائن اصطناعي", PlayerCode = "V2-CREDIT-DEMO-V2", DateOfBirth = new(2016, 6, 1), CreatedAtUtc = now, UpdatedAtUtc = now });
        var enrollment = new SportEnrollment { Id = enrollmentId, AcademyId = academy, PlayerId = player, SportId = Slice2DemoSeed.FootballId, BranchId = Slice2DemoSeed.CityBranchId, TrainingGroupId = Slice2DemoSeed.FootballGroupId, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.SportEnrollments.Add(enrollment); db.GuardianPlayerLinks.Add(new() { Id = Guid.Parse("85000017-0000-0000-0000-000000000003"), AcademyId = academy, PlayerId = player, GuardianId = guardian, CreatedByUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now }); await db.SaveChangesAsync();
        var root = Guid.Parse("85000017-0000-0000-0000-000000000006");
        await Slice3DemoSeed.Confirmed(db, root, enrollment, Slice3DemoSeed.FootballMonthlyId, actor, new(2026, 8, 1), new(2026, 8, 30), now, now, default);
        var bytes = root.ToByteArray(); bytes[14] = 3; var collection = new Guid(bytes);
        var collected = await db.Collections.Where(c => c.Id == collection).Select(c => c.Amount).SingleAsync();
        foreach (var (id, amount, label) in new[] { (SourceId, collected, "مصدر رصيد دائن اصطناعي"), (TargetId, 300m, "مطالبة مستفيدة اصطناعية") }) db.ReceivableDocuments.Add(new() { Id = id, AcademyId = academy, SportEnrollmentId = enrollmentId, PlayerId = player, PlayerNameSnapshot = "زياد — رصيد دائن اصطناعي", BranchId = enrollment.BranchId, Amount = amount, Currency = "EGP", DueDate = new(2026, 9, 20), Source = ReceivableSource.ManualCharge, Description = label, SourceKey = $"v2-credit-demo-{id}", ContentHash = "DEMO-CREDIT-V1", ActorUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.ReceivableMovements.AddRange(new ReceivableMovement { Id = Guid.Parse("85000017-0000-0000-0000-000000000007"), AcademyId = academy, DocumentId = SourceId, Kind = ReceivableMovementKind.PaymentAllocation, Amount = collected, Currency = "EGP", CollectionId = collection, Reason = "تخصيص التحصيل الاصطناعي", SourceKey = "v2-credit-demo-v2-allocation", ContentHash = "DEMO-CREDIT-V2", ActorUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now }, new ReceivableMovement { Id = Guid.Parse("85000017-0000-0000-0000-000000000008"), AcademyId = academy, DocumentId = SourceId, Kind = ReceivableMovementKind.CreditNote, Amount = 200, Currency = "EGP", Reason = "تصحيح المطالبة بعد سدادها — رصيد دائن200", SourceKey = "v2-credit-demo-v2-note", ContentHash = "DEMO-CREDIT-V2", ActorUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
