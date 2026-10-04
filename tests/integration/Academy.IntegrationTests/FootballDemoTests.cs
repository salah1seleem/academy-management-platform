using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Demo;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class FootballDemoTests : IAsyncLifetime
{
    private readonly Factory factory = new();
    private static Guid AcademyId => FootballDemoSeed.AcademyId;
    public async Task InitializeAsync() { _ = factory.CreateClient(); await Reset(); }
    public async Task DisposeAsync() => await factory.DisposeAsync();
    private async Task Reset() { await using var scope = factory.Services.CreateAsyncScope(); await FootballDemoSeed.RunAsync(scope.ServiceProvider, true); }

    [Fact]
    public async Task Football_dataset_has_exact_counts_and_unique_linked_identities()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(["كرة القدم"], await db.Sports.Where(x => x.AcademyId == AcademyId).Select(x => x.ArabicName).ToArrayAsync());
        Assert.Equal(new[] { "فرع مدينة نصر", "فرع مدينتي", "فرع الشروق" }.Order(), (await db.Branches.Where(x => x.AcademyId == AcademyId).Select(x => x.ArabicName).ToArrayAsync()).Order());
        var players = await db.Players.Where(x => x.AcademyId == AcademyId).ToArrayAsync();
        Assert.Equal(30, players.Length); Assert.Equal(30, players.Select(p => p.PlayerCode).Distinct().Count()); Assert.Equal(30, players.Select(p => p.ArabicName).Distinct().Count());
        Assert.Equal(30, await db.SportEnrollments.CountAsync(x => x.AcademyId == AcademyId));
        Assert.Equal(30, await db.GuardianPlayerLinks.CountAsync(x => x.AcademyId == AcademyId));
        Assert.Equal(6, await db.TrainingGroups.CountAsync(x => x.AcademyId == AcademyId));
        var coaches = await db.AcademyMemberships.Where(m => m.AcademyId == AcademyId && m.Role == AcademyRole.Coach).Include(m => m.User).ToArrayAsync();
        Assert.Equal(4, coaches.Length); Assert.All(coaches, c => Assert.True(c.IsActive));
        Assert.Equal(4, coaches.Select(c => c.User.DisplayName).Distinct().Count()); Assert.Equal(4, coaches.Select(c => c.User.PhoneNumber).Distinct().Count());
        Assert.All(coaches, c => Assert.Equal(c.User.PhoneNumber, EgyptPhoneNormalizer.Normalize(c.User.PhoneNumber)));
        foreach (var coach in coaches) Assert.True(await db.StaffGroupAssignments.AnyAsync(a => a.AcademyId == AcademyId && a.AcademyMembershipId == coach.Id));
        Assert.Equal(1, await db.GuardianPlayerLinks.CountAsync(x => x.GuardianId == FootballDemoSeed.Id("guardian/0")));
        Assert.Equal(2, await db.GuardianPlayerLinks.CountAsync(x => x.GuardianId == FootballDemoSeed.Id("guardian/1")));
    }

    [Fact]
    public async Task Reset_is_repeatable_and_preserves_other_tenants_and_global_users()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var before = await db.Players.AsNoTracking().Where(p => p.AcademyId != AcademyId).Select(p => new { p.Id, p.ArabicName }).ToArrayAsync();
        var users = await db.Users.AsNoTracking().Select(u => new { u.Id, u.PasswordHash, u.SecurityStamp }).ToArrayAsync();
        var ids = await db.Players.Where(p => p.AcademyId == AcademyId).Select(p => p.Id).ToArrayAsync();
        await Reset(); await Reset();
        Assert.Equal(ids.Order(), (await db.Players.Where(p => p.AcademyId == AcademyId).Select(p => p.Id).ToArrayAsync()).Order());
        Assert.Equal(before.OrderBy(p => p.Id), (await db.Players.AsNoTracking().Where(p => p.AcademyId != AcademyId).Select(p => new { p.Id, p.ArabicName }).ToArrayAsync()).OrderBy(p => p.Id));
        Assert.Equal(users.OrderBy(u => u.Id), (await db.Users.AsNoTracking().Select(u => new { u.Id, u.PasswordHash, u.SecurityStamp }).ToArrayAsync()).OrderBy(u => u.Id));
        Assert.Equal(30, await db.Receipts.CountAsync(x => x.AcademyId == AcademyId));
        Assert.Equal(90, await db.PlayerAttendances.CountAsync(x => x.AcademyId == AcademyId));
        Assert.Equal(31, await db.PlayerEvaluations.CountAsync(x => x.AcademyId == AcademyId));
    }

    [Fact]
    public async Task Normal_startup_does_not_undo_manual_edits()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var player = await db.Players.SingleAsync(p => p.Id == FootballDemoSeed.Id("player/0")); player.ArabicName = "تعديل يدوي محفوظ"; await db.SaveChangesAsync();
        await FootballDemoSeed.RunAsync(scope.ServiceProvider, false); db.ChangeTracker.Clear();
        Assert.Equal("تعديل يدوي محفوظ", (await db.Players.SingleAsync(p => p.Id == player.Id)).ArabicName);
    }

    [Fact]
    public async Task Reset_refuses_missing_confirmation_and_changed_marker_before_deletion()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>(); config["Demo:ResetConfirmation"] = "wrong";
        await Assert.ThrowsAsync<InvalidOperationException>(() => FootballDemoSeed.RunAsync(scope.ServiceProvider, true));
        config["Demo:ResetConfirmation"] = "football-demo-only";
        var academy = await db.Academies.SingleAsync(a => a.Id == AcademyId); academy.EnglishName = "Unmarked"; await db.SaveChangesAsync();
        try { await Assert.ThrowsAsync<InvalidOperationException>(() => FootballDemoSeed.RunAsync(scope.ServiceProvider, true)); Assert.Equal(30, await db.Players.CountAsync(p => p.AcademyId == AcademyId)); }
        finally { academy.EnglishName = FootballDemoSeed.Marker; await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task Production_reset_is_forbidden_even_with_confirmation()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var previous = environment.EnvironmentName; environment.EnvironmentName = "Production";
        try { await Assert.ThrowsAsync<InvalidOperationException>(() => FootballDemoSeed.RunAsync(scope.ServiceProvider, true)); }
        finally { environment.EnvironmentName = previous; }
    }

    [Fact]
    public async Task Financial_attendance_and_evaluation_history_is_connected_and_nutrition_not_fabricated()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var today = new DateOnly(2026, 9, 28);
        Assert.Equal(10, await db.SubscriptionPeriods.CountAsync(p => p.AcademyId == AcademyId && p.EndDate < today));
        Assert.Equal(10, await db.SubscriptionPeriods.CountAsync(p => p.AcademyId == AcademyId && p.EndDate >= today && p.EndDate <= today.AddDays(7)));
        Assert.Equal(30, await db.Collections.CountAsync(c => c.AcademyId == AcademyId));
        Assert.Equal(24, await db.TrainingSessions.CountAsync(c => c.AcademyId == AcademyId));
        var meals = await db.NutritionItems.Where(m => m.AcademyId == AcademyId).ToArrayAsync();
        Assert.Equal(3, meals.Length); Assert.All(meals, m => { Assert.Null(m.Calories); Assert.DoesNotContain("شكشوكة", m.ArabicName); Assert.DoesNotContain("كشري", m.ArabicName); });
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("admin")]
    public async Task Authorized_staff_creates_individual_coach_idempotently_and_coach_can_use_mobile_OTP(string role)
    {
        using var client = await Login(role); var phone = NewPhone();
        var body = new { displayName = "مدرب اختبار مستقل", phoneNumber = phone, email = (string?)null, isActive = true, groupIds = new[] { FootballDemoSeed.Id("group/0") } };
        var response = await Post(client, body); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("membershipId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Post(client, body)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var user = await db.Users.SingleAsync(u => u.PhoneNumber == EgyptPhoneNormalizer.Normalize(phone));
        Assert.Null(user.PasswordHash); Assert.False(user.PhoneNumberConfirmed);
        Assert.Equal(1, await db.AcademyMemberships.CountAsync(m => m.UserId == user.Id));
        Assert.Equal(1, await db.StaffGroupAssignments.CountAsync(a => a.AcademyMembershipId == id));
        using var mobile = factory.CreateClient();
        var challengeResponse = await mobile.PostAsJsonAsync("/api/v1/mobile/auth/otp/request", new { phoneNumber = phone });
        var challenge = (await challengeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetGuid();
        var verified = await mobile.PostAsJsonAsync("/api/v1/mobile/auth/otp/verify", new { phoneNumber = phone, challengeId = challenge, code = "246810", deviceName = "Phase B test" });
        verified.EnsureSuccessStatusCode(); var tokens = await verified.Content.ReadFromJsonAsync<JsonElement>();
        mobile.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await mobile.GetAsync("/api/v1/reports/owner-summary")).StatusCode);
    }

    [Fact]
    public async Task Coach_and_anonymous_cannot_provision()
    {
        using var coach = await Login("coach1"); using var anonymous = factory.CreateClient();
        var body = new { displayName = "ممنوع", phoneNumber = NewPhone(), isActive = true };
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(coach, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(anonymous, body)).StatusCode);
    }

    [Fact]
    public async Task Invalid_or_foreign_group_does_not_create_identity_and_CSFR_is_required()
    {
        using var owner = await Login("owner"); var phone = NewPhone();
        var body = new { displayName = "اختبار مجموعة", phoneNumber = phone, isActive = true, groupIds = new[] { Guid.NewGuid() } };
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(owner, body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/manage/coaches", body)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Users.AnyAsync(u => u.PhoneNumber == EgyptPhoneNormalizer.Normalize(phone)));
    }

    [Fact]
    public async Task Existing_identity_is_not_renamed_and_inactive_coach_cannot_authenticate()
    {
        using var owner = await Login("owner");
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, new { displayName = "اسم بديل", phoneNumber = FootballDemoSeed.Phone(20), isActive = true })).StatusCode);
        var phone = NewPhone(); Assert.Equal(HttpStatusCode.Created, (await Post(owner, new { displayName = "مدرب غير نشط", phoneNumber = phone, isActive = false })).StatusCode);
        using var mobile = factory.CreateClient(); var challenge = await mobile.PostAsJsonAsync("/api/v1/mobile/auth/otp/request", new { phoneNumber = phone });
        Assert.Equal(HttpStatusCode.Unauthorized, challenge.StatusCode);
    }

    private static string NewPhone() => $"011{System.Security.Cryptography.RandomNumberGenerator.GetInt32(10000000, 99999999)}";

    [Fact]
    public async Task Shared_identity_is_reused_without_mutating_another_academy_or_credentials()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Academy.Infrastructure.Identity.ApplicationUser>>();
        var phone = EgyptPhoneNormalizer.Normalize(NewPhone())!; var userId = Guid.NewGuid(); var otherId = Guid.NewGuid();
        var user = new Academy.Infrastructure.Identity.ApplicationUser { Id = userId, UserName = phone, PhoneNumber = phone, DisplayName = "مدرب متعدد الأكاديميات", IsActive = true };
        Assert.True((await users.CreateAsync(user, "Original-Only-123!")).Succeeded);
        db.Academies.Add(new Academy.Infrastructure.Tenancy.Academy { Id = otherId, ArabicName = "أكاديمية عزل اختبارية", Slug = $"test-{otherId:N}", DefaultCurrency = "EGP", TimeZone = "Africa/Cairo" });
        db.AcademyMemberships.Add(new AcademyMembership { Id = Guid.NewGuid(), AcademyId = otherId, UserId = userId, Role = AcademyRole.Coach, IsActive = true });
        await db.SaveChangesAsync(); var hash = user.PasswordHash; var stamp = user.SecurityStamp;
        using var owner = await Login("owner");
        var response = await Post(owner, new { displayName = user.DisplayName, phoneNumber = phone, isActive = true }); response.EnsureSuccessStatusCode();
        await Reset(); db.ChangeTracker.Clear();
        user = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.Equal(hash, user.PasswordHash); Assert.Equal(stamp, user.SecurityStamp);
        Assert.True(await db.AcademyMemberships.AnyAsync(m => m.AcademyId == otherId && m.UserId == userId && m.IsActive));
        Assert.False(await db.GuardianPlayerLinks.AnyAsync(l => l.Guardian.UserId == userId));
    }

    [Fact]
    public async Task Concurrent_duplicate_create_has_one_identity_membership_and_assignment()
    {
        using var first = await Login("owner"); using var second = await Login("admin"); var phone = NewPhone();
        var body = new { displayName = "مدرب متزامن", phoneNumber = phone, isActive = true, groupIds = new[] { FootballDemoSeed.Id("group/0") } };
        var results = await Task.WhenAll(Post(first, body), Post(second, body));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created); Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var user = await db.Users.SingleAsync(u => u.PhoneNumber == EgyptPhoneNormalizer.Normalize(phone));
        Assert.Equal(1, await db.AcademyMemberships.CountAsync(m => m.AcademyId == AcademyId && m.UserId == user.Id));
    }
    private async Task<HttpClient> Login(string role)
    {
        var client = factory.CreateClient(); var token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = $"{role}.football@example.test", password = "Demo-Only-123!" }) };
        request.Headers.Add("X-CSRF-TOKEN", token); (await client.SendAsync(request)).EnsureSuccessStatusCode(); return client;
    }
    private static async Task<HttpResponseMessage> Post(HttpClient client, object body)
    {
        var token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/manage/coaches") { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-TOKEN", token); return await client.SendAsync(request);
    }
    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Demo"); builder.UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING"));
            builder.UseSetting("Demo:SeedProfile", "Football"); builder.UseSetting("Demo:SeedEnabled", "true"); builder.UseSetting("Demo:ResetConfirmation", "football-demo-only");
            builder.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); builder.UseSetting("Demo:ReferenceDate", "2026-09-28");
            builder.UseSetting("Demo:FixedOtpEnabled", "true"); builder.UseSetting("Demo:FixedOtp", "246810");
        }
    }
}
