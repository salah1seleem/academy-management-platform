using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Academy.Api.Auth;
using Academy.Api.Mobile;
using Academy.Api.Slice2;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class MobileAuthenticationTests : IAsyncLifetime
{
    private readonly string connection = Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")
        ?? throw new InvalidOperationException("ACADEMY_TEST_CONNECTION_STRING is required.");
    private MobileFactory factory = null!;
    private HttpClient client = null!;
    private readonly Guid userId = Guid.NewGuid();
    private Guid? createdBranchId;
    private readonly string phone = $"+2011{RandomNumberGenerator.GetInt32(10000000, 99999999)}";
    private const string Root = "/api/v1/mobile/auth";

    public async Task InitializeAsync()
    {
        factory = new MobileFactory(connection);
        client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var result = await users.CreateAsync(new ApplicationUser { Id = userId, UserName = phone, PhoneNumber = phone,
            DisplayName = "حساب اختبار الموبايل", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow });
        Assert.True(result.Succeeded);
    }
    public async Task DisposeAsync()
    {
        await Mutate(async db => {
            await db.MobileSessions.Where(x => x.UserId == userId).ExecuteDeleteAsync();
            await db.MobileOtpChallenges.Where(x => x.PhoneNumberNormalized == phone).ExecuteDeleteAsync();
            await db.AcademyMemberships.Where(x => x.UserId == userId).ExecuteDeleteAsync();
            await db.Users.Where(x => x.Id == userId).ExecuteDeleteAsync();
            if (createdBranchId is Guid branchId) await db.Branches.Where(x => x.Id == branchId).ExecuteDeleteAsync();
        });
        client.Dispose(); await factory.DisposeAsync();
    }
    private async Task<Guid> AddRole(AcademyRole role, Guid? academy = null)
    {
        var id = Guid.NewGuid();
        await Mutate(db => { db.AcademyMemberships.Add(new AcademyMembership { Id = id, UserId = userId,
            AcademyId = academy ?? DemoSeed.NogoomAcademyId, Role = role, IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow }); return Task.CompletedTask; });
        return id;
    }
    private async Task Mutate(Func<FoundationDbContext, Task> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await action(db); await db.SaveChangesAsync();
    }
    private async Task<Guid> Challenge()
    {
        var response = await client.PostAsJsonAsync(Root + "/otp/request", new { phoneNumber = phone });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ChallengeBody>())!.ChallengeId;
    }
    private Task<HttpResponseMessage> Verify(Guid id, string code = "246810") => client.PostAsJsonAsync(Root + "/otp/verify",
        new { challengeId = id, phoneNumber = phone, code, deviceName = "Integration test" });
    private async Task<MobileCredentials> Login()
    {
        var response = await Verify(await Challenge()); response.EnsureSuccessStatusCode();
        var credentials = (await response.Content.ReadFromJsonAsync<MobileCredentials>())!;
        Bearer(credentials.AccessToken); return credentials;
    }
    private void Bearer(string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task Legacy_payment_grant_changes_the_active_mobile_membership_without_rotating_credentials()
    {
        var member = await AddRole(AcademyRole.AcademyAdmin);
        var session = await Login();
        var initial = Assert.Single(session.Memberships).ContextRevision;
        using var owner = factory.CreateClient();
        var csrf = await owner.GetFromJsonAsync<CsrfBody>("/api/v1/auth/csrf");
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email = DemoSeed.OwnerEmail, password = "Demo-Only-123!" })
        };
        login.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
        (await owner.SendAsync(login)).EnsureSuccessStatusCode();

        async Task Save(bool allowed)
        {
            var token = await owner.GetFromJsonAsync<CsrfBody>("/api/v1/auth/csrf");
            using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/manage/staff/{member}/grants")
            {
                Content = JsonContent.Create(new { grants = new[] { new { permission = AcademyPermissions.PaymentRead, allowed } } })
            };
            request.Headers.Add("X-CSRF-TOKEN", token!.Token);
            Assert.Equal(HttpStatusCode.NoContent, (await owner.SendAsync(request)).StatusCode);
        }

        async Task<int> ReadRevision() => Assert.Single((await client.GetFromJsonAsync<MobileMembership[]>(Root + "/memberships"))!).ContextRevision;
        const string queue = "/api/v1/manage/payment-requests?allDates=true&status=Pending";
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(queue)).StatusCode);
            await Save(false);
            Assert.Equal(initial + 1, await ReadRevision());
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(queue)).StatusCode);
            await Save(false);
            Assert.Equal(initial + 1, await ReadRevision());
            await Save(true);
            Assert.Equal(initial + 2, await ReadRevision());
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(queue)).StatusCode);
            await Mutate(async db =>
            {
                var stored = await db.MobileSessions.SingleAsync(x => x.UserId == userId);
                Assert.Equal(MobileAuthenticationHandler.Hash(session.AccessToken), stored.AccessTokenHash);
                Assert.Equal(MobileAuthenticationHandler.Hash(session.RefreshToken), stored.RefreshTokenHash);
            });
        }
        finally
        {
            await Mutate(async db => { await db.StaffGrants.Where(x => x.MembershipId == member).ExecuteDeleteAsync(); });
        }
    }

    [Theory]
    [InlineData(AcademyRole.Guardian)] [InlineData(AcademyRole.Coach)] [InlineData(AcademyRole.AcademyOwner)] [InlineData(AcademyRole.AcademyAdmin)]
    public async Task Each_supported_role_can_verify_phone_and_access_trusted_tenant(AcademyRole role)
    {
        var member = await AddRole(role); var credentials = await Login();
        Assert.Equal(member, credentials.MembershipId);
        Assert.Equal(role.ToString(), Assert.Single(credentials.Memberships).Role);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/tenant/probe/{DemoSeed.FutureAcademyId}")).StatusCode);
        await Mutate(async db => {
            var session = await db.MobileSessions.SingleAsync(x => x.UserId == userId);
            Assert.Equal(MobileAuthenticationHandler.Hash(credentials.AccessToken), session.AccessTokenHash);
            Assert.Equal(MobileAuthenticationHandler.Hash(credentials.RefreshToken), session.RefreshTokenHash);
            Assert.NotEqual(credentials.RefreshToken, session.RefreshTokenHash);
        });
    }

    [Fact]
    public async Task Unknown_phone_and_account_without_active_membership_are_rejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "/otp/request", new { phoneNumber = phone })).StatusCode);
        var member = await AddRole(AcademyRole.AcademyAdmin);
        await Mutate(async db => (await db.AcademyMemberships.SingleAsync(x => x.Id == member)).IsActive = false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "/otp/request", new { phoneNumber = phone })).StatusCode);
    }

    [Fact]
    public async Task Native_administrator_keeps_operational_permissions_without_owner_financial_privileges()
    {
        await AddRole(AcademyRole.AcademyAdmin); await Login();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/attendance/sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/manage/payment-requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/reports/owner-summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/reports/financial")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/manage/academy/policy-audit")).StatusCode);
    }

    [Fact]
    public async Task Wrong_otp_has_five_attempt_limit_and_cannot_then_be_redeemed()
    {
        await AddRole(AcademyRole.Guardian); var id = await Challenge();
        for (var attempt = 0; attempt < 5; attempt++) Assert.Equal(HttpStatusCode.Unauthorized, (await Verify(id, "000000")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Verify(id)).StatusCode);
    }

    [Fact]
    public async Task Expired_otp_is_rejected()
    {
        await AddRole(AcademyRole.Guardian); var id = await Challenge();
        await Mutate(async db => (await db.MobileOtpChallenges.SingleAsync(x => x.Id == id)).ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Verify(id)).StatusCode);
    }

    [Fact]
    public async Task Otp_is_one_time_even_for_concurrent_verification()
    {
        await AddRole(AcademyRole.Guardian); var id = await Challenge();
        var responses = await Task.WhenAll(Verify(id), Verify(id));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Resend_invalidates_old_challenge_and_phone_request_limit_is_enforced()
    {
        await AddRole(AcademyRole.Guardian); var old = await Challenge(); await Challenge(); await Challenge();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Verify(old)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync(Root + "/otp/request", new { phoneNumber = phone })).StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_both_credentials_and_old_tokens_no_longer_work()
    {
        await AddRole(AcademyRole.Guardian); var old = await Login();
        var response = await client.PostAsJsonAsync(Root + "/refresh", new { refreshToken = old.RefreshToken });
        response.EnsureSuccessStatusCode(); var fresh = (await response.Content.ReadFromJsonAsync<MobileCredentials>())!;
        Assert.NotEqual(old.RefreshToken, fresh.RefreshToken); Assert.NotEqual(old.AccessToken, fresh.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "/refresh", new { refreshToken = old.RefreshToken })).StatusCode);
        Bearer(fresh.AccessToken); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_refresh_has_exactly_one_winner()
    {
        await AddRole(AcademyRole.Guardian); var old = await Login();
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(Root + "/refresh", new { refreshToken = old.RefreshToken })));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Expired_access_can_refresh_but_expired_session_cannot()
    {
        await AddRole(AcademyRole.Guardian); var old = await Login();
        await Mutate(async db => (await db.MobileSessions.SingleAsync(x => x.UserId == userId)).AccessExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
        var response = await client.PostAsJsonAsync(Root + "/refresh", new { refreshToken = old.RefreshToken }); response.EnsureSuccessStatusCode();
        var fresh = (await response.Content.ReadFromJsonAsync<MobileCredentials>())!;
        await Mutate(async db => (await db.MobileSessions.SingleAsync(x => x.UserId == userId)).ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1));
        Bearer(fresh.AccessToken); Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "/refresh", new { refreshToken = fresh.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_current_device_but_not_other_device()
    {
        await AddRole(AcademyRole.Guardian); var first = await Login(); var second = await Login();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(Root + "/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "/refresh", new { refreshToken = second.RefreshToken })).StatusCode);
        Bearer(first.AccessToken); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Revoke_all_devices_prevents_access_and_refresh()
    {
        await AddRole(AcademyRole.Guardian); var first = await Login(); var second = await Login();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(Root + "/revoke-all", null)).StatusCode);
        foreach (var credential in new[] { first, second })
        {
            Bearer(credential.AccessToken); Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "/refresh", new { refreshToken = credential.RefreshToken })).StatusCode);
        }
    }

    [Theory]
    [InlineData("membership")] [InlineData("user")] [InlineData("stamp")]
    public async Task Disabled_membership_user_or_security_stamp_change_prevents_access_and_refresh(string kind)
    {
        var member = await AddRole(AcademyRole.Guardian); var credential = await Login();
        await Mutate(async db => {
            if (kind == "membership") (await db.AcademyMemberships.SingleAsync(x => x.Id == member)).IsActive = false;
            else if (kind == "user") (await db.Users.SingleAsync(x => x.Id == userId)).IsActive = false;
            else (await db.Users.SingleAsync(x => x.Id == userId)).SecurityStamp = Guid.NewGuid().ToString();
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "/refresh", new { refreshToken = credential.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task Guardian_is_resource_scoped_and_cannot_use_coach_or_staff_endpoints()
    {
        await AddRole(AcademyRole.Guardian); await Login();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/coach/groups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/people/players")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/guardian/children/{Slice2DemoSeed.OmarPlayerId}")).StatusCode);
    }

    [Fact]
    public async Task Coach_has_no_owner_access_and_only_assigned_groups()
    {
        await AddRole(AcademyRole.Coach); await Login();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/reports/owner-summary")).StatusCode);
        var groups = await client.GetFromJsonAsync<object[]>("/api/v1/coach/groups"); Assert.Empty(groups!);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/guardian/children")).StatusCode);
    }

    [Fact]
    public async Task Multiple_roles_require_explicit_selection_and_switch_invalidates_old_credentials()
    {
        var guardian = await AddRole(AcademyRole.Guardian); var owner = await AddRole(AcademyRole.AcademyOwner);
        var credential = await Login(); Assert.Null(credential.MembershipId); Assert.Equal(2, credential.Memberships.Count);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Root + "/select-role", new { membershipId = Guid.NewGuid() })).StatusCode);
        Guid otherMember = Guid.Empty;
        await Mutate(async db => otherMember = await db.AcademyMemberships.Where(x => x.User.Email == DemoSeed.OwnerEmail && x.AcademyId == DemoSeed.NogoomAcademyId && x.Role == AcademyRole.AcademyOwner).Select(x => x.Id).SingleAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Root + "/select-role", new { membershipId = otherMember })).StatusCode);
        var response = await client.PostAsJsonAsync(Root + "/select-role", new { membershipId = guardian }); response.EnsureSuccessStatusCode();
        var selected = (await response.Content.ReadFromJsonAsync<MobileCredentials>())!; Assert.Equal(guardian, selected.MembershipId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "/refresh", new { refreshToken = credential.RefreshToken })).StatusCode);
        Bearer(selected.AccessToken); Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/reports/owner-summary")).StatusCode);
        var switched = await client.PostAsJsonAsync(Root + "/select-role", new { membershipId = owner }); switched.EnsureSuccessStatusCode();
        var ownerCredentials = (await switched.Content.ReadFromJsonAsync<MobileCredentials>())!;
        Assert.Equal(owner, ownerCredentials.MembershipId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
        Bearer(ownerCredentials.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/reports/owner-summary")).StatusCode);
    }

    [Fact]
    public async Task Multiple_academies_require_selection_and_client_header_cannot_change_tenant()
    {
        await AddRole(AcademyRole.Coach); var second = await AddRole(AcademyRole.Coach, DemoSeed.FutureAcademyId);
        var credentials = await Login(); Assert.Null(credentials.MembershipId);
        var selected = await client.PostAsJsonAsync(Root + "/select-role", new { membershipId = second }); selected.EnsureSuccessStatusCode();
        Bearer((await selected.Content.ReadFromJsonAsync<MobileCredentials>())!.AccessToken);
        client.DefaultRequestHeaders.Add("X-Academy-Id", DemoSeed.NogoomAcademyId.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/tenant/probe/{DemoSeed.FutureAcademyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/tenant/probe/{DemoSeed.NogoomAcademyId}")).StatusCode);
    }

    [Fact]
    public async Task Authenticated_bearer_mutation_needs_no_cookie_csrf_and_cannot_mint_web_cookie()
    {
        await AddRole(AcademyRole.AcademyOwner); await Login();
        var result = await client.PostAsJsonAsync("/api/v1/manage/branches", new { arabicName = "فرع اختبار " + Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        createdBranchId = await result.Content.ReadFromJsonAsync<Guid>();
        Assert.False(result.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/session/academy", new { academyId = DemoSeed.NogoomAcademyId })).StatusCode);
    }

    [Fact]
    public async Task Cookies_still_require_csrf_and_invalid_bearer_never_falls_back_to_valid_cookie()
    {
        var csrf = await client.GetFromJsonAsync<CsrfBody>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = DemoSeed.OwnerEmail, password = "Demo-Only-123!" }) };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.Token); (await client.SendAsync(request)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/manage/branches", new { arabicName = "محاولة دون حماية" })).StatusCode);
        Bearer(new string('x', 64)); Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/manage/branches", new { arabicName = "محاولة غير موثقة" })).StatusCode);
    }

    [Fact]
    public async Task Production_without_sms_returns_501_not_demo_credentials()
    {
        await using var production = new MobileFactory(connection, "Production"); using var external = production.CreateClient();
        Assert.Equal(HttpStatusCode.NotImplemented, (await external.PostAsJsonAsync(Root + "/otp/request", new { phoneNumber = phone })).StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented, (await external.PostAsJsonAsync(Root + "/otp/verify", new { phoneNumber = phone, challengeId = Guid.NewGuid(), code = "246810", deviceName = "Test" })).StatusCode);
    }

    [Fact]
    public async Task Verify_sms_is_limited_to_one_recipient_and_cannot_issue_two_sessions()
    {
        var member = await AddRole(AcademyRole.Guardian);
        using var twilio = new VerifyHandler(phone);
        await using var production = new MobileFactory(connection, "Production", twilio, phone);
        using var external = production.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await external.PostAsJsonAsync(Root + "/otp/request",
            new { phoneNumber = "+201000000001" })).StatusCode);
        var starts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            external.PostAsJsonAsync(Root + "/otp/request", new { phoneNumber = phone })));
        var accepted = Assert.Single(starts, x => x.StatusCode == HttpStatusCode.Accepted);
        Assert.Single(starts, x => x.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.Equal(1, twilio.Starts);
        var id = (await accepted.Content.ReadFromJsonAsync<ChallengeBody>())!.ChallengeId;
        Task<HttpResponseMessage> Check(string code) => external.PostAsJsonAsync(Root + "/otp/verify",
            new { challengeId = id, phoneNumber = phone, code, deviceName = "Verify test" });

        Assert.Equal(HttpStatusCode.Unauthorized, (await Check("246810")).StatusCode);
        var approvals = await Task.WhenAll(Check("483716"), Check("483716"));
        var success = Assert.Single(approvals, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(approvals, x => x.StatusCode == HttpStatusCode.Unauthorized);
        var credentials = (await success.Content.ReadFromJsonAsync<MobileCredentials>())!;
        Assert.Equal(member, credentials.MembershipId);
        external.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await external.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await external.GetAsync($"/api/v1/tenant/probe/{DemoSeed.FutureAcademyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Check("483716")).StatusCode);
    }

    [Fact]
    public async Task Failed_sms_send_does_not_create_a_redeemable_challenge_or_retry_automatically()
    {
        await AddRole(AcademyRole.Guardian);
        using var twilio = new VerifyHandler(phone, failSend: true);
        await using var production = new MobileFactory(connection, "Production", twilio, phone);
        using var external = production.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await external.PostAsJsonAsync(Root + "/otp/request",
            new { phoneNumber = phone })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await external.PostAsJsonAsync(Root + "/otp/request",
            new { phoneNumber = phone })).StatusCode);
        Assert.Equal(1, twilio.Starts);
        await Mutate(async db =>
        {
            var challenge = await db.MobileOtpChallenges.SingleAsync(x => x.PhoneNumberNormalized == phone);
            Assert.Null(challenge.ProviderVerificationSid);
            Assert.Equal(HttpStatusCode.Unauthorized, (await external.PostAsJsonAsync(Root + "/otp/verify",
                new { challengeId = challenge.Id, phoneNumber = phone, code = "483716", deviceName = "Verify test" })).StatusCode);
        });
    }

    [Fact]
    public async Task Disabled_academy_revokes_effective_access_and_refresh()
    {
        await AddRole(AcademyRole.Coach, DemoSeed.FutureAcademyId); var credentials = await Login();
        try
        {
            await Mutate(async db => (await db.Academies.SingleAsync(x => x.Id == DemoSeed.FutureAcademyId)).IsActive = false);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "/refresh", new { refreshToken = credentials.RefreshToken })).StatusCode);
        }
        finally { await Mutate(async db => (await db.Academies.SingleAsync(x => x.Id == DemoSeed.FutureAcademyId)).IsActive = true); }
    }

    [Fact]
    public async Task Duplicate_role_is_rejected_by_database_but_distinct_roles_are_allowed()
    {
        await AddRole(AcademyRole.Guardian); await AddRole(AcademyRole.Coach);
        await Assert.ThrowsAsync<DbUpdateException>(() => AddRole(AcademyRole.Coach));
    }

    [Fact]
    public async Task Ip_rate_limit_applies_to_unknown_phone_requests()
    {
        for (var i = 0; i < 30; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "/otp/request", new { phoneNumber = phone })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync(Root + "/otp/request", new { phoneNumber = phone })).StatusCode);
    }

    [Theory]
    [InlineData("01000000001")] [InlineData("+20 100 000 0001")] [InlineData("00201000000001")] [InlineData("٠١٠٠٠٠٠٠٠٠١")]
    public void Common_phone_formats_have_one_identity(string input) => Assert.Equal("+201000000001", EgyptPhoneNormalizer.Normalize(input));

    private sealed record ChallengeBody(Guid ChallengeId);
    private sealed record CsrfBody(string Token);
    private sealed class VerifyHandler(string phone, bool failSend = false) : HttpMessageHandler
    {
        private const string Sid = "VE11111111111111111111111111111111";
        private int starts;
        public int Starts => starts;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("verify.twilio.com", request.RequestUri!.Host);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            var body = await request.Content!.ReadAsStringAsync(ct);
            if (request.RequestUri.AbsolutePath.EndsWith("/Verifications", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref starts);
                Assert.Contains("To=" + Uri.EscapeDataString(phone), body);
                Assert.Contains("Channel=sms", body);
                Assert.Contains("Locale=ar", body);
                if (failSend) return new HttpResponseMessage(HttpStatusCode.BadGateway);
                return new HttpResponseMessage(HttpStatusCode.Created)
                { Content = JsonContent.Create(new { sid = Sid, status = "pending" }) };
            }
            Assert.EndsWith("/VerificationCheck", request.RequestUri.AbsolutePath);
            Assert.Contains("VerificationSid=" + Sid, body);
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(new { sid = Sid, status = body.Contains("Code=483716") ? "approved" : "pending" }) };
        }
    }

    private sealed class MobileFactory(string connection, string environment = "Demo",
        HttpMessageHandler? verifyHandler = null, string? allowedPhone = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment); builder.UseSetting("ConnectionStrings:Default", connection);
            builder.UseSetting("Demo:SeedProfile", "LegacyRegression"); builder.UseSetting("Demo:SeedEnabled", (environment == "Demo").ToString());
            builder.UseSetting("Demo:FixedOtpEnabled", (environment == "Demo").ToString());
            builder.UseSetting("Demo:FixedOtp", "246810"); builder.UseSetting("Demo:StaffPassword", "Demo-Only-123!");
            builder.UseSetting("Payments:InternalTest:Enabled", "false");
            if (verifyHandler is null) return;
            builder.UseSetting("MobileOtp:Provider", "TwilioVerify");
            builder.UseSetting("MobileOtp:AllowedPhone", allowedPhone);
            builder.UseSetting("MobileOtp:Twilio:ApiKeySid", "SK" + new string('1', 32));
            builder.UseSetting("MobileOtp:Twilio:ServiceSid", "VA" + new string('1', 32));
            builder.UseSetting("MobileOtp:Twilio:ApiKeySecret", "test-only");
            builder.ConfigureServices(services => services.AddHttpClient(TwilioMobileOtp.ClientName)
                .ConfigurePrimaryHttpMessageHandler(() => verifyHandler));
        }
    }
}
