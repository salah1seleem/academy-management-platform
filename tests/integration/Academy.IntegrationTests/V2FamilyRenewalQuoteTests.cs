using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class SubscriptionPaymentTests
{
    private const string FamilyQuotes = "/api/v1/guardian/subscriptions/family";
    private static decimal Decimal(JsonElement e, string key) => decimal.Parse(e.GetProperty(key).GetString()!, CultureInfo.InvariantCulture);
    private async Task<FamilyRenewalChoice[]> MainFamilyChoices() => [new(await Enrollment(Slice2DemoSeed.OmarPlayerId, Slice2DemoSeed.FootballId), Slice3DemoSeed.FootballMonthlyId), new(await Enrollment(Slice2DemoSeed.OmarPlayerId, Slice2DemoSeed.SwimmingId), Slice3DemoSeed.SwimmingMonthlyId), new(await Enrollment(Slice2DemoSeed.MariamPlayerId, Slice2DemoSeed.FootballId), Slice3DemoSeed.FootballMonthlyId)];
    [Fact]
    public async Task Family_quote_prices_three_enrollments_for_two_children_without_creating_financial_records()
    {
        using var guardian = await GuardianClient(); var choices = await MainFamilyChoices(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var counts = new[] { await db.RenewalRequests.CountAsync(), await db.PaymentRequests.CountAsync(), await db.Collections.CountAsync(), await db.SubscriptionPeriods.CountAsync(), await db.Receipts.CountAsync() };
        var response = await Post(guardian, FamilyQuotes + "/quote", new FamilyRenewalWrite(choices), await Csrf(guardian)); response.EnsureSuccessStatusCode(); var quote = await response.Content.ReadFromJsonAsync<JsonElement>(); var lines = quote.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, lines.Length); Assert.Equal(Decimal(quote, "totalAmount"), lines.Sum(x => Decimal(x, "finalAmount"))); Assert.Equal(Decimal(quote, "originalAmount") - Decimal(quote, "discountAmount"), Decimal(quote, "totalAmount")); Assert.True(Decimal(quote, "discountAmount") > 0);
        var repeat = await Post(guardian, FamilyQuotes + "/quote", new FamilyRenewalWrite(choices.Reverse().ToArray()), await Csrf(guardian)); repeat.EnsureSuccessStatusCode(); Assert.Equal(quote.GetProperty("quoteHash").GetString(), (await repeat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteHash").GetString());
        Assert.Equal(counts, new[] { await db.RenewalRequests.CountAsync(), await db.PaymentRequests.CountAsync(), await db.Collections.CountAsync(), await db.SubscriptionPeriods.CountAsync(), await db.Receipts.CountAsync() });
    }
    [Fact]
    public async Task Family_quote_requires_a_trusted_link_not_a_known_foreign_child_or_enrollment_id()
    {
        using var guardian = await GuardianClient(); var choices = await MainFamilyChoices();
        foreach (var player in new[] { Slice2DemoSeed.OtherPlayerId, Slice2DemoSeed.AcademyBPlayerId })
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var foreign = new FamilyRenewalChoice(await db.SportEnrollments.Where(x => x.PlayerId == player).Select(x => x.Id).FirstAsync(), Slice3DemoSeed.FootballMonthlyId);
            var response = await Post(guardian, FamilyQuotes + "/quote", new FamilyRenewalWrite([choices[0], foreign]), await Csrf(guardian)); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Assert.DoesNotContain("عمر أحمد حسن", await response.Content.ReadAsStringAsync());
        }
        var options = await guardian.GetFromJsonAsync<JsonElement>(FamilyQuotes + "/options"); var ids = options.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("playerId").GetGuid()).ToArray(); Assert.DoesNotContain(Slice2DemoSeed.OtherPlayerId, ids); Assert.DoesNotContain(Slice2DemoSeed.AcademyBPlayerId, ids);
        Assert.Equal(HttpStatusCode.BadRequest, (await guardian.PostAsJsonAsync(FamilyQuotes + "/quote", new FamilyRenewalWrite(choices))).StatusCode);
        foreach (var email in new[] { DemoSeed.OwnerEmail, DemoSeed.AdminEmail, DemoSeed.CoachEmail }) { using var staff = await InboxStaff(email); Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(FamilyQuotes + "/options")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Post(staff, FamilyQuotes + "/quote", new FamilyRenewalWrite(choices), await Csrf(staff))).StatusCode); }
    }
    [Fact]
    public async Task Family_quote_validates_shape_publication_sport_and_currency_without_partial_pricing()
    {
        using var guardian = await GuardianClient(); var choices = await MainFamilyChoices(); var csrf = await Csrf(guardian);
        foreach (var lines in new IReadOnlyList<FamilyRenewalChoice>?[] { null, [], [choices[0], choices[0]], [null!], Enumerable.Repeat(choices[0], 21).ToArray(), [choices[0] with { SubscriptionPlanId = choices[1].SubscriptionPlanId }] }) Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(guardian, FamilyQuotes + "/quote", new FamilyRenewalWrite(lines), csrf)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var plan = await db.SubscriptionPlans.SingleAsync(x => x.Id == choices[0].SubscriptionPlanId); var originalCurrency = plan.Currency; var allBranches = plan.AllBranches; var active = plan.IsActive;
        try
        {
            plan.Currency = "USD"; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(guardian, FamilyQuotes + "/quote", new FamilyRenewalWrite(choices), csrf)).StatusCode);
            plan.Currency = originalCurrency; plan.IsActive = false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(guardian, FamilyQuotes + "/quote", new FamilyRenewalWrite(choices), csrf)).StatusCode);
            plan.IsActive = true; plan.AllBranches = false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(guardian, FamilyQuotes + "/quote", new FamilyRenewalWrite(choices), csrf)).StatusCode);
        }
        finally { plan.Currency = originalCurrency; plan.AllBranches = allBranches; plan.IsActive = active; await db.SaveChangesAsync(); }
    }
    [Fact]
    public async Task Family_quote_rechecks_price_and_revoked_guardian_link_instead_of_reusing_a_stale_view()
    {
        using var guardian = await GuardianClient(); var choices = await MainFamilyChoices(); var csrf = await Csrf(guardian);
        var first = await Post(guardian, FamilyQuotes + "/quote", new FamilyRenewalWrite(choices), csrf); first.EnsureSuccessStatusCode(); var hash = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteHash").GetString();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var plan = await db.SubscriptionPlans.SingleAsync(x => x.Id == choices[0].SubscriptionPlanId); var original = plan.Price; var link = await db.GuardianPlayerLinks.SingleAsync(x => x.GuardianId == Slice2DemoSeed.MainGuardianId && x.PlayerId == Slice2DemoSeed.MariamPlayerId);
        try
        {
            plan.Price += 10m; await db.SaveChangesAsync(); var updated = await Post(guardian, FamilyQuotes + "/quote", new FamilyRenewalWrite(choices), csrf); updated.EnsureSuccessStatusCode(); Assert.NotEqual(hash, (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteHash").GetString());
            link.IsActive = false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.NotFound, (await Post(guardian, FamilyQuotes + "/quote", new FamilyRenewalWrite(choices), csrf)).StatusCode);
        }
        finally { plan.Price = original; link.IsActive = true; await db.SaveChangesAsync(); }
    }
    [Fact]
    public async Task Family_quote_applies_prospective_siblings_only_for_distinct_trusted_children_not_two_sports()
    {
        var phone = "+2010" + Random.Shared.Next(10000000, 99999999).ToString(CultureInfo.InvariantCulture); Guid guardianId; FamilyRenewalChoice[] choices;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var now = DateTimeOffset.UtcNow;
            var user = new ApplicationUser { Id = Guid.NewGuid(), DisplayName = "ولي أسرة اختبار اصطناعية", UserName = phone, PhoneNumber = phone, PhoneNumberConfirmed = true, CreatedAtUtc = now, UpdatedAtUtc = now }; Assert.True((await users.CreateAsync(user)).Succeeded);
            guardianId = Guid.NewGuid(); db.GuardianProfiles.Add(new() { Id = guardianId, AcademyId = DemoSeed.NogoomAcademyId, UserId = user.Id, DisplayName = user.DisplayName, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.AcademyMemberships.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, UserId = user.Id, Role = AcademyRole.Guardian, CreatedAtUtc = now, UpdatedAtUtc = now });
            var players = new[] { Guid.NewGuid(), Guid.NewGuid() };
            for (var i = 0; i < 2; i++) { db.Players.Add(new() { Id = players[i], AcademyId = DemoSeed.NogoomAcademyId, PlayerCode = "FAMILY-" + players[i].ToString("N")[..16], ArabicName = "لاعب أسرة اصطناعي " + i, DateOfBirth = new(2016, 1, 1), CreatedAtUtc = now, UpdatedAtUtc = now }); db.GuardianPlayerLinks.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, GuardianId = guardianId, PlayerId = players[i], CreatedByUserId = user.Id, CreatedAtUtc = now, UpdatedAtUtc = now }); }
            var enrollments = new[] { new SportEnrollment { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, PlayerId = players[0], BranchId = Slice2DemoSeed.CityBranchId, SportId = Slice2DemoSeed.FootballId, CreatedAtUtc = now, UpdatedAtUtc = now }, new SportEnrollment { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, PlayerId = players[0], BranchId = Slice2DemoSeed.FifthBranchId, SportId = Slice2DemoSeed.SwimmingId, CreatedAtUtc = now, UpdatedAtUtc = now }, new SportEnrollment { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, PlayerId = players[1], BranchId = Slice2DemoSeed.CityBranchId, SportId = Slice2DemoSeed.FootballId, CreatedAtUtc = now, UpdatedAtUtc = now } };
            db.SportEnrollments.AddRange(enrollments); await db.SaveChangesAsync(); choices = [new(enrollments[0].Id, Slice3DemoSeed.FootballMonthlyId), new(enrollments[1].Id, Slice3DemoSeed.SwimmingMonthlyId), new(enrollments[2].Id, Slice3DemoSeed.FootballMonthlyId)];
        }
        using var guardian = factory.CreateClient(); var csrf = await Csrf(guardian); var request = await Post(guardian, "/api/v1/auth/guardian/otp/request", new { phoneNumber = phone }, csrf); request.EnsureSuccessStatusCode(); var challenge = await request.Content.ReadFromJsonAsync<Challenge>(); (await Post(guardian, "/api/v1/auth/guardian/otp/verify", new { phoneNumber = phone, challengeId = challenge!.ChallengeId, code = "246810" }, csrf)).EnsureSuccessStatusCode(); csrf = await Csrf(guardian);
        var oneChild = await Post(guardian, FamilyQuotes + "/quote", new FamilyRenewalWrite(choices.Take(2).ToArray()), csrf); oneChild.EnsureSuccessStatusCode(); Assert.Equal(0m, Decimal(await oneChild.Content.ReadFromJsonAsync<JsonElement>(), "discountAmount"));
        var siblings = await Post(guardian, FamilyQuotes + "/quote", new FamilyRenewalWrite([choices[0], choices[2]]), csrf); siblings.EnsureSuccessStatusCode(); Assert.True(Decimal(await siblings.Content.ReadFromJsonAsync<JsonElement>(), "discountAmount") > 0);
        await using var finalScope = factory.Services.CreateAsyncScope(); var finalDb = finalScope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.False(await finalDb.RenewalRequests.AnyAsync(x => choices.Select(c => c.SportEnrollmentId).Contains(x.SportEnrollmentId)));
    }
}
