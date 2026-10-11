using System.Globalization;
using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice3;

public static class PlanCatalog
{
    public static IQueryable<SubscriptionPlan> ForBranch(this IQueryable<SubscriptionPlan> plans, Guid branch) =>
        plans.Where(p => p.AllBranches || p.BranchPublications.Any(b => b.BranchId == branch && b.IsActive && b.Branch.IsActive));
    public static Task LockAsync(FoundationDbContext db, Guid academy, Guid plan) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"plan-publication:{academy}:{plan}"}, 0))");
    public static async Task<IResult> Detail(Guid id, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        var plan = await db.SubscriptionPlans.AsNoTracking().Include(x => x.Sport).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id);
        if (plan is null) return Results.NotFound();
        var branches = await db.PlanBranchPublications.Where(x => x.AcademyId == t.AcademyId && x.SubscriptionPlanId == id && x.IsActive).Select(x => x.BranchId).ToArrayAsync();
        var versions = await db.PlanPriceVersions.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.SubscriptionPlanId == id).OrderByDescending(x => x.Revision)
            .Select(x => new { x.Revision, x.ArabicName, price = x.Price.ToString("0.00", CultureInfo.InvariantCulture), x.Currency, planType = x.PlanType.ToString(), x.DurationDays, x.SessionCount, x.Published, x.EffectiveAtUtc }).Take(50).ToListAsync();
        return Results.Ok(new { plan.Id, plan.ArabicName, plan.EnglishName, plan.SportId, sport = plan.Sport.ArabicName, planType = plan.PlanType.ToString(), price = plan.Price.ToString("0.00", CultureInfo.InvariantCulture), plan.Currency, plan.DurationDays, plan.SessionCount, plan.DisplayOrder, plan.IsActive, plan.AllBranches, branchIds = branches, plan.Revision, versions });
    }
    public static async Task<IResult> Save(Guid? id, PlanRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var branches = (request.BranchIds ?? []).Distinct().ToArray();
        var validType = request.PlanType switch { SubscriptionPlanType.Duration => request.DurationDays is > 0 and <= 3660 && request.SessionCount is null, SubscriptionPlanType.Sessions => request.DurationDays is null && request.SessionCount is > 0 and <= 10000, SubscriptionPlanType.Combined => request.DurationDays is > 0 and <= 3660 && request.SessionCount is > 0 and <= 10000, _ => false };
        if (string.IsNullOrWhiteSpace(request.ArabicName) || request.ArabicName.Trim().Length > 160 || request.EnglishName?.Length > 160 || request.Price <= 0 || request.Price > 999999999m || decimal.Round(request.Price, 2) != request.Price || !validType || branches.Length > 200 || (!request.AllBranches && branches.Length == 0))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["plan"] = ["أكمل اسم الباقة وسعرًا صحيحًا وحدود الخدمة والفروع المتاحة."] });
        if (db.BranchScopeRestricted && (request.AllBranches || branches.Any(b => !db.RequestBranchIds.Contains(b)))) return Results.Forbid();
        if (!await db.Sports.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == request.SportId && x.IsActive)
            || await db.Branches.CountAsync(x => x.AcademyId == t.AcademyId && branches.Contains(x.Id) && x.IsActive) != branches.Length)
            return Results.UnprocessableEntity(new { message = "الرياضة أو أحد الفروع غير متاح داخل الأكاديمية." });
        if (request.AllBranches) branches = [];
        await using var tx = await db.Database.BeginTransactionAsync();
        var planId = id ?? Guid.NewGuid(); await LockAsync(db, t.AcademyId, planId);
        var plan = id.HasValue ? await db.SubscriptionPlans.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id) : null;
        if (id.HasValue && plan is null) return Results.NotFound();
        if (plan is not null && request.ExpectedRevision.HasValue && plan.Revision != request.ExpectedRevision.Value) return Results.Conflict(new { message = "عدّل مستخدم آخر هذه الباقة. أعد تحميلها قبل الحفظ." });
        if (plan is not null && plan.SportId != request.SportId && (await db.RenewalRequests.AnyAsync(x => x.SubscriptionPlanId == plan.Id) || await db.NewEnrollmentRequests.AnyAsync(x => x.SubscriptionPlanId == plan.Id)))
            return Results.Conflict(new { message = "الباقة مستخدمة. أنشئ باقة منفصلة للرياضة الأخرى مع حفظ التاريخ." });
        var currency = await db.Academies.Where(x => x.Id == t.AcademyId).Select(x => x.DefaultCurrency).SingleAsync();
        plan ??= new SubscriptionPlan { Id = planId, AcademyId = t.AcademyId, ArabicName = "", Currency = currency, CreatedAtUtc = clock.UtcNow };
        if (!id.HasValue) db.SubscriptionPlans.Add(plan);
        plan.ArabicName = request.ArabicName.Trim(); plan.EnglishName = request.EnglishName?.Trim(); plan.SportId = request.SportId;
        plan.PlanType = request.PlanType; plan.Price = request.Price; plan.DurationDays = request.DurationDays; plan.SessionCount = request.SessionCount;
        plan.DisplayOrder = request.DisplayOrder; plan.AllBranches = request.AllBranches; plan.Revision++; plan.UpdatedAtUtc = clock.UtcNow;
        var old = await db.PlanBranchPublications.Where(x => x.AcademyId == t.AcademyId && x.SubscriptionPlanId == planId).ToListAsync();
        foreach (var row in old) row.IsActive = branches.Contains(row.BranchId);
        foreach (var branch in branches.Except(old.Select(x => x.BranchId))) db.PlanBranchPublications.Add(new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SubscriptionPlanId = planId, BranchId = branch, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
        db.PlanPriceVersions.Add(PlanPriceVersion.Create(plan, branches, Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!), clock.UtcNow));
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); }
        catch (DbUpdateException) { return Results.Conflict(new { message = "تعذر حفظ الباقة؛ تأكد من عدم تكرار اسمها ثم حدّث الصفحة." }); }
        return id.HasValue ? Results.NoContent() : Results.Created($"/api/v1/subscriptions/plans/{plan.Id}", new { plan.Id, plan.Revision });
    }
    public static async Task<IResult> SetStatus(Guid id, bool active, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; await using var tx = await db.Database.BeginTransactionAsync(); await LockAsync(db, t.AcademyId, id);
        var plan = await db.SubscriptionPlans.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id);
        if (plan is null) return Results.NotFound();
        if (plan.IsActive != active)
        {
            plan.IsActive = active; plan.Revision++; plan.UpdatedAtUtc = clock.UtcNow;
            var branches = await db.PlanBranchPublications.Where(x => x.SubscriptionPlanId == id && x.IsActive).Select(x => x.BranchId).ToArrayAsync();
            db.PlanPriceVersions.Add(PlanPriceVersion.Create(plan, branches, Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!), clock.UtcNow));
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync(); return Results.Ok(new { archived = !active, message = active ? "تم تفعيل الباقة." : "تمت أرشفة الباقة مع حفظ تاريخ الأسعار والاشتراكات." });
    }
}
