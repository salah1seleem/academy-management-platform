using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class RuleCatalog
{
    public static object View(DiscountRule r) => new { r.Id, r.ArabicName, type = r.Type.ToString(), r.Percentage,
        fixedAmount = Money(r.FixedAmount), maximumAmount = Money(r.MaximumAmount), r.Currency, r.BranchId, r.SportId, r.SubscriptionPlanId,
        r.StartsOn, r.EndsOn, r.Priority, r.Revision, r.IsSiblingOffer, r.IsActive, r.Reason };
    private static string? Money(decimal? v) => v?.ToString("0.00", CultureInfo.InvariantCulture);
    public static async Task<IResult> History(Guid id, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        if (!await db.DiscountRules.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == id)) return Results.NotFound();
        var rows = await db.DiscountRuleVersions.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.DiscountRuleId == id).OrderByDescending(x => x.Revision).Take(50).ToListAsync();
        return Results.Ok(rows.Select(x => new { x.Revision, x.CreatedAtUtc, snapshot = JsonSerializer.Deserialize<JsonElement>(x.Payload) }));
    }
    public static Task<IResult> Create(RuleWrite body, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock, ClaimsPrincipal actor) => Save(null, body, tenant, db, clock, actor);
    public static Task<IResult> Update(Guid id, RuleWrite body, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock, ClaimsPrincipal actor) => Save(id, body, tenant, db, clock, actor);
    private static async Task<IResult> Save(Guid? id, RuleWrite body, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock, ClaimsPrincipal actor)
    {
        var t = (await tenant.ResolveAsync())!;
        static bool MoneyValid(decimal? v) => v is null || v is > 0 and <= 999999999m && decimal.Round(v.Value, 2) == v;
        if (string.IsNullOrWhiteSpace(body.ArabicName) || body.ArabicName.Trim().Length > 120 || body.Reason?.Length > 500 || !Enum.IsDefined(body.Type)
            || body.Percentage < 0 || body.Percentage > 100 || decimal.Round(body.Percentage, 2) != body.Percentage || !MoneyValid(body.FixedAmount) || !MoneyValid(body.MaximumAmount)
            || body.Type == RenewalDiscountType.FixedAmount && (!body.FixedAmount.HasValue || body.Percentage != 0)
            || body.Type == RenewalDiscountType.Percentage && body.FixedAmount.HasValue || body.Priority is < 0 or > 10000 || body.StartsOn > body.EndsOn)
            return Results.UnprocessableEntity(new { message = "راجع اسم العرض ونوع الخصم وقيمته وحدوده وفترة السريان. وصف العرض اختياري." });
        if (db.BranchScopeRestricted && (!body.BranchId.HasValue || !db.RequestBranchIds.Contains(body.BranchId.Value))) return Results.Forbid();
        if (body.BranchId.HasValue && !await db.Branches.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.BranchId && x.IsActive)
            || body.SportId.HasValue && !await db.Sports.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.SportId && x.IsActive)) return Results.NotFound();
        if (body.SubscriptionPlanId.HasValue && !await db.SubscriptionPlans.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.SubscriptionPlanId && x.IsActive && (!body.SportId.HasValue || x.SportId == body.SportId)
            && (!body.BranchId.HasValue || x.AllBranches || x.BranchPublications.Any(p => p.BranchId == body.BranchId && p.IsActive)))) return Results.UnprocessableEntity(new { message = "الباقة غير متوافقة مع نطاق العرض." });
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"discount-rules:{t.AcademyId}"}, 0))");
        var row = id.HasValue ? await db.DiscountRules.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id) : null;
        if (id.HasValue && row is null) return Results.NotFound();
        if ((body.IsSiblingOffer || row?.IsSiblingOffer == true) && t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        if (row is not null && row.IsSiblingOffer != body.IsSiblingOffer) return Results.UnprocessableEntity();
        if (row is not null && body.ExpectedRevision.HasValue && body.ExpectedRevision != row.Revision) return Results.Conflict(new { message = "تغير العرض منذ فتحه؛ أعد تحميله قبل الحفظ." });
        if (await db.DiscountRules.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id != id && (x.ArabicName == body.ArabicName.Trim() || body.IsSiblingOffer && x.IsSiblingOffer))) return Results.Conflict(new { message = "يوجد عرض بهذا الاسم أو عرض الإخوة بالفعل." });
        if (row is null) { row = new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, ArabicName = "", Reason = "", CreatedAtUtc = clock.UtcNow }; db.DiscountRules.Add(row); }
        row.IsSiblingOffer = body.IsSiblingOffer; row.ArabicName = row.IsSiblingOffer ? "عرض الإخوة" : body.ArabicName.Trim();
        row.Type = body.Type; row.Percentage = body.Percentage; row.FixedAmount = body.FixedAmount; row.MaximumAmount = body.MaximumAmount;
        row.BranchId = body.BranchId; row.SportId = body.SportId; row.SubscriptionPlanId = body.SubscriptionPlanId;
        row.StartsOn = body.StartsOn; row.EndsOn = body.EndsOn; row.Priority = body.Priority; row.Revision++;
        row.Currency = await db.Academies.Where(x => x.Id == t.AcademyId).Select(x => x.DefaultCurrency).SingleAsync();
        row.Reason = body.Reason?.Trim() ?? ""; row.IsActive = row.IsSiblingOffer || body.IsActive; row.UpdatedAtUtc = clock.UtcNow;
        db.DiscountRuleVersions.Add(DiscountRuleVersion.Create(row, Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!), clock.UtcNow));
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); } catch (DbUpdateException) { return Results.Conflict(new { message = "تعذر حفظ العرض؛ حدّث القائمة وتحقق من عدم تكرار الاسم." }); }
        return Results.Ok(new { row.Id, row.Revision });
    }
}
