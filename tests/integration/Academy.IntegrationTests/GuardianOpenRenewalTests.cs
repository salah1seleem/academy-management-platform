using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class SubscriptionPaymentTests
{
    private const string GuardianRenewalPath = "/api/v1/guardian/subscriptions/renewals";
    private static string GuardianReviewPath(Guid enrollmentId) =>
        $"/api/v1/guardian/subscriptions/enrollments/{enrollmentId}/renewal-review";

    [Fact]
    public async Task Guardian_open_renewal_serializes_different_keys_and_reopens_the_saved_payment()
    {
        await using var family = await NewFamily();
        await using var other = await NewFamily();
        await using var gateway = await InternalGatewayRouteScope.Begin(factory.Services);
        var choice = family.Choices[0];
        var enrollment = choice.SportEnrollmentId;
        var body = new { sportEnrollmentId = enrollment, subscriptionPlanId = choice.SubscriptionPlanId };
        var token = await Csrf(family.Client);
        var playersBefore = await Count<Player>(p => p.AcademyId == DemoSeed.NogoomAcademyId);
        var review = await ReadBoundaryJson(await family.Client.GetAsync(GuardianReviewPath(enrollment)));
        Assert.Equal(JsonValueKind.Null, review.GetProperty("openRenewal").ValueKind);
        Assert.NotEmpty(review.GetProperty("plans").EnumerateArray());
        var keys = new[] { Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N") };
        var requests = await Task.WhenAll(keys.Select(key => Post(family.Client, GuardianRenewalPath, body, token, key)));
        var accepted = Assert.Single(requests, response => response.StatusCode == HttpStatusCode.Created);
        var rejected = Assert.Single(requests, response => response.StatusCode == HttpStatusCode.Conflict);
        var id = (await accepted.Content.ReadFromJsonAsync<Created>())!.PaymentId;
        var conflict = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("renewal_already_open", conflict.GetProperty("code").GetString());
        Assert.False(conflict.TryGetProperty("paymentId", out _));
        var winningKey = keys[Array.IndexOf(requests, accepted)];
        using var replay = await Post(family.Client, GuardianRenewalPath, body, token, winningKey);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(id, (await replay.Content.ReadFromJsonAsync<Created>())!.PaymentId);
        var open = await ReadBoundaryJson(await family.Client.GetAsync(GuardianReviewPath(enrollment)));
        Assert.Empty(open.GetProperty("plans").EnumerateArray());
        Assert.Equal(id, open.GetProperty("openRenewal").GetProperty("paymentId").GetGuid());
        Assert.Equal(1, await Count<RenewalRequest>(r => r.SportEnrollmentId == enrollment));
        Assert.Equal(0, await Count<PaymentCollection>(c => c.SportEnrollmentId == enrollment));
        Assert.Equal(0, await Count<SubscriptionPeriod>(p => p.SportEnrollmentId == enrollment));
        Assert.Equal(0, await Count<Receipt>(r => r.SportEnrollmentId == enrollment));

        using var denied = await other.Client.GetAsync(GuardianReviewPath(enrollment));
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        await LinkAdditionalGuardian(other, enrollment);
        var shared = await ReadBoundaryJson(await other.Client.GetAsync(GuardianReviewPath(enrollment)));
        Assert.Equal(JsonValueKind.Null, shared.GetProperty("openRenewal").GetProperty("paymentId").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/v1/guardian/subscriptions/payments/{id}")).StatusCode);
        for (var attempt = 0; attempt < 2; attempt++)
            (await Post(family.Client, $"/api/v1/guardian/subscriptions/payments/{id}/simulate", new { outcome = "Success" }, token)).EnsureSuccessStatusCode();
        Assert.Equal(1, await Count<PaymentCollection>(c => c.SportEnrollmentId == enrollment));
        Assert.Equal(1, await Count<SubscriptionPeriod>(p => p.SportEnrollmentId == enrollment));
        Assert.Equal(1, await Count<Receipt>(r => r.SportEnrollmentId == enrollment));
        Assert.Equal(playersBefore, await Count<Player>(p => p.AcademyId == DemoSeed.NogoomAcademyId));
        Assert.Equal(JsonValueKind.Null, (await ReadBoundaryJson(await family.Client.GetAsync(GuardianReviewPath(enrollment)))).GetProperty("openRenewal").ValueKind);
    }

    [Fact]
    public async Task Guardian_open_renewal_shows_staff_and_family_requests_without_disclosing_another_actor()
    {
        await using var family = await NewFamily();
        await using var second = await NewFamily();
        using var owner = factory.CreateClient();
        await StaffLogin(owner);
        var choice = family.Choices[0];
        var body = new { sportEnrollmentId = choice.SportEnrollmentId, subscriptionPlanId = choice.SubscriptionPlanId };
        var staff = await ReadBoundaryJson(await Post(owner, "/api/v1/subscriptions/admin-renewals", body, await Csrf(owner), Guid.NewGuid().ToString("N")));
        var review = await ReadBoundaryJson(await family.Client.GetAsync(GuardianReviewPath(choice.SportEnrollmentId)));
        Assert.Empty(review.GetProperty("plans").EnumerateArray());
        Assert.Equal("Payment", review.GetProperty("openRenewal").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, review.GetProperty("openRenewal").GetProperty("paymentId").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await family.Client.GetAsync($"/api/v1/guardian/subscriptions/payments/{staff.GetProperty("paymentId").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(family.Client, GuardianRenewalPath, body, await Csrf(family.Client), Guid.NewGuid().ToString("N"))).StatusCode);

        var pending = await CreateFamily(second);
        var secondChoice = second.Choices[0];
        var familyReview = await ReadBoundaryJson(await second.Client.GetAsync(GuardianReviewPath(secondChoice.SportEnrollmentId)));
        Assert.Equal("Family", familyReview.GetProperty("openRenewal").GetProperty("kind").GetString());
        Assert.Equal(pending.GetProperty("id").GetGuid(), familyReview.GetProperty("openRenewal").GetProperty("familyRequestId").GetGuid());
        Assert.Empty(familyReview.GetProperty("plans").EnumerateArray());
        var collision = await Post(second.Client, GuardianRenewalPath,
            new { sportEnrollmentId = secondChoice.SportEnrollmentId, subscriptionPlanId = secondChoice.SubscriptionPlanId },
            await Csrf(second.Client), Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        Assert.Equal(0, await Count<RenewalRequest>(r => r.SportEnrollmentId == secondChoice.SportEnrollmentId));
        await LinkAdditionalGuardian(family, secondChoice.SportEnrollmentId);
        var hiddenFamily = await ReadBoundaryJson(await family.Client.GetAsync(GuardianReviewPath(secondChoice.SportEnrollmentId)));
        Assert.Equal("Family", hiddenFamily.GetProperty("openRenewal").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, hiddenFamily.GetProperty("openRenewal").GetProperty("familyRequestId").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await family.Client.GetAsync($"/api/v1/guardian/subscriptions/family/requests/{pending.GetProperty("id").GetGuid()}")).StatusCode);
        var reference = await ReadBoundaryJson(await Post(owner,
            $"/api/v1/subscriptions/beneficiary-references/{secondChoice.SportEnrollmentId}/regenerate", new { }, await Csrf(owner)));
        var referenceBody = new { reference = reference.GetProperty("reference").GetString() };
        var ownExternal = await ReadBoundaryJson(await Post(second.Client,
            "/api/v1/guardian/subscriptions/external/resolve", referenceBody, await Csrf(second.Client)));
        Assert.Equal("Family", ownExternal.GetProperty("openRenewal").GetProperty("kind").GetString());
        Assert.Equal(pending.GetProperty("id").GetGuid(), ownExternal.GetProperty("openRenewal").GetProperty("familyRequestId").GetGuid());
        var hiddenExternal = await ReadBoundaryJson(await Post(family.Client,
            "/api/v1/guardian/subscriptions/external/resolve", referenceBody, await Csrf(family.Client)));
        Assert.Equal("Unavailable", hiddenExternal.GetProperty("openRenewal").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, hiddenExternal.GetProperty("openRenewal").GetProperty("familyRequestId").ValueKind);
        var external = await Post(family.Client, "/api/v1/guardian/subscriptions/external/renewals",
            new { reference = reference.GetProperty("reference").GetString(), subscriptionPlanId = secondChoice.SubscriptionPlanId },
            await Csrf(family.Client), Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Conflict, external.StatusCode);
        Assert.False((await external.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("paymentId", out _));
        Assert.Equal(0, await Count<RenewalRequest>(r => r.SportEnrollmentId == secondChoice.SportEnrollmentId));
    }

    [Fact]
    public async Task Guardian_open_renewal_blocks_retry_and_external_payment_and_rechecks_a_revoked_link()
    {
        await using var family = await NewFamily();
        await using var gateway = await InternalGatewayRouteScope.Begin(factory.Services);
        using var owner = factory.CreateClient();
        await StaffLogin(owner);
        var choice = family.Choices[0];
        var enrollment = choice.SportEnrollmentId;
        var body = new { sportEnrollmentId = enrollment, subscriptionPlanId = choice.SubscriptionPlanId };
        var token = await Csrf(family.Client);
        var oldKey = Guid.NewGuid().ToString("N");
        var prior = await ReadBoundaryJson(await Post(family.Client, GuardianRenewalPath, body, token, oldKey));
        var oldPayment = prior.GetProperty("paymentId").GetGuid();
        (await Post(family.Client, $"/api/v1/guardian/subscriptions/payments/{oldPayment}/simulate", new { outcome = "Cancelled" }, token)).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await ReadBoundaryJson(await family.Client.GetAsync(GuardianReviewPath(enrollment)))).GetProperty("openRenewal").ValueKind);
        var newKey = Guid.NewGuid().ToString("N");
        var current = await ReadBoundaryJson(await Post(family.Client, GuardianRenewalPath, body, token, newKey));
        Assert.NotEqual(oldPayment, current.GetProperty("paymentId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(family.Client, $"/api/v1/guardian/subscriptions/payments/{oldPayment}/retry", new { }, token, Guid.NewGuid().ToString("N"))).StatusCode);
        var reference = await ReadBoundaryJson(await Post(owner, $"/api/v1/subscriptions/beneficiary-references/{enrollment}/regenerate", new { }, await Csrf(owner)));
        var external = await Post(family.Client, "/api/v1/guardian/subscriptions/external/renewals",
            new { reference = reference.GetProperty("reference").GetString(), subscriptionPlanId = choice.SubscriptionPlanId },
            token, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Conflict, external.StatusCode);
        Assert.False((await external.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("paymentId", out _));
        Assert.Equal(oldPayment, (await ReadBoundaryJson(await Post(family.Client, GuardianRenewalPath, body, token, oldKey))).GetProperty("paymentId").GetGuid());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var player = await db.SportEnrollments.Where(e => e.Id == enrollment).Select(e => e.PlayerId).SingleAsync();
        var link = await db.GuardianPlayerLinks.SingleAsync(l => l.GuardianId == family.Guardian && l.PlayerId == player);
        link.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await family.Client.GetAsync(GuardianReviewPath(enrollment))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(family.Client, GuardianRenewalPath, body, token, newKey)).StatusCode);
        Assert.Equal(2, await Count<RenewalRequest>(r => r.SportEnrollmentId == enrollment));
        Assert.Equal(0, await Count<PaymentCollection>(c => c.SportEnrollmentId == enrollment));
    }

    private async Task LinkAdditionalGuardian(FamilyFixture guardian, Guid enrollment)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var player = await db.SportEnrollments.Where(e => e.Id == enrollment).Select(e => e.PlayerId).SingleAsync();
        var now = DateTimeOffset.UtcNow;
        db.GuardianPlayerLinks.Add(new GuardianPlayerLink
        {
            Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, GuardianId = guardian.Guardian, PlayerId = player,
            CreatedByUserId = guardian.User, CreatedAtUtc = now, UpdatedAtUtc = now,
        });
        await db.SaveChangesAsync();
    }
}
