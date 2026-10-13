using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;

public sealed partial class V2ReceivablesTests
{
    private const string CreditPath = "/api/v1/finance/credit-applications";
    private async Task<(Guid Source, Guid Target, Guid Collection)> CreditFixture(HttpClient owner, Guid enrollment, Guid actor)
    {
        var source = await Charge(owner, enrollment, 100); var target = await Charge(owner, enrollment, 300);
        var collection = await Collection(enrollment, actor, 200);
        (await Move(owner, source, "PaymentAllocation", 200, collection)).EnsureSuccessStatusCode();
        return (source, target, collection);
    }
    private Task<HttpResponseMessage> CreditQuote(HttpClient c, Guid source, Guid target, decimal amount = 60) => Post(c, CreditPath + "/quote", new { sourceDocumentId = source, targetDocumentId = target, amount, reason = "تخصيص رصيد دائن اصطناعي" });
    private static object CreditBody(Guid source, Guid target, JsonElement q, decimal amount = 60) => new { sourceDocumentId = source, targetDocumentId = target, amount, reason = "تخصيص رصيد دائن اصطناعي", quoteHash = q.GetProperty("quoteHash").GetString() };
    [Fact]
    public async Task Credit_application_preview_then_atomic_replay_preserves_cash_and_guardian_csv_asOf()
    {
        var f = await Fixture(); using var owner = await Staff(DemoSeed.OwnerEmail); var c = await CreditFixture(owner, f.Enrollment, f.Owner);
        var old = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/receivables?playerId={f.Player}");
        var qr = await CreditQuote(owner, c.Source, c.Target); qr.EnsureSuccessStatusCode(); var q = await qr.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("-100.00", q.GetProperty("source").GetProperty("before").GetString()); Assert.Equal("-40.00", q.GetProperty("source").GetProperty("after").GetString());
        await using (var s = factory.Services.CreateAsyncScope()) { var db = s.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.False(await db.ReceivableCreditApplications.AnyAsync(x => x.SourceDocumentId == c.Source)); }
        var key = Guid.NewGuid().ToString(); var body = CreditBody(c.Source, c.Target, q);
        var replies = await Task.WhenAll(Post(owner, CreditPath + "/approve", body, key), Post(owner, CreditPath + "/approve", body, key)); Assert.All(replies, x => x.EnsureSuccessStatusCode());
        Assert.Equal((await replies[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(), (await replies[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        var after = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/receivables?playerId={f.Player}"); Assert.Equal("240.00", after.GetProperty("outstanding").GetString()); Assert.Equal("40.00", after.GetProperty("overpaymentCredit").GetString());
        var asOf = Uri.EscapeDataString(old.GetProperty("asOf").GetString()!); var past = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/receivables?playerId={f.Player}&asOf={asOf}"); Assert.Equal("300.00", past.GetProperty("outstanding").GetString()); Assert.Equal("100.00", past.GetProperty("overpaymentCredit").GetString());
        var csv = await owner.GetStringAsync($"/api/v1/finance/receivables/export?playerId={f.Player}&asOf={Uri.EscapeDataString(after.GetProperty("asOf").GetString()!)}"); Assert.Contains("240.00", csv); Assert.Contains("60.00", csv);
        using var guardian = await Guardian(); var own = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/guardian/receivables"); var target = own.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == c.Target); Assert.Equal("60.00", target.GetProperty("creditReceived").GetString()); Assert.Equal("240.00", target.GetProperty("outstanding").GetString());
        await using (var s = factory.Services.CreateAsyncScope()) { var db = s.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Equal(1, await db.ReceivableCreditApplications.CountAsync(x => x.SourceDocumentId == c.Source)); Assert.Equal(200, await db.Collections.Where(x => x.Id == c.Collection).Select(x => x.Amount).SingleAsync()); Assert.Equal(200, await db.ReceivableMovements.Where(x => x.CollectionId == c.Collection).SumAsync(x => x.Amount)); Assert.False(await db.CollectionRefunds.AnyAsync(x => x.CollectionId == c.Collection)); }
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, CreditPath + "/approve", CreditBody(c.Source, c.Target, q, 61), key)).StatusCode);
    }
    [Fact]
    public async Task Credit_application_rejects_roles_foreign_tenant_csrf_other_player_and_unfunded_document()
    {
        var f = await Fixture(); using var owner = await Staff(DemoSeed.OwnerEmail); var c = await CreditFixture(owner, f.Enrollment, f.Owner);
        using var admin = await Staff(DemoSeed.AdminEmail); using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services); using var guardian = await Guardian(); using var foreign = await Staff(DemoSeed.FutureOwnerEmail);
        foreach (var actor in new[] { admin, coach, guardian }) Assert.Equal(HttpStatusCode.Forbidden, (await actor.GetAsync($"{CreditPath}/{c.Source}/options")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"{CreditPath}/{c.Source}/options")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(CreditPath + "/quote", new { sourceDocumentId = c.Source, targetDocumentId = c.Target, amount = 60 })).StatusCode);
        var other = await Fixture(); var target = await Charge(owner, other.Enrollment, 200);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await CreditQuote(owner, c.Source, target)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CreditQuote(owner, c.Target, c.Source)).StatusCode);
        foreach (var amount in new[] { 0m, -.01m, .001m, 9999999999m }) Assert.Equal(HttpStatusCode.UnprocessableEntity, (await CreditQuote(owner, c.Source, c.Target, amount)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(owner, CreditPath + "/quote", new { sourceDocumentId = c.Source, targetDocumentId = c.Target, amount = 1, reason = " " })).StatusCode);
    }
    [Fact]
    public async Task Credit_application_stale_quote_and_concurrent_targets_never_spend_same_credit_twice()
    {
        var f = await Fixture(); using var owner = await Staff(DemoSeed.OwnerEmail); var c = await CreditFixture(owner, f.Enrollment, f.Owner); var second = await Charge(owner, f.Enrollment, 200);
        var q1 = await (await CreditQuote(owner, c.Source, c.Target, 80)).Content.ReadFromJsonAsync<JsonElement>(); var q2 = await (await CreditQuote(owner, c.Source, second, 80)).Content.ReadFromJsonAsync<JsonElement>();
        var replies = await Task.WhenAll(Post(owner, CreditPath + "/approve", CreditBody(c.Source, c.Target, q1, 80)), Post(owner, CreditPath + "/approve", CreditBody(c.Source, second, q2, 80)));
        Assert.Single(replies, x => x.IsSuccessStatusCode); Assert.Single(replies, x => x.StatusCode == HttpStatusCode.Conflict);
        var q = await (await CreditQuote(owner, c.Source, c.Target, 10)).Content.ReadFromJsonAsync<JsonElement>();
        (await Move(owner, c.Target, "CreditNote", 1)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, CreditPath + "/approve", CreditBody(c.Source, c.Target, q, 10))).StatusCode);
        await using var s = factory.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Equal(80, await db.ReceivableCreditApplications.Where(x => x.SourceDocumentId == c.Source).SumAsync(x => x.Amount));
    }
    [Fact]
    public async Task Credit_application_database_guards_immutable_history_and_forged_source_balance()
    {
        var f = await Fixture(); using var owner = await Staff(DemoSeed.OwnerEmail); var c = await CreditFixture(owner, f.Enrollment, f.Owner); var q = await (await CreditQuote(owner, c.Source, c.Target)).Content.ReadFromJsonAsync<JsonElement>();
        (await Post(owner, CreditPath + "/approve", CreditBody(c.Source, c.Target, q))).EnsureSuccessStatusCode();
        await using var s = factory.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<FoundationDbContext>(); var row = await db.ReceivableCreditApplications.SingleAsync(x => x.SourceDocumentId == c.Source);
        row.Reason = "تعديل ممنوع"; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        foreach (var sql in new[] { "UPDATE \"ReceivableCreditApplications\" SET \"Amount\"=1 WHERE \"Id\"={0}", "DELETE FROM \"ReceivableCreditApplications\" WHERE \"Id\"={0}" }) {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, row.Id)); Assert.Equal("23514", ex.SqlState);
        }
        var duplicate = await db.ReceivableCreditApplications.AsNoTracking().SingleAsync(x => x.Id == row.Id); duplicate.Id = Guid.NewGuid(); duplicate.RequestKey = Guid.NewGuid().ToString(); duplicate.SourceBalanceBefore = -100; db.ReceivableCreditApplications.Add(duplicate);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal("23514", ((PostgresException)error.InnerException!).SqlState);
        db.ChangeTracker.Clear();
        var admin = await db.AcademyMemberships.Where(m => m.AcademyId == row.AcademyId && m.Role == Academy.Infrastructure.Tenancy.AcademyRole.AcademyAdmin && m.IsActive).FirstAsync();
        foreach (var mode in new[] { "role", "currency" }) {
            var forged = await db.ReceivableCreditApplications.AsNoTracking().SingleAsync(x => x.Id == row.Id); forged.Id = Guid.NewGuid(); forged.RequestKey = Guid.NewGuid().ToString(); forged.Amount = 10; forged.SourceBalanceBefore = -40; forged.TargetBalanceBefore = 240;
            if (mode == "role") { forged.ActorUserId = admin.UserId; forged.ActorMembershipId = admin.Id; } else forged.Currency = "USD";
            db.ReceivableCreditApplications.Add(forged); var rejected = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal("23514", ((PostgresException)rejected.InnerException!).SqlState); db.ChangeTracker.Clear();
        }
    }
    [Fact]
    public async Task Credit_application_seed_is_idempotent_funded_and_does_not_auto_apply()
    {
        await using var s = factory.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await ReceivableCreditDemoSeed.Seed(db); var collections = await db.Collections.CountAsync(); var movements = await db.ReceivableMovements.CountAsync(); await ReceivableCreditDemoSeed.Seed(db);
        Assert.Equal(collections, await db.Collections.CountAsync()); Assert.Equal(movements, await db.ReceivableMovements.CountAsync()); Assert.False(await db.ReceivableCreditApplications.AnyAsync(x => x.SourceDocumentId == ReceivableCreditDemoSeed.SourceId));
        var allocation = await db.ReceivableMovements.SingleAsync(x => x.DocumentId == ReceivableCreditDemoSeed.SourceId && x.Kind == Academy.Infrastructure.Subscriptions.ReceivableMovementKind.PaymentAllocation);
        Assert.Equal(await db.Collections.Where(c => c.Id == allocation.CollectionId).Select(c => c.Amount).SingleAsync(), allocation.Amount);
        using var owner = await Staff(DemoSeed.OwnerEmail); var options = await owner.GetFromJsonAsync<JsonElement>($"{CreditPath}/{ReceivableCreditDemoSeed.SourceId}/options"); Assert.Equal("-200.00", options.GetProperty("source").GetProperty("outstanding").GetString());
    }
}
