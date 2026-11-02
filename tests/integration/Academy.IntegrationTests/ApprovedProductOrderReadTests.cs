using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Content;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2ReceivablesTests
{
    private async Task<(Guid Product, Guid Branch, string Search)> ProductOrderListFixture()
    {
        var f = await Fixture();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var search = "طلبات اصطناعية " + Guid.NewGuid().ToString("N");
        var product = new SportCatalogItem { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, SportId = Slice2DemoSeed.FootballId,
            ArabicName = search, ImageReference = "/demo-assets/catalog-football.svg", DisplayPrice = 17, Currency = "EGP", StockQuantity = 10 };
        db.SportCatalogItems.Add(product);
        var user = await db.GuardianProfiles.Where(g => g.AcademyId == product.AcademyId && g.User.PhoneNumber == DemoSeed.GuardianPhone).Select(g => g.UserId).SingleAsync();
        var date = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 53; i++)
            db.ProductSales.Add(new ProductSale { Id = Guid.NewGuid(), AcademyId = product.AcademyId, ProductId = product.Id, RequestedByUserId = user,
                PlayerId = f.Player, SportEnrollmentId = f.Enrollment, SportId = product.SportId, BranchId = f.Branch,
                ProductName = product.ArabicName, SportName = "كرة القدم", BranchName = "فرع اصطناعي", Quantity = 1, UnitPrice = 17, Amount = 17, Currency = "EGP",
                Status = i == 52 ? "Rejected" : "Pending", ReviewReason = i == 52 ? "رفض اصطناعي" : null,
                IdempotencyKey = Guid.NewGuid().ToString(), CreatedAtUtc = date, UpdatedAtUtc = date });
        await db.SaveChangesAsync();
        return (product.Id, f.Branch, search);
    }

    [Fact]
    public async Task Approved_product_order_pages_are_stable_filtered_and_read_only_without_fabricated_collections()
    {
        var f = await ProductOrderListFixture();
        using var owner = await Staff(DemoSeed.OwnerEmail);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var before = (await db.Collections.CountAsync(), await db.CashProductPostings.CountAsync(), await db.ProductSales.CountAsync());
        var seen = new HashSet<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await owner.GetFromJsonAsync<JsonElement>($"{SalesRoot}?status=Pending&search={Uri.EscapeDataString(f.Search)}&page={page}&pageSize=25&branchId={f.Branch}&from=2026-09-28&to=2026-09-28");
            Assert.Equal(52, result.GetProperty("totalCount").GetInt32()); Assert.Equal(page, result.GetProperty("page").GetInt32());
            var rows = result.GetProperty("items").EnumerateArray().ToArray(); Assert.Equal(page == 3 ? 2 : 25, rows.Length);
            Assert.All(rows, row => { Assert.True(seen.Add(row.GetProperty("id").GetGuid())); Assert.Equal("Pending", row.GetProperty("status").GetString()); Assert.Equal(17, row.GetProperty("amount").GetDecimal()); Assert.False(row.TryGetProperty("proofUrl", out _)); });
            Assert.Equal(result.GetRawText(), (await owner.GetFromJsonAsync<JsonElement>($"{SalesRoot}?status=Pending&search={Uri.EscapeDataString(f.Search)}&page={page}&pageSize=25&branchId={f.Branch}&from=2026-09-28&to=2026-09-28")).GetRawText());
        }
        Assert.Equal(52, seen.Count);
        var rejected = await owner.GetFromJsonAsync<JsonElement>($"{SalesRoot}?status=Rejected&search={Uri.EscapeDataString(f.Search)}");
        Assert.Equal(1, rejected.GetProperty("totalCount").GetInt32());
        var otherDate = await owner.GetFromJsonAsync<JsonElement>($"{SalesRoot}?search={Uri.EscapeDataString(f.Search)}&to=2026-09-27"); Assert.Equal(0, otherDate.GetProperty("totalCount").GetInt32());
        var emptyPage = await owner.GetFromJsonAsync<JsonElement>($"{SalesRoot}?search={Uri.EscapeDataString(f.Search)}&page=4"); Assert.Empty(emptyPage.GetProperty("items").EnumerateArray());
        Assert.Equal(before, (await db.Collections.CountAsync(), await db.CashProductPostings.CountAsync(), await db.ProductSales.CountAsync()));
        Assert.Equal(10, await db.SportCatalogItems.Where(p => p.Id == f.Product).Select(p => p.StockQuantity).SingleAsync());
    }

    [Fact]
    public async Task Approved_product_order_list_does_not_trust_academy_query_or_cross_branch_staff_and_requires_read_permission()
    {
        var f = await ProductOrderListFixture(); using var owner = await Staff(DemoSeed.OwnerEmail);
        using var foreign = await Staff(DemoSeed.FutureOwnerEmail); using var guardian = await Guardian();
        var phone = "010" + Random.Shared.Next(10000000, 99999999);
        (await Post(owner, "/api/v1/manage/coaches", new { displayName = "مدرب عزل طلبات اصطناعي", phoneNumber = phone, isActive = true, groupIds = Array.Empty<Guid>() })).EnsureSuccessStatusCode();
        using var coach = factory.CreateClient();
        var challenge = await coach.PostAsJsonAsync("/api/v1/mobile/auth/otp/request", new { phoneNumber = phone }); challenge.EnsureSuccessStatusCode();
        var challengeId = (await challenge.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetGuid();
        var verified = await coach.PostAsJsonAsync("/api/v1/mobile/auth/otp/verify", new { phoneNumber = phone, challengeId, code = "246810", deviceName = "Product order test" }); verified.EnsureSuccessStatusCode();
        var credentials = (await verified.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal("Coach", credentials.GetProperty("memberships")[0].GetProperty("role").GetString());
        coach.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credentials.GetProperty("accessToken").GetString());
        var path = $"{SalesRoot}?search={Uri.EscapeDataString(f.Search)}&academyId={DemoSeed.NogoomAcademyId}";
        Assert.Equal(0, (await foreign.GetFromJsonAsync<JsonElement>(path)).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await coach.GetAsync(path)).StatusCode);
        var email = $"order.read.{Guid.NewGuid():N}@example.test";
        var created = await Post(owner, "/api/v1/manage/staff", new { name = "إداري طلبات اصطناعي", email, phone = "010" + Random.Shared.Next(10000000, 99999999), temporaryPassword = "Demo-Only-123!", role = "AcademyAdmin", jobTitle = "طلبات" });
        created.EnsureSuccessStatusCode(); var member = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var otherBranch = await db.Branches.Where(b => b.AcademyId == DemoSeed.NogoomAcademyId && b.Id != f.Branch && b.IsActive).Select(b => b.Id).FirstAsync();
        async Task Access(Guid branch, bool catalog)
        {
            var actions = StaffAccessRules.Modules.Select(module => new { module, read = module == "payments" || module == "catalog" && catalog, add = false, edit = false, delete = false });
            var csrf = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
            using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/manage/staff/{member}/access") { Content = JsonContent.Create(new { allBranches = false, branches = new[] { branch }, allSports = false, sports = new[] { Slice2DemoSeed.FootballId }, actions }) };
            request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); (await owner.SendAsync(request)).EnsureSuccessStatusCode();
        }
        await Access(otherBranch, true); using var admin = await Staff(email);
        Assert.Equal(0, (await admin.GetFromJsonAsync<JsonElement>(path)).GetProperty("totalCount").GetInt32());
        await Access(f.Branch, true);
        Assert.Equal(53, (await admin.GetFromJsonAsync<JsonElement>(path)).GetProperty("totalCount").GetInt32());
        await Access(f.Branch, false);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Approved_product_order_proof_notice_keeps_exact_owner_target_without_duplicate_or_collection()
    {
        var f = await ProductOrderListFixture(); using var guardian = await Guardian();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var sale = await db.ProductSales.FirstAsync(s => s.ProductId == f.Product && s.Status == "Pending");
        var proof = new PrivateAsset { Id = Guid.NewGuid(), AcademyId = sale.AcademyId, UploadedByUserId = sale.RequestedByUserId,
            ContentType = "image/png", StorageName = "synthetic-proof-only", Length = 16, Purpose = "Proof", CreatedAtUtc = sale.CreatedAtUtc, UpdatedAtUtc = sale.CreatedAtUtc };
        db.PrivateAssets.Add(proof); await db.SaveChangesAsync();
        var before = await db.Collections.CountAsync();
        for (var i = 0; i < 2; i++) (await Post(guardian, $"/api/v1/guardian/product-sales/{sale.Id}/proof", new { assetId = proof.Id })).EnsureSuccessStatusCode();
        var owners = await db.AcademyMemberships.Where(m => m.AcademyId == sale.AcademyId && m.IsActive && m.Role == Academy.Infrastructure.Tenancy.AcademyRole.AcademyOwner && m.User.IsActive).Select(m => m.UserId).Distinct().ToArrayAsync();
        var notices = await db.AcademyNotices.AsNoTracking().Where(n => n.TargetId == sale.Id).ToArrayAsync();
        Assert.NotEmpty(owners); Assert.Equal(owners.Length, notices.Length);
        Assert.All(notices, n => { Assert.Contains(n.UserId, owners); Assert.Equal("AcademyOwner", n.AudienceRole); Assert.Equal("ProductReview", n.TargetKind); Assert.Equal("Payment", n.EventKey); Assert.False(n.IsRead); Assert.DoesNotContain("{", n.Message); });
        Assert.Equal(before, await db.Collections.CountAsync());
        Assert.Equal("Pending", await db.ProductSales.AsNoTracking().Where(s => s.Id == sale.Id).Select(s => s.Status).SingleAsync());
        Assert.Equal(10, await db.SportCatalogItems.Where(p => p.Id == f.Product).Select(p => p.StockQuantity).SingleAsync());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("status=Paid")]
    [InlineData("from=2026-10-02&to=2026-10-01")]
    [InlineData("to=9999-12-31")]
    public async Task Approved_product_order_list_rejects_invalid_filters(string query)
    {
        using var owner = await Staff(DemoSeed.OwnerEmail);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.GetAsync($"{SalesRoot}?{query}")).StatusCode);
    }
}
