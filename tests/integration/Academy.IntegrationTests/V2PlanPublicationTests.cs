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
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed partial class V2PlanPublicationTests : IAsyncLifetime
{
    private Factory factory = null!;
    public async Task InitializeAsync()
    {
        factory = new(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Isolated database required")); _ = factory.CreateClient();
        // Retain abandoned synthetic test audit rows, release only their queue leases on a repeated test run.
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await db.AcademyImportJobs.Where(j => j.SourceSystem.StartsWith("test-") && (j.State == ImportJobState.Queued || j.State == ImportJobState.Running)).ExecuteUpdateAsync(s => s.SetProperty(j => j.State, ImportJobState.Expired).SetProperty(j => j.StagedFile, (byte[]?)null).SetProperty(j => j.ErrorCode, "expired"));
    }
    public async Task DisposeAsync() => await factory.DisposeAsync();
    private async Task<(Guid A, Guid B, Guid Group, Guid Enrollment, Guid Owner, int BirthYear)> Fixture(bool linkedGuardian = true)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var age = await db.AgeCategories.FirstAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.IsActive);
        var birthYear = age.MinimumBirthYear ?? Math.Min(age.MaximumBirthYear ?? 2017, 2017);
        var owner = await db.Users.Where(x => x.Email == DemoSeed.OwnerEmail).Select(x => x.Id).SingleAsync();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var group = Guid.NewGuid(); var player = Guid.NewGuid(); var enrollment = Guid.NewGuid();
        foreach (var branch in new[] { a, b }) db.Branches.Add(new() { Id = branch, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "فرع سباحة تحقق " + branch.ToString("N")[..8] });
        db.TrainingGroups.Add(new() { Id = group, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "مجموعة سباحة تحقق " + group.ToString("N")[..8], BranchId = a, SportId = Slice2DemoSeed.SwimmingId, AgeCategoryId = age.Id, Capacity = 100 });
        db.Players.Add(new() { Id = player, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "لاعب اختبار نشر الباقات", PlayerCode = "PUB-" + player.ToString("N")[..12], DateOfBirth = new(birthYear, 1, 1) });
        db.SportEnrollments.Add(new() { Id = enrollment, AcademyId = DemoSeed.NogoomAcademyId, PlayerId = player, SportId = Slice2DemoSeed.SwimmingId, BranchId = a, TrainingGroupId = group });
        var guardian = await db.GuardianProfiles.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.User.PhoneNumber == DemoSeed.GuardianPhone).Select(x => x.Id).SingleAsync();
        if (linkedGuardian) db.GuardianPlayerLinks.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, GuardianId = guardian, PlayerId = player, CreatedByUserId = owner });
        await db.SaveChangesAsync(); return (a, b, group, enrollment, owner, birthYear);
    }
    private static PlanRequest Body(Guid branch, string name, decimal price = 600, int? revision = null, SubscriptionPlanType type = SubscriptionPlanType.Combined) => new(name, null, Slice2DemoSeed.SwimmingId, type, price, type == SubscriptionPlanType.Sessions ? null : 30, type == SubscriptionPlanType.Duration ? null : 8, AllBranches: false, BranchIds: [branch], ExpectedRevision: revision);
    private async Task<Guid> CreatePlan(HttpClient owner, Guid branch) { var r = await Write(owner, "/api/v1/subscriptions/plans", Body(branch, "سباحة اختبار " + Guid.NewGuid().ToString("N")[..8])); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Golden_season_is_2400_cash_for_five_calendar_months_and_preserves_terms(bool manual)
    {
        await using var route = await InternalGatewayRouteScope.Begin(factory.Services);
        var f = await Fixture(false); using var owner = await Staff();
        var body = Body(f.A, "موسم اختبار " + Guid.NewGuid().ToString("N")[..8], 2400, type: SubscriptionPlanType.Duration)
            with { DurationDays = null, DurationMonths = 5, TrainingFormat = TrainingFormat.Season, ReferenceMonthlyPrice = 600, ChargedMonths = 4 };
        var created = await Write(owner, "/api/v1/subscriptions/plans", body); created.EnsureSuccessStatusCode(); var plan = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var r = await Write(owner, "/api/v1/subscriptions/admin-renewals", new { sportEnrollmentId = f.Enrollment, subscriptionPlanId = plan }); r.EnsureSuccessStatusCode(); var payment = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid();
        (await Write(owner, $"/api/v1/subscriptions/plans/{plan}", body with { ExpectedRevision = 1, DurationMonths = 6, ReferenceMonthlyPrice = 700, Price = 2800 }, true)).EnsureSuccessStatusCode();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            if (manual) { await using var tx = await db.Database.BeginTransactionAsync(); await scope.ServiceProvider.GetRequiredService<CollectionService>().ConfirmAsync(DemoSeed.NogoomAcademyId, f.Owner, payment, "Online"); await tx.CommitAsync(); }
            else { var p = await db.PaymentRequests.SingleAsync(x => x.Id == payment); var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGateway>(); Assert.True((await scope.ServiceProvider.GetRequiredService<PaymentProcessor>().ProcessAsync(gateway.CreateTestEvent(p, PaymentEventOutcome.Success))).Accepted); }
        }
        await using var verify = factory.Services.CreateAsyncScope(); var v = verify.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var period = await v.SubscriptionPeriods.SingleAsync(x => x.Collection.PaymentRequestId == payment);
        Assert.Equal(2400m, period.PriceSnapshot); Assert.Equal(5, period.DurationMonthsSnapshot); Assert.Equal(TrainingFormat.Season, period.TrainingFormatSnapshot);
        Assert.Equal(period.StartDate.AddMonths(5).AddDays(-1), period.EndDate); Assert.Null(period.InitialSessions);
        Assert.Equal(480m, period.PriceSnapshot / period.DurationMonthsSnapshot!.Value);
        Assert.Single(await v.Collections.Where(x => x.PaymentRequestId == payment).ToListAsync());
        Assert.Equal(2400m, (await v.Receipts.SingleAsync(x => x.CollectionId == period.CollectionId)).Amount);
        await using var rollback = await v.Database.BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => v.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SubscriptionPeriods\" SET \"DurationMonthsSnapshot\" = 1 WHERE \"Id\" = {period.Id}")); Assert.Equal("23514", error.SqlState);
    }

    [Fact]
    public async Task Non_training_services_are_separate_and_cannot_activate_training_or_appear_in_its_options()
    {
        var f = await Fixture(); using var owner = await Staff(); using var guardian = await Guardian();
        var body = Body(f.A, "زيارة مسبح " + Guid.NewGuid().ToString("N")[..8], 100, type: SubscriptionPlanType.Duration) with { ServiceKind = AcademyServiceKind.DayPass, DurationDays = 1 };
        var created = await Write(owner, "/api/v1/subscriptions/plans", body); created.EnsureSuccessStatusCode(); var plan = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/plans/{plan}"); Assert.Equal("DayPass", detail.GetProperty("serviceKind").GetString());
        var options = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/subscriptions/enrollments/{f.Enrollment}/plans"); Assert.DoesNotContain(options.EnumerateArray(), x => x.GetProperty("id").GetGuid() == plan);
        var newOptions = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/guardian/enrollment-request-options"); Assert.DoesNotContain(newOptions.GetProperty("plans").EnumerateArray(), x => x.GetProperty("id").GetGuid() == plan);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(owner, "/api/v1/subscriptions/admin-renewals", new { sportEnrollmentId = f.Enrollment, subscriptionPlanId = plan })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(owner, "/api/v1/people/registration-quote", new { planId = plan, branchId = f.A })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, "/api/v1/subscriptions/plans", body with { ServiceKind = AcademyServiceKind.Training, TrainingFormat = TrainingFormat.Season, DurationDays = null, DurationMonths = 5, ReferenceMonthlyPrice = 600, ChargedMonths = 4, Price = 2399 })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, "/api/v1/subscriptions/plans", body with { ServiceKind = (AcademyServiceKind)999 })).StatusCode);
    }

    [Fact]
    public async Task ENR_01_branch_published_swimming_plan_appears_in_correct_options_quote_and_registration()
    {
        var f = await Fixture(); using var owner = await Staff(); var plan = await CreatePlan(owner, f.A); using var guardian = await Guardian();
        var available = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/subscriptions/enrollments/{f.Enrollment}/plans");
        Assert.Contains(available.EnumerateArray(), x => x.GetProperty("id").GetGuid() == plan);
        var options = await owner.GetFromJsonAsync<JsonElement>("/api/v1/subscriptions/plans"); var option = options.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == plan);
        Assert.False(option.GetProperty("allBranches").GetBoolean()); Assert.Equal([f.A], option.GetProperty("branchIds").EnumerateArray().Select(x => x.GetGuid()));
        var phone = "010" + Random.Shared.Next(10000000, 99999999);
        var quote = await Write(owner, "/api/v1/people/registration-quote", new { planId = plan, branchId = f.A, guardianPhone = phone }); quote.EnsureSuccessStatusCode();
        var value = await quote.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("600.00", value.GetProperty("finalAmount").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, "/api/v1/people/registration-quote", new { planId = plan, branchId = f.B, guardianPhone = phone })).StatusCode);
        var registration = await Write(owner, "/api/v1/people/registrations", new { playerArabicName = "لاعب سباحة جديد اصطناعي", dateOfBirth = $"{f.BirthYear}-01-02", guardianName = "ولي أمر اختبار", guardianPhone = phone, branchId = f.A, sportId = Slice2DemoSeed.SwimmingId, groupId = f.Group, planId = plan, paymentMethod = "Online", expectedAmount = 600 });
        registration.EnsureSuccessStatusCode();
        var id = (await registration.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var period = await db.SubscriptionPeriods.SingleAsync(x => x.SportEnrollment.PlayerId == id); Assert.Equal((600m, 8, 1), (period.PriceSnapshot, period.InitialSessions, period.PlanRevisionSnapshot));
        Assert.Equal(29, period.EndDate!.Value.DayNumber - period.StartDate.DayNumber);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FIN_03_pending_renewal_uses_captured_price_type_days_sessions_and_name_after_plan_changes(bool manual)
    {
        await using var route = await InternalGatewayRouteScope.Begin(factory.Services);
        var f = await Fixture(); using var owner = await Staff(); var plan = await CreatePlan(owner, f.A);
        var response = await Write(owner, "/api/v1/subscriptions/admin-renewals", new { sportEnrollmentId = f.Enrollment, subscriptionPlanId = plan }); response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(); var payment = created.GetProperty("paymentId").GetGuid();
        var old = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/plans/{plan}"); var name = old.GetProperty("arabicName").GetString()!;
        (await Write(owner, $"/api/v1/subscriptions/plans/{plan}", Body(f.B, name + " معدلة", 950, 1, SubscriptionPlanType.Sessions), true)).EnsureSuccessStatusCode();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            if (manual) { await using var tx = await db.Database.BeginTransactionAsync(); await scope.ServiceProvider.GetRequiredService<CollectionService>().ConfirmAsync(DemoSeed.NogoomAcademyId, f.Owner, payment, "Online"); await tx.CommitAsync(); }
            else { var p = await db.PaymentRequests.SingleAsync(x => x.Id == payment); var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGateway>(); Assert.True((await scope.ServiceProvider.GetRequiredService<PaymentProcessor>().ProcessAsync(gateway.CreateTestEvent(p, PaymentEventOutcome.Success))).Accepted); }
        }
        await using var verification = factory.Services.CreateAsyncScope(); var verifyDb = verification.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var period = await verifyDb.SubscriptionPeriods.SingleAsync(x => x.Collection.PaymentRequestId == payment);
        Assert.Equal((SubscriptionPlanType.Combined, 8, 1, name), (period.PlanTypeSnapshot, period.InitialSessions, period.PlanRevisionSnapshot, period.PlanNameSnapshot));
        Assert.Equal(29, period.EndDate!.Value.DayNumber - period.StartDate.DayNumber);
        var receipt = await verifyDb.Receipts.SingleAsync(x => x.Collection.PaymentRequestId == payment); Assert.Equal(600m, receipt.OriginalAmount); Assert.Equal(name, receipt.PlanNameSnapshot);
        await using (var scoped = factory.Services.CreateAsyncScope())
        {
            var branchDb = scoped.ServiceProvider.GetRequiredService<FoundationDbContext>(); branchDb.BindRequestScope(DemoSeed.NogoomAcademyId, f.Owner, Guid.NewGuid(), [f.A]);
            Assert.True(await branchDb.SubscriptionPeriods.AnyAsync(x => x.Id == period.Id));
            Assert.Equal(name, await branchDb.SubscriptionPeriods.Where(x => x.Id == period.Id).Select(x => x.PlanNameSnapshot ?? x.SubscriptionPlan.ArabicName).SingleAsync());
        }
        var context = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/admin-renewals/enrollments/{f.Enrollment}"); Assert.DoesNotContain(context.GetProperty("plans").EnumerateArray(), x => x.GetProperty("id").GetGuid() == plan);
        var versions = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/plans/{plan}"); Assert.Equal(2, versions.GetProperty("versions").GetArrayLength());
        Assert.Equal("600.00", versions.GetProperty("versions")[1].GetProperty("price").GetString());
        period.DurationDaysSnapshot = 999; await Assert.ThrowsAsync<InvalidOperationException>(() => verifyDb.SaveChangesAsync()); verifyDb.Entry(period).State = EntityState.Unchanged;
        var sqlError = await Assert.ThrowsAsync<PostgresException>(() => verifyDb.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SubscriptionPeriods\" SET \"DurationDaysSnapshot\" = 999 WHERE \"Id\" = {period.Id}")); Assert.Equal("23514", sqlError.SqlState);
    }
    [Fact]
    public async Task Price_versions_are_immutable_concurrent_edit_is_detected_and_archive_retains_history()
    {
        var f = await Fixture(); using var owner = await Staff(); var plan = await CreatePlan(owner, f.A);
        var edit = Body(f.A, "نسخة منافسة " + Guid.NewGuid().ToString("N")[..8], 650, 1);
        var attempts = await Task.WhenAll(Write(owner, $"/api/v1/subscriptions/plans/{plan}", edit, true), Write(owner, $"/api/v1/subscriptions/plans/{plan}", edit with { Price = 700 }, true));
        Assert.Single(attempts, x => x.IsSuccessStatusCode); Assert.Single(attempts, x => x.StatusCode == HttpStatusCode.Conflict);
        (await Write(owner, $"/api/v1/subscriptions/plans/{plan}/delete", new { })).EnsureSuccessStatusCode();
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/plans/{plan}"); Assert.False(detail.GetProperty("isActive").GetBoolean()); Assert.Equal(3, detail.GetProperty("versions").GetArrayLength());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var version = await db.PlanPriceVersions.FirstAsync(x => x.SubscriptionPlanId == plan);
        version.Price = 1; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.Entry(version).State = EntityState.Unchanged;
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PlanPriceVersions\" WHERE \"Id\" = {version.Id}")); Assert.Equal("23514", error.SqlState);
    }
    [Fact]
    public async Task Branch_admin_cannot_publish_outside_scope_or_reprice_a_shared_plan_and_tenant_IDOR_fails()
    {
        var f = await Fixture(); using var owner = await Staff(); var plan = await CreatePlan(owner, f.A);
        var email = $"pub.{Guid.NewGuid():N}@example.test";
        var created = await Write(owner, "/api/v1/manage/staff", new StaffWrite("إداري نشر اصطناعي", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, "اختبار")); created.EnsureSuccessStatusCode();
        var member = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var access = new StaffAccessWrite(false, [f.B], StaffAccessRules.Modules.Select(m => new StaffActionValue(m, true, m != "followups", true, m != "followups")).ToArray());
        (await Write(owner, $"/api/v1/manage/staff/{member}/access", access, true)).EnsureSuccessStatusCode(); using var admin = await Staff(email);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/subscriptions/plans/{plan}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, "/api/v1/subscriptions/plans", Body(f.A, "خارج الفرع"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, "/api/v1/subscriptions/plans", Body(f.B, "كل الفروع") with { AllBranches = true })).StatusCode);
        var own = await CreatePlan(admin, f.B); Assert.NotEqual(Guid.Empty, own);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, $"/api/v1/subscriptions/plans/{Slice3DemoSeed.SwimmingMonthlyId}", Body(f.B, "استيلاء على باقة مشتركة"), true)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var foreign = await db.SubscriptionPlans.FirstAsync(x => x.AcademyId == DemoSeed.FutureAcademyId);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/subscriptions/plans/{foreign.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, "/api/v1/subscriptions/plans", Body(f.A, "فرع أكاديمية أخرى") with { BranchIds = [await db.Branches.Where(x => x.AcademyId == DemoSeed.FutureAcademyId).Select(x => x.Id).FirstAsync()] })).StatusCode);
    }
    [Fact]
    public async Task Guardian_new_enrollment_captures_terms_and_rejects_unpublished_branch()
    {
        var f = await Fixture(); using var owner = await Staff(); var plan = await CreatePlan(owner, f.A); using var guardian = await Guardian();
        object BodyFor(Guid branch) => new { requestType = 2, newChildArabicName = "طفل طلب اصطناعي " + Guid.NewGuid().ToString("N")[..8], newChildDateOfBirth = $"{f.BirthYear}-01-01", sportId = Slice2DemoSeed.SwimmingId, preferredBranchId = branch, subscriptionPlanId = plan };
        Assert.Equal(HttpStatusCode.BadRequest, (await Write(guardian, "/api/v1/guardian/enrollment-requests", BodyFor(f.B))).StatusCode);
        var r = await Write(guardian, "/api/v1/guardian/enrollment-requests", BodyFor(f.A)); r.EnsureSuccessStatusCode(); var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var request = await db.NewEnrollmentRequests.SingleAsync(x => x.Id == id);
        Assert.Equal((30, 8, 1), (request.DurationDaysSnapshot, request.SessionCountSnapshot, request.PlanRevisionSnapshot)); Assert.False(await db.Collections.AnyAsync(x => x.PaymentRequestId == request.PaymentRequestId));
        using var upload = new MultipartFormDataContent(); var file = new ByteArrayContent([137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0]); file.Headers.ContentType = new("image/png"); upload.Add(file, "file", "synthetic-proof.png"); upload.Add(new StringContent("Proof"), "purpose");
        var csrf = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/assets/upload") { Content = upload }; uploadRequest.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var uploaded = await guardian.SendAsync(uploadRequest); uploaded.EnsureSuccessStatusCode(); var asset = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Write(guardian, $"/api/v1/guardian/enrollment-requests/{id}/proof", new { assetId = asset })).EnsureSuccessStatusCode();
        (await Write(owner, $"/api/v1/subscriptions/plans/{plan}", Body(f.B, "سعر جديد " + Guid.NewGuid().ToString("N")[..8], 950, 1, SubscriptionPlanType.Sessions), true)).EnsureSuccessStatusCode();
        (await Write(owner, $"/api/v1/enrollment-requests/{id}/approve", new { trainingGroupId = f.Group })).EnsureSuccessStatusCode();
        db.ChangeTracker.Clear(); var approved = await db.NewEnrollmentRequests.SingleAsync(x => x.Id == id);
        var period = await db.SubscriptionPeriods.SingleAsync(x => x.SportEnrollmentId == approved.CreatedSportEnrollmentId);
        Assert.Equal((30, 8, 1), (period.DurationDaysSnapshot, period.InitialSessions, period.PlanRevisionSnapshot));
        var receipt = await db.Receipts.SingleAsync(x => x.SportEnrollmentId == approved.CreatedSportEnrollmentId); Assert.Equal(600, receipt.OriginalAmount); Assert.Equal(request.PlanNameSnapshot, receipt.PlanNameSnapshot);
    }
    [Fact]
    public async Task Demo_publication_seed_is_repeatable_and_local_demo_reset_flag_never_unlocks_another_tenant()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await PlanPublicationDemoSeed.SeedAsync(db); await PlanPublicationDemoSeed.SeedAsync(db);
        Assert.Equal(1, await db.SubscriptionPlans.CountAsync(x => x.Id == PlanPublicationDemoSeed.PlanId));
        Assert.Equal(1, await db.PlanPriceVersions.CountAsync(x => x.SubscriptionPlanId == PlanPublicationDemoSeed.PlanId));
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('academy.reset_demo_tenant', '30000000-0000-0000-0000-000000000003', true)");
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PlanPriceVersions\" WHERE \"SubscriptionPlanId\" = {PlanPublicationDemoSeed.PlanId}")); Assert.Equal("23514", error.SqlState);
            await tx.RollbackAsync();
        }
        var connection = db.Database.GetDbConnection(); await db.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT current_setting('academy.reset_demo_tenant', true)";
        Assert.NotEqual("30000000-0000-0000-0000-000000000003", (await command.ExecuteScalarAsync())?.ToString());
    }
    private async Task<HttpClient> Staff(string email = DemoSeed.OwnerEmail) { var c = factory.CreateClient(); (await Write(c, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" })).EnsureSuccessStatusCode(); return c; }
    private async Task<HttpClient> Guardian() { var c = factory.CreateClient(); var r = await Write(c, "/api/v1/auth/guardian/otp/request", new { phoneNumber = DemoSeed.GuardianPhone }); r.EnsureSuccessStatusCode(); var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetGuid(); (await Write(c, "/api/v1/auth/guardian/otp/verify", new { phoneNumber = DemoSeed.GuardianPhone, challengeId = id, code = "246810" })).EnsureSuccessStatusCode(); return c; }
    private static async Task<HttpResponseMessage> Write(HttpClient c, string path, object body, bool put = false) { var csrf = await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); var r = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(body) }; r.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); r.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); return await c.SendAsync(r); }
    private sealed class Factory(string connection) : WebApplicationFactory<Program> { public VisitTestClock VisitClock { get; } = new(); protected override void ConfigureWebHost(IWebHostBuilder b) { b.ConfigureServices(s => { s.AddSingleton<TimeProvider>(VisitClock); s.PostConfigure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme, o => o.TimeProvider = VisitClock); }); b.UseEnvironment("Demo"); b.UseSetting("Imports:WorkerEnabled", "false"); b.UseSetting("ConnectionStrings:Default", connection); b.UseSetting("Demo:SeedProfile", "LegacyRegression"); b.UseSetting("Demo:SeedEnabled", "true"); b.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); b.UseSetting("Demo:FixedOtpEnabled", "true"); b.UseSetting("Demo:FixedOtp", "246810"); b.UseSetting("Demo:ReferenceDate", "2026-09-28"); b.UseSetting("Payments:InternalTest:Enabled", "true"); b.UseSetting("Payments:InternalTest:SigningKey", "Demo-Test-Signing-Key-Only-123456"); } }
}
