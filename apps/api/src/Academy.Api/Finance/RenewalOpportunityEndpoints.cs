using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice3;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public static class RenewalOpportunityEndpoints
{
    public sealed record Filters(Guid? BranchId, Guid? SportId, Guid? GroupId, Guid? PlayerId, string? Search);
    public sealed record Row(Guid EnrollmentId, Guid PlayerId, string Player, string PlayerCode, Guid BranchId, string Branch,
        Guid SportId, string Sport, Guid GroupId, string Group, DateOnly? LastEndDate, Guid PlanId, string Plan,
        bool RequiresPlanSelection, int? PlanRevision, Guid? DiscountRuleId, int? DiscountRuleRevision, string? DiscountReason,
        [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] decimal? BasePrice,
        [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] decimal? Discount,
        [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] decimal? Quote, string Currency);
    public sealed record Report(DateTimeOffset QuotedAt, DateOnly QuoteDay, string Currency, int Count, int UnpricedCount,
        [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] decimal Potential, string QuoteHash, IReadOnlyList<Row> Items);

    public static void MapRenewalOpportunities(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/finance/renewal-opportunities").RequireAuthorization(AcademyPermissions.ReportsFinancialRead);
        api.MapGet("/", Read);
        api.MapGet("/export", Export).RequireAuthorization(AcademyPermissions.ReportsExport);
        api.MapGet("/options", async (CurrentTenant tenant, FoundationDbContext db) => {
            var t = (await tenant.ResolveAsync())!;
            return Results.Ok(new {
                branches = await db.Branches.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).Select(x => new { x.Id, x.ArabicName }).ToListAsync(),
                sports = await db.Sports.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).Select(x => new { x.Id, x.ArabicName }).ToListAsync(),
                groups = await db.TrainingGroups.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive).Select(x => new { x.Id, x.ArabicName, x.BranchId, x.SportId }).ToListAsync()
            });
        });
    }

    // RepeatableRead keeps all eligibility and catalog queries on one database snapshot.
    // The fingerprint prevents exporting different quotes after a price/status/permission change.
    private static async Task<IResult> Read(Guid? branchId, Guid? sportId, Guid? groupId, Guid? playerId, string? search,
        CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock, DiscountRules pricing)
    {
        if (search?.Length > 120) return Results.Problem(statusCode: 422, title: "نص البحث أطول من المسموح");
        var t = (await tenant.ResolveAsync())!;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var report = await Build(db, pricing, clock, t.AcademyId, Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!), new(branchId, sportId, groupId, playerId, search));
        await tx.CommitAsync(); return Results.Ok(report);
    }
    private static async Task<IResult> Export(Guid? branchId, Guid? sportId, Guid? groupId, Guid? playerId, string? search, string? quoteHash,
        CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock, DiscountRules pricing)
    {
        if (quoteHash?.Length != 64 || search?.Length > 120) return Results.Problem(statusCode: 422, title: "افتح التقرير أولًا ثم صدّره من نفس النطاق");
        var t = (await tenant.ResolveAsync())!;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var report = await Build(db, pricing, clock, t.AcademyId, Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!), new(branchId, sportId, groupId, playerId, search));
        if (report.QuoteHash != quoteHash) return Results.Conflict(new { message = "تغيّرت الأسعار أو الأهلية أو نطاق الوصول. حدّث التقرير ثم أعد التصدير." });
        static string Cell(string value) => "\"" + (value.TrimStart() is { Length: > 0 } v && "=+-@".Contains(v[0]) || value.StartsWith('\t') || value.StartsWith('\r') ? "'" : "") + value.Replace("\"", "\"\"") + "\"";
        static string Amount(decimal? value) => value?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var lines = new List<string> { "اللاعب,الكود,الفرع,الرياضة,المجموعة,انتهاء الخدمة السابقة,الباقة,نسخة السعر,الأساس,الخصم,فرصة التجديد,العملة,تحتاج اختيار باقة,تاريخ التقييم,مرجع النطاق" };
        lines.AddRange(report.Items.Select(x => string.Join(',', Cell(x.Player), Cell(x.PlayerCode), Cell(x.Branch), Cell(x.Sport), Cell(x.Group), x.LastEndDate?.ToString("yyyy-MM-dd") ?? "", Cell(x.Plan), x.PlanRevision?.ToString() ?? "", Amount(x.BasePrice), Amount(x.Discount), Amount(x.Quote), x.Currency, x.RequiresPlanSelection ? "نعم" : "لا", report.QuoteDay.ToString("yyyy-MM-dd"), report.QuoteHash)));
        await tx.CommitAsync();
        return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join('\n', lines))).ToArray(), "text/csv; charset=utf-8", "renewal-opportunities.csv");
    }
    internal static async Task<Report> Build(FoundationDbContext db, DiscountRules pricing, ISubscriptionClock clock, Guid academy, Guid actor, Filters filter)
    {
        var today = clock.Today; var instant = clock.UtcNow;
        var currency = await db.Academies.Where(x => x.Id == academy).Select(x => x.DefaultCurrency).SingleAsync();
        var q = db.SportEnrollments.AsNoTracking().Where(e => e.AcademyId == academy && e.IsActive && e.Status == EnrollmentStatus.Active && e.Player.IsActive && e.Branch.IsActive && e.Sport.IsActive);
        if (filter.BranchId.HasValue) q = q.Where(e => e.BranchId == filter.BranchId);
        if (filter.SportId.HasValue) q = q.Where(e => e.SportId == filter.SportId);
        if (filter.GroupId.HasValue) q = q.Where(e => e.TrainingGroupId == filter.GroupId);
        if (filter.PlayerId.HasValue) q = q.Where(e => e.PlayerId == filter.PlayerId);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = filter.Search.Trim(); q = q.Where(e => e.Player.ArabicName.Contains(term) || e.Player.PlayerCode.Contains(term)); }
        q = q.Where(e => db.SubscriptionPeriods.Any(p => p.SportEnrollmentId == e.Id && p.IsActive && p.StartDate <= today && (p.EndDate < today || p.RemainingSessions == 0)));
        var enrollments = await q.Include(e => e.Player).Include(e => e.Branch).Include(e => e.Sport).Include(e => e.TrainingGroup).OrderBy(e => e.Id).ToListAsync();
        var ids = enrollments.Select(e => e.Id).ToArray();
        var periods = await db.SubscriptionPeriods.AsNoTracking().Include(p => p.SubscriptionPlan).Include(p => p.Collection).ThenInclude(c => c.PaymentRequest)
            .Where(p => p.AcademyId == academy && ids.Contains(p.SportEnrollmentId)).ToListAsync();
        var byEnrollment = periods.ToLookup(p => p.SportEnrollmentId);
        var collections = periods.Select(p => p.CollectionId).Distinct().ToArray();
        var refunds = await db.CollectionRefunds.Where(r => r.AcademyId == academy && collections.Contains(r.CollectionId)).GroupBy(r => r.CollectionId).Select(g => new { Id = g.Key, Amount = g.Sum(r => r.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Amount);
        var plans = await db.SubscriptionPlans.TrainingOnly().AsNoTracking().Include(p => p.BranchPublications).Where(p => p.AcademyId == academy && p.IsActive && p.Currency == currency).ToDictionaryAsync(p => p.Id);
        var candidates = new List<(SportEnrollment Enrollment, SubscriptionPeriod Last, SubscriptionPlan? Plan)>();
        foreach (var e in enrollments)
        {
            var history = byEnrollment[e.Id].OrderByDescending(p => p.StartDate).ThenByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id).ToArray();
            var last = history.FirstOrDefault();
            if (last is null || !RenewalOpportunity.Expired(last, last.SubscriptionPlan, today)
                || history.Any(p => p.IsActive && p.Status != SubscriptionPeriodStatus.Cancelled && !RenewalOpportunity.Expired(p, p.SubscriptionPlan, today))
                || last.Origin == SubscriptionOrigin.Collected && (!last.Collection.IsActive || last.Collection.PaymentRequest.Status != PaymentRequestStatus.Confirmed
                || last.Collection.Amount - refunds.GetValueOrDefault(last.CollectionId!.Value) < last.PriceSnapshot)) continue;
            var plan = plans.GetValueOrDefault(last.SubscriptionPlanId);
            if (plan is not null && !(plan.AllBranches || plan.BranchPublications.Any(b => b.IsActive && b.BranchId == e.BranchId))) plan = null;
            candidates.Add((e, last, plan));
        }
        var quotes = await pricing.CalculateManyAsync(academy, actor, candidates.Where(c => c.Plan is not null).Select(c => new DiscountRules.PricingLine(c.Enrollment.Id, c.Enrollment.PlayerId, c.Enrollment.BranchId, c.Plan!)).ToArray());
        var rows = new List<Row>();
        foreach (var (e, last, plan) in candidates)
        {
            var quote = quotes.GetValueOrDefault(e.Id);
            rows.Add(new(e.Id, e.PlayerId, e.Player.ArabicName, e.Player.PlayerCode, e.BranchId, e.Branch.ArabicName, e.SportId, e.Sport.ArabicName, e.TrainingGroupId, e.TrainingGroup.ArabicName,
                last.EndDate, last.SubscriptionPlanId, plan?.ArabicName ?? last.PlanNameSnapshot ?? last.SubscriptionPlan.ArabicName, plan is null, plan?.Revision,
                quote?.DiscountRuleId, quote?.DiscountRuleRevision, quote?.DiscountReason, quote?.OriginalAmount, quote?.DiscountAmount, quote?.FinalAmount, currency));
        }
        // A hash is a stale-data check, not authorization. Every export rechecks grants and scope.
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { academy, today, filter, rows })));
        return new(instant, today, currency, rows.Count, rows.Count(x => x.RequiresPlanSelection), rows.Sum(x => x.Quote ?? 0), fingerprint, rows);
    }
}
