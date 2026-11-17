using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Academy.Api.Auth;
using Academy.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Academy.Api.Mobile;

public sealed class MobileOtpOptions
{
    public string Provider { get; init; } = "Disabled";
    public string AllowedPhone { get; init; } = "";
    public TwilioVerifyOptions Twilio { get; init; } = new();
    public string StagingCode { get; init; } = "";
    public StagingOtpAccount[] StagingAccounts { get; init; } = [];
    public bool IsStagingFixed => Provider == "StagingFixed";

    public bool IsValid(bool isStaging) => Provider switch
    {
        "Disabled" => true,
        "TwilioVerify" =>
         EgyptPhoneNormalizer.Normalize(AllowedPhone) is { } phone && phone == AllowedPhone &&
         TwilioMobileOtp.IsSid(Twilio.ApiKeySid, "SK") &&
         TwilioMobileOtp.IsSid(Twilio.ServiceSid, "VA") &&
         !string.IsNullOrWhiteSpace(Twilio.ApiKeySecret),
        "StagingFixed" => isStaging && StagingCode.Length == 6 && StagingCode.All(char.IsAsciiDigit) &&
            StagingAccounts.Length > 0 && StagingAccounts.All(x => x.IsValid()) &&
            StagingAccounts.Select(x => x.Phone).Distinct().Count() == StagingAccounts.Length,
        _ => false
    };

    internal bool MatchesSession(MobileSession session, out StagingOtpAccount? stagingAccount)
    {
        stagingAccount = null;
        if (session.SecurityStamp == session.User.SecurityStamp) return true;
        if (!IsStagingFixed || !StagingOtpAccount.IsFixture(session.User)) return false;
        stagingAccount = StagingAccounts.FirstOrDefault(x => x.UserId == session.UserId &&
            x.MembershipId == session.MembershipId && x.Stamp(session.User) == session.SecurityStamp);
        return stagingAccount is not null;
    }
}

public sealed class StagingOtpAccount
{
    public string Phone { get; init; } = "";
    public Guid UserId { get; init; }
    public Guid AcademyId { get; init; }
    public Guid MembershipId { get; init; }

    internal bool IsValid() =>
        Phone.Length > 0 && EgyptPhoneNormalizer.Normalize(Phone) == Phone &&
        UserId != Guid.Empty && AcademyId != Guid.Empty && MembershipId != Guid.Empty;

    internal static bool IsFixture(ApplicationUser user) =>
        user.Email?.EndsWith("@test.iacademy.invalid", StringComparison.OrdinalIgnoreCase) == true;

    // A separately scoped stamp invalidates these sessions if the mode is switched off
    // or the configured test account changes, without changing the user's Identity stamp.
    internal string Stamp(ApplicationUser user) =>
        "staging:" + MobileAuthenticationHandler.Hash($"{Phone}:{AcademyId}:{MembershipId}:{user.SecurityStamp}");
}

public sealed class TwilioVerifyOptions
{
    public string ApiKeySid { get; init; } = "";
    public string ApiKeySecret { get; init; } = "";
    public string ServiceSid { get; init; } = "";
}

public enum MobileOtpCheck { Unavailable, Invalid, Approved }

public sealed class TwilioMobileOtp(IHttpClientFactory clients, IOptions<MobileOtpOptions> options)
{
    public const string ClientName = "mobile-otp-twilio";
    public bool Enabled => options.Value.Provider == "TwilioVerify";
    public bool Allows(string phone) => Enabled && options.Value.AllowedPhone == phone;

    public async Task<string?> StartAsync(string phone, CancellationToken ct)
    {
        var result = await SendAsync("Verifications", new Dictionary<string, string>
        {
            ["To"] = phone, ["Channel"] = "sms", ["Locale"] = "ar"
        }, ct);
        return result.Body is { Status: "pending", Sid: { } sid } && IsSid(sid, "VE") ? sid : null;
    }

    public async Task<MobileOtpCheck> CheckAsync(string sid, string code, CancellationToken ct)
    {
        var result = await SendAsync("VerificationCheck", new Dictionary<string, string>
        {
            ["VerificationSid"] = sid, ["Code"] = code
        }, ct);
        if (result.Status == HttpStatusCode.NotFound) return MobileOtpCheck.Invalid;
        if (result.Body?.Sid != sid) return MobileOtpCheck.Unavailable;
        return result.Body.Status == "approved" ? MobileOtpCheck.Approved : MobileOtpCheck.Invalid;
    }

    internal static bool IsSid(string? sid, string prefix) =>
        sid is { Length: 34 } && sid.StartsWith(prefix, StringComparison.Ordinal) && sid.Skip(2).All(char.IsAsciiHexDigit);

    private async Task<(HttpStatusCode? Status, VerifyBody? Body)> SendAsync(
        string action, Dictionary<string, string> fields, CancellationToken ct)
    {
        var settings = options.Value.Twilio;
        using var client = clients.CreateClient(ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"v2/Services/{settings.ServiceSid}/{action}")
        {
            Content = new FormUrlEncodedContent(fields)
        };
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiKeySid}:{settings.ApiKeySecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credential);
        try
        {
            using var response = await client.SendAsync(request, ct);
            return (response.StatusCode, response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<VerifyBody>(cancellationToken: ct) : null);
        }
        catch (Exception e) when (e is HttpRequestException or System.Text.Json.JsonException ||
                                  (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return (null, null);
        }
    }

    private sealed record VerifyBody(string? Sid, string? Status);
}
