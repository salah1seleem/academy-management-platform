using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed partial class V2ReceivablesTests : IAsyncLifetime
{
    private Factory factory = null!;
    public async Task InitializeAsync()
    {
        factory = new(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Isolated test database required")); _ = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        // Repair only this fixture's pre-existing synthetic collections from its earlier test run.
        // Every confirmed collection must also have a receipt, as required by the shared reports.
        var rows = await db.Collections.Include(x => x.SportEnrollment).Include(x => x.RenewalRequest)
            .Where(x => x.ConfirmedBy == "synthetic-test" && !db.Receipts.Any(r => r.CollectionId == x.Id)).ToListAsync();
        foreach (var row in rows) db.Receipts.Add(TestReceipt(row));
        await db.SaveChangesAsync();
    }
    public async Task DisposeAsync() => await factory.DisposeAsync();
    private async Task<(Guid Player, Guid Enrollment, Guid Branch, Guid Owner)> Fixture()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var group = await db.TrainingGroups.FirstAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.SportId == Slice2DemoSeed.FootballId && x.IsActive && x.Branch.IsActive);
        var owner = await db.Users.Where(x => x.Email == DemoSeed.OwnerEmail).Select(x => x.Id).SingleAsync();
        var player = new Player { Id = Guid.NewGuid(), AcademyId = group.AcademyId, ArabicName = "لاعب أرصدة اصطناعي", PlayerCode = "LED-" + Guid.NewGuid().ToString("N")[..12], DateOfBirth = new(2017, 1, 1) };
        var enrollment = new SportEnrollment { Id = Guid.NewGuid(), AcademyId = group.AcademyId, PlayerId = player.Id, SportId = group.SportId, BranchId = group.BranchId, TrainingGroupId = group.Id };
        var guardian = await db.GuardianProfiles.Where(x => x.AcademyId == group.AcademyId && x.User.PhoneNumber == DemoSeed.GuardianPhone).Select(x => x.Id).SingleAsync();
        db.Players.Add(player); db.SportEnrollments.Add(enrollment); db.GuardianPlayerLinks.Add(new() { Id = Guid.NewGuid(), AcademyId = group.AcademyId, PlayerId = player.Id, GuardianId = guardian, CreatedByUserId = owner }); await db.SaveChangesAsync();
        return (player.Id, enrollment.Id, group.BranchId, owner);
    }
    [Fact]
    public async Task FIN_02_posted_records_cards_rows_and_csv_share_exact_asOf_and_no_fake_cash()
    {
        var f = await Fixture(); using var owner = await Staff(DemoSeed.OwnerEmail);
        var a = await Charge(owner, f.Enrollment, 540); var b = await Charge(owner, f.Enrollment, 700); var c = await Charge(owner, f.Enrollment, 1000); await Charge(owner, f.Enrollment, 500, future: true);
        var collection = await Collection(f.Enrollment, f.Owner, 900);
        (await Move(owner, a, "PaymentAllocation", 200, collection)).EnsureSuccessStatusCode();
        (await Move(owner, b, "PaymentAllocation", 700, collection)).EnsureSuccessStatusCode();
        (await Move(owner, c, "CreditNote", 100)).EnsureSuccessStatusCode();
        var report = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/receivables?playerId={f.Player}");
        Assert.Equal("1740.00", report.GetProperty("outstanding").GetString()); Assert.Equal("1240.00", report.GetProperty("overdue").GetString()); Assert.Equal("500.00", report.GetProperty("notYetDue").GetString());
        Assert.Equal(4, report.GetProperty("items").GetArrayLength());
        Assert.Equal(1740, report.GetProperty("items").EnumerateArray().Sum(x => decimal.Parse(x.GetProperty("outstanding").GetString()!, System.Globalization.CultureInfo.InvariantCulture)));
        var asOf = Uri.EscapeDataString(report.GetProperty("asOf").GetString()!);
        var csv = await owner.GetStringAsync($"/api/v1/finance/receivables/export?playerId={f.Player}&asOf={asOf}");
        Assert.Contains("340.00", csv); Assert.Contains("900.00", csv); Assert.Contains("500.00", csv);
        await Charge(owner, f.Enrollment, 123);
        Assert.Equal(csv, await owner.GetStringAsync($"/api/v1/finance/receivables/export?playerId={f.Player}&asOf={asOf}"));
        await using var s = factory.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, await db.Collections.CountAsync(x => x.SportEnrollmentId == f.Enrollment));
        Assert.False(await db.SubscriptionPeriods.AnyAsync(x => x.SportEnrollmentId == f.Enrollment)); // opening charge does not activate
    }
    [Fact]
    public async Task Historical_price_and_posted_branch_are_immutable_and_opening_balance_is_not_cash()
    {
        var f = await Fixture(); using var owner = await Staff(DemoSeed.OwnerEmail); await using var s = factory.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var plan = await db.SubscriptionPlans.SingleAsync(x => x.Id == Slice3DemoSeed.FootballMonthlyId); var original = plan.Price;
        var doc = await Charge(owner, f.Enrollment, 350, plan: plan.Id);
        try
        {
            plan.Price += 100; await db.SaveChangesAsync();
            var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/receivables/{doc}"); Assert.Equal(original, decimal.Parse(detail.GetProperty("document").GetProperty("planPriceSnapshot").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
            var enrollment = await db.SportEnrollments.SingleAsync(x => x.Id == f.Enrollment);
            var another = await db.TrainingGroups.FirstAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.BranchId != f.Branch && x.SportId == enrollment.SportId && x.IsActive);
            // Branch is now part of the enrollment's relational identity. Do not bypass
            // dated transfer workflows by mutating a key in this older ledger fixture.
            enrollment.BranchId = another.BranchId;
            await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
            enrollment.BranchId = f.Branch;
            Assert.Equal(f.Branch, (await db.ReceivableDocuments.SingleAsync(x => x.Id == doc)).BranchId);
            await using (var branchScope = factory.Services.CreateAsyncScope())
            {
                var scoped = branchScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
                scoped.BindRequestScope(DemoSeed.NogoomAcademyId, f.Owner, Guid.NewGuid(), [f.Branch]);
                Assert.True(await scoped.ReceivableDocuments.AnyAsync(x => x.Id == doc));
                Assert.True(await scoped.SportEnrollments.AnyAsync(x => x.Id == f.Enrollment));
            }
            var row = await db.ReceivableDocuments.SingleAsync(x => x.Id == doc); row.Amount = 1;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.Entry(row).State = EntityState.Unchanged;
            var failure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ReceivableDocuments\" SET \"Amount\"=1 WHERE \"Id\"={doc}")); Assert.Equal("23514", failure.SqlState);
            Assert.False(await db.Collections.AnyAsync(x => x.SportEnrollmentId == f.Enrollment));
        }
        finally { plan.Price = original; await db.SaveChangesAsync(); }
    }
    [Fact]
    public async Task Demo_golden_seed_is_repeatable_and_creates_no_new_cash()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var cash = await db.Collections.CountAsync(); var periods = await db.SubscriptionPeriods.CountAsync();
        await Academy.Api.Finance.ReceivableDemoSeed.SeedAsync(db); await Academy.Api.Finance.ReceivableDemoSeed.SeedAsync(db);
        Assert.Equal(cash, await db.Collections.CountAsync()); Assert.Equal(periods, await db.SubscriptionPeriods.CountAsync());
        var docs = await db.ReceivableDocuments.Where(x => x.SourceKey.StartsWith("v2-demo-fin02-")).ToListAsync();
        if (docs.Count == 0)
        {
            // A reused test database may already have refunded the reserved demo collection.
            // The seed must refuse reconstruction rather than inventing replacement cash.
            var bytes = Guid.Parse("53000000-0000-0000-0000-000000000002").ToByteArray(); bytes[14] = 3; var reserved = new Guid(bytes);
            Assert.True(!await db.Collections.AnyAsync(x => x.Id == reserved && x.Amount == 900m && x.Currency == "EGP") || await db.CollectionRefunds.AnyAsync(x => x.CollectionId == reserved) || await db.ReceivableMovements.AnyAsync(x => x.CollectionId == reserved));
            return;
        }
        Assert.Equal(4, docs.Count);
        var ids = docs.Select(x => x.Id).ToArray(); var moves = await db.ReceivableMovements.Where(x => ids.Contains(x.DocumentId)).ToListAsync(); Assert.Equal(3, moves.Count);
        Assert.Equal(1740m, docs.Sum(x => x.Amount) - moves.Sum(x => x.Amount));
    }
    [Fact]
    public async Task Replayed_create_and_concurrent_allocations_cannot_duplicate_or_overspend()
    {
        var f = await Fixture(); using var owner = await Staff(DemoSeed.OwnerEmail); var key = Guid.NewGuid().ToString();
        var body = new { sportEnrollmentId = f.Enrollment, amount = "200.00", dueDate = "2026-09-20", source = "ManualCharge", description = "اختبار عدم التكرار" };
        var one = await Post(owner, "/api/v1/finance/receivables/", body, key); one.EnsureSuccessStatusCode(); var id = (await one.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var again = await Post(owner, "/api/v1/finance/receivables/", body, key); again.EnsureSuccessStatusCode(); Assert.Equal(id, (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, "/api/v1/finance/receivables/", new { body.sportEnrollmentId, amount = "201.00", body.dueDate, body.source, body.description }, key)).StatusCode);
        var other = await Charge(owner, f.Enrollment, 200); var collection = await Collection(f.Enrollment, f.Owner, 100);
        var results = await Task.WhenAll(Move(owner, id, "PaymentAllocation", 80, collection), Move(owner, other, "PaymentAllocation", 80, collection));
        Assert.Single(results, x => x.IsSuccessStatusCode); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var s = factory.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Equal(80, await db.ReceivableMovements.Where(x => x.CollectionId == collection).SumAsync(x => x.Amount));
    }
    [Fact]
    public async Task Tenant_and_guardian_isolation_reject_foreign_documents_and_collection_allocations()
    {
        var f = await Fixture(); using var owner = await Staff(DemoSeed.OwnerEmail); using var foreign = await Staff(DemoSeed.FutureOwnerEmail); using var admin = await Staff(DemoSeed.AdminEmail);
        var doc = await Charge(owner, f.Enrollment, 100);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/api/v1/finance/receivables/{doc}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Move(foreign, doc, "CreditNote", 10)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/finance/receivables")).StatusCode);
        using var guardian = await Guardian(); var balances = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/guardian/receivables"); Assert.Contains(balances.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == doc);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync($"/api/v1/finance/receivables/{doc}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Move(guardian, doc, "CreditNote", 10)).StatusCode);
        await using var s = factory.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var unrelated = await db.SportEnrollments.FirstAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.PlayerId == Slice2DemoSeed.OtherPlayerId); var unlinkedDoc = await Charge(owner, unrelated.Id, 111);
        balances = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/guardian/receivables"); Assert.DoesNotContain(balances.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == unlinkedDoc);
        var collection = await Collection(unrelated.Id, f.Owner, 100);
        Assert.Equal(HttpStatusCode.NotFound, (await Move(owner, doc, "PaymentAllocation", 10, collection)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(owner, "/api/v1/finance/receivables/", new { sportEnrollmentId = f.Enrollment, amount = ".001", dueDate = "2026-09-28", source = "ManualCharge", description = "كسور غير صالحة" })).StatusCode);
        (await Post(owner, "/api/v1/finance/receivables/", new { sportEnrollmentId = f.Enrollment, amount = "1.00", dueDate = "2026-09-20", source = "ManualCharge", description = "\n =1+1" })).EnsureSuccessStatusCode();
        Assert.Contains("\"'=1+1\"", await owner.GetStringAsync($"/api/v1/finance/receivables/export?playerId={f.Player}"));
    }
    [Fact]
    public async Task Read_only_branch_admin_can_read_posted_scope_but_cannot_create_or_correct()
    {
        var f = await Fixture(); using var owner = await Staff(DemoSeed.OwnerEmail); var doc = await Charge(owner, f.Enrollment, 100);
        var stamp = Guid.NewGuid().ToString("N"); var email = $"ledger.scope.{stamp}@example.test";
        var phone = $"010{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 100000000:00000000}";
        var created = await Post(owner, "/api/v1/manage/staff/", new { name = "إداري أرصدة اصطناعي", email, phone, temporaryPassword = "Demo-Only-123!", role = "AcademyAdmin", jobTitle = "اختبار" }); created.EnsureSuccessStatusCode();
        var member = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var access = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff/{member}/access");
        var actions = access.GetProperty("modules").EnumerateArray().Select(x => new { module = x.GetString(), read = x.GetString() == "finance", add = false, edit = false, delete = false }).ToArray();
        var csrf = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); var update = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/manage/staff/{member}/access") { Content = JsonContent.Create(new { allBranches = false, branches = new[] { f.Branch }, actions }) }; update.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); (await owner.SendAsync(update)).EnsureSuccessStatusCode();
        using var admin = await Staff(email);
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/v1/finance/receivables"); Assert.All(list.GetProperty("items").EnumerateArray(), x => Assert.Equal(f.Branch, x.GetProperty("branchId").GetGuid()));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/finance/receivables/{doc}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Move(admin, doc, "CreditNote", 10)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, "/api/v1/finance/receivables/", new { sportEnrollmentId = f.Enrollment, amount = "20.00", dueDate = "2026-09-20", source = "OpeningBalance", description = "مرفوض" })).StatusCode);
    }
    [Fact]
    public async Task Refund_does_not_silently_recreate_debt_and_excess_payment_is_separate_credit()
    {
        var f = await Fixture(); using var owner = await Staff(DemoSeed.OwnerEmail); var doc = await Charge(owner, f.Enrollment, 100); var collection = await Collection(f.Enrollment, f.Owner, 140);
        (await Move(owner, doc, "PaymentAllocation", 120, collection)).EnsureSuccessStatusCode();
        await using var s = factory.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<FoundationDbContext>();
        db.CollectionRefunds.Add(new CollectionRefund { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, CollectionId = collection, PlayerId = f.Player, Amount = 20, Currency = "EGP", PerformedByUserId = f.Owner, IdempotencyKey = Guid.NewGuid().ToString() }); await db.SaveChangesAsync();
        var report = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/receivables?playerId={f.Player}"); Assert.Equal("0.00", report.GetProperty("outstanding").GetString()); Assert.Equal("20.00", report.GetProperty("overpaymentCredit").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Move(owner, doc, "PaymentAllocation", 1, collection)).StatusCode);
    }
    private async Task<Guid> Charge(HttpClient client, Guid enrollment, decimal amount, bool future = false, Guid? plan = null)
    {
        var response = await Post(client, "/api/v1/finance/receivables/", new { sportEnrollmentId = enrollment, planId = plan, amount, dueDate = future ? "2026-10-01" : "2026-09-20", source = "OpeningBalance", description = "رصيد افتتاحي صناعي" }); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private Task<HttpResponseMessage> Move(HttpClient client, Guid doc, string kind, decimal amount, Guid? collection = null) => Post(client, $"/api/v1/finance/receivables/{doc}/movements", new { kind, amount, collectionId = collection, reason = "حركة اختبار موثقة" });
    private async Task<Guid> Collection(Guid enrollment, Guid owner, decimal amount)
    {
        await using var s = factory.Services.CreateAsyncScope(); var db = s.ServiceProvider.GetRequiredService<FoundationDbContext>(); var e = await db.SportEnrollments.SingleAsync(x => x.Id == enrollment); var id = Guid.NewGuid(); var key = id.ToString(); var now = new DateTimeOffset(2026, 9, 28, 6, 0, 0, TimeSpan.Zero);
        var renewal = new RenewalRequest { Id = Guid.NewGuid(), AcademyId = e.AcademyId, SportEnrollmentId = e.Id, SportId = e.SportId, SubscriptionPlanId = Slice3DemoSeed.FootballMonthlyId, RequestedByUserId = owner, RequestedAtUtc = now, OriginalAmount = amount, FinalAmount = amount, AmountExpected = amount, Currency = "EGP", Status = RenewalRequestStatus.Paid, IdempotencyKey = key };
        var payment = new PaymentRequest { Id = Guid.NewGuid(), AcademyId = e.AcademyId, RenewalRequestId = renewal.Id, Amount = amount, Currency = "EGP", Status = PaymentRequestStatus.Confirmed, Provider = "InternalTest", ProviderEnvironment = "Demo", ProviderReference = key, CheckoutReference = key, ConfirmedAtUtc = now };
        var collection = new PaymentCollection { Id = id, AcademyId = e.AcademyId, SportEnrollmentId = e.Id, SportEnrollment = e, RenewalRequestId = renewal.Id, RenewalRequest = renewal, PaymentRequestId = payment.Id, Amount = amount, Currency = "EGP", PaymentMethod = "InternalTest", Provider = "InternalTest", ProviderReference = key, ConfirmedAtUtc = now, ConfirmedBy = "synthetic-test" };
        db.RenewalRequests.Add(renewal); db.PaymentRequests.Add(payment); db.Collections.Add(collection); db.Receipts.Add(TestReceipt(collection)); await db.SaveChangesAsync(); return id;
    }
    private static Receipt TestReceipt(PaymentCollection c) => new() { Id = Guid.NewGuid(), AcademyId = c.AcademyId, CollectionId = c.Id, ReceiptNumber = "TEST-" + c.Id.ToString("N"), PlayerId = c.SportEnrollment.PlayerId, SportEnrollmentId = c.SportEnrollmentId, SubscriptionPlanId = c.RenewalRequest.SubscriptionPlanId, PlayerNameSnapshot = "لاعب أرصدة اصطناعي", SportNameSnapshot = "كرة القدم", PlanNameSnapshot = "باقة اختبار أرصدة", OriginalAmount = c.Amount, FinalAmount = c.Amount, Amount = c.Amount, Currency = c.Currency, PaidAtUtc = c.ConfirmedAtUtc, PaymentMethod = "InternalTest", ProviderReference = c.ProviderReference };
    private async Task<HttpClient> Staff(string email) { var c = factory.CreateClient(); (await Post(c, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" })).EnsureSuccessStatusCode(); return c; }
    private async Task<HttpClient> Guardian(string? phone = null)
    {
        phone ??= DemoSeed.GuardianPhone;
        var c = factory.CreateClient(); var challenge = await Post(c, "/api/v1/auth/guardian/otp/request", new { phoneNumber = phone }); challenge.EnsureSuccessStatusCode(); var id = (await challenge.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetGuid(); (await Post(c, "/api/v1/auth/guardian/otp/verify", new { challengeId = id, phoneNumber = phone, code = "246810" })).EnsureSuccessStatusCode(); return c;
    }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body, string? key = null) { var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); var r = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; r.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); r.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString()); return await client.SendAsync(r); }
    private sealed class Factory(string connection) : WebApplicationFactory<Program> { protected override void ConfigureWebHost(IWebHostBuilder b) { b.UseEnvironment("Demo"); b.UseSetting("ConnectionStrings:Default", connection); b.UseSetting("Demo:SeedProfile", "LegacyRegression"); b.UseSetting("Demo:SeedEnabled", "true"); b.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); b.UseSetting("Demo:ReferenceDate", "2026-09-28"); b.UseSetting("Demo:FixedOtpEnabled", "true"); b.UseSetting("Demo:FixedOtp", "246810"); } }
}
