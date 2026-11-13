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
    private readonly List<Guid> suspendedTransferSettings = [];

    public async Task InitializeAsync() { factory = new Factory(connection); _ = factory.CreateClient(); await Cleanup(); await SuspendExistingOpenRenewals(); await SuspendManualTransferRoute(); }
    public async Task DisposeAsync() { await Cleanup(); await RestoreExistingOpenRenewals(); await RestoreManualTransferRoute(); await factory.DisposeAsync(); }

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

    [Theory]
    [InlineData(SubscriptionPeriodStatus.Active, SubscriptionPlanType.Duration, "2026-09-01", "2026-09-27", null, "Expired")]
    [InlineData(SubscriptionPeriodStatus.Active, SubscriptionPlanType.Sessions, "2026-09-01", null, 0, "Expired")]
    [InlineData(SubscriptionPeriodStatus.Frozen, SubscriptionPlanType.Duration, "2026-09-01", "2026-09-27", null, "Frozen")]
    [InlineData(SubscriptionPeriodStatus.Active, SubscriptionPlanType.Duration, "2026-09-29", "2026-10-29", null, "Scheduled")]
    public async Task Search_and_review_show_the_same_effective_period_status(SubscriptionPeriodStatus storedStatus,
        SubscriptionPlanType planType, string start, string? end, int? sessions, string expected)
    {
        var enrollmentId = await Enrollment();
        var planId = planType == SubscriptionPlanType.Sessions ? Slice3DemoSeed.FootballSessionsId : Slice3DemoSeed.FootballMonthlyId;
        using var owner = await Staff(DemoSeed.OwnerEmail);
        var created = await Json(await Post(owner, "/api/v1/subscriptions/admin-renewals",
            new { sportEnrollmentId = enrollmentId, subscriptionPlanId = planId }, Key()));
        var paymentId = created.GetProperty("paymentId").GetGuid();
        await Simulate(paymentId, "Success");

        await using var scope = Scope();
        var db = Db(scope);
        var code = await db.SportEnrollments.Where(x => x.Id == enrollmentId).Select(x => x.Player.PlayerCode).SingleAsync();
        var period = await db.SubscriptionPeriods.SingleAsync(x => x.Collection.PaymentRequestId == paymentId);
        var previous = (await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == enrollmentId && p.Id != period.Id)
            .ToListAsync()).Select(p => (Period: p, p.Status)).ToArray();
        try
        {
            foreach (var (older, _) in previous) older.Status = SubscriptionPeriodStatus.Cancelled;
            period.Status = storedStatus;
            period.StartDate = DateOnly.ParseExact(start, "yyyy-MM-dd");
            period.EndDate = end is null ? null : DateOnly.ParseExact(end, "yyyy-MM-dd");
            period.RemainingSessions = sessions;
            await db.SaveChangesAsync();

            var results = await Search(code);
            var row = Assert.Single(results.EnumerateArray(), x => x.GetProperty("id").GetGuid() == enrollmentId);
            var review = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/admin-renewals/enrollments/{enrollmentId}");
            Assert.Equal(period.Id, row.GetProperty("period").GetProperty("id").GetGuid());
            Assert.Equal(period.Id, review.GetProperty("current").GetProperty("id").GetGuid());
            Assert.Equal(expected, row.GetProperty("period").GetProperty("status").GetString());
            Assert.Equal(expected, review.GetProperty("current").GetProperty("status").GetString());
        }
        finally
        {
            foreach (var (older, state) in previous) older.Status = state;
            await db.SaveChangesAsync();
        }
    }

    [Fact] public async Task Owner_can_create_admin_renewal() { using var c = await Staff(DemoSeed.OwnerEmail); Assert.Equal(HttpStatusCode.Created, (await Create(c)).StatusCode); }
    [Fact] public async Task Admin_can_create_admin_renewal() { using var c = await Staff(DemoSeed.AdminEmail); Assert.Equal(HttpStatusCode.Created, (await Create(c)).StatusCode); }
    [Fact] public async Task Idempotency_key_is_required() { using var c = await Staff(DemoSeed.OwnerEmail); var r = await Post(c, "/api/v1/subscriptions/admin-renewals", Body(), null); Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); }
    [Fact] public async Task Same_idempotency_key_replays_same_result() { using var c = await Staff(DemoSeed.OwnerEmail); var key = Key(); var first = await Create(c, key); var second = await Create(c, key); Assert.Equal((await Json(first)).GetProperty("paymentId").GetGuid(), (await Json(second)).GetProperty("paymentId").GetGuid()); }
    [Fact] public async Task Reusing_a_key_for_a_different_plan_is_rejected_without_a_second_request() { using var c = await Staff(DemoSeed.OwnerEmail); var key = Key(); (await Create(c, key)).EnsureSuccessStatusCode(); var r = await Post(c, "/api/v1/subscriptions/admin-renewals", new { sportEnrollmentId = await Enrollment(), subscriptionPlanId = Slice3DemoSeed.SwimmingMonthlyId }, key); Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); }
    [Fact] public async Task Second_open_renewal_is_rejected() { using var c = await Staff(DemoSeed.OwnerEmail); (await Create(c)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Conflict, (await Create(c)).StatusCode); }

    [Fact]
    public async Task Staff_quote_is_read_only_and_posting_uses_the_quoted_amount_and_terms()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail);
        var enrollment = await Enrollment();
        var plans = (await Details()).GetProperty("plans");
        var demoPlans = new[] { Slice3DemoSeed.FootballMonthlyId, Slice3DemoSeed.FootballQuarterlyId,
            Slice3DemoSeed.FootballAnnualId, Slice3DemoSeed.FootballSessionsId };
        foreach (var id in demoPlans)
        {
            var plan = Assert.Single(plans.EnumerateArray(), p => p.GetProperty("id").GetGuid() == id);
            var quote = await ReadQuote(owner, enrollment, id);
            Assert.Equal(enrollment, quote.EnrollmentId);
            Assert.Equal(id, quote.PlanId);
            Assert.Equal(plan.GetProperty("price").GetDecimal(), quote.OriginalAmount);
            Assert.Equal(quote.OriginalAmount - quote.DiscountAmount, quote.FinalAmount);
            var months = plan.GetProperty("durationMonths");
            var days = plan.GetProperty("durationDays");
            var sessions = plan.GetProperty("sessionCount");
            Assert.Equal(months.ValueKind == JsonValueKind.Number ? quote.Start.AddMonths(months.GetInt32()).AddDays(-1)
                : days.ValueKind == JsonValueKind.Number ? quote.Start.AddDays(days.GetInt32() - 1) : (DateOnly?)null, quote.End);
            Assert.Equal(sessions.ValueKind == JsonValueKind.Number ? sessions.GetInt32() : (int?)null, quote.Sessions);
        }
        Assert.Equal(0, await Count<RenewalRequest>(r => r.IdempotencyKey.StartsWith(Prefix)));

        var sessionsPlan = Slice3DemoSeed.FootballSessionsId;
        var selected = await ReadQuote(owner, enrollment, sessionsPlan);
        using var created = await Post(owner, "/api/v1/subscriptions/admin-renewals", new
            { sportEnrollmentId = enrollment, subscriptionPlanId = sessionsPlan, expectedQuoteHash = selected.QuoteHash }, Key());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var ids = (await created.Content.ReadFromJsonAsync<CreatedDto>())!;
        Assert.Equal(0, await Count<PaymentCollection>(c => c.PaymentRequestId == ids.PaymentId));
        Assert.Equal(HttpStatusCode.Conflict, (await owner.GetAsync(QuotePath(enrollment, sessionsPlan))).StatusCode);
        await Simulate(ids.PaymentId, "Success");
        await Simulate(ids.PaymentId, "Success");
        await using var scope = Scope(); var db = Db(scope);
        var collected = await db.PaymentRequests.SingleAsync(p => p.Id == ids.PaymentId);
        Assert.Equal(selected.FinalAmount, collected.Amount);
        var period = await db.SubscriptionPeriods.SingleAsync(p => p.Collection.PaymentRequestId == ids.PaymentId);
        Assert.Equal(selected.Start, period.StartDate);
        Assert.Equal(selected.End, period.EndDate);
        Assert.Equal(selected.Sessions, period.InitialSessions);
        Assert.Equal(1, await Count<Receipt>(r => r.Collection.PaymentRequestId == ids.PaymentId));
    }

    [Fact]
    public async Task A_changed_plan_rejects_the_quote_and_a_committed_key_still_replays_after_another_change()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail);
        var enrollment = await Enrollment(); var planId = Slice3DemoSeed.FootballMonthlyId;
        var original = await ReadQuote(owner, enrollment, planId);
        await using var scope = Scope(); var db = Db(scope);
        var plan = await db.SubscriptionPlans.SingleAsync(x => x.Id == planId);
        var before = (plan.Price, plan.Revision);
        try
        {
            plan.Price += 25; plan.Revision++;
            await db.SaveChangesAsync();
            object Body(string hash) => new { sportEnrollmentId = enrollment, subscriptionPlanId = planId, expectedQuoteHash = hash };
            using var stale = await Post(owner, "/api/v1/subscriptions/admin-renewals", Body(original.QuoteHash), Key());
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Contains("تغيّر السعر", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
            Assert.Equal(0, await Count<RenewalRequest>(r => r.IdempotencyKey.StartsWith(Prefix)));
            var updated = await ReadQuote(owner, enrollment, planId);
            Assert.Equal(original.OriginalAmount + 25, updated.OriginalAmount);
            var key = Key();
            var created = await Json(await Post(owner, "/api/v1/subscriptions/admin-renewals", Body(updated.QuoteHash), key));
            plan.Price += 10; plan.Revision++;
            await db.SaveChangesAsync();
            var replay = await Json(await Post(owner, "/api/v1/subscriptions/admin-renewals", Body(updated.QuoteHash), key));
            Assert.Equal(created.GetProperty("paymentId").GetGuid(), replay.GetProperty("paymentId").GetGuid());
            Assert.Equal(1, await Count<RenewalRequest>(r => r.IdempotencyKey.StartsWith(Prefix)));
        }
        finally { (plan.Price, plan.Revision) = before; await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task A_new_discount_and_changed_prior_period_each_invalidate_the_quote()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail);
        var enrollment = await Enrollment(); var planId = Slice3DemoSeed.FootballMonthlyId;
        var original = await ReadQuote(owner, enrollment, planId);
        await using var scope = Scope(); var db = Db(scope);
        var period = await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == enrollment && p.EndDate != null && p.Status != SubscriptionPeriodStatus.Cancelled)
            .OrderByDescending(p => p.EndDate).FirstAsync();
        var oldEnd = period.EndDate;
        var ruleId = Guid.Parse("b5ce42b5-47f1-4e3f-acdb-e89c85f73f50");
        var now = DateTimeOffset.UtcNow;
        var rule = await db.DiscountRules.SingleOrDefaultAsync(r => r.Id == ruleId);
        if (rule is null)
        {
            rule = new DiscountRule
            {
                Id = ruleId, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "خصم اختبار التجديد",
                Reason = "تحديث الخصم", Percentage = 50, SubscriptionPlanId = planId, Revision = 1,
                IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now
            };
            db.DiscountRules.Add(rule);
            db.DiscountRuleVersions.Add(DiscountRuleVersion.Create(rule, null, now));
        }
        var link = new PlayerDiscountLink { Id = Guid.NewGuid(), AcademyId = rule.AcademyId, DiscountRuleId = rule.Id, PlayerId = Slice2DemoSeed.OtherPlayerId,
            IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.PlayerDiscountLinks.Add(link);
        await db.SaveChangesAsync();
        try
        {
            Task<HttpResponseMessage> CreateFrom(StaffRenewalQuote quote) => Post(owner, "/api/v1/subscriptions/admin-renewals", new
                { sportEnrollmentId = enrollment, subscriptionPlanId = planId, expectedQuoteHash = quote.QuoteHash }, Key());
            Assert.Equal(HttpStatusCode.Conflict, (await CreateFrom(original)).StatusCode);
            var discounted = await ReadQuote(owner, enrollment, planId);
            Assert.Equal(original.OriginalAmount / 2, discounted.DiscountAmount);
            Assert.Equal(original.OriginalAmount / 2, discounted.FinalAmount);
            period.EndDate = discounted.Start.AddDays(2);
            await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.Conflict, (await CreateFrom(discounted)).StatusCode);
            var extended = await ReadQuote(owner, enrollment, planId);
            Assert.Equal(discounted.Start.AddDays(3), extended.Start);
            Assert.Equal(discounted.FinalAmount, extended.FinalAmount);
            Assert.Equal(0, await Count<RenewalRequest>(r => r.IdempotencyKey.StartsWith(Prefix)));
            var periodsBefore = await Count<SubscriptionPeriod>(p => p.SportEnrollmentId == enrollment);
            using var created = await CreateFrom(extended);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var ids = (await created.Content.ReadFromJsonAsync<CreatedDto>())!;
            var payment = await db.PaymentRequests.AsNoTracking().SingleAsync(p => p.Id == ids.PaymentId);
            var renewal = await db.RenewalRequests.AsNoTracking().SingleAsync(r => r.Id == ids.RenewalId);
            Assert.Equal(extended.FinalAmount, payment.Amount);
            Assert.Equal(extended.DiscountAmount, renewal.DiscountAmount);
            Assert.Equal(rule.Id, renewal.DiscountRuleIdSnapshot);
            Assert.Equal(PaymentRequestStatus.Pending, payment.Status);
            Assert.Equal(periodsBefore, await Count<SubscriptionPeriod>(p => p.SportEnrollmentId == enrollment));
            Assert.Equal(0, await Count<PaymentCollection>(c => c.PaymentRequestId == ids.PaymentId));
            Assert.Equal(0, await Count<Receipt>(r => r.Collection.PaymentRequestId == ids.PaymentId));
        }
        finally
        {
            period.EndDate = oldEnd;
            db.PlayerDiscountLinks.Remove(link);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Staff_quotes_reject_other_academies_wrong_sports_and_coaches()
    {
        var enrollment = await Enrollment(); var plan = Slice3DemoSeed.FootballMonthlyId;
        using var foreign = await Staff(DemoSeed.FutureOwnerEmail);
        using var owner = await Staff(DemoSeed.OwnerEmail);
        using var coach = await Staff(DemoSeed.CoachEmail);
        using var guardian = await Guardian();
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(QuotePath(enrollment, plan))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(QuotePath(enrollment, Slice3DemoSeed.SwimmingMonthlyId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await coach.GetAsync(QuotePath(enrollment, plan))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync(QuotePath(enrollment, plan))).StatusCode);
    }

    [Fact]
    public async Task Calendar_month_quote_at_month_end_uses_only_plans_published_to_the_enrollment_branch()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail);
        var enrollment = await Enrollment();
        await using var scope = Scope(); var db = Db(scope);
        var branch = await db.SportEnrollments.Where(x => x.Id == enrollment).Select(x => x.BranchId).SingleAsync();
        var other = await db.Branches.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Id != branch && x.IsActive)
            .OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
        var periods = await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == enrollment && p.EndDate != null && p.Status != SubscriptionPeriodStatus.Cancelled)
            .OrderByDescending(p => p.EndDate).ToListAsync();
        var originalPeriods = periods.Select(p => (Period: p, p.EndDate, p.Status)).ToArray();
        var now = DateTimeOffset.UtcNow;
        var available = Guid.NewGuid();
        var unavailable = Guid.NewGuid();
        var createdPlans = new[] { available, unavailable };
        try
        {
            foreach (var (id, publication) in new[] { (available, branch), (unavailable, other) })
            {
                db.SubscriptionPlans.Add(new SubscriptionPlan
                {
                    Id = id, AcademyId = DemoSeed.NogoomAcademyId, SportId = Slice2DemoSeed.FootballId,
                    ArabicName = $"شهر تقويمي لاختبار التجديد {id:N}", PlanType = SubscriptionPlanType.Duration,
                    DurationMonths = 1, Price = 600m, Currency = "EGP", AllBranches = false, Revision = 1,
                    CreatedAtUtc = now, UpdatedAtUtc = now
                });
                db.PlanBranchPublications.Add(new PlanBranchPublication
                {
                    Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, SubscriptionPlanId = id,
                    BranchId = publication, CreatedAtUtc = now, UpdatedAtUtc = now
                });
            }
            periods[0].EndDate = new DateOnly(2027, 1, 30);
            foreach (var period in periods.Skip(1)) period.Status = SubscriptionPeriodStatus.Cancelled;
            await db.SaveChangesAsync();
            var quote = await ReadQuote(owner, enrollment, available);
            Assert.Equal(new DateOnly(2027, 1, 31), quote.Start);
            Assert.Equal(new DateOnly(2027, 2, 27), quote.End);
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(QuotePath(enrollment, unavailable))).StatusCode);
            Assert.Equal(0, await Count<RenewalRequest>(r => r.IdempotencyKey.StartsWith(Prefix)));
        }
        finally
        {
            foreach (var (period, endDate, status) in originalPeriods)
            {
                period.EndDate = endDate;
                period.Status = status;
            }
            await db.SaveChangesAsync();
            await db.PlanBranchPublications.Where(x => createdPlans.Contains(x.SubscriptionPlanId)).ExecuteDeleteAsync();
            await db.SubscriptionPlans.Where(x => createdPlans.Contains(x.Id)).ExecuteDeleteAsync();
        }
    }

    private static string QuotePath(Guid enrollment, Guid plan) => $"/api/v1/subscriptions/admin-renewals/enrollments/{enrollment}/quote?planId={plan}";
    private static async Task<StaffRenewalQuote> ReadQuote(HttpClient owner, Guid enrollment, Guid plan)
        => (await owner.GetFromJsonAsync<StaffRenewalQuote>(QuotePath(enrollment, plan)))!;

    [Fact]
    public async Task Renewal_search_and_review_show_the_active_period_before_a_paid_future_period()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail);
        var enrollment = await Enrollment();
        await using var scope = Scope(); var db = Db(scope);
        var code = await db.SportEnrollments.Where(e => e.Id == enrollment).Select(e => e.Player.PlayerCode).SingleAsync();
        var active = await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == enrollment &&
            p.EndDate != null && p.Status == SubscriptionPeriodStatus.Active)
            .OrderByDescending(p => p.EndDate).FirstAsync();
        var original = (active.StartDate, active.EndDate, active.UpdatedAtUtc);
        var otherPeriods = await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == enrollment && p.Id != active.Id && p.Status != SubscriptionPeriodStatus.Cancelled).ToListAsync();
        var originalStatuses = otherPeriods.Select(p => (Period: p, p.Status)).ToArray();
        var today = new DateOnly(2026, 9, 28);
        try
        {
            active.StartDate = today.AddDays(-30);
            active.EndDate = today.AddDays(14);
            foreach (var period in otherPeriods) period.Status = SubscriptionPeriodStatus.Cancelled;
            await db.SaveChangesAsync();
            var payment = await Created();
            await Simulate(payment.PaymentId, "Success");
            var paid = await db.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.Collection!.PaymentRequestId == payment.PaymentId);
            Assert.Equal(SubscriptionPeriodStatus.Scheduled, paid.Status);
            async Task AssertDisplay(Guid id, string status)
            {
                var search = (await Search(code)).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == enrollment);
                foreach (var period in new[] { search.GetProperty("period"), (await Details()).GetProperty("current") })
                {
                    Assert.Equal(id, period.GetProperty("id").GetGuid());
                    Assert.Equal(status, period.GetProperty("status").GetString());
                }
            }
            await AssertDisplay(active.Id, "Active");
            Assert.Equal(paid.EndDate!.Value.AddDays(1), (await ReadQuote(owner, enrollment, Slice3DemoSeed.FootballMonthlyId)).Start);
            active.EndDate = today.AddDays(-2);
            active.UpdatedAtUtc = paid.UpdatedAtUtc.AddHours(1);
            await db.SaveChangesAsync();
            await AssertDisplay(paid.Id, "Scheduled");
        }
        finally
        {
            (active.StartDate, active.EndDate, active.UpdatedAtUtc) = original;
            foreach (var (period, status) in originalStatuses) period.Status = status;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Existing_renewal_can_be_resumed_only_with_authorized_payment_read()
    {
        var enrollment = await Enrollment();
        var created = await Created();
        using var owner = await Staff(DemoSeed.OwnerEmail);
        var path = $"/api/v1/subscriptions/admin-renewals/enrollments/{enrollment}";
        var paymentPath = $"/api/v1/subscriptions/payments/{created.PaymentId}";
        var details = await owner.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal("Single", details.GetProperty("openRenewal").GetProperty("kind").GetString());
        Assert.Equal(created.PaymentId, details.GetProperty("openRenewal").GetProperty("paymentId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Create(owner)).StatusCode);
        Assert.Equal(0, await Count<PaymentCollection>(c => c.PaymentRequestId == created.PaymentId));

        var email = $"renewal-scope.{Guid.NewGuid():N}@example.test";
        using var registered = await Post(owner, "/api/v1/manage/staff",
            new StaffWrite("إداري اختبار استكمال التجديد", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, "اختبار"), null);
        registered.EnsureSuccessStatusCode();
        var member = (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var admin = await Staff(email);
        async Task Grant(bool canReadPayments, bool canReadSubscriptions = true, bool canCollect = false)
        {
            var actions = StaffAccessRules.Modules.Select(module => new StaffActionValue(module,
                module == "subscriptions" && canReadSubscriptions || module == "payments" && canReadPayments,
                module == "payments" && canCollect, false, false)).ToArray();
            using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/manage/staff/{member}/access")
            { Content = JsonContent.Create(new StaffAccessWrite(true, [], actions)) };
            request.Headers.Add("X-CSRF-TOKEN", (await owner.GetFromJsonAsync<Csrf>("/api/v1/auth/csrf"))!.Token);
            (await owner.SendAsync(request)).EnsureSuccessStatusCode();
        }
        await Grant(false);
        var restricted = await admin.GetFromJsonAsync<JsonElement>(path);
        Assert.False(restricted.GetProperty("canReadPayment").GetBoolean());
        Assert.Equal("Single", restricted.GetProperty("openRenewal").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, restricted.GetProperty("openRenewal").GetProperty("paymentId").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(paymentPath)).StatusCode);
        const string queue = "/api/v1/manage/payment-requests?type=Renewal&allDates=true&status=Pending";
        async Task<bool> CanOpen() => Assert.Single((await admin.GetFromJsonAsync<JsonElement>(queue))
            .GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetGuid() == created.PaymentId)
            .GetProperty("canOpen").GetBoolean();
        await Grant(true, canReadSubscriptions: false);
        Assert.False(await CanOpen());
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(paymentPath)).StatusCode);
        await Grant(true);
        var allowed = await admin.GetFromJsonAsync<JsonElement>(path);
        Assert.True(allowed.GetProperty("canReadPayment").GetBoolean());
        Assert.Equal(created.PaymentId, allowed.GetProperty("openRenewal").GetProperty("paymentId").GetGuid());
        Assert.True(await CanOpen());
        var readOnly = await admin.GetFromJsonAsync<JsonElement>(paymentPath);
        foreach (var action in new[] { "canCollect", "canCollectCash", "canReviewProof", "canRetry", "canSimulate", "canEditDiscount" })
            Assert.False(readOnly.GetProperty(action).GetBoolean(), action);
        var collectionPath = $"/api/v1/manage/payments/{created.PaymentId}/collect";
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, collectionPath, new { paymentMethod = "Online" }, null)).StatusCode);
        await Grant(true, canCollect: true);
        var collecting = await admin.GetFromJsonAsync<JsonElement>(paymentPath);
        Assert.True(collecting.GetProperty("canCollect").GetBoolean());
        Assert.False(collecting.GetProperty("canCollectCash").GetBoolean());
        Assert.False(collecting.GetProperty("canReviewProof").GetBoolean());
        await Grant(false);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(queue)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, collectionPath, new { paymentMethod = "Online" }, null)).StatusCode);
        Assert.Equal(0, await Count<PaymentCollection>(c => c.PaymentRequestId == created.PaymentId));
        using var foreign = await Staff(DemoSeed.FutureOwnerEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(paymentPath)).StatusCode);
    }
    [Fact] public async Task Concurrent_open_renewals_create_only_one_request() { using var first = await Staff(DemoSeed.OwnerEmail); using var second = await Staff(DemoSeed.AdminEmail); var responses = await Task.WhenAll(Create(first), Create(second)); Assert.Single(responses, x => x.IsSuccessStatusCode); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict); }
    [Fact] public async Task Creation_persists_renewal_request() { var created = await Created(); Assert.Equal(1, await Count<RenewalRequest>(x => x.Id == created.RenewalId)); }
    [Fact] public async Task Creation_persists_payment_request() { var created = await Created(); Assert.Equal(1, await Count<PaymentRequest>(x => x.Id == created.PaymentId)); }
    [Fact] public async Task Creation_does_not_create_collection() { var created = await Created(); Assert.Equal(0, await Count<PaymentCollection>(x => x.PaymentRequestId == created.PaymentId)); }
    [Fact] public async Task Creation_does_not_create_subscription_period() { var created = await Created(); Assert.Equal(0, await Count<SubscriptionPeriod>(x => x.Collection.PaymentRequestId == created.PaymentId)); }
    [Fact] public async Task Server_uses_plan_amount_and_currency() { var created = await Created(); await using var scope = Scope(); var p = await Db(scope).PaymentRequests.Include(x => x.RenewalRequest).SingleAsync(x => x.Id == created.PaymentId); Assert.Equal(900m, p.RenewalRequest.OriginalAmount); Assert.Equal(p.RenewalRequest.FinalAmount, p.Amount); Assert.InRange(p.Amount, 0.01m, 900m); Assert.Equal("EGP", p.Currency); }
    [Fact] public async Task Renewal_records_staff_actor() { var created = await Created(); await using var scope = Scope(); var renewal = await Db(scope).RenewalRequests.SingleAsync(x => x.Id == created.RenewalId); var owner = await Db(scope).Users.Where(x => x.NormalizedEmail == DemoSeed.OwnerEmail.ToUpperInvariant()).Select(x => x.Id).SingleAsync(); Assert.Equal(owner, renewal.RequestedByUserId); }
    [Fact] public async Task Demo_success_confirms_payment() { var created = await Created(); await Simulate(created.PaymentId, "Success"); await using var scope = Scope(); Assert.Equal(PaymentRequestStatus.Confirmed, await Db(scope).PaymentRequests.Where(x => x.Id == created.PaymentId).Select(x => x.Status).SingleAsync()); }
    [Fact] public async Task Demo_success_creates_one_collection() { var created = await Created(); await Simulate(created.PaymentId, "Success"); Assert.Equal(1, await Count<PaymentCollection>(x => x.PaymentRequestId == created.PaymentId)); }
    [Fact] public async Task Demo_success_creates_one_receipt() { var created = await Created(); await Simulate(created.PaymentId, "Success"); Assert.Equal(1, await Count<Receipt>(x => x.Collection.PaymentRequestId == created.PaymentId)); }
    [Fact] public async Task Demo_success_creates_one_period()
    {
        var created=await Created();using var owner=await Staff(DemoSeed.OwnerEmail);
        var pending=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/payments/{created.PaymentId}");
        Assert.Equal(await Enrollment(),pending.GetProperty("sportEnrollmentId").GetGuid());Assert.Equal(Slice3DemoSeed.FootballMonthlyId,pending.GetProperty("subscriptionPlanId").GetGuid());
        Assert.Equal(JsonValueKind.Null,pending.GetProperty("periodId").ValueKind);Assert.Equal(JsonValueKind.Null,pending.GetProperty("receiptId").ValueKind);
        await Simulate(created.PaymentId,"Success");Assert.Equal(1,await Count<SubscriptionPeriod>(p=>p.Collection.PaymentRequestId==created.PaymentId));
        var saved=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/payments/{created.PaymentId}");
        var period=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/periods/{saved.GetProperty("periodId").GetGuid()}");
        Assert.Equal(saved.GetProperty("sportEnrollmentId").GetGuid(),period.GetProperty("sportEnrollmentId").GetGuid());
        Assert.Equal(saved.GetProperty("subscriptionPlanId").GetGuid(),period.GetProperty("subscriptionPlanId").GetGuid());
        Assert.Equal(saved.GetProperty("receiptId").GetGuid(),period.GetProperty("receiptId").GetGuid());
        var receipt=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/receipts/{saved.GetProperty("receiptId").GetGuid()}");Assert.Equal(saved.GetProperty("finalAmount").GetDecimal(),receipt.GetProperty("amount").GetDecimal());
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync($"/api/v1/subscriptions/payments/{created.PaymentId}")).StatusCode);
    }
    [Fact] public async Task Demo_failure_creates_no_collection() { var created = await Created(); await Simulate(created.PaymentId, "Failed"); Assert.Equal(0, await Count<PaymentCollection>(x => x.PaymentRequestId == created.PaymentId)); }
    [Fact] public async Task Approved_manual_renewal_uses_exact_decimal_string_and_refuses_changed_amount_before_collection()
    {
        var created=await Created();using var owner=await Staff(DemoSeed.OwnerEmail);using var foreign=await Staff(DemoSeed.FutureOwnerEmail);
        var pending=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/payments/{created.PaymentId}");
        var amount=pending.GetProperty("finalAmount").GetDecimal();var url=$"/api/v1/manage/payments/{created.PaymentId}/collect";
        var changed=(amount+1).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,url,new{paymentMethod="Online",expectedAmount=changed},null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,url,new{paymentMethod="Online",expectedAmount="0"},null)).StatusCode);
        Assert.Equal(0,await Count<PaymentCollection>(c=>c.PaymentRequestId==created.PaymentId));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(owner,url,new{paymentMethod="Online",expectedAmount="1,000"},null)).StatusCode);
        var body=new{paymentMethod="Online",expectedAmount=amount.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)};
        Assert.Equal(HttpStatusCode.NotFound,(await Post(foreign,url,body,null)).StatusCode);
        var first=await Post(owner,url,body,null);first.EnsureSuccessStatusCode();
        var replay=await Post(owner,url,body,null);replay.EnsureSuccessStatusCode();
        var saved=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/payments/{created.PaymentId}");
        var period=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/periods/{saved.GetProperty("periodId").GetGuid()}");
        var receipt=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/receipts/{saved.GetProperty("receiptId").GetGuid()}");
        Assert.Equal(amount,receipt.GetProperty("amount").GetDecimal());
        Assert.Equal("Online",receipt.GetProperty("paymentMethod").GetString());
        Assert.Equal(pending.GetProperty("providerReference").GetString(), receipt.GetProperty("providerReference").GetString());
        Assert.False(string.IsNullOrWhiteSpace(receipt.GetProperty("providerReference").GetString()));
        Assert.Equal(saved.GetProperty("receiptId").GetGuid(),period.GetProperty("receiptId").GetGuid());
        Assert.Equal(1,await Count<PaymentCollection>(c=>c.PaymentRequestId==created.PaymentId));
        Assert.Equal(1,await Count<SubscriptionPeriod>(p=>p.Collection.PaymentRequestId==created.PaymentId));
    }
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
    private async Task<HttpClient> Staff(string email) { var c = factory.CreateClient(); if (email == DemoSeed.CoachEmail) await MobileCoachTestSession.SignInAsync(c, factory.Services); else (await Post(c, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" }, null)).EnsureSuccessStatusCode(); return c; }
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
        await db.SubscriptionPeriods.Where(x => x.CollectionId.HasValue && collections.Contains(x.CollectionId.Value)).ExecuteDeleteAsync();
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

    // These cases exercise InternalTest, not manual receipt review. Other test suites
    // can configure the shared synthetic academy's transfer route; preserve that state.
    private async Task SuspendManualTransferRoute()
    {
        await using var scope = Scope(); var db = Db(scope);
        var settings = await db.AcademyTransferSettings.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.IsActive).ToListAsync();
        foreach (var setting in settings) { suspendedTransferSettings.Add(setting.Id); setting.IsActive = false; }
        await db.SaveChangesAsync();
    }

    private async Task RestoreManualTransferRoute()
    {
        if (suspendedTransferSettings.Count == 0) return;
        await using var scope = Scope(); var db = Db(scope);
        await db.AcademyTransferSettings.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && suspendedTransferSettings.Contains(x.Id)).ExecuteUpdateAsync(x => x.SetProperty(s => s.IsActive, true));
    }

    private sealed record Csrf(string Token);
    private sealed record Challenge(Guid ChallengeId);
    private sealed record CreatedDto(Guid RenewalId, Guid PaymentId);
    private sealed class Factory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Demo"); builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Demo:SeedProfile", "LegacyRegression"); builder.UseSetting("Demo:SeedEnabled", "true"); builder.UseSetting("Demo:FixedOtpEnabled", "true");
            builder.UseSetting("Demo:FixedOtp", "246810"); builder.UseSetting("Demo:StaffPassword", "Demo-Only-123!");
            builder.UseSetting("Demo:ReferenceDate", "2026-09-28"); builder.UseSetting("Payments:InternalTest:Enabled", "true");
            builder.UseSetting("Payments:InternalTest:SigningKey", "Demo-Test-Signing-Key-Only-123456");
        }
    }
}
