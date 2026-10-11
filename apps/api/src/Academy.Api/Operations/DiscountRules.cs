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
    public async Task<RenewalFinancialSnapshot> CalculateAsync(Guid academy, Guid playerId, decimal amount, Guid actor, Guid? guardianId = null, Guid? selectedRule = null)
    {
        var rules = await db.DiscountRules.AsNoTracking().Where(x => x.AcademyId == academy && x.IsActive && !x.IsSiblingOffer && (x.Id == selectedRule || db.PlayerDiscountLinks.Any(l => l.AcademyId == academy && l.PlayerId == playerId && l.DiscountRuleId == x.Id && l.IsActive))).ToListAsync();
        // Shared guardian alone is insufficient: another child must have a current paid period.
        var guardians = db.GuardianProfiles.Where(g => g.AcademyId == academy && g.IsActive && (g.Id == guardianId || db.GuardianPlayerLinks.Any(l => l.AcademyId == academy && l.PlayerId == playerId && l.IsActive && l.GuardianId == g.Id))).Select(g => g.Id);
        var siblings = db.GuardianPlayerLinks.Where(l => l.AcademyId == academy && l.PlayerId != playerId && l.IsActive && l.Player.IsActive && guardians.Contains(l.GuardianId)).Select(l => l.PlayerId);
        if (await db.SubscriptionPeriods.AnyAsync(p => p.AcademyId == academy && siblings.Contains(p.SportEnrollment.PlayerId) && p.Status != SubscriptionPeriodStatus.Cancelled && p.Status != SubscriptionPeriodStatus.Frozen && p.StartDate <= clock.Today && (p.EndDate == null || p.EndDate >= clock.Today) && ((p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) == SubscriptionPlanType.Duration || p.RemainingSessions > 0)))
        {
            var sibling = await db.DiscountRules.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == academy && x.IsSiblingOffer);
            if (sibling is null) rules.Add(new DiscountRule { AcademyId = academy, ArabicName = "عرض الإخوة", Percentage = 20, IsSiblingOffer = true, Reason = "عرض الإخوة الافتراضي" });
            else if (sibling.IsActive) rules.Add(sibling);
        }
        var best = rules.OrderByDescending(x => x.Percentage).FirstOrDefault();
        var discount = decimal.Round(amount * (best?.Percentage ?? 0) / 100m, 2, MidpointRounding.AwayFromZero);
        return new(amount, best is null ? null : RenewalDiscountType.Percentage, best?.Percentage, discount, amount - discount, best?.Reason, best is null ? null : actor, best is null ? null : clock.UtcNow);
    }
}

public static class DiscountRuleEndpoints
{
    public static void MapDiscountRuleEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/discount-rules").RequireAuthorization(AcademyPermissions.SubscriptionDiscountManage);
        api.MapGet("/", async (CurrentTenant tenant, FoundationDbContext db) => {
            var t = (await tenant.ResolveAsync())!;
            var rows = await db.DiscountRules.AsNoTracking().Where(x => x.AcademyId == t.AcademyId).OrderByDescending(x => x.IsSiblingOffer).ThenBy(x => x.ArabicName).Select(x => new { x.Id, x.ArabicName, x.Percentage, x.IsSiblingOffer, x.IsActive, x.Reason }).ToListAsync();
            return Results.Ok(new { defaultSiblingPercentage = 20, items = rows });
        });
        api.MapPost("/", Save).AddEndpointFilter<CsrfFilter>();
        api.MapPut("/{id:guid}", Update).AddEndpointFilter<CsrfFilter>();
        app.MapPost("/api/v1/people/registration-quote", async (RegistrationQuote body, CurrentTenant tenant, FoundationDbContext db, DiscountRules rules, ClaimsPrincipal principal) => {
            var t = (await tenant.ResolveAsync())!;
            var plan = await db.SubscriptionPlans.TrainingOnly().AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == body.PlanId && x.IsActive);
            if (plan is null || body.DiscountRuleId.HasValue && !await db.DiscountRules.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.DiscountRuleId && x.IsActive && !x.IsSiblingOffer)) return Results.NotFound();
            if (body.BranchId.HasValue && (!await db.Branches.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.BranchId && x.IsActive) || !await db.SubscriptionPlans.TrainingOnly().ForBranch(body.BranchId.Value).AnyAsync(x => x.Id == plan.Id))) return Results.UnprocessableEntity(new { message = "الباقة غير منشورة لهذا الفرع." });
            var phone = EgyptPhoneNormalizer.Normalize(body.GuardianPhone);
            var guardian = await db.GuardianProfiles.Where(x => x.AcademyId == t.AcademyId && x.IsActive && x.ContactPhone == phone).Select(x => (Guid?)x.Id).FirstOrDefaultAsync();
            var price = await rules.CalculateAsync(t.AcademyId, Guid.Empty, plan.Price, Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!), guardian, body.DiscountRuleId);
            return Results.Ok(new { price.OriginalAmount, price.DiscountAmount, price.FinalAmount, price.DiscountReason, plan.Currency });
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
    private static async Task<IResult> Save(RuleWrite body, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock) => await Write(null, body, tenant, db, clock);
    private static async Task<IResult> Update(Guid id, RuleWrite body, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock) => await Write(id, body, tenant, db, clock);
    private static async Task<IResult> Write(Guid? id, RuleWrite body, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!;
        if (body.Percentage < 0 || body.Percentage > 100 || decimal.Round(body.Percentage, 2) != body.Percentage || string.IsNullOrWhiteSpace(body.ArabicName) || body.ArabicName.Length > 120 || string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length > 500) return Results.UnprocessableEntity(new { message = "الاسم وسبب الخصم مطلوبان، والنسبة بين صفر و100." });
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"discount-rules:{t.AcademyId}"}, 0))");
        var row = id.HasValue ? await db.DiscountRules.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id) : null;
        if (id.HasValue && row is null) return Results.NotFound();
        if ((body.IsSiblingOffer || row?.IsSiblingOffer == true) && t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        if (row is not null && row.IsSiblingOffer != body.IsSiblingOffer) return Results.UnprocessableEntity();
        if (await db.DiscountRules.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id != id && (x.ArabicName == body.ArabicName.Trim() || body.IsSiblingOffer && x.IsSiblingOffer))) return Results.Conflict(new { message = "يوجد عرض بهذا الاسم أو عرض الإخوة بالفعل." });
        if (row is null) { row = new DiscountRule { Id = Guid.NewGuid(), AcademyId = t.AcademyId, ArabicName = body.ArabicName.Trim(), Reason = body.Reason.Trim(), IsSiblingOffer = body.IsSiblingOffer, CreatedAtUtc = clock.UtcNow }; db.DiscountRules.Add(row); }
        // The built-in sibling offer is permanent; the owner changes its percentage only.
        row.ArabicName = row.IsSiblingOffer ? "عرض الإخوة" : body.ArabicName.Trim(); row.Percentage = body.Percentage; row.Reason = row.IsSiblingOffer ? "عرض الإخوة الافتراضي" : body.Reason.Trim(); row.IsActive = row.IsSiblingOffer || body.IsActive; row.UpdatedAtUtc = clock.UtcNow; await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { row.Id });
    }
}
public sealed record RuleWrite(string ArabicName, decimal Percentage, bool IsSiblingOffer, bool IsActive, string Reason);
public sealed record PlayerRuleWrite(Guid DiscountRuleId, bool IsActive);
public sealed record RegistrationQuote(Guid PlanId, string? GuardianPhone, Guid? DiscountRuleId, Guid? BranchId = null);
