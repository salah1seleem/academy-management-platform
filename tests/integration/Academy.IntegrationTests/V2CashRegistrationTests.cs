using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2ReceivablesTests
{
    private async Task<(RegistrationRequest Body, Guid Shift, Guid Register, Guid Member)> CashRegistrationFixture(HttpClient owner)
    {
        var f = await CashCollectionFixture(owner);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var category = new Academy.Infrastructure.Structure.AgeCategory { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId,
            ArabicName = "عمرية تسجيل نقدي اصطناعية " + Guid.NewGuid(), MinimumBirthYear = 2010, MaximumBirthYear = 2020 };
        var group = new Academy.Infrastructure.Structure.TrainingGroup { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId,
            ArabicName = "مجموعة تسجيل نقدي اصطناعية " + Guid.NewGuid(), BranchId = f.Branch, SportId = Slice2DemoSeed.FootballId, AgeCategoryId = category.Id, Capacity = 2 };
        db.AgeCategories.Add(category); db.TrainingGroups.Add(group); await db.SaveChangesAsync();
        var phone = "010" + Random.Shared.Next(10000000, 99999999);
        var quote = await Post(owner, "/api/v1/people/registration-quote", new { planId = Slice3DemoSeed.FootballMonthlyId, branchId = f.Branch, guardianPhone = phone }); quote.EnsureSuccessStatusCode();
        var amount = decimal.Parse((await quote.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("finalAmount").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        return (new(null, "لاعب تسجيل نقدي اصطناعي " + Guid.NewGuid().ToString("N"), new(2017, 1, 1), null, null, "ولي نقدي اصطناعي", phone, null,
            f.Branch, Slice2DemoSeed.FootballId, group.Id, Slice3DemoSeed.FootballMonthlyId, "Cash", ExpectedAmount: amount, CashSessionId: f.Session), f.Session, f.Register, f.Member);
    }

    [Fact]
    public async Task Cash_registration_commits_player_service_receipt_and_posting_once_and_replays_closed_shift()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var f = await CashRegistrationFixture(owner); var key = Guid.NewGuid().ToString();
        var options = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/registration-cash-options?branchId={f.Body.BranchId}&planId={f.Body.PlanId}");
        Assert.Contains(options.GetProperty("sessions").EnumerateArray(), s => s.GetProperty("id").GetGuid() == f.Shift);
        var results = await Task.WhenAll(Post(owner, "/api/v1/people/registrations", f.Body, key), Post(owner, "/api/v1/people/registrations", f.Body, key));
        Assert.All(results, r => r.EnsureSuccessStatusCode());
        var player = (await results[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid();
        Assert.Equal(player, (await results[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var enrollment = Assert.Single(await db.SportEnrollments.Where(e => e.PlayerId == player).ToListAsync());
        var collection = Assert.Single(await db.Collections.Where(c => c.SportEnrollmentId == enrollment.Id).ToListAsync());
        var receipt = Assert.Single(await db.Receipts.Where(r => r.PlayerId == player).ToListAsync());
        var posting = Assert.Single(await db.CashReceiptPostings.Where(p => p.SessionId == f.Shift).ToListAsync());
        Assert.Equal(collection.Id, posting.CollectionId); Assert.Equal(receipt.Id, posting.ReceiptId); Assert.Equal(f.Body.ExpectedAmount, posting.Amount);
        Assert.Single(await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == enrollment.Id).ToListAsync());
        Assert.Single(await db.GuardianPlayerLinks.Where(l => l.PlayerId == player && l.IsActive).ToListAsync());
        var detail = await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{f.Shift}");
        Assert.Equal(100m + posting.Amount, decimal.Parse(detail.GetProperty("balance").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        (await Post(owner, $"{CashRoot}/sessions/{f.Shift}/close", new { countedAmount = detail.GetProperty("balance").GetString() })).EnsureSuccessStatusCode();
        (await Post(owner, "/api/v1/people/registrations", f.Body, key)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, "/api/v1/people/registrations", f.Body with { CashSessionId = Guid.NewGuid() }, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, "/api/v1/people/registrations", f.Body with { PlayerArabicName = f.Body.PlayerArabicName + " مختلف" }, key)).StatusCode);
        using var guardian = await Guardian(f.Body.GuardianPhone!);
        Assert.Equal(HttpStatusCode.OK, (await guardian.GetAsync($"/api/v1/guardian/subscriptions/receipts/{receipt.Id}")).StatusCode);
    }

    [Fact]
    public async Task Cash_registration_missing_or_stale_amount_and_shift_roll_back_every_new_record()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var f = await CashRegistrationFixture(owner);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(owner, "/api/v1/people/registrations", f.Body with { CashSessionId = null })).StatusCode);
        foreach (var amount in new decimal?[] { null, 0, -1, .001m, 1000000000 })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(owner, "/api/v1/people/registrations", f.Body with { ExpectedAmount = amount })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, "/api/v1/people/registrations", f.Body with { ExpectedAmount = .01m })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.False(await db.Players.AnyAsync(p => p.ArabicName == f.Body.PlayerArabicName));
        Assert.False(await db.Users.AnyAsync(u => u.PhoneNumber == f.Body.GuardianPhone));
        Assert.False(await db.CashReceiptPostings.AnyAsync(p => p.SessionId == f.Shift));
    }

    [Fact]
    public async Task Cash_registration_closed_disabled_or_revoked_shift_and_cross_branch_are_rejected()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var f = await CashRegistrationFixture(owner);
        (await CashAssign(owner, f.Register, f.Member, false)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(owner, "/api/v1/people/registrations", f.Body)).StatusCode);
        (await CashAssign(owner, f.Register, f.Member)).EnsureSuccessStatusCode();
        var settings = await CashSettings();
        try { await CashSetConfiguration(owner, settings with { CashEnabled = false }); Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, "/api/v1/people/registrations", f.Body)).StatusCode); }
        finally { await CashSetConfiguration(owner, settings); }
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var branch = await db.Branches.Where(b => b.AcademyId == DemoSeed.NogoomAcademyId && b.IsActive && b.Id != f.Body.BranchId).Select(b => b.Id).FirstAsync();
        var r = await Post(owner, CashRoot, new { branchId = branch, name = "وردية تسجيل فرع آخر " + Guid.NewGuid() }); r.EnsureSuccessStatusCode();
        var register = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); (await CashAssign(owner, register, f.Member)).EnsureSuccessStatusCode();
        var otherShift = await CashOpen(owner, register, "0");
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, "/api/v1/people/registrations", f.Body with { CashSessionId = otherShift })).StatusCode);
        (await Post(owner, $"{CashRoot}/sessions/{f.Shift}/close", new { countedAmount = "100" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, "/api/v1/people/registrations", f.Body)).StatusCode);
        Assert.False(await db.Players.AnyAsync(p => p.ArabicName == f.Body.PlayerArabicName));
    }

    [Fact]
    public async Task Cash_registration_roles_csrf_and_tenant_isolation_cannot_be_bypassed_as_online()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); using var admin = await Staff(DemoSeed.AdminEmail); using var guardian = await Guardian(); using var other = await Staff(DemoSeed.FutureOwnerEmail);
        var f = await CashRegistrationFixture(owner);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/people/registrations", f.Body)).StatusCode);
        foreach (var client in new[] { admin, guardian })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "/api/v1/people/registrations", f.Body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "/api/v1/people/registrations", f.Body with { PaymentMethod = "Online", CashSessionId = null })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/people/registration-cash-options?branchId={f.Body.BranchId}&planId={f.Body.PlanId}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(other, "/api/v1/people/registrations", f.Body)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); Assert.False(await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Players.AnyAsync(p => p.ArabicName == f.Body.PlayerArabicName));
    }

    [Fact]
    public async Task Cash_registration_administrator_needs_explicit_financial_grants_own_shift_and_branch_scope()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var f = await CashRegistrationFixture(owner); var email = $"register.cash.{Guid.NewGuid():N}@example.test";
        var created = await Post(owner, "/api/v1/manage/staff", new { name = "أمين تسجيل اصطناعي", email, phone = "010" + Random.Shared.Next(10000000, 99999999), temporaryPassword = "Demo-Only-123!", role = "AcademyAdmin", jobTitle = "تحصيل" }); created.EnsureSuccessStatusCode();
        var member = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        async Task Access(bool financeAdd)
        {
            var actions = StaffAccessRules.Modules.Select(module => new { module, read = module is "players" or "subscriptions" or "finance", add = module is "players" or "subscriptions" || module == "finance" && financeAdd, edit = false, delete = false });
            var csrf = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/manage/staff/{member}/access") { Content = JsonContent.Create(new { allBranches = false, branches = new[] { f.Body.BranchId }, actions }) };
            request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); (await owner.SendAsync(request)).EnsureSuccessStatusCode();
        }
        await Access(false); using var admin = await Staff(email);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, "/api/v1/people/registrations", f.Body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, "/api/v1/people/registrations", f.Body with { PaymentMethod = "Online" })).StatusCode);
        await Access(true); (await CashAssign(owner, f.Register, member)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, "/api/v1/people/registrations", f.Body)).StatusCode); // Another cashier's shift.
        (await Post(owner, $"{CashRoot}/sessions/{f.Shift}/close", new { countedAmount = "100" })).EnsureSuccessStatusCode();
        var shift = await CashOpen(admin, f.Register, "0");
        var accepted = await Post(admin, "/api/v1/people/registrations", f.Body with { CashSessionId = shift }); accepted.EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Single(await db.CashReceiptPostings.Where(p => p.SessionId == shift).ToListAsync());
        var otherBranch = await db.Branches.Where(b => b.AcademyId == DemoSeed.NogoomAcademyId && b.IsActive && b.Id != f.Body.BranchId).Select(b => b.Id).FirstAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/people/registration-cash-options?branchId={otherBranch}&planId={f.Body.PlanId}")).StatusCode);
    }

    [Fact]
    public async Task Cash_registration_demo_seed_is_repeatable_preserves_review_and_creates_no_cash()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await CashRegistrationDemoSeed.Seed(db); var group = await db.TrainingGroups.SingleAsync(g => g.Id == CashRegistrationDemoSeed.GroupId);
        group.ArabicName = "مجموعة نقدي راجعها المستخدم اصطناعيًا"; await db.SaveChangesAsync();
        var counts = (await db.Players.CountAsync(), await db.Collections.CountAsync(), await db.CashReceiptPostings.CountAsync(), await db.CashSessions.CountAsync(), await db.AcademyConfigurationVersions.CountAsync());
        await CashRegistrationDemoSeed.Seed(db); await db.Entry(group).ReloadAsync();
        Assert.Equal("مجموعة نقدي راجعها المستخدم اصطناعيًا", group.ArabicName);
        Assert.Equal(counts, (await db.Players.CountAsync(), await db.Collections.CountAsync(), await db.CashReceiptPostings.CountAsync(), await db.CashSessions.CountAsync(), await db.AcademyConfigurationVersions.CountAsync()));
    }

    [Fact]
    public async Task Cash_registration_serializes_with_close_without_changing_a_closed_balance()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail); var f = await CashRegistrationFixture(owner);
        var results = await Task.WhenAll(Post(owner, "/api/v1/people/registrations", f.Body), Post(owner, $"{CashRoot}/sessions/{f.Shift}/close", new { countedAmount = "100" }));
        results[1].EnsureSuccessStatusCode(); Assert.True(results[0].IsSuccessStatusCode || results[0].StatusCode == HttpStatusCode.Conflict);
        var detail = await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{f.Shift}");
        Assert.Equal(decimal.Parse(detail.GetProperty("balance").GetString()!, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(detail.GetProperty("session").GetProperty("expectedAmount").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        await using var scope = factory.Services.CreateAsyncScope(); Assert.Equal(results[0].IsSuccessStatusCode, await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Players.AnyAsync(p => p.ArabicName == f.Body.PlayerArabicName));
    }
}
