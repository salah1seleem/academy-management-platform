using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Infrastructure.Content;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2ReceivablesTests
{
    [Fact]
    public async Task Staff_can_find_the_guardian_product_reference_without_widening_payment_scope()
    {
        var fixture = await ProductOrderListFixture();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var order = await db.ProductSales.Where(s => s.ProductId == fixture.Product && s.Status == "Rejected").SingleAsync();
        var otherBranch = await db.Branches.Where(b => b.AcademyId == order.AcademyId && b.Id != fixture.Branch && b.IsActive).Select(b => b.Id).FirstAsync();
        var before = (await db.Collections.CountAsync(), await db.ProductSales.CountAsync(), await db.AcademyNotices.CountAsync());
        using var owner = await Staff(DemoSeed.OwnerEmail);
        using var foreign = await Staff(DemoSeed.FutureOwnerEmail);
        using var guardian = await Guardian();
        var family = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/product-sales/{order.Id}");
        var reference = family.GetProperty("reference").GetString()!;
        var paymentPath = $"/api/v1/manage/payment-requests?allDates=true&type=Product&status=Rejected&reference={reference}";
        foreach (var term in new[] { reference, $" {reference.ToLowerInvariant()} ", order.Id.ToString() })
        {
            var termQuery = Uri.EscapeDataString(term);
            var payment = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/payment-requests?allDates=true&reference={termQuery}");
            var row = Assert.Single(payment.GetProperty("items").EnumerateArray());
            Assert.Equal(order.Id, row.GetProperty("id").GetGuid());
            Assert.Equal("Product", row.GetProperty("type").GetString());
            Assert.Equal(reference, row.GetProperty("reference").GetString());
            Assert.Equal(17, row.GetProperty("amount").GetDecimal());
            var listing = await owner.GetFromJsonAsync<JsonElement>($"{SalesRoot}?search={termQuery}");
            var item = Assert.Single(listing.GetProperty("items").EnumerateArray());
            Assert.Equal(order.Id, item.GetProperty("id").GetGuid());
            Assert.Equal(reference, item.GetProperty("reference").GetString());
            Assert.Equal(family.GetProperty("createdOn").GetString(), item.GetProperty("createdOn").GetString());
        }
        var details = await owner.GetFromJsonAsync<JsonElement>($"{SalesRoot}/{order.Id}");
        Assert.Equal(reference, details.GetProperty("reference").GetString());
        Assert.Equal(family.GetProperty("createdOn").GetString(), details.GetProperty("createdOn").GetString());
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(paymentPath + $"&branchId={otherBranch}")).GetProperty("items").EnumerateArray());
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(paymentPath.Replace("status=Rejected", "status=Pending"))).GetProperty("items").EnumerateArray());
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(paymentPath.Replace("allDates=true", "from=2026-09-29&to=2026-09-29"))).GetProperty("items").EnumerateArray());
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(paymentPath.Replace(reference, "000000000000"))).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.GetAsync(paymentPath.Replace(reference, "123"))).StatusCode);
        Assert.Empty((await foreign.GetFromJsonAsync<JsonElement>(paymentPath + $"&academyId={order.AcademyId}")).GetProperty("items").EnumerateArray());
        Assert.Empty((await foreign.GetFromJsonAsync<JsonElement>($"{SalesRoot}?search={reference}")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"{SalesRoot}/{order.Id}")).StatusCode);
        Assert.Equal(before, (await db.Collections.CountAsync(), await db.ProductSales.CountAsync(), await db.AcademyNotices.CountAsync()));
        Assert.Equal(10, await db.SportCatalogItems.Where(p => p.Id == fixture.Product).Select(p => p.StockQuantity).SingleAsync());
    }

    [Fact]
    public async Task Guardian_product_history_pages_keep_reference_academy_date_and_household_scope()
    {
        const string path = "/api/v1/guardian/product-sales";
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var academy = DemoSeed.NogoomAcademyId;
        var user = await db.GuardianProfiles.Where(g => g.AcademyId == academy && g.User.PhoneNumber == DemoSeed.GuardianPhone)
            .Select(g => g.UserId).SingleAsync();
        var otherUser = await db.GuardianProfiles.Where(g => g.AcademyId == academy && g.User.PhoneNumber == "+201000000002")
            .Select(g => new { g.UserId, g.User.PhoneNumber }).FirstAsync();
        var product = new SportCatalogItem
        {
            Id = Guid.NewGuid(), AcademyId = academy, SportId = Slice2DemoSeed.FootballId,
            ArabicName = "زجاجة مياه اختبار السجل", ImageReference = "/demo-assets/catalog-football.svg",
            DisplayPrice = 17, Currency = "EGP", StockQuantity = 10
        };
        db.SportCatalogItems.Add(product);
        var date = new DateTimeOffset(2026, 11, 12, 22, 30, 0, TimeSpan.Zero);
        for (var i = 0; i <= 101; i++)
        {
            var id = Guid.NewGuid();
            db.ProductSales.Add(new ProductSale
            {
                Id = id, AcademyId = academy, ProductId = product.Id, SportId = product.SportId,
                RequestedByUserId = i == 101 ? otherUser.UserId : user, ProductName = product.ArabicName, SportName = "كرة القدم",
                Quantity = 1, UnitPrice = 17, Amount = 17, Currency = "EGP", Status = "Pending",
                IdempotencyKey = id.ToString(), CreatedAtUtc = date, UpdatedAtUtc = date
            });
        }
        await db.SaveChangesAsync();
        var all = db.ProductSales.Where(s => s.AcademyId == academy && s.RequestedByUserId == user);
        var expected = await all.OrderByDescending(s => s.CreatedAtUtc).ThenBy(s => s.Id).Select(s => s.Id).ToArrayAsync();
        var before = (await db.ProductSales.CountAsync(), await db.Collections.CountAsync(), await db.CashProductPostings.CountAsync());
        using var guardian = await Guardian();
        using var stranger = await Guardian(otherUser.PhoneNumber);
        using var foreignGuardian = await Guardian("+201000000003");
        using var foreignOwner = await Staff(DemoSeed.FutureOwnerEmail);
        var seen = new List<Guid>();
        var references = new HashSet<string>();
        for (var page = 1; page <= (expected.Length + 24) / 25; page++)
        {
            using var response = await guardian.GetAsync($"{path}?page={page}&pageSize=25&academyId={DemoSeed.FutureAcademyId}");
            response.EnsureSuccessStatusCode();
            Assert.True(response.Headers.CacheControl?.NoStore);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(page, result.GetProperty("page").GetInt32());
            Assert.Equal(expected.Length > page * 25, result.GetProperty("hasMore").GetBoolean());
            foreach (var row in result.GetProperty("items").EnumerateArray())
            {
                var id = row.GetProperty("id").GetGuid();
                seen.Add(id);
                Assert.True(references.Add(row.GetProperty("reference").GetString()!));
                if (row.GetProperty("productId").GetGuid() != product.Id) continue;
                Assert.Equal("2026-11-13", row.GetProperty("createdOn").GetString());
                Assert.Equal(17, row.GetProperty("amount").GetDecimal());
                var details = await guardian.GetFromJsonAsync<JsonElement>($"{path}/{id}");
                Assert.Equal(row.GetProperty("reference").GetString(), details.GetProperty("reference").GetString());
                Assert.Equal(row.GetProperty("createdOn").GetString(), details.GetProperty("createdOn").GetString());
                Assert.False(details.GetProperty("hasProof").GetBoolean());
            }
        }
        Assert.Equal(expected, seen);
        var legacy = await guardian.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(expected.Take(100), legacy.EnumerateArray().Select(r => r.GetProperty("id").GetGuid()));
        var next = await guardian.GetFromJsonAsync<JsonElement>($"{path}?page=100000");
        Assert.Empty(next.GetProperty("items").EnumerateArray());
        Assert.False(next.GetProperty("hasMore").GetBoolean());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await guardian.GetAsync($"{path}?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await guardian.GetAsync($"{path}?pageSize=101")).StatusCode);
        var other = await stranger.GetFromJsonAsync<JsonElement>($"{path}?page=1&academyId={academy}");
        Assert.DoesNotContain(other.GetProperty("items").EnumerateArray(), row => expected.Contains(row.GetProperty("id").GetGuid()));
        var first = $"{path}/{expected[0]}";
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(first)).StatusCode);
        var foreign = await foreignGuardian.GetFromJsonAsync<JsonElement>($"{path}?page=1&academyId={academy}");
        Assert.DoesNotContain(foreign.GetProperty("items").EnumerateArray(), row => expected.Contains(row.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.NotFound, (await foreignGuardian.GetAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await foreignOwner.GetAsync($"{path}?page=1&academyId={academy}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await foreignOwner.GetAsync(first)).StatusCode);
        Assert.Equal(before, (await db.ProductSales.CountAsync(), await db.Collections.CountAsync(), await db.CashProductPostings.CountAsync()));
        Assert.Equal(10, await db.SportCatalogItems.Where(p => p.Id == product.Id).Select(p => p.StockQuantity).SingleAsync());
    }

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
        var order = await db.ProductSales.Where(s => s.ProductId == f.Product && s.Status == "Pending").Select(s => s.Id).FirstAsync();
        var source = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/product-sales/{order}");
        var reference = source.GetProperty("reference").GetString();
        var queueSearch = $"/api/v1/manage/payment-requests?allDates=true&type=Product&reference={reference}";
        await Access(otherBranch, true); using var admin = await Staff(email);
        Assert.Equal(0, (await admin.GetFromJsonAsync<JsonElement>(path)).GetProperty("totalCount").GetInt32());
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>(queueSearch)).GetProperty("items").EnumerateArray());
        await Access(f.Branch, true);
        Assert.Equal(53, (await admin.GetFromJsonAsync<JsonElement>(path)).GetProperty("totalCount").GetInt32());
        Assert.Equal(order, Assert.Single((await admin.GetFromJsonAsync<JsonElement>(queueSearch)).GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var readable = await admin.GetFromJsonAsync<JsonElement>($"{SalesRoot}/{order}");
        Assert.False(readable.GetProperty("canReview").GetBoolean());
        Assert.False(readable.GetProperty("canAccept").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, $"{SalesRoot}/{order}/review", new ProductReviewWrite(false, "رفض اصطناعي"))).StatusCode);
        await Access(f.Branch, false);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Approved_product_order_review_uses_the_current_proof_and_the_saved_decision()
    {
        var fixture = await ProductOrderListFixture();
        using var guardian = await Guardian();
        using var owner = await Staff(DemoSeed.OwnerEmail);
        using var foreignOwner = await Staff(DemoSeed.FutureOwnerEmail);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var orders = await db.ProductSales.Where(s => s.ProductId == fixture.Product && s.Status == "Pending").Take(2).ToArrayAsync();
        var first = orders[0];
        var rejected = orders[1];
        var foreign = await db.Users.Where(u => u.Id != first.RequestedByUserId).Select(u => u.Id).FirstAsync();
        var now = first.CreatedAtUtc;
        PrivateAsset Asset(Guid user) => new() {
            Id = Guid.NewGuid(), AcademyId = first.AcademyId, UploadedByUserId = user, ContentType = "image/png",
            StorageName = Guid.NewGuid().ToString(), Length = 16, Purpose = "Proof", CreatedAtUtc = now, UpdatedAtUtc = now
        };
        var oldProof = Asset(first.RequestedByUserId);
        var currentProof = Asset(first.RequestedByUserId);
        var otherProof = Asset(foreign);
        db.PrivateAssets.AddRange(oldProof, currentProof, otherProof);
        await db.SaveChangesAsync();
        string ReviewPath(ProductSale order) => $"{SalesRoot}/{order.Id}/review";
        string ProofPath(ProductSale order) => $"/api/v1/guardian/product-sales/{order.Id}/proof";
        string GuardianPath(ProductSale order) => $"/api/v1/guardian/product-sales/{order.Id}";

        var count = await db.Collections.CountAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, ReviewPath(first), new ProductReviewWrite(false, "مراجعة"))).StatusCode);
        Assert.False((await guardian.GetFromJsonAsync<JsonElement>(GuardianPath(first))).GetProperty("hasProof").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await Post(guardian, ProofPath(first), new { assetId = otherProof.Id })).StatusCode);
        (await Post(guardian, ProofPath(first), new { assetId = oldProof.Id })).EnsureSuccessStatusCode();
        (await Post(guardian, ProofPath(first), new { assetId = currentProof.Id })).EnsureSuccessStatusCode();
        Assert.True((await guardian.GetFromJsonAsync<JsonElement>(GuardianPath(first))).GetProperty("hasProof").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, ReviewPath(first), new ProductReviewWrite(true, null, oldProof.Id))).StatusCode);
        Assert.Equal("Pending", (await guardian.GetFromJsonAsync<JsonElement>(GuardianPath(first))).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await foreignOwner.GetAsync($"{SalesRoot}/{first.Id}")).StatusCode);
        Assert.Equal(10, await db.SportCatalogItems.Where(p => p.Id == fixture.Product).Select(p => p.StockQuantity).SingleAsync());

        var accept = new ProductReviewWrite(true, "تحويل اختباري مطابق", currentProof.Id);
        (await Post(owner, ReviewPath(first), accept)).EnsureSuccessStatusCode();
        (await Post(owner, ReviewPath(first), accept)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, ReviewPath(first), accept with { Reason = "سبب بديل" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(guardian, ProofPath(first), new { assetId = oldProof.Id })).StatusCode);
        (await Post(guardian, ProofPath(first), new { assetId = currentProof.Id })).EnsureSuccessStatusCode();

        (await Post(guardian, ProofPath(rejected), new { assetId = oldProof.Id })).EnsureSuccessStatusCode();
        var reject = new ProductReviewWrite(false, "الإثبات غير مطابق", oldProof.Id);
        (await Post(owner, ReviewPath(rejected), reject)).EnsureSuccessStatusCode();
        var result = await guardian.GetFromJsonAsync<JsonElement>(GuardianPath(rejected));
        Assert.Equal("Rejected", result.GetProperty("status").GetString());
        Assert.Equal(reject.Reason, result.GetProperty("reviewReason").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, ReviewPath(rejected), reject with { Accept = true })).StatusCode);
        Assert.Equal(9, await db.SportCatalogItems.Where(p => p.Id == fixture.Product).Select(p => p.StockQuantity).SingleAsync());
        Assert.Equal(count, await db.Collections.CountAsync());
        Assert.Equal(2, await db.AcademyNotices.CountAsync(n => n.TargetKind == "Product" && (n.TargetId == first.Id || n.TargetId == rejected.Id)));
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
