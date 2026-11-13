using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class SubscriptionPaymentTests
{
    private static readonly DateTimeOffset CairoMidnight = new(2026, 11, 11, 22, 0, 0, TimeSpan.Zero);
    private const string SubscriptionPeriodsPath = "/api/v1/subscriptions/periods";
    private const string GuardianPeriodsPath = "/api/v1/guardian/subscriptions/periods";

    [Fact]
    public async Task Guardian_surfaces_preserve_current_and_next_paid_period_across_midnight_and_link_removal()
    {
        await using var family = await NewFamily(mixedSports: true);
        await using var otherFamily = await NewFamily();
        var now = new SubscriptionCalendarTime(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero));
        await using var host = SubscriptionCalendarHost(now);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var academy = DemoSeed.NogoomAcademyId;
        var enrollment = family.Choices[0].SportEnrollmentId;
        var plan = family.Choices[0].SubscriptionPlanId;
        var player = await db.SportEnrollments.Where(e => e.Id == enrollment).Select(e => e.PlayerId).SingleAsync();
        var actor = await db.AcademyMemberships.Where(m => m.AcademyId == academy && m.Role == AcademyRole.AcademyOwner)
            .OrderBy(m => m.Id).Select(m => m.UserId).FirstAsync();

        async Task<SubscriptionPeriod> Pay()
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var payment = await scope.ServiceProvider.GetRequiredService<CollectionService>()
                .CreateCashAsync(academy, actor, enrollment, plan, $"family-period-{Guid.NewGuid():N}", "Online");
            await transaction.CommitAsync();
            return await db.SubscriptionPeriods.SingleAsync(p => p.Collection!.PaymentRequestId == payment);
        }

        var current = await Pay();
        now.Value = now.Value.AddDays(2);
        var next = await Pay();
        now.Value = now.Value.AddDays(1);
        var later = await Pay();
        Assert.Equal(new DateOnly(2026, 11, 30), current.EndDate);
        Assert.Equal(new DateOnly(2026, 12, 1), next.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 31), later.StartDate);
        Assert.True(later.UpdatedAtUtc > next.UpdatedAtUtc);
        var receipt = await db.Receipts.SingleAsync(r => r.CollectionId == next.CollectionId);
        var profilePath = $"/api/v1/guardian/children/{player}/profile";
        const string listPath = "/api/v1/guardian/subscriptions/enrollments";
        var rollover = new DateTimeOffset(2026, 11, 30, 22, 0, 0, TimeSpan.Zero);
        now.Value = rollover.AddSeconds(-1);
        using var guardian = await BoundaryGuardian(host, family.User);
        using var stranger = await BoundaryGuardian(host, otherFamily.User);
        JsonElement EnrollmentRow(JsonElement rows) => Assert.Single(rows.EnumerateArray(), e => e.GetProperty("id").GetGuid() == enrollment);
        async Task AssertTimeline(Guid periodId, Guid nextId, string status)
        {
            var home = await ReadBoundaryJson(await guardian.GetAsync("/api/v1/guardian/home"));
            var child = Assert.Single(home.GetProperty("children").EnumerateArray(), x => x.GetProperty("id").GetGuid() == player);
            var profile = await ReadBoundaryJson(await guardian.GetAsync(profilePath));
            var profileRow = EnrollmentRow(profile.GetProperty("enrollments"));
            var picker = EnrollmentRow(await ReadBoundaryJson(await guardian.GetAsync(listPath)));
            foreach (var row in new[] { EnrollmentRow(child.GetProperty("sports")), profileRow, picker })
            {
                Assert.Equal(periodId, row.GetProperty("period").GetProperty("id").GetGuid());
                Assert.Equal(status, row.GetProperty("period").GetProperty("status").GetString());
            }
            Assert.Equal(nextId, profileRow.GetProperty("nextPeriod").GetProperty("id").GetGuid());
            var noPeriod = Assert.Single((await ReadBoundaryJson(await guardian.GetAsync(listPath))).EnumerateArray(),
                e => e.GetProperty("id").GetGuid() == family.Choices[1].SportEnrollmentId);
            Assert.Equal(JsonValueKind.Null, noPeriod.GetProperty("period").ValueKind);
        }

        await AssertTimeline(current.Id, next.Id, "Expiring");
        Assert.Equal("Scheduled", (await BoundaryPeriod(guardian, next.Id, guardian: true)).GetProperty("status").GetString());
        var receiptPath = $"/api/v1/guardian/subscriptions/receipts/{receipt.Id}";
        var displayedReceipt = await ReadBoundaryJson(await guardian.GetAsync(receiptPath));
        Assert.Equal(receipt.ReceiptNumber, displayedReceipt.GetProperty("receiptNumber").GetString());
        Assert.Equal(900m, displayedReceipt.GetProperty("amount").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(receiptPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(profilePath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"{GuardianPeriodsPath}/{next.Id}/adjustments")).StatusCode);

        now.Value = rollover;
        await AssertTimeline(next.Id, later.Id, "Active");
        Assert.Equal("Active", (await BoundaryPeriod(guardian, next.Id, guardian: true)).GetProperty("status").GetString());
        next.Status = SubscriptionPeriodStatus.Frozen;
        await db.SaveChangesAsync();
        await AssertTimeline(next.Id, later.Id, "Frozen");
        next.Status = SubscriptionPeriodStatus.Cancelled;
        next.UpdatedAtUtc = now.Value;
        await db.SaveChangesAsync();
        await AssertTimeline(later.Id, later.Id, "Scheduled");

        var link = await db.GuardianPlayerLinks.SingleAsync(l => l.GuardianId == family.Guardian && l.PlayerId == player);
        link.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync(profilePath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync($"{GuardianPeriodsPath}/{next.Id}/adjustments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync(receiptPath)).StatusCode);
        Assert.DoesNotContain((await ReadBoundaryJson(await guardian.GetAsync(listPath))).EnumerateArray(),
            e => e.GetProperty("id").GetGuid() == enrollment);
        Assert.Equal(3, await Count<SubscriptionPeriod>(p => p.SportEnrollmentId == enrollment));
        Assert.Equal(3, await Count<PaymentCollection>(p => p.SportEnrollmentId == enrollment));
        Assert.Equal(3, await Count<Receipt>(p => p.SportEnrollmentId == enrollment));
    }

    [Fact]
    public async Task Subscription_calendar_changes_waiver_quote_list_and_guardian_status_at_Cairo_midnight()
    {
        await using var family = await NewFamily();
        var now = new SubscriptionCalendarTime(CairoMidnight.AddDays(-29));
        await using var host = SubscriptionCalendarHost(now);
        using var priorOwner = host.CreateClient();
        await StaffLogin(priorOwner);
        var expiredChoice = family.Choices[0];
        var freshChoice = family.Choices[1];
        var (priorQuote, priorBody) = await ReviewAtBoundary(priorOwner, expiredChoice.SportEnrollmentId, expiredChoice.SubscriptionPlanId);
        Assert.Equal("2026-10-14", priorQuote.GetProperty("startDate").GetString());
        Assert.Equal("2026-11-12", priorQuote.GetProperty("endDate").GetString());

        // A second fixture ends one day earlier and must leave the active list at the boundary.
        await using var endingYesterday = await NewFamily();
        now.Value = CairoMidnight.AddDays(-30);
        using var historicalOwner = host.CreateClient();
        await StaffLogin(historicalOwner);
        var earlierChoice = endingYesterday.Choices[0];
        var (earlierQuote, earlierBody) = await ReviewAtBoundary(historicalOwner, earlierChoice.SportEnrollmentId, earlierChoice.SubscriptionPlanId);
        Assert.Equal("2026-11-11", earlierQuote.GetProperty("endDate").GetString());
        var earlier = await ReadBoundaryJson(await Waive(historicalOwner, earlierBody));
        var earlierId = earlier.GetProperty("periodId").GetGuid();

        now.Value = CairoMidnight.AddDays(-29);
        var prior = await ReadBoundaryJson(await Waive(priorOwner, priorBody));
        var priorId = prior.GetProperty("periodId").GetGuid();
        now.Value = CairoMidnight.AddSeconds(-1);
        using var owner = host.CreateClient();
        await StaffLogin(owner);
        using var guardian = await BoundaryGuardian(host, family.User);
        using var earlierGuardian = await BoundaryGuardian(host, endingYesterday.User);
        using var signedOut = host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await signedOut.GetAsync($"{SubscriptionPeriodsPath}/{priorId}")).StatusCode);

        var (before, staleBody) = await ReviewAtBoundary(owner, freshChoice.SportEnrollmentId, freshChoice.SubscriptionPlanId);
        Assert.Equal("2026-11-11", before.GetProperty("startDate").GetString());
        Assert.Equal("2026-12-10", before.GetProperty("endDate").GetString());
        Assert.Equal("Active", (await BoundaryPeriod(earlierGuardian, earlierId, guardian: true)).GetProperty("status").GetString());
        Assert.Contains(await BoundaryList(owner, "active"), row => row.GetProperty("id").GetGuid() == earlierId);

        now.Value = CairoMidnight;
        var (after, body) = await ReviewAtBoundary(owner, freshChoice.SportEnrollmentId, freshChoice.SubscriptionPlanId);
        Assert.Equal("2026-11-12", after.GetProperty("startDate").GetString());
        Assert.Equal("2026-12-11", after.GetProperty("endDate").GetString());
        Assert.Equal("0.00", after.GetProperty("payable").GetString());
        Assert.Equal("900.00", after.GetProperty("waivedAmount").GetString());
        Assert.NotEqual(staleBody.QuoteHash, body.QuoteHash);
        Assert.Equal(HttpStatusCode.Conflict, (await Waive(owner, staleBody)).StatusCode);
        Assert.Equal(0, await Count<SubscriptionPeriod>(p => p.SportEnrollmentId == freshChoice.SportEnrollmentId));
        Assert.Equal(0, await Count<FreeTrainingApproval>(p => p.SportEnrollmentId == freshChoice.SportEnrollmentId));

        var approved = await ReadBoundaryJson(await Waive(owner, body));
        var id = approved.GetProperty("periodId").GetGuid();
        Assert.Equal(1, await Count<SubscriptionPeriod>(p => p.SportEnrollmentId == freshChoice.SportEnrollmentId));
        Assert.Equal(0, await Count<PaymentCollection>(p => p.SportEnrollmentId == freshChoice.SportEnrollmentId));
        Assert.Equal(0, await Count<Receipt>(p => p.SportEnrollmentId == freshChoice.SportEnrollmentId));
        Assert.Equal(0, await Count<ReceivableDocument>(p => p.SportEnrollmentId == freshChoice.SportEnrollmentId));
        var ownerPeriod = await BoundaryPeriod(owner, id);
        Assert.Equal("2026-11-12", ownerPeriod.GetProperty("asOfDate").GetString());
        Assert.Equal("Active", ownerPeriod.GetProperty("status").GetString());
        var familyPeriod = await BoundaryPeriod(guardian, id, guardian: true);
        Assert.Equal("Active", familyPeriod.GetProperty("status").GetString());
        Assert.Equal("2026-11-12", familyPeriod.GetProperty("startDate").GetString());
        Assert.DoesNotContain(body.Reason!, familyPeriod.ToString());
        Assert.Contains(await BoundaryList(owner, "active"), row => row.GetProperty("id").GetGuid() == id);
        Assert.DoesNotContain(await BoundaryList(owner, "active"), row => row.GetProperty("id").GetGuid() == earlierId);
        Assert.Contains(await BoundaryList(owner, "expired"), row => row.GetProperty("id").GetGuid() == earlierId);
        Assert.Equal("Expired", (await BoundaryPeriod(earlierGuardian, earlierId, guardian: true)).GetProperty("status").GetString());
        Assert.Equal("Expired", (await BoundaryPeriod(owner, earlierId)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await earlierGuardian.GetAsync($"{GuardianPeriodsPath}/{id}/adjustments")).StatusCode);

        var (next, _) = await ReviewAtBoundary(owner, expiredChoice.SportEnrollmentId, expiredChoice.SubscriptionPlanId);
        Assert.Equal("Active", (await BoundaryPeriod(owner, priorId)).GetProperty("status").GetString());
        Assert.Equal("2026-11-13", next.GetProperty("startDate").GetString());
        var renewal = await ReadBoundaryJson(await owner.GetAsync($"/api/v1/subscriptions/admin-renewals/enrollments/{expiredChoice.SportEnrollmentId}"));
        Assert.Equal("2026-11-13", renewal.GetProperty("previewStart").GetString());
        Assert.Equal("Active", renewal.GetProperty("current").GetProperty("status").GetString());
        now.Value = CairoMidnight.AddSeconds(-1);
        Assert.Equal("Scheduled", (await BoundaryPeriod(owner, id)).GetProperty("status").GetString());
        Assert.Equal("Scheduled", (await BoundaryPeriod(guardian, id, guardian: true)).GetProperty("status").GetString());
        Assert.DoesNotContain(await BoundaryList(owner, "active"), row => row.GetProperty("id").GetGuid() == id);
    }

    [Theory]
    [InlineData("2026-11-11T23:59:00+02:00")]
    [InlineData("2026-11-12T00:00:00+02:00")]
    [InlineData("2026-07-15T00:10:00+03:00")]
    public async Task Subscription_calendar_posts_provider_renewal_on_payment_academy_day_without_a_member_session(string paidLocal)
    {
        var expected = DateTimeOffset.Parse(paidLocal, CultureInfo.InvariantCulture);
        var paid = expected.ToUniversalTime();
        var paidDay = DateOnly.FromDateTime(expected.DateTime);
        await using var family = await NewFamily();
        await using var onlineRoute = await InternalGatewayRouteScope.Begin(factory.Services);
        var now = new SubscriptionCalendarTime(paid.AddDays(-31));
        await using var host = SubscriptionCalendarHost(now);
        using var historicalOwner = host.CreateClient();
        await StaffLogin(historicalOwner);
        var choice = family.Choices[0];
        var (_, priorBody) = await ReviewAtBoundary(historicalOwner, choice.SportEnrollmentId, choice.SubscriptionPlanId);
        (await Waive(historicalOwner, priorBody)).EnsureSuccessStatusCode();

        now.Value = paid;
        using var guardian = await BoundaryGuardian(host, family.User);
        var body = new { sportEnrollmentId = choice.SportEnrollmentId, subscriptionPlanId = choice.SubscriptionPlanId };
        using var unrelated = await GuardianClient();
        Assert.Equal(HttpStatusCode.NotFound, (await Post(unrelated, "/api/v1/guardian/subscriptions/renewals", body, await Csrf(unrelated), Guid.NewGuid().ToString("N"))).StatusCode);
        var intent = await ReadBoundaryJson(await Post(guardian, "/api/v1/guardian/subscriptions/renewals", body, await Csrf(guardian), Guid.NewGuid().ToString("N")));
        var paymentId = intent.GetProperty("paymentId").GetGuid();
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var payment = await db.PaymentRequests.AsNoTracking().SingleAsync(p => p.Id == paymentId);
        var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGateway>();
        Assert.Equal(gateway.Name, payment.Provider);
        var notification = gateway.CreateTestEvent(payment, PaymentEventOutcome.Success);
        using var provider = host.CreateClient();
        _ = await ReadBoundaryJson(await provider.PostAsJsonAsync("/api/v1/payments/internal-test/events", notification));
        _ = await ReadBoundaryJson(await provider.PostAsJsonAsync("/api/v1/payments/internal-test/events", notification));

        var period = await db.SubscriptionPeriods.SingleAsync(p => p.Collection.PaymentRequestId == paymentId);
        var receipt = await db.Receipts.SingleAsync(r => r.Collection.PaymentRequestId == paymentId);
        var collection = await db.Collections.SingleAsync(c => c.PaymentRequestId == paymentId);
        Assert.Equal(paidDay, period.StartDate);
        Assert.Equal(paid, receipt.PaidAtUtc);
        Assert.Equal(paid, collection.ConfirmedAtUtc);
        Assert.StartsWith($"RC-{paidDay:yyyyMMdd}-", receipt.ReceiptNumber);
        Assert.Equal(payment.Amount, period.PriceSnapshot);
        Assert.Equal(payment.Amount, receipt.Amount);
        Assert.Equal(payment.Amount, collection.Amount);
        Assert.Equal("Active", (await BoundaryPeriod(guardian, period.Id, guardian: true)).GetProperty("status").GetString());

        using var owner = host.CreateClient();
        await StaffLogin(owner);
        var ownerPath = $"/api/v1/subscriptions/receipts/{receipt.Id}";
        var familyPath = $"/api/v1/guardian/subscriptions/receipts/{receipt.Id}";
        var ownerReceipt = await ReadBoundaryJson(await owner.GetAsync(ownerPath));
        var familyReceipt = await ReadBoundaryJson(await guardian.GetAsync(familyPath));
        var feed = await ReadBoundaryJson(await guardian.GetAsync("/api/v1/guardian/subscriptions/receipts?page=1&allIssued=true"));
        var familyRow = Assert.Single(feed.GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetGuid() == receipt.Id);
        foreach (var row in new[] { ownerReceipt, familyReceipt, familyRow })
        {
            Assert.True(expected.EqualsExact(row.GetProperty("paidAtAcademy").GetDateTimeOffset()));
            Assert.Equal(paid, row.GetProperty("paidAtUtc").GetDateTimeOffset());
            Assert.Equal(receipt.ReceiptNumber, row.GetProperty("receiptNumber").GetString());
            Assert.Equal(payment.Amount, row.GetProperty("amount").GetDecimal());
        }
        Assert.Equal(HttpStatusCode.NotFound, (await unrelated.GetAsync(familyPath)).StatusCode);
        using var foreignOwner = await InboxStaff(DemoSeed.FutureOwnerEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await foreignOwner.GetAsync(ownerPath)).StatusCode);
        var report = $"/api/v1/reports/financial?collectionType=Subscription&search={receipt.ReceiptNumber}";
        var paidRows = await ReadBoundaryJson(await owner.GetAsync($"{report}&from={paidDay:yyyy-MM-dd}&to={paidDay:yyyy-MM-dd}"));
        Assert.Equal(receipt.Id, Assert.Single(paidRows.GetProperty("items").EnumerateArray()).GetProperty("receiptId").GetGuid());
        Assert.Equal(receipt.Amount, paidRows.GetProperty("netAmount").GetDecimal());
        var previousDay = paidDay.AddDays(-1);
        var priorRows = await ReadBoundaryJson(await owner.GetAsync($"{report}&from={previousDay:yyyy-MM-dd}&to={previousDay:yyyy-MM-dd}"));
        Assert.Empty(priorRows.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Subscription_calendar_uses_the_selected_academy_zone_at_the_same_instant()
    {
        var now = new SubscriptionCalendarTime(CairoMidnight);
        await using var host = SubscriptionCalendarHost(now);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var other = await db.Academies.SingleAsync(a => a.Id == DemoSeed.FutureAcademyId);
        var localAcademy = await db.Academies.SingleAsync(a => a.Id == DemoSeed.NogoomAcademyId);
        var original = other.TimeZone;
        var localOriginal = localAcademy.TimeZone;
        other.TimeZone = "Pacific/Honolulu";
        await db.SaveChangesAsync();

        try
        {
            using var cairo = host.CreateClient();
            await StaffLogin(cairo);
            using var honolulu = host.CreateClient();
            (await Post(honolulu, "/api/v1/auth/login", new { email = DemoSeed.FutureOwnerEmail, password = "Demo-Only-123!" }, await Csrf(honolulu))).EnsureSuccessStatusCode();
            const string report = "/api/v1/reports/financial";
            Assert.Equal("2026-11-12", (await ReadBoundaryJson(await cairo.GetAsync(report))).GetProperty("to").GetString());
            Assert.Equal("2026-11-11", (await ReadBoundaryJson(await honolulu.GetAsync(report))).GetProperty("to").GetString());
            var cairoEnrollment = await Enrollment(Slice2DemoSeed.OmarPlayerId, Slice2DemoSeed.FootballId);
            var cairoPeriod = await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == cairoEnrollment).Select(p => p.Id).FirstAsync();
            Assert.Equal(HttpStatusCode.NotFound, (await honolulu.GetAsync($"{SubscriptionPeriodsPath}/{cairoPeriod}")).StatusCode);
            var receiptId = await db.Receipts.Where(r => r.SportEnrollmentId == cairoEnrollment).Select(r => r.Id).FirstAsync();
            var receiptPath = $"/api/v1/subscriptions/receipts/{receiptId}";
            Assert.Equal(HttpStatusCode.NotFound, (await honolulu.GetAsync(receiptPath)).StatusCode);
            var before = await ReadBoundaryJson(await cairo.GetAsync(receiptPath));
            localAcademy.TimeZone = "Pacific/Honolulu";
            await db.SaveChangesAsync();
            var receipt = await ReadBoundaryJson(await cairo.GetAsync(receiptPath));
            var local = receipt.GetProperty("paidAtAcademy").GetDateTimeOffset();
            Assert.Equal(TimeSpan.FromHours(-10), local.Offset);
            Assert.Equal(receipt.GetProperty("paidAtUtc").GetDateTimeOffset(), local);
            Assert.Equal(before.GetProperty("paidAtUtc").GetString(), receipt.GetProperty("paidAtUtc").GetString());
        }
        finally
        {
            other.TimeZone = original;
            localAcademy.TimeZone = localOriginal;
            await db.SaveChangesAsync();
        }
    }

    private WebApplicationFactory<Program> SubscriptionCalendarHost(SubscriptionCalendarTime now) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Demo:ReferenceDate", "");
            builder.UseSetting("Demo:SeedEnabled", "false");
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(now));
        });

    private async Task<HttpClient> BoundaryGuardian(WebApplicationFactory<Program> host, Guid userId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var phone = await db.Users.Where(user => user.Id == userId).Select(user => user.PhoneNumber!).SingleAsync();
        var client = host.CreateClient();
        var csrf = await Csrf(client);
        var challenge = await ReadBoundaryJson(await Post(client, "/api/v1/auth/guardian/otp/request", new { phoneNumber = phone }, csrf));
        (await Post(client, "/api/v1/auth/guardian/otp/verify", new { phoneNumber = phone, challengeId = challenge.GetProperty("challengeId").GetGuid(), code = "246810" }, csrf)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<(JsonElement Quote, FreeTrainingApprove Body)> ReviewAtBoundary(HttpClient owner, Guid enrollment, Guid plan)
    {
        const string reason = "إعفاء اصطناعي لاختبار التوقيت";
        var quote = await ReadBoundaryJson(await Post(owner, Waivers + "/quote", new FreeTrainingReview(enrollment, plan, reason), await Csrf(owner)));
        return (quote, new(enrollment, plan, reason, quote.GetProperty("quoteHash").GetString()!));
    }

    private static async Task<JsonElement[]> BoundaryList(HttpClient owner, string state) =>
        (await ReadBoundaryJson(await owner.GetAsync($"{SubscriptionPeriodsPath}?state={state}"))).EnumerateArray().ToArray();

    private static async Task<JsonElement> BoundaryPeriod(HttpClient client, Guid id, bool guardian = false) =>
        await ReadBoundaryJson(await client.GetAsync(guardian ? $"{GuardianPeriodsPath}/{id}/adjustments" : $"{SubscriptionPeriodsPath}/{id}"));

    private static async Task<JsonElement> ReadBoundaryJson(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
    }

    private sealed class SubscriptionCalendarTime(DateTimeOffset instant) : TimeProvider
    {
        public DateTimeOffset Value { get; set; } = instant;
        public override DateTimeOffset GetUtcNow() => Value;
    }
}
