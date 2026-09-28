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

        return await db.AcademyMemberships.AsNoTracking()
            .Where(x => x.UserId == userId && x.AcademyId == academyId && x.IsActive && x.Academy.IsActive)
            .Select(x => new TenantMembership(x.AcademyId, x.Academy.ArabicName, x.Role))
            .SingleOrDefaultAsync(cancellationToken);
    }

}
