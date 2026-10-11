using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Academy.Api.Auth;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Academy.Api.Platform;

public sealed record PlatformAccessRequirement : IAuthorizationRequirement;
public sealed class PlatformAccessHandler(FoundationDbContext db, IHostEnvironment environment, IOptions<DemoOptions> demo) : AuthorizationHandler<PlatformAccessRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PlatformAccessRequirement requirement)
    {
        if (context.User.Identity?.AuthenticationType != PlatformIdentity.Scheme || !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return;
        var mfa = context.User.FindFirstValue("platform_mfa");
        if (mfa != "totp" && !(mfa == "demo" && environment.IsEnvironment("Demo") && demo.Value.FixedOtpEnabled)) return;
        var stamp = context.User.FindFirstValue("platform_stamp");
        if (await db.PlatformAdministrators.AnyAsync(x => x.UserId == id && x.IsActive && db.Users.Any(u => u.Id == id && u.IsActive && u.SecurityStamp == stamp))) context.Succeed(requirement);
    }
}

public static class PlatformIdentity
{
    public const string Scheme = "Academy.Platform";
    public const string Policy = "platform.access";
    public const string DemoEmail = "platform.demo@example.test";
    public static readonly Guid DemoUserId = Guid.Parse("5b974028-0127-4404-9c08-49d8fbe71472");
    public static void MapPlatformIdentity(this WebApplication app)
    {
        app.MapGet("/api/v1/platform/auth/csrf", async (HttpContext context, IAntiforgery antiforgery) => {
            context.User = (await context.AuthenticateAsync(Scheme)).Principal ?? new ClaimsPrincipal(new ClaimsIdentity());
            context.Response.Headers.CacheControl = "private, no-store";
            return Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken });
        });
        app.MapPost("/api/v1/platform/auth/login", Login).RequireRateLimiting("mobile-auth")
            .AddEndpointFilter(async (context, next) => {
                context.HttpContext.User = (await context.HttpContext.AuthenticateAsync(Scheme)).Principal ?? new ClaimsPrincipal(new ClaimsIdentity());
                return await next(context);
            }).AddEndpointFilter<CsrfFilter>();
        app.MapPost("/api/v1/platform/auth/logout", async (HttpContext c) => { await c.SignOutAsync(Scheme); return Results.NoContent(); }).RequireAuthorization(Policy).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/platform/me", (ClaimsPrincipal p) => Results.Ok(new { name = p.FindFirstValue(ClaimTypes.Name), role = "PlatformSuperAdmin" })).RequireAuthorization(Policy);
    }
    private static IResult Invalid() => Results.Problem(statusCode: 401, title: "تعذر التحقق من دخول مشغّل المنصة");
    private static async Task<IResult> Login(PlatformLogin body, HttpContext context, FoundationDbContext db, UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn, TimeProvider clock, IHostEnvironment env, IOptions<DemoOptions> demo)
    {
        if (string.IsNullOrWhiteSpace(body.Email) || body.Email.Length > 256 || string.IsNullOrWhiteSpace(body.Password) || body.Password.Length > 1024 || body.Code is null || body.Code.Length != 6 || body.Code.Any(c => !char.IsAsciiDigit(c))) return Invalid();
        var user = await users.FindByEmailAsync(body.Email.Trim());
        if (user is null || !user.IsActive || !await db.PlatformAdministrators.AnyAsync(x => x.UserId == user.Id && x.IsActive)) return Invalid();
        if (!(await signIn.CheckPasswordSignInAsync(user, body.Password, lockoutOnFailure: true)).Succeeded) return Invalid();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"platform-mfa:" + user.Id}, 0))");
        var admin = await db.PlatformAdministrators.SingleAsync(x => x.UserId == user.Id); var now = clock.GetUtcNow();
        if (!admin.IsActive || admin.MfaLockedUntilUtc > now) return Invalid();
        var demoMfa = env.IsEnvironment("Demo") && demo.Value.FixedOtpEnabled && body.Code == demo.Value.FixedOtp;
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body.Code)));
        var repeated = await db.PlatformMfaUses.AnyAsync(x => x.UserId == user.Id && x.Fingerprint == fingerprint && x.AtUtc > now.AddMinutes(-2));
        var valid = demoMfa || user.TwoFactorEnabled && !repeated && await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, body.Code);
        if (!valid)
        {
            admin.FailedMfaAttempts++; if (admin.FailedMfaAttempts >= 5) { admin.MfaLockedUntilUtc = now.AddMinutes(15); admin.FailedMfaAttempts = 0; }
            await db.SaveChangesAsync(); await tx.CommitAsync(); return Invalid();
        }
        admin.FailedMfaAttempts = 0; admin.MfaLockedUntilUtc = null; admin.LastMfaFingerprint = fingerprint; admin.LastMfaAtUtc = now;
        if (!demoMfa) db.PlatformMfaUses.Add(new() { Id = Guid.NewGuid(), UserId = user.Id, Fingerprint = fingerprint, AtUtc = now });
        db.PlatformAudits.Add(new() { Id = Guid.NewGuid(), ActorUserId = user.Id, Action = "platform.login", Details = demoMfa ? "{\"authentication\":\"explicit-demo-only\"}" : "{\"authentication\":\"password-and-totp\"}", AtUtc = now });
        await db.SaveChangesAsync(); await tx.CommitAsync();
        await context.SignOutAsync(Scheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, user.DisplayName), new("platform_mfa", demoMfa ? "demo" : "totp"), new("platform_stamp", user.SecurityStamp ?? "")], Scheme));
        await context.SignInAsync(Scheme, principal, new AuthenticationProperties { IsPersistent = false, ExpiresUtc = now.AddMinutes(15), AllowRefresh = false });
        return Results.Ok(new { authenticated = true, demo = demoMfa });
    }
    public static async Task SeedDemo(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope(); var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>(); var options = scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;
        if (!env.IsEnvironment("Demo") || !options.SeedEnabled) return;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var user = await users.FindByIdAsync(DemoUserId.ToString());
        if (user is null)
        {
            if (await users.FindByEmailAsync(DemoEmail) is not null) throw new InvalidOperationException("Reserved platform demo identity collision. No existing user modified.");
            var now = DateTimeOffset.Parse("2026-09-28T06:00:00Z");
            user = new ApplicationUser { Id = DemoUserId, UserName = DemoEmail, Email = DemoEmail, DisplayName = "صلاح — مشغّل منصة تجريبي", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
            var result = await users.CreateAsync(user, options.StaffPassword); if (!result.Succeeded) throw new InvalidOperationException("Could not create synthetic platform identity");
            db.PlatformAdministrators.Add(new() { UserId = user.Id }); await db.SaveChangesAsync();
        }
        else if (user.Email != DemoEmail) throw new InvalidOperationException("Reserved platform demo identity collision");
        // Existing platform activity, credentials and grants are not re-enabled.
    }
}
public sealed record PlatformLogin(string Email, string Password, string Code);
