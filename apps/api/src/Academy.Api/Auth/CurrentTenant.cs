using System.Security.Claims;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Auth;

public sealed record TenantMembership(Guid AcademyId, string AcademyName, AcademyRole Role, Guid MembershipId = default, bool AdditionalContext = false);

public sealed class CurrentTenant(IHttpContextAccessor accessor, FoundationDbContext db)
{
    public async Task<TenantMembership?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.FindFirstValue("web_staff_context_pending") == "true") return null;
        var userIdText = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var academyIdText = principal?.FindFirstValue("academy_id");
        if (!Guid.TryParse(userIdText, out var userId) || !Guid.TryParse(academyIdText, out var academyId)) return null;

        // Identity and parent activity are checked by exact user/academy/selected
        // IDs, not by the operational branch filter of a different context.
        var query = db.AcademyMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.UserId == userId && x.User.IsActive && x.AcademyId == academyId && x.IsActive && x.Academy.IsActive
                && (x.PrimaryMembershipId == null || x.PrimaryMembership!.IsActive && x.PrimaryMembership.PrimaryMembershipId == null && x.PrimaryMembership.Role == AcademyRole.AcademyAdmin));
        var membershipText = principal?.FindFirstValue("membership_id");
        if (membershipText is not null)
        {
            if (!Guid.TryParse(membershipText, out var membershipId)) return null;
            query = query.Where(x => x.Id == membershipId);
        }
        // Legacy cookies without a selected role must not implicitly acquire another role.
        var memberships = await query
            .Select(x => new TenantMembership(x.AcademyId, db.AcademyExperiences.Where(b => b.AcademyId == x.AcademyId).Select(b => b.DisplayName).FirstOrDefault() ?? x.Academy.ArabicName, x.Role, x.Id, x.PrimaryMembershipId != null))
            .Take(2).ToListAsync(cancellationToken);
        return memberships.Count == 1 ? memberships[0] : null;
    }

}
