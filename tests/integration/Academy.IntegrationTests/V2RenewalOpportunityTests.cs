using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2PlanPublicationTests
{
    [Fact]
    public async Task FIN_01_twenty_paid_expired_periods_quote_12700_and_zero_overdue_without_creating_debt_or_cash()
    {
        using var owner = await Staff(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var before = (await db.Collections.CountAsync(), await db.ReceivableDocuments.CountAsync(), await db.RenewalRequests.CountAsync(), await db.SubscriptionPeriods.CountAsync());
        await RenewalOpportunityDemoSeed.SeedAsync(db); await RenewalOpportunityDemoSeed.SeedAsync(db);
        Assert.Equal(before, (await db.Collections.CountAsync(), await db.ReceivableDocuments.CountAsync(), await db.RenewalRequests.CountAsync(), await db.SubscriptionPeriods.CountAsync()));
        var path = $"/api/v1/finance/renewal-opportunities?branchId={RenewalOpportunityDemoSeed.BranchId}";
        var report = await owner.GetFromJsonAsync<JsonElement>(path); Assert.Equal(20, report.GetProperty("count").GetInt32()); Assert.Equal(12700, decimal.Parse(report.GetProperty("potential").GetString()!, System.Globalization.CultureInfo.InvariantCulture)); Assert.Equal(0, report.GetProperty("unpricedCount").GetInt32());
        var quotes = report.GetProperty("items").EnumerateArray().Select(r => decimal.Parse(r.GetProperty("quote").GetString()!, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(8, quotes.Count(q => q == 600)); Assert.Equal(5, quotes.Count(q => q == 540)); Assert.Equal(4, quotes.Count(q => q == 700)); Assert.Equal(3, quotes.Count(q => q == 800)); Assert.Equal(12700, quotes.Sum());
        var csv = await owner.GetStringAsync($"/api/v1/finance/renewal-opportunities/export?branchId={RenewalOpportunityDemoSeed.BranchId}&quoteHash={report.GetProperty("quoteHash").GetString()}"); Assert.Equal(21, csv.Split('\n').Length); Assert.Contains("540.00", csv);
        var debt = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/receivables?branchId={RenewalOpportunityDemoSeed.BranchId}"); Assert.Equal("0.00", debt.GetProperty("overdue").GetString()); Assert.Equal(0, debt.GetProperty("count").GetInt32());
        Assert.Equal(before, (await db.Collections.CountAsync(), await db.ReceivableDocuments.CountAsync(), await db.RenewalRequests.CountAsync(), await db.SubscriptionPeriods.CountAsync()));
    }
    private async Task<Guid> ExpiredPaid(Guid enrollment, Guid plan, Guid actor)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(); await scope.ServiceProvider.GetRequiredService<CollectionService>().CreateCashAsync(DemoSeed.NogoomAcademyId, actor, enrollment, plan, "opportunity-test-" + Guid.NewGuid(), "Cash");
        var period = await db.SubscriptionPeriods.SingleAsync(p => p.SportEnrollmentId == enrollment); period.StartDate = new(2026, 8, 29); period.EndDate = new(2026, 9, 27); period.Status = SubscriptionPeriodStatus.Expired;
        await db.SaveChangesAsync(); await tx.CommitAsync(); return period.Id;
    }
    [Fact]
    public async Task Report_reprices_current_rules_and_export_rejects_stale_prices_and_scope()
    {
        var f = await Fixture(false); using var owner = await Staff(); var plan = await CreatePlan(owner, f.A); await ExpiredPaid(f.Enrollment, plan, f.Owner);
        var path = $"/api/v1/finance/renewal-opportunities?branchId={f.A}"; var before = await owner.GetFromJsonAsync<JsonElement>(path); Assert.Equal("600.00", before.GetProperty("items")[0].GetProperty("quote").GetString());
        var player = before.GetProperty("items")[0].GetProperty("playerId").GetGuid(); var rule = await AddRule(owner, FixedRule(f.A, plan, 100));
        (await Write(owner, $"/api/v1/discount-rules/players/{player}", new { discountRuleId = rule, isActive = true }, true)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await owner.GetAsync($"/api/v1/finance/renewal-opportunities/export?branchId={f.A}&quoteHash={before.GetProperty("quoteHash").GetString()}")).StatusCode);
        var after = await owner.GetFromJsonAsync<JsonElement>(path); Assert.Equal("500.00", after.GetProperty("items")[0].GetProperty("quote").GetString()); Assert.NotEqual(before.GetProperty("quoteHash").GetString(), after.GetProperty("quoteHash").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.GetAsync($"/api/v1/finance/renewal-opportunities/export?branchId={f.B}&quoteHash={after.GetProperty("quoteHash").GetString()}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.GetAsync("/api/v1/finance/renewal-opportunities/export")).StatusCode);
    }
    [Fact]
    public async Task Archived_or_unpublished_plan_is_missing_quote_not_zero_and_does_not_change_history()
    {
        var f = await Fixture(false); using var owner = await Staff(); var plan = await CreatePlan(owner, f.A); var id = await ExpiredPaid(f.Enrollment, plan, f.Owner);
        var response = await Write(owner, $"/api/v1/subscriptions/plans/{plan}/delete", new { }); response.EnsureSuccessStatusCode();
        var report = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/renewal-opportunities?branchId={f.A}"); Assert.Equal(1, report.GetProperty("count").GetInt32()); Assert.Equal(1, report.GetProperty("unpricedCount").GetInt32()); Assert.Equal(JsonValueKind.Null, report.GetProperty("items")[0].GetProperty("quote").ValueKind); Assert.True(report.GetProperty("items")[0].GetProperty("requiresPlanSelection").GetBoolean());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Equal(600, (await db.SubscriptionPeriods.SingleAsync(p => p.Id == id)).PriceSnapshot);
    }
    [Fact]
    public async Task Frozen_cancelled_future_active_refunded_and_inactive_player_do_not_become_opportunities()
    {
        var f = await Fixture(false); using var owner = await Staff(); var plan = await CreatePlan(owner, f.A); var id = await ExpiredPaid(f.Enrollment, plan, f.Owner);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var period = await db.SubscriptionPeriods.Include(p => p.SportEnrollment).ThenInclude(e => e.Player).Include(p => p.Collection).ThenInclude(c => c.PaymentRequest).SingleAsync(p => p.Id == id);
        async Task Count(int n) { var report = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/renewal-opportunities?branchId={f.A}"); Assert.Equal(n, report.GetProperty("count").GetInt32()); }
        await Count(1);
        foreach (var status in new[] { SubscriptionPeriodStatus.Frozen, SubscriptionPeriodStatus.Cancelled }) { period.Status = status; await db.SaveChangesAsync(); await Count(0); }
        period.Status = SubscriptionPeriodStatus.Active; period.StartDate = new(2026, 9, 29); period.EndDate = new(2026, 10, 28); await db.SaveChangesAsync(); await Count(0);
        period.StartDate = new(2026, 8, 29); period.EndDate = new(2026, 9, 28); await db.SaveChangesAsync(); await Count(0); // inclusive last service day
        period.EndDate = new(2026, 9, 27); period.Collection.PaymentRequest.Status = PaymentRequestStatus.Failed; await db.SaveChangesAsync(); await Count(0);
        period.Collection.PaymentRequest.Status = PaymentRequestStatus.Confirmed; period.SportEnrollment.Player.IsActive = false; await db.SaveChangesAsync(); await Count(0);
        period.SportEnrollment.Player.IsActive = true; db.CollectionRefunds.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, CollectionId = period.CollectionId!.Value, PlayerId = period.SportEnrollment.PlayerId, Amount = 1, Currency = "EGP", PerformedByUserId = f.Owner, IdempotencyKey = Guid.NewGuid().ToString() }); await db.SaveChangesAsync(); await Count(0);
    }
    [Fact]
    public async Task Branch_admin_forecast_and_options_are_scoped_and_export_requires_its_own_grant()
    {
        var f = await Fixture(false); using var owner = await Staff(); var plan = await CreatePlan(owner, f.A); await ExpiredPaid(f.Enrollment, plan, f.Owner);
        var email = $"forecast.{Guid.NewGuid():N}@example.test";
        var created = await Write(owner, "/api/v1/manage/staff", new StaffWrite("إداري فرص اصطناعي", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, "اختبار")); created.EnsureSuccessStatusCode(); var member = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var actions = StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m == "finance", false, false, false)).ToArray();
        (await Write(owner, $"/api/v1/manage/staff/{member}/access", new StaffAccessWrite(false, [f.A], actions), true)).EnsureSuccessStatusCode(); using var admin = await Staff(email);
        var report = await admin.GetFromJsonAsync<JsonElement>("/api/v1/finance/renewal-opportunities"); Assert.Equal(1, report.GetProperty("count").GetInt32()); Assert.All(report.GetProperty("items").EnumerateArray(), x => Assert.Equal(f.A, x.GetProperty("branchId").GetGuid()));
        var options = await admin.GetFromJsonAsync<JsonElement>("/api/v1/finance/renewal-opportunities/options"); Assert.Single(options.GetProperty("branches").EnumerateArray()); Assert.All(options.GetProperty("groups").EnumerateArray(), x => Assert.Equal(f.A, x.GetProperty("branchId").GetGuid()));
        Assert.Equal(0, (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/finance/renewal-opportunities?branchId={RenewalOpportunityDemoSeed.BranchId}")).GetProperty("count").GetInt32());
        // reports.export is a separate module; finance.read must not imply it.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/finance/renewal-opportunities/export?quoteHash=" + report.GetProperty("quoteHash").GetString())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/finance/receivables/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/reports/financial/export")).StatusCode);
        var withExport = StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m is "finance" or "reports", false, false, false)).ToArray();
        (await Write(owner, $"/api/v1/manage/staff/{member}/access", new StaffAccessWrite(false, [f.A], withExport), true)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/finance/renewal-opportunities/export?quoteHash=" + report.GetProperty("quoteHash").GetString())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/finance/receivables/export")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/reports/financial/export")).StatusCode);
        var onlyExport = StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m == "reports", false, false, false)).ToArray();
        (await Write(owner, $"/api/v1/manage/staff/{member}/access", new StaffAccessWrite(false, [f.A], onlyExport), true)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/finance/renewal-opportunities/export?quoteHash=" + report.GetProperty("quoteHash").GetString())).StatusCode);
        (await Write(owner, $"/api/v1/manage/staff/{member}/access", new StaffAccessWrite(false, [f.B], actions), true)).EnsureSuccessStatusCode();
        Assert.Equal(0, (await admin.GetFromJsonAsync<JsonElement>("/api/v1/finance/renewal-opportunities")).GetProperty("count").GetInt32());
    }
    [Fact]
    public async Task Future_academy_guardian_and_coach_cannot_read_or_export_another_tenants_family_forecast()
    {
        using var owner = await Staff(DemoSeed.FutureOwnerEmail); var path = $"/api/v1/finance/renewal-opportunities?branchId={RenewalOpportunityDemoSeed.BranchId}";
        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>(path)).GetProperty("count").GetInt32());
        using var guardian = await Guardian(); Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync(path)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/v1/finance/renewal-opportunities/options")).StatusCode);
        using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services); Assert.Equal(HttpStatusCode.Forbidden, (await coach.GetAsync(path)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await coach.GetAsync("/api/v1/finance/renewal-opportunities/export?quoteHash=" + new string('a', 64))).StatusCode);
    }
}
