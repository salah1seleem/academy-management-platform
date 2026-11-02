using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Microsoft.AspNetCore.DataProtection;

namespace Academy.Api.Slice4;

// A short-lived intent bound to the authenticated account, selected role and
// login session. It is not an authorization grant; every save still authorizes.
public sealed class AttendanceDraftGuard(IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("Academy.AttendanceDraft.v1");
    private sealed record Lease(string Scope, Guid Session, DateTimeOffset Expires);
    public static string? Scope(TenantMembership tenant, ClaimsPrincipal principal)
    {
        var login = principal.FindFirstValue("mobile_session_id") ?? principal.FindFirstValue("attendance_session");
        if (string.IsNullOrEmpty(login)) return null;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{principal.FindFirstValue(ClaimTypes.NameIdentifier)}:{tenant.AcademyId}:{tenant.MembershipId}:{tenant.Role}:{login}")));
    }
    public (string? Token, string? Scope, DateTimeOffset? Expires) Issue(TenantMembership tenant, ClaimsPrincipal principal, Guid session)
    {
        var scope = Scope(tenant, principal);
        if (scope is null) return (null, null, null);
        var expires = clock.GetUtcNow().AddMinutes(15);
        return (protector.Protect(JsonSerializer.Serialize(new Lease(scope, session, expires))), scope, expires);
    }
    public bool Valid(TenantMembership tenant, ClaimsPrincipal principal, Guid session, string? token)
    {
        // Older online clients remain supported. Queue clients always send the lease.
        if (token is null) return true;
        if (token.Length > 4096) return false;
        try
        {
            var lease = JsonSerializer.Deserialize<Lease>(protector.Unprotect(token));
            return lease is not null && lease.Session == session && lease.Expires > clock.GetUtcNow() && lease.Scope == Scope(tenant, principal);
        }
        catch (Exception error) when (error is CryptographicException or JsonException or ArgumentException) { return false; }
    }
}
