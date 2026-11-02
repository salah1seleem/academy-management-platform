using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;

public sealed partial class SubscriptionPaymentTests
{
    private const string Statements = "/api/v1/finance/settlement-comparisons";
    [Fact]
    public async Task Manual_statement_demo_is_repeatable_and_does_not_create_collections()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await CollectionFundsDemoSeed.Seed(db); var before = await db.Collections.CountAsync();
        await SettlementComparisonDemoSeed.Seed(db); await SettlementComparisonDemoSeed.Seed(db);
        Assert.Equal(before, await db.Collections.CountAsync()); Assert.Equal(2, await db.SettlementComparisons.CountAsync(x => x.Id == SettlementComparisonDemoSeed.Id(1) || x.Id == SettlementComparisonDemoSeed.Id(2)));
    }
    private async Task<StatementLineWrite> Statement(bool confirm = true)
    {
        var id = await NewPayment(Slice2DemoSeed.OmarPlayerId, Slice2DemoSeed.FootballId);
        if (confirm) { using var c = factory.CreateClient(); (await c.PostAsJsonAsync(Events, await Signed(id))).EnsureSuccessStatusCode(); }
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var p = await db.PaymentRequests.Include(p => p.RenewalRequest).ThenInclude(r => r.SportEnrollment).SingleAsync(p => p.Id == id);
        return new(p.RenewalRequest.SportEnrollment.BranchId, "Provider", "DEMO-ACCOUNT", $"demo-statement-{Guid.NewGuid():N}", "1", p.Provider, p.ProviderReference, new(2026, 9, 28), p.Amount, 5m, p.Amount - 5m, p.Currency);
    }
    private async Task<StatementLineWrite> StatementQuote(HttpClient owner, StatementLineWrite body)
    {
        var r = await Post(owner, $"{Statements}/preview", body, await Csrf(owner)); r.EnsureSuccessStatusCode();
        return body with { QuoteHash = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteHash").GetString() };
    }
    [Fact]
    public async Task Owner_matches_fee_and_net_exactly_once_without_altering_collection_or_subscription()
    {
        var body = await Statement(); using var owner = await InboxStaff(DemoSeed.OwnerEmail); body = await StatementQuote(owner, body); var csrf = await Csrf(owner);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var collection = await db.Collections.SingleAsync(c => c.ProviderReference == body.PaymentReference);
        var periods = await db.SubscriptionPeriods.CountAsync(p => p.CollectionId == collection.Id);
        var replies = await Task.WhenAll(Post(owner, Statements, body, csrf), Post(owner, Statements, body, csrf)); foreach (var r in replies) r.EnsureSuccessStatusCode();
        var a = await replies[0].Content.ReadFromJsonAsync<JsonElement>(); var b = await replies[1].Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(a.GetProperty("id").GetGuid(), b.GetProperty("id").GetGuid()); Assert.Equal("Matched", a.GetProperty("result").GetString()); Assert.Equal("5.00", a.GetProperty("fee").GetString()); Assert.Equal(CashRegisterService.Money(body.NetAmount), a.GetProperty("net").GetString());
        Assert.Equal(1, await db.Collections.CountAsync(c => c.Id == collection.Id)); Assert.Equal(periods, await db.SubscriptionPeriods.CountAsync(p => p.CollectionId == collection.Id)); Assert.Equal(body.GrossAmount, (await db.Collections.AsNoTracking().SingleAsync(c => c.Id == collection.Id)).Amount);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, Statements, body with { GrossAmount = body.GrossAmount + 1 }, csrf)).StatusCode);
        var duplicate = await StatementQuote(owner, body with { StatementReference = $"another-{Guid.NewGuid():N}" }); Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, Statements, duplicate, csrf)).StatusCode);
    }
    [Theory] [InlineData("AmountMismatch")] [InlineData("CurrencyMismatch")] [InlineData("NetMismatch")] [InlineData("Unmatched")]
    public async Task Statement_discrepancy_is_retained_without_fabricating_a_match(string result)
    {
        var body = await Statement(); body = result switch { "AmountMismatch" => body with { GrossAmount = body.GrossAmount + 1, NetAmount = body.NetAmount + 1 }, "CurrencyMismatch" => body with { Currency = "USD" }, "NetMismatch" => body with { NetAmount = body.NetAmount + 1 }, _ => body with { PaymentReference = $"unknown-{Guid.NewGuid():N}" } };
        using var owner = await InboxStaff(DemoSeed.OwnerEmail); body = await StatementQuote(owner, body); var response = await Post(owner, Statements, body, await Csrf(owner)); response.EnsureSuccessStatusCode(); var item = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(result, item.GetProperty("result").GetString()); if (result == "Unmatched") Assert.Equal(JsonValueKind.Null, item.GetProperty("collectionId").ValueKind);
    }
    [Fact]
    public async Task Reconciliation_is_owner_only_csrf_tenant_scoped_and_immutable()
    {
        var body = await Statement(); using var owner = await InboxStaff(DemoSeed.OwnerEmail); body = await StatementQuote(owner, body);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(Statements, body)).StatusCode);
        var saved = await Post(owner, Statements, body, await Csrf(owner)); saved.EnsureSuccessStatusCode(); var id = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var other = await InboxStaff(DemoSeed.FutureOwnerEmail); Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Statements}/{id}")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await Post(other, $"{Statements}/preview", body, await Csrf(other))).StatusCode);
        await using (var foreignScope = factory.Services.CreateAsyncScope()) {
            var branch = await foreignScope.ServiceProvider.GetRequiredService<FoundationDbContext>().Branches.Where(x => x.AcademyId == DemoSeed.FutureAcademyId).Select(x => x.Id).FirstAsync();
            var foreignPreview = await Post(other, $"{Statements}/preview", body with { BranchId = branch }, await Csrf(other)); foreignPreview.EnsureSuccessStatusCode();
            var hidden = await foreignPreview.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("Unmatched", hidden.GetProperty("result").GetString()); Assert.Equal(JsonValueKind.Null, hidden.GetProperty("collectionId").ValueKind); Assert.Equal(JsonValueKind.Null, hidden.GetProperty("expected").ValueKind);
        }
        foreach (var email in new[] { DemoSeed.AdminEmail, DemoSeed.CoachEmail }) { using var staff = await InboxStaff(email); Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(Statements)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Post(staff, Statements, body, await Csrf(staff))).StatusCode); }
        using var guardian = await GuardianClient(); Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync(Statements)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var row = await db.SettlementComparisons.SingleAsync(x => x.Id == id); row.Result = "changed"; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear(); await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SettlementComparisons\" SET \"NetAmount\"=0 WHERE \"Id\"={id}"));
        var forged = Guid.NewGuid(); var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"SettlementComparisons\" (\"Id\",\"AcademyId\",\"BranchId\",\"CollectionId\",\"SourceKind\",\"AccountTag\",\"StatementReference\",\"LineReference\",\"Provider\",\"PaymentReference\",\"SettlementDate\",\"GrossAmount\",\"FeeAmount\",\"NetAmount\",\"Currency\",\"ExpectedAmount\",\"ExpectedCurrency\",\"Result\",\"ContentHash\",\"ActorUserId\",\"CreatedAtUtc\",\"UpdatedAtUtc\",\"IsActive\") SELECT {forged},\"AcademyId\",\"BranchId\",\"CollectionId\",\"SourceKind\",'FORGED',\"StatementReference\",\"LineReference\",\"Provider\",\"PaymentReference\",\"SettlementDate\",0,0,0,\"Currency\",\"ExpectedAmount\",\"ExpectedCurrency\",'Matched',\"ContentHash\",\"ActorUserId\",\"CreatedAtUtc\",\"UpdatedAtUtc\",true FROM \"SettlementComparisons\" WHERE \"Id\"={id}")); Assert.Equal("23514", error.SqlState);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.GetAsync($"{Statements}?from=2026-10-20&to=2026-10-01")).StatusCode);
        var list = await owner.GetFromJsonAsync<JsonElement>(Statements); Assert.True(list.GetProperty("total").GetInt32() > 0);
    }
    [Fact]
    public async Task Comparison_rejects_stale_preview_when_a_pending_payment_becomes_confirmed()
    {
        var body = await Statement(false); using var owner = await InboxStaff(DemoSeed.OwnerEmail); var quote = await StatementQuote(owner, body);
        await using var scope = factory.Services.CreateAsyncScope(); var id = await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().PaymentRequests.Where(p => p.ProviderReference == body.PaymentReference).Select(p => p.Id).SingleAsync();
        using var provider = factory.CreateClient(); (await provider.PostAsJsonAsync(Events, await Signed(id))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, Statements, quote, await Csrf(owner))).StatusCode);
        Assert.False(await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().SettlementComparisons.AnyAsync(x => x.StatementReference == body.StatementReference));
    }
    [Fact]
    public async Task Invalid_statement_precision_negative_money_and_future_date_are_rejected()
    {
        var body = await Statement(); using var owner = await InboxStaff(DemoSeed.OwnerEmail);
        foreach (var invalid in new[] { body with { GrossAmount = -1 }, body with { GrossAmount = .001m }, body with { FeeAmount = 9999 }, body with { SettlementDate = new(2099, 1, 1) }, body with { AccountTag = "" }, body with { Currency = null! } }) Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(owner, $"{Statements}/preview", invalid, await Csrf(owner))).StatusCode);
    }
}
