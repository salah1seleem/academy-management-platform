using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Academy.IntegrationTests;

public sealed partial class SubscriptionPaymentTests
{
    private const string Events = "/api/v1/payments/internal-test/events", Inbox = "/api/v1/finance/provider-quarantine";
    private async Task<ProviderEventEnvelope> Signed(Guid id, Func<ProviderEventEnvelope, ProviderEventEnvelope>? change = null)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var e = scope.ServiceProvider.GetRequiredService<IPaymentGateway>().CreateTestEvent(await db.PaymentRequests.SingleAsync(p => p.Id == id), PaymentEventOutcome.Success);
        if (change is not null) e = change(e);
        var key = scope.ServiceProvider.GetRequiredService<IOptions<InternalTestPaymentOptions>>().Value.SigningKey;
        var payload = string.Join('|', e.AcademyId, e.PaymentRequestId, e.ProviderReference, e.ProviderEventId, e.Status, e.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), e.Currency);
        return e with { Signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(payload))) };
    }
    private async Task<HttpClient> InboxStaff(string email)
    {
        var client = factory.CreateClient();
        if (email == DemoSeed.CoachEmail) await MobileCoachTestSession.SignInAsync(client, factory.Services);
        else (await Post(client, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" }, await Csrf(client))).EnsureSuccessStatusCode();
        return client;
    }
    [Fact]
    public async Task Quarantine_demo_seed_is_repeatable_without_collection_or_activation()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await ProviderQuarantineDemoSeed.Seed(db); await ProviderQuarantineDemoSeed.Seed(db);
        Assert.Equal(1, await db.ProviderEventQuarantines.CountAsync(q => q.Id == ProviderQuarantineDemoSeed.Id(3)));
        Assert.False(await db.Collections.AnyAsync(c => c.PaymentRequestId == ProviderQuarantineDemoSeed.Id(2)));
        Assert.Equal(PaymentRequestStatus.Pending, (await db.PaymentRequests.SingleAsync(p => p.Id == ProviderQuarantineDemoSeed.Id(2))).Status);
    }
    [Theory]
    [InlineData("amount-mismatch")]
    [InlineData("currency-mismatch")]
    [InlineData("reference-mismatch")]
    [InlineData("unsupported-status")]
    public async Task Signed_known_mismatch_is_durably_quarantined_without_cash_or_activation(string reason)
    {
        var id = await NewPayment(Slice2DemoSeed.OmarPlayerId, Slice2DemoSeed.FootballId);
        var e = await Signed(id, e => reason switch { "amount-mismatch" => e with { Amount = e.Amount + .01m }, "currency-mismatch" => e with { Currency = "USD" }, "reference-mismatch" => e with { ProviderReference = "wrong-reference" }, _ => e with { Status = "unknown" } });
        using var source = factory.CreateClient(); var responses = await Task.WhenAll(source.PostAsJsonAsync(Events, e), source.PostAsJsonAsync(Events, e));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.All(await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<PaymentProcessingResult>())), r => { Assert.False(r!.Accepted); Assert.Equal("quarantined", r.State); });
        Assert.Equal(0, await Count<PaymentCollection>(c => c.PaymentRequestId == id));
        Assert.Equal(0, await Count<PaymentProviderEvent>(p => p.PaymentRequestId == id));
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var q = await db.ProviderEventQuarantines.SingleAsync(q => q.PaymentRequestId == id);
        Assert.Equal(reason, q.ReasonCode); Assert.Equal(e.Amount, q.ReceivedAmount); Assert.Equal(e.Currency, q.ReceivedCurrency);
        Assert.Equal(PaymentRequestStatus.Pending, (await db.PaymentRequests.SingleAsync(p => p.Id == id)).Status);
        Assert.InRange(q.CreatedAtUtc, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));
    }
    [Fact]
    public async Task Corrected_signed_event_after_quarantine_confirms_once_and_keeps_original_discrepancy()
    {
        var id = await NewPayment(Slice2DemoSeed.OmarPlayerId, Slice2DemoSeed.FootballId); using var source = factory.CreateClient();
        var bad = await Signed(id, e => e with { Amount = e.Amount + 30m }); Assert.Equal(HttpStatusCode.BadRequest, (await source.PostAsJsonAsync(Events, bad)).StatusCode);
        var correct = await Signed(id, e => e with { ProviderEventId = bad.ProviderEventId });
        var replies = await Task.WhenAll(source.PostAsJsonAsync(Events, correct), source.PostAsJsonAsync(Events, correct)); Assert.All(replies, r => r.EnsureSuccessStatusCode());
        Assert.Equal(1, await Count<PaymentCollection>(p => p.PaymentRequestId == id)); Assert.Equal(1, await Count<Receipt>(r => r.Collection.PaymentRequestId == id)); Assert.Equal(1, await Count<SubscriptionPeriod>(p => p.Collection.PaymentRequestId == id));
        Assert.Equal(1, await Count<ProviderEventQuarantine>(q => q.PaymentRequestId == id));
        Assert.Equal(HttpStatusCode.BadRequest, (await source.PostAsJsonAsync(Events, bad)).StatusCode);
        using var owner = await InboxStaff(DemoSeed.OwnerEmail); var list = await owner.GetFromJsonAsync<JsonElement>(Inbox);
        await using var scope = factory.Services.CreateAsyncScope(); var q = await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().ProviderEventQuarantines.SingleAsync(q => q.PaymentRequestId == id);
        var detail = await owner.GetFromJsonAsync<JsonElement>($"{Inbox}/{q.Id}"); Assert.Equal("Confirmed", detail.GetProperty("paymentStatus").GetString()); Assert.NotEqual(Guid.Empty, detail.GetProperty("receiptId").GetGuid());
    }
    [Fact]
    public async Task Same_event_id_with_another_request_or_amount_never_replays_as_success()
    {
        var first = await NewPayment(Slice2DemoSeed.OmarPlayerId, Slice2DemoSeed.FootballId); var second = await NewPayment(Slice2DemoSeed.OmarPlayerId, Slice2DemoSeed.FootballId);
        using var source = factory.CreateClient(); var e = await Signed(first); (await source.PostAsJsonAsync(Events, e)).EnsureSuccessStatusCode();
        var other = await Signed(second, v => v with { ProviderEventId = e.ProviderEventId });
        Assert.Equal(HttpStatusCode.BadRequest, (await source.PostAsJsonAsync(Events, other)).StatusCode);
        var changed = await Signed(first, v => v with { ProviderEventId = e.ProviderEventId, Amount = e.Amount + 1m });
        Assert.Equal(HttpStatusCode.BadRequest, (await source.PostAsJsonAsync(Events, changed)).StatusCode);
        Assert.Equal(0, await Count<PaymentCollection>(p => p.PaymentRequestId == second)); Assert.Equal(1, await Count<PaymentCollection>(p => p.PaymentRequestId == first));
        Assert.Equal(1, await Count<ProviderEventQuarantine>(q => q.PaymentRequestId == second && q.ReasonCode == "event-reuse"));
    }
    [Fact]
    public async Task Unsigned_malformed_and_unknown_request_events_create_no_followup_or_financial_records()
    {
        var id = await NewPayment(Slice2DemoSeed.OmarPlayerId, Slice2DemoSeed.FootballId); var e = await Signed(id); using var source = factory.CreateClient();
        foreach (var invalid in new[] { e with { Signature = new string('0', 64) }, e with { Signature = null! }, e with { ProviderEventId = new string('x', 101) }, e with { Amount = e.Amount + .001m } })
            Assert.Equal(HttpStatusCode.BadRequest, (await source.PostAsJsonAsync(Events, invalid)).StatusCode);
        var unknown = await Signed(id, e => e with { PaymentRequestId = Guid.NewGuid() }); Assert.Equal(HttpStatusCode.BadRequest, (await source.PostAsJsonAsync(Events, unknown)).StatusCode);
        Assert.Equal(0, await Count<ProviderEventQuarantine>(q => q.PaymentRequestId == id)); Assert.Equal(0, await Count<PaymentCollection>(p => p.PaymentRequestId == id));
    }
    [Fact]
    public async Task Global_provider_event_collision_cannot_accept_or_disclose_another_academy_payment()
    {
        var first = await NewPayment(Slice2DemoSeed.OmarPlayerId, Slice2DemoSeed.FootballId); using var source = factory.CreateClient(); var firstEvent = await Signed(first); (await source.PostAsJsonAsync(Events, firstEvent)).EnsureSuccessStatusCode();
        var paymentId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var academy = DemoSeed.FutureAcademyId;
            var e = await db.SportEnrollments.SingleAsync(e => e.AcademyId == academy && e.PlayerId == Slice2DemoSeed.AcademyBPlayerId);
            var plan = await db.SubscriptionPlans.SingleAsync(p => p.AcademyId == academy); var at = DateTimeOffset.UtcNow;
            var user = await db.AcademyMemberships.Where(m => m.AcademyId == academy && m.User.Email == DemoSeed.FutureOwnerEmail).Select(m => m.UserId).SingleAsync();
            var renewal = new RenewalRequest { Id = Guid.NewGuid(), AcademyId = academy, SportEnrollmentId = e.Id, SportId = e.SportId, SubscriptionPlanId = plan.Id, RequestedByUserId = user, RequestedAtUtc = at,
                OriginalAmount = plan.Price, FinalAmount = plan.Price, AmountExpected = plan.Price, Currency = plan.Currency, Status = RenewalRequestStatus.PaymentInProgress, PaymentRequestId = paymentId, IdempotencyKey = Guid.NewGuid().ToString(), CreatedAtUtc = at, UpdatedAtUtc = at };
            PlanTerms.From(plan).Capture(renewal); db.RenewalRequests.Add(renewal);
            db.PaymentRequests.Add(new() { Id = paymentId, AcademyId = academy, RenewalRequestId = renewal.Id, Provider = "InternalTest", ProviderEnvironment = "Demo", Amount = plan.Price, Currency = plan.Currency, Status = PaymentRequestStatus.Pending, ProviderReference = $"ITP-{paymentId:N}", CheckoutReference = "synthetic-other-academy", CreatedAtUtc = at, UpdatedAtUtc = at }); await db.SaveChangesAsync();
        }
        var collision = await Signed(paymentId, e => e with { ProviderEventId = firstEvent.ProviderEventId }); var rejected = await source.PostAsJsonAsync(Events, collision);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); Assert.Equal("quarantined", (await rejected.Content.ReadFromJsonAsync<PaymentProcessingResult>())!.State);
        Assert.Equal(0, await Count<PaymentCollection>(p => p.PaymentRequestId == paymentId));
        await using var check = factory.Services.CreateAsyncScope(); var row = await check.ServiceProvider.GetRequiredService<FoundationDbContext>().ProviderEventQuarantines.SingleAsync(q => q.PaymentRequestId == paymentId);
        Assert.Equal(DemoSeed.FutureAcademyId, row.AcademyId); Assert.Equal("event-reuse", row.ReasonCode);
        using var owner = await InboxStaff(DemoSeed.OwnerEmail); Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{Inbox}/{row.Id}")).StatusCode);
        using var otherOwner = await InboxStaff(DemoSeed.FutureOwnerEmail); (await otherOwner.GetAsync($"{Inbox}/{row.Id}")).EnsureSuccessStatusCode();
        var correct = await Signed(paymentId); (await source.PostAsJsonAsync(Events, correct)).EnsureSuccessStatusCode(); Assert.Equal(1, await Count<PaymentCollection>(p => p.PaymentRequestId == paymentId));
    }
    [Fact]
    public async Task Owner_inbox_is_tenant_scoped_read_only_with_actual_day_filter_and_no_secrets()
    {
        var id = await NewPayment(Slice2DemoSeed.OmarPlayerId, Slice2DemoSeed.FootballId); using var source = factory.CreateClient();
        var e = await Signed(id, e => e with { Amount = e.Amount + 1m }); await source.PostAsJsonAsync(Events, e);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var q = await db.ProviderEventQuarantines.SingleAsync(q => q.PaymentRequestId == id);
        using var owner = await InboxStaff(DemoSeed.OwnerEmail); var r = await owner.GetAsync($"{Inbox}/{q.Id}"); r.EnsureSuccessStatusCode(); var body = await r.Content.ReadAsStringAsync(); Assert.DoesNotContain("signature", body, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("envelopeHash", body, StringComparison.OrdinalIgnoreCase);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")).DateTime);
        var today = await owner.GetFromJsonAsync<JsonElement>($"{Inbox}?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}"); Assert.True(today.GetProperty("total").GetInt32() > 0);
        var old = await owner.GetFromJsonAsync<JsonElement>($"{Inbox}?from=2020-01-01&to=2020-01-02"); Assert.Equal(0, old.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.GetAsync($"{Inbox}?from=2026-10-20&to=2026-10-01")).StatusCode);
        using var futureOwner = await InboxStaff(DemoSeed.FutureOwnerEmail); Assert.Equal(HttpStatusCode.NotFound, (await futureOwner.GetAsync($"{Inbox}/{q.Id}")).StatusCode);
        using var guardian = await GuardianClient(); Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync(Inbox)).StatusCode);
        using var admin = await InboxStaff(DemoSeed.AdminEmail); Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(Inbox)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await owner.PostAsJsonAsync($"{Inbox}/{q.Id}", new { accepted = true })).StatusCode);
        q.ReasonCode = "changed"; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ProviderEventQuarantines\" SET \"ReceivedAmount\"=0 WHERE \"Id\"={q.Id}"));
    }
    [Fact]
    public async Task Late_failed_event_never_regresses_confirmed_payment_and_different_success_events_share_one_receipt()
    {
        var id = await NewPayment(Slice2DemoSeed.OmarPlayerId, Slice2DemoSeed.FootballId); using var source = factory.CreateClient();
        var first = await Signed(id); var second = await Signed(id); Assert.All(await Task.WhenAll(source.PostAsJsonAsync(Events, first), source.PostAsJsonAsync(Events, second)), r => r.EnsureSuccessStatusCode());
        var failed = await Signed(id, e => e with { Status = "failed" }); (await source.PostAsJsonAsync(Events, failed)).EnsureSuccessStatusCode();
        Assert.Equal(1, await Count<PaymentCollection>(p => p.PaymentRequestId == id)); Assert.Equal(1, await Count<Receipt>(r => r.Collection.PaymentRequestId == id));
        await using var scope = factory.Services.CreateAsyncScope(); Assert.Equal(PaymentRequestStatus.Confirmed, (await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().PaymentRequests.SingleAsync(p => p.Id == id)).Status);
    }
}
