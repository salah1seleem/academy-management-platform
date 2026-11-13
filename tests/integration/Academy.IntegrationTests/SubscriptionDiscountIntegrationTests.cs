using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Api.Slice8;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class SubscriptionDiscountIntegrationTests : IAsyncLifetime
{
    private readonly string connection = Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("ACADEMY_TEST_CONNECTION_STRING is required.");
    private readonly List<Guid> renewals = [];
    private Factory factory = null!;

    private InternalGatewayRouteScope route = null!;
    public async Task InitializeAsync() { factory = new Factory(connection); _ = factory.CreateClient(); route = await InternalGatewayRouteScope.Begin(factory.Services); }
    public async Task DisposeAsync()
    {
        await using var scope = Scope(); var db = Db(scope);
        // Signed discrepancies are immutable evidence; retain their synthetic source graph too.
        var retained = await db.ProviderEventQuarantines.Where(x => renewals.Contains(x.PaymentRequest.RenewalRequestId))
            .Select(x => x.PaymentRequest.RenewalRequestId).Distinct().ToArrayAsync();
        // Close pending replacements on retained test graphs so their
        // enrollment can be used by the next scenario.
        var pendingRetainedPayments = await db.RenewalRequests.Where(x => retained.Contains(x.Id))
            .Join(db.PaymentRequests, renewal => renewal.PaymentRequestId, payment => (Guid?)payment.Id, (_, payment) => payment)
            .Where(payment => payment.Status == PaymentRequestStatus.Pending).Select(payment => payment.Id).ToArrayAsync();
        foreach (var payment in pendingRetainedPayments) await Simulate(payment, "Failed");
        var ids = renewals.Distinct().Except(retained).ToArray();
        if (ids.Length > 0)
        {
            var payments = await db.PaymentRequests.Where(x => ids.Contains(x.RenewalRequestId)).Select(x => x.Id).ToArrayAsync();
            var collections = await db.Collections.Where(x => ids.Contains(x.RenewalRequestId)).Select(x => x.Id).ToArrayAsync();
            var periods = await db.SubscriptionPeriods.Where(x => x.CollectionId.HasValue && collections.Contains(x.CollectionId.Value)).Select(x => x.Id).ToArrayAsync();
            await db.SubscriptionAdjustments.Where(x => periods.Contains(x.SubscriptionPeriodId)).ExecuteDeleteAsync();
            await db.SubscriptionPeriods.Where(x => periods.Contains(x.Id)).ExecuteDeleteAsync();
            await db.Receipts.Where(x => collections.Contains(x.CollectionId)).ExecuteDeleteAsync();
            await db.Collections.Where(x => collections.Contains(x.Id)).ExecuteDeleteAsync();
            await db.PaymentProviderEvents.Where(x => payments.Contains(x.PaymentRequestId)).ExecuteDeleteAsync();
            await db.RenewalDiscountAdjustments.Where(x => ids.Contains(x.RenewalRequestId)).ExecuteDeleteAsync();
            await db.RenewalRequests.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.PaymentRequestId, (Guid?)null));
            await db.PaymentRequests.Where(x => payments.Contains(x.Id)).ExecuteDeleteAsync();
            await db.RenewalRequests.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
        }
        await route.DisposeAsync(); await factory.DisposeAsync();
    }

    [Fact] public async Task Admin_applies_percentage_discount() { var s = await NewRenewal(); using var c = await Staff(DemoSeed.AdminEmail); Assert.Equal(HttpStatusCode.OK, (await ApplyHttp(c, s, "Percentage", 10)).StatusCode); }
    [Fact] public async Task Admin_applies_fixed_discount() { var s = await NewRenewal(); using var c = await Staff(DemoSeed.AdminEmail); Assert.Equal(HttpStatusCode.OK, (await ApplyHttp(c, s, "FixedAmount", 150)).StatusCode); }
    [Fact] public async Task Guardian_cannot_apply_discount() { var s = await NewRenewal(); using var c = await Guardian(); Assert.Equal(HttpStatusCode.Forbidden, (await ApplyHttp(c, s, "Percentage", 10)).StatusCode); }
    [Fact] public async Task Coach_cannot_apply_discount() { var s = await NewRenewal(); using var c = await Staff(DemoSeed.CoachEmail); Assert.Equal(HttpStatusCode.Forbidden, (await ApplyHttp(c, s, "Percentage", 10)).StatusCode); }
    [Fact] public async Task Academy_A_cannot_discount_Academy_B_renewal() { var s = await NewDirectRenewal(DemoSeed.FutureAcademyId); using var c = await Staff(DemoSeed.OwnerEmail); Assert.Equal(HttpStatusCode.NotFound, (await ApplyHttp(c, s, "Percentage", 10)).StatusCode); }
    [Fact] public async Task Percentage_calculation_is_correct() { var r = await Apply(await NewRenewal(), RenewalDiscountType.Percentage, 10); Assert.Equal((90m, 810m), (r.DiscountAmount, r.FinalAmount)); }
    [Fact] public async Task Fixed_calculation_is_correct() { var r = await Apply(await NewRenewal(), RenewalDiscountType.FixedAmount, 150); Assert.Equal((150m, 750m), (r.DiscountAmount, r.FinalAmount)); }
    [Fact] public async Task Percentage_over_100_is_rejected() { var s = await NewRenewal(); await Assert.ThrowsAsync<DiscountValidationException>(() => Apply(s, RenewalDiscountType.Percentage, 101)); }
    [Fact] public async Task Fixed_over_original_is_rejected() { var s = await NewRenewal(); await Assert.ThrowsAsync<DiscountValidationException>(() => Apply(s, RenewalDiscountType.FixedAmount, 901)); }
    [Fact] public async Task Negative_discount_is_rejected() { var s = await NewRenewal(); await Assert.ThrowsAsync<DiscountValidationException>(() => Apply(s, RenewalDiscountType.FixedAmount, -1)); }
    [Fact] public async Task Zero_discount_is_rejected() { var s = await NewRenewal(); await Assert.ThrowsAsync<DiscountValidationException>(() => Apply(s, RenewalDiscountType.Percentage, 0)); }
    [Fact] public async Task Reason_is_required() { var s = await NewRenewal(); await Assert.ThrowsAsync<DiscountValidationException>(() => Apply(s, RenewalDiscountType.Percentage, 10, "   ")); }
    [Fact] public async Task Final_amount_never_negative() { var s = await NewRenewal(); var r = await Apply(s, RenewalDiscountType.Percentage, 100); Assert.Equal(0m, r.FinalAmount); }
    [Fact] public async Task Client_final_amount_is_ignored() { var s = await NewRenewal(); using var c = await Staff(DemoSeed.AdminEmail); var response = await Post(c, $"/api/v1/subscriptions/renewals/{s.RenewalId}/discount", new { type = "Percentage", value = 10, reason = "اختبار", expectedVersion = s.Version, finalAmount = 1 }, Guid.NewGuid().ToString()); response.EnsureSuccessStatusCode(); Assert.Equal(810m, (await Renewal(s.RenewalId)).FinalAmount); }
    [Fact] public async Task Confirmed_payment_cannot_be_discounted_afterward() { var s = await NewRenewal(); await Simulate(s.PaymentId, "Success"); var current = await Renewal(s.RenewalId); await Assert.ThrowsAsync<DiscountConflictException>(() => Apply(s with { Version = current.Version }, RenewalDiscountType.Percentage, 10)); }
    [Fact] public async Task Plan_price_change_does_not_change_snapshot() { var s = await NewRenewal(); await using var scope = Scope(); var db = Db(scope); var plan = await db.SubscriptionPlans.SingleAsync(x => x.Id == Slice3DemoSeed.FootballMonthlyId); var old = plan.Price; plan.Price = 999m; await db.SaveChangesAsync(); try { Assert.Equal(900m, (await Renewal(s.RenewalId)).OriginalAmount); } finally { plan.Price = old; await db.SaveChangesAsync(); } }
    [Fact] public async Task Replacement_payment_uses_final_amount() { var r = await Apply(await NewRenewal(), RenewalDiscountType.Percentage, 10); await using var scope = Scope(); Assert.Equal(810m, await Db(scope).PaymentRequests.Where(x => x.Id == r.PaymentRequestId).Select(x => x.Amount).SingleAsync()); }
    [Fact] public async Task Callback_with_original_amount_is_rejected() { var s = await NewRenewal(); var r = await Apply(s, RenewalDiscountType.Percentage, 10); var envelope = await Event(r.PaymentRequestId!.Value, "succeeded", 900m); using var c = factory.CreateClient(); Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/payments/internal-test/events", envelope)).StatusCode); }
    [Fact] public async Task Discounted_success_creates_one_final_collection() { var r = await Apply(await NewRenewal(), RenewalDiscountType.Percentage, 10); await Simulate(r.PaymentRequestId!.Value, "Success"); await using var scope = Scope(); var rows = await Db(scope).Collections.Where(x => x.PaymentRequestId == r.PaymentRequestId).Select(x => x.Amount).ToListAsync(); Assert.Equal([810m], rows); }
    [Fact] public async Task Receipt_contains_original_discount_and_final() { var r = await Apply(await NewRenewal(), RenewalDiscountType.Percentage, 10); await Simulate(r.PaymentRequestId!.Value, "Success"); await using var scope = Scope(); var receipt = await Db(scope).Receipts.SingleAsync(x => x.Collection.PaymentRequestId == r.PaymentRequestId); Assert.Equal((900m, 90m, 810m, 810m), (receipt.OriginalAmount, receipt.DiscountAmount, receipt.FinalAmount, receipt.Amount)); }
    [Fact] public async Task Financial_report_total_increases_by_final_only() { using var c = await Staff(DemoSeed.OwnerEmail); var before = (await Get(c, "/api/v1/reports/financial?from=2026-01-01&to=2026-12-31")).GetProperty("totalAmount").GetDecimal(); var r = await Apply(await NewRenewal(), RenewalDiscountType.Percentage, 10); await Simulate(r.PaymentRequestId!.Value, "Success"); var after = (await Get(c, "/api/v1/reports/financial?from=2026-01-01&to=2026-12-31")).GetProperty("totalAmount").GetDecimal(); Assert.Equal(810m, after - before); }
    [Fact] public async Task Owner_dashboard_revenue_increases_by_final_only() { using var c = await Staff(DemoSeed.OwnerEmail); var before = (await Get(c, "/api/v1/reports/owner-summary?from=2026-01-01&to=2026-12-31")).GetProperty("collections").GetProperty("total").GetDecimal(); var r = await Apply(await NewRenewal(), RenewalDiscountType.FixedAmount, 150); await Simulate(r.PaymentRequestId!.Value, "Success"); var after = (await Get(c, "/api/v1/reports/owner-summary?from=2026-01-01&to=2026-12-31")).GetProperty("collections").GetProperty("total").GetDecimal(); Assert.Equal(750m, after - before); }
    [Fact] public async Task Failed_discounted_payment_creates_no_collection() { var r = await Apply(await NewRenewal(), RenewalDiscountType.Percentage, 10); await Simulate(r.PaymentRequestId!.Value, "Failed"); await using var scope = Scope(); Assert.False(await Db(scope).Collections.AnyAsync(x => x.PaymentRequestId == r.PaymentRequestId)); }
    [Fact]
    public async Task Retry_keeps_discounted_snapshot_and_amount()
    {
        var discount = await Apply(await NewRenewal(), RenewalDiscountType.Percentage, 10);
        await Simulate(discount.PaymentRequestId!.Value, "Failed");
        using var guardian = await Guardian();
        var retryPath = $"/api/v1/guardian/subscriptions/payments/{discount.PaymentRequestId}/retry";
        var key = $"guardian-checkout-retry:{discount.PaymentRequestId}";
        using var response = await Post(guardian, retryPath, new { }, key);
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<Created>())!;
        renewals.Add(created.RenewalId);

        using var replayResponse = await Post(guardian, retryPath, new { }, key);
        replayResponse.EnsureSuccessStatusCode();
        Assert.Equal(created, await replayResponse.Content.ReadFromJsonAsync<Created>());
        await using var scope = Scope();
        var db = Db(scope);
        Assert.Equal(810m, await db.PaymentRequests.Where(x => x.Id == created.PaymentId).Select(x => x.Amount).SingleAsync());
        Assert.Equal(90m, await db.RenewalRequests.Where(x => x.Id == created.RenewalId).Select(x => x.DiscountAmount).SingleAsync());
        Assert.Equal(1, await db.PaymentRequests.CountAsync(x => x.RenewalRequestId == created.RenewalId));
        Assert.False(await db.Collections.AnyAsync(x => x.RenewalRequestId == created.RenewalId));
    }
    [Fact] public async Task Remove_discount_restores_original_and_replaces_payment() { var s = await NewRenewal(); var applied = await Apply(s, RenewalDiscountType.Percentage, 10); await using var scope = Scope(); var service = scope.ServiceProvider.GetRequiredService<SubscriptionDiscountService>(); var removed = await service.RemoveAsync(DemoSeed.NogoomAcademyId, await Owner(), s.RenewalId, "إزالة للاختبار", applied.Version, Guid.NewGuid().ToString()); Assert.Equal(900m, removed.FinalAmount); Assert.NotEqual(applied.PaymentRequestId, removed.PaymentRequestId); }
    [Fact] public async Task Changing_discount_cancels_stale_payment() { var s = await NewRenewal(); var first = await Apply(s, RenewalDiscountType.Percentage, 10); var current = await Renewal(s.RenewalId); var second = await Apply(s with { Version = current.Version }, RenewalDiscountType.FixedAmount, 100); await using var scope = Scope(); var db = Db(scope); Assert.Equal(PaymentRequestStatus.Cancelled, await db.PaymentRequests.Where(x => x.Id == first.PaymentRequestId).Select(x => x.Status).SingleAsync()); Assert.Equal(800m, await db.PaymentRequests.Where(x => x.Id == second.PaymentRequestId).Select(x => x.Amount).SingleAsync()); }
    [Fact] public async Task Concurrent_updates_have_one_winner() { var s = await NewRenewal(); async Task<bool> Run(decimal v) { try { await Apply(s, RenewalDiscountType.FixedAmount, v); return true; } catch (DiscountConflictException) { return false; } } var outcomes = await Task.WhenAll(Run(100), Run(200)); Assert.Single(outcomes, x => x); }
    [Fact] public async Task Duplicate_discount_command_is_idempotent() { var s = await NewRenewal(); var key = Guid.NewGuid().ToString(); var first = await Apply(s, RenewalDiscountType.Percentage, 10, key: key); var second = await Apply(s, RenewalDiscountType.Percentage, 10, key: key); Assert.True(second.Replay); Assert.Equal(first.AdjustmentId, second.AdjustmentId); await using var scope = Scope(); Assert.Equal(1, await Db(scope).RenewalDiscountAdjustments.CountAsync(x => x.RenewalRequestId == s.RenewalId)); }
    [Fact] public async Task Renew_for_another_discount_does_not_grant_profile_access() { using var guardian = await Guardian(); var resolved = await Post(guardian, "/api/v1/guardian/subscriptions/external/resolve", new { reference = Slice3DemoSeed.ExternalRenewalCode }); resolved.EnsureSuccessStatusCode(); var response = await Post(guardian, "/api/v1/guardian/subscriptions/external/renewals", new { reference = Slice3DemoSeed.ExternalRenewalCode, subscriptionPlanId = Slice3DemoSeed.FootballMonthlyId }, Guid.NewGuid().ToString()); response.EnsureSuccessStatusCode(); var created = (await response.Content.ReadFromJsonAsync<Created>())!; renewals.Add(created.RenewalId); var row = await Renewal(created.RenewalId); await Apply(new Scenario(created.RenewalId, created.PaymentId, row.Version), RenewalDiscountType.Percentage, 10); Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync($"/api/v1/guardian/children/{Slice2DemoSeed.OtherPlayerId}")).StatusCode); }
    [Fact] public async Task Later_period_adjustments_do_not_change_paid_discount() { var r = await Apply(await NewRenewal(), RenewalDiscountType.Percentage, 10); await Simulate(r.PaymentRequestId!.Value, "Success"); await using var scope = Scope(); var db = Db(scope); var period = await db.SubscriptionPeriods.SingleAsync(x => x.Collection.PaymentRequestId == r.PaymentRequestId); period.EndDate = period.EndDate?.AddDays(5); period.Status = SubscriptionPeriodStatus.Cancelled; await db.SaveChangesAsync(); var renewal = await Renewal(r.RenewalRequestId); Assert.Equal((90m, 810m), (renewal.DiscountAmount, renewal.FinalAmount)); }
    [Fact] public async Task Financial_truth_is_consistent_across_all_records() { var r = await Apply(await NewRenewal(), RenewalDiscountType.FixedAmount, 125); await Simulate(r.PaymentRequestId!.Value, "Success"); await using var scope = Scope(); var db = Db(scope); var payment = await db.PaymentRequests.SingleAsync(x => x.Id == r.PaymentRequestId); var renewal = await db.RenewalRequests.SingleAsync(x => x.Id == r.RenewalRequestId); var collection = await db.Collections.SingleAsync(x => x.PaymentRequestId == r.PaymentRequestId); var receipt = await db.Receipts.SingleAsync(x => x.CollectionId == collection.Id); Assert.Equal([775m, 775m, 775m, 775m, 775m], [payment.Amount, renewal.FinalAmount, collection.Amount, receipt.FinalAmount, receipt.Amount]); }

    [Fact]
    public async Task Discount_attempt_reads_only_the_matching_actor_and_request()
    {
        var renewal = await NewRenewal();
        var other = await NewDirectRenewal(DemoSeed.NogoomAcademyId);
        var key = Guid.NewGuid().ToString();
        using var owner = await Staff(DemoSeed.OwnerEmail);
        using var admin = await Staff(DemoSeed.AdminEmail);
        var saved = await ApplyHttp(owner, renewal, "Percentage", 10, key);
        saved.EnsureSuccessStatusCode();
        var changeId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("adjustmentId").GetGuid();
        async Task<HttpResponseMessage> Attempt(HttpClient client, Guid id)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/subscriptions/renewals/{id}/discount/attempt");
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }
        using var outcome = await Attempt(owner, renewal.RenewalId);
        outcome.EnsureSuccessStatusCode();
        Assert.Equal(changeId, (await outcome.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("adjustmentId").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await Attempt(owner, other.RenewalId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Attempt(admin, renewal.RenewalId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ApplyHttp(owner, other, "Percentage", 10, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ApplyHttp(owner, renewal, "Percentage", 11, key)).StatusCode);
    }

    [Fact]
    public async Task Discount_rejects_precision_beyond_what_the_audit_can_store()
    {
        var renewal = await NewRenewal();
        using var owner = await Staff(DemoSeed.OwnerEmail);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ApplyHttp(owner, renewal, "Percentage", 10.125m)).StatusCode);
        Assert.Equal(renewal.Version, (await Renewal(renewal.RenewalId)).Version);
    }

    [Fact]
    public async Task Late_confirmation_of_replaced_payment_keeps_the_discounted_request_unpaid()
    {
        var scenario = await NewRenewal();
        await using var scope = Scope();
        var db = Db(scope);
        var oldAmount = await db.PaymentRequests.Where(row => row.Id == scenario.PaymentId).Select(row => row.Amount).SingleAsync();
        var updated = await Apply(scenario, RenewalDiscountType.Percentage, 10);
        var late = await Event(scenario.PaymentId, "succeeded", oldAmount);
        using var callback = factory.CreateClient();
        using var response = await callback.PostAsJsonAsync("/api/v1/payments/internal-test/events", late);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("request-terms-mismatch", await db.ProviderEventQuarantines.Where(row => row.ProviderEventId == late.ProviderEventId)
            .Select(row => row.ReasonCode).SingleAsync());
        var current = await Renewal(scenario.RenewalId);
        Assert.Equal(updated.PaymentRequestId, current.PaymentRequestId);
        Assert.Equal(810m, current.FinalAmount);
        Assert.Equal(RenewalRequestStatus.PaymentInProgress, current.Status);
        Assert.False(await db.Collections.AnyAsync(row => row.RenewalRequestId == scenario.RenewalId));
        Assert.False(await db.Receipts.AnyAsync(row => row.Collection.RenewalRequestId == scenario.RenewalId));
        Assert.False(await db.SubscriptionPeriods.AnyAsync(row => row.Collection != null && row.Collection.RenewalRequestId == scenario.RenewalId));
    }

    [Fact]
    public async Task Replaced_payment_cannot_be_retried_by_the_guardian_or_staff()
    {
        var source = await NewRenewal();
        var created = await Renewal(source.RenewalId);
        var replacement = await Apply(source, RenewalDiscountType.FixedAmount, 125);
        await using var scope = Scope();
        var db = Db(scope);
        var count = await db.RenewalRequests.CountAsync(row => row.AcademyId == created.AcademyId);
        using var guardian = await Guardian();
        using var owner = await Staff(DemoSeed.OwnerEmail);
        var guardianPath = $"/api/v1/guardian/subscriptions/payments/{source.PaymentId}/retry";
        var ownerPath = $"/api/v1/subscriptions/payments/{source.PaymentId}/retry";
        Assert.Equal(HttpStatusCode.Conflict, (await Post(guardian, guardianPath, new { }, Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(guardian, guardianPath, new { }, created.IdempotencyKey)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, ownerPath, new { }, Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(count, await db.RenewalRequests.CountAsync(row => row.AcademyId == created.AcademyId));
        Assert.Equal(replacement.PaymentRequestId, (await Renewal(source.RenewalId)).PaymentRequestId);
        Assert.False(await db.Collections.AnyAsync(row => row.RenewalRequestId == source.RenewalId));
    }

    private async Task<Scenario> NewRenewal()
    {
        using var c = await Guardian(); var enrollment = await Enrollment(); var response = await Post(c, "/api/v1/guardian/subscriptions/renewals", new { sportEnrollmentId = enrollment, subscriptionPlanId = Slice3DemoSeed.FootballMonthlyId }, Guid.NewGuid().ToString()); response.EnsureSuccessStatusCode(); var created = (await response.Content.ReadFromJsonAsync<Created>())!; renewals.Add(created.RenewalId); var row = await Renewal(created.RenewalId); return new(created.RenewalId, created.PaymentId, row.Version);
    }

    private async Task<Scenario> NewDirectRenewal(Guid academy)
    {
        await using var scope = Scope(); var db = Db(scope); var enrollment = await db.SportEnrollments.FirstAsync(x => x.AcademyId == academy); var plan = await db.SubscriptionPlans.FirstAsync(x => x.AcademyId == academy && x.SportId == enrollment.SportId); var actor = await db.AcademyMemberships.Where(x => x.AcademyId == academy).Select(x => x.UserId).FirstAsync(); var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid(); var paymentId = Guid.NewGuid();
        var renewal = new RenewalRequest { Id = id, AcademyId = academy, SportEnrollmentId = enrollment.Id, SportId = enrollment.SportId, SubscriptionPlanId = plan.Id, RequestedByUserId = actor, RequestedAtUtc = now, OriginalAmount = plan.Price, FinalAmount = plan.Price, AmountExpected = plan.Price, Currency = plan.Currency, Status = RenewalRequestStatus.PaymentInProgress, PaymentRequestId = paymentId, IdempotencyKey = Guid.NewGuid().ToString(), CreatedAtUtc = now, UpdatedAtUtc = now };
        db.AddRange(renewal, new PaymentRequest { Id = paymentId, AcademyId = academy, RenewalRequestId = id, Provider = "InternalTest", ProviderEnvironment = "Demo", Amount = plan.Price, Currency = plan.Currency, Status = PaymentRequestStatus.Pending, ProviderReference = $"ITP-{paymentId:N}", CheckoutReference = Guid.NewGuid().ToString("N"), CreatedAtUtc = now, UpdatedAtUtc = now }); await db.SaveChangesAsync(); renewals.Add(id); return new(id, paymentId, renewal.Version);
    }

    private async Task<RenewalDiscountResult> Apply(Scenario s, RenewalDiscountType type, decimal value, string reason = "خصم اختبار", string? key = null)
    { await using var scope = Scope(); return await scope.ServiceProvider.GetRequiredService<SubscriptionDiscountService>().ApplyAsync(await Academy(s.RenewalId), await Owner(), s.RenewalId, type, value, reason, s.Version, key ?? Guid.NewGuid().ToString()); }
    private async Task<HttpResponseMessage> ApplyHttp(HttpClient c, Scenario s, string type, decimal value, string? key = null) => await Post(c, $"/api/v1/subscriptions/renewals/{s.RenewalId}/discount", new { type, value, reason = "خصم اختبار", expectedVersion = s.Version }, key ?? Guid.NewGuid().ToString());
    private async Task Simulate(Guid payment, string outcome) { using var c = await Guardian(); (await Post(c, $"/api/v1/guardian/subscriptions/payments/{payment}/simulate", new { outcome })).EnsureSuccessStatusCode(); }
    private async Task<ProviderEventEnvelope> Event(Guid paymentId, string status, decimal amount) { await using var scope = Scope(); var p = await Db(scope).PaymentRequests.SingleAsync(x => x.Id == paymentId); var unsigned = new ProviderEventEnvelope(p.AcademyId, p.Id, p.ProviderReference, $"evt-{Guid.NewGuid():N}", status, amount, p.Currency, ""); return unsigned with { Signature = Sign(unsigned) }; }
    private static string Sign(ProviderEventEnvelope e) { var payload = string.Join('|', e.AcademyId, e.PaymentRequestId, e.ProviderReference, e.ProviderEventId, e.Status, e.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), e.Currency); return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("Demo-Test-Signing-Key-Only-123456"), Encoding.UTF8.GetBytes(payload))); }
    private async Task<HttpClient> Staff(string email) { var c = factory.CreateClient(); if (email == DemoSeed.CoachEmail) await MobileCoachTestSession.SignInAsync(c, factory.Services); else (await Post(c, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" })).EnsureSuccessStatusCode(); return c; }
    private async Task<HttpClient> Guardian() { var c = factory.CreateClient(); var request = await Post(c, "/api/v1/auth/guardian/otp/request", new { phoneNumber = DemoSeed.GuardianPhone }); var challenge = (await request.Content.ReadFromJsonAsync<Challenge>())!; (await Post(c, "/api/v1/auth/guardian/otp/verify", new { challengeId = challenge.ChallengeId, phoneNumber = DemoSeed.GuardianPhone, code = "246810" })).EnsureSuccessStatusCode(); return c; }
    private static async Task<HttpResponseMessage> Post(HttpClient c, string path, object body, string? key = null) { var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", (await c.GetFromJsonAsync<Csrf>("/api/v1/auth/csrf"))!.Token); if (key is not null) request.Headers.Add("Idempotency-Key", key); return await c.SendAsync(request); }
    private static async Task<JsonElement> Get(HttpClient c, string path) => await c.GetFromJsonAsync<JsonElement>(path);
    private async Task<Guid> Enrollment() { await using var scope = Scope(); return await Db(scope).SportEnrollments.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.PlayerId == Slice2DemoSeed.OmarPlayerId && x.SportId == Slice2DemoSeed.FootballId).Select(x => x.Id).SingleAsync(); }
    private async Task<Guid> Owner() { await using var scope = Scope(); return await Db(scope).AcademyMemberships.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Role == global::Academy.Infrastructure.Tenancy.AcademyRole.AcademyOwner && x.User.Email == DemoSeed.OwnerEmail).Select(x => x.UserId).SingleAsync(); }
    private async Task<Guid> Academy(Guid renewal) { await using var scope = Scope(); return await Db(scope).RenewalRequests.Where(x => x.Id == renewal).Select(x => x.AcademyId).SingleAsync(); }
    private async Task<RenewalRequest> Renewal(Guid id) { await using var scope = Scope(); return await Db(scope).RenewalRequests.AsNoTracking().SingleAsync(x => x.Id == id); }
    private AsyncServiceScope Scope() => factory.Services.CreateAsyncScope(); private static FoundationDbContext Db(AsyncServiceScope s) => s.ServiceProvider.GetRequiredService<FoundationDbContext>();
    private sealed record Scenario(Guid RenewalId, Guid PaymentId, uint Version); private sealed record Created(Guid RenewalId, Guid PaymentId); private sealed record Challenge(Guid ChallengeId); private sealed record Csrf(string Token);
    private sealed class Factory(string connection) : WebApplicationFactory<Program> { protected override void ConfigureWebHost(IWebHostBuilder b) { b.UseEnvironment("Demo"); b.UseSetting("ConnectionStrings:Default", connection); b.UseSetting("Demo:SeedProfile", "LegacyRegression"); b.UseSetting("Demo:SeedEnabled", "true"); b.UseSetting("Demo:FixedOtpEnabled", "true"); b.UseSetting("Demo:FixedOtp", "246810"); b.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); b.UseSetting("Demo:ReferenceDate", "2026-09-28"); b.UseSetting("Payments:InternalTest:Enabled", "true"); b.UseSetting("Payments:InternalTest:SigningKey", "Demo-Test-Signing-Key-Only-123456"); } }
}
