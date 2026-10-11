using System.Net.Http.Headers;
using Academy.Api.Auth;
using Academy.Api.Mobile;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

internal static class MobileCoachTestSession
{
    public static async Task SignInAsync(HttpClient client, IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var user = await db.Users.SingleAsync(x => x.NormalizedEmail == DemoSeed.CoachEmail.ToUpperInvariant());
        var membership = await db.AcademyMemberships.SingleAsync(x => x.UserId == user.Id &&
            x.AcademyId == DemoSeed.NogoomAcademyId && x.Role == AcademyRole.Coach && x.IsActive);
        var accessToken = MobileAuthenticationHandler.NewToken();
        var now = DateTimeOffset.UtcNow;
        db.MobileSessions.Add(new MobileSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            MembershipId = membership.Id,
            AccessTokenHash = MobileAuthenticationHandler.Hash(accessToken),
            RefreshTokenHash = MobileAuthenticationHandler.Hash(MobileAuthenticationHandler.NewToken()),
            SecurityStamp = user.SecurityStamp ?? string.Empty,
            DeviceName = "Integration test",
            CreatedAtUtc = now,
            LastUsedAtUtc = now,
            AccessExpiresAtUtc = now.AddMinutes(10),
            ExpiresAtUtc = now.AddHours(1)
        });
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }
}
