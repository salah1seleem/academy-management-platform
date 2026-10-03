using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Academy.Api.Mobile;

public sealed class MobileAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    FoundationDbContext db, TimeProvider clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Academy.Mobile";
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var token = header[7..].Trim();
        if (token.Length != 64) return AuthenticateResult.Fail("Invalid mobile credential");
        var hash = Hash(token);
        var now = clock.GetUtcNow();
        var session = await db.MobileSessions.AsNoTracking().Include(x => x.User)
            .Include(x => x.Membership!).ThenInclude(x => x.Academy)
            .SingleOrDefaultAsync(x => x.AccessTokenHash == hash, Context.RequestAborted);
        if (session is null || session.RevokedAtUtc != null || session.AccessExpiresAtUtc <= now ||
            session.ExpiresAtUtc <= now || !session.User.IsActive || session.SecurityStamp != session.User.SecurityStamp)
            return AuthenticateResult.Fail("Expired or revoked mobile session");
        if (session.MembershipId is not null && (session.Membership is null || !session.Membership.IsActive ||
            !session.Membership.Academy.IsActive || session.Membership.UserId != session.UserId ||
            session.Membership.Role is not (AcademyRole.Guardian or AcademyRole.Coach or AcademyRole.AcademyOwner)))
            return AuthenticateResult.Fail("Inactive mobile membership");
        var identity = new ClaimsIdentity(SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new(ClaimTypes.NameIdentifier, session.UserId.ToString()));
        identity.AddClaim(new(ClaimTypes.Name, session.User.UserName ?? session.UserId.ToString()));
        identity.AddClaim(new("mobile_session_id", session.Id.ToString()));
        if (session.Membership is { } member)
        {
            identity.AddClaim(new("academy_id", member.AcademyId.ToString()));
            identity.AddClaim(new("membership_id", member.Id.ToString()));
            identity.AddClaim(new(ClaimTypes.Role, member.Role.ToString()));
        }
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
