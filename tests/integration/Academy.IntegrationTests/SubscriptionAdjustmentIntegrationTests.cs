using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Api.Slice4;
using Academy.Api.Slice8;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class SubscriptionAdjustmentIntegrationTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 9, 28);
    private readonly string connection = Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("ACADEMY_TEST_CONNECTION_STRING is required.");
    private readonly List<Guid> createdPlayerIds = [];
    private readonly List<Guid> createdPlanIds = [];
    private readonly List<Guid> createdSessionIds = [];
    private Factory factory = null!;

    public Task InitializeAsync() { factory = new Factory(connection); _ = factory.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync()
    {
        if (createdPlayerIds.Count > 0)
        {
            await using var scope = Scope(); var db = Db(scope); var players = createdPlayerIds.ToArray();
            var enrollments = await db.SportEnrollments.Where(x => players.Contains(x.PlayerId)).Select(x => x.Id).ToArrayAsync();
            var periods = await db.SubscriptionPeriods.Where(x => enrollments.Contains(x.SportEnrollmentId)).Select(x => x.Id).ToArrayAsync();
            var renewals = await db.RenewalRequests.Where(x => enrollments.Contains(x.SportEnrollmentId)).Select(x => x.Id).ToArrayAsync();
            var payments = await db.PaymentRequests.Where(x => renewals.Contains(x.RenewalRequestId)).Select(x => x.Id).ToArrayAsync();
            var collections = await db.Collections.Where(x => enrollments.Contains(x.SportEnrollmentId)).Select(x => x.Id).ToArrayAsync();
            await db.SubscriptionSessionMovements.Where(x => periods.Contains(x.SubscriptionPeriodId)).ExecuteDeleteAsync();
            await db.PlayerAttendances.Where(x => enrollments.Contains(x.SportEnrollmentId)).ExecuteDeleteAsync();
            if (createdSessionIds.Count > 0) await db.TrainingSessions.Where(x => createdSessionIds.Contains(x.Id)).ExecuteDeleteAsync();
            await db.SubscriptionAdjustments.Where(x => periods.Contains(x.SubscriptionPeriodId)).ExecuteDeleteAsync();
            await db.SubscriptionPeriods.Where(x => periods.Contains(x.Id)).ExecuteDeleteAsync();
            await db.Receipts.Where(x => collections.Contains(x.CollectionId)).ExecuteDeleteAsync();
            await db.Collections.Where(x => collections.Contains(x.Id)).ExecuteDeleteAsync();
            await db.PaymentProviderEvents.Where(x => payments.Contains(x.PaymentRequestId)).ExecuteDeleteAsync();
            await db.RenewalRequests.Where(x => renewals.Contains(x.Id)).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.PaymentRequestId, (Guid?)null));
            await db.PaymentRequests.Where(x => payments.Contains(x.Id)).ExecuteDeleteAsync();
            await db.RenewalRequests.Where(x => renewals.Contains(x.Id)).ExecuteDeleteAsync();
            await db.GuardianPlayerLinks.Where(x => players.Contains(x.PlayerId)).ExecuteDeleteAsync();
            await db.SportEnrollments.Where(x => enrollments.Contains(x.Id)).ExecuteDeleteAsync();
            await db.SubscriptionPlans.Where(x => createdPlanIds.Contains(x.Id)).ExecuteDeleteAsync();
            await db.Players.Where(x => players.Contains(x.Id)).ExecuteDeleteAsync();
        }
        await factory.DisposeAsync();
    }

    [Fact] public async Task Owner_can_freeze_active_duration_period()
    {
        var s = await NewScenario(); using var c = await Staff(DemoSeed.OwnerEmail);
        var response = await Adjust(c, s, "freeze", new { effectiveDate = "2026-09-27", reason = "إيقاف مؤقت", expectedVersion = s.Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact] public async Task Admin_can_freeze_active_duration_period()
    {
        var s = await NewScenario(); using var c = await Staff(DemoSeed.AdminEmail);
        Assert.Equal(HttpStatusCode.OK, (await Adjust(c, s, "freeze", new { effectiveDate = "2026-09-27", reason = "قرار إداري", expectedVersion = s.Version })).StatusCode);
    }

    [Fact] public async Task Coach_cannot_freeze()
    {
        var s = await NewScenario(); using var c = await Staff(DemoSeed.CoachEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await Adjust(c, s, "freeze", new { effectiveDate = "2026-09-27", reason = "غير مصرح", expectedVersion = s.Version })).StatusCode);
    }

    [Fact] public async Task Guardian_cannot_mutate_adjustments()
    {
        var s = await NewScenario(linkGuardian: true); using var c = await Guardian();
        Assert.Equal(HttpStatusCode.Forbidden, (await Adjust(c, s, "freeze", new { effectiveDate = "2026-09-27", reason = "غير مصرح", expectedVersion = s.Version })).StatusCode);
    }

    [Fact] public async Task Academy_A_cannot_adjust_Academy_B_period()
    {
        var s = await NewScenario(academyId: DemoSeed.FutureAcademyId); using var c = await Staff(DemoSeed.OwnerEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await Adjust(c, s, "freeze", new { effectiveDate = "2026-09-27", reason = "عزل", expectedVersion = s.Version })).StatusCode);
    }

    [Fact] public async Task Freeze_creates_exactly_one_adjustment()
    {
        var s = await NewScenario(); await Freeze(s);
        Assert.Equal(1, await AdjustmentCount(s, SubscriptionAdjustmentType.FreezeStarted));
    }

    [Fact] public async Task Duplicate_freeze_command_is_idempotent()
    {
        var s = await NewScenario(); var key = Guid.NewGuid().ToString(); var first = await Freeze(s, key); var second = await Freeze(s, key);
        Assert.False(first.Replay); Assert.True(second.Replay); Assert.Equal(first.AdjustmentId, second.AdjustmentId); Assert.Equal(1, await AdjustmentCount(s, SubscriptionAdjustmentType.FreezeStarted));
    }

    [Fact] public async Task Frozen_period_status_and_start_are_persisted()
    {
        var s = await NewScenario(); await Freeze(s); var p = await Period(s);
        Assert.Equal(SubscriptionPeriodStatus.Frozen, p.Status); Assert.Equal(new DateOnly(2026, 9, 27), p.FrozenFromDate); Assert.Equal(s.EndDate, p.EndDate);
    }

    [Fact] public async Task Frozen_period_attendance_records_without_session_consumption()
    {
        var s = await NewScenario(SubscriptionPlanType.Combined); await Freeze(s); var before = (await Period(s)).RemainingSessions;
        var result = await Attend(s, Today);
        Assert.False(result.SessionConsumed); Assert.Equal("تم تسجيل الحضور دون خصم: الاشتراك مجمد أو غير مؤهل.", result.Warning); Assert.Equal(before, (await Period(s)).RemainingSessions);
    }

    [Fact] public async Task Resume_extends_end_by_exact_frozen_calendar_days()
    {
        var s = await NewScenario(); await Freeze(s); var frozen = await Snapshot(s); var result = await Resume(s, frozen.Version, Today);
        Assert.Equal(s.EndDate.AddDays(1), result.EndDate);
    }

    [Fact] public async Task Resume_creates_linked_freeze_ended_audit()
    {
        var s = await NewScenario(); var start = await Freeze(s); var frozen = await Snapshot(s); await Resume(s, frozen.Version, Today);
        await using var scope = Scope(); var end = await Db(scope).SubscriptionAdjustments.SingleAsync(x => x.SubscriptionPeriodId == s.PeriodId && x.AdjustmentType == SubscriptionAdjustmentType.FreezeEnded);
        Assert.Equal(start.AdjustmentId, end.RelatedAdjustmentId); Assert.Equal(1, end.DaysDelta);
    }

    [Fact] public async Task Duplicate_resume_command_is_idempotent()
    {
        var s = await NewScenario(); await Freeze(s); var frozen = await Snapshot(s); var key = Guid.NewGuid().ToString();
        var first = await Resume(s, frozen.Version, Today, key); var second = await Resume(s, frozen.Version, Today, key);
        Assert.True(second.Replay); Assert.Equal(first.AdjustmentId, second.AdjustmentId); Assert.Equal(1, await AdjustmentCount(s, SubscriptionAdjustmentType.FreezeEnded));
    }

    [Fact] public async Task Add_five_days_changes_end_by_exactly_five()
    {
        var s = await NewScenario(); var result = await Days(s, 5, true);
        Assert.Equal(s.EndDate.AddDays(5), result.EndDate); Assert.Equal(5, result.DaysDelta);
    }

    [Fact] public async Task Deduct_three_days_changes_end_by_exactly_three()
    {
        var s = await NewScenario(); var result = await Days(s, 3, false);
        Assert.Equal(s.EndDate.AddDays(-3), result.EndDate); Assert.Equal(-3, result.DaysDelta);
    }

    [Fact] public async Task Deduct_cannot_move_end_before_start()
    {
        var s = await NewScenario(end: Today.AddDays(2));
        var error = await Assert.ThrowsAsync<AdjustmentValidationException>(() => Days(s, 60, false));
        Assert.Contains("قبل تاريخ البداية", error.Message);
    }

    [Fact] public async Task Sessions_only_rejects_day_adjustment_with_Arabic_message()
    {
        var s = await NewScenario(SubscriptionPlanType.Sessions);
        var error = await Assert.ThrowsAsync<AdjustmentValidationException>(() => Days(s, 1, true));
        Assert.Equal("هذه الباقة تعتمد على عدد الحصص ولا تحتوي مدة زمنية قابلة للتعديل.", error.Message);
    }

    [Fact] public async Task Sessions_only_rejects_freeze_with_Arabic_message()
    {
        var s = await NewScenario(SubscriptionPlanType.Sessions);
        var error = await Assert.ThrowsAsync<AdjustmentValidationException>(() => Freeze(s));
        Assert.Equal("تجميد الباقة بالحصة فقط غير مدعوم في هذا الإصدار.", error.Message);
    }

    [Fact] public async Task Combined_add_days_keeps_remaining_sessions()
    {
        var s = await NewScenario(SubscriptionPlanType.Combined); var before = (await Period(s)).RemainingSessions; await Days(s, 5, true);
        Assert.Equal(before, (await Period(s)).RemainingSessions);
    }

    [Fact] public async Task Combined_deduct_days_keeps_remaining_sessions()
    {
        var s = await NewScenario(SubscriptionPlanType.Combined); var before = (await Period(s)).RemainingSessions; await Days(s, 3, false);
        Assert.Equal(before, (await Period(s)).RemainingSessions);
    }

    [Fact] public async Task Combined_freeze_does_not_reset_sessions()
    {
        var s = await NewScenario(SubscriptionPlanType.Combined); var before = (await Period(s)).RemainingSessions; await Freeze(s);
        Assert.Equal(before, (await Period(s)).RemainingSessions);
    }

    [Fact] public async Task Cancel_sets_only_selected_period_cancelled()
    {
        var s = await NewScenario(); await Cancel(s); Assert.Equal(SubscriptionPeriodStatus.Cancelled, (await Period(s)).Status);
    }

    [Fact] public async Task Cancellation_preserves_collection()
    {
        var s = await NewScenario(); var before = await CollectionSnapshot(s); await Cancel(s); Assert.Equal(before, await CollectionSnapshot(s));
    }

    [Fact] public async Task Cancellation_preserves_receipt()
    {
        var s = await NewScenario(); await Cancel(s); await using var scope = Scope(); Assert.True(await Db(scope).Receipts.AnyAsync(x => x.Id == s.ReceiptId && x.CollectionId == s.CollectionId));
    }

    [Fact] public async Task Cancellation_preserves_attendance()
    {
        var s = await NewScenario(SubscriptionPlanType.Combined); await Attend(s, Today); await Cancel(s); await using var scope = Scope();
        Assert.True(await Db(scope).PlayerAttendances.AnyAsync(x => x.SportEnrollmentId == s.EnrollmentId && x.Status == AttendanceStatus.Present));
    }

    [Fact] public async Task Cancellation_preserves_session_movements()
    {
        var s = await NewScenario(SubscriptionPlanType.Combined); await Attend(s, Today); var before = await MovementCount(s); await Cancel(s);
        Assert.Equal(before, await MovementCount(s)); Assert.Equal(1, before);
    }

    [Fact] public async Task Cancellation_does_not_cancel_later_paid_period()
    {
        var s = await NewScenario(); var later = await AddPaidPeriod(s, Today.AddDays(70), Today.AddDays(129), SubscriptionPeriodStatus.Scheduled); await Cancel(s);
        Assert.Equal(SubscriptionPeriodStatus.Scheduled, (await Period(later)).Status);
    }

    [Fact] public async Task Cancelled_period_is_excluded_from_attendance_eligibility()
    {
        var s = await NewScenario(SubscriptionPlanType.Combined); await Cancel(s); var result = await Attend(s, Today);
        Assert.False(result.SessionConsumed); Assert.Equal(10, (await Period(s)).RemainingSessions);
    }

    [Fact] public async Task Cancelled_period_does_not_block_new_renewal()
    {
        var s = await NewScenario(linkGuardian: true); await Cancel(s); var payment = await Renew(s); await Simulate(payment);
        await using var scope = Scope(); var renewed = await Db(scope).SubscriptionPeriods.SingleAsync(x => x.Collection.PaymentRequestId == payment);
        Assert.Equal(Today, renewed.StartDate); Assert.Equal(SubscriptionPeriodStatus.Cancelled, (await Period(s)).Status);
    }

    [Fact] public async Task Early_renewal_uses_adjusted_end_date()
    {
        var s = await NewScenario(linkGuardian: true); await Days(s, 5, true); var payment = await Renew(s); await Simulate(payment);
        await using var scope = Scope(); var renewed = await Db(scope).SubscriptionPeriods.SingleAsync(x => x.Collection.PaymentRequestId == payment);
        Assert.Equal(s.EndDate.AddDays(6), renewed.StartDate);
    }

    [Fact] public async Task Adjustment_keeps_old_paid_period_and_history()
    {
        var s = await NewScenario(); await Days(s, 5, true); await using var scope = Scope(); var db = Db(scope);
        Assert.True(await db.SubscriptionPeriods.AnyAsync(x => x.Id == s.PeriodId && x.CollectionId == s.CollectionId)); Assert.True(await db.SubscriptionAdjustments.AnyAsync(x => x.SubscriptionPeriodId == s.PeriodId));
    }

    [Fact] public async Task Concurrent_adjustments_do_not_silently_lose_update()
    {
        var s = await NewScenario();
        async Task<bool> Run(string key) { try { await Days(s, 5, true, s.Version, key); return true; } catch (AdjustmentConflictException) { return false; } }
        var outcomes = await Task.WhenAll(Run(Guid.NewGuid().ToString()), Run(Guid.NewGuid().ToString()));
        Assert.Single(outcomes, x => x); Assert.Equal(s.EndDate.AddDays(5), (await Period(s)).EndDate); Assert.Equal(1, await AdjustmentCount(s, SubscriptionAdjustmentType.DaysAdded));
    }

    [Fact] public async Task Adjustment_reason_is_required_and_trimmed()
    {
        var s = await NewScenario(); await Assert.ThrowsAsync<AdjustmentValidationException>(() => Days(s, 1, true, reason: "   "));
        await Days(s, 1, true, reason: "  سبب واضح  "); await using var scope = Scope(); Assert.Equal("سبب واضح", await Db(scope).SubscriptionAdjustments.Where(x => x.SubscriptionPeriodId == s.PeriodId).Select(x => x.Reason).SingleAsync());
    }

    [Fact] public async Task Audit_performer_and_timestamp_are_stored()
    {
        var s = await NewScenario(); var owner = await OwnerId(DemoSeed.NogoomAcademyId); await Days(s, 1, true); await using var scope = Scope(); var row = await Db(scope).SubscriptionAdjustments.SingleAsync(x => x.SubscriptionPeriodId == s.PeriodId);
        Assert.Equal(owner, row.PerformedByUserId); Assert.NotEqual(default, row.PerformedAtUtc);
    }

    [Fact] public async Task Freeze_and_day_adjustments_do_not_change_financial_records()
    {
        var freeze = await NewScenario(); var beforeFreeze = await CollectionSnapshot(freeze); await Freeze(freeze); Assert.Equal(beforeFreeze, await CollectionSnapshot(freeze));
        var days = await NewScenario(); var beforeDays = await CollectionSnapshot(days); await Days(days, 5, true); var latest = await Snapshot(days); await Days(days, 2, false, latest.Version); Assert.Equal(beforeDays, await CollectionSnapshot(days));
    }

    [Fact] public async Task Cancellation_does_not_change_revenue_or_create_reversal()
    {
        var s = await NewScenario(); using var c = await Staff(DemoSeed.OwnerEmail); var before = await OwnerSummary(c); await Cancel(s); var after = await OwnerSummary(c);
        Assert.Equal(before.GetProperty("collections").GetProperty("total").GetDecimal(), after.GetProperty("collections").GetProperty("total").GetDecimal()); Assert.Equal(0, await NegativeCollectionCount(s));
    }

    [Fact] public async Task Active_subscription_metric_drops_after_cancellation()
    {
        var s = await NewScenario(); using var c = await Staff(DemoSeed.OwnerEmail); var before = await OwnerSummary(c); await Cancel(s); var after = await OwnerSummary(c);
        Assert.Equal(before.GetProperty("subscriptions").GetProperty("active").GetInt32() - 1, after.GetProperty("subscriptions").GetProperty("active").GetInt32());
    }

    [Fact] public async Task Guardian_sees_safe_frozen_state_without_internal_reason_or_employee()
    {
        var s = await NewScenario(linkGuardian: true); await Freeze(s); using var c = await Guardian(); var json = await c.GetStringAsync($"/api/v1/guardian/subscriptions/periods/{s.PeriodId}/adjustments");
        Assert.Contains("Frozen", json); Assert.DoesNotContain("إيقاف للاختبار", json); Assert.DoesNotContain("performedBy", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("reason", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public async Task Guardian_cannot_read_unlinked_period_adjustments()
    {
        var s = await NewScenario(); using var c = await Guardian(); Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/guardian/subscriptions/periods/{s.PeriodId}/adjustments")).StatusCode);
    }

    [Fact] public async Task Client_cannot_override_end_status_balance_or_financial_links()
    {
        var s = await NewScenario(SubscriptionPlanType.Combined); using var c = await Staff(DemoSeed.OwnerEmail);
        var response = await Adjust(c, s, "days", new { direction = 1, days = 1, reason = "اختبار الحقول", expectedVersion = s.Version, newEndDate = "2099-12-31", status = "Cancelled", remainingSessions = 999, collectionId = Guid.NewGuid() }); response.EnsureSuccessStatusCode();
        var p = await Period(s); Assert.Equal(s.EndDate.AddDays(1), p.EndDate); Assert.Equal(SubscriptionPeriodStatus.Active, p.Status); Assert.Equal(10, p.RemainingSessions); Assert.Equal(s.CollectionId, p.CollectionId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    [InlineData(int.MaxValue)]
    public async Task Invalid_or_absurd_day_amount_is_rejected(int days)
    {
        var s = await NewScenario(); await Assert.ThrowsAsync<AdjustmentValidationException>(() => Days(s, days, true));
    }

    [Fact] public async Task Adjustment_history_has_no_edit_or_delete_endpoint()
    {
        var s = await NewScenario(); var result = await Days(s, 1, true); using var c = await Staff(DemoSeed.OwnerEmail); var csrf = await Csrf(c);
        var put = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/subscriptions/adjustments/{result.AdjustmentId}") { Content = JsonContent.Create(new { reason = "rewrite" }) }; put.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(put)).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/v1/subscriptions/adjustments/{result.AdjustmentId}")).StatusCode);
    }

    [Fact] public async Task Resume_enables_future_consumption_without_retroactive_consumption()
    {
        var s = await NewScenario(SubscriptionPlanType.Combined); await Freeze(s); var during = await Attend(s, Today.AddDays(-1)); Assert.False(during.SessionConsumed); var frozen = await Snapshot(s); await Resume(s, frozen.Version, Today); var later = await Attend(s, Today.AddDays(1));
        Assert.True(later.SessionConsumed); Assert.Equal(9, (await Period(s)).RemainingSessions); Assert.Equal(1, await MovementCount(s));
    }

    private async Task<Scenario> NewScenario(SubscriptionPlanType type = SubscriptionPlanType.Duration, SubscriptionPeriodStatus status = SubscriptionPeriodStatus.Active, bool linkGuardian = false, Guid? academyId = null, DateOnly? end = null)
    {
        var academy = academyId ?? DemoSeed.NogoomAcademyId; var now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero); var root = Guid.NewGuid();
        await using var scope = Scope(); var db = Db(scope); var group = await db.TrainingGroups.AsNoTracking().FirstAsync(x => x.AcademyId == academy); var actor = await db.AcademyMemberships.Where(x => x.AcademyId == academy && x.Role == AcademyRole.AcademyOwner).Select(x => x.UserId).SingleAsync();
        var player = new Player { Id = Guid.NewGuid(), AcademyId = academy, PlayerCode = $"T-{root:N}"[..18], ArabicName = "لاعب اختبار تعديلات", DateOfBirth = new DateOnly(2016, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now };
        var enrollment = new SportEnrollment { Id = Guid.NewGuid(), AcademyId = academy, PlayerId = player.Id, SportId = group.SportId, BranchId = group.BranchId, TrainingGroupId = group.Id, Status = EnrollmentStatus.Active, CreatedAtUtc = now, UpdatedAtUtc = now };
        var plan = new SubscriptionPlan { Id = Guid.NewGuid(), AcademyId = academy, SportId = group.SportId, ArabicName = $"باقة اختبار {root:N}", PlanType = type, Price = 321m, Currency = "EGP", DurationDays = type == SubscriptionPlanType.Sessions ? null : 60, SessionCount = type == SubscriptionPlanType.Duration ? null : 10, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.AddRange(player, enrollment, plan); await db.SaveChangesAsync();
        createdPlayerIds.Add(player.Id); createdPlanIds.Add(plan.Id);
        if (linkGuardian)
        {
            var guardian = await db.GuardianProfiles.SingleAsync(x => x.AcademyId == academy && x.ContactPhone == DemoSeed.GuardianPhone);
            db.GuardianPlayerLinks.Add(new GuardianPlayerLink { Id = Guid.NewGuid(), AcademyId = academy, GuardianId = guardian.Id, PlayerId = player.Id, RelationshipType = "ولي أمر", CreatedByUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now }); await db.SaveChangesAsync();
        }
        return await AddPaidPeriod(new Scenario(Guid.Empty, enrollment.Id, player.Id, plan.Id, Guid.Empty, Guid.Empty, group.Id, new DateOnly(2026, 9, 1), end ?? new DateOnly(2026, 10, 31), 0), new DateOnly(2026, 9, 1), type == SubscriptionPlanType.Sessions ? null : end ?? new DateOnly(2026, 10, 31), status);
    }

    private async Task<Scenario> AddPaidPeriod(Scenario source, DateOnly start, DateOnly? end, SubscriptionPeriodStatus status)
    {
        var now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero); await using var scope = Scope(); var db = Db(scope);
        var enrollment = await db.SportEnrollments.Include(x => x.Player).Include(x => x.Sport).SingleAsync(x => x.Id == source.EnrollmentId); var plan = await db.SubscriptionPlans.SingleAsync(x => x.Id == source.PlanId); var actor = await db.AcademyMemberships.Where(x => x.AcademyId == enrollment.AcademyId && x.Role == AcademyRole.AcademyOwner).Select(x => x.UserId).SingleAsync();
        var renewalId = Guid.NewGuid(); var paymentId = Guid.NewGuid(); var collectionId = Guid.NewGuid(); var receiptId = Guid.NewGuid(); var periodId = Guid.NewGuid();
        var renewal = new RenewalRequest { Id = renewalId, AcademyId = enrollment.AcademyId, SportEnrollmentId = enrollment.Id, SportId = enrollment.SportId, SubscriptionPlanId = plan.Id, RequestedByUserId = actor, RequestedAtUtc = now, OriginalAmount = plan.Price, FinalAmount = plan.Price, AmountExpected = plan.Price, Currency = plan.Currency, Status = RenewalRequestStatus.Paid, PaymentRequestId = paymentId, IdempotencyKey = Guid.NewGuid().ToString(), CreatedAtUtc = now, UpdatedAtUtc = now };
        var payment = new PaymentRequest { Id = paymentId, AcademyId = enrollment.AcademyId, RenewalRequestId = renewalId, Provider = "Test", ProviderEnvironment = "Demo", Amount = plan.Price, Currency = plan.Currency, Status = PaymentRequestStatus.Confirmed, ProviderReference = $"TEST-{paymentId:N}", CheckoutReference = $"checkout-{paymentId:N}", ConfirmedAtUtc = now, LastProviderEventAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now };
        var collection = new PaymentCollection { Id = collectionId, AcademyId = enrollment.AcademyId, SportEnrollmentId = enrollment.Id, RenewalRequestId = renewalId, PaymentRequestId = paymentId, Amount = plan.Price, Currency = plan.Currency, PaymentMethod = "Test", Provider = "Test", ProviderReference = payment.ProviderReference, ConfirmedAtUtc = now, ConfirmedBy = "integration-test", CreatedAtUtc = now, UpdatedAtUtc = now };
        var receipt = new Receipt { Id = receiptId, AcademyId = enrollment.AcademyId, CollectionId = collectionId, ReceiptNumber = $"TEST-{receiptId:N}", PlayerId = enrollment.PlayerId, SportEnrollmentId = enrollment.Id, SubscriptionPlanId = plan.Id, PlayerNameSnapshot = enrollment.Player.ArabicName, SportNameSnapshot = enrollment.Sport.ArabicName, PlanNameSnapshot = plan.ArabicName, OriginalAmount = plan.Price, FinalAmount = plan.Price, Amount = plan.Price, Currency = plan.Currency, PaidAtUtc = now, PaymentMethod = "Test", ProviderReference = payment.ProviderReference, CreatedAtUtc = now, UpdatedAtUtc = now };
        var period = new SubscriptionPeriod { Id = periodId, AcademyId = enrollment.AcademyId, SportEnrollmentId = enrollment.Id, SportId = enrollment.SportId, SubscriptionPlanId = plan.Id, CollectionId = collectionId, StartDate = start, EndDate = end, InitialSessions = plan.SessionCount, RemainingSessions = plan.SessionCount, Status = status, PriceSnapshot = plan.Price, CurrencySnapshot = plan.Currency, CreatedByUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.AddRange(renewal, payment, collection, receipt, period); await db.SaveChangesAsync(); return source with { PeriodId = periodId, CollectionId = collectionId, ReceiptId = receiptId, StartDate = start, EndDate = end ?? start, Version = period.Version };
    }

    private async Task<SubscriptionAdjustmentResult> Freeze(Scenario s, string? key = null, uint? version = null)
    { await using var scope = Scope(); return await scope.ServiceProvider.GetRequiredService<SubscriptionAdjustmentService>().FreezeAsync(await Academy(s), await OwnerId(await Academy(s)), s.PeriodId, Today.AddDays(-1), "إيقاف للاختبار", version ?? s.Version, key ?? Guid.NewGuid().ToString()); }
    private async Task<SubscriptionAdjustmentResult> Resume(Scenario s, uint version, DateOnly date, string? key = null)
    { await using var scope = Scope(); return await scope.ServiceProvider.GetRequiredService<SubscriptionAdjustmentService>().ResumeAsync(await Academy(s), await OwnerId(await Academy(s)), s.PeriodId, date, "استئناف للاختبار", version, key ?? Guid.NewGuid().ToString()); }
    private async Task<SubscriptionAdjustmentResult> Days(Scenario s, int days, bool add, uint? version = null, string? key = null, string reason = "تعديل أيام للاختبار")
    { await using var scope = Scope(); return await scope.ServiceProvider.GetRequiredService<SubscriptionAdjustmentService>().AdjustDaysAsync(await Academy(s), await OwnerId(await Academy(s)), s.PeriodId, days, add, reason, version ?? s.Version, key ?? Guid.NewGuid().ToString()); }
    private async Task<SubscriptionAdjustmentResult> Cancel(Scenario s, uint? version = null)
    { var expected = version ?? (await Period(s)).Version; await using var scope = Scope(); return await scope.ServiceProvider.GetRequiredService<SubscriptionAdjustmentService>().CancelAsync(await Academy(s), await OwnerId(await Academy(s)), s.PeriodId, Today, "إلغاء إداري للاختبار", expected, Guid.NewGuid().ToString()); }

    private async Task<AttendanceSaveResult> Attend(Scenario s, DateOnly date)
    {
        await using var scope = Scope(); var db = Db(scope); var group = await db.TrainingGroups.SingleAsync(x => x.Id == s.GroupId); var actor = await OwnerId(group.AcademyId); var starts = await db.TrainingSessions.Where(x => x.AcademyId == group.AcademyId && x.TrainingGroupId == group.Id && x.SessionDate == date).Select(x => x.StartTime).ToListAsync(); var start = new TimeOnly(2, 0); while (starts.Contains(start)) start = start.AddMinutes(1);
        var session = new TrainingSession { Id = Guid.NewGuid(), AcademyId = group.AcademyId, TrainingGroupId = group.Id, BranchId = group.BranchId, SportId = group.SportId, AgeCategoryId = group.AgeCategoryId, SessionDate = date, StartTime = start, EndTime = start.AddMinutes(45), Status = TrainingSessionStatus.Scheduled, Source = TrainingSessionSource.Manual, CreatedByUserId = actor, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
        db.TrainingSessions.Add(session); await db.SaveChangesAsync(); createdSessionIds.Add(session.Id); return (await scope.ServiceProvider.GetRequiredService<AttendanceService>().SavePlayersAsync(group.AcademyId, session.Id, actor, [new(s.EnrollmentId, AttendanceStatus.Present)])).Single();
    }

    private async Task<Guid> Renew(Scenario s)
    {
        using var c = await Guardian(); var response = await Post(c, "/api/v1/guardian/subscriptions/renewals", new { sportEnrollmentId = s.EnrollmentId, subscriptionPlanId = s.PlanId }, Guid.NewGuid().ToString()); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<Created>())!.PaymentId;
    }
    private async Task Simulate(Guid payment) { using var c = await Guardian(); (await Post(c, $"/api/v1/guardian/subscriptions/payments/{payment}/simulate", new { outcome = "Success" })).EnsureSuccessStatusCode(); }
    private async Task<HttpClient> Staff(string email) { var c = factory.CreateClient(); (await Post(c, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" })).EnsureSuccessStatusCode(); return c; }
    private async Task<HttpClient> Guardian() { var c = factory.CreateClient(); var request = await Post(c, "/api/v1/auth/guardian/otp/request", new { phoneNumber = DemoSeed.GuardianPhone }); var challenge = await request.Content.ReadFromJsonAsync<Challenge>(); (await Post(c, "/api/v1/auth/guardian/otp/verify", new { challengeId = challenge!.ChallengeId, phoneNumber = DemoSeed.GuardianPhone, code = "246810" })).EnsureSuccessStatusCode(); return c; }
    private static async Task<HttpResponseMessage> Post(HttpClient c, string path, object body, string? key = null) { var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", await Csrf(c)); if (key is not null) request.Headers.Add("Idempotency-Key", key); return await c.SendAsync(request); }
    private static async Task<HttpResponseMessage> Adjust(HttpClient c, Scenario s, string action, object body) => await Post(c, $"/api/v1/subscriptions/periods/{s.PeriodId}/{action}", body, Guid.NewGuid().ToString());
    private static async Task<string> Csrf(HttpClient c) => (await c.GetFromJsonAsync<CsrfDto>("/api/v1/auth/csrf"))!.Token;
    private static async Task<JsonElement> OwnerSummary(HttpClient c) => await c.GetFromJsonAsync<JsonElement>("/api/v1/reports/owner-summary");

    private async Task<SubscriptionPeriod> Period(Scenario s) { await using var scope = Scope(); return await Db(scope).SubscriptionPeriods.AsNoTracking().SingleAsync(x => x.Id == s.PeriodId); }
    private async Task<(uint Version, DateOnly? EndDate)> Snapshot(Scenario s) { var p = await Period(s); return (p.Version, p.EndDate); }
    private async Task<Guid> Academy(Scenario s) { await using var scope = Scope(); return await Db(scope).SubscriptionPeriods.Where(x => x.Id == s.PeriodId).Select(x => x.AcademyId).SingleAsync(); }
    private async Task<Guid> OwnerId(Guid academy) { await using var scope = Scope(); return await Db(scope).AcademyMemberships.Where(x => x.AcademyId == academy && x.Role == AcademyRole.AcademyOwner).Select(x => x.UserId).SingleAsync(); }
    private async Task<int> AdjustmentCount(Scenario s, SubscriptionAdjustmentType type) { await using var scope = Scope(); return await Db(scope).SubscriptionAdjustments.CountAsync(x => x.SubscriptionPeriodId == s.PeriodId && x.AdjustmentType == type); }
    private async Task<int> MovementCount(Scenario s) { await using var scope = Scope(); return await Db(scope).SubscriptionSessionMovements.CountAsync(x => x.SubscriptionPeriodId == s.PeriodId); }
    private async Task<(decimal Amount, int Collections, int Receipts)> CollectionSnapshot(Scenario s) { await using var scope = Scope(); var db = Db(scope); return (await db.Collections.Where(x => x.Id == s.CollectionId).Select(x => x.Amount).SingleAsync(), await db.Collections.CountAsync(x => x.Id == s.CollectionId), await db.Receipts.CountAsync(x => x.CollectionId == s.CollectionId)); }
    private async Task<int> NegativeCollectionCount(Scenario s) { await using var scope = Scope(); return await Db(scope).Collections.CountAsync(x => x.SportEnrollmentId == s.EnrollmentId && x.Amount < 0); }
    private AsyncServiceScope Scope() => factory.Services.CreateAsyncScope();
    private static FoundationDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<FoundationDbContext>();

    private sealed record Scenario(Guid PeriodId, Guid EnrollmentId, Guid PlayerId, Guid PlanId, Guid CollectionId, Guid ReceiptId, Guid GroupId, DateOnly StartDate, DateOnly EndDate, uint Version);
    private sealed record CsrfDto(string Token); private sealed record Challenge(Guid ChallengeId); private sealed record Created(Guid PaymentId);
    private sealed class Factory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Demo"); builder.UseSetting("ConnectionStrings:Default", connection); builder.UseSetting("Demo:SeedEnabled", "true"); builder.UseSetting("Demo:FixedOtpEnabled", "true"); builder.UseSetting("Demo:FixedOtp", "246810"); builder.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); builder.UseSetting("Demo:ReferenceDate", "2026-09-28"); builder.UseSetting("Payments:InternalTest:Enabled", "true"); builder.UseSetting("Payments:InternalTest:SigningKey", "Demo-Test-Signing-Key-Only-123456");
        }
    }
}
