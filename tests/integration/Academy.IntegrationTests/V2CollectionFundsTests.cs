using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2ReceivablesTests
{
    private Task<HttpResponseMessage> Refund(HttpClient client, Guid player, Guid collection, string amount, string? reason = null, string? key = null) =>
        Post(client, $"/api/v1/people/players/{player}/refunds", new { collectionId = collection, amount, reason }, key);

    [Fact]
    public async Task Collection_budget_demo_seed_is_scoped_repeatable_and_preserves_posted_records()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var foreign = await db.Collections.CountAsync(c => c.AcademyId == DemoSeed.FutureAcademyId);
        await CollectionFundsDemoSeed.Seed(db);
        var count = await db.Collections.CountAsync(); var refundCount = await db.CollectionRefunds.CountAsync();
        await CollectionFundsDemoSeed.Seed(db);
        Assert.Equal(count, await db.Collections.CountAsync()); Assert.Equal(refundCount, await db.CollectionRefunds.CountAsync());
        Assert.Equal(foreign, await db.Collections.CountAsync(c => c.AcademyId == DemoSeed.FutureAcademyId));
        var allocation = await db.ReceivableMovements.SingleAsync(m => m.DocumentId == CollectionFundsDemoSeed.DocumentId);
        var used = await CollectionFunds.Used(db, DemoSeed.NogoomAcademyId, allocation.CollectionId!.Value);
        Assert.Equal(100, used.Allocated); Assert.Equal(50, used.Refunded);
    }

    [Fact]
    public async Task Collection_budget_includes_hidden_historical_branch_allocations_without_exposing_documents()
    {
        var f = await Fixture(); var collection = await Collection(f.Enrollment, f.Owner, 100);
        await using var setup = factory.Services.CreateAsyncScope(); var db = setup.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var other = await db.Branches.FirstAsync(b => b.AcademyId == DemoSeed.NogoomAcademyId && b.Id != f.Branch);
        var doc = new ReceivableDocument { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, BranchId = other.Id, SportEnrollmentId = f.Enrollment, PlayerId = f.Player, PlayerNameSnapshot = "تاريخ فرع اصطناعي", Amount = 100, Currency = "EGP", Description = "مطالبة فرع سابق اصطناعية", DueDate = new(2026,9,20), Source = ReceivableSource.OpeningBalance, SourceKey = Guid.NewGuid().ToString(), ContentHash = "TEST", ActorUserId = f.Owner };
        db.ReceivableDocuments.Add(doc);
        db.ReceivableMovements.Add(new() { Id = Guid.NewGuid(), AcademyId = doc.AcademyId, DocumentId = doc.Id, Kind = ReceivableMovementKind.PaymentAllocation, Amount = 80, Currency = "EGP", CollectionId = collection, Reason = "تاريخ تخصيص اصطناعي", SourceKey = Guid.NewGuid().ToString(), ContentHash = "TEST", ActorUserId = f.Owner });
        await db.SaveChangesAsync();
        await using var scope = factory.Services.CreateAsyncScope(); var scoped = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        scoped.BindRequestScope(DemoSeed.NogoomAcademyId, f.Owner, Guid.NewGuid(), [f.Branch]);
        Assert.True(await scoped.Collections.AnyAsync(c => c.Id == collection));
        Assert.False(await scoped.ReceivableDocuments.AnyAsync(d => d.Id == doc.Id));
        var used = await CollectionFunds.Used(scoped, DemoSeed.NogoomAcademyId, collection);
        Assert.Equal(80, used.Allocated); Assert.Equal(20, CollectionFunds.Available(100,used.Refunded,used.Allocated));
        Assert.Equal((0m,0m), await CollectionFunds.Used(scoped, DemoSeed.FutureAcademyId, collection));
    }

    [Fact]
    public async Task Collection_budget_allocation_then_refund_shares_exact_available_without_reopening_debt()
    {
        var f = await Fixture(); using var owner = await Staff(DemoSeed.OwnerEmail);
        var doc = await Charge(owner, f.Enrollment, 100); var collection = await Collection(f.Enrollment, f.Owner, 100);
        (await Move(owner, doc, "PaymentAllocation", 80, collection)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Refund(owner, f.Player, collection, "20.01")).StatusCode);
        var options = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/players/{f.Player}/refunds");
        Assert.Equal("20.00", options.GetProperty("collections")[0].GetProperty("available").GetString());
        Assert.Equal(80, options.GetProperty("collections")[0].GetProperty("allocated").GetDecimal());
        (await Refund(owner, f.Player, collection, "20.00")).EnsureSuccessStatusCode();
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/receivables/{doc}/collections")).EnumerateArray());
        var report = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/receivables?playerId={f.Player}");
        Assert.Equal("20.00", report.GetProperty("outstanding").GetString());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, await db.Receipts.CountAsync(x => x.CollectionId == collection));
        Assert.False(await db.SubscriptionPeriods.AnyAsync(x => x.SportEnrollmentId == f.Enrollment));
    }

    [Fact]
    public async Task Collection_budget_refund_then_allocation_cannot_spend_refunded_money()
    {
        var f = await Fixture(); using var owner = await Staff(DemoSeed.OwnerEmail);
        var doc = await Charge(owner, f.Enrollment, 100); var collection = await Collection(f.Enrollment, f.Owner, 100);
        (await Refund(owner, f.Player, collection, "80.00")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Move(owner, doc, "PaymentAllocation", 21, collection)).StatusCode);
        (await Move(owner, doc, "PaymentAllocation", 20, collection)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Refund(owner, f.Player, collection, "0.01")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Collection_budget_parallel_consumers_allow_only_one_winner(bool twoRefunds)
    {
        var f = await Fixture(); using var a = await Staff(DemoSeed.OwnerEmail); using var b = await Staff(DemoSeed.OwnerEmail);
        var doc = await Charge(a, f.Enrollment, 100); var collection = await Collection(f.Enrollment, f.Owner, 100);
        var results = await Task.WhenAll(Refund(a, f.Player, collection, "80.00"), twoRefunds ? Refund(b, f.Player, collection, "80.00") : Move(b, doc, "PaymentAllocation", 80, collection));
        Assert.Single(results, x => x.IsSuccessStatusCode); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var used = await CollectionFunds.Used(db, DemoSeed.NogoomAcademyId, collection);
        Assert.Equal(80, used.Allocated + used.Refunded);
    }

    [Fact]
    public async Task Collection_budget_refund_retry_is_exact_and_rejects_changed_content_and_invalid_precision()
    {
        var f = await Fixture(); using var owner = await Staff(DemoSeed.OwnerEmail); var collection = await Collection(f.Enrollment, f.Owner, 100);
        var key = Guid.NewGuid().ToString();
        var first = await Refund(owner, f.Player, collection, "12.34", "  اختبار استرداد  ", key); first.EnsureSuccessStatusCode();
        var replay = await Refund(owner, f.Player, collection, "12.34", "اختبار استرداد", key); replay.EnsureSuccessStatusCode();
        Assert.Equal((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Refund(owner, f.Player, collection, "12.34", "سبب مختلف", key)).StatusCode);
        foreach (var invalid in new[] { "0", "-1", "0.001", "1000000000" }) Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Refund(owner, f.Player, collection, invalid)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Single(await db.CollectionRefunds.Where(x => x.CollectionId == collection).ToListAsync());
    }

    [Fact]
    public async Task Collection_budget_rejects_foreign_tenant_unlinked_player_roles_and_missing_csrf()
    {
        var f = await Fixture(); using var owner = await Staff(DemoSeed.OwnerEmail); var collection = await Collection(f.Enrollment, f.Owner, 100);
        using var foreign = await Staff(DemoSeed.FutureOwnerEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await Refund(foreign, f.Player, collection, "1.00")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Refund(owner, Guid.NewGuid(), collection, "1.00")).StatusCode);
        using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services); using var guardian = await Guardian();
        Assert.Equal(HttpStatusCode.Forbidden, (await Refund(coach, f.Player, collection, "1.00")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Refund(guardian, f.Player, collection, "1.00")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/people/players/{f.Player}/refunds", new { collectionId = collection, amount = "1.00" })).StatusCode);
    }
}
