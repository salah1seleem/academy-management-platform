using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public sealed partial class DiscountRules
{
    public sealed record PricingLine(Guid EnrollmentId, Guid PlayerId, Guid BranchId, SubscriptionPlan Plan);
    // Same eligibility, rounding and winner as an individual quote, without an N+1 report.
    public async Task<Dictionary<Guid, RenewalFinancialSnapshot>> CalculateManyAsync(Guid academy, Guid actor, IReadOnlyCollection<PricingLine> lines, Guid? familyGuardianId = null)
    {
        if (lines.Count == 0) return [];
        var players = lines.Select(l => l.PlayerId).Distinct().ToArray();
        var rules = await db.DiscountRules.AsNoTracking().Where(r => r.AcademyId == academy && !r.IsSiblingOffer && r.IsActive).ToListAsync();
        var links = await db.PlayerDiscountLinks.AsNoTracking().Where(l => l.AcademyId == academy && l.IsActive && players.Contains(l.PlayerId)).Select(l => new { l.PlayerId, l.DiscountRuleId }).ToListAsync();
        var byPlayer = links.ToLookup(l => l.PlayerId, l => l.DiscountRuleId);
        var today = clock.Today;
        // Trusted family eligibility only; no other branch's child identity is returned.
        var paidPlayers = db.SubscriptionPeriods.IgnoreQueryFilters().Where(p => p.AcademyId == academy && p.IsActive
            && p.SportEnrollment.IsActive && p.SportEnrollment.Status == EnrollmentStatus.Active
            && p.Collection.IsActive && p.Collection.PaymentRequest.Status == PaymentRequestStatus.Confirmed
            && p.Status != SubscriptionPeriodStatus.Cancelled && p.Status != SubscriptionPeriodStatus.Frozen
            && p.StartDate <= today && (p.EndDate == null || p.EndDate >= today)
            && ((p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) == SubscriptionPlanType.Duration || p.RemainingSessions > 0)).Select(p => p.SportEnrollment.PlayerId);
        var siblingEligible = (await db.GuardianPlayerLinks.IgnoreQueryFilters().Where(l => l.AcademyId == academy && l.IsActive && players.Contains(l.PlayerId) && l.Guardian.IsActive
            && db.GuardianPlayerLinks.IgnoreQueryFilters().Any(s => s.AcademyId == academy && s.IsActive && s.GuardianId == l.GuardianId && s.PlayerId != l.PlayerId && s.Player.IsActive && paidPlayers.Contains(s.PlayerId)))
            .Select(l => l.PlayerId).Distinct().ToArrayAsync()).ToHashSet();
        // Endpoint authorization validates the staff's read grants and every selected branch.
        // Pricing additionally checks active same-academy membership; it never grants resource access.
        // Prospective family pricing only for a verified family's selected, distinct children.
        // Selecting two sports for one child is not a sibling discount; a supplied phone is irrelevant.
        if (familyGuardianId.HasValue && players.Length > 1)
        {
            var staff = await db.AcademyMemberships.AnyAsync(m => m.AcademyId == academy && m.UserId == actor && m.IsActive && (m.Role == AcademyRole.AcademyOwner || m.Role == AcademyRole.AcademyAdmin));
            var family = await db.GuardianPlayerLinks.IgnoreQueryFilters().Where(l => l.AcademyId == academy && l.GuardianId == familyGuardianId && l.IsActive && l.Guardian.IsActive && (l.Guardian.UserId == actor || staff) && l.Player.IsActive && players.Contains(l.PlayerId)).Select(l => l.PlayerId).Distinct().ToArrayAsync();
            if (family.Length == players.Length) siblingEligible.UnionWith(family);
        }
        var sibling = await db.DiscountRules.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(r => r.AcademyId == academy && r.IsSiblingOffer)
            ?? new DiscountRule { AcademyId = academy, ArabicName = "عرض الإخوة", Percentage = 20, IsSiblingOffer = true, Reason = "عرض الإخوة الافتراضي" };
        return lines.ToDictionary(l => l.EnrollmentId, l => Quote(rules.Where(r => byPlayer[l.PlayerId].Contains(r.Id))
            .Concat(siblingEligible.Contains(l.PlayerId) && sibling.IsActive ? [sibling] : []), l.Plan.Price, actor, l.Plan, l.BranchId));
    }
}
