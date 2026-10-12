using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;

public sealed partial class V2ReceivablesTests
{
    private async Task<(SaleWrite Body, Guid Register, Guid Member)> CashProductFixture(HttpClient owner)
    {
        var f = await CashRegisterFixture(owner); (await CashAssign(owner, f.Register, f.Member)).EnsureSuccessStatusCode();
        var shift = await CashOpen(owner, f.Register, "100");
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var p = new Academy.Infrastructure.Content.SportCatalogItem { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId,
            SportId = Slice2DemoSeed.FootballId, ArabicName = "منتج قبض اصطناعي " + Guid.NewGuid(), ImageReference = "/demo-assets/catalog-football.svg",
            DisplayPrice = 100, Currency = "EGP", DiscountPercentage = 10, StockQuantity = 3 };
        db.SportCatalogItems.Add(p); await db.SaveChangesAsync();
        return (new(p.Id, 2, null, "Cash", f.Branch, 180, shift), f.Register, f.Member);
    }
    private const string SalesRoot = "/api/v1/manage/product-sales";
    private static string ProductQuoteUrl(SaleWrite b) => $"{SalesRoot}/quote?productId={b.ProductId}&branchId={b.BranchId}&quantity={b.Quantity}" + (b.PlayerId.HasValue ? $"&playerId={b.PlayerId}" : "");

    [Fact]
    public async Task Cash_product_demo_seed_is_repeatable_preserves_stock_edits_and_never_collects()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await CashProductDemoSeed.Seed(db); var product = await db.SportCatalogItems.SingleAsync(p => p.Id == CashProductDemoSeed.ProductId);
        product.StockQuantity = 17; product.ArabicName = "منتج راجعه المستخدم اصطناعيًا"; await db.SaveChangesAsync();
        var counts = (await db.ProductSales.CountAsync(), await db.CashProductPostings.CountAsync(), await db.CashSessions.CountAsync());
        await CashProductDemoSeed.Seed(db); await db.Entry(product).ReloadAsync(); Assert.Equal(17, product.StockQuantity); Assert.Equal("منتج راجعه المستخدم اصطناعيًا", product.ArabicName);
        Assert.Equal(counts, (await db.ProductSales.CountAsync(), await db.CashProductPostings.CountAsync(), await db.CashSessions.CountAsync()));
    }

    [Fact]
    public async Task Cash_product_sale_stock_and_shift_commit_once_and_replay_after_close()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var f = await CashProductFixture(owner); var b = f.Body; var key = Guid.NewGuid().ToString();
        var q = await owner.GetFromJsonAsync<JsonElement>(ProductQuoteUrl(b)); Assert.Equal("180.00", q.GetProperty("amount").GetString());
        Assert.Contains(q.GetProperty("sessions").EnumerateArray(), s => s.GetProperty("id").GetGuid() == b.CashSessionId);
        var results = await Task.WhenAll(Post(owner, SalesRoot, b, key), Post(owner, SalesRoot, b, key)); Assert.All(results, r => r.EnsureSuccessStatusCode());
        var sale = (await results[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(sale, (await results[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Single(await db.ProductSales.Where(p => p.ProductId == b.ProductId).ToArrayAsync());
        var posting = Assert.Single(await db.CashProductPostings.Where(p => p.SessionId == b.CashSessionId).ToArrayAsync()); Assert.Equal(sale, posting.ProductSaleId); Assert.Equal(180, posting.Amount);
        Assert.Equal(1, await db.SportCatalogItems.Where(p => p.Id == b.ProductId).Select(p => p.StockQuantity).SingleAsync());
        Assert.False(await db.CashReceiptPostings.AnyAsync(p => p.SessionId == b.CashSessionId));
        var detail = await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{b.CashSessionId}"); Assert.Equal("280.00", detail.GetProperty("balance").GetString());
        Assert.Equal(sale, detail.GetProperty("productSales")[0].GetProperty("productSaleId").GetGuid());
        (await Post(owner, $"{CashRoot}/sessions/{b.CashSessionId}/close", new { countedAmount = "280" })).EnsureSuccessStatusCode();
        (await Post(owner, SalesRoot, b, key)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, SalesRoot, b with { CashSessionId = Guid.NewGuid() }, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, SalesRoot, b with { Quantity = 1, ExpectedAmount = 90 }, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, SalesRoot, b)).StatusCode); // stock insufficient, no second sale
        var report = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial?allDates=true&collectionType=Product&branchId={b.BranchId}");
        Assert.Contains(report.GetProperty("items").EnumerateArray(), p => p.GetProperty("collectionId").GetGuid() == sale);
    }
    [Fact]
    public async Task Cash_product_missing_stale_price_or_shift_rolls_back_stock_and_all_financial_effects()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var b = (await CashProductFixture(owner)).Body;
        foreach (var amount in new decimal?[] { null, 0, -1, .001m, 1000000000 }) Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(owner, SalesRoot, b with { ExpectedAmount = amount })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(owner, SalesRoot, b with { CashSessionId = null })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, SalesRoot, b with { ExpectedAmount = .01m })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var p = await db.SportCatalogItems.SingleAsync(p => p.Id == b.ProductId);
        p.DisplayPrice = 110; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, SalesRoot, b)).StatusCode);
        Assert.Equal(3, (await db.SportCatalogItems.AsNoTracking().SingleAsync(p => p.Id == b.ProductId)).StockQuantity);
        Assert.False(await db.ProductSales.AnyAsync(p => p.ProductId == b.ProductId)); Assert.False(await db.CashProductPostings.AnyAsync(p => p.SessionId == b.CashSessionId));
    }
    [Fact]
    public async Task Cash_product_distinct_concurrent_sales_cannot_oversell_or_post_twice()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var b = (await CashProductFixture(owner)).Body;
        var results = await Task.WhenAll(Post(owner, SalesRoot, b), Post(owner, SalesRoot, b));
        Assert.Single(results, r => r.IsSuccessStatusCode); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, await db.SportCatalogItems.Where(p => p.Id == b.ProductId).Select(p => p.StockQuantity).SingleAsync());
        Assert.Single(await db.ProductSales.Where(p => p.ProductId == b.ProductId).ToArrayAsync()); Assert.Single(await db.CashProductPostings.Where(p => p.SessionId == b.CashSessionId).ToArrayAsync());
    }
    [Fact]
    public async Task Cash_product_historical_unposted_sale_is_not_backfilled_by_a_retry()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var b = (await CashProductFixture(owner)).Body; var key = Guid.NewGuid().ToString();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var user = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
        db.ProductSales.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, ProductId = b.ProductId, BranchId = b.BranchId,
            Quantity = b.Quantity, UnitPrice = 90, Amount = 180, Currency = "EGP", ProductName = "بيع تاريخي اصطناعي", SportId = Slice2DemoSeed.FootballId, SportName = "كرة القدم",
            Status = "Confirmed", PaymentMethod = "Cash", RequestedByUserId = user, ConfirmedByUserId = user, ConfirmedAtUtc = DateTimeOffset.UtcNow, IdempotencyKey = key }); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, SalesRoot, b, key)).StatusCode);
        Assert.False(await db.CashProductPostings.AnyAsync(p => p.SessionId == b.CashSessionId)); Assert.Equal(3, await db.SportCatalogItems.Where(p => p.Id == b.ProductId).Select(p => p.StockQuantity).SingleAsync());
    }
    [Fact]
    public async Task Cash_product_closed_disabled_revoked_and_wrong_branch_or_currency_are_rejected()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var f = await CashProductFixture(owner); var b = f.Body;
        (await CashAssign(owner, f.Register, f.Member, false)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Forbidden, (await Post(owner, SalesRoot, b)).StatusCode);
        (await CashAssign(owner, f.Register, f.Member)).EnsureSuccessStatusCode();
        var settings = await CashSettings(); try { await CashSetConfiguration(owner, settings with { CashEnabled = false }); Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, SalesRoot, b)).StatusCode); }
        finally { await CashSetConfiguration(owner, settings); }
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var branch = await db.Branches.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.IsActive && x.Id != b.BranchId).Select(x => x.Id).FirstAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, SalesRoot, b with { BranchId = branch })).StatusCode);
        var p = await db.SportCatalogItems.SingleAsync(p => p.Id == b.ProductId); p.Currency = "USD"; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, SalesRoot, b)).StatusCode); p.Currency = "EGP"; await db.SaveChangesAsync();
        (await Post(owner, $"{CashRoot}/sessions/{b.CashSessionId}/close", new { countedAmount = "100" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, SalesRoot, b)).StatusCode); Assert.False(await db.ProductSales.AnyAsync(p => p.ProductId == b.ProductId));
    }
    [Fact]
    public async Task Cash_product_csrf_role_and_tenant_guards_cannot_be_bypassed_as_online()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); using var admin = await Staff(DemoSeed.AdminEmail); using var guardian = await Guardian(); using var other = await Staff(DemoSeed.FutureOwnerEmail);
        var b = (await CashProductFixture(owner)).Body;
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(SalesRoot, b)).StatusCode);
        foreach (var denied in new[] { admin, guardian }) { Assert.Equal(HttpStatusCode.Forbidden, (await Post(denied, SalesRoot, b)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Post(denied, SalesRoot, b with { PaymentMethod = "Online", CashSessionId = null })).StatusCode); }
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(ProductQuoteUrl(b))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(other, SalesRoot, b)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync(ProductQuoteUrl(b))).StatusCode);
        (await Post(owner, SalesRoot, b with { PaymentMethod = "Online", CashSessionId = null })).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); Assert.False(await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().CashProductPostings.AnyAsync(p => p.SessionId == b.CashSessionId));
    }
    [Fact]
    public async Task Cash_product_race_with_close_preserves_frozen_balance_and_no_partial_stock_write()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var b = (await CashProductFixture(owner)).Body;
        var results = await Task.WhenAll(Post(owner, SalesRoot, b), Post(owner, $"{CashRoot}/sessions/{b.CashSessionId}/close", new { countedAmount = "100" })); results[1].EnsureSuccessStatusCode();
        Assert.True(results[0].IsSuccessStatusCode || results[0].StatusCode == HttpStatusCode.Conflict);
        var detail = await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{b.CashSessionId}"); Assert.Equal(detail.GetProperty("balance").GetString(), detail.GetProperty("session").GetProperty("expectedAmount").GetString());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(results[0].IsSuccessStatusCode ? 1 : 3, await db.SportCatalogItems.Where(p => p.Id == b.ProductId).Select(p => p.StockQuantity).SingleAsync());
        Assert.Equal(results[0].IsSuccessStatusCode ? 1 : 0, await db.CashProductPostings.CountAsync(p => p.SessionId == b.CashSessionId));
    }
    [Fact]
    public async Task Cash_product_posting_and_sale_source_are_immutable_and_forged_amount_is_rejected()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var b = (await CashProductFixture(owner)).Body; (await Post(owner, SalesRoot, b)).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var p = await db.CashProductPostings.SingleAsync(p => p.SessionId == b.CashSessionId);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CashProductPostings\" SET \"Amount\"=1 WHERE \"Id\"={p.Id}"))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"ProductSales\" WHERE \"Id\"={p.ProductSaleId}"))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ProductSales\" SET \"Status\"='Rejected' WHERE \"Id\"={p.ProductSaleId}"))).SqlState);
        p.Amount++; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.Entry(p).State = EntityState.Unchanged;
        db.CashProductPostings.Add(new() { Id = Guid.NewGuid(), AcademyId = p.AcademyId, SessionId = p.SessionId, BranchId = p.BranchId, ProductSaleId = p.ProductSaleId, Amount = p.Amount + 1, Currency = p.Currency, ActorUserId = p.ActorUserId, RequestHash = new string('A', 64) });
        var e = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal("23514", Assert.IsType<PostgresException>(e.InnerException).SqlState);
    }
    [Fact]
    public async Task Cash_product_admin_requires_catalog_finance_grants_branch_and_own_shift_not_subscription_grants()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var f = await CashProductFixture(owner); var email = $"product.cash.{Guid.NewGuid():N}@example.test";
        var created = await Post(owner, "/api/v1/manage/staff", new { name = "أمين متجر اصطناعي", email, phone = "010" + Random.Shared.Next(10000000, 99999999), temporaryPassword = "Demo-Only-123!", role = "AcademyAdmin", jobTitle = "متجر" }); created.EnsureSuccessStatusCode(); var member = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        async Task Access(bool finance)
        {
            var actions = StaffAccessRules.Modules.Select(module => new { module, read = module == "catalog" || module == "finance" && finance, add = module == "catalog" || module == "finance" && finance, edit = false, delete = false });
            var csrf = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/manage/staff/{member}/access") { Content = JsonContent.Create(new { allBranches = false, branches = new[] { f.Body.BranchId }, actions }) };
            request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); (await owner.SendAsync(request)).EnsureSuccessStatusCode();
        }
        await Access(false); using var admin = await Staff(email); Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, SalesRoot, f.Body with { PaymentMethod = "Online" })).StatusCode);
        await Access(true); (await CashAssign(owner, f.Register, member)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/manage/structure/options")).StatusCode);
        var branches = (await admin.GetFromJsonAsync<JsonElement>(SalesRoot + "/options")).GetProperty("branches");
        Assert.Equal(f.Body.BranchId, Assert.Single(branches.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, SalesRoot, f.Body)).StatusCode);
        (await Post(owner, $"{CashRoot}/sessions/{f.Body.CashSessionId}/close", new { countedAmount = "100" })).EnsureSuccessStatusCode(); var shift = await CashOpen(admin, f.Register, "0");
        (await Post(admin, SalesRoot, f.Body with { CashSessionId = shift })).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var otherBranch = await db.Branches.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Id != f.Body.BranchId).Select(x => x.Id).FirstAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(ProductQuoteUrl(f.Body with { BranchId = otherBranch }))).StatusCode);
        var player = await db.SportEnrollments.Where(e => e.AcademyId == DemoSeed.NogoomAcademyId && e.BranchId == f.Body.BranchId && e.IsActive).Select(e => e.PlayerId).FirstAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(ProductQuoteUrl(f.Body with { PlayerId = player }))).StatusCode);
    }
}
