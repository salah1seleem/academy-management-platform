using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class ExternalRenewalTests : IAsyncLifetime
{
    private readonly string connection = Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("ACADEMY_TEST_CONNECTION_STRING is required.");
    private Factory factory = null!;

    public Task InitializeAsync() { factory = new Factory(connection); _ = factory.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task Payer_can_resolve_valid_same_academy_reference()
    {
        using var client = await GuardianClient();
        var response = await Resolve(client, Slice3DemoSeed.ExternalRenewalCode);
        response.EnsureSuccessStatusCode();
        var value = await response.Content.ReadFromJsonAsync<Resolved>();
        Assert.Equal("عمر أحمد حسن", value!.PlayerDisplayName);
        Assert.Equal("كرة القدم", value.Sport);
        Assert.NotEmpty(value.Plans);
    }

    [Fact]
    public async Task Resolution_exposes_only_minimal_beneficiary_data()
    {
        using var client = await GuardianClient();
        var response = await Resolve(client, Slice3DemoSeed.ExternalRenewalCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = json.RootElement.EnumerateObject().Select(x => x.Name).Order().ToArray();
        Assert.Equal(new[] { "academyName", "plans", "playerDisplayName", "sport" }, names);
        var body = json.RootElement.GetRawText().ToLowerInvariant();
        foreach (var forbidden in new[] { "playerid", "enrollmentid", "dateofbirth", "phone", "guardian", "medical", "attendance", "evaluation", "gallery" }) Assert.DoesNotContain(forbidden, body);
    }

    [Fact]
    public async Task Invalid_reference_returns_safe_failure()
    {
        using var client = await GuardianClient();
        var response = await Resolve(client, "RNW-NOT-A-REAL-CODE");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("player", (await response.Content.ReadAsStringAsync()).ToLowerInvariant());
    }

    [Fact]
    public async Task Academy_A_reference_cannot_be_used_from_Academy_B()
    {
        using var client = await GuardianClient("+201000000003");
        Assert.Equal(HttpStatusCode.NotFound, (await Resolve(client, Slice3DemoSeed.ExternalRenewalCode)).StatusCode);
    }

    [Fact]
    public async Task Guardian_can_search_same_academy_unlinked_player_by_name_and_select_minimal_candidate()
    {
        using var client = await GuardianClient();
        var search = await client.GetAsync($"/api/v1/guardian/subscriptions/external/search?query={Uri.EscapeDataString("عمر")}");
        search.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
        var item = json.RootElement.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("playerDisplayName").GetString() == "عمر أحمد حسن");
        Assert.DoesNotContain("phone", item.GetRawText().ToLowerInvariant());
        Assert.DoesNotContain("dateofbirth", item.GetRawText().ToLowerInvariant());

        var selected = await Post(client, "/api/v1/guardian/subscriptions/external/select", new { candidateId = item.GetProperty("candidateId").GetGuid() }, await Csrf(client));
        selected.EnsureSuccessStatusCode();
        var resolved = await selected.Content.ReadFromJsonAsync<Selected>();
        Assert.Equal("عمر أحمد حسن", resolved!.PlayerDisplayName);
        Assert.StartsWith("RNW-", resolved.Reference);
        Assert.NotEmpty(resolved.Plans);
    }

    [Fact]
    public async Task Guardian_name_search_excludes_linked_children_and_other_academies()
    {
        using var client = await GuardianClient();
        var own = await client.GetFromJsonAsync<SearchResults>($"/api/v1/guardian/subscriptions/external/search?query={Uri.EscapeDataString("آدم")}");
        Assert.Empty(own!.Items);
        var sameNameAcrossTenants = await client.GetFromJsonAsync<SearchResults>($"/api/v1/guardian/subscriptions/external/search?query={Uri.EscapeDataString("عمر")}");
        Assert.Single(sameNameAcrossTenants!.Items);
        Assert.Equal("عمر أحمد حسن", sameNameAcrossTenants.Items[0].PlayerDisplayName);
    }

    [Fact]
    public async Task External_name_search_requires_meaningful_query()
    {
        using var client = await GuardianClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/guardian/subscriptions/external/search?query=ع")).StatusCode);
    }

    [Fact]
    public async Task Payer_cannot_access_beneficiary_player_after_payment()
    {
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        await Simulate(client, payment, "Success");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/guardian/children/{Slice2DemoSeed.OtherPlayerId}")).StatusCode);
    }

    [Fact]
    public async Task Payment_success_renews_beneficiary_enrollment()
    {
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        await Simulate(client, payment, "Success");
        var enrollment = await BeneficiaryEnrollment();
        Assert.Equal(1, await Count<SubscriptionPeriod>(x => x.SportEnrollmentId == enrollment && x.Collection.PaymentRequestId == payment));
    }

    [Fact]
    public async Task Payer_can_access_receipt_for_external_renewal()
    {
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        await Simulate(client, payment, "Success");
        var state = await client.GetFromJsonAsync<PaymentState>($"/api/v1/guardian/subscriptions/payments/{payment}");
        var receipt = await client.GetFromJsonAsync<ReceiptView>($"/api/v1/guardian/subscriptions/receipts/{state!.ReceiptId}");
        Assert.Equal("عمر أحمد حسن", receipt!.PlayerNameSnapshot);
        Assert.Equal(900m, receipt.Amount);
        Assert.Equal("EGP", receipt.Currency);
        Assert.False(string.IsNullOrWhiteSpace(receipt.ProviderReference));
    }

    [Fact]
    public async Task Payment_creates_no_guardian_player_link()
    {
        var before = await Count<GuardianPlayerLink>(x => x.GuardianId == Slice2DemoSeed.MainGuardianId && x.PlayerId == Slice2DemoSeed.OtherPlayerId);
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        await Simulate(client, payment, "Success");
        Assert.Equal(before, await Count<GuardianPlayerLink>(x => x.GuardianId == Slice2DemoSeed.MainGuardianId && x.PlayerId == Slice2DemoSeed.OtherPlayerId));
    }

    [Fact]
    public async Task Failed_payment_does_not_renew_beneficiary()
    {
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        await Simulate(client, payment, "Failed");
        Assert.Equal(0, await Count<PaymentCollection>(x => x.PaymentRequestId == payment));
        Assert.Equal(0, await Count<SubscriptionPeriod>(x => x.Collection.PaymentRequestId == payment));
    }

    [Fact]
    public async Task Retry_after_failure_creates_new_payment_request()
    {
        using var client = await GuardianClient();
        var oldPayment = await NewExternalPayment(client);
        await Simulate(client, oldPayment, "Failed");
        var response = await Post(client, $"/api/v1/guardian/subscriptions/payments/{oldPayment}/retry", new { }, await Csrf(client), Guid.NewGuid().ToString());
        response.EnsureSuccessStatusCode();
        var fresh = (await response.Content.ReadFromJsonAsync<Created>())!.PaymentId;
        Assert.NotEqual(oldPayment, fresh);
        Assert.Equal(PaymentRequestStatus.Failed, await PaymentStatus(oldPayment));
        Assert.Equal(PaymentRequestStatus.Pending, await PaymentStatus(fresh));
    }

    [Fact]
    public async Task Late_success_for_failed_payment_has_no_financial_effect()
    {
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        var lateSuccess = await SignedSuccess(payment);
        await Simulate(client, payment, "Failed");
        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/payments/internal-test/events", lateSuccess)).EnsureSuccessStatusCode();
        Assert.Equal(PaymentRequestStatus.Failed, await PaymentStatus(payment));
        Assert.Equal(0, await Count<PaymentCollection>(x => x.PaymentRequestId == payment));
    }

    [Fact]
    public async Task Late_success_for_cancelled_payment_has_no_financial_effect()
    {
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        var lateSuccess = await SignedSuccess(payment);
        await Simulate(client, payment, "Cancelled");
        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/payments/internal-test/events", lateSuccess)).EnsureSuccessStatusCode();
        Assert.Equal(PaymentRequestStatus.Cancelled, await PaymentStatus(payment));
        Assert.Equal(0, await Count<PaymentCollection>(x => x.PaymentRequestId == payment));
    }

    [Fact]
    public async Task Duplicate_success_callback_is_idempotent()
    {
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        var callback = await SignedSuccess(payment);
        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/payments/internal-test/events", callback)).EnsureSuccessStatusCode();
        (await anonymous.PostAsJsonAsync("/api/v1/payments/internal-test/events", callback)).EnsureSuccessStatusCode();
        Assert.Equal(1, await Count<PaymentProviderEvent>(x => x.ProviderEventId == callback.ProviderEventId));
        Assert.Equal(1, await Count<PaymentCollection>(x => x.PaymentRequestId == payment));
    }

    [Fact]
    public async Task Double_click_external_renewal_start_is_idempotent()
    {
        using var client = await GuardianClient();
        var key = Guid.NewGuid().ToString();
        var first = await StartExternal(client, key);
        var second = await StartExternal(client, key);
        var firstValue = (await first.Content.ReadFromJsonAsync<Created>())!;
        var secondValue = (await second.Content.ReadFromJsonAsync<Created>())!;
        Assert.Equal(firstValue.PaymentId, secondValue.PaymentId);
        Assert.Equal(1, await Count<RenewalRequest>(x => x.IdempotencyKey == key));
    }

    [Fact]
    public async Task Success_replay_creates_no_duplicate_collection_receipt_or_period()
    {
        using var client = await GuardianClient();
        var payment = await NewExternalPayment(client);
        await Simulate(client, payment, "Success");
        await Simulate(client, payment, "Success");
        Assert.Equal(1, await Count<PaymentCollection>(x => x.PaymentRequestId == payment));
        Assert.Equal(1, await Count<Receipt>(x => x.Collection.PaymentRequestId == payment));
        Assert.Equal(1, await Count<SubscriptionPeriod>(x => x.Collection.PaymentRequestId == payment));
    }

    [Fact]
    public async Task Client_cannot_override_amount_currency_or_plan_scope()
    {
        using var client = await GuardianClient();
        var key = Guid.NewGuid().ToString();
        var response = await Post(client, "/api/v1/guardian/subscriptions/external/renewals", new { reference = Slice3DemoSeed.ExternalRenewalCode, subscriptionPlanId = Slice3DemoSeed.FootballMonthlyId, amount = .01m, currency = "USD" }, await Csrf(client), key);
        response.EnsureSuccessStatusCode();
        var paymentId = (await response.Content.ReadFromJsonAsync<Created>())!.PaymentId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var payment = await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().PaymentRequests.SingleAsync(x => x.Id == paymentId);
            Assert.Equal(900m, payment.Amount);
            Assert.Equal("EGP", payment.Currency);
        }
        var wrongPlan = await Post(client, "/api/v1/guardian/subscriptions/external/renewals", new { reference = Slice3DemoSeed.ExternalRenewalCode, subscriptionPlanId = Slice3DemoSeed.SwimmingMonthlyId }, await Csrf(client), Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, wrongPlan.StatusCode);
    }

    [Fact]
    public async Task Cross_tenant_reference_and_id_tampering_are_rejected()
    {
        using var academyA = await GuardianClient();
        Assert.Equal(HttpStatusCode.NotFound, (await Resolve(academyA, Slice3DemoSeed.FutureAcademyRenewalCode)).StatusCode);
        var bEnrollment = await Enrollment(Slice2DemoSeed.AcademyBPlayerId);
        var tampered = await Post(academyA, "/api/v1/guardian/subscriptions/renewals", new { sportEnrollmentId = bEnrollment, subscriptionPlanId = Slice3DemoSeed.FootballMonthlyId }, await Csrf(academyA), Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, tampered.StatusCode);
    }

    private async Task<Guid> NewExternalPayment(HttpClient client)
    {
        var response = await StartExternal(client, Guid.NewGuid().ToString());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Created>())!.PaymentId;
    }

    private async Task<HttpResponseMessage> StartExternal(HttpClient client, string key) => await Post(client, "/api/v1/guardian/subscriptions/external/renewals", new { reference = Slice3DemoSeed.ExternalRenewalCode, subscriptionPlanId = Slice3DemoSeed.FootballMonthlyId }, await Csrf(client), key);
    private async Task<HttpResponseMessage> Resolve(HttpClient client, string reference) => await Post(client, "/api/v1/guardian/subscriptions/external/resolve", new { reference }, await Csrf(client));
    private async Task Simulate(HttpClient client, Guid payment, string outcome) => (await Post(client, $"/api/v1/guardian/subscriptions/payments/{payment}/simulate", new { outcome }, await Csrf(client))).EnsureSuccessStatusCode();
    private async Task<HttpClient> GuardianClient(string phone = DemoSeed.GuardianPhone) { var client = factory.CreateClient(); var request = await Post(client, "/api/v1/auth/guardian/otp/request", new { phoneNumber = phone }, await Csrf(client)); var challenge = await request.Content.ReadFromJsonAsync<Challenge>(); (await Post(client, "/api/v1/auth/guardian/otp/verify", new { challengeId = challenge!.ChallengeId, phoneNumber = phone, code = "246810" }, await Csrf(client))).EnsureSuccessStatusCode(); return client; }
    private async Task<ProviderEventEnvelope> SignedSuccess(Guid payment) { await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); return scope.ServiceProvider.GetRequiredService<IPaymentGateway>().CreateTestEvent(await db.PaymentRequests.SingleAsync(x => x.Id == payment), PaymentEventOutcome.Success); }
    private async Task<Guid> BeneficiaryEnrollment() => await Enrollment(Slice2DemoSeed.OtherPlayerId);
    private async Task<Guid> Enrollment(Guid player) { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().SportEnrollments.Where(x => x.PlayerId == player).Select(x => x.Id).SingleAsync(); }
    private async Task<PaymentRequestStatus> PaymentStatus(Guid payment) { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().PaymentRequests.Where(x => x.Id == payment).Select(x => x.Status).SingleAsync(); }
    private async Task<int> Count<T>(System.Linq.Expressions.Expression<Func<T, bool>> predicate) where T : class { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Set<T>().CountAsync(predicate); }
    private static async Task<string> Csrf(HttpClient client) => (await client.GetFromJsonAsync<CsrfDto>("/api/v1/auth/csrf"))!.Token;
    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object body, string token, string? key = null) { var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", token); if (key is not null) request.Headers.Add("Idempotency-Key", key); return client.SendAsync(request); }

    private sealed record CsrfDto(string Token);
    private sealed record Challenge(Guid ChallengeId);
    private sealed record Created(Guid PaymentId);
    private sealed record Resolved(string PlayerDisplayName, string Sport, string AcademyName, List<PlanView> Plans);
    private sealed record Selected(string Reference, string PlayerDisplayName, string Sport, string AcademyName, List<PlanView> Plans);
    private sealed record SearchResults(List<SearchCandidate> Items);
    private sealed record SearchCandidate(Guid CandidateId, string PlayerDisplayName, string Sport, string Branch, string Group);
    private sealed record PlanView(Guid Id, string ArabicName, decimal Price, string Currency);
    private sealed record PaymentState(string Status, Guid? ReceiptId);
    private sealed record ReceiptView(string PlayerNameSnapshot, decimal Amount, string Currency, string ProviderReference);
    private sealed class Factory(string connection) : WebApplicationFactory<Program> { protected override void ConfigureWebHost(IWebHostBuilder builder) { builder.UseEnvironment("Demo"); builder.UseSetting("ConnectionStrings:Default", connection); builder.UseSetting("Demo:SeedProfile", "LegacyRegression"); builder.UseSetting("Demo:SeedEnabled", "true"); builder.UseSetting("Demo:FixedOtpEnabled", "true"); builder.UseSetting("Demo:FixedOtp", "246810"); builder.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); builder.UseSetting("Demo:ReferenceDate", "2026-09-28"); builder.UseSetting("Payments:InternalTest:Enabled", "true"); builder.UseSetting("Payments:InternalTest:SigningKey", "Demo-Test-Signing-Key-Only-123456"); } }
}
