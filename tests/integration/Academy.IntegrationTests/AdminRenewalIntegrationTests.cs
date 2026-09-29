using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class AdminRenewalIntegrationTests : IAsyncLifetime
{
    private const string Prefix = "slice8d-";
    private readonly string connection = Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")
        ?? throw new InvalidOperationException("ACADEMY_TEST_CONNECTION_STRING is required.");
    private Factory factory = null!;
    private readonly List<(Guid RenewalId, RenewalRequestStatus RenewalStatus, Guid PaymentId, PaymentRequestStatus PaymentStatus)> suspended = [];

    public async Task InitializeAsync() { factory = new Factory(connection); _ = factory.CreateClient(); await Cleanup(); await SuspendExistingOpenRenewals(); }
    public async Task DisposeAsync() { await Cleanup(); await RestoreExistingOpenRenewals(); await factory.DisposeAsync(); }

    [Fact] public async Task Owner_can_list_renewable_enrollments() { using var c = await Staff(DemoSeed.OwnerEmail); Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/subscriptions/admin-renewals/enrollments")).StatusCode); }
    [Fact] public async Task Admin_can_list_renewable_enrollments() { using var c = await Staff(DemoSeed.AdminEmail); Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/subscriptions/admin-renewals/enrollments")).StatusCode); }
    [Fact] public async Task Search_by_player_name_finds_enrollment() { var rows = await Search("مريم"); Assert.Contains(rows.EnumerateArray(), x => x.GetProperty("player").GetString()!.Contains("مريم")); }
    [Fact] public async Task Search_by_player_code_finds_all_sport_enrollments() { var rows = await Search("NG-0002"); var items = rows.EnumerateArray().ToArray(); Assert.NotEmpty(items); Assert.All(items, x => Assert.Equal("NG-0002", x.GetProperty("playerCode").GetString())); }
    [Fact] public async Task Search_by_guardian_phone_finds_linked_children() { var rows = await Search(DemoSeed.GuardianPhone); Assert.NotEmpty(rows.EnumerateArray()); }
    [Fact] public async Task Enrollment_details_are_available_to_owner() { using var c = await Staff(DemoSeed.OwnerEmail); Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/v1/subscriptions/admin-renewals/enrollments/{await Enrollment()}")).StatusCode); }
    [Fact] public async Task Enrollment_details_return_only_same_sport_plans() { var details = await Details(); Assert.DoesNotContain(details.GetProperty("plans").EnumerateArray(), x => x.GetProperty("id").GetGuid() == Slice3DemoSeed.SwimmingMonthlyId); }
    [Fact] public async Task Enrollment_details_include_current_period_context() { var details = await Details(); Assert.True(details.TryGetProperty("current", out _)); }
    [Fact]
    public async Task Expired_enrollment_preview_starts_on_demo_reference_date()
    {
        await using var scope = Scope(); var db = Db(scope); var enrollment = await Enrollment();
        var periods = await db.SubscriptionPeriods.Where(x => x.SportEnrollmentId == enrollment && x.Status != SubscriptionPeriodStatus.Cancelled).ToListAsync();
        var states = periods.Select(x => (x, x.Status)).ToArray();
        foreach (var (period, _) in states) period.Status = SubscriptionPeriodStatus.Cancelled;
        await db.SaveChangesAsync();
        try { var details = await Details(); Assert.Equal("2026-09-28", details.GetProperty("previewStart").GetString()); }
        finally { foreach (var (period, state) in states) period.Status = state; await db.SaveChangesAsync(); }
    }
    [Fact] public async Task Owner_can_create_admin_renewal() { using var c = await Staff(DemoSeed.OwnerEmail); Assert.Equal(HttpStatusCode.Created, (await Create(c)).StatusCode); }
    [Fact] public async Task Admin_can_create_admin_renewal() { using var c = await Staff(DemoSeed.AdminEmail); Assert.Equal(HttpStatusCode.Created, (await Create(c)).StatusCode); }
    [Fact] public async Task Idempotency_key_is_required() { using var c = await Staff(DemoSeed.OwnerEmail); var r = await Post(c, "/api/v1/subscriptions/admin-renewals", Body(), null); Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); }
    [Fact] public async Task Same_idempotency_key_replays_same_result() { using var c = await Staff(DemoSeed.OwnerEmail); var key = Key(); var first = await Create(c, key); var second = await Create(c, key); Assert.Equal((await Json(first)).GetProperty("paymentId").GetGuid(), (await Json(second)).GetProperty("paymentId").GetGuid()); }
    [Fact] public async Task Second_open_renewal_is_rejected() { using var c = await Staff(DemoSeed.OwnerEmail); (await Create(c)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Conflict, (await Create(c)).StatusCode); }
    [Fact] public async Task Concurrent_open_renewals_create_only_one_request() { using var first = await Staff(DemoSeed.OwnerEmail); using var second = await Staff(DemoSeed.AdminEmail); var responses = await Task.WhenAll(Create(first), Create(second)); Assert.Single(responses, x => x.IsSuccessStatusCode); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict); }
    [Fact] public async Task Creation_persists_renewal_request() { var created = await Created(); Assert.Equal(1, await Count<RenewalRequest>(x => x.Id == created.RenewalId)); }
    [Fact] public async Task Creation_persists_payment_request() { var created = await Created(); Assert.Equal(1, await Count<PaymentRequest>(x => x.Id == created.PaymentId)); }
    [Fact] public async Task Creation_does_not_create_collection() { var created = await Created(); Assert.Equal(0, await Count<PaymentCollection>(x => x.PaymentRequestId == created.PaymentId)); }
    [Fact] public async Task Creation_does_not_create_subscription_period() { var created = await Created(); Assert.Equal(0, await Count<SubscriptionPeriod>(x => x.Collection.PaymentRequestId == created.PaymentId)); }
    [Fact] public async Task Server_uses_plan_amount_and_currency() { var created = await Created(); await using var scope = Scope(); var p = await Db(scope).PaymentRequests.SingleAsync(x => x.Id == created.PaymentId); Assert.Equal((900m, "EGP"), (p.Amount, p.Currency)); }
    [Fact] public async Task Renewal_records_staff_actor() { var created = await Created(); await using var scope = Scope(); var renewal = await Db(scope).RenewalRequests.SingleAsync(x => x.Id == created.RenewalId); var owner = await Db(scope).Users.Where(x => x.NormalizedEmail == DemoSeed.OwnerEmail.ToUpperInvariant()).Select(x => x.Id).SingleAsync(); Assert.Equal(owner, renewal.RequestedByUserId); }
    [Fact] public async Task Demo_success_confirms_payment() { var created = await Created(); await Simulate(created.PaymentId, "Success"); await using var scope = Scope(); Assert.Equal(PaymentRequestStatus.Confirmed, await Db(scope).PaymentRequests.Where(x => x.Id == created.PaymentId).Select(x => x.Status).SingleAsync()); }
    [Fact] public async Task Demo_success_creates_one_collection() { var created = await Created(); await Simulate(created.PaymentId, "Success"); Assert.Equal(1, await Count<PaymentCollection>(x => x.PaymentRequestId == created.PaymentId)); }
    [Fact] public async Task Demo_success_creates_one_receipt() { var created = await Created(); await Simulate(created.PaymentId, "Success"); Assert.Equal(1, await Count<Receipt>(x => x.Collection.PaymentRequestId == created.PaymentId)); }
    [Fact] public async Task Demo_success_creates_one_period() { var created = await Created(); await Simulate(created.PaymentId, "Success"); Assert.Equal(1, await Count<SubscriptionPeriod>(x => x.Collection.PaymentRequestId == created.PaymentId)); }
    [Fact] public async Task Demo_failure_creates_no_collection() { var created = await Created(); await Simulate(created.PaymentId, "Failed"); Assert.Equal(0, await Count<PaymentCollection>(x => x.PaymentRequestId == created.PaymentId)); }
    [Fact] public async Task Coach_cannot_list_admin_renewals() { using var c = await Staff(DemoSeed.CoachEmail); Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/v1/subscriptions/admin-renewals/enrollments")).StatusCode); }
    [Fact] public async Task Coach_cannot_create_admin_renewal() { using var c = await Staff(DemoSeed.CoachEmail); Assert.Equal(HttpStatusCode.Forbidden, (await Create(c)).StatusCode); }
    [Fact] public async Task Guardian_cannot_list_admin_renewals() { using var c = await Guardian(); Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/v1/subscriptions/admin-renewals/enrollments")).StatusCode); }
    [Fact] public async Task Guardian_cannot_create_admin_renewal() { using var c = await Guardian(); Assert.Equal(HttpStatusCode.Forbidden, (await Create(c)).StatusCode); }
    [Fact] public async Task Cross_tenant_enrollment_is_not_found() { using var c = await Staff(DemoSeed.OwnerEmail); await using var scope = Scope(); var id = await Db(scope).SportEnrollments.Where(x => x.AcademyId == DemoSeed.FutureAcademyId).Select(x => x.Id).FirstAsync(); Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/subscriptions/admin-renewals/enrollments/{id}")).StatusCode); }
    [Fact] public async Task Cross_tenant_payment_is_not_found() { using var c = await Staff(DemoSeed.OwnerEmail); await using var scope = Scope(); var id = await Db(scope).PaymentRequests.Where(x => x.AcademyId == DemoSeed.FutureAcademyId).Select(x => x.Id).FirstOrDefaultAsync(); if (id == Guid.Empty) id = Guid.NewGuid(); Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/subscriptions/payments/{id}")).StatusCode); }
    [Fact] public async Task Wrong_sport_plan_is_not_found() { using var c = await Staff(DemoSeed.OwnerEmail); var r = await Post(c, "/api/v1/subscriptions/admin-renewals", new { sportEnrollmentId = await Enrollment(), subscriptionPlanId = Slice3DemoSeed.SwimmingMonthlyId }, Key()); Assert.Equal(HttpStatusCode.NotFound, r.StatusCode); }
    [Fact] public async Task Payment_detail_is_tenant_scoped_and_readable() { var created = await Created(); using var c = await Staff(DemoSeed.AdminEmail); Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/v1/subscriptions/payments/{created.PaymentId}")).StatusCode); }
    [Fact] public async Task Pending_payment_cannot_be_retried() { var created = await Created(); using var c = await Staff(DemoSeed.OwnerEmail); Assert.Equal(HttpStatusCode.Conflict, (await Post(c, $"/api/v1/subscriptions/payments/{created.PaymentId}/retry", new { }, Key())).StatusCode); }
    [Fact] public async Task Failed_payment_can_be_retried() { var created = await Created(); await Simulate(created.PaymentId, "Failed"); using var c = await Staff(DemoSeed.OwnerEmail); Assert.Equal(HttpStatusCode.Created, (await Post(c, $"/api/v1/subscriptions/payments/{created.PaymentId}/retry", new { }, Key())).StatusCode); }
    [Fact] public async Task Player_details_expose_renewable_enrollment_context() { using var c = await Staff(DemoSeed.OwnerEmail); var json = await Json(await c.GetAsync($"/api/v1/people/players/{Slice2DemoSeed.MariamPlayerId}")); Assert.NotEmpty(json.GetProperty("enrollments").EnumerateArray()); }

    private object Body() => new { sportEnrollmentId = Enrollment().GetAwaiter().GetResult(), subscriptionPlanId = Slice3DemoSeed.FootballMonthlyId };
    private async Task<Guid> Enrollment() { await using var scope = Scope(); return await Db(scope).SportEnrollments.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.PlayerId == Slice2DemoSeed.OtherPlayerId && x.SportId == Slice2DemoSeed.FootballId).Select(x => x.Id).SingleAsync(); }
    private async Task<JsonElement> Search(string value) { using var c = await Staff(DemoSeed.OwnerEmail); return await Json(await c.GetAsync($"/api/v1/subscriptions/admin-renewals/enrollments?search={Uri.EscapeDataString(value)}")); }
    private async Task<JsonElement> Details() { using var c = await Staff(DemoSeed.OwnerEmail); return await Json(await c.GetAsync($"/api/v1/subscriptions/admin-renewals/enrollments/{await Enrollment()}")); }
    private async Task<CreatedDto> Created() { using var c = await Staff(DemoSeed.OwnerEmail); var response = await Create(c); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<CreatedDto>())!; }
    private Task<HttpResponseMessage> Create(HttpClient c, string? key = null) => Post(c, "/api/v1/subscriptions/admin-renewals", Body(), key ?? Key());
    private async Task Simulate(Guid paymentId, string outcome) { using var c = await Staff(DemoSeed.OwnerEmail); (await Post(c, $"/api/v1/subscriptions/payments/{paymentId}/simulate", new { outcome }, null)).EnsureSuccessStatusCode(); }
    private async Task<HttpClient> Staff(string email) { var c = factory.CreateClient(); (await Post(c, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" }, null)).EnsureSuccessStatusCode(); return c; }
    private async Task<HttpClient> Guardian() { var c = factory.CreateClient(); var request = await Post(c, "/api/v1/auth/guardian/otp/request", new { phoneNumber = DemoSeed.GuardianPhone }, null); var challenge = (await request.Content.ReadFromJsonAsync<Challenge>())!; (await Post(c, "/api/v1/auth/guardian/otp/verify", new { challengeId = challenge.ChallengeId, phoneNumber = DemoSeed.GuardianPhone, code = "246810" }, null)).EnsureSuccessStatusCode(); return c; }
    private static string Key() => $"{Prefix}{Guid.NewGuid():N}";
    private static async Task<JsonElement> Json(HttpResponseMessage response) { response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<JsonElement>()); }
    private async Task<HttpResponseMessage> Post(HttpClient c, string path, object body, string? key) { var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", (await c.GetFromJsonAsync<Csrf>("/api/v1/auth/csrf"))!.Token); if (key is not null) request.Headers.Add("Idempotency-Key", key); return await c.SendAsync(request); }
    private AsyncServiceScope Scope() => factory.Services.CreateAsyncScope();
    private static FoundationDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
    private async Task<int> Count<T>(System.Linq.Expressions.Expression<Func<T, bool>> predicate) where T : class { await using var scope = Scope(); return await Db(scope).Set<T>().CountAsync(predicate); }
    private async Task Cleanup()
    {
        if (factory is null) return;
        await using var scope = Scope(); var db = Db(scope);
        var renewals = await db.RenewalRequests.Where(x => x.IdempotencyKey.StartsWith(Prefix)).Select(x => x.Id).ToArrayAsync();
        if (renewals.Length == 0) return;
        var payments = await db.PaymentRequests.Where(x => renewals.Contains(x.RenewalRequestId)).Select(x => x.Id).ToArrayAsync();
        var collections = await db.Collections.Where(x => renewals.Contains(x.RenewalRequestId)).Select(x => x.Id).ToArrayAsync();
        await db.SubscriptionPeriods.Where(x => collections.Contains(x.CollectionId)).ExecuteDeleteAsync();
        await db.Receipts.Where(x => collections.Contains(x.CollectionId)).ExecuteDeleteAsync();
        await db.Collections.Where(x => collections.Contains(x.Id)).ExecuteDeleteAsync();
        await db.PaymentProviderEvents.Where(x => payments.Contains(x.PaymentRequestId)).ExecuteDeleteAsync();
        await db.RenewalDiscountAdjustments.Where(x => renewals.Contains(x.RenewalRequestId)).ExecuteDeleteAsync();
        await db.RenewalRequests.Where(x => renewals.Contains(x.Id)).ExecuteUpdateAsync(x => x.SetProperty(y => y.PaymentRequestId, (Guid?)null));
        await db.PaymentRequests.Where(x => payments.Contains(x.Id)).ExecuteDeleteAsync();
        await db.RenewalRequests.Where(x => renewals.Contains(x.Id)).ExecuteDeleteAsync();
    }

    private async Task SuspendExistingOpenRenewals()
    {
        await using var scope = Scope(); var db = Db(scope); var enrollment = await Enrollment();
        var rows = await db.RenewalRequests.Include(x => x.SubscriptionPlan)
            .Where(x => x.SportEnrollmentId == enrollment && !x.IdempotencyKey.StartsWith(Prefix) && x.PaymentRequestId != null &&
                (x.Status == RenewalRequestStatus.PendingPayment || x.Status == RenewalRequestStatus.PaymentInProgress))
            .ToListAsync();
        foreach (var renewal in rows)
        {
            var payment = await db.PaymentRequests.SingleAsync(x => x.Id == renewal.PaymentRequestId);
            suspended.Add((renewal.Id, renewal.Status, payment.Id, payment.Status));
            renewal.Status = RenewalRequestStatus.Failed; payment.Status = PaymentRequestStatus.Failed;
        }
        await db.SaveChangesAsync();
    }

    private async Task RestoreExistingOpenRenewals()
    {
        if (suspended.Count == 0) return;
        await using var scope = Scope(); var db = Db(scope);
        foreach (var state in suspended)
        {
            var renewal = await db.RenewalRequests.SingleAsync(x => x.Id == state.RenewalId);
            var payment = await db.PaymentRequests.SingleAsync(x => x.Id == state.PaymentId);
            renewal.Status = state.RenewalStatus; payment.Status = state.PaymentStatus;
        }
        await db.SaveChangesAsync();
    }

    private sealed record Csrf(string Token);
    private sealed record Challenge(Guid ChallengeId);
    private sealed record CreatedDto(Guid RenewalId, Guid PaymentId);
    private sealed class Factory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Demo"); builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Demo:SeedEnabled", "true"); builder.UseSetting("Demo:FixedOtpEnabled", "true");
            builder.UseSetting("Demo:FixedOtp", "246810"); builder.UseSetting("Demo:StaffPassword", "Demo-Only-123!");
            builder.UseSetting("Demo:ReferenceDate", "2026-09-28"); builder.UseSetting("Payments:InternalTest:Enabled", "true");
            builder.UseSetting("Payments:InternalTest:SigningKey", "Demo-Test-Signing-Key-Only-123456");
        }
    }
}
