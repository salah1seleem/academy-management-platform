using System.Data;
using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice3;

public sealed record FamilyRenewalChoice(Guid SportEnrollmentId, Guid SubscriptionPlanId);
public sealed record FamilyRenewalWrite(IReadOnlyList<FamilyRenewalChoice>? Lines);

public static class FamilyRenewalEndpoints
{
    public static void MapFamilyRenewalQuotes(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/guardian/subscriptions/family").RequireAuthorization(AcademyPermissions.GuardianOwnRenewal);
        api.MapGet("/options", async (FamilyRenewalQuoteService service) => await Run(service.Options));
        api.MapPost("/quote", async (FamilyRenewalWrite body, FamilyRenewalQuoteService service) => await Run(() => service.Quote(body))).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("renewal-search");
        var staff = app.MapGroup("/api/v1/manage/family-renewals/quotes").RequireAuthorization(AcademyPermissions.SubscriptionRead, AcademyPermissions.PaymentRead);
        staff.MapGet("/families", async (string? search, FamilyRenewalQuoteService service) => await Run(() => service.StaffFamilies(search))).RequireRateLimiting("renewal-search");
        staff.MapGet("/{guardianId:guid}/options", async (Guid guardianId, FamilyRenewalQuoteService service) => await Run(() => service.StaffOptions(guardianId)));
        staff.MapPost("/{guardianId:guid}/quote", async (Guid guardianId, FamilyRenewalWrite body, FamilyRenewalQuoteService service) => await Run(() => service.StaffQuote(guardianId, body))).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("renewal-search");
    }
    private static async Task<IResult> Run(Func<Task<object>> work)
    {
        try { return Results.Ok(await work()); }
        catch (CashRejected error) { return Results.Problem(title: error.Message, statusCode: error.Status); }
    }
}

public sealed class FamilyRenewalQuoteService(FoundationDbContext db, CurrentTenant tenant, IHttpContextAccessor accessor, DiscountRules discounts, ISubscriptionClock clock)
{
    private async Task<(TenantMembership Tenant, Guid User, Guid Guardian)> Context()
    {
        var t = await tenant.ResolveAsync() ?? throw new CashRejected("سجل الدخول أولًا.", 401);
        if (t.Role != AcademyRole.Guardian || !Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var user)) throw new CashRejected("هذه الصفحة لولي الأمر المرتبط بالأبناء فقط.", 403);
        var guardian = await db.GuardianProfiles.Where(g => g.AcademyId == t.AcademyId && g.IsActive && g.UserId == user).Select(g => (Guid?)g.Id).SingleOrDefaultAsync();
        return guardian is { } id ? (t, user, id) : throw new CashRejected("ملف ولي الأمر غير متاح.", 404);
    }
    private IQueryable<SportEnrollment> Own(Guid academy, Guid guardian) => db.SportEnrollments.AsNoTracking().Where(e => e.AcademyId == academy && e.IsActive && e.Status == EnrollmentStatus.Active && e.Player.IsActive && e.Branch.IsActive && e.Sport.IsActive && db.GuardianPlayerLinks.Any(l => l.AcademyId == academy && l.IsActive && l.Guardian.IsActive && l.GuardianId == guardian && l.PlayerId == e.PlayerId));
    private IQueryable<SubscriptionPlan> Available(Guid academy) => db.SubscriptionPlans.AsNoTracking().TrainingOnly().Where(p => p.AcademyId == academy && p.IsActive);
    private async Task<(TenantMembership Tenant, Guid User, Guid Guardian)> StaffContext(Guid? guardian = null)
    {
        var t = await tenant.ResolveAsync() ?? throw new CashRejected("سجل الدخول أولًا.", 401);
        if (t.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin) || !Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var user)) throw new CashRejected("مراجعة التجديد للإدارة المصرح لها فقط.", 403);
        if (t.Role != AcademyRole.AcademyOwner && await db.AcademyMemberships.AnyAsync(m => m.Id == t.MembershipId && m.UseActionGrants))
        {
            foreach (var module in new[] { "players", "subscriptions", "payments" })
                if (!await db.StaffActionGrants.AnyAsync(g => g.MembershipId == t.MembershipId && g.IsActive && g.Module == module && g.Read)) throw new CashRejected("يلزم تفويض قراءة اللاعبين والاشتراكات وطلبات الدفع.", 403);
        }
        if (guardian.HasValue && (!await db.GuardianProfiles.AnyAsync(g => g.AcademyId == t.AcademyId && g.Id == guardian && g.IsActive) || !await Own(t.AcademyId, guardian.Value).AnyAsync())) throw new CashRejected("الأسرة غير متاحة في الفروع المصرح بها.", 404);
        return (t, user, guardian ?? Guid.Empty);
    }
    public async Task<object> StaffFamilies(string? search)
    {
        var (t, _, _) = await StaffContext(); var term = search?.Trim();
        if (term?.Length > 100) throw new CashRejected("البحث حتى100حرف.", 422);
        if (string.IsNullOrEmpty(term) || term.Length < 3) return new { items = Array.Empty<object>() };
        var phoneSearch = term.All(c => char.IsAsciiDigit(c) || c == '+'); var phone = EgyptPhoneNormalizer.Normalize(term);
        if (phoneSearch && phone is null) return new { items = Array.Empty<object>() };
        return new { items = await db.GuardianProfiles.AsNoTracking().Where(g => g.AcademyId == t.AcademyId && g.IsActive &&
            (phoneSearch ? g.ContactPhone == phone || g.User.PhoneNumber == phone : g.DisplayName.Contains(term)) &&
            db.SportEnrollments.Any(e => e.AcademyId == t.AcademyId && e.IsActive && e.Status == EnrollmentStatus.Active && e.Player.IsActive && e.Branch.IsActive && e.Sport.IsActive && db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.GuardianId == g.Id && l.PlayerId == e.PlayerId && l.IsActive)))
            .OrderBy(g => g.DisplayName).ThenBy(g => g.Id).Take(30).Select(g => new { g.Id, name = g.DisplayName, phone = g.ContactPhone ?? g.User.PhoneNumber }).ToListAsync() };
    }
    public async Task<object> StaffOptions(Guid guardian) => await OptionsFor(await StaffContext(guardian));
    internal async Task<(object View, string Hash, List<DiscountRules.PricingLine> Lines, Dictionary<Guid, RenewalFinancialSnapshot> Prices, Guid Guardian, Guid User)> PrepareStaff(Guid guardian, FamilyRenewalWrite body) => await PrepareFor(body, await StaffContext(guardian));
    public async Task<object> StaffQuote(Guid guardian, FamilyRenewalWrite body)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        return (await PrepareFor(body, await StaffContext(guardian))).View;
    }
    public async Task<object> Options()
    {
        return await OptionsFor(await Context());
    }
    private async Task<object> OptionsFor((TenantMembership Tenant, Guid User, Guid Guardian) context)
    {
        var (t, _, guardian) = context;
        var enrollments = await Own(t.AcademyId, guardian).Include(e => e.Player).Include(e => e.Sport).Include(e => e.Branch).OrderBy(e => e.Player.ArabicName).ThenBy(e => e.Sport.ArabicName).ToListAsync();
        var sports = enrollments.Select(e => e.SportId).Distinct().ToArray();
        var plans = await Available(t.AcademyId).Where(p => sports.Contains(p.SportId)).Include(p => p.BranchPublications).ThenInclude(p => p.Branch).OrderBy(p => p.DisplayOrder).ThenBy(p => p.ArabicName).ToListAsync();
        return new { items = enrollments.Select(e => new { id = e.Id, e.PlayerId, player = e.Player.ArabicName, sport = e.Sport.ArabicName, branch = e.Branch.ArabicName, plans = plans.Where(p => p.SportId == e.SportId && PlanCatalog.FormatMatches(e.RegistrationFormat, p.TrainingFormat) && (p.AllBranches || p.BranchPublications.Any(b => b.BranchId == e.BranchId && b.IsActive && b.Branch.IsActive))).Select(p => new { p.Id, name = p.ArabicName, amount = CashRegisterService.Money(p.Price), p.Currency, p.Revision }).ToArray() }).ToArray() };
    }
    public async Task<object> Quote(FamilyRenewalWrite body)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        return (await Prepare(body)).View;
    }
    internal async Task<(object View, string Hash, List<DiscountRules.PricingLine> Lines, Dictionary<Guid, RenewalFinancialSnapshot> Prices, Guid Guardian, Guid User)> Prepare(FamilyRenewalWrite body)
    {
        return await PrepareFor(body, await Context());
    }
    private async Task<(object View, string Hash, List<DiscountRules.PricingLine> Lines, Dictionary<Guid, RenewalFinancialSnapshot> Prices, Guid Guardian, Guid User)> PrepareFor(FamilyRenewalWrite body, (TenantMembership Tenant, Guid User, Guid Guardian) context)
    {
        var (t, user, guardian) = context;
        if (body.Lines is not { Count: > 0 and <= 20 } choices || choices.Any(x => x is null || x.SportEnrollmentId == Guid.Empty || x.SubscriptionPlanId == Guid.Empty) || choices.Select(x => x.SportEnrollmentId).Distinct().Count() != choices.Count) throw new CashRejected("اختر من تسجيل واحد إلى 20 تسجيلًا، وباقة واحدة لكل تسجيل دون تكرار.", 422);
        // Caller owns the snapshot transaction, including when creating a family request.
        var ids = choices.Select(x => x.SportEnrollmentId).ToArray();
        var enrollments = await Own(t.AcademyId, guardian).Where(e => ids.Contains(e.Id)).Include(e => e.Player).Include(e => e.Sport).Include(e => e.Branch).ToDictionaryAsync(e => e.Id);
        if (enrollments.Count != ids.Length) throw new CashRejected("أحد التسجيلات غير متاح لأبنائك. حدّث القائمة قبل المتابعة.", 404);
        var planIds = choices.Select(x => x.SubscriptionPlanId).Distinct().ToArray();
        var plans = await Available(t.AcademyId).Where(p => planIds.Contains(p.Id)).Include(p => p.BranchPublications).ThenInclude(p => p.Branch).ToDictionaryAsync(p => p.Id);
        List<DiscountRules.PricingLine> pricing = [];
        foreach (var choice in choices.OrderBy(x => x.SportEnrollmentId))
        {
            var e = enrollments[choice.SportEnrollmentId];
            if (!plans.TryGetValue(choice.SubscriptionPlanId, out var p) || p.SportId != e.SportId || !PlanCatalog.FormatMatches(e.RegistrationFormat, p.TrainingFormat) || !p.AllBranches && !p.BranchPublications.Any(b => b.BranchId == e.BranchId && b.IsActive && b.Branch.IsActive)) throw new CashRejected("إحدى الباقات غير متاحة لهذا التسجيل والفرع. اختر باقة من القائمة الحالية.", 422);
            pricing.Add(new(e.Id, e.PlayerId, e.BranchId, p));
        }
        if (pricing.Select(x => x.Plan.Currency).Distinct().Count() != 1) throw new CashRejected("اختر باقات بعملة واحدة لتجديدها معًا. لا تُجمع عملات مختلفة.", 422);
        var quotes = await discounts.CalculateManyAsync(t.AcademyId, user, pricing, guardian);
        var original = quotes.Values.Sum(x => x.OriginalAmount); var discount = quotes.Values.Sum(x => x.DiscountAmount); var total = quotes.Values.Sum(x => x.FinalAmount);
        if (total > 999999999m) throw new CashRejected("إجمالي الطلب كبير جدًا. قسّم التسجيلات إلى طلبات أصغر.", 422);
        var currency = pricing[0].Plan.Currency;
        var hash = StatementComparisonRules.Hash(new { academy = t.AcademyId, user, guardian, day = clock.Today, currency, lines = pricing.Select(x => new { x.EnrollmentId, x.PlayerId, x.BranchId, enrollments[x.EnrollmentId].RegistrationFormat, x.Plan.Id, x.Plan.Revision, x.Plan.Currency, terms = PlanTerms.From(x.Plan), quotes[x.EnrollmentId].OriginalAmount, quotes[x.EnrollmentId].DiscountType, quotes[x.EnrollmentId].DiscountValue, quotes[x.EnrollmentId].DiscountAmount, quotes[x.EnrollmentId].FinalAmount, quotes[x.EnrollmentId].DiscountRuleId, quotes[x.EnrollmentId].DiscountRuleRevision }).ToArray() });
        object view = new { quoteHash = hash, pricingDate = clock.Today, currency, originalAmount = CashRegisterService.Money(original), discountAmount = CashRegisterService.Money(discount), totalAmount = CashRegisterService.Money(total), items = pricing.Select(x => new { sportEnrollmentId = x.EnrollmentId, subscriptionPlanId = x.Plan.Id, player = enrollments[x.EnrollmentId].Player.ArabicName, sport = enrollments[x.EnrollmentId].Sport.ArabicName, branch = enrollments[x.EnrollmentId].Branch.ArabicName, plan = x.Plan.ArabicName, originalAmount = CashRegisterService.Money(quotes[x.EnrollmentId].OriginalAmount), discountAmount = CashRegisterService.Money(quotes[x.EnrollmentId].DiscountAmount), finalAmount = CashRegisterService.Money(quotes[x.EnrollmentId].FinalAmount), discountReason = quotes[x.EnrollmentId].DiscountReason, x.Plan.Currency, planRevision = x.Plan.Revision, discountRuleRevision = quotes[x.EnrollmentId].DiscountRuleRevision }).ToArray() };
        return (view, hash, pricing, quotes, guardian, user);
    }
}
