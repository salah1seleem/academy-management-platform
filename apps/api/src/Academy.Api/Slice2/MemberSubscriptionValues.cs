using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice2;

// A read model of associated subscriptions, not collections, payer identity, or revenue.
public static class MemberSubscriptionValues
{
    public const string Basis = "القيمة الشهرية المكافئة للاشتراكات السارية؛ ليست تحصيلًا أو إثباتًا للدافع.";
    // Member initializers remain composable inside EF aggregates and ordering.
    public sealed class PeriodValue { public Guid PlayerId {get;init;} public decimal? MonthlyValue {get;init;} }
    public sealed class GuardianValue {
        public Guid Id {get;init;} public required string DisplayName {get;init;} public string? ContactPhone {get;init;}
        public int LinkedChildren {get;init;} public decimal MonthlySubscriptionValue {get;init;}
        public int UnpricedSubscriptions {get;init;} public required string Currency {get;init;}
    }

    public static IQueryable<PeriodValue> Active(FoundationDbContext db, Guid academy, DateOnly day, string currency,
        Guid? branch = null, Guid? sport = null)
    {
        var candidates = db.SubscriptionPeriods.AsNoTracking().Where(p => p.AcademyId == academy && p.IsActive
            && (p.Status == SubscriptionPeriodStatus.Active || p.Status == SubscriptionPeriodStatus.Scheduled) && p.StartDate <= day && (p.EndDate == null || p.EndDate >= day)
            && p.SportEnrollment.IsActive && p.SportEnrollment.Status == EnrollmentStatus.Active && p.SportEnrollment.Player.IsActive
            && (!branch.HasValue || p.SportEnrollment.BranchId == branch) && (!sport.HasValue || p.SportId == sport)
            && (p.PlanTypeSnapshot == SubscriptionPlanType.Duration || p.PlanTypeSnapshot == null && p.InitialSessions == null || p.RemainingSessions > 0));
        // One effective period per enrollment. Stable tie-breaker avoids duplicate legacy/import joins.
        var effective = candidates.Where(p => p.Id == candidates.Where(n => n.SportEnrollmentId == p.SportEnrollmentId)
            .OrderByDescending(n => n.StartDate).ThenByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id).Select(n => n.Id).First());
        var terms = effective.Select(p => new {
            p.SportEnrollment.PlayerId, p.CurrencySnapshot,
            Price = p.PriceSnapshot ?? (p.CollectionId.HasValue ? (decimal?)p.Collection.Amount : null),
            Months = p.DurationMonthsSnapshot,
            Days = p.DurationDaysSnapshot ?? (p.EndDate.HasValue ? (int?)(p.EndDate.Value.DayNumber - p.StartDate.DayNumber + 1) : null)
        });
        // Calendar-month terms take precedence; day-based legacy terms use an explicit 30-day equivalent.
        // Never read a mutable plan price to invent a historical amount. Undated session packs remain unknown.
        return terms.Select(p => new PeriodValue {PlayerId=p.PlayerId,MonthlyValue=
            p.CurrencySnapshot != currency || p.Price == null || p.Price < 0 ? null : p.Price == 0 ? 0m : p.Months > 0 ? p.Price / p.Months : p.Days > 0 ? p.Price * 30m / p.Days : null});
    }

    public static IQueryable<GuardianValue> Guardians(FoundationDbContext db, Guid academy, DateOnly day, string currency,
        Guid? branch = null, Guid? sport = null, string? search = null)
    {
        var values = Active(db, academy, day, currency, branch, sport);
        var links = db.GuardianPlayerLinks.Where(l => l.AcademyId == academy && l.IsActive && l.Guardian.IsActive && l.Player.IsActive
            && (!branch.HasValue && !sport.HasValue || db.SportEnrollments.Any(e => e.AcademyId == academy && e.PlayerId == l.PlayerId && e.IsActive
                && (!branch.HasValue || e.BranchId == branch) && (!sport.HasValue || e.SportId == sport))))
            .Select(l => new { l.GuardianId, l.PlayerId }).Distinct();
        var term = search?.Trim(); var phone = EgyptPhoneNormalizer.Normalize(term);
        return db.GuardianProfiles.AsNoTracking().Where(g => g.AcademyId == academy && g.IsActive
            && (string.IsNullOrEmpty(term) || g.DisplayName.Contains(term) || phone != null && g.ContactPhone == phone)
            && (!branch.HasValue && !sport.HasValue || links.Any(l => l.GuardianId == g.Id)))
            .Select(g => new GuardianValue {Id=g.Id, DisplayName=g.DisplayName, ContactPhone=g.ContactPhone,
                LinkedChildren=links.Count(l => l.GuardianId == g.Id),
                MonthlySubscriptionValue=Math.Round(values.Where(v => links.Where(l => l.GuardianId == g.Id).Select(l => l.PlayerId).Contains(v.PlayerId)).Sum(v => v.MonthlyValue) ?? 0, 2),
                UnpricedSubscriptions=values.Count(v => v.MonthlyValue == null && links.Where(l => l.GuardianId == g.Id).Select(l => l.PlayerId).Contains(v.PlayerId)), Currency=currency});
    }

    public static async Task<IResult> GuardianList(string? search, string? sort, int? page, int? pageSize, Guid? branchId, Guid? sportId,
        DateOnly? asOf, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!;
        if (sort is not (null or "" or "name" or "value_asc" or "value_desc") || page is < 1 or > 1000000 || pageSize is < 1 or > 100)
            return Results.UnprocessableEntity(new { message = "اختر ترتيبًا وصفحة صحيحين." });
        var currency = await db.Academies.Where(a => a.Id == t.AcademyId).Select(a => a.DefaultCurrency).SingleAsync();
        var date = asOf ?? clock.Today; var query = Guardians(db, t.AcademyId, date, currency, branchId, sportId, search);
        var ordered = sort switch {
            "value_desc" => query.OrderBy(g => g.UnpricedSubscriptions > 0).ThenByDescending(g => g.MonthlySubscriptionValue).ThenBy(g => g.DisplayName).ThenBy(g => g.Id),
            "value_asc" => query.OrderBy(g => g.UnpricedSubscriptions > 0).ThenBy(g => g.MonthlySubscriptionValue).ThenBy(g => g.DisplayName).ThenBy(g => g.Id),
            _ => query.OrderBy(g => g.DisplayName).ThenBy(g => g.Id)
        };
        if (!page.HasValue && !pageSize.HasValue) return Results.Ok(await ordered.ToArrayAsync()); // Existing callers retain array contract.
        var size = pageSize ?? 25; var number = page ?? 1;
        return Results.Ok(new { items = await ordered.Skip((number - 1) * size).Take(size).ToArrayAsync(), total = await query.CountAsync(), page = number, pageSize = size, currency, asOf = date, basis = Basis });
    }

    public static void MapVipMembers(this WebApplication app) => app.MapGet("/api/v1/reports/vip-members",
        async (Guid? branchId, Guid? sportId, DateOnly? asOf, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock) => {
            var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
            var currency = await db.Academies.Where(a => a.Id == t.AcademyId).Select(a => a.DefaultCurrency).SingleAsync(); var date = asOf ?? clock.Today;
            var items = await Guardians(db, t.AcademyId, date, currency, branchId, sportId).Where(g => g.UnpricedSubscriptions == 0 && g.MonthlySubscriptionValue > 0)
                .OrderByDescending(g => g.MonthlySubscriptionValue).ThenBy(g => g.DisplayName).ThenBy(g => g.Id).Take(10)
                .Select(g => new { g.Id, g.DisplayName, g.MonthlySubscriptionValue, g.Currency }).ToArrayAsync();
            return Results.Ok(new { items, currency, asOf = date, basis = Basis });
        }).RequireAuthorization(AcademyPermissions.OwnerDashboardRead);
}
