using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Content;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class OwnerReviewOperationsTests : IAsyncLifetime
{
    private Factory factory = null!;
    public async Task InitializeAsync() { factory = new Factory(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Test database required")); _ = factory.CreateClient(); await CleanupOwnedFixtures(); }
    public async Task DisposeAsync() { await CleanupOwnedFixtures(); await factory.DisposeAsync(); }
    private async Task CleanupOwnedFixtures()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        // Preserve published source identities, but retire this class's owned fixtures
        // and clean its unposted legacy financial records so later seed totals stay stable.
        var allPlayers=await db.Players.Where(x=>x.AcademyId==DemoSeed.NogoomAcademyId&&x.ArabicName.StartsWith("لاعب اختبار التسجيل ")).Select(x=>x.Id).ToArrayAsync();
        var retainedEnrollments=await db.PrivateAssetAudiences.Where(a=>allPlayers.Contains(a.PlayerId)&&a.SportEnrollmentId!=null).Select(a=>a.SportEnrollmentId!.Value).Distinct().ToArrayAsync();
        var retainedPlayers=await db.SportEnrollments.Where(e=>retainedEnrollments.Contains(e.Id)).Select(e=>e.PlayerId).ToArrayAsync();
        var players=allPlayers.Except(retainedPlayers).ToArray();
        var enrollments = await db.SportEnrollments.Where(x => allPlayers.Contains(x.PlayerId)).Select(x => x.Id).ToArrayAsync();
        await db.SportEnrollments.Where(e=>retainedEnrollments.Contains(e.Id)).ExecuteUpdateAsync(s=>s.SetProperty(e=>e.IsActive,false));
        await db.Players.Where(p=>retainedPlayers.Contains(p.Id)).ExecuteUpdateAsync(s=>s.SetProperty(p=>p.IsActive,false));
        var payments = await db.PaymentRequests.Where(x => enrollments.Contains(x.RenewalRequest.SportEnrollmentId)).Select(x => x.Id).ToArrayAsync();
        var collections = await db.Collections.Where(x => enrollments.Contains(x.SportEnrollmentId)).Select(x => x.Id).ToArrayAsync();
        var products = await db.SportCatalogItems.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.ArabicName.StartsWith("منتج اختبار عمليات ")).Select(x => x.Id).ToArrayAsync();
        await db.ProductSales.Where(x => products.Contains(x.ProductId)).ExecuteDeleteAsync();
        await db.NewEnrollmentRequests.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.NewChildArabicName != null && x.NewChildArabicName.StartsWith("لاعب اختبار التسجيل ")).ExecuteDeleteAsync();
        await db.SportCatalogItems.Where(x => products.Contains(x.Id)).ExecuteDeleteAsync();
        var assets = await db.PrivateAssets.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.MediaPolicyVersion==0&&(players.Contains(x.PlayerId ?? Guid.Empty) || x.Caption!.StartsWith("اختبار عمليات "))).Select(x => x.Id).ToArrayAsync();
        await db.AcademyNews.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Title.StartsWith("خبر اختبار عمليات ")).ExecuteDeleteAsync();
        await db.PrivateAssetAudiences.Where(x => assets.Contains(x.AssetId) || players.Contains(x.PlayerId)).ExecuteDeleteAsync();
        await db.Players.Where(x => players.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.PhotoReference, (string?)null));
        await db.PrivateAssets.Where(x => assets.Contains(x.Id)).ExecuteDeleteAsync();
        await db.PlayerAttendances.Where(x => enrollments.Contains(x.SportEnrollmentId)).ExecuteDeleteAsync();
        var members = await db.AcademyMemberships.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.User.Email!.StartsWith("owner-review-test-")).Select(x => x.Id).ToArrayAsync();
        var retained = await db.StaffAttendanceChanges.Where(x => members.Contains(x.MembershipId)).Select(x => x.MembershipId).Distinct().ToArrayAsync();
        await db.AcademyMemberships.Where(x => retained.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        members = members.Except(retained).ToArray(); // permanent source/evidence and its identities stay linked
        var userIds = await db.AcademyMemberships.Where(x => members.Contains(x.Id)).Select(x => x.UserId).ToArrayAsync();
        await db.StaffDailyAttendances.Where(x => members.Contains(x.MembershipId)).ExecuteDeleteAsync();
        await db.StaffWorkSchedules.Where(x => members.Contains(x.MembershipId)).ExecuteDeleteAsync();
        await db.StaffGrants.Where(x => members.Contains(x.MembershipId)).ExecuteDeleteAsync();
        await db.StaffActionGrants.Where(x => members.Contains(x.MembershipId)).ExecuteDeleteAsync();
        await db.MembershipBranchAccesses.Where(x => members.Contains(x.MembershipId)).ExecuteDeleteAsync();
        await db.StaffAccessAudits.Where(x => members.Contains(x.MembershipId)).ExecuteDeleteAsync();
        await db.UserSessions.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync();
        await db.AcademyMemberships.Where(x => members.Contains(x.Id)).ExecuteDeleteAsync();
        await db.Users.Where(x => userIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.CollectionRefunds.Where(x => collections.Contains(x.CollectionId)).ExecuteDeleteAsync();
        await db.SubscriptionPeriods.Where(x => enrollments.Contains(x.SportEnrollmentId)).ExecuteDeleteAsync();
        await db.Receipts.Where(x => collections.Contains(x.CollectionId)).ExecuteDeleteAsync();
        await db.Collections.Where(x => collections.Contains(x.Id)).ExecuteDeleteAsync();
        await db.PaymentProviderEvents.Where(x => payments.Contains(x.PaymentRequestId)).ExecuteDeleteAsync();
        await db.RenewalRequests.Where(x => enrollments.Contains(x.SportEnrollmentId)).ExecuteUpdateAsync(s => s.SetProperty(x => x.PaymentRequestId, (Guid?)null));
        await db.PaymentRequests.Where(x => payments.Contains(x.Id)).ExecuteDeleteAsync();
        await db.RenewalRequests.Where(x => enrollments.Contains(x.SportEnrollmentId)).ExecuteDeleteAsync();
        await db.PlayerDiscountLinks.Where(x => players.Contains(x.PlayerId)).ExecuteDeleteAsync();
        await db.SportEnrollments.Where(x => enrollments.Contains(x.Id)&&!retainedEnrollments.Contains(x.Id)).ExecuteDeleteAsync();
        await db.GuardianPlayerLinks.Where(x => players.Contains(x.PlayerId)).ExecuteDeleteAsync();
        await db.Players.Where(x => players.Contains(x.Id)).ExecuteDeleteAsync();
        // Immutable timetable revisions retain their original group/session history across reruns.
        var historicalGroups = db.TrainingScheduleRevisions.Select(x => x.TrainingGroupId).Union(db.PrivateAssets.Where(a=>a.MediaPolicyVersion==1&&a.TrainingGroupId!=null).Select(a=>a.TrainingGroupId!.Value));
        await db.TrainingGroups.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.ArabicName.StartsWith("جدول اختبار مستقل ") && historicalGroups.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        var groups = await db.TrainingGroups.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && (x.ArabicName.StartsWith("جدول اختبار مستقل ") || x.ArabicName.StartsWith("مجموعة نقل اختبار ")) && !historicalGroups.Contains(x.Id)).Select(x => x.Id).ToArrayAsync();
        await db.TrainingSessions.Where(x => groups.Contains(x.TrainingGroupId)).ExecuteDeleteAsync();
        await db.RecurringSchedules.Where(x => groups.Contains(x.TrainingGroupId)).ExecuteDeleteAsync();
        await db.StaffGroupAssignments.Where(x => groups.Contains(x.TrainingGroupId)).ExecuteDeleteAsync();
        await db.TrainingGroups.Where(x => groups.Contains(x.Id)).ExecuteDeleteAsync();
        // Posted rule versions are permanent, including synthetic test history. Archive only our rules.
        foreach (var rule in await db.DiscountRules.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.IsActive && x.ArabicName.StartsWith("مدرسة اختبار ")).ToListAsync())
        {
            rule.IsActive = false; rule.Revision++;
            db.DiscountRuleVersions.Add(DiscountRuleVersion.Create(rule, null, DateTimeOffset.UtcNow));
        }
        await db.SaveChangesAsync();
    }
    private async Task<HttpClient> Owner() { var c = factory.CreateClient(); var r = await Send(c, "/api/v1/auth/login", new { email = DemoSeed.OwnerEmail, password = "Demo-Only-123!" }); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return c; }
    private static async Task<HttpResponseMessage> Send(HttpClient c, string path, object body, string? key = null, bool put = false) { var csrf = await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); var request = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(put ? await DailyAttendanceTestCommand.WithCurrentVersion(c, path, body) : body) }; request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); if (key is not null) request.Headers.Add("Idempotency-Key", key); return await c.SendAsync(request); }
    private async Task<(Guid Player, Guid Enrollment, Guid Collection)> Register(HttpClient c, Guid? groupId = null)
    {
        var ownGroup = groupId ?? await FreshRegistrationGroup();
        var key = Guid.NewGuid().ToString(); var options = await c.GetFromJsonAsync<JsonElement>("/api/v1/manage/structure/options"); var group = options.GetProperty("groups").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == ownGroup);
        // This legacy fixture exercises people/subscription workflows, not a cash
        // shift. Cash-specific registration tests retain immutable posted sources.
        var body = new { playerArabicName = "لاعب اختبار التسجيل " + key[..8], dateOfBirth = "2017-05-01", existingGuardianId = await GuardianId(), branchId = group.GetProperty("branchId").GetGuid(), sportId = Slice2DemoSeed.FootballId, groupId = group.GetProperty("id").GetGuid(), planId = Slice3DemoSeed.FootballMonthlyId, paymentMethod = "Online" };
        var response = await Send(c, "/api/v1/people/registrations", body, key); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var player = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid();
        var replay = await Send(c, "/api/v1/people/registrations", body, key); Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(player, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(c, "/api/v1/people/registrations", body with { playerArabicName = body.playerArabicName + " مختلف" }, key)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var enrollment = await db.SportEnrollments.SingleAsync(x => x.PlayerId == player);
        var collection = await db.Collections.SingleAsync(x => x.SportEnrollmentId == enrollment.Id); Assert.Equal("Online", collection.PaymentMethod); Assert.Equal(1, await db.Receipts.CountAsync(x => x.CollectionId == collection.Id)); Assert.Equal(1, await db.SubscriptionPeriods.CountAsync(x => x.CollectionId == collection.Id)); return (player, enrollment.Id, collection.Id);
    }
    private async Task<Guid> FreshRegistrationGroup()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var source = await db.TrainingGroups.SingleAsync(g => g.Id == Slice2DemoSeed.FootballGroupId);
        var id = Guid.NewGuid(); var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
        db.TrainingGroups.Add(new() { Id = id, AcademyId = source.AcademyId, ArabicName = "مجموعة نقل اختبار " + id, BranchId = source.BranchId, SportId = source.SportId, AgeCategoryId = source.AgeCategoryId, Capacity = 5 });
        foreach (var member in await db.StaffGroupAssignments.Where(a => a.TrainingGroupId == source.Id && a.IsActive).Select(a => a.AcademyMembershipId).ToArrayAsync())
            db.StaffGroupAssignments.Add(new() { Id = Guid.NewGuid(), AcademyId = source.AcademyId, TrainingGroupId = id, AcademyMembershipId = member });
        db.TrainingSessions.Add(new() { Id = Guid.NewGuid(), AcademyId = source.AcademyId, TrainingGroupId = id, BranchId = source.BranchId, SportId = source.SportId, AgeCategoryId = source.AgeCategoryId, SessionDate = new(2026, 9, 28), StartTime = new(1, 0), EndTime = new(1, 30), CreatedByUserId = actor });
        await db.SaveChangesAsync(); return id;
    }
    private async Task<Guid> GuardianId() { await using var scope = factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().GuardianProfiles.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.User.PhoneNumber == DemoSeed.GuardianPhone).Select(x => x.Id).SingleAsync(); }
    private async Task<HttpClient> Guardian()
    {
        var c = factory.CreateClient(); var r = await Send(c, "/api/v1/auth/guardian/otp/request", new { phoneNumber = DemoSeed.GuardianPhone }); r.EnsureSuccessStatusCode();
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetGuid();
        (await Send(c, "/api/v1/auth/guardian/otp/verify", new { challengeId = id, phoneNumber = DemoSeed.GuardianPhone, code = "246810" })).EnsureSuccessStatusCode(); return c;
    }
    private async Task<Guid> Upload(HttpClient c, string purpose, Guid? player = null, Guid? group = null, bool valid = true)
    {
        using var form = new MultipartFormDataContent(); var file = new ByteArrayContent(valid ? Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=") : new byte[12]); file.Headers.ContentType = new("image/png"); form.Add(file, "file", "demo.png"); form.Add(new StringContent(purpose), "purpose"); form.Add(new StringContent("اختبار عمليات " + Guid.NewGuid()), "caption");
        if (player.HasValue) form.Add(new StringContent(player.Value.ToString()), "playerId"); if (group.HasValue) form.Add(new StringContent(group.Value.ToString()), "groupId");
        if(purpose=="Media") { var audience=await c.GetFromJsonAsync<JsonElement>($"/api/v1/assets/media-audience?groupId={group}");form.Add(new StringContent(audience.GetProperty("eventOn").GetString()!),"eventOn");form.Add(new StringContent("true"),"privacyConfirmed");form.Add(new StringContent("true"),"noRecognizableChildren"); } // Valid synthetic pixel contains no identifiable child.
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/assets/upload") { Content = form }; request.Headers.Add("X-CSRF-TOKEN", (await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString());
        var r = await c.SendAsync(request); if (!valid) { Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); return Guid.Empty; }
        Assert.Equal(HttpStatusCode.Created, r.StatusCode); return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private async Task<Guid> Product()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var p = new SportCatalogItem { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, SportId = Slice2DemoSeed.FootballId, ArabicName = "منتج اختبار عمليات " + Guid.NewGuid(), ImageReference = "demo://catalog/football-kit", DisplayPrice = 100m, Currency = "EGP", DiscountPercentage = 10m, StockQuantity = 3 }; db.SportCatalogItems.Add(p); await db.SaveChangesAsync(); return p.Id;
    }
    [Fact] public async Task Product_sale_uses_server_price_decrements_stock_once_and_rejects_changed_replay()
    {
        using var c = await Owner(); var p = await Product(); var key = Guid.NewGuid().ToString(); var body = new { productId = p, quantity = 2, branchId = Slice2DemoSeed.CityBranchId, paymentMethod = "Online", expectedAmount = "180.00", amount = 1 };
        var r = await Send(c, "/api/v1/manage/product-sales", body, key); Assert.Equal(HttpStatusCode.OK, r.StatusCode); var saleResult = await r.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("180.00", saleResult.GetProperty("amount").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Send(c, "/api/v1/manage/product-sales", body, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(c, "/api/v1/manage/product-sales", new { productId = p, quantity = 2, branchId = Slice2DemoSeed.CityBranchId, paymentMethod = "Cash", expectedAmount = "180.00" }, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(c, "/api/v1/manage/product-sales", body, Guid.NewGuid().ToString())).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Equal(1, await db.ProductSales.CountAsync(x => x.ProductId == p)); Assert.Equal(1, (await db.SportCatalogItems.SingleAsync(x => x.Id == p)).StockQuantity);
        var saleId = saleResult.GetProperty("id").GetGuid(); var report = await c.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial?from=2026-01-01&to=2026-12-31&collectionType=Product&branchId={Slice2DemoSeed.CityBranchId}"); Assert.Contains(report.GetProperty("items").EnumerateArray(), x => x.GetProperty("collectionId").GetGuid() == saleId);
    }
    [Fact] public async Task Guardian_product_proof_requires_manual_review_before_stock_and_revenue_change()
    {
        using var owner = await Owner(); using var guardian = await Guardian(); var p = await Product();
        var result = await Send(guardian, "/api/v1/guardian/product-sales", new { productId = p, quantity = 1 }, Guid.NewGuid().ToString()); Assert.Equal(HttpStatusCode.OK, result.StatusCode); var id = (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"/api/v1/manage/product-sales/{id}/review", new { accept = true })).StatusCode);
        var proof = await Upload(guardian, "Proof"); (await Send(guardian, $"/api/v1/guardian/product-sales/{id}/proof", new { assetId = proof })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, $"/api/v1/manage/product-sales/{id}/review", new { accept = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, $"/api/v1/manage/product-sales/{id}/review", new { accept = true })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); Assert.Equal(2, (await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().SportCatalogItems.SingleAsync(x => x.Id == p)).StockQuantity);
    }
    [Fact] public async Task Photo_and_group_media_are_protected_and_new_joiners_do_not_inherit_old_media()
    {
        using var c = await Owner(); using var guardian = await Guardian(); using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services);
        var record = await Register(c); var photo = await Upload(c, "Photo", record.Player); Assert.Equal(HttpStatusCode.OK, (await guardian.GetAsync($"/api/v1/assets/{photo}")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await coach.GetAsync($"/api/v1/assets/{photo}")).StatusCode); await Upload(c, "Photo", record.Player, valid: false);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var group = await db.SportEnrollments.Where(x => x.Id == record.Enrollment).Select(x => x.TrainingGroupId).SingleAsync();
        var media = await Upload(c, "Media", group: group); Assert.Equal(HttpStatusCode.OK, (await guardian.GetAsync($"/api/v1/assets/{media}")).StatusCode); Assert.True(await db.PrivateAssetAudiences.AnyAsync(x => x.AssetId == media && x.PlayerId == record.Player));
        var later = await Register(c, group); Assert.False(await db.PrivateAssetAudiences.AnyAsync(x => x.AssetId == media && x.PlayerId == later.Player));
        var future = factory.CreateClient(); (await Send(future, "/api/v1/auth/login", new { email = DemoSeed.FutureOwnerEmail, password = "Demo-Only-123!" })).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.NotFound, (await future.GetAsync($"/api/v1/assets/{media}")).StatusCode);
    }
    [Fact] public async Task Draft_and_expired_news_hide_both_text_and_private_image()
    {
        using var c = await Owner(); using var guardian = await Guardian(); var image = await Upload(c, "News"); var title = "خبر اختبار عمليات " + Guid.NewGuid(); var body = new { title, body = "خبر تجريبي لا يحوي بيانات أطفال", imageAssetId = image, visibleDays = 7, publish = false, isActive = true };
        var r = await Send(c, "/api/v1/content/news/", body); var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync($"/api/v1/assets/{image}")).StatusCode); Assert.DoesNotContain(title, await guardian.GetStringAsync("/api/v1/news"));
        (await Send(c, $"/api/v1/content/news/{id}", new { title, body = "خبر منشور", imageAssetId = image, visibleDays = 7, publish = true, isActive = true }, put: true)).EnsureSuccessStatusCode(); Assert.Contains(title, await guardian.GetStringAsync("/api/v1/news")); Assert.Equal(HttpStatusCode.OK, (await guardian.GetAsync($"/api/v1/assets/{image}")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); await db.AcademyNews.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PublishedAtUtc, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero))); Assert.DoesNotContain(title, await guardian.GetStringAsync("/api/v1/news")); Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync($"/api/v1/assets/{image}")).StatusCode);
    }
    [Fact] public async Task Name_search_is_tenant_scoped_minimal_and_reference_does_not_grant_child_access()
    {
        using var c = await Owner(); using var guardian = await Guardian(); var record = await Register(c);
        var r = await Send(guardian, "/api/v1/guardian/subscriptions/external/search", new { name = "لاعب اختبار التسجيل" }); Assert.Equal(HttpStatusCode.OK, r.StatusCode); var results = await r.Content.ReadFromJsonAsync<JsonElement>(); Assert.InRange(results.GetArrayLength(), 1, 10); var hit = results[0]; Assert.Equal(4, hit.EnumerateObject().Count()); var token = hit.GetProperty("reference").GetString()!; Assert.StartsWith("SR1.", token);
        Assert.Equal(HttpStatusCode.OK, (await Send(guardian, "/api/v1/guardian/subscriptions/external/resolve", new { reference = token })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(guardian, "/api/v1/guardian/subscriptions/external/resolve", new { reference = token + "tampered" })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var refs = scope.ServiceProvider.GetRequiredService<ScopedRenewalReferences>(); Assert.Null(refs.Resolve(token, DemoSeed.NogoomAcademyId, Guid.NewGuid())); Assert.Null(refs.Resolve(token, Guid.NewGuid(), Guid.NewGuid()));
        Assert.DoesNotContain("المستقبل", await r.Content.ReadAsStringAsync()); Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync($"/api/v1/guardian/children/{Slice2DemoSeed.AcademyBPlayerId}")).StatusCode);
    }
    [Fact] public async Task Payment_queue_has_sport_branch_type_and_date_filters()
    {
        using var c = await Owner(); _ = await Register(c);
        var result = await c.GetAsync($"/api/v1/manage/payment-requests?type=Renewal&status=Approved&branchId={Slice2DemoSeed.CityBranchId}&from=2026-01-01&to=2026-12-31"); Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync() + string.Join("\n", factory.Errors));
        var rows = (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToArray(); Assert.NotEmpty(rows);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        foreach (var x in rows)
        {
            Assert.Contains(x.GetProperty("type").GetString(), new[] { "Renewal", "FamilyRenewal" }); Assert.Equal("Approved", x.GetProperty("status").GetString()); Assert.False(string.IsNullOrWhiteSpace(x.GetProperty("sport").GetString()));
            if (x.GetProperty("type").GetString() == "FamilyRenewal") Assert.True(await db.FamilyRenewalLines.AnyAsync(l => l.AcademyId == DemoSeed.NogoomAcademyId && l.RequestId == x.GetProperty("id").GetGuid() && l.BranchId == Slice2DemoSeed.CityBranchId));
            else Assert.Equal("فرع مدينة نصر", x.GetProperty("branch").GetString());
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.GetAsync("/api/v1/manage/payment-requests?from=2026-10-10&to=2026-01-01")).StatusCode);
    }
    [Fact] public async Task Owner_decision_backlog_keeps_older_pending_requests_outside_the_chart_period()
    {
        using var owner = await Owner(); using var guardian = await Guardian();
        var path = $"/api/v1/reports/owner-summary?from=2026-09-28&to=2026-09-28&branchId={Slice2DemoSeed.CityBranchId}";
        var before = await owner.GetFromJsonAsync<JsonElement>(path);
        var name = "لاعب اختبار التسجيل " + Guid.NewGuid();
        var request = await Send(guardian, "/api/v1/guardian/enrollment-requests", new { requestType = "NewChild", newChildArabicName = name, newChildDateOfBirth = "2017-05-01", sportId = Slice2DemoSeed.FootballId, preferredBranchId = Slice2DemoSeed.CityBranchId, subscriptionPlanId = Slice3DemoSeed.FootballMonthlyId }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Created, request.StatusCode);
        var id = (await request.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            await db.NewEnrollmentRequests.Where(x => x.Id == id && x.AcademyId == DemoSeed.NogoomAcademyId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAtUtc, new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero)));
        }
        var after = await owner.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(before.GetProperty("paymentRequests").GetProperty("total").GetInt32(), after.GetProperty("paymentRequests").GetProperty("total").GetInt32());
        Assert.Equal(before.GetProperty("operations").GetProperty("pendingPaymentBacklog").GetInt32() + 1, after.GetProperty("operations").GetProperty("pendingPaymentBacklog").GetInt32());
        Assert.Equal(before.GetProperty("operations").GetProperty("pendingPaymentQueueBacklog").GetInt32() + 1, after.GetProperty("operations").GetProperty("pendingPaymentQueueBacklog").GetInt32());
        var queue = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/payment-requests?status=Pending&allDates=true&branchId={Slice2DemoSeed.CityBranchId}");
        Assert.Equal(queue.GetProperty("pending").GetInt32(), after.GetProperty("operations").GetProperty("pendingPaymentQueueBacklog").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync(path)).StatusCode);
    }
    private async Task<(Guid Id, string Email)> CreateStaff(HttpClient c, string role)
    {
        var email = "owner-review-test-" + Guid.NewGuid().ToString("N") + "@example.test";
        var r = await Send(c, "/api/v1/manage/staff/", new { name = "موظف اختبار عمليات", email, phone = "010" + Random.Shared.Next(10000000, 99999999), temporaryPassword = "Demo-Only-123!", role, jobTitle = "مدير فني" }); Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return ((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(), email);
    }
    [Fact] public async Task Owner_grants_and_revokes_individual_administrator_permissions_on_server()
    {
        using var owner = await Owner(); var staff = await CreateStaff(owner, "AcademyAdmin"); using var admin = factory.CreateClient(); (await Send(admin, "/api/v1/auth/login", new { email = staff.Email, password = "Demo-Only-123!" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/reports/financial")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/reports/owner-summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/manage/staff/")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"/api/v1/manage/staff/{staff.Id}/grants", new { grants = new[] { new { permission = AcademyPermissions.ReportsFinancialRead, allowed = true }, new { permission = AcademyPermissions.OwnerDashboardRead, allowed = true }, new { permission = AcademyPermissions.PeopleManage, allowed = false } } }, put: true)).StatusCode);
        // V2 newly created staff remain default-deny: legacy read flags cannot bypass the CRUD matrix.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/reports/financial")).StatusCode);
        (await Send(owner, $"/api/v1/manage/staff/{staff.Id}/access", new StaffAccessWrite(true, [], StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m == "finance", false, false, false)).ToArray()), put: true)).EnsureSuccessStatusCode();
        var accessAudit = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/staff-access-audit?allDates=true");
        Assert.Contains(accessAudit.GetProperty("items").EnumerateArray(), x => x.GetProperty("membershipId").GetGuid() == staff.Id && x.GetProperty("actorName").GetString() is { Length: > 0 });
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/reports/financial")).StatusCode); Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/reports/owner-summary")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/people/players")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/reports/staff-access-audit?allDates=true")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, $"/api/v1/manage/staff/{staff.Id}/grants", new { grants = Array.Empty<object>() }, put: true)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"/api/v1/manage/staff/{staff.Id}/grants", new { grants = new[] { new { permission = AcademyPermissions.ReportsFinancialRead, allowed = false } } }, put: true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/reports/financial")).StatusCode);
        (await Send(owner, $"/api/v1/manage/staff/{staff.Id}/access", new StaffAccessWrite(true, [], StaffAccessRules.Modules.Select(m => new StaffActionValue(m, false, false, false, false)).ToArray()), put: true)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/reports/financial")).StatusCode);
    }
    [Fact] public async Task Daily_coach_absence_and_lateness_are_reported_from_the_same_stored_record()
    {
        using var c = await Owner(); var coach = await CreateStaff(c, "Coach");
        (await Send(c, $"/api/v1/manage/staff-attendance/{coach.Id}/schedule", new { days = new[] { "Monday" }, start = "17:00:00", end = "22:00:00", thresholdMinutes = 30, deduction = 100m }, put: true)).EnsureSuccessStatusCode();
        var late = await Send(c, $"/api/v1/manage/staff-attendance/{coach.Id}", new { date = "2026-09-28", status = "Present", checkIn = "17:30:00", checkOut = "22:00:00", approvedDeduction = 0m }, put: true); Assert.Equal(HttpStatusCode.OK, late.StatusCode); var lateResult = await late.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(30, lateResult.GetProperty("lateMinutes").GetInt32()); Assert.Equal(100m, lateResult.GetProperty("suggestedDeduction").GetDecimal());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(c, $"/api/v1/manage/staff-attendance/{coach.Id}", new { date = "2026-09-28", status = "Present", checkIn = "17:30:00", approvedDeduction = 100m }, put: true)).StatusCode);
        (await Send(c, $"/api/v1/manage/staff-attendance/{coach.Id}", new { date = "2026-09-28", status = "Absent", approvedDeduction = 0m }, put: true)).EnsureSuccessStatusCode();
        var report = await c.GetFromJsonAsync<JsonElement>($"/api/v1/reports/staff-daily?coachId={coach.Id}&from=2026-09-01&to=2026-09-28&status=Absent"); Assert.Equal(1, report.GetProperty("totalCount").GetInt32()); Assert.Equal("Absent", report.GetProperty("items")[0].GetProperty("status").GetString()); Assert.Equal(JsonValueKind.Null, report.GetProperty("items")[0].GetProperty("checkIn").ValueKind);
        Assert.Contains("غائب", await c.GetStringAsync($"/api/v1/reports/staff-daily/export?coachId={coach.Id}&from=2026-09-01&to=2026-09-28"));
    }
    [Fact] public async Task Paid_registration_is_atomic_and_repeated_click_does_not_duplicate() { using var c = await Owner(); var record = await Register(c); var profile = await c.GetFromJsonAsync<JsonElement>($"/api/v1/people/players/{record.Player}"); Assert.True(profile.GetProperty("hasActiveSubscription").GetBoolean()); }
    [Fact] public async Task Changed_registration_quote_rolls_back_player_enrollment_and_collection()
    {
        using var c = await Owner(); var name = "لاعب اختبار التسجيل " + Guid.NewGuid();
        var body = new { playerArabicName = name, dateOfBirth = "2017-05-01", existingGuardianId = await GuardianId(), branchId = Slice2DemoSeed.CityBranchId, sportId = Slice2DemoSeed.FootballId, groupId = await FreshRegistrationGroup(), planId = Slice3DemoSeed.FootballMonthlyId, paymentMethod = "Online", expectedAmount = 1m };
        Assert.Equal(HttpStatusCode.Conflict, (await Send(c, "/api/v1/people/registrations", body, Guid.NewGuid().ToString())).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.False(await db.Players.AnyAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.ArabicName == name));
    }
    [Fact] public async Task New_child_payment_proof_activates_only_after_manual_acceptance_once()
    {
        using var guardian = await Guardian(); using var owner = await Owner(); var name = "لاعب اختبار التسجيل " + Guid.NewGuid();
        var body = new { requestType = "NewChild", newChildArabicName = name, newChildDateOfBirth = "2017-05-01", sportId = Slice2DemoSeed.FootballId, preferredBranchId = Slice2DemoSeed.CityBranchId, subscriptionPlanId = Slice3DemoSeed.FootballMonthlyId };
        var created = await Send(guardian, "/api/v1/guardian/enrollment-requests", body, Guid.NewGuid().ToString()); Assert.Equal(HttpStatusCode.Created, created.StatusCode); var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var approval = new { trainingGroupId = await FreshRegistrationGroup() };
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"/api/v1/enrollment-requests/{id}/approve", approval)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.False(await db.Players.AnyAsync(x => x.ArabicName == name));
        var asset = await Upload(guardian, "Proof"); Assert.Equal(HttpStatusCode.OK, (await Send(guardian, $"/api/v1/guardian/enrollment-requests/{id}/proof", new { assetId = asset })).StatusCode);
        var accepted = await Send(owner, $"/api/v1/enrollment-requests/{id}/approve", approval); Assert.True(accepted.StatusCode == HttpStatusCode.OK, string.Join("\n", factory.Errors));
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, $"/api/v1/enrollment-requests/{id}/approve", approval)).StatusCode);
        var result = await accepted.Content.ReadFromJsonAsync<JsonElement>(); var enrollment = result.GetProperty("enrollmentId").GetGuid();
        Assert.Equal(1, await db.Collections.CountAsync(x => x.SportEnrollmentId == enrollment)); Assert.Equal(1, await db.SubscriptionPeriods.CountAsync(x => x.SportEnrollmentId == enrollment));
        var collection = await db.Collections.SingleAsync(x => x.SportEnrollmentId == enrollment); Assert.Equal("Online", collection.PaymentMethod); Assert.Equal(1, await db.Receipts.CountAsync(x => x.CollectionId == collection.Id));
        Assert.Equal(HttpStatusCode.OK, (await guardian.GetAsync($"/api/v1/guardian/children/{result.GetProperty("playerId").GetGuid()}" )).StatusCode);
    }
    [Fact] public async Task Refund_is_append_only_idempotent_and_bounded_by_collection()
    {
        using var c = await Owner(); var record = await Register(c); var body = new { collectionId = record.Collection, amount = 100m, reason = (string?)null }; var key = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.OK, (await Send(c, $"/api/v1/people/players/{record.Player}/refunds", body, key)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(c, $"/api/v1/people/players/{record.Player}/refunds", body, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(c, $"/api/v1/people/players/{record.Player}/refunds", new { collectionId = record.Collection, amount = 999999m }, Guid.NewGuid().ToString())).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Equal(1, await db.CollectionRefunds.CountAsync(x => x.CollectionId == record.Collection)); Assert.True(await db.Receipts.AnyAsync(x => x.CollectionId == record.Collection));
    }
    [Fact] public async Task New_asset_guids_and_foreign_player_refunds_are_not_disclosed()
    {
        using var c = await Owner(); Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/assets/{Guid.NewGuid()}")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/people/players/{Slice2DemoSeed.AcademyBPlayerId}/refunds")).StatusCode);
    }
    [Fact] public async Task Legacy_group_transfer_cannot_bypass_review_and_keeps_subscription_and_history_unchanged()
    {
        using var c = await Owner(); var record = await Register(c); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var enrollment = await db.SportEnrollments.Include(x => x.TrainingGroup).SingleAsync(x => x.Id == record.Enrollment); var old = Assert.IsType<TrainingGroup>(enrollment.TrainingGroup);
        var g = new TrainingGroup { Id = Guid.NewGuid(), AcademyId = old.AcademyId, ArabicName = "مجموعة نقل اختبار " + Guid.NewGuid(), BranchId = old.BranchId, SportId = old.SportId, AgeCategoryId = old.AgeCategoryId, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow }; db.TrainingGroups.Add(g); await db.SaveChangesAsync();
        var session = await db.TrainingSessions.FirstAsync(x => x.AcademyId == old.AcademyId && x.TrainingGroupId == old.Id);
        var attendance = new PlayerAttendance { Id = Guid.NewGuid(), AcademyId = old.AcademyId, TrainingSessionId = session.Id, TrainingGroupId = old.Id, SportEnrollmentId = enrollment.Id, Status = AttendanceStatus.Present, RecordedByUserId = await db.Users.Where(x => x.Email == DemoSeed.OwnerEmail).Select(x => x.Id).SingleAsync() }; db.PlayerAttendances.Add(attendance); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await Send(c, $"/api/v1/people/enrollments/{record.Enrollment}/group", new { groupId = g.Id }, put: true)).StatusCode);
        await db.Entry(enrollment).ReloadAsync(); Assert.Equal(old.Id, enrollment.TrainingGroupId); Assert.Equal(1, await db.SubscriptionPeriods.CountAsync(x => x.SportEnrollmentId == record.Enrollment)); Assert.False(await db.EnrollmentGroupTransfers.AnyAsync(x => x.SportEnrollmentId == record.Enrollment));
        var historical = await db.PlayerAttendances.AsNoTracking().SingleAsync(x => x.Id == attendance.Id); Assert.Equal(old.Id, historical.TrainingGroupId); Assert.Equal(session.Id, historical.TrainingSessionId); Assert.Equal(AttendanceStatus.Present, historical.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(c, $"/api/v1/people/enrollments/{record.Enrollment}/group", new { groupId = Slice2DemoSeed.SwimmingGroupId }, put: true)).StatusCode);
    }
    [Fact] public async Task Timetable_is_group_scoped_repeatable_and_supports_six_months()
    {
        using var c = await Owner(); var record = await Register(c); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var original = Assert.IsType<TrainingGroup>(await db.SportEnrollments.Include(x => x.TrainingGroup).Where(x => x.Id == record.Enrollment).Select(x => x.TrainingGroup).SingleAsync()); var newGroup = new TrainingGroup { Id = Guid.NewGuid(), AcademyId = original.AcademyId, ArabicName = "جدول اختبار مستقل " + Guid.NewGuid(), BranchId = original.BranchId, SportId = original.SportId, AgeCategoryId = original.AgeCategoryId }; db.TrainingGroups.Add(newGroup); await db.SaveChangesAsync(); var group = newGroup.Id;
        var body = new { from = "2026-10-01", to = "2027-03-31", days = new[] { "Monday", "Wednesday" }, startTime = "17:00:00", endTime = "18:00:00" };
        var result = await Send(c, $"/api/v1/manage/groups/{group}/timetable", body); Assert.True(result.StatusCode == HttpStatusCode.OK, string.Join("\n", factory.Errors)); var count = await db.TrainingSessions.CountAsync(x => x.TrainingGroupId == group && x.SessionDate >= new DateOnly(2026, 10, 1));
        Assert.Equal(HttpStatusCode.OK, (await Send(c, $"/api/v1/manage/groups/{group}/timetable", body)).StatusCode); Assert.Equal(count, await db.TrainingSessions.CountAsync(x => x.TrainingGroupId == group && x.SessionDate >= new DateOnly(2026, 10, 1))); Assert.True(count >= 50);
    }
    [Fact] public async Task Linked_siblings_get_highest_non_stacking_discount()
    {
        using var c = await Owner(); var record = await Register(c); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var rules = scope.ServiceProvider.GetRequiredService<DiscountRules>();
        var best = new DiscountRule { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "مدرسة اختبار " + Guid.NewGuid(), Reason = "اتفاق مدرسة", Percentage = 25, IsActive = true }; db.DiscountRules.Add(best); db.DiscountRuleVersions.Add(DiscountRuleVersion.Create(best, null, DateTimeOffset.UtcNow)); db.PlayerDiscountLinks.Add(new PlayerDiscountLink { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, PlayerId = record.Player, DiscountRuleId = best.Id }); await db.SaveChangesAsync();
        var actor = await db.Users.Where(x => x.NormalizedEmail == DemoSeed.OwnerEmail.ToUpperInvariant()).Select(x => x.Id).SingleAsync(); var price = await rules.CalculateAsync(DemoSeed.NogoomAcademyId, record.Player, 1000, actor); Assert.Equal(250m, price.DiscountAmount); Assert.Equal(750m, price.FinalAmount);
    }
    private sealed class Factory(string connection) : WebApplicationFactory<Program> { public List<string> Errors { get; } = []; private readonly string mediaRoot = Path.Combine(Path.GetTempPath(), "academy-owner-review-" + Guid.NewGuid().ToString("N")); protected override void ConfigureWebHost(IWebHostBuilder b) { b.ConfigureLogging(l => l.AddProvider(new ErrorLoggerProvider(Errors))); b.UseEnvironment("Demo"); b.UseSetting("Media:PrivateRoot", mediaRoot); b.UseSetting("ConnectionStrings:Default", connection); b.UseSetting("Demo:SeedProfile", "LegacyRegression"); b.UseSetting("Demo:SeedEnabled", "true"); b.UseSetting("Demo:FixedOtpEnabled", "true"); b.UseSetting("Demo:FixedOtp", "246810"); b.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); b.UseSetting("Demo:ReferenceDate", "2026-09-28"); } public override async ValueTask DisposeAsync() { await base.DisposeAsync(); if (Directory.Exists(mediaRoot)) Directory.Delete(mediaRoot, true); } }
    private sealed class ErrorLoggerProvider(List<string> errors) : ILoggerProvider { public ILogger CreateLogger(string name) => new ErrorLogger(errors); public void Dispose() { } }
    private sealed class ErrorLogger(List<string> errors) : ILogger { public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null; public bool IsEnabled(LogLevel level) => level >= LogLevel.Error; public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter) { if (level >= LogLevel.Error && error is not null) errors.Add(error.ToString()); } }
}
