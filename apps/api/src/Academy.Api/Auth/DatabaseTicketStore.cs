using System.Security.Claims;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Auth;

public sealed class DatabaseTicketStore(IServiceScopeFactory scopeFactory, TimeProvider timeProvider) : ITicketStore
{
    private static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(12);

    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, CancellationToken.None);

    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = timeProvider.GetUtcNow();
        var key = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        db.UserSessions.Add(new UserSession
        {
            Id = key,
            UserId = userId,
            ActiveAcademyId = Guid.TryParse(ticket.Principal.FindFirstValue("academy_id"), out var academyId) ? academyId : null,
            Ticket = TicketSerializer.Default.Serialize(ticket),
            CreatedAtUtc = now,
            LastSeenAtUtc = now,
            ExpiresAtUtc = ticket.Properties.ExpiresUtc ?? now.AddMinutes(30),
            AbsoluteExpiresAtUtc = now.Add(AbsoluteLifetime)
        });
        await db.SaveChangesAsync(cancellationToken);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);

    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var session = await db.UserSessions.SingleOrDefaultAsync(x => x.Id == key, cancellationToken);
        if (session is null || session.RevokedAtUtc is not null) return;
        var now = timeProvider.GetUtcNow();
        session.Ticket = TicketSerializer.Default.Serialize(ticket);
        session.ActiveAcademyId = Guid.TryParse(ticket.Principal.FindFirstValue("academy_id"), out var academyId) ? academyId : null;
        session.LastSeenAtUtc = now;
        session.ExpiresAtUtc = ticket.Properties.ExpiresUtc ?? now.AddMinutes(30);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var session = await db.UserSessions
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.Id == key, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (session is null || session.RevokedAtUtc is not null || !session.User.IsActive ||
            session.ExpiresAtUtc <= now || session.AbsoluteExpiresAtUtc <= now)
        {
            return null;
        }

        session.LastSeenAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return TicketSerializer.Default.Deserialize(session.Ticket);
    }

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var session = await db.UserSessions.SingleOrDefaultAsync(x => x.Id == key, cancellationToken);
        if (session is null) return;
        session.RevokedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }
}
