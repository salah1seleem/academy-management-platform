using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public sealed class DiscountRules(FoundationDbContext db, ISubscriptionClock clock)
{
    public async Task<RenewalFinancialSnapshot> CalculateAsync(Guid academy, Guid playerId, decimal amount, Guid actor, Guid? guardianId = null, Guid? selectedRule = null, SubscriptionPlan? plan = null, Guid? branchId = null)
    {
        var rules = await db.DiscountRules.AsNoTracking().Where(x => x.AcademyId == academy && x.IsActive && !x.IsSiblingOffer && (x.Id == selectedRule || db.PlayerDiscountLinks.Any(l => l.AcademyId == academy && l.PlayerId == playerId && l.DiscountRuleId == x.Id && l.IsActive))).ToListAsync();
        // Shared guardian alone is insufficient: another child must have a current paid period.
        // A trusted child's linked family may span branches. Only eligibility is returned, never another branch's roster.
        var guardians = db.GuardianProfiles.IgnoreQueryFilters().Where(g => g.AcademyId == academy && g.IsActive && (g.Id == guardianId || db.GuardianPlayerLinks.IgnoreQueryFilters().Any(l => l.AcademyId == academy && l.PlayerId == playerId && l.IsActive && l.GuardianId == g.Id))).Select(g => g.Id);
        var siblings = db.GuardianPlayerLinks.IgnoreQueryFilters().Where(l => l.AcademyId == academy && l.PlayerId != playerId && l.IsActive && l.Player.IsActive && guardians.Contains(l.GuardianId)).Select(l => l.PlayerId);
        if (await db.SubscriptionPeriods.IgnoreQueryFilters().AnyAsync(p => p.AcademyId == academy && siblings.Contains(p.SportEnrollment.PlayerId) && p.SportEnrollment.IsActive && p.SportEnrollment.Status == Academy.Infrastructure.People.EnrollmentStatus.Active && p.IsActive && p.Collection.IsActive && p.Collection.PaymentRequest.Status == PaymentRequestStatus.Confirmed && p.Status != SubscriptionPeriodStatus.Cancelled && p.Status != SubscriptionPeriodStatus.Frozen && p.StartDate <= clock.Today && (p.EndDate == null || p.EndDate >= clock.Today) && ((p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) == SubscriptionPlanType.Duration || p.RemainingSessions > 0)))
        {
            var sibling = await db.DiscountRules.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == academy && x.IsSiblingOffer);
            if (sibling is null) rules.Add(new DiscountRule { AcademyId = academy, ArabicName = "عرض الإخوة", Percentage = 20, IsSiblingOffer = true, Reason = "عرض الإخوة الافتراضي" });
            else if (sibling.IsActive) rules.Add(sibling);
        }
        var best = DiscountPricing.Best(rules.Where(r => DiscountPricing.Eligible(r, clock.Today, branchId, plan)), amount);
        var discount = best is null ? 0 : DiscountPricing.Amount(best, amount);
        var reason = best is null ? null : string.IsNullOrWhiteSpace(best.Reason) ? $"تطبيق العرض المستحق: {best.ArabicName}" : best.Reason;
        return new(amount, best?.Type, best is null ? null : best.Type == RenewalDiscountType.Percentage ? best.Percentage : best.FixedAmount,
            discount, amount - discount, reason, best is null ? null : actor, best is null ? null : clock.UtcNow,
            best is null || best.Id == Guid.Empty ? null : best.Id, best is null || best.Id == Guid.Empty ? null : best.Revision);
    }
}

public static class DiscountRuleEndpoints
{
    public static void MapDiscountRuleEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/discount-rules").RequireAuthorization(AcademyPermissions.SubscriptionDiscountManage);
        api.MapGet("/", async (CurrentTenant tenant, FoundationDbContext db) => {
            var t = (await tenant.ResolveAsync())!;
            var rows = await db.DiscountRules.AsNoTracking().Where(x => x.AcademyId == t.AcademyId).OrderByDescending(x => x.IsSiblingOffer).ThenBy(x => x.ArabicName).ToListAsync();
            return Results.Ok(new { defaultSiblingPercentage = 20, items = rows.Select(RuleCatalog.View) });
        });
        api.MapPost("/", RuleCatalog.Create).AddEndpointFilter<CsrfFilter>();
        api.MapPut("/{id:guid}", RuleCatalog.Update).AddEndpointFilter<CsrfFilter>();
        api.MapGet("/{id:guid}/versions", RuleCatalog.History);
        api.MapGet("/options", async (CurrentTenant tenant, FoundationDbContext db) => {
            var t = (await tenant.ResolveAsync())!;
            return Results.Ok(new { owner = t.Role == AcademyRole.AcademyOwner, restricted = db.BranchScopeRestricted,
                branches = await db.Branches.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).Select(x => new { x.Id, x.ArabicName }).ToListAsync(),
                sports = await db.Sports.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).Select(x => new { x.Id, x.ArabicName }).ToListAsync(),
                plans = await db.SubscriptionPlans.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).Select(x => new { x.Id, x.ArabicName, x.SportId, x.AllBranches, branchIds = x.BranchPublications.Where(p => p.IsActive).Select(p => p.BranchId).ToArray() }).ToListAsync() });
        });
        app.MapPost("/api/v1/people/registration-quote", async (RegistrationQuote body, CurrentTenant tenant, FoundationDbContext db, DiscountRules rules, ClaimsPrincipal principal) => {
            var t = (await tenant.ResolveAsync())!;
            var plan = await db.SubscriptionPlans.TrainingOnly().AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == body.PlanId && x.IsActive);
            if (plan is null || body.DiscountRuleId.HasValue && !await db.DiscountRules.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.DiscountRuleId && x.IsActive && !x.IsSiblingOffer)) return Results.NotFound();
            if (body.BranchId.HasValue && (!await db.Branches.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.BranchId && x.IsActive) || !await db.SubscriptionPlans.TrainingOnly().ForBranch(body.BranchId.Value).AnyAsync(x => x.Id == plan.Id))) return Results.UnprocessableEntity(new { message = "الباقة غير منشورة لهذا الفرع." });
            var phone = EgyptPhoneNormalizer.Normalize(body.GuardianPhone);
            var guardian = await db.GuardianProfiles.Where(x => x.AcademyId == t.AcademyId && x.IsActive && x.ContactPhone == phone).Select(x => (Guid?)x.Id).FirstOrDefaultAsync();
            var price = await rules.CalculateAsync(t.AcademyId, Guid.Empty, plan.Price, Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!), guardian, body.DiscountRuleId, plan, body.BranchId);
            static string Money(decimal value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            return Results.Ok(new { originalAmount = Money(price.OriginalAmount), discountAmount = Money(price.DiscountAmount), finalAmount = Money(price.FinalAmount), price.DiscountReason, plan.Currency });
        }).RequireAuthorization(AcademyPermissions.PeopleManage).AddEndpointFilter<CsrfFilter>();
        api.MapGet("/players/{playerId:guid}", async (Guid playerId, CurrentTenant tenant, FoundationDbContext db) => { var t = (await tenant.ResolveAsync())!; if (!await db.Players.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == playerId)) return Results.NotFound(); return Results.Ok(await db.PlayerDiscountLinks.Where(x => x.AcademyId == t.AcademyId && x.PlayerId == playerId && x.IsActive).Select(x => x.DiscountRuleId).ToListAsync()); });
        api.MapPut("/players/{playerId:guid}", async (Guid playerId, PlayerRuleWrite body, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock) => {
            var t = (await tenant.ResolveAsync())!;
            if (!await db.Players.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == playerId) || !await db.DiscountRules.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.DiscountRuleId && x.IsActive && !x.IsSiblingOffer)) return Results.NotFound();
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"player-rule:{t.AcademyId}:{playerId}:{body.DiscountRuleId}"}, 0))");
            var link = await db.PlayerDiscountLinks.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.PlayerId == playerId && x.DiscountRuleId == body.DiscountRuleId);
            if (link is null) { link = new PlayerDiscountLink { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerId = playerId, DiscountRuleId = body.DiscountRuleId, CreatedAtUtc = clock.UtcNow }; db.PlayerDiscountLinks.Add(link); }
            link.IsActive = body.IsActive; link.UpdatedAtUtc = clock.UtcNow; await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
        }).AddEndpointFilter<CsrfFilter>();
    }
}
public sealed record RuleWrite(string ArabicName,
    [property: System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)] decimal Percentage,
    bool IsSiblingOffer, bool IsActive, string? Reason, RenewalDiscountType Type = RenewalDiscountType.Percentage,
    [property: System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)] decimal? FixedAmount = null,
    [property: System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)] decimal? MaximumAmount = null,
    Guid? BranchId = null, Guid? SportId = null, Guid? SubscriptionPlanId = null, DateOnly? StartsOn = null, DateOnly? EndsOn = null, int Priority = 0, int? ExpectedRevision = null);
public sealed record PlayerRuleWrite(Guid DiscountRuleId, bool IsActive);
public sealed record RegistrationQuote(Guid PlanId, string? GuardianPhone, Guid? DiscountRuleId, Guid? BranchId = null);
