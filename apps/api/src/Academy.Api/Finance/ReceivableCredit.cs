using System.Security.Claims;
using System.Text.Json.Serialization;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public sealed record CreditReview(Guid SourceDocumentId, Guid TargetDocumentId, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Amount, string? Reason);
public sealed record CreditApprove(Guid SourceDocumentId, Guid TargetDocumentId, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Amount, string? Reason, string QuoteHash);
public static class ReceivableCreditEndpoints
{
    public static void MapReceivableCredit(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/finance/credit-applications").RequireAuthorization(AcademyPermissions.ReportsFinancialRead);
        api.MapGet("/{sourceId:guid}/options", async (Guid sourceId, ReceivableCreditService service) => await Run(() => service.Options(sourceId)));
        api.MapPost("/quote", async (CreditReview body, ReceivableCreditService service) => await Run(() => service.Quote(body))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/approve", async (CreditApprove body, HttpRequest request, ReceivableCreditService service) => await Run(() => service.Approve(body, request.Headers["Idempotency-Key"].ToString()))).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Run(Func<Task<object>> work) { try { return Results.Ok(await work()); } catch (CashRejected e) { return Results.Problem(title: e.Message, statusCode: e.Status); } }
}

public sealed class ReceivableCreditService(FoundationDbContext db, CurrentTenant tenant, IHttpContextAccessor accessor, ISubscriptionClock clock)
{
    private async Task<(TenantMembership Tenant, Guid Actor)> Context()
    {
        var t = await tenant.ResolveAsync() ?? throw new CashRejected("سجل الدخول أولًا.", 401);
        if (t.Role != AcademyRole.AcademyOwner) throw new CashRejected("استخدام الرصيد الدائن متاح للمالك فقط.", 403);
        var actor = Guid.Parse(accessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!await db.AcademyMemberships.AnyAsync(m => m.Id == t.MembershipId && m.AcademyId == t.AcademyId && m.UserId == actor && m.IsActive && m.Role == AcademyRole.AcademyOwner)) throw new CashRejected("عضوية المالك غير متاحة.", 403);
        return (t, actor);
    }
    private IQueryable<ReceivableEndpoints.BalanceRow> Balances(Guid academy) => ReceivableEndpoints.Rows(db, academy, clock.UtcNow, clock.Today, null, null, null);
    public async Task<object> Options(Guid sourceId)
    {
        var (t, _) = await Context(); var source = await Balances(t.AcademyId).SingleOrDefaultAsync(x => x.Id == sourceId) ?? throw new CashRejected("المطالبة غير متاحة.", 404);
        if (source.Outstanding >= 0) throw new CashRejected("لا يوجد رصيد دائن مثبت في هذه المطالبة.");
        var targets = await Balances(t.AcademyId).Where(x => x.PlayerId == source.PlayerId && x.Currency == source.Currency && x.Id != sourceId && x.Outstanding > 0).OrderBy(x => x.DueDate).ThenBy(x => x.Id).ToListAsync();
        return new { source, targets };
    }
    private sealed record Prepared(ReceivableEndpoints.BalanceRow Source, ReceivableEndpoints.BalanceRow Target, string Reason, string Hash);
    private async Task<Prepared> Prepare(TenantMembership t, Guid actor, CreditReview body)
    {
        var reason = body.Reason?.Trim();
        if (body.Amount <= 0 || body.Amount > 999999999m || body.Amount != FinancialAmounts.Round(body.Amount) || string.IsNullOrEmpty(reason) || reason.Length > 400 || body.SourceDocumentId == body.TargetDocumentId) throw new CashRejected("راجع المبلغ والسبب والمطالبتين المختلفتين.", 422);
        var rows = await Balances(t.AcademyId).Where(x => x.Id == body.SourceDocumentId || x.Id == body.TargetDocumentId).ToListAsync();
        var source = rows.SingleOrDefault(x => x.Id == body.SourceDocumentId); var target = rows.SingleOrDefault(x => x.Id == body.TargetDocumentId);
        if (source is null || target is null) throw new CashRejected("المطالبة غير متاحة.", 404);
        if (source.PlayerId != target.PlayerId || source.Currency != target.Currency) throw new CashRejected("استخدم الرصيد لنفس اللاعب والعملة فقط.", 422);
        if (body.Amount > -source.Outstanding || body.Amount > target.Outstanding) throw new CashRejected("المبلغ يتجاوز الرصيد الدائن أو القائم على المطالبة المختارة.");
        var hash = StatementComparisonRules.Hash(new { t.AcademyId, t.MembershipId, actor, source, target, body.Amount, reason });
        return new(source, target, reason, hash);
    }
    private static object View(Prepared q, decimal amount) => new { quoteHash = q.Hash, player = q.Source.Player, q.Source.Currency, amount = CashRegisterService.Money(amount), source = new { q.Source.Id, q.Source.Description, before = CashRegisterService.Money(q.Source.Outstanding), after = CashRegisterService.Money(q.Source.Outstanding + amount) }, target = new { q.Target.Id, q.Target.Description, before = CashRegisterService.Money(q.Target.Outstanding), after = CashRegisterService.Money(q.Target.Outstanding - amount) }, q.Reason };
    public async Task<object> Quote(CreditReview body)
    {
        var (t, actor) = await Context(); await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead); return View(await Prepare(t, actor, body), body.Amount);
    }
    public async Task<object> Approve(CreditApprove body, string key)
    {
        var (t, actor) = await Context();
        if (string.IsNullOrWhiteSpace(key) || key.Length is < 8 or > 80 || body.QuoteHash?.Length != 64) throw new CashRejected("راجع الرصيد قبل الاعتماد بمعرف طلب صحيح.", 422);
        var hash = StatementComparisonRules.Hash(new { body.SourceDocumentId, body.TargetDocumentId, body.Amount, reason = body.Reason?.Trim(), body.QuoteHash });
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"receivable-credit:{t.AcademyId}:{actor}:{key}"},0))");
        var prior = await db.ReceivableCreditApplications.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.ActorUserId == actor && x.RequestKey == key);
        if (prior != null) { if (prior.RequestHash != hash) throw new CashRejected("معرف الطلب مستخدم بتفاصيل أخرى."); return new { prior.Id, replay = true }; }
        // Same document locks as credit notes/payment allocations, in a stable order.
        foreach (var id in new[] { body.SourceDocumentId, body.TargetDocumentId }.Distinct().OrderBy(x => x))
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"receivable-document:{t.AcademyId}:{id}"},0))");
        var q = await Prepare(t, actor, new(body.SourceDocumentId, body.TargetDocumentId, body.Amount, body.Reason));
        if (q.Hash != body.QuoteHash) throw new CashRejected("تغير الرصيد. راجع القيم الجديدة قبل الاعتماد.");
        var row = new ReceivableCreditApplication { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SourceDocumentId = q.Source.Id, TargetDocumentId = q.Target.Id, ActorUserId = actor, ActorMembershipId = t.MembershipId, Amount = body.Amount, SourceBalanceBefore = q.Source.Outstanding, TargetBalanceBefore = q.Target.Outstanding, Currency = q.Source.Currency, Reason = q.Reason, RequestKey = key, RequestHash = hash, QuoteHash = q.Hash, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        db.ReceivableCreditApplications.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return new { row.Id, replay = false };
    }
}
