using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Operations;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2ReceivablesTests
{
    private async Task<Guid> PendingCashRequest(HttpClient owner, Guid enrollment)
    {
        var response = await Post(owner, "/api/v1/subscriptions/admin-renewals", new { sportEnrollmentId = enrollment, subscriptionPlanId = Slice3DemoSeed.FootballMonthlyId });
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid();
    }
    private Task<HttpResponseMessage> PendingCash(HttpClient client, Guid payment, Guid? session, string? amount) =>
        Post(client, $"/api/v1/manage/payments/{payment}/collect", new { paymentMethod = "Cash", cashSessionId = session, expectedAmount = amount });
    private async Task<string> PendingCashAmount(HttpClient owner, Guid payment) =>
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/payments/{payment}/cash-options")).GetProperty("amount").GetString()!;

    [Fact]
    public async Task Pending_cash_preserves_issued_price_posts_once_and_replays_after_close()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var f = await CashCollectionFixture(owner); var payment = await PendingCashRequest(owner, f.Enrollment);
        var options = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/payments/{payment}/cash-options");
        Assert.Contains(options.GetProperty("sessions").EnumerateArray(), s => s.GetProperty("id").GetGuid() == f.Session);
        var amount = options.GetProperty("amount").GetString()!;
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var plan = await db.SubscriptionPlans.SingleAsync(p => p.Id == Slice3DemoSeed.FootballMonthlyId); var old = plan.Price;
        try
        {
            plan.Price += 100; await db.SaveChangesAsync();
            var responses = await Task.WhenAll(PendingCash(owner, payment, f.Session, amount), PendingCash(owner, payment, f.Session, amount));
            Assert.All(responses, r => r.EnsureSuccessStatusCode());
            var receipt = (await responses[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptId").GetGuid();
            Assert.Equal(receipt, (await responses[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptId").GetGuid());
            var posting = Assert.Single(await db.CashReceiptPostings.Where(p => p.SessionId == f.Session).ToListAsync());
            Assert.Equal(receipt, posting.ReceiptId); Assert.Equal(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), posting.Amount);
            Assert.Single(await db.Collections.Where(c => c.PaymentRequestId == payment).ToListAsync());
            Assert.Single(await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == f.Enrollment).ToListAsync());
            var detail = await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{f.Session}");
            (await Post(owner, $"{CashRoot}/sessions/{f.Session}/close", new { countedAmount = detail.GetProperty("balance").GetString() })).EnsureSuccessStatusCode();
            (await PendingCash(owner, payment, f.Session, amount)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Conflict, (await PendingCash(owner, payment, f.Session, "1.00")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await PendingCash(owner, payment, Guid.NewGuid(), amount)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, $"/api/v1/manage/payments/{payment}/collect", new { paymentMethod = "Online" })).StatusCode);
            using var guardian = await Guardian(); Assert.Equal(HttpStatusCode.OK, (await guardian.GetAsync($"/api/v1/guardian/subscriptions/receipts/{receipt}")).StatusCode);
        }
        finally { plan.Price = old; await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task Pending_cash_never_converts_a_gateway_request_or_failed_payment()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var f = await CashCollectionFixture(owner); var id = await PendingCashRequest(owner, f.Enrollment);
        var amount = await PendingCashAmount(owner, id);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var payment = await db.PaymentRequests.SingleAsync(p => p.Id == id);
        payment.Provider = "UnverifiedProvider"; payment.ProviderEnvironment = "Live"; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await PendingCash(owner, id, f.Session, amount)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.GetAsync($"/api/v1/manage/payments/{id}/cash-options")).StatusCode);
        payment.Provider = "Manual"; payment.ProviderEnvironment = "Manual"; payment.Status = Academy.Infrastructure.Subscriptions.PaymentRequestStatus.Failed; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await PendingCash(owner, id, f.Session, amount)).StatusCode);
        Assert.False(await db.Collections.AnyAsync(c => c.PaymentRequestId == id));
        Assert.False(await db.CashReceiptPostings.AnyAsync(p => p.SessionId == f.Session));
    }

    [Fact]
    public async Task Pending_cash_missing_shift_bad_or_stale_amount_has_no_financial_effect()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var f = await CashCollectionFixture(owner); var payment = await PendingCashRequest(owner, f.Enrollment);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PendingCash(owner, payment, null, "100")).StatusCode);
        foreach (var amount in new[] { "0", "-1", "0.001", "1e3", "1000000000" })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PendingCash(owner, payment, f.Session, amount)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PendingCash(owner, payment, f.Session, "0.01")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.False(await db.Collections.AnyAsync(c => c.PaymentRequestId == payment));
        Assert.False(await db.SubscriptionPeriods.AnyAsync(p => p.SportEnrollmentId == f.Enrollment));
        Assert.False(await db.CashReceiptPostings.AnyAsync(p => p.SessionId == f.Session));
    }

    [Fact]
    public async Task Pending_cash_disabled_closed_revoked_or_foreign_shift_cannot_collect()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); using var other = await Staff(DemoSeed.FutureOwnerEmail);
        var f = await CashCollectionFixture(owner); var payment = await PendingCashRequest(owner, f.Enrollment); var amount = await PendingCashAmount(owner, payment);
        (await CashAssign(owner, f.Register, f.Member, false)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await PendingCash(owner, payment, f.Session, amount)).StatusCode);
        (await CashAssign(owner, f.Register, f.Member)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await PendingCash(other, payment, f.Session, amount)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/manage/payments/{Guid.NewGuid()}/cash-options")).StatusCode);
        var settings = await CashSettings();
        try { await CashSetConfiguration(owner, settings with { CashEnabled = false }); Assert.Equal(HttpStatusCode.Conflict, (await PendingCash(owner, payment, f.Session, amount)).StatusCode); }
        finally { await CashSetConfiguration(owner, settings); }
        (await Post(owner, $"{CashRoot}/sessions/{f.Session}/close", new { countedAmount = "100" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await PendingCash(owner, payment, f.Session, amount)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); Assert.False(await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Collections.AnyAsync(c => c.PaymentRequestId == payment));
    }

    [Fact]
    public async Task Pending_cash_csrf_roles_and_explicit_administrator_grants_are_enforced()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); using var admin = await Staff(DemoSeed.AdminEmail); using var guardian = await Guardian();
        using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services);
        var f = await CashCollectionFixture(owner); var payment = await PendingCashRequest(owner, f.Enrollment); var amount = await PendingCashAmount(owner, payment);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/manage/payments/{payment}/collect", new { paymentMethod = "Cash", cashSessionId = f.Session, expectedAmount = amount })).StatusCode);
        foreach (var client in new[] { admin, guardian, coach })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await PendingCash(client, payment, f.Session, amount)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/manage/payments/{payment}/cash-options")).StatusCode);
        }
    }

    [Fact]
    public async Task Pending_cash_race_with_close_never_changes_the_closed_expected_balance()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var f = await CashCollectionFixture(owner); var payment = await PendingCashRequest(owner, f.Enrollment); var amount = await PendingCashAmount(owner, payment);
        var responses = await Task.WhenAll(PendingCash(owner, payment, f.Session, amount), Post(owner, $"{CashRoot}/sessions/{f.Session}/close", new { countedAmount = "100" }));
        responses[1].EnsureSuccessStatusCode(); Assert.True(responses[0].IsSuccessStatusCode || responses[0].StatusCode == HttpStatusCode.Conflict);
        var detail = await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{f.Session}");
        Assert.Equal(detail.GetProperty("balance").GetString(), detail.GetProperty("session").GetProperty("expectedAmount").GetString());
    }

    [Fact]
    public async Task Pending_cash_wrong_branch_and_guardian_requested_payment_are_rejected()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var f = await CashCollectionFixture(owner); var payment = await PendingCashRequest(owner, f.Enrollment); var amount = await PendingCashAmount(owner, payment);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var branch = await db.Branches.Where(b => b.AcademyId == DemoSeed.NogoomAcademyId && b.IsActive && b.Id != f.Branch).Select(b => b.Id).FirstAsync();
        var created = await Post(owner, CashRoot, new { branchId = branch, name = "صندوق فرع آخر اصطناعي " + Guid.NewGuid().ToString("N")[..8] }); created.EnsureSuccessStatusCode();
        var register = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await CashAssign(owner, register, f.Member)).EnsureSuccessStatusCode(); var shift = await CashOpen(owner, register, "0");
        var options = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/payments/{payment}/cash-options");
        Assert.DoesNotContain(options.GetProperty("sessions").EnumerateArray(), s => s.GetProperty("id").GetGuid() == shift);
        Assert.Equal(HttpStatusCode.Conflict, (await PendingCash(owner, payment, shift, amount)).StatusCode);
        var row = await db.PaymentRequests.Include(p => p.RenewalRequest).SingleAsync(p => p.Id == payment);
        row.RenewalRequest.RequestedByUserId = await db.GuardianProfiles.Where(g => g.AcademyId == DemoSeed.NogoomAcademyId && g.User.PhoneNumber == DemoSeed.GuardianPhone).Select(g => g.UserId).SingleAsync();
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await PendingCash(owner, payment, f.Session, amount)).StatusCode);
        Assert.False(await db.Collections.AnyAsync(c => c.PaymentRequestId == payment));
    }

    [Fact]
    public async Task Pending_cash_demo_seed_is_repeatable_preserves_review_and_never_collects_or_enables_cash()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var rules = scope.ServiceProvider.GetRequiredService<DiscountRules>();
        await CashPendingPaymentDemoSeed.Seed(db, rules);
        var child = await db.Players.SingleAsync(p => p.Id == CashPendingPaymentDemoSeed.Id(2)); child.ArabicName = "اسم مراجعة نقدي محفوظ — اصطناعي"; await db.SaveChangesAsync();
        var before = (await db.Players.CountAsync(), await db.PaymentRequests.CountAsync(), await db.Collections.CountAsync(), await db.CashSessions.CountAsync(), await db.AcademyConfigurationVersions.CountAsync());
        await CashPendingPaymentDemoSeed.Seed(db, rules); await db.Entry(child).ReloadAsync();
        Assert.Equal("اسم مراجعة نقدي محفوظ — اصطناعي", child.ArabicName);
        Assert.Equal(before, (await db.Players.CountAsync(), await db.PaymentRequests.CountAsync(), await db.Collections.CountAsync(), await db.CashSessions.CountAsync(), await db.AcademyConfigurationVersions.CountAsync()));
        Assert.False(await db.Collections.AnyAsync(c => c.PaymentRequestId == CashPendingPaymentDemoSeed.Id(6)));
        Assert.False(await db.SubscriptionPeriods.AnyAsync(p => p.SportEnrollmentId == CashPendingPaymentDemoSeed.Id(3)));
    }
}
