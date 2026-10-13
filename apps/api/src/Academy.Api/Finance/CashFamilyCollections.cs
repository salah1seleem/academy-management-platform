using Academy.Api.Configuration;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice3;
using Academy.Api.Slice4;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public sealed record CashFamilyQuote(Guid GuardianId, IReadOnlyList<FamilyRenewalChoice>? Lines);
public sealed record CashFamilyCollect(Guid GuardianId, IReadOnlyList<FamilyRenewalChoice>? Lines, string QuoteHash);

public sealed partial class CashRegisterService
{
    // Receipt postings already provide immutable financial/idempotency sources. A family
    // cash operation is a single transaction of postings, not a pending transfer request.
    private static void FamilyChoices(IReadOnlyList<FamilyRenewalChoice>? lines)
    {
        if (lines is not { Count: > 0 and <= 20 } || lines.Any(l => l is null || l.SportEnrollmentId == Guid.Empty || l.SubscriptionPlanId == Guid.Empty) || lines.Select(l => l.SportEnrollmentId).Distinct().Count() != lines.Count)
            throw new CashRejected("اختر من تسجيل واحد إلى20تسجيلًا وباقة لكل تسجيل دون تكرار.", 422);
    }
    private async Task FamilyCashEligible(Guid id, TenantMembership t, Guid user, IReadOnlyList<DiscountRules.PricingLine> lines, Dictionary<Guid, RenewalFinancialSnapshot> prices,VerifiedCashFamily? external=null)
    {
        var (session, register) = await CollectingSession(id, t);
        external?.Check(new(t,user,id,session.BranchId),external.Guardian,true,time.GetUtcNow());
        if (lines.Any(l => l.Plan.Currency != register.Currency || external is null && t.Role != AcademyRole.AcademyOwner && l.BranchId != session.BranchId)) throw new CashRejected("اختر تسجيلات من فرع الوردية وبعملتها. جدد الفروع الأخرى بعمليات منفصلة.", 422);
        if (prices.Values.Any(p => p.FinalAmount <= 0)) throw new CashRejected("الإعفاء الكامل يحتاج مساره المستقل؛ لا يمكن إنشاء قبض نقدي صفري.", 422);
        var day = await AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
        foreach (var line in lines)
        {
            var e = await db.SportEnrollments.AsNoTracking().SingleAsync(e => e.Id == line.EnrollmentId);
            if (!await db.TrainingGroups.AnyAsync(g => g.Id == FoundationDbContext.AssignedGroupAt(t.AcademyId, e.Id, day) && g.IsActive && g.BranchId == e.BranchId && g.SportId == e.SportId)) throw new CashRejected("أسند كل تسجيل لمجموعة فعالة مناسبة قبل التحصيل.");
        }
        var ids = lines.Select(l => l.EnrollmentId).ToArray();
        if (await db.FamilyRenewalLines.IgnoreQueryFilters().AnyAsync(l => l.AcademyId == t.AcademyId && ids.Contains(l.SportEnrollmentId) && l.Request.Status == FamilyRenewalStatus.Pending) || await db.RenewalRequests.IgnoreQueryFilters().AnyAsync(r => r.AcademyId == t.AcademyId && ids.Contains(r.SportEnrollmentId) && (r.Status == RenewalRequestStatus.PendingPayment || r.Status == RenewalRequestStatus.PaymentInProgress) && db.PaymentRequests.IgnoreQueryFilters().Any(p => p.AcademyId == t.AcademyId && p.Id == r.PaymentRequestId && (p.Status == PaymentRequestStatus.Pending || p.Status == PaymentRequestStatus.Created))))
            throw new CashRejected("أحد التسجيلات له طلب دفع مفتوح. أكمله أو ألغِه قبل التحصيل الجماعي.");
    }
    private async Task<string> FamilyCashHash(Guid session, string familyHash, IReadOnlyList<DiscountRules.PricingLine> lines, Guid academy)
    {
        var day=await AttendanceCalendar.TodayAsync(academy,db,time);var ids=lines.Select(l=>l.EnrollmentId).ToArray();
        var enrollments=await db.SportEnrollments.AsNoTracking().Where(e=>ids.Contains(e.Id)).OrderBy(e=>e.Id).Select(e=>new{e.Id,e.Version,e.BranchId,e.Status,e.TrainingGroupId}).ToArrayAsync();
        var groups=new List<object>();
        foreach(var e in enrollments)
        {
            var assigned=await db.SportEnrollments.Where(x=>x.Id==e.Id).Select(x=>FoundationDbContext.AssignedGroupAt(academy,x.Id,day)).SingleAsync();
            var version=await db.TrainingGroups.Where(g=>g.Id==assigned).Select(g=>(uint?)g.Version).SingleOrDefaultAsync();
            groups.Add(new{e.Id,assigned,version});
        }
        var service=await db.SubscriptionPeriods.AsNoTracking().Where(p=>ids.Contains(p.SportEnrollmentId)).OrderBy(p=>p.Id)
            .Select(p=>new{p.Id,p.SportEnrollmentId,p.Version,p.StartDate,p.EndDate,p.Status,p.RemainingSessions}).ToArrayAsync();
        return StatementComparisonRules.Hash(new{session,familyHash,enrollments,groups,service});
    }
    public Task<object> FamilyQuote(Guid id, CashFamilyQuote body, FamilyRenewalQuoteService family)=>FamilyQuoteCore(id,body,family,null);
    internal Task<object> ExternalFamilyQuote(Guid id,CashFamilyQuote body,FamilyRenewalQuoteService family,VerifiedCashFamily external)=>FamilyQuoteCore(id,body,family,external);
    private async Task<object> FamilyQuoteCore(Guid id, CashFamilyQuote body, FamilyRenewalQuoteService family,VerifiedCashFamily? external)
    {
        FamilyChoices(body.Lines); var (t, user) = await Context(write: true); await CollectionPermissions(t, true);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        if(external is not null&&external.Guardian!=body.GuardianId)throw new CashRejected("مرجع الأسرة غير متاح.",403);
        var q = await family.PrepareStaff(body.GuardianId, new(body.Lines)); await FamilyCashEligible(id, t, user, q.Lines, q.Prices,external);
        return new { quoteHash = await FamilyCashHash(id, q.Hash, q.Lines, t.AcademyId), family = q.View };
    }
    private async Task TryFamilyLock(string resource)
    {
        // Never wait while holding a different plan/shift/enrollment lock. Existing
        // individual paths acquire these in different orders; fail safely for retry.
        if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({resource},0)) AS \"Value\"").SingleAsync()) throw new CashRejected("تُراجع عملية أخرى أحد التسجيلات أو الوردية. أعد المحاولة دون تغيير معرف العملية.");
    }
    public Task<object> CollectFamily(Guid id, CashFamilyCollect body, string key, FamilyRenewalQuoteService family, CollectionService collections)=>CollectFamilyCore(id,body,key,family,collections,null);
    internal Task<object> CollectExternalFamily(Guid id,CashFamilyCollect body,string key,FamilyRenewalQuoteService family,CollectionService collections,VerifiedCashFamily external)=>CollectFamilyCore(id,body,key,family,collections,external);
    private async Task<object> CollectFamilyCore(Guid id, CashFamilyCollect body, string key, FamilyRenewalQuoteService family, CollectionService collections,VerifiedCashFamily? external)
    {
        FamilyChoices(body.Lines); if (body.QuoteHash?.Length != 64) throw new CashRejected("راجع إجمالي الأسرة قبل التحصيل.", 422);
        var (t, user) = await Context(write: true); await CollectionPermissions(t, true); var choices = body.Lines!;
        if(external is not null&&external.Guardian!=body.GuardianId)throw new CashRejected("مرجع الأسرة غير متاح.",403);
        await using var tx = await db.Database.BeginTransactionAsync(); var request = await Request(t.AcademyId, user, "familycollect", key);
        var prefix = "family-" + AcademyConfigurationService.Hash(request) + ":";
        var payload = StatementComparisonRules.Hash(new { id, body.GuardianId, body.QuoteHash, lines = choices.OrderBy(l => l.SportEnrollmentId).ToArray() });
        var prior = await db.CashReceiptPostings.Include(p => p.Receipt).Where(p => p.AcademyId == t.AcademyId && p.RequestKey.StartsWith(prefix)).OrderBy(p => p.RequestKey).ToListAsync();
        if (prior.Count > 0)
        {
            if (prior.Count != choices.Count || prior.Any(p => p.ActorUserId != user || p.SessionId != id || p.RequestHash != payload)) throw new CashRejected("معرف العملية مستخدم بتفاصيل أخرى أو يحتاج مراجعة سجل التحصيل.");
            return FamilyCashResult(prior, true);
        }
        if(external is not null)external.Check(new(t,user,id,(await Session(id)).BranchId),body.GuardianId,true,time.GetUtcNow());
        foreach (var plan in choices.Select(l => l.SubscriptionPlanId).Distinct().Order()) await TryFamilyLock($"plan-publication:{t.AcademyId}:{plan}");
        await TryFamilyLock($"cash:{t.AcademyId}:session:{id}");
        foreach (var enrollment in choices.Select(l => l.SportEnrollmentId).Order()) await TryFamilyLock($"renewal-enrollment:{t.AcademyId:N}:{enrollment:N}");
        foreach (var enrollment in choices.Select(l => l.SportEnrollmentId).Order()) await TryFamilyLock($"collection-enrollment:{t.AcademyId}:{enrollment}");
        foreach (var enrollment in choices.Select(l => l.SportEnrollmentId).Order()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SportEnrollments\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\"={enrollment} FOR UPDATE");
        var q = await family.PrepareStaff(body.GuardianId, new(body.Lines)); await FamilyCashEligible(id, t, user, q.Lines, q.Prices,external);
        if (await FamilyCashHash(id, q.Hash, q.Lines, t.AcademyId) != body.QuoteHash) throw new CashRejected("تغير الاشتراك أو السعر أو الأهلية أو التسكين. حدّث المراجعة ثم أكد المبلغ الجديد.");
        var session = await Session(id); List<CashReceiptPosting> postings = [];
        foreach (var line in q.Lines)
        {
            var payment = await collections.CreateCashAsync(t.AcademyId, user, line.EnrollmentId, line.Plan.Id, "cash-family-" + AcademyConfigurationService.Hash(request + line.EnrollmentId), "Cash", fixedSnapshot: q.Prices[line.EnrollmentId], fixedTerms: PlanTerms.From(line.Plan));
            var receipt = await db.Receipts.SingleAsync(r => r.Collection.PaymentRequestId == payment); var at = time.GetUtcNow();
            var posting = new CashReceiptPosting { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SessionId = id, BranchId = session.BranchId, CollectionId = receipt.CollectionId, ReceiptId = receipt.Id, Receipt = receipt, Amount = receipt.Amount, Currency = receipt.Currency, ActorUserId = user, RequestKey = prefix + line.EnrollmentId.ToString("N"), RequestHash = payload, CreatedAtUtc = at, UpdatedAtUtc = at };
            postings.Add(posting); db.CashReceiptPostings.Add(posting);
        }
        await db.SaveChangesAsync(); await tx.CommitAsync(); return FamilyCashResult(postings, false);
    }
    private static object FamilyCashResult(List<CashReceiptPosting> postings, bool replay) => new { replay, amount = Money(postings.Sum(p => p.Amount)), currency = postings[0].Currency, items = postings.Select(p => new { receiptId = p.ReceiptId, postingId = p.Id, player = p.Receipt.PlayerNameSnapshot, sport = p.Receipt.SportNameSnapshot, amount = Money(p.Amount), p.Currency }).ToArray() };
}
