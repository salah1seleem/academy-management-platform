using System.Security.Claims;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Auth;

public sealed record TenantMembership(Guid AcademyId, string AcademyName, AcademyRole Role);

public sealed class CurrentTenant(IHttpContextAccessor accessor, FoundationDbContext db)
{
    public async Task<TenantMembership?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var principal = accessor.HttpContext?.User;
        var userIdText = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var academyIdText = principal?.FindFirstValue("academy_id");
        if (!Guid.TryParse(userIdText, out var userId) || !Guid.TryParse(academyIdText, out var academyId)) return null;

        var query = db.AcademyMemberships.AsNoTracking()
            .Where(x => x.UserId == userId && x.User.IsActive && x.AcademyId == academyId && x.IsActive && x.Academy.IsActive);
        var membershipText = principal?.FindFirstValue("membership_id");
        if (membershipText is not null)
        {
            if (!Guid.TryParse(membershipText, out var membershipId)) return null;
            query = query.Where(x => x.Id == membershipId);
        }
        // Legacy cookies without a selected role must not implicitly acquire another role.
        var memberships = await query
            .Select(x => new TenantMembership(x.AcademyId, x.Academy.ArabicName, x.Role))
            .Take(2).ToListAsync(cancellationToken);
        return memberships.Count == 1 ? memberships[0] : null;
    }

}
