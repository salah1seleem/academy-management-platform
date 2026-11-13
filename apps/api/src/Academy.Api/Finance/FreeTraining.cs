using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public sealed record FreeTrainingReview(Guid SportEnrollmentId, Guid SubscriptionPlanId, string? Reason);
public sealed record FreeTrainingApprove(Guid SportEnrollmentId, Guid SubscriptionPlanId, string? Reason, string QuoteHash);

public static class FreeTrainingEndpoints
{
    public static void MapFreeTraining(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/finance/free-training").RequireAuthorization(AcademyPermissions.SubscriptionRead);
        api.MapGet("/{enrollmentId:guid}/options", async (Guid enrollmentId, FreeTrainingService s) => await Run(() => s.Options(enrollmentId)));
        api.MapGet("/{enrollmentId:guid}/attempt", async (Guid enrollmentId, HttpRequest request, FreeTrainingService service) =>
        {
            request.HttpContext.Response.Headers.CacheControl = "private, no-store";
            return await Run(() => service.Attempt(enrollmentId, request.Headers["Idempotency-Key"].ToString()));
        });
        api.MapPost("/quote", async (FreeTrainingReview body, FreeTrainingService s) => await Run(() => s.Quote(body))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/approve", async (FreeTrainingApprove body, HttpRequest request, FreeTrainingService s) => await Run(() => s.Approve(body, request.Headers["Idempotency-Key"].ToString()))).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Run(Func<Task<object>> work)
    {
        try { return Results.Ok(await work()); }
        catch (CashRejected error) { return Results.Problem(title: error.Message, statusCode: error.Status); }
    }
}

public sealed class FreeTrainingService(FoundationDbContext db, CurrentTenant tenant, IHttpContextAccessor accessor, ISubscriptionClock clock)
{
    private static readonly JsonSerializerOptions SourceJson = new() { Converters = { new JsonStringEnumConverter() } };
    private async Task<(TenantMembership Tenant, Guid Actor)> Context()
    {
        var t = await tenant.ResolveAsync() ?? throw new CashRejected("سجل الدخول أولًا.", 401);
        if (t.Role != AcademyRole.AcademyOwner) throw new CashRejected("اعتماد التدريب المجاني متاح للمالك فقط.", 403);
        var actor = Guid.Parse(accessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!await db.AcademyMemberships.AnyAsync(m => m.Id == t.MembershipId && m.AcademyId == t.AcademyId && m.UserId == actor && m.IsActive && m.Role == AcademyRole.AcademyOwner)) throw new CashRejected("عضوية المالك غير متاحة.", 403);
        return (t, actor);
    }
    private IQueryable<SportEnrollment> Eligible(Guid academy) => db.SportEnrollments.AsNoTracking().Where(e => e.AcademyId == academy && e.IsActive && e.Status == EnrollmentStatus.Active && e.Player.IsActive && e.Branch.IsActive && e.Sport.IsActive);
    public async Task<object> Options(Guid enrollmentId)
    {
        var (t, _) = await Context(); var e = await Eligible(t.AcademyId).Where(e => e.Id == enrollmentId).Select(e => new { e.Id, e.PlayerId, e.BranchId, e.SportId, e.RegistrationFormat, player = e.Player.ArabicName, sport = e.Sport.ArabicName, branch = e.Branch.ArabicName }).SingleOrDefaultAsync() ?? throw new CashRejected("التسجيل غير متاح.", 404);
        var plans = await db.SubscriptionPlans.TrainingOnly().ForBranch(e.BranchId).ForRegistrationFormat(e.RegistrationFormat).Where(p => p.AcademyId == t.AcademyId && p.IsActive && p.SportId == e.SportId).OrderBy(p => p.DisplayOrder).ThenBy(p => p.Id).Select(p => new { p.Id, p.ArabicName, p.Price, p.Currency }).ToListAsync();
        return new { enrollment = e, plans };
    }
    private sealed record Prepared(SportEnrollment Enrollment, SubscriptionPlan Plan, PlanTerms Terms, string Reason, DateOnly Today, DateOnly Start, DateOnly? End, string Hash);
    private async Task<Prepared> Prepare(TenantMembership t, Guid actor, FreeTrainingReview body)
    {
        var reason = body.Reason?.Trim(); if (string.IsNullOrEmpty(reason) || reason.Length > 500) throw new CashRejected("أدخل سبب الإعفاء حتى500حرف.", 422);
        var e = await Eligible(t.AcademyId).Include(e => e.Player).Include(e => e.Sport).Include(e => e.Branch).SingleOrDefaultAsync(e => e.Id == body.SportEnrollmentId) ?? throw new CashRejected("التسجيل غير متاح.", 404);
        var p = await db.SubscriptionPlans.AsNoTracking().TrainingOnly().ForBranch(e.BranchId).ForRegistrationFormat(e.RegistrationFormat).SingleOrDefaultAsync(p => p.AcademyId == t.AcademyId && p.Id == body.SubscriptionPlanId && p.SportId == e.SportId && p.IsActive) ?? throw new CashRejected("اختر باقة تدريب منشورة مناسبة للفرع ونوع التسجيل.", 422);
        if (await db.FamilyRenewalLines.IgnoreQueryFilters().AnyAsync(l => l.AcademyId == t.AcademyId && l.SportEnrollmentId == e.Id && l.Request.Status == FamilyRenewalStatus.Pending) || await db.RenewalRequests.IgnoreQueryFilters().AnyAsync(r => r.AcademyId == t.AcademyId && r.SportEnrollmentId == e.Id && (r.Status == RenewalRequestStatus.PendingPayment || r.Status == RenewalRequestStatus.PaymentInProgress))) throw new CashRejected("للتسجيل طلب دفع مفتوح. أكمله أو ألغِه قبل اعتماد الإعفاء.");
        var periods = await db.SubscriptionPeriods.AsNoTracking().Where(s => s.AcademyId == t.AcademyId && s.SportEnrollmentId == e.Id && s.IsActive && s.Status != SubscriptionPeriodStatus.Cancelled).OrderBy(s => s.Id).Select(s => new { s.Id, s.Version, s.Status, s.StartDate, s.EndDate, s.RemainingSessions }).ToListAsync();
        if (periods.Any(s => s.Status == SubscriptionPeriodStatus.Frozen || s.EndDate == null && s.RemainingSessions > 0)) throw new CashRejected("راجع الفترة المجمدة أو رصيد الحصص المفتوح قبل إضافة فترة جديدة.");
        var today = clock.Today;
        var latest = periods.Select(s => s.EndDate).Max(); var start = latest >= today ? latest!.Value.AddDays(1) : today;
        var terms = PlanTerms.From(p); var end = terms.EndDate(start);
        var hash = StatementComparisonRules.Hash(new { t.AcademyId, t.MembershipId, actor, e.Id, e.BranchId, e.SportId, e.RegistrationFormat, plan = p.Id, terms, p.Price, p.Currency, reason, day = today, start, end, periods });
        return new(e, p, terms, reason, today, start, end, hash);
    }
    public async Task<object> Quote(FreeTrainingReview body)
    {
        var (t, actor) = await Context(); await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var q = await Prepare(t, actor, body);
        return new { quoteHash = q.Hash, player = q.Enrollment.Player.ArabicName, sport = q.Enrollment.Sport.ArabicName, branch = q.Enrollment.Branch.ArabicName, plan = q.Terms.Name, waivedAmount = CashRegisterService.Money(q.Plan.Price), payable = "0.00", q.Plan.Currency, startDate = q.Start, endDate = q.End, sessions = q.Terms.Sessions, q.Reason };
    }
    private async Task TryLock(string resource)
    {
        if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({resource},0)) AS \"Value\"").SingleAsync()) throw new CashRejected("توجد عملية أخرى على التسجيل أو الباقة. أعد نفس المحاولة.");
    }
    private static bool ValidKey(string key) => !string.IsNullOrWhiteSpace(key) && key.Length is >= 8 and <= 80;
    private Task LockAttempt(Guid academyId, Guid actor, string key) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"free-training:{academyId}:{actor}:{key}"},0))");

    public async Task<object> Attempt(Guid enrollmentId, string key)
    {
        var (tenant, actor) = await Context();
        if (!ValidKey(key)) throw new CashRejected("تعذر تحديد محاولة الاعتماد.", 422);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await LockAttempt(tenant.AcademyId, actor, key);
        var prior = await db.FreeTrainingApprovals.AsNoTracking().SingleOrDefaultAsync(approval =>
            approval.AcademyId == tenant.AcademyId && approval.ActorUserId == actor &&
            approval.SportEnrollmentId == enrollmentId && approval.IdempotencyKey == key);
        if (prior is null) throw new CashRejected("لا يوجد اعتماد محفوظ لهذه المحاولة.", 404);
        return await Result(prior, true);
    }

    public async Task<object> Approve(FreeTrainingApprove body, string key)
    {
        var (t, actor) = await Context();
        if (!ValidKey(key) || body.QuoteHash?.Length != 64) throw new CashRejected("راجع الإعفاء ثم أعد الطلب بمعرف صحيح.", 422);
        var payload = StatementComparisonRules.Hash(new { body.SportEnrollmentId, body.SubscriptionPlanId, reason = body.Reason?.Trim(), body.QuoteHash });
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockAttempt(t.AcademyId, actor, key);
        var prior = await db.FreeTrainingApprovals.SingleOrDefaultAsync(a => a.AcademyId == t.AcademyId && a.ActorUserId == actor && a.IdempotencyKey == key);
        if (prior != null) { if (prior.RequestHash != payload) throw new CashRejected("معرف الاعتماد مستخدم بتفاصيل أخرى."); return await Result(prior, true); }
        await TryLock($"plan-publication:{t.AcademyId}:{body.SubscriptionPlanId}");
        await TryLock($"renewal-enrollment:{t.AcademyId:N}:{body.SportEnrollmentId:N}");
        await TryLock($"collection-enrollment:{t.AcademyId}:{body.SportEnrollmentId}");
        var q = await Prepare(t, actor, new(body.SportEnrollmentId, body.SubscriptionPlanId, body.Reason));
        if (q.Hash != body.QuoteHash) throw new CashRejected("تغير السعر أو الاشتراك أو الأهلية. حدّث المراجعة قبل الاعتماد.");
        var now = clock.UtcNow;
        var (approval,period) = Record(db,t,actor,q.Enrollment,q.Plan,q.Terms,q.Start,q.Today,q.Reason,key,payload,q.Hash,now);
        await db.SaveChangesAsync();
        var firstPeriod = !await db.SubscriptionPeriods.AnyAsync(p=>p.AcademyId==t.AcademyId&&p.SportEnrollmentId==period.SportEnrollmentId&&p.Id!=period.Id);
        await Academy.Api.Operations.GuardianLifecycleNotice.Add(db,t.AcademyId,period.SportEnrollmentId,firstPeriod?"Enrollment":"SingleRenewal",period.Id,"SubscriptionPeriod",period.Id,now);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Outcome(approval, q.Enrollment.PlayerId, period.Id, false);
    }
    internal static (FreeTrainingApproval Approval, SubscriptionPeriod Period) Record(FoundationDbContext db, TenantMembership t, Guid actor, SportEnrollment enrollment, SubscriptionPlan plan, PlanTerms terms, DateOnly start, DateOnly today, string reason, string key, string payload, string hash, DateTimeOffset now)
    {
        var approval = new FreeTrainingApproval { Id = Guid.NewGuid(), AcademyId = t.AcademyId, BranchId = enrollment.BranchId, SportEnrollmentId = enrollment.Id, SubscriptionPlanId = plan.Id, ActorUserId = actor, ActorMembershipId = t.MembershipId, WaivedAmount = plan.Price, Currency = plan.Currency, Reason = reason, IdempotencyKey = key, RequestHash = payload, QuoteHash = hash, StartDate = start, EndDate = terms.EndDate(start), InitialSessions = terms.Sessions, PlanTermsJson = JsonSerializer.Serialize(terms, SourceJson), CreatedAtUtc = now, UpdatedAtUtc = now };
        db.FreeTrainingApprovals.Add(approval);
        var period = new SubscriptionPeriod { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SportEnrollmentId = enrollment.Id, SportId = enrollment.SportId, SubscriptionPlanId = plan.Id, Origin = SubscriptionOrigin.ApprovedWaiver, FreeTrainingApproval = approval, StartDate = start, EndDate = terms.EndDate(start), InitialSessions = terms.Sessions, RemainingSessions = terms.Sessions, Status = start > today ? SubscriptionPeriodStatus.Scheduled : SubscriptionPeriodStatus.Active, PriceSnapshot = 0, CurrencySnapshot = plan.Currency, CreatedByUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now };
        terms.Capture(period); db.SubscriptionPeriods.Add(period);
        return (approval,period);
    }
    private static object Outcome(FreeTrainingApproval approval, Guid playerId, Guid periodId, bool replay) =>
        new { approval.Id, approval.SportEnrollmentId, playerId, periodId, replay, source = "ApprovedWaiver" };

    private async Task<object> Result(FreeTrainingApproval approval, bool replay)
    {
        var period = await db.SubscriptionPeriods.AsNoTracking()
            .Where(period => period.AcademyId == approval.AcademyId && period.FreeTrainingApprovalId == approval.Id)
            .Select(period => new { period.Id, period.SportEnrollment.PlayerId })
            .SingleAsync();
        return Outcome(approval, period.PlayerId, period.Id, replay);
    }
}
