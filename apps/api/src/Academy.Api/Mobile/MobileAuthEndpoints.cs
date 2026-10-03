using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Academy.Api.Mobile;

public static class MobileAuthEndpoints
{
    public static void MapMobileAuth(this WebApplication app)
    {
        var auth = app.MapGroup("/api/v1/mobile/auth").RequireRateLimiting("mobile-auth");
        auth.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.Headers.Pragma = "no-cache";
            return await next(context);
        });
        auth.MapPost("/otp/request", RequestOtp);
        auth.MapPost("/otp/verify", VerifyOtp);
        auth.MapPost("/refresh", Refresh);
        var session = auth.MapGroup("").RequireAuthorization("mobile-session");
        session.MapGet("/memberships", async (ClaimsPrincipal principal, FoundationDbContext db) =>
            Results.Ok(await Memberships(db, UserId(principal))));
        session.MapPost("/select-role", SelectRole);
        session.MapPost("/logout", async (ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock) =>
        {
            var id = SessionId(principal);
            await db.MobileSessions.Where(x => x.Id == id && x.UserId == UserId(principal))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, clock.GetUtcNow()));
            return Results.NoContent();
        });
        session.MapPost("/revoke-all", async (ClaimsPrincipal principal, FoundationDbContext db, TimeProvider clock) =>
        {
            var userId = UserId(principal);
            await db.MobileSessions.Where(x => x.UserId == userId && x.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, clock.GetUtcNow()));
            return Results.NoContent();
        });
    }

    private static bool DemoOtpAvailable(IHostEnvironment env, DemoOptions options)
    {
        DemoSeed.ValidateEnvironment(env, options);
        return env.IsEnvironment("Demo") && options.FixedOtpEnabled;
    }
    private static IResult Unavailable() => Results.Problem(statusCode: 501, title: "خدمة رسائل التحقق غير مفعلة");
    private static IResult Invalid() => Results.Problem(statusCode: 401, title: "تعذر التحقق من بيانات الدخول");
    private static Guid UserId(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static Guid SessionId(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue("mobile_session_id")!);
    private static IQueryable<AcademyMembership> Eligible(FoundationDbContext db, Guid user) =>
        db.AcademyMemberships.Where(x => x.UserId == user && x.IsActive && x.User.IsActive && x.Academy.IsActive &&
            (x.Role == AcademyRole.Guardian || x.Role == AcademyRole.Coach || x.Role == AcademyRole.AcademyOwner));
    private static Task<List<MobileMembership>> Memberships(FoundationDbContext db, Guid user) =>
        Eligible(db, user).OrderBy(x => x.Academy.ArabicName).ThenBy(x => x.Id)
            .Select(x => new MobileMembership(x.Id, x.AcademyId, x.Academy.ArabicName, x.Role.ToString())).ToListAsync();

    private static async Task<IResult> RequestOtp(MobileOtpRequest request, FoundationDbContext db,
        IOptions<DemoOptions> options, IHostEnvironment env, TimeProvider clock)
    {
        if (!DemoOtpAvailable(env, options.Value)) return Unavailable();
        var phone = EgyptPhoneNormalizer.Normalize(request.PhoneNumber);
        if (phone is null) return Results.Problem(statusCode: 400, title: "رقم الهاتف غير صالح");
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"mobile-otp:" + phone}, 0))");
        if (await db.MobileOtpChallenges.CountAsync(x => x.PhoneNumberNormalized == phone && x.CreatedAtUtc > now.AddMinutes(-15)) >= 3)
            return Results.Problem(statusCode: 429, title: "محاولات كثيرة، يرجى الانتظار قبل طلب رمز جديد");
        var user = await db.Users.SingleOrDefaultAsync(x => x.PhoneNumber == phone && x.IsActive);
        if (user is null || !await Eligible(db, user.Id).AnyAsync()) return Invalid();
        // Invalidate earlier challenges so only the latest SMS/OTP can be used.
        await db.MobileOtpChallenges.Where(x => x.PhoneNumberNormalized == phone && x.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAtUtc, now));
        var challenge = new MobileOtpChallenge { Id = Guid.NewGuid(), PhoneNumberNormalized = phone,
            CreatedAtUtc = now, ExpiresAtUtc = now.AddMinutes(5) };
        db.MobileOtpChallenges.Add(challenge);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Accepted(value: new { challengeId = challenge.Id, demo = true });
    }

    private static async Task<IResult> VerifyOtp(MobileOtpVerify request, FoundationDbContext db,
        IOptions<DemoOptions> options, IHostEnvironment env, TimeProvider clock)
    {
        if (!DemoOtpAvailable(env, options.Value)) return Unavailable();
        if (string.IsNullOrWhiteSpace(request.DeviceName) || request.DeviceName.Length > 100)
            return Results.Problem(statusCode: 400, title: "اسم الجهاز غير صالح");
        var phone = EgyptPhoneNormalizer.Normalize(request.PhoneNumber);
        if (phone is null) return Invalid();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"mobile-otp:" + phone}, 0))");
        var now = clock.GetUtcNow();
        var challenge = await db.MobileOtpChallenges.SingleOrDefaultAsync(x => x.Id == request.ChallengeId && x.PhoneNumberNormalized == phone);
        if (challenge is null || challenge.ConsumedAtUtc != null || challenge.ExpiresAtUtc <= now || challenge.FailedAttempts >= 5)
            return Invalid();
        if (request.Code != options.Value.FixedOtp)
        {
            challenge.FailedAttempts++;
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Invalid();
        }
        var user = await db.Users.SingleOrDefaultAsync(x => x.PhoneNumber == phone && x.IsActive);
        var memberships = user is null ? [] : await Memberships(db, user.Id);
        if (user is null || memberships.Count == 0) return Invalid();
        challenge.ConsumedAtUtc = now;
        user.PhoneNumberConfirmed = true;
        var session = new MobileSession { Id = Guid.NewGuid(), UserId = user.Id, User = user,
            MembershipId = memberships.Count == 1 ? memberships[0].Id : null,
            DeviceName = request.DeviceName.Trim(), SecurityStamp = user.SecurityStamp ?? "",
            AccessTokenHash = "", RefreshTokenHash = "", CreatedAtUtc = now, LastUsedAtUtc = now,
            ExpiresAtUtc = now.AddDays(30) };
        db.MobileSessions.Add(session);
        var result = Issue(session, memberships, now);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Ok(result);
    }

    private static async Task<IResult> Refresh(MobileRefresh request, FoundationDbContext db, TimeProvider clock)
    {
        if (request.RefreshToken is null || request.RefreshToken.Length != 64) return Invalid();
        var hash = MobileAuthenticationHandler.Hash(request.RefreshToken);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"mobile-refresh:" + hash}, 0))");
        var session = await db.MobileSessions.Include(x => x.User).SingleOrDefaultAsync(x => x.RefreshTokenHash == hash);
        var now = clock.GetUtcNow();
        if (session is null || session.RevokedAtUtc != null || session.ExpiresAtUtc <= now || !session.User.IsActive ||
            session.SecurityStamp != session.User.SecurityStamp) return Invalid();
        // Serialize role switch / refresh / logout as well as same-token refreshes.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"mobile-session:" + session.Id}, 0))");
        await db.Entry(session).ReloadAsync();
        if (session.RefreshTokenHash != hash || session.RevokedAtUtc != null) return Invalid();
        var memberships = await Memberships(db, session.UserId);
        if (memberships.Count == 0 || (session.MembershipId is Guid selected && !memberships.Any(x => x.Id == selected)))
        {
            session.RevokedAtUtc = now;
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Invalid();
        }
        var result = Issue(session, memberships, now);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Ok(result);
    }

    private static async Task<IResult> SelectRole(MobileRoleSelection request, ClaimsPrincipal principal,
        FoundationDbContext db, TimeProvider clock)
    {
        var sessionId = SessionId(principal);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"mobile-session:" + sessionId}, 0))");
        var session = await db.MobileSessions.Include(x => x.User).SingleAsync(x => x.Id == sessionId);
        if (session.RevokedAtUtc != null || session.ExpiresAtUtc <= clock.GetUtcNow()) return Invalid();
        var memberships = await Memberships(db, UserId(principal));
        if (!memberships.Any(x => x.Id == request.MembershipId)) return Results.Forbid();
        session.MembershipId = request.MembershipId;
        var result = Issue(session, memberships, clock.GetUtcNow());
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Ok(result);
    }

    private static MobileCredentials Issue(MobileSession session, List<MobileMembership> memberships, DateTimeOffset now)
    {
        var access = MobileAuthenticationHandler.NewToken();
        var refresh = MobileAuthenticationHandler.NewToken();
        session.AccessTokenHash = MobileAuthenticationHandler.Hash(access);
        session.RefreshTokenHash = MobileAuthenticationHandler.Hash(refresh);
        session.AccessExpiresAtUtc = now.AddMinutes(10);
        session.LastUsedAtUtc = now;
        return new(access, refresh, session.AccessExpiresAtUtc, session.ExpiresAtUtc, session.MembershipId,
            session.User.DisplayName, memberships);
    }
}

public sealed record MobileOtpRequest(string PhoneNumber);
public sealed record MobileOtpVerify(Guid ChallengeId, string PhoneNumber, string Code, string DeviceName);
public sealed record MobileRefresh(string RefreshToken);
public sealed record MobileRoleSelection(Guid MembershipId);
public sealed record MobileMembership(Guid Id, Guid AcademyId, string AcademyName, string Role);
public sealed record MobileCredentials(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAtUtc,
    DateTimeOffset RefreshExpiresAtUtc, Guid? MembershipId, string DisplayName, List<MobileMembership> Memberships);
