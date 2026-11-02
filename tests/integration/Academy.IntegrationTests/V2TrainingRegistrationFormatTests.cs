using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    [Fact]
    public async Task Training_format_manual_proof_admission_rechecks_resource_before_confirming_and_retries_once()
    {
        await using var license = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await PlacementReady(owner, secondPaid: false);
        (await Write(owner, $"/api/v1/manage/groups/{f.Group}/training-format", new GroupTrainingFormatWrite(TrainingRegistrationFormat.Private, true), true)).EnsureSuccessStatusCode();
        var created = await Write(owner, "/api/v1/subscriptions/plans", Body(f.Branch, "باقة إثبات خاصة " + Guid.NewGuid().ToString("N")[..8], type: SubscriptionPlanType.Duration) with { DurationDays = 365, TrainingFormat = TrainingFormat.Private }); created.EnsureSuccessStatusCode(); var plan = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); (await db.SportEnrollments.SingleAsync(e => e.Id == f.Second)).RegistrationFormat = TrainingRegistrationFormat.Private; await db.SaveChangesAsync();
        var request = await Write(owner, "/api/v1/subscriptions/admin-renewals", new { sportEnrollmentId = f.Second, subscriptionPlanId = plan }); request.EnsureSuccessStatusCode(); var paymentId = (await request.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid(); var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync(); var asset = Guid.NewGuid();
        (await db.PaymentRequests.SingleAsync(p => p.Id == paymentId)).Provider = "Manual"; db.PrivateAssets.Add(new() { Id = asset, AcademyId = DemoSeed.NogoomAcademyId, UploadedByUserId = actor, ContentType = "image/png", StorageName = "synthetic-proof-metadata-only.png", Length = 10, Purpose = "Proof" }); db.PaymentProofs.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, PaymentRequestId = paymentId, AssetId = asset, SubmittedByUserId = actor, Status = "Pending" }); await db.SaveChangesAsync();
        var path = $"/api/v1/manage/payments/{paymentId}/review"; Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, path, new ReviewWrite(true, f.Group, null))).StatusCode); Assert.False(await db.Collections.AnyAsync(c => c.PaymentRequestId == paymentId)); Assert.False(await db.SportEnrollments.AnyAsync(e => e.Id == f.Second && e.TrainingGroupId != null));
        var resource = await FacilityFixture(owner, branch: f.Branch); var day = PlacementToday.AddDays(1); var timetable = await Reviewed(owner, f.Group, new TimetableWrite(day, day, [day.DayOfWeek], new(10, 0), new(11, 0), new(resource, f.Coach, 2, false))); (await SaveTimetable(owner, f.Group, timetable, Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        var saved = await Write(owner, path, new ReviewWrite(true, f.Group, null)); Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync()); (await Write(owner, path, new ReviewWrite(true, f.Group, null))).EnsureSuccessStatusCode(); Assert.Equal(1, await db.Collections.CountAsync(c => c.PaymentRequestId == paymentId)); Assert.True(await db.SportEnrollments.AnyAsync(e => e.Id == f.Second && e.TrainingGroupId == f.Group));
        var decisions = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial-decisions?allDates=true&branchId={f.Branch}");
        var proofDecisions = decisions.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("sourceId").GetGuid() == paymentId && x.GetProperty("kind").GetString() == "RenewalProof").ToArray();
        Assert.Single(proofDecisions);
        Assert.Equal("Accepted", proofDecisions[0].GetProperty("state").GetString());
    }
    [Fact]
    public async Task Training_format_guardian_can_request_parallel_private_sport_but_not_duplicate_active_track()
    {
        using var owner = await Staff(); using var guardian = await Guardian(); var f = await Fixture(); var created = await Write(owner, "/api/v1/subscriptions/plans", Body(f.A, "باقة خاصة لولي الأمر " + Guid.NewGuid().ToString("N")[..8]) with { TrainingFormat = TrainingFormat.Private }); created.EnsureSuccessStatusCode(); var plan = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); var player = await PlayerFor(f.Enrollment);
        var body = new Academy.Api.Slice6.NewEnrollmentCreateRequest(NewEnrollmentRequestType.ExistingChildNewSport, player, null, null, null, Slice2DemoSeed.SwimmingId, f.A, null, plan);
        Assert.Equal(HttpStatusCode.Created, (await Write(guardian, "/api/v1/guardian/enrollment-requests", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(guardian, "/api/v1/guardian/enrollment-requests", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(guardian, "/api/v1/guardian/enrollment-requests", body with { SubscriptionPlanId = null })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Equal(1, await db.SportEnrollments.CountAsync(e => e.PlayerId == player)); Assert.False(await db.Collections.AnyAsync(c => c.RenewalRequest.SubscriptionPlanId == plan));
    }
    [Fact]
    public async Task Training_format_changed_track_cannot_confirm_an_incompatible_captured_payment()
    {
        await using var route = await InternalGatewayRouteScope.Begin(factory.Services); using var owner = await Staff(); var f = await Fixture(); var plan = await CreatePlan(owner, f.A); var pending = await Write(owner, "/api/v1/subscriptions/admin-renewals", new { sportEnrollmentId = f.Enrollment, subscriptionPlanId = plan }); pending.EnsureSuccessStatusCode(); var payment = (await pending.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); (await db.SportEnrollments.SingleAsync(e => e.Id == f.Enrollment)).RegistrationFormat = TrainingRegistrationFormat.Private; await db.SaveChangesAsync();
        var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGateway>(); var envelope = gateway.CreateTestEvent(await db.PaymentRequests.SingleAsync(p => p.Id == payment), PaymentEventOutcome.Success); var result = await scope.ServiceProvider.GetRequiredService<PaymentProcessor>().ProcessAsync(envelope); Assert.False(result.Accepted); Assert.Equal("quarantined", result.State); var held = await db.ProviderEventQuarantines.SingleAsync(q => q.PaymentRequestId == payment && q.ProviderEventId == envelope.ProviderEventId); Assert.Equal("registration-format-conflict", held.ReasonCode); var replay = await scope.ServiceProvider.GetRequiredService<PaymentProcessor>().ProcessAsync(envelope); Assert.False(replay.Accepted); Assert.True(replay.Replay); Assert.Equal("quarantined", replay.State); Assert.False(await db.Collections.AnyAsync(c => c.PaymentRequestId == payment)); Assert.False(await db.SubscriptionPeriods.AnyAsync(p => p.Collection.PaymentRequestId == payment));
    }
    [Fact]
    public async Task Training_format_seed_is_additive_keeps_separate_private_offer_and_never_collects_on_restart()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); await GroupPlacementDemoSeed.Seed(db, scope.ServiceProvider.GetRequiredService<CollectionService>()); await TrainingFormatDemoSeed.Seed(db); var money = await db.Collections.CountAsync(); var plans = await db.SubscriptionPlans.CountAsync(); await TrainingFormatDemoSeed.Seed(db); Assert.Equal(money, await db.Collections.CountAsync()); Assert.Equal(plans, await db.SubscriptionPlans.CountAsync()); Assert.Equal(TrainingFormat.Private, await db.SubscriptionPlans.Where(p => p.Id == GroupPlacementDemoSeed.Id(42)).Select(p => p.TrainingFormat).SingleAsync()); Assert.False(await db.SubscriptionPeriods.AnyAsync(p => p.SportEnrollmentId == GroupPlacementDemoSeed.Id(41)));
    }
    [Fact]
    public async Task Training_format_direct_admission_uses_shared_checks_and_rolls_back_player_money_then_replays_once()
    {
        await using var license = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await PlacementReady(owner, secondPaid: false);
        (await Write(owner, $"/api/v1/manage/groups/{f.Group}/training-format", new GroupTrainingFormatWrite(TrainingRegistrationFormat.Private, true), true)).EnsureSuccessStatusCode();
        var created = await Write(owner, "/api/v1/subscriptions/plans", Body(f.Branch, "خاص تسجيل اصطناعي " + Guid.NewGuid().ToString("N")[..8], type: SubscriptionPlanType.Duration) with { DurationDays = 365, TrainingFormat = TrainingFormat.Private }); created.EnsureSuccessStatusCode(); var plan = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var guardian = await db.GuardianProfiles.Where(g => g.User.PhoneNumber == "+201000000001").Select(g => g.Id).SingleAsync(); var year = await db.SportEnrollments.Where(e => e.Id == f.First).Select(e => e.Player.DateOfBirth.Year).SingleAsync(); var name = "لاعب خاص اصطناعي " + Guid.NewGuid().ToString("N")[..8];
        var quote = await Write(owner, "/api/v1/people/registration-quote", new { planId = plan, branchId = f.Branch, guardianPhone = DemoSeed.GuardianPhone }); quote.EnsureSuccessStatusCode();
        var amount = decimal.Parse((await quote.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("finalAmount").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var body = new RegistrationRequest(null, name, new DateOnly(year, 1, 3), Gender.Male, guardian, null, null, null, f.Branch, Slice2DemoSeed.SwimmingId, f.Group, plan, "Online", ExpectedAmount: amount, TrainingLevelId: f.Level, LevelReviewed: true);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, "/api/v1/people/registrations", body with { LevelReviewed = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, "/api/v1/people/registrations", body)).StatusCode); Assert.False(await db.Players.AnyAsync(p => p.ArabicName == name)); Assert.False(await db.Collections.AnyAsync(c => c.RenewalRequest.SubscriptionPlanId == plan));
        var resource = await FacilityFixture(owner, branch: f.Branch); var day = PlacementToday.AddDays(1); var timetable = await Reviewed(owner, f.Group, new TimetableWrite(day, day, [day.DayOfWeek], new(10, 0), new(11, 0), new(resource, f.Coach, 2, false))); (await SaveTimetable(owner, f.Group, timetable, Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        var token = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); var key = Guid.NewGuid().ToString();
        async Task<HttpResponseMessage> Send() { using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/people/registrations") { Content = JsonContent.Create(body) }; req.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString()); req.Headers.Add("Idempotency-Key", key); return await owner.SendAsync(req); }
        var saved = await Send(); Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync()); (await Send()).EnsureSuccessStatusCode();
        Assert.Equal(1, await db.Players.CountAsync(p => p.ArabicName == name)); Assert.Equal(1, await db.Collections.CountAsync(c => c.RenewalRequest.SubscriptionPlanId == plan)); Assert.Equal(1, await db.SportEnrollments.CountAsync(e => e.Player.ArabicName == name && e.RegistrationFormat == TrainingRegistrationFormat.Private && e.TrainingGroupId == f.Group && e.GroupAssignedFrom == PlacementToday));
    }
    [Fact]
    public async Task Training_format_parallel_registrations_keep_one_player_and_separate_waiting_without_money_or_duplicate_retries()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner); var player = await PlayerFor(f.First); var path = $"/api/v1/people/players/{player}/unassigned-enrollments";
        var privateResponse = await Write(owner, path, new UnassignedEnrollmentWrite(Slice2DemoSeed.SwimmingId, f.Branch, TrainingRegistrationFormat.Private)); Assert.Equal(HttpStatusCode.Created, privateResponse.StatusCode); var id = (await privateResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var retry = await Write(owner, path, new UnassignedEnrollmentWrite(Slice2DemoSeed.SwimmingId, f.Branch, TrainingRegistrationFormat.Private)); Assert.Equal(id, (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, path, new UnassignedEnrollmentWrite(Slice2DemoSeed.SwimmingId, f.Branch, (TrainingRegistrationFormat)99))).StatusCode);
        var candidates = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/groups/{f.Group}/placement-candidates"); Assert.DoesNotContain(candidates.GetProperty("players").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id); Assert.Empty((await Suggest(owner, id, new())).Choices);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Equal(2, await db.SportEnrollments.CountAsync(e => e.PlayerId == player && e.SportId == Slice2DemoSeed.SwimmingId)); Assert.False(await db.Collections.AnyAsync(c => c.SportEnrollmentId == id)); Assert.False(await db.SubscriptionPeriods.AnyAsync(c => c.SportEnrollmentId == id)); Assert.False(await db.PlayerAttendances.AnyAsync(c => c.SportEnrollmentId == id));
    }
    [Theory] [InlineData(TrainingRegistrationFormat.Private, TrainingFormat.Private)] [InlineData(TrainingRegistrationFormat.SemiPrivate, TrainingFormat.SemiPrivate)]
    public async Task Training_format_private_requires_explicit_capacity_review_coach_resource_and_matching_entitlement(TrainingRegistrationFormat registration, TrainingFormat product)
    {
        await using var license = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await PlacementReady(owner, secondPaid: false); var formatPath = $"/api/v1/manage/groups/{f.Group}/training-format";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, formatPath, new GroupTrainingFormatWrite(registration, false), true)).StatusCode);
        (await Write(owner, formatPath, new GroupTrainingFormatWrite(registration, true), true)).EnsureSuccessStatusCode(); var before = await ReviewPlacement(owner, f.Group, f.Second); Assert.Contains(before.Conflicts, x => x.Contains("حجز مورد")); Assert.Contains(before.Players[0].Reasons, x => x.Contains("نوع تسجيل"));
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); (await db.SportEnrollments.SingleAsync(e => e.Id == f.Second)).RegistrationFormat = registration; await db.SaveChangesAsync(); }
        var created = await Write(owner, "/api/v1/subscriptions/plans", Body(f.Branch, "باقة نمط اصطناعي " + Guid.NewGuid().ToString("N")[..8], type: SubscriptionPlanType.Duration) with { DurationDays = 365, TrainingFormat = product }); created.EnsureSuccessStatusCode(); var plan = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); await using var tx = await db.Database.BeginTransactionAsync(); var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync(); await scope.ServiceProvider.GetRequiredService<CollectionService>().CreateCashAsync(DemoSeed.NogoomAcademyId, actor, f.Second, plan, "format-" + Guid.NewGuid(), "Cash"); await tx.CommitAsync(); }
        var resource = await FacilityFixture(owner, branch: f.Branch); var date = PlacementToday.AddDays(2); var timetable = await Reviewed(owner, f.Group, new TimetableWrite(date.AddDays(-1), date, [date.AddDays(-1).DayOfWeek, date.DayOfWeek], new(10, 0), new(11, 0), new(resource, f.Coach, 2, false))); (await SaveTimetable(owner, f.Group, timetable, Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        var p = await ReviewPlacement(owner, f.Group, f.Second); Assert.Empty(p.Conflicts); Assert.Empty(p.Players[0].Reasons); Assert.Equal(f.Group, Assert.Single((await Suggest(owner, f.Second, new())).Choices).GroupId); (await Place(owner, f.Group, p)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, formatPath, new GroupTrainingFormatWrite(TrainingRegistrationFormat.Group, false), true)).StatusCode);
        await using var verifyScope = factory.Services.CreateAsyncScope(); var verifyDb = verifyScope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var group = await verifyDb.TrainingGroups.AsNoTracking().SingleAsync(g => g.Id == f.Group);
        (await Write(owner, $"/api/v1/manage/structure/groups/{f.Group}", new StructureUpdateRequest(group.ArabicName, null, null, null, null, group.BranchId, group.SportId, group.AgeCategoryId, 3), true)).EnsureSuccessStatusCode(); Assert.Null(await verifyDb.TrainingGroups.Where(g => g.Id == f.Group).Select(g => g.FormatReviewedAtUtc).SingleAsync()); Assert.Contains((await ReviewPlacement(owner, f.Group, f.First)).Conflicts, c => c.Contains("اعتماد السعة"));
    }
    [Fact]
    public async Task Training_format_cannot_use_group_entitlement_or_sell_wrong_product_and_keeps_season_on_group_track()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var groupPeriod = await db.SubscriptionPeriods.AsNoTracking().SingleAsync(p => p.SportEnrollmentId == f.First); var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
        (await db.SportEnrollments.SingleAsync(e => e.Id == f.First)).RegistrationFormat = TrainingRegistrationFormat.Private; await db.SaveChangesAsync();
        var p = await ReviewPlacement(owner, f.Group, f.First); Assert.Contains(p.Players[0].Reasons, x => x.Contains("نوع تسجيل"));
        await using (var tx = await db.Database.BeginTransactionAsync()) { await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<CollectionService>().CreateCashAsync(DemoSeed.NogoomAcademyId, actor, f.First, groupPeriod.SubscriptionPlanId, "wrong-format-" + Guid.NewGuid(), "Cash")); }
        Assert.Equal(RenewalCreationState.NotFound, (await scope.ServiceProvider.GetRequiredService<RenewalCreationService>().CreateAsync(DemoSeed.NogoomAcademyId, actor, f.First, groupPeriod.SubscriptionPlanId, "wrong-renewal-" + Guid.NewGuid(), true)).State);
        (await Write(owner, $"/api/v1/manage/groups/{f.Group}/training-format", new GroupTrainingFormatWrite(TrainingRegistrationFormat.Private, true), true)).EnsureSuccessStatusCode(); p = await ReviewPlacement(owner, f.Group, f.First); Assert.Contains(p.Players[0].Reasons, x => x.Contains("مطابق للنوع"));
        Assert.True(PlanCatalog.FormatMatches(TrainingRegistrationFormat.Group, TrainingFormat.Season)); Assert.False(PlanCatalog.FormatMatches(TrainingRegistrationFormat.Private, TrainingFormat.Season));
    }
    [Fact]
    public async Task Training_format_settings_require_tenant_role_CSRF_and_never_auto_classify_current_members()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner); var path = $"/api/v1/manage/groups/{f.Group}/training-format"; using var foreign = await Staff("owner.future@example.test"); using var guardian = await Guardian(); var body = new GroupTrainingFormatWrite(TrainingRegistrationFormat.Private, true);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(foreign, path, body, true)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Write(guardian, path, body, true)).StatusCode); Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync(path, body)).StatusCode);
        (await Place(owner, f.Group, await ReviewPlacement(owner, f.Group, f.First))).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, path, body, true)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Equal(TrainingRegistrationFormat.Group, await db.TrainingGroups.Where(g => g.Id == f.Group).Select(g => g.RegistrationFormat).SingleAsync());
    }
}
