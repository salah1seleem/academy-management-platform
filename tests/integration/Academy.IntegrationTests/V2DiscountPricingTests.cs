using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;

public sealed partial class V2PlanPublicationTests
{
    [Fact]
    public async Task Discount_retry_preserves_financial_and_service_terms_instead_of_repricing_history()
    {
        var f = await Fixture(false); using var owner = await Staff(); var plan = await CreatePlan(owner, f.A); var body = FixedRule(f.A, plan); var rule = await AddRule(owner, body);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var player = await db.SportEnrollments.Where(x => x.Id == f.Enrollment).Select(x => x.PlayerId).SingleAsync();
        (await Write(owner, $"/api/v1/discount-rules/players/{player}", new { discountRuleId = rule, isActive = true }, true)).EnsureSuccessStatusCode();
        var created = await Write(owner, "/api/v1/subscriptions/admin-renewals", new { sportEnrollmentId = f.Enrollment, subscriptionPlanId = plan }); created.EnsureSuccessStatusCode(); var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid();
        var payment = await db.PaymentRequests.SingleAsync(x => x.Id == id); payment.Status = PaymentRequestStatus.Failed; await db.SaveChangesAsync();
        (await Write(owner, $"/api/v1/discount-rules/{rule}", body with { FixedAmount = 200, ExpectedRevision = 1 }, true)).EnsureSuccessStatusCode();
        (await Write(owner, $"/api/v1/subscriptions/plans/{plan}", Body(f.A, "شروط جديدة للاشتراك القادم " + Guid.NewGuid().ToString("N")[..8], 700, 1, SubscriptionPlanType.Sessions), true)).EnsureSuccessStatusCode();
        var retry = await Write(owner, $"/api/v1/subscriptions/payments/{id}/retry", new { }); retry.EnsureSuccessStatusCode(); var next = (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid();
        var renewal = await db.RenewalRequests.AsNoTracking().SingleAsync(x => x.PaymentRequestId == next); Assert.Equal((500m, 1, 1, 30, 8), (renewal.FinalAmount, renewal.DiscountRuleRevisionSnapshot, renewal.PlanRevisionSnapshot, renewal.DurationDaysSnapshot, renewal.SessionCountSnapshot));
    }
    [Fact]
    public async Task Guardian_discount_request_keeps_fixed_rule_snapshot_through_manual_proof_approval()
    {
        var f = await Fixture(); using var owner = await Staff(); using var guardian = await Guardian();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var player = await db.SportEnrollments.Where(x => x.Id == f.Enrollment).Select(x => x.PlayerId).SingleAsync();
        var group = Guid.NewGuid(); var age = await db.TrainingGroups.Where(x => x.Id == f.Group).Select(x => x.AgeCategoryId).SingleAsync();
        db.TrainingGroups.Add(new() { Id = group, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "مجموعة قدم تحقق خصم " + group.ToString("N")[..8], BranchId = f.A, SportId = Slice2DemoSeed.FootballId, AgeCategoryId = age, Capacity = 100 }); await db.SaveChangesAsync();
        var p = await Write(owner, "/api/v1/subscriptions/plans", Body(f.A, "باقة قدم تحقق خصم " + Guid.NewGuid().ToString("N")[..8]) with { SportId = Slice2DemoSeed.FootballId }); p.EnsureSuccessStatusCode(); var plan = (await p.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var body = FixedRule(f.A, plan, 200) with { SportId = Slice2DemoSeed.FootballId }; var rule = await AddRule(owner, body);
        (await Write(owner, $"/api/v1/discount-rules/players/{player}", new { discountRuleId = rule, isActive = true }, true)).EnsureSuccessStatusCode();
        var submitted = await Write(guardian, "/api/v1/guardian/enrollment-requests", new { requestType = 1, existingPlayerId = player, sportId = Slice2DemoSeed.FootballId, preferredBranchId = f.A, subscriptionPlanId = plan }); submitted.EnsureSuccessStatusCode(); var id = (await submitted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var request = await db.NewEnrollmentRequests.SingleAsync(x => x.Id == id); Assert.Equal((RenewalDiscountType.FixedAmount, 200m, 200m, 400m), (request.DiscountType, request.DiscountValue, request.DiscountAmount, request.FinalAmount));
        using var upload = new MultipartFormDataContent(); var file = new ByteArrayContent([137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0]); file.Headers.ContentType = new("image/png"); upload.Add(file, "file", "synthetic-proof.png"); upload.Add(new StringContent("Proof"), "purpose");
        var csrf = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/assets/upload") { Content = upload }; uploadRequest.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var uploaded = await guardian.SendAsync(uploadRequest); uploaded.EnsureSuccessStatusCode(); var asset = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Write(guardian, $"/api/v1/guardian/enrollment-requests/{id}/proof", new { assetId = asset })).EnsureSuccessStatusCode();
        (await Write(owner, $"/api/v1/discount-rules/{rule}", body with { FixedAmount = 300, ExpectedRevision = 1 }, true)).EnsureSuccessStatusCode();
        (await Write(owner, $"/api/v1/enrollment-requests/{id}/approve", new { trainingGroupId = group })).EnsureSuccessStatusCode();
        db.ChangeTracker.Clear(); request = await db.NewEnrollmentRequests.SingleAsync(x => x.Id == id); var receipt = await db.Receipts.SingleAsync(x => x.SportEnrollmentId == request.CreatedSportEnrollmentId);
        Assert.Equal(400, receipt.Amount); Assert.Equal(RenewalDiscountType.FixedAmount, receipt.DiscountType); Assert.Equal(200, receipt.DiscountValue); Assert.Equal(rule, receipt.DiscountRuleIdSnapshot); Assert.Equal(1, receipt.DiscountRuleRevisionSnapshot);
    }
    private static RuleWrite FixedRule(Guid branch, Guid plan, decimal value = 100, int? revision = null) => new("عرض تحقق " + Guid.NewGuid().ToString("N")[..8], 0, false, true, null,
        RenewalDiscountType.FixedAmount, value, BranchId: branch, SportId: Slice2DemoSeed.SwimmingId, SubscriptionPlanId: plan, ExpectedRevision: revision);
    private async Task<Guid> AddRule(HttpClient owner, RuleWrite body)
    {
        var response = await Write(owner, "/api/v1/discount-rules/", body); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    [Fact]
    public async Task FIN_03_discount_rule_changes_preserve_pending_money_revision_and_receipt()
    {
        var f = await Fixture(false); using var owner = await Staff(); var plan = await CreatePlan(owner, f.A); var body = FixedRule(f.A, plan); var rule = await AddRule(owner, body);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var player = await db.SportEnrollments.Where(x => x.Id == f.Enrollment).Select(x => x.PlayerId).SingleAsync();
        (await Write(owner, $"/api/v1/discount-rules/players/{player}", new { discountRuleId = rule, isActive = true }, true)).EnsureSuccessStatusCode();
        var r = await Write(owner, "/api/v1/subscriptions/admin-renewals", new { sportEnrollmentId = f.Enrollment, subscriptionPlanId = plan }); r.EnsureSuccessStatusCode(); var payment = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid();
        (await Write(owner, $"/api/v1/discount-rules/{rule}", body with { FixedAmount = 200, ExpectedRevision = 1 }, true)).EnsureSuccessStatusCode();
        var history = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/discount-rules/{rule}/versions"); Assert.Equal(2, history.GetArrayLength());
        Assert.Equal("100.00", history[1].GetProperty("snapshot").GetProperty("fixedAmount").GetString());
        await using (var tx = await db.Database.BeginTransactionAsync()) { await scope.ServiceProvider.GetRequiredService<CollectionService>().ConfirmAsync(DemoSeed.NogoomAcademyId, f.Owner, payment, "Online"); await tx.CommitAsync(); }
        db.ChangeTracker.Clear(); var receipt = await db.Receipts.SingleAsync(x => x.Collection.PaymentRequestId == payment);
        Assert.Equal((600m, 100m, 500m), (receipt.OriginalAmount, receipt.DiscountAmount, receipt.Amount)); Assert.Equal(RenewalDiscountType.FixedAmount, receipt.DiscountType);
        Assert.Equal(rule, receipt.DiscountRuleIdSnapshot); Assert.Equal(1, receipt.DiscountRuleRevisionSnapshot); Assert.Contains(body.ArabicName, receipt.DiscountReason);
        var next = await Write(owner, "/api/v1/subscriptions/admin-renewals", new { sportEnrollmentId = f.Enrollment, subscriptionPlanId = plan }); next.EnsureSuccessStatusCode();
        var nextId = (await next.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid(); Assert.Equal(400, (await db.PaymentRequests.AsNoTracking().SingleAsync(x => x.Id == nextId)).Amount);
    }
    [Fact]
    public async Task FIN_04_registration_uses_single_best_scoped_discount_and_optional_description()
    {
        var f = await Fixture(false); using var owner = await Staff(); var plan = await CreatePlan(owner, f.A);
        var rule = await AddRule(owner, FixedRule(f.A, plan, 200));
        var phone = "010" + Random.Shared.Next(10000000, 99999999);
        var quote = await Write(owner, "/api/v1/people/registration-quote", new { planId = plan, branchId = f.A, guardianPhone = phone, discountRuleId = rule }); quote.EnsureSuccessStatusCode();
        Assert.Equal("400.00", (await quote.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("finalAmount").GetString());
        var registration = await Write(owner, "/api/v1/people/registrations", new { playerArabicName = "طفل خصم اصطناعي", dateOfBirth = $"{f.BirthYear}-01-01", guardianName = "ولي أمر اصطناعي", guardianPhone = phone, branchId = f.A, sportId = Slice2DemoSeed.SwimmingId, groupId = f.Group, planId = plan, paymentMethod = "Online", discountRuleId = rule, expectedAmount = 400 });
        registration.EnsureSuccessStatusCode(); var player = (await registration.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var percentage = await AddRule(owner, FixedRule(f.A, plan) with { Type = RenewalDiscountType.Percentage, FixedAmount = null, Percentage = 25 });
        (await Write(owner, $"/api/v1/discount-rules/players/{player}", new { discountRuleId = percentage, isActive = true }, true)).EnsureSuccessStatusCode();
        var enrolled = await db.SportEnrollments.SingleAsync(x => x.PlayerId == player);
        var r = await Write(owner, "/api/v1/subscriptions/admin-renewals", new { sportEnrollmentId = enrolled.Id, subscriptionPlanId = plan }); r.EnsureSuccessStatusCode(); var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid();
        var renewal = await db.RenewalRequests.SingleAsync(x => x.PaymentRequestId == id); Assert.Equal(200, renewal.DiscountAmount); Assert.Equal(400, renewal.FinalAmount); Assert.Equal(rule, renewal.DiscountRuleIdSnapshot); Assert.False(string.IsNullOrWhiteSpace(renewal.DiscountReason));
        // An expired offer remains in the history but is not eligible for a new transaction.
        (await Write(owner, $"/api/v1/discount-rules/{rule}", FixedRule(f.A, plan, 200, 1) with { EndsOn = new(2026, 9, 27) }, true)).EnsureSuccessStatusCode();
        var pricing = scope.ServiceProvider.GetRequiredService<DiscountRules>(); var catalog = await db.SubscriptionPlans.SingleAsync(x => x.Id == plan);
        var result = await pricing.CalculateAsync(DemoSeed.NogoomAcademyId, player, 600, f.Owner, plan: catalog, branchId: f.A); Assert.Equal(150, result.DiscountAmount); Assert.Equal(percentage, result.DiscountRuleId);
    }
    [Fact]
    public async Task Discount_sibling_eligibility_requires_real_current_paid_active_period_not_a_name_or_link_alone()
    {
        var f = await Fixture(false); using var owner = await Staff(); var plan = await CreatePlan(owner, f.A); var phone = "010" + Random.Shared.Next(10000000, 99999999);
        var r = await Write(owner, "/api/v1/people/registrations", new { playerArabicName = "أخ تحقق اصطناعي", dateOfBirth = $"{f.BirthYear}-01-01", guardianName = "اسم ولي أمر مكرر", guardianPhone = phone, branchId = f.A, sportId = Slice2DemoSeed.SwimmingId, groupId = f.Group, planId = plan, paymentMethod = "Online", expectedAmount = 600 }); r.EnsureSuccessStatusCode(); var player = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid();
        async Task<decimal> Quote(string number) { var q = await Write(owner, "/api/v1/people/registration-quote", new { planId = plan, branchId = f.A, guardianPhone = number }); q.EnsureSuccessStatusCode(); return decimal.Parse((await q.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("discountAmount").GetString()!, System.Globalization.CultureInfo.InvariantCulture); }
        Assert.Equal(120, await Quote(phone)); Assert.Equal(0, await Quote("010" + Random.Shared.Next(10000000, 99999999)));
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var period = await db.SubscriptionPeriods.Include(x => x.SportEnrollment).Include(x => x.Collection).ThenInclude(x => x.PaymentRequest).SingleAsync(x => x.SportEnrollment.PlayerId == player);
        foreach (var status in new[] { SubscriptionPeriodStatus.Frozen, SubscriptionPeriodStatus.Cancelled }) { period.Status = status; await db.SaveChangesAsync(); Assert.Equal(0, await Quote(phone)); }
        period.Status = SubscriptionPeriodStatus.Active; period.EndDate = new(2026, 9, 27); await db.SaveChangesAsync(); Assert.Equal(0, await Quote(phone));
        period.EndDate = new(2026, 10, 27); period.RemainingSessions = 0; await db.SaveChangesAsync(); Assert.Equal(0, await Quote(phone));
        period.RemainingSessions = 8; period.SportEnrollment.IsActive = false; await db.SaveChangesAsync(); Assert.Equal(0, await Quote(phone));
        period.SportEnrollment.IsActive = true; period.Collection.PaymentRequest.Status = PaymentRequestStatus.Pending; await db.SaveChangesAsync(); Assert.Equal(0, await Quote(phone));
        period.Collection.PaymentRequest.Status = PaymentRequestStatus.Confirmed; await db.SaveChangesAsync(); Assert.Equal(120, await Quote(phone));
    }
    [Fact]
    public async Task Discount_history_is_immutable_updates_are_revision_safe_and_branch_or_tenant_IDOR_is_denied()
    {
        var f = await Fixture(false); using var owner = await Staff(); var plan = await CreatePlan(owner, f.A); var body = FixedRule(f.A, plan); var id = await AddRule(owner, body);
        var results = await Task.WhenAll(Write(owner, $"/api/v1/discount-rules/{id}", body with { FixedAmount = 200, ExpectedRevision = 1 }, true), Write(owner, $"/api/v1/discount-rules/{id}", body with { FixedAmount = 300, ExpectedRevision = 1 }, true));
        Assert.Single(results, x => x.IsSuccessStatusCode); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        var email = $"discount.{Guid.NewGuid():N}@example.test"; var created = await Write(owner, "/api/v1/manage/staff", new StaffWrite("إداري خصومات اصطناعي", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, null)); created.EnsureSuccessStatusCode(); var member = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Write(owner, $"/api/v1/manage/staff/{member}/access", new StaffAccessWrite(false, [f.B], StaffAccessRules.Modules.Select(m => new StaffActionValue(m, true, m != "followups", true, false)).ToArray()), true)).EnsureSuccessStatusCode(); using var admin = await Staff(email);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/discount-rules/{id}/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, "/api/v1/discount-rules/", FixedRule(f.A, plan))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, "/api/v1/discount-rules/", body with { BranchId = null, SubscriptionPlanId = null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, "/api/v1/discount-rules/", body with { BranchId = f.B, SubscriptionPlanId = null, IsSiblingOffer = true })).StatusCode);
        using var foreign = await Staff("owner.future@example.test"); Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/api/v1/discount-rules/{id}/versions")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var version = await db.DiscountRuleVersions.FirstAsync(x => x.DiscountRuleId == id);
        version.Payload = "{}"; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.Entry(version).State = EntityState.Unchanged;
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"DiscountRuleVersions\" WHERE \"Id\" = {version.Id}")); Assert.Equal("23514", error.SqlState);
    }
}
