using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public static class ReceivableEndpoints
{
    public static void MapReceivables(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/finance/receivables").RequireAuthorization(AcademyPermissions.ReportsFinancialRead);
        api.MapGet("/", Read); api.MapGet("/export", Export).RequireAuthorization(AcademyPermissions.ReportsExport);
        api.MapGet("/options", async (string? search, CurrentTenant tenant, FoundationDbContext db) => {
            var t = (await tenant.ResolveAsync())!; var term = search?.Trim();
            var enrollments = db.SportEnrollments.Where(e => e.IsActive && (term == null || e.Player.ArabicName.Contains(term) || e.Player.PlayerCode.Contains(term)));
            return Results.Ok(new { enrollments = await enrollments.OrderBy(e => e.Player.ArabicName).Take(50).Select(e => new { e.Id, player = e.Player.ArabicName, branch = e.Branch.ArabicName, sport = e.Sport.ArabicName }).ToListAsync() });
        });
        api.MapPost("/", Create).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/{id:guid}/movements", Move).AddEndpointFilter<CsrfFilter>();
        api.MapGet("/{id:guid}", async (Guid id, CurrentTenant tenant, FoundationDbContext db) => {
            var t = (await tenant.ResolveAsync())!;
            var row = await db.ReceivableDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id);
            return row is null ? Results.NotFound() : Results.Ok(new { document = row, movements = await db.ReceivableMovements.AsNoTracking().Where(x => x.DocumentId == id).OrderBy(x => x.CreatedAtUtc).ToListAsync() });
        });
        api.MapGet("/{id:guid}/collections", async (Guid id, CurrentTenant tenant, FoundationDbContext db) => {
            var t = (await tenant.ResolveAsync())!; var doc = await db.ReceivableDocuments.SingleOrDefaultAsync(x => x.Id == id);
            if (doc is null) return Results.NotFound();
            var rows = await db.Collections.Where(c => c.SportEnrollmentId == doc.SportEnrollmentId && c.Currency == doc.Currency)
                .Select(c => new { c.Id, c.ConfirmedAtUtc, c.Amount, available = c.Amount - (db.ReceivableMovements.Where(m => m.CollectionId == c.Id).Sum(m => (decimal?)m.Amount) ?? 0) - (db.CollectionRefunds.Where(r => r.CollectionId == c.Id).Sum(r => (decimal?)r.Amount) ?? 0) })
                .Where(c => c.available > 0).OrderByDescending(c => c.ConfirmedAtUtc).ToListAsync();
            return Results.Ok(rows.Select(c => new { c.Id, c.ConfirmedAtUtc, amount = Money(c.Amount), available = Money(c.available) }));
        });
        app.MapGet("/api/v1/guardian/receivables", async (CurrentTenant tenant, ClaimsPrincipal actor, FoundationDbContext db, ISubscriptionClock clock) => {
            var t = (await tenant.ResolveAsync())!; var user = Actor(actor);
            var enrollments = db.SportEnrollments.Where(e => db.GuardianPlayerLinks.Any(l => l.PlayerId == e.PlayerId && l.IsActive && l.Guardian.UserId == user)).Select(e => e.Id);
            var instant = clock.UtcNow;
            var accounting = await AccountingContext(db, t.AcademyId, instant);
            var rows = await Rows(db, t.AcademyId, instant, accounting.Day, null, null, null, enrollments).ToListAsync();
            return Results.Ok(Summary(rows, instant, accounting.Currency));
        }).RequireAuthorization(AcademyPermissions.GuardianChildrenRead);
    }
    public sealed record ChargeWrite(Guid SportEnrollmentId, Guid? PlanId, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Amount, DateOnly DueDate, ReceivableSource Source, string Description);
    public sealed record MovementWrite(ReceivableMovementKind Kind, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Amount, Guid? CollectionId, string Reason);
    public sealed class BalanceRow
    {
        public Guid Id { get; init; } public Guid EnrollmentId { get; init; } public Guid PlayerId { get; init; }
        public required string Player { get; init; } public Guid BranchId { get; init; } public required string Branch { get; init; }
        public required string Description { get; init; } public required string Currency { get; init; }
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public decimal Charge { get; init; }
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public decimal Credits { get; init; }
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public decimal Allocated { get; init; }
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public decimal Outstanding { get; init; }
        public DateOnly DueDate { get; init; } public bool Overdue { get; init; } public required string Source { get; init; }
    }
    private static IQueryable<BalanceRow> Rows(FoundationDbContext db, Guid academy, DateTimeOffset asOf, DateOnly day, Guid? branch, Guid? player, string? state, IQueryable<Guid>? own = null)
    {
        var query = db.ReceivableDocuments.AsNoTracking().Where(d => d.AcademyId == academy && d.CreatedAtUtc <= asOf);
        if (branch.HasValue) query = query.Where(d => d.BranchId == branch);
        if (player.HasValue) query = query.Where(d => d.PlayerId == player);
        if (own is not null) query = query.Where(d => own.Contains(d.SportEnrollmentId));
        var values = from d in query
                     let credits = db.ReceivableMovements.Where(m => m.DocumentId == d.Id && m.Kind == ReceivableMovementKind.CreditNote && m.CreatedAtUtc <= asOf).Sum(m => (decimal?)m.Amount) ?? 0
                     let paid = db.ReceivableMovements.Where(m => m.DocumentId == d.Id && m.Kind == ReceivableMovementKind.PaymentAllocation && m.CreatedAtUtc <= asOf).Sum(m => (decimal?)m.Amount) ?? 0
                     let outstanding = d.Amount - credits - paid
                     join b in db.Branches on d.BranchId equals b.Id
                     select new BalanceRow { Id = d.Id, EnrollmentId = d.SportEnrollmentId, PlayerId = d.PlayerId, Player = d.PlayerNameSnapshot, BranchId = d.BranchId, Branch = b.ArabicName, Description = d.Description, Currency = d.Currency, Charge = d.Amount, Credits = credits, Allocated = paid, Outstanding = outstanding, DueDate = d.DueDate, Overdue = outstanding > 0 && d.DueDate < day, Source = d.Source.ToString() };
        return state switch { "overdue" => values.Where(x => x.Overdue), "future" => values.Where(x => x.Outstanding > 0 && x.DueDate >= day), "credit" => values.Where(x => x.Outstanding < 0), "settled" => values.Where(x => x.Outstanding == 0), "open" => values.Where(x => x.Outstanding > 0), _ => values };
    }
    private static string Money(decimal value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    private static object Summary(IReadOnlyCollection<BalanceRow> rows, DateTimeOffset asOf, string currency) => new { asOf, currency, count = rows.Count, outstanding = Money(rows.Sum(x => Math.Max(0, x.Outstanding))), overdue = Money(rows.Where(x => x.Overdue).Sum(x => x.Outstanding)), notYetDue = Money(rows.Where(x => !x.Overdue).Sum(x => Math.Max(0, x.Outstanding))), overpaymentCredit = Money(rows.Sum(x => Math.Max(0, -x.Outstanding))), items = rows };
    private static async Task<(DateOnly Day, string Currency)> AccountingContext(FoundationDbContext db, Guid academy, DateTimeOffset time)
    {
        var settings = await db.Academies.Where(x => x.Id == academy).Select(x => new { x.TimeZone, x.DefaultCurrency }).SingleAsync();
        return (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZone)).DateTime), settings.DefaultCurrency);
    }
    private static async Task<IResult> Read(Guid? branchId, Guid? playerId, string? state, DateTimeOffset? asOf, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var instant = asOf ?? clock.UtcNow;
        if (instant > clock.UtcNow || instant.Year < 2000) return Results.Problem(statusCode: 422, title: "تاريخ الرصيد غير صالح");
        var accounting = await AccountingContext(db, t.AcademyId, instant);
        var rows = await Rows(db, t.AcademyId, instant, accounting.Day, branchId, playerId, state).OrderBy(x => x.DueDate).ThenBy(x => x.Id).ToListAsync();
        return Results.Ok(Summary(rows, instant, accounting.Currency));
    }
    private static async Task<IResult> Export(Guid? branchId, Guid? playerId, string? state, DateTimeOffset? asOf, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var instant = asOf ?? clock.UtcNow;
        if (instant > clock.UtcNow || instant.Year < 2000) return Results.Problem(statusCode: 422, title: "تاريخ الرصيد غير صالح");
        var accounting = await AccountingContext(db, t.AcademyId, instant);
        var rows = await Rows(db, t.AcademyId, instant, accounting.Day, branchId, playerId, state).OrderBy(x => x.DueDate).ThenBy(x => x.Id).ToListAsync();
        static string Cell(string value) => "\"" + (value.TrimStart() is { Length: > 0 } trimmed && "=+-@".Contains(trimmed[0]) || value.StartsWith('\t') || value.StartsWith('\r') ? "'" : "") + value.Replace("\"", "\"\"") + "\"";
        var lines = new List<string> { "اللاعب,الفرع,الوصف,العملة,المطالبة,الدائن,المخصص,القائم,تاريخ الاستحقاق,متأخر" };
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        lines.AddRange(rows.Select(x => string.Join(',', Cell(x.Player), Cell(x.Branch), Cell(x.Description), x.Currency, x.Charge.ToString("0.00", culture), x.Credits.ToString("0.00", culture), x.Allocated.ToString("0.00", culture), x.Outstanding.ToString("0.00", culture), x.DueDate.ToString("yyyy-MM-dd"), x.Overdue ? "نعم" : "لا")));
        return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join('\n', lines))).ToArray(), "text/csv; charset=utf-8", "receivables.csv");
    }
    private static bool Valid(decimal amount) => amount > 0 && amount <= 999999999m && amount == FinancialAmounts.Round(amount);
    private static Guid Actor(ClaimsPrincipal actor) => Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static string Hash(object body) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(body)));
    private static string? Key(HttpContext http, Guid actor)
    {
        var key = http.Request.Headers["Idempotency-Key"].ToString();
        return key.Length is >= 8 and <= 100 ? $"{actor:N}:{key}" : null;
    }
    private static async Task<bool> MayWrite(TenantMembership t, FoundationDbContext db) => t.Role == AcademyRole.AcademyOwner || t.Role == AcademyRole.AcademyAdmin && await db.AcademyMemberships.AnyAsync(x => x.Id == t.MembershipId && x.UseActionGrants);
    private static async Task<IResult> Create(ChargeWrite body, HttpContext http, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var actor = Actor(http.User); var key = Key(http, actor);
        if (!await MayWrite(t, db)) return Results.Forbid();
        if (!Valid(body.Amount) || key is null || !Enum.IsDefined(body.Source) || string.IsNullOrWhiteSpace(body.Description) || body.Description.Length > 400 || body.DueDate.Year is < 2000 or > 2100) return Results.Problem(statusCode: 422, title: "راجع مبلغ المطالبة ووصفها وتاريخها ومعرف الطلب");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"receivable:{t.AcademyId}:{key}"}, 0))");
        var hash = Hash(body); var replay = await db.ReceivableDocuments.SingleOrDefaultAsync(x => x.SourceKey == key);
        if (replay is not null) return replay.ContentHash == hash ? Results.Ok(new { replay.Id, replay = true }) : Results.Conflict(new { message = "معرف الطلب مستخدم بمحتوى مختلف." });
        var e = await db.SportEnrollments.AsNoTracking().Include(x => x.Player).SingleOrDefaultAsync(x => x.Id == body.SportEnrollmentId && x.IsActive);
        if (e is null) return Results.NotFound();
        var plan = body.PlanId.HasValue ? await db.SubscriptionPlans.SingleOrDefaultAsync(x => x.Id == body.PlanId && x.SportId == e.SportId) : null;
        if (body.PlanId.HasValue && plan is null) return Results.NotFound();
        var accounting = await AccountingContext(db, t.AcademyId, clock.UtcNow);
        if (plan is not null && plan.Currency != accounting.Currency) return Results.Problem(statusCode: 422, title: "عملة الباقة لا تطابق عملة الأكاديمية");
        var row = new ReceivableDocument { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SportEnrollmentId = e.Id, PlayerId = e.PlayerId, PlayerNameSnapshot = e.Player.ArabicName, BranchId = e.BranchId, PlanId = plan?.Id, PlanNameSnapshot = plan?.ArabicName, PlanPriceSnapshot = plan?.Price, Amount = body.Amount, Currency = accounting.Currency, DueDate = body.DueDate, Description = body.Description.Trim(), Source = body.Source, SourceKey = key, ContentHash = hash, ActorUserId = actor, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        db.ReceivableDocuments.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Created($"/api/v1/finance/receivables/{row.Id}", new { row.Id });
    }
    private static async Task<IResult> Move(Guid id, MovementWrite body, HttpContext http, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var actor = Actor(http.User); var key = Key(http, actor);
        if (!await MayWrite(t, db)) return Results.Forbid();
        if (!Valid(body.Amount) || key is null || !Enum.IsDefined(body.Kind) || string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length > 400 || (body.Kind == ReceivableMovementKind.PaymentAllocation) != body.CollectionId.HasValue) return Results.Problem(statusCode: 422, title: "راجع المبلغ ونوع الحركة وسببها ومصدر التحصيل");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"receivable:{t.AcademyId}:{key}"}, 0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"receivable-document:{t.AcademyId}:{id}"}, 0))");
        var hash = Hash(new { id, body }); var replay = await db.ReceivableMovements.SingleOrDefaultAsync(x => x.SourceKey == key);
        if (replay is not null) return replay.ContentHash == hash ? Results.Ok(new { replay.Id, replay = true }) : Results.Conflict(new { message = "معرف الطلب مستخدم بمحتوى مختلف." });
        var doc = await db.ReceivableDocuments.SingleOrDefaultAsync(x => x.Id == id); if (doc is null) return Results.NotFound();
        if (body.CollectionId.HasValue)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"receivable-collection:{t.AcademyId}:{body.CollectionId}"}, 0))");
            var c = await db.Collections.SingleOrDefaultAsync(x => x.Id == body.CollectionId && x.SportEnrollmentId == doc.SportEnrollmentId && x.Currency == doc.Currency);
            if (c is null) return Results.NotFound();
            var allocated = await db.ReceivableMovements.Where(x => x.CollectionId == c.Id).SumAsync(x => (decimal?)x.Amount) ?? 0;
            var refunded = await db.CollectionRefunds.Where(x => x.CollectionId == c.Id).SumAsync(x => (decimal?)x.Amount) ?? 0;
            if (allocated + body.Amount > c.Amount - refunded) return Results.Conflict(new { message = "المبلغ يتجاوز الرصيد غير المخصص من التحصيل المؤكد." });
        }
        else if (body.Amount + (await db.ReceivableMovements.Where(x => x.DocumentId == id && x.Kind == ReceivableMovementKind.CreditNote).SumAsync(x => (decimal?)x.Amount) ?? 0) > doc.Amount) return Results.Conflict(new { message = "إجمالي الإشعارات الدائنة لا يتجاوز أصل المطالبة." });
        var row = new ReceivableMovement { Id = Guid.NewGuid(), AcademyId = t.AcademyId, DocumentId = id, Kind = body.Kind, Amount = body.Amount, Currency = doc.Currency, CollectionId = body.CollectionId, Reason = body.Reason.Trim(), SourceKey = key, ContentHash = hash, ActorUserId = actor, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        db.ReceivableMovements.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Created($"/api/v1/finance/receivables/{id}", new { row.Id });
    }
}
