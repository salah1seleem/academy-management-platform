using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;

public sealed partial class SubscriptionPaymentTests
{
    private const string FamilyRequests = FamilyQuotes + "/requests";
    private const string FamilyStaff = "/api/v1/manage/family-renewals";
    private sealed record FamilyFixture(HttpClient Client, Guid User, Guid Guardian, FamilyRenewalChoice[] Choices, Func<Task> Restore) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() { Client.Dispose(); await Restore(); }
    }
    private async Task<FamilyFixture> NewFamily(bool multipleBranches = false)
    {
        var phone = "+2010" + Random.Shared.Next(10000000, 99999999).ToString(CultureInfo.InvariantCulture); var guardian = Guid.NewGuid(); var userId = Guid.NewGuid(); FamilyRenewalChoice[] choices;
        Guid settingsId; (bool Active, string? Insta, string? Wallet, string? Name) original;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var now = DateTimeOffset.UtcNow;
            var setting = await db.AcademyTransferSettings.SingleOrDefaultAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId);
            if (setting is null) { setting = new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, IsActive = false, CreatedAtUtc = now, UpdatedAtUtc = now }; db.AcademyTransferSettings.Add(setting); }
            settingsId = setting.Id; original = (setting.IsActive, setting.InstaPayAddress, setting.WalletNumber, setting.RecipientName); setting.IsActive = true; setting.InstaPayAddress = "family-demo@example.test"; setting.RecipientName = "حساب اصطناعي لا تحول إليه";
            var user = new ApplicationUser { Id = userId, DisplayName = "أسرة تجديد اختبار", UserName = phone, PhoneNumber = phone, PhoneNumberConfirmed = true, CreatedAtUtc = now, UpdatedAtUtc = now }; Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user)).Succeeded);
            db.GuardianProfiles.Add(new() { Id = guardian, AcademyId = DemoSeed.NogoomAcademyId, UserId = userId, DisplayName = user.DisplayName, CreatedAtUtc = now, UpdatedAtUtc = now }); db.AcademyMemberships.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, UserId = userId, Role = AcademyRole.Guardian, CreatedAtUtc = now, UpdatedAtUtc = now });
            var enrollments = new List<SportEnrollment>();
            for (var i = 0; i < 2; i++) { var player = Guid.NewGuid(); db.Players.Add(new() { Id = player, AcademyId = DemoSeed.NogoomAcademyId, PlayerCode = "FR-" + player.ToString("N")[..16], ArabicName = "طفل أسرة اصطناعي " + i, DateOfBirth = new(2016, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now }); db.GuardianPlayerLinks.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, GuardianId = guardian, PlayerId = player, CreatedByUserId = userId, CreatedAtUtc = now, UpdatedAtUtc = now }); var e = new SportEnrollment { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, PlayerId = player, SportId = Slice2DemoSeed.FootballId, BranchId = multipleBranches && i == 1 ? Slice2DemoSeed.FifthBranchId : Slice2DemoSeed.CityBranchId, TrainingGroupId = multipleBranches && i == 1 ? null : Slice2DemoSeed.FootballGroupId, CreatedAtUtc = now, UpdatedAtUtc = now }; enrollments.Add(e); db.SportEnrollments.Add(e); }
            await db.SaveChangesAsync(); choices = enrollments.Select(e => new FamilyRenewalChoice(e.Id, Slice3DemoSeed.FootballMonthlyId)).ToArray();
        }
        var client = factory.CreateClient(); var csrf = await Csrf(client); var challenge = await Post(client, "/api/v1/auth/guardian/otp/request", new { phoneNumber = phone }, csrf); challenge.EnsureSuccessStatusCode(); (await Post(client, "/api/v1/auth/guardian/otp/verify", new { phoneNumber = phone, challengeId = (await challenge.Content.ReadFromJsonAsync<Challenge>())!.ChallengeId, code = "246810" }, csrf)).EnsureSuccessStatusCode();
        return new(client, userId, guardian, choices, async () => { await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var row = await db.AcademyTransferSettings.SingleAsync(x => x.Id == settingsId); row.IsActive = original.Active; row.InstaPayAddress = original.Insta; row.WalletNumber = original.Wallet; row.RecipientName = original.Name; await db.SaveChangesAsync(); });
    }
    private async Task<FamilyRequestWrite> FamilyBody(FamilyFixture f)
    { var response = await Post(f.Client, FamilyQuotes + "/quote", new FamilyRenewalWrite(f.Choices), await Csrf(f.Client)); response.EnsureSuccessStatusCode(); return new(f.Choices, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteHash").GetString()!, Guid.NewGuid().ToString("N")); }
    private async Task<JsonElement> CreateFamily(FamilyFixture f, FamilyRequestWrite? body = null)
    { var response = await Post(f.Client, FamilyRequests, body ?? await FamilyBody(f), await Csrf(f.Client)); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    private async Task<JsonElement> FamilyProof(FamilyFixture f, JsonElement request)
    {
        using var form = new MultipartFormDataContent(); var file = new ByteArrayContent([137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0]); file.Headers.ContentType = new("image/png"); form.Add(file, "file", "synthetic-family-proof.png"); form.Add(new StringContent("Proof"), "purpose"); using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/v1/assets/upload") { Content = form }; upload.Headers.Add("X-CSRF-TOKEN", await Csrf(f.Client)); var saved = await f.Client.SendAsync(upload); saved.EnsureSuccessStatusCode(); var asset = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); var proof = await Post(f.Client, $"{FamilyRequests}/{request.GetProperty("id").GetGuid()}/proof", new FamilyProofWrite(asset, request.GetProperty("revision").GetInt32()), await Csrf(f.Client)); proof.EnsureSuccessStatusCode(); return await proof.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static FamilyReviewWrite FamilyApproval(JsonElement request, bool accept = true) => new(accept, request.GetProperty("revision").GetInt32(), null, request.GetProperty("items").EnumerateArray().Select(l => new FamilyPlacement(l.GetProperty("id").GetGuid(), Slice2DemoSeed.FootballGroupId)).ToArray());
    [Fact]
    public async Task Family_request_has_one_proof_and_approves_all_lines_once_with_frozen_terms_and_currency()
    {
        await using var f = await NewFamily(); var body = await FamilyBody(f); var pending = await CreateFamily(f, body); var id = pending.GetProperty("id").GetGuid();
        Assert.Equal("Pending", pending.GetProperty("status").GetString()); Assert.Equal(pending.GetProperty("id").GetGuid(), (await CreateFamily(f, body with { Lines = body.Lines!.Reverse().ToArray() })).GetProperty("id").GetGuid());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var ids = f.Choices.Select(c => c.SportEnrollmentId).ToArray(); Assert.False(await db.RenewalRequests.AnyAsync(r => ids.Contains(r.SportEnrollmentId))); Assert.False(await db.SubscriptionPeriods.AnyAsync(p => ids.Contains(p.SportEnrollmentId)));
        pending = await FamilyProof(f, pending); using var owner = await InboxStaff(DemoSeed.OwnerEmail); var plan = await db.SubscriptionPlans.SingleAsync(p => p.Id == Slice3DemoSeed.FootballMonthlyId); var old = (plan.Price, plan.Currency, plan.DurationDays, plan.ArabicName, plan.IsActive);
        try
        {
            plan.Price += 99m; plan.Currency = "USD"; plan.DurationDays = 60; plan.ArabicName = "خطة معدلة لا تستبدل الاتفاق"; plan.IsActive = false; await db.SaveChangesAsync();
            var approval = FamilyApproval(pending); var result = await Post(owner, $"{FamilyStaff}/{id}/review", approval, await Csrf(owner)); result.EnsureSuccessStatusCode(); var approved = await result.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("Approved", approved.GetProperty("status").GetString()); Assert.All(approved.GetProperty("items").EnumerateArray(), l => Assert.NotEqual(JsonValueKind.Null, l.GetProperty("receiptId").ValueKind));
            (await Post(owner, $"{FamilyStaff}/{id}/review", approval, await Csrf(owner))).EnsureSuccessStatusCode(); Assert.Equal(2, await db.Receipts.CountAsync(r => ids.Contains(r.SportEnrollmentId))); Assert.Equal(2, await db.SubscriptionPeriods.CountAsync(p => ids.Contains(p.SportEnrollmentId))); Assert.All(await db.Receipts.Where(r => ids.Contains(r.SportEnrollmentId)).ToListAsync(), r => { Assert.Equal("EGP", r.Currency); Assert.Equal(old.ArabicName, r.PlanNameSnapshot); });
            Assert.Equal(Decimal(approved, "totalAmount"), await db.Collections.Where(c => ids.Contains(c.SportEnrollmentId)).SumAsync(c => c.Amount)); Assert.Equal(1, await db.AcademyNotices.CountAsync(n => n.UserId == f.User));
        }
        finally { (plan.Price, plan.Currency, plan.DurationDays, plan.ArabicName, plan.IsActive) = old; await db.SaveChangesAsync(); }
    }
    [Fact]
    public async Task Family_request_bad_second_group_rolls_back_every_receipt_period_and_decision()
    {
        await using var f = await NewFamily(); var pending = await FamilyProof(f, await CreateFamily(f)); var id = pending.GetProperty("id").GetGuid(); var approval = FamilyApproval(pending); var badLine = pending.GetProperty("items").EnumerateArray().OrderBy(l => l.GetProperty("sportEnrollmentId").GetGuid()).Last().GetProperty("id").GetGuid(); var placements = approval.Placements!.Select(p => p.LineId == badLine ? p with { GroupId = Guid.NewGuid() } : p).ToArray(); using var owner = await InboxStaff(DemoSeed.OwnerEmail);
        var fail = await Post(owner, $"{FamilyStaff}/{id}/review", approval with { Placements = placements }, await Csrf(owner)); Assert.Equal(HttpStatusCode.Conflict, fail.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var ids = f.Choices.Select(c => c.SportEnrollmentId).ToArray(); Assert.False(await db.Collections.AnyAsync(c => ids.Contains(c.SportEnrollmentId))); Assert.False(await db.Receipts.AnyAsync(c => ids.Contains(c.SportEnrollmentId))); Assert.False(await db.SubscriptionPeriods.AnyAsync(c => ids.Contains(c.SportEnrollmentId))); Assert.Equal(FamilyRenewalStatus.Pending, (await db.FamilyRenewalRequests.SingleAsync(r => r.Id == id)).Status);
        (await Post(owner, $"{FamilyStaff}/{id}/review", approval, await Csrf(owner))).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Family_request_reject_cancel_stale_quote_and_idempotency_conflicts_never_collect()
    {
        await using var f = await NewFamily(); var body = await FamilyBody(f); Assert.Equal(HttpStatusCode.Conflict, (await Post(f.Client, FamilyRequests, body with { QuoteHash = new string('A', 64) }, await Csrf(f.Client))).StatusCode); var pending = await CreateFamily(f, body); var id = pending.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(f.Client, FamilyRequests, body with { Lines = [body.Lines![0]] }, await Csrf(f.Client))).StatusCode);
        using var owner = await InboxStaff(DemoSeed.OwnerEmail); (await Post(owner, $"{FamilyStaff}/{id}/review", FamilyApproval(pending, false) with { Reason = "إثبات غير صحيح" }, await Csrf(owner))).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, $"{FamilyStaff}/{id}/review", FamilyApproval(pending), await Csrf(owner))).StatusCode);
        var next = await CreateFamily(f); var nextId = next.GetProperty("id").GetGuid(); (await Post(f.Client, $"{FamilyRequests}/{nextId}/cancel", new FamilyCancelWrite(next.GetProperty("revision").GetInt32()), await Csrf(f.Client))).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.False(await db.Collections.AnyAsync(c => f.Choices.Select(l => l.SportEnrollmentId).Contains(c.SportEnrollmentId)));
    }
    [Fact]
    public async Task Family_request_blocks_parallel_individual_renewals_and_refuses_untrusted_roles_and_children()
    {
        await using var f = await NewFamily(); var pending = await CreateFamily(f); var id = pending.GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope(); var creation = scope.ServiceProvider.GetRequiredService<RenewalCreationService>(); var individual = await creation.CreateAsync(DemoSeed.NogoomAcademyId, f.User, f.Choices[0].SportEnrollmentId, f.Choices[0].SubscriptionPlanId, Guid.NewGuid().ToString("N"), true); Assert.Equal(RenewalCreationState.OpenRenewalExists, individual.State);
        using var unrelated = await GuardianClient(); Assert.Equal(HttpStatusCode.NotFound, (await unrelated.GetAsync($"{FamilyRequests}/{id}")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await unrelated.GetAsync($"{FamilyStaff}/{id}")).StatusCode);
        using var coach = await InboxStaff(DemoSeed.CoachEmail); Assert.Equal(HttpStatusCode.Forbidden, (await coach.GetAsync($"{FamilyStaff}/{id}")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await coach.GetAsync($"{FamilyRequests}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Client.PostAsJsonAsync(FamilyRequests, await FamilyBody(f))).StatusCode);
    }
    [Fact]
    public async Task Family_request_concurrent_create_and_approval_have_one_header_and_exactly_one_receipt_per_child()
    {
        await using var f = await NewFamily(); var body = await FamilyBody(f); var csrf = await Csrf(f.Client);
        var creates = await Task.WhenAll(Post(f.Client, FamilyRequests, body, csrf), Post(f.Client, FamilyRequests, body, csrf));
        Assert.All(creates, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict })); Assert.Contains(creates, r => r.IsSuccessStatusCode);
        var pending = await FamilyProof(f, await CreateFamily(f, body)); var id = pending.GetProperty("id").GetGuid(); using var owner = await InboxStaff(DemoSeed.OwnerEmail); var token = await Csrf(owner); var approval = FamilyApproval(pending);
        var accepts = await Task.WhenAll(Post(owner, $"{FamilyStaff}/{id}/review", approval, token), Post(owner, $"{FamilyStaff}/{id}/review", approval, token)); Assert.All(accepts, r => r.EnsureSuccessStatusCode());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1, await db.FamilyRenewalRequests.CountAsync(r => r.RequestedByUserId == f.User)); var ids = f.Choices.Select(l => l.SportEnrollmentId).ToArray(); Assert.Equal(2, await db.Collections.CountAsync(c => ids.Contains(c.SportEnrollmentId))); Assert.Equal(2, await db.Receipts.CountAsync(c => ids.Contains(c.SportEnrollmentId))); Assert.Equal(2, await db.SubscriptionPeriods.CountAsync(c => ids.Contains(c.SportEnrollmentId)));
        var queue = await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/payment-requests?type=Renewal&allDates=true"); var results = await db.FamilyRenewalLines.Where(l => l.RequestId == id).Select(l => l.ResultPaymentId!.Value).ToArrayAsync(); Assert.DoesNotContain(queue.GetProperty("items").EnumerateArray(), x => results.Contains(x.GetProperty("id").GetGuid())); Assert.Equal("Approved", (await owner.GetFromJsonAsync<JsonElement>($"{FamilyStaff}/{id}")).GetProperty("status").GetString());
    }
    [Fact]
    public async Task Family_request_missing_proof_revoked_links_and_foreign_tenant_never_activate()
    {
        await using var f = await NewFamily(); var request = await CreateFamily(f); var id = request.GetProperty("id").GetGuid(); using var owner = await InboxStaff(DemoSeed.OwnerEmail);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, $"{FamilyStaff}/{id}/review", FamilyApproval(request), await Csrf(owner))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(f.Client, $"{FamilyRequests}/{id}/proof", new FamilyProofWrite(Guid.NewGuid(), request.GetProperty("revision").GetInt32()), await Csrf(f.Client))).StatusCode);
        request = await FamilyProof(f, request); using var other = await InboxStaff(DemoSeed.FutureOwnerEmail); Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{FamilyStaff}/{id}")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(request.GetProperty("proofUrl").GetString())).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var link = await db.GuardianPlayerLinks.FirstAsync(l => l.GuardianId == f.Guardian); link.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await f.Client.GetAsync($"{FamilyRequests}/{id}")).StatusCode); Assert.Empty((await f.Client.GetFromJsonAsync<JsonElement>(FamilyRequests)).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, $"{FamilyStaff}/{id}/review", FamilyApproval(request), await Csrf(owner))).StatusCode); Assert.False(await db.Collections.AnyAsync(c => f.Choices.Select(x => x.SportEnrollmentId).Contains(c.SportEnrollmentId)));
        (await Post(owner, $"{FamilyStaff}/{id}/review", FamilyApproval(request, false), await Csrf(owner))).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Family_request_database_guards_freeze_sources_and_reject_partial_approval()
    {
        await using var f = await NewFamily(); var request = await CreateFamily(f); var id = request.GetProperty("id").GetGuid(); var lineId = request.GetProperty("items")[0].GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"FamilyRenewalRequests\" SET \"TotalAmount\" = \"TotalAmount\" + 1 WHERE \"Id\" = {id}"))).SqlState);
        Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"FamilyRenewalLines\" SET \"Currency\" = 'USD' WHERE \"Id\" = {lineId}"))).SqlState);
        Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"FamilyRenewalLines\" WHERE \"Id\" = {lineId}"))).SqlState);
        request = await FamilyProof(f, request);
        Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"FamilyRenewalRequests\" SET \"Status\" = 'Approved', \"Revision\" = \"Revision\" + 1, \"DecidedByUserId\" = {f.User}, \"DecidedAtUtc\" = now() WHERE \"Id\" = {id}"))).SqlState);
        Assert.Equal(FamilyRenewalStatus.Pending, (await db.FamilyRenewalRequests.AsNoTracking().SingleAsync(r => r.Id == id)).Status);
    }
    [Fact]
    public async Task Family_request_and_proof_require_every_branch_not_just_one_visible_child()
    {
        await using var f = await NewFamily(true); var request = await FamilyProof(f, await CreateFamily(f)); var id = request.GetProperty("id").GetGuid(); using var owner = await InboxStaff(DemoSeed.OwnerEmail);
        var email = $"family.branch.{Guid.NewGuid():N}@example.test"; var created = await Post(owner, "/api/v1/manage/staff", new StaffWrite("إداري أسرة اصطناعي", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, "اختبار"), await Csrf(owner)); created.EnsureSuccessStatusCode(); var member = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var actions = StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m is "subscriptions" or "payments" or "media", false, m is "subscriptions" or "payments", false)).ToArray();
        async Task Grant(Guid[] branches) { using var message = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/manage/staff/{member}/access") { Content = JsonContent.Create(new StaffAccessWrite(false, branches, actions)) }; message.Headers.Add("X-CSRF-TOKEN", await Csrf(owner)); (await owner.SendAsync(message)).EnsureSuccessStatusCode(); }
        await Grant([Slice2DemoSeed.CityBranchId]); using var admin = await InboxStaff(email);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{FamilyStaff}/{id}")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(request.GetProperty("proofUrl").GetString())).StatusCode);
        var queue = await admin.GetFromJsonAsync<JsonElement>("/api/v1/manage/payment-requests?allDates=true"); Assert.DoesNotContain(queue.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        await Grant([Slice2DemoSeed.CityBranchId, Slice2DemoSeed.FifthBranchId]); Assert.True((await admin.GetFromJsonAsync<JsonElement>($"{FamilyStaff}/{id}")).GetProperty("canReview").GetBoolean()); Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(request.GetProperty("proofUrl").GetString())).StatusCode);
        actions = actions.Select(a => a with { Edit = false }).ToArray(); await Grant([Slice2DemoSeed.CityBranchId, Slice2DemoSeed.FifthBranchId]); Assert.False((await admin.GetFromJsonAsync<JsonElement>($"{FamilyStaff}/{id}")).GetProperty("canReview").GetBoolean()); Assert.Equal(HttpStatusCode.Forbidden, (await Post(admin, $"{FamilyStaff}/{id}/review", new FamilyReviewWrite(false, request.GetProperty("revision").GetInt32(), null, null), await Csrf(admin))).StatusCode);
    }
    [Fact]
    public async Task Family_quote_currency_change_requires_new_consent_even_if_all_amounts_are_unchanged()
    {
        await using var f = await NewFamily(); var body = await FamilyBody(f); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var plan = await db.SubscriptionPlans.SingleAsync(x => x.Id == Slice3DemoSeed.FootballMonthlyId); var currency = plan.Currency;
        try { plan.Currency = "USD"; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Conflict, (await Post(f.Client, FamilyRequests, body, await Csrf(f.Client))).StatusCode); Assert.False(await db.FamilyRenewalRequests.AnyAsync(r => r.RequestedByUserId == f.User)); }
        finally { plan.Currency = currency; await db.SaveChangesAsync(); }
    }
    [Fact]
    public async Task Family_demo_seed_is_repeatable_and_never_posts_money_or_changes_request_history()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var before = (await db.Collections.CountAsync(), await db.Receipts.CountAsync(), await db.FamilyRenewalRequests.CountAsync(), await db.SubscriptionPeriods.CountAsync()); await FamilyRenewalDemoSeed.Seed(db, users); await FamilyRenewalDemoSeed.Seed(db, users);
        Assert.Equal(before, (await db.Collections.CountAsync(), await db.Receipts.CountAsync(), await db.FamilyRenewalRequests.CountAsync(), await db.SubscriptionPeriods.CountAsync())); Assert.Equal(2, await db.GuardianPlayerLinks.CountAsync(l => l.GuardianId == FamilyRenewalDemoSeed.GuardianId)); Assert.Equal(2, await db.SportEnrollments.CountAsync(e => db.GuardianPlayerLinks.Any(l => l.GuardianId == FamilyRenewalDemoSeed.GuardianId && l.PlayerId == e.PlayerId)));
    }
    [Fact]
    public async Task Family_transfer_does_not_turn_a_full_fee_waiver_into_zero_cash_collection()
    {
        await using var f = await NewFamily(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var rule = new DiscountRule { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "إعفاء اختبار اصطناعي", Percentage = 100, Reason = "إعفاء معتمد للاختبار", Revision = 1 }; db.DiscountRules.Add(rule);
        foreach (var player in await db.SportEnrollments.Where(e => f.Choices.Select(c => c.SportEnrollmentId).Contains(e.Id)).Select(e => e.PlayerId).ToListAsync()) db.PlayerDiscountLinks.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, PlayerId = player, DiscountRuleId = rule.Id });
        await db.SaveChangesAsync(); var body = await FamilyBody(f); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(f.Client, FamilyRequests, body, await Csrf(f.Client))).StatusCode); Assert.False(await db.FamilyRenewalRequests.AnyAsync(r => r.RequestedByUserId == f.User)); Assert.False(await db.RenewalRequests.AnyAsync(r => r.RequestedByUserId == f.User));
    }
}
