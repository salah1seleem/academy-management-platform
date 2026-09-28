using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice3;

public static class Slice3DemoSeed
{
    public static readonly Guid FootballMonthlyId = Guid.Parse("51000000-0000-0000-0000-000000000001");
    public static readonly Guid FootballQuarterlyId = Guid.Parse("51000000-0000-0000-0000-000000000002");
    public static readonly Guid FootballAnnualId = Guid.Parse("51000000-0000-0000-0000-000000000003");
    public static readonly Guid FootballSessionsId = Guid.Parse("51000000-0000-0000-0000-000000000004");
    public static readonly Guid SwimmingMonthlyId = Guid.Parse("51000000-0000-0000-0000-000000000005");
    public const string ExternalRenewalCode = "RNW-DEMO-NG-0003-7K9M";
    public const string FutureAcademyRenewalCode = "RNW-DEMO-FT-0001-4Q2X";

    public static async Task SeedAsync(FoundationDbContext db, Guid guardianUserId, DateTimeOffset now, CancellationToken ct)
    {
        await Plan(db, FootballMonthlyId, DemoSeed.NogoomAcademyId, Slice2DemoSeed.FootballId, "اشتراك شهري", SubscriptionPlanType.Duration, 900m, 30, null, 1, now, ct);
        await Plan(db, FootballQuarterlyId, DemoSeed.NogoomAcademyId, Slice2DemoSeed.FootballId, "اشتراك 3 أشهر", SubscriptionPlanType.Duration, 2500m, 90, null, 2, now, ct);
        await Plan(db, FootballAnnualId, DemoSeed.NogoomAcademyId, Slice2DemoSeed.FootballId, "اشتراك سنوي", SubscriptionPlanType.Duration, 8500m, 365, null, 3, now, ct);
        await Plan(db, FootballSessionsId, DemoSeed.NogoomAcademyId, Slice2DemoSeed.FootballId, "باقة 12 حصة", SubscriptionPlanType.Sessions, 1200m, null, 12, 4, now, ct);
        await Plan(db, SwimmingMonthlyId, DemoSeed.NogoomAcademyId, Slice2DemoSeed.SwimmingId, "اشتراك سباحة شهري", SubscriptionPlanType.Combined, 1100m, 30, 12, 1, now, ct);
        var bSport = await db.Sports.Where(x => x.AcademyId == DemoSeed.FutureAcademyId).Select(x => x.Id).FirstAsync(ct);
        await Plan(db, Guid.Parse("52000000-0000-0000-0000-000000000001"), DemoSeed.FutureAcademyId, bSport, "اشتراك المستقبل", SubscriptionPlanType.Duration, 700m, 30, null, 1, now, ct);

        var omarFootball = await Enrollment(db, Slice2DemoSeed.OmarPlayerId, Slice2DemoSeed.FootballId, ct);
        var omarSwimming = await Enrollment(db, Slice2DemoSeed.OmarPlayerId, Slice2DemoSeed.SwimmingId, ct);
        var mariamFootball = await Enrollment(db, Slice2DemoSeed.MariamPlayerId, Slice2DemoSeed.FootballId, ct);
        await Confirmed(db, Guid.Parse("53000000-0000-0000-0000-000000000001"), omarFootball, FootballMonthlyId, guardianUserId, new DateOnly(2026, 9, 5), new DateOnly(2026, 10, 4), now.AddDays(-23), now, ct);
        await Confirmed(db, Guid.Parse("53000000-0000-0000-0000-000000000002"), omarFootball, FootballMonthlyId, guardianUserId, new DateOnly(2026, 10, 5), new DateOnly(2026, 11, 3), now.AddDays(-2), now, ct);
        await Confirmed(db, Guid.Parse("53000000-0000-0000-0000-000000000003"), omarSwimming, SwimmingMonthlyId, guardianUserId, new DateOnly(2026, 9, 20), new DateOnly(2026, 10, 19), now.AddDays(-8), now, ct);
        await Confirmed(db, Guid.Parse("53000000-0000-0000-0000-000000000004"), mariamFootball, FootballMonthlyId, guardianUserId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 30), now.AddDays(-58), now, ct);
        await Incomplete(db, Guid.Parse("54000000-0000-0000-0000-000000000001"), mariamFootball, FootballMonthlyId, guardianUserId, PaymentRequestStatus.Failed, RenewalRequestStatus.Failed, now.AddDays(-1), ct);
        await Incomplete(db, Guid.Parse("54000000-0000-0000-0000-000000000002"), omarSwimming, SwimmingMonthlyId, guardianUserId, PaymentRequestStatus.Pending, RenewalRequestStatus.PaymentInProgress, now, ct);
        var otherFootball = await Enrollment(db, Slice2DemoSeed.OtherPlayerId, Slice2DemoSeed.FootballId, ct);
        await Reference(db, Guid.Parse("55000000-0000-0000-0000-000000000001"), otherFootball, ExternalRenewalCode, guardianUserId, now, ct);
        var academyBEnrollment = await db.SportEnrollments.SingleAsync(x => x.AcademyId == DemoSeed.FutureAcademyId && x.PlayerId == Slice2DemoSeed.AcademyBPlayerId, ct);
        var academyBOwner = await db.AcademyMemberships.Where(x => x.AcademyId == DemoSeed.FutureAcademyId && x.Role == Academy.Infrastructure.Tenancy.AcademyRole.AcademyOwner).Select(x => x.UserId).SingleAsync(ct);
        await Reference(db, Guid.Parse("55000000-0000-0000-0000-000000000002"), academyBEnrollment, FutureAcademyRenewalCode, academyBOwner, now, ct);
    }

    internal static async Task Plan(FoundationDbContext db, Guid id, Guid academy, Guid sport, string name, SubscriptionPlanType type, decimal price, int? days, int? sessions, int order, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.SubscriptionPlans.AnyAsync(x => x.Id == id, ct)) return;
        db.Add(new SubscriptionPlan { Id = id, AcademyId = academy, SportId = sport, ArabicName = name, PlanType = type, Price = price, Currency = "EGP", DurationDays = days, SessionCount = sessions, DisplayOrder = order, CreatedAtUtc = now, UpdatedAtUtc = now }); await db.SaveChangesAsync(ct);
    }
    private static Task<Academy.Infrastructure.People.SportEnrollment> Enrollment(FoundationDbContext db, Guid player, Guid sport, CancellationToken ct) => db.SportEnrollments.SingleAsync(x => x.PlayerId == player && x.SportId == sport, ct);

    internal static async Task Confirmed(FoundationDbContext db, Guid root, Academy.Infrastructure.People.SportEnrollment enrollment, Guid planId, Guid user, DateOnly start, DateOnly end, DateTimeOffset paid, DateTimeOffset now, CancellationToken ct)
    {
        var paymentId = Change(root, 1); if (await db.PaymentRequests.AnyAsync(x => x.Id == paymentId, ct)) return;
        var plan = await db.SubscriptionPlans.Include(x => x.Sport).SingleAsync(x => x.Id == planId, ct); var renewalId = Change(root, 2); var collectionId = Change(root, 3); var receiptId = Change(root, 4); var periodId = Change(root, 5);
        var renewal = new RenewalRequest { Id = renewalId, AcademyId = enrollment.AcademyId, SportEnrollmentId = enrollment.Id, SportId = enrollment.SportId, SubscriptionPlanId = plan.Id, RequestedByUserId = user, RequestedAtUtc = paid, AmountExpected = plan.Price, Currency = plan.Currency, Status = RenewalRequestStatus.Paid, PaymentRequestId = paymentId, IdempotencyKey = $"seed-{root:N}", CreatedAtUtc = paid, UpdatedAtUtc = paid };
        var payment = new PaymentRequest { Id = paymentId, AcademyId = enrollment.AcademyId, RenewalRequestId = renewalId, Provider = InternalTestPaymentGateway.ProviderName, ProviderEnvironment = "Demo", Amount = plan.Price, Currency = plan.Currency, Status = PaymentRequestStatus.Confirmed, ProviderReference = $"ITP-{paymentId:N}", CheckoutReference = $"seed-{paymentId:N}", ConfirmedAtUtc = paid, LastProviderEventAtUtc = paid, CreatedAtUtc = paid, UpdatedAtUtc = paid };
        var collection = new PaymentCollection { Id = collectionId, AcademyId = enrollment.AcademyId, SportEnrollmentId = enrollment.Id, RenewalRequestId = renewalId, PaymentRequestId = paymentId, Amount = plan.Price, Currency = plan.Currency, PaymentMethod = "Online", Provider = InternalTestPaymentGateway.ProviderName, ProviderReference = payment.ProviderReference, ConfirmedAtUtc = paid, ConfirmedBy = "system/provider", CreatedAtUtc = paid, UpdatedAtUtc = paid };
        var player = await db.Players.SingleAsync(x => x.Id == enrollment.PlayerId, ct);
        db.AddRange(renewal, payment, collection,
            new Receipt { Id = receiptId, AcademyId = enrollment.AcademyId, CollectionId = collectionId, ReceiptNumber = $"DEMO-{receiptId:N}".ToUpperInvariant(), PlayerId = player.Id, SportEnrollmentId = enrollment.Id, SubscriptionPlanId = plan.Id, PlayerNameSnapshot = player.ArabicName, SportNameSnapshot = plan.Sport.ArabicName, PlanNameSnapshot = plan.ArabicName, Amount = plan.Price, Currency = plan.Currency, PaidAtUtc = paid, PaymentMethod = "الدفع الإلكتروني", ProviderReference = payment.ProviderReference, CreatedAtUtc = paid, UpdatedAtUtc = paid },
            new SubscriptionPeriod { Id = periodId, AcademyId = enrollment.AcademyId, SportEnrollmentId = enrollment.Id, SportId = enrollment.SportId, SubscriptionPlanId = plan.Id, CollectionId = collectionId, StartDate = start, EndDate = end, InitialSessions = plan.SessionCount, RemainingSessions = plan.SessionCount, Status = start > DateOnly.FromDateTime(now.Date) ? SubscriptionPeriodStatus.Scheduled : end < DateOnly.FromDateTime(now.Date) ? SubscriptionPeriodStatus.Expired : SubscriptionPeriodStatus.Active, PriceSnapshot = plan.Price, CurrencySnapshot = plan.Currency, CreatedByUserId = user, CreatedAtUtc = paid, UpdatedAtUtc = paid });
        await db.SaveChangesAsync(ct);
    }

    private static async Task Incomplete(FoundationDbContext db, Guid root, Academy.Infrastructure.People.SportEnrollment enrollment, Guid planId, Guid user, PaymentRequestStatus paymentStatus, RenewalRequestStatus renewalStatus, DateTimeOffset at, CancellationToken ct)
    {
        var paymentId = Change(root, 1); if (await db.PaymentRequests.AnyAsync(x => x.Id == paymentId, ct)) return; var plan = await db.SubscriptionPlans.SingleAsync(x => x.Id == planId, ct); var renewalId = Change(root, 2);
        db.AddRange(new RenewalRequest { Id = renewalId, AcademyId = enrollment.AcademyId, SportEnrollmentId = enrollment.Id, SportId = enrollment.SportId, SubscriptionPlanId = plan.Id, RequestedByUserId = user, RequestedAtUtc = at, AmountExpected = plan.Price, Currency = plan.Currency, Status = renewalStatus, PaymentRequestId = paymentId, IdempotencyKey = $"seed-{root:N}", CreatedAtUtc = at, UpdatedAtUtc = at }, new PaymentRequest { Id = paymentId, AcademyId = enrollment.AcademyId, RenewalRequestId = renewalId, Provider = InternalTestPaymentGateway.ProviderName, ProviderEnvironment = "Demo", Amount = plan.Price, Currency = plan.Currency, Status = paymentStatus, ProviderReference = $"ITP-{paymentId:N}", CheckoutReference = $"seed-{paymentId:N}", CreatedAtUtc = at, UpdatedAtUtc = at }); await db.SaveChangesAsync(ct);
    }
    private static async Task Reference(FoundationDbContext db, Guid id, Academy.Infrastructure.People.SportEnrollment enrollment, string code, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.BeneficiaryRenewalReferences.AnyAsync(x => x.Id == id, ct)) return;
        db.Add(new BeneficiaryRenewalReference { Id = id, AcademyId = enrollment.AcademyId, SportEnrollmentId = enrollment.Id, CodeHash = BeneficiaryRenewalCodes.Hash(code), CodeHint = BeneficiaryRenewalCodes.Hint(code), ExpiresAtUtc = now.AddYears(2), GeneratedByUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now }); await db.SaveChangesAsync(ct);
    }
    private static Guid Change(Guid id, byte value) { var bytes = id.ToByteArray(); bytes[14] = value; return new Guid(bytes); }
}
