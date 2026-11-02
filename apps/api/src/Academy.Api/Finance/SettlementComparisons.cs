using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public sealed record StatementLineWrite(Guid BranchId, string SourceKind, string AccountTag, string StatementReference, string LineReference,
    string Provider, string PaymentReference, DateOnly SettlementDate,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal GrossAmount,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal FeeAmount,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal NetAmount,
    string Currency, string? Note = null, string? QuoteHash = null);

public static class StatementComparisonRules
{
    public static string Compare(decimal gross, decimal fee, decimal net, string currency, decimal? expected, string? expectedCurrency, bool ambiguous = false) =>
        gross - fee != net ? "NetMismatch" : ambiguous ? "Ambiguous" : expected is null ? "Unmatched" :
        currency != expectedCurrency ? "CurrencyMismatch" : gross != expected ? "AmountMismatch" : "Matched";
    public static bool Money(decimal amount) => amount >= 0 && amount <= 999999999m && decimal.Round(amount, 2) == amount;
    public static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    public static string BodyHash(StatementLineWrite body) => Hash(new { body.BranchId, body.SourceKind, body.AccountTag, body.StatementReference, body.LineReference, body.Provider, body.PaymentReference, body.SettlementDate, gross = body.GrossAmount.ToString("0.00", CultureInfo.InvariantCulture), fee = body.FeeAmount.ToString("0.00", CultureInfo.InvariantCulture), net = body.NetAmount.ToString("0.00", CultureInfo.InvariantCulture), body.Currency, body.Note });
}

public static class SettlementComparisonEndpoints
{
    public static void MapSettlementComparisons(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/finance/settlement-comparisons").RequireAuthorization(AcademyPermissions.ReportsFinancialRead);
        api.MapGet("/", async (DateOnly? from, DateOnly? to, int? page, SettlementComparisonService service) => await Run(() => service.Read(from, to, page)));
        api.MapGet("/options", async (SettlementComparisonService service) => await Run(service.Options));
        api.MapGet("/import/template", async (SettlementComparisonService service) => await Run(service.ImportTemplate));
        api.MapPost("/preview", async (StatementLineWrite body, SettlementComparisonService service) => await Run(() => service.Preview(body))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/", async (StatementLineWrite body, SettlementComparisonService service) => await Run(() => service.Post(body))).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/import/preview", async (StatementImportWrite body, SettlementComparisonService service) => await Run(() => service.PreviewImport(body))).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("import");
        api.MapPost("/import", async (StatementImportWrite body, SettlementComparisonService service) => await Run(() => service.PostImport(body))).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("import");
        api.MapGet("/{id:guid}", async (Guid id, SettlementComparisonService service) => await Run(() => service.Detail(id)));
    }
    private static async Task<IResult> Run(Func<Task<object>> work)
    {
        try { var result = await work(); return result is IResult response ? response : Results.Ok(result); }
        catch (CashRejected e) { return Results.Problem(title: e.Message, statusCode: e.Status); }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Problem(title: "سطر الكشف أو التحصيل مطابق بالفعل. راجع السجل المحفوظ.", statusCode: 409); }
    }
}

public sealed partial class SettlementComparisonService(FoundationDbContext db, CurrentTenant tenant, IHttpContextAccessor accessor, TimeProvider time)
{
    private async Task<(TenantMembership Tenant, Guid User)> Context()
    {
        var t = await tenant.ResolveAsync() ?? throw new CashRejected("سجل الدخول أولًا.", 401);
        if (t.Role != AcademyRole.AcademyOwner) throw new CashRejected("مطابقة الكشف متاحة للمالك فقط.", 403);
        if (!Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var user)) throw new CashRejected("سجل الدخول أولًا.", 401);
        return (t, user);
    }
    public async Task<object> Options()
    {
        var (t, _) = await Context();
        return new { branches = await db.Branches.Where(b => b.AcademyId == t.AcademyId && b.IsActive).OrderBy(b => b.ArabicName).Select(b => new { b.Id, name = b.ArabicName }).ToListAsync(), providers = await db.Collections.Where(c => c.AcademyId == t.AcademyId && c.PaymentMethod == "Online").Select(c => c.Provider).Distinct().OrderBy(x => x).ToListAsync() };
    }
    public async Task<object> Read(DateOnly? from, DateOnly? to, int? page)
    {
        var (t, _) = await Context();
        if (from > to || page is < 1 or > 100000) throw new CashRejected("اختر فترة وصفحة صحيحتين.", 422);
        var query = db.SettlementComparisons.AsNoTracking().Where(x => x.AcademyId == t.AcademyId);
        if (from.HasValue) query = query.Where(x => x.SettlementDate >= from); if (to.HasValue) query = query.Where(x => x.SettlementDate <= to);
        var index = page ?? 1;
        return new { total = await query.CountAsync(), page = index, pageSize = 25, items = await query.OrderByDescending(x => x.SettlementDate).ThenByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip((index - 1) * 25).Take(25).Select(x => new { x.Id, x.SettlementDate, x.SourceKind, x.AccountTag, x.StatementReference, x.LineReference, x.Result, gross = CashRegisterService.Money(x.GrossAmount), fee = CashRegisterService.Money(x.FeeAmount), net = CashRegisterService.Money(x.NetAmount), x.Currency }).ToListAsync() };
    }
    public async Task<object> Detail(Guid id)
    {
        var (t, _) = await Context(); var row = await db.SettlementComparisons.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id) ?? throw new CashRejected("سجل المطابقة غير متاح.", 404);
        return await View(row);
    }
    private static bool Text(string? s, int max) => !string.IsNullOrWhiteSpace(s) && s.Length <= max && !s.Any(char.IsControl);
    private async Task<StatementLineWrite> Validate(StatementLineWrite value, Guid academy, bool checkBranch = true)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")).DateTime);
        if (value.SourceKind is not ("Bank" or "Provider") || !Text(value.AccountTag, 60) || !Text(value.StatementReference, 100) || !Text(value.LineReference, 100) || !Text(value.Provider, 60) || !Text(value.PaymentReference, 100) || value.SettlementDate < new DateOnly(2000, 1, 1) || value.SettlementDate > today || !StatementComparisonRules.Money(value.GrossAmount) || value.GrossAmount == 0 || !StatementComparisonRules.Money(value.FeeAmount) || value.FeeAmount > value.GrossAmount || !StatementComparisonRules.Money(value.NetAmount) || value.Currency?.Length != 3 || !value.Currency.All(c => c is >= 'A' and <= 'Z') || value.Note?.Length > 300 || value.Note?.Any(char.IsControl) == true)
            throw new CashRejected("راجع مرجع الكشف والمبالغ والعملة والتاريخ. لا تدخل أسرار الحساب أو بيانات بطاقة.", 422);
        if (checkBranch && !await db.Branches.AnyAsync(b => b.AcademyId == academy && b.Id == value.BranchId)) throw new CashRejected("الفرع غير متاح.", 404);
        return value with { AccountTag = value.AccountTag.Trim().ToUpperInvariant(), StatementReference = value.StatementReference.Trim(), LineReference = value.LineReference.Trim(), Provider = value.Provider.Trim(), PaymentReference = value.PaymentReference.Trim(), Note = string.IsNullOrWhiteSpace(value.Note) ? null : value.Note.Trim(), QuoteHash = null };
    }
    private async Task<SettlementComparison> Evaluate(StatementLineWrite body, Guid academy, Guid user)
    {
        var candidates = await db.Collections.AsNoTracking().Where(c => c.AcademyId == academy && c.SportEnrollment.BranchId == body.BranchId && c.PaymentMethod == "Online" && c.Provider == body.Provider && c.ProviderReference == body.PaymentReference).Take(2).ToListAsync();
        return BuildComparison(body, academy, user, candidates);
    }
    private SettlementComparison BuildComparison(StatementLineWrite body, Guid academy, Guid user, IReadOnlyList<PaymentCollection> candidates)
    {
        var c = candidates.Count == 1 ? candidates[0] : null;
        return new() { Id = Guid.NewGuid(), AcademyId = academy, BranchId = body.BranchId, CollectionId = c?.Id, SourceKind = body.SourceKind, AccountTag = body.AccountTag, StatementReference = body.StatementReference, LineReference = body.LineReference, Provider = body.Provider, PaymentReference = body.PaymentReference, SettlementDate = body.SettlementDate, GrossAmount = body.GrossAmount, FeeAmount = body.FeeAmount, NetAmount = body.NetAmount, Currency = body.Currency, ExpectedAmount = c?.Amount, ExpectedCurrency = c?.Currency, Result = StatementComparisonRules.Compare(body.GrossAmount, body.FeeAmount, body.NetAmount, body.Currency, c?.Amount, c?.Currency, candidates.Count > 1), ActorUserId = user, Note = body.Note, ContentHash = StatementComparisonRules.BodyHash(body), CreatedAtUtc = time.GetUtcNow(), UpdatedAtUtc = time.GetUtcNow() };
    }
    private static string Quote(SettlementComparison row) => StatementComparisonRules.Hash(new { row.ContentHash, row.CollectionId, row.ExpectedAmount, row.ExpectedCurrency, row.Result });
    public async Task<object> Preview(StatementLineWrite body)
    {
        var (t, user) = await Context(); var clean = await Validate(body, t.AcademyId); var row = await Evaluate(clean, t.AcademyId, user);
        return new { quoteHash = Quote(row), row.Result, row.CollectionId, expected = row.ExpectedAmount is { } amount ? CashRegisterService.Money(amount) : null, row.ExpectedCurrency, gross = CashRegisterService.Money(row.GrossAmount), fee = CashRegisterService.Money(row.FeeAmount), net = CashRegisterService.Money(row.NetAmount), row.Currency };
    }
    public async Task<object> Post(StatementLineWrite body)
    {
        var (t, user) = await Context(); var clean = await Validate(body, t.AcademyId);
        if (!Text(body.QuoteHash, 64) || body.QuoteHash!.Length != 64) throw new CashRejected("اعرض المقارنة أولًا ثم أكد حفظها.", 422);
        await using var tx = await db.Database.BeginTransactionAsync();
        // Shared with CSV import: serialize source-line identities and per-account matched collections.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"statement-comparison:{t.AcademyId}"},0))");
        var hash = StatementComparisonRules.BodyHash(clean);
        var existing = await db.SettlementComparisons.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.SourceKind == clean.SourceKind && x.AccountTag == clean.AccountTag && x.StatementReference == clean.StatementReference && x.LineReference == clean.LineReference);
        if (existing is not null) { if (existing.ContentHash != hash) throw new CashRejected("سطر الكشف مسجل ببيانات أخرى؛ لا يستبدل التاريخ المالي."); return await View(existing); }
        var row = await Evaluate(clean, t.AcademyId, user);
        if (Quote(row) != body.QuoteHash) throw new CashRejected("تغيرت نتيجة المقارنة. أعد المعاينة قبل الحفظ.");
        if (row.Result == "Matched" && await db.SettlementComparisons.AnyAsync(x => x.AcademyId == t.AcademyId && x.SourceKind == row.SourceKind && x.AccountTag == row.AccountTag && x.CollectionId == row.CollectionId && x.Result == "Matched")) throw new CashRejected("هذا التحصيل طابق سطرًا سابقًا للحساب نفسه. راجع السجل بدل تكراره.");
        db.SettlementComparisons.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return await View(row);
    }
    private async Task<object> View(SettlementComparison row) => new { row.Id, row.BranchId, row.CollectionId, row.SourceKind, row.AccountTag, row.StatementReference, row.LineReference, row.Provider, row.PaymentReference, row.SettlementDate, gross = CashRegisterService.Money(row.GrossAmount), fee = CashRegisterService.Money(row.FeeAmount), net = CashRegisterService.Money(row.NetAmount), row.Currency, expected = row.ExpectedAmount is { } amount ? CashRegisterService.Money(amount) : null, row.ExpectedCurrency, row.Result, row.Note, row.CreatedAtUtc, actor = await db.Users.Where(u => u.Id == row.ActorUserId).Select(u => u.DisplayName).SingleAsync(), receiptId = await db.Receipts.Where(r => r.AcademyId == row.AcademyId && r.CollectionId == row.CollectionId).Select(r => (Guid?)r.Id).SingleOrDefaultAsync() };
}
