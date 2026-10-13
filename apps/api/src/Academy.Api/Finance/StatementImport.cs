using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public sealed partial class SettlementComparisonService
{
    public async Task<object> ImportTemplate()
    {
        await Context();
        return Results.File(System.Text.Encoding.UTF8.GetBytes("\uFEFF" + StatementCsv.DemoTemplate), "text/csv; charset=utf-8", "academy-statement-template.csv");
    }
    private sealed record ComparedLine(int Number, SettlementComparison Row, bool Reused);
    private async Task<List<StatementLineWrite>> ValidateImport(StatementImportWrite body, Guid academy)
    {
        var parsed = StatementCsv.Parse(body); List<StatementLineWrite> clean = [];
        for (var i = 0; i < parsed.Count; i++)
        {
            try { clean.Add(await Validate(parsed[i], academy, i == 0)); }
            catch (CashRejected e) when (e.Status == 422) { throw new CashRejected($"السطر {i + 2}: {e.Message} لا تُحفظ أي أسطر.", 422); }
        }
        return clean;
    }
    private async Task<List<ComparedLine>> CompareImport(List<StatementLineWrite> lines, Guid academy, Guid user)
    {
        var first = lines[0]; var references = lines.Select(x => x.PaymentReference).Distinct().ToArray(); var lineReferences = lines.Select(x => x.LineReference).ToArray();
        var candidates = await db.Collections.AsNoTracking().Where(c => c.AcademyId == academy && c.SportEnrollment.BranchId == first.BranchId && c.PaymentMethod == "Online" && c.Provider == first.Provider && references.Contains(c.ProviderReference)).Take(501).ToListAsync();
        if (candidates.Count > 500) throw new CashRejected("توجد مراجع دفع غير حاسمة كثيرة. قسّم الكشف وراجع المراجع قبل الاستيراد.", 422);
        var grouped = candidates.ToLookup(c => c.ProviderReference, StringComparer.Ordinal);
        var existing = await db.SettlementComparisons.AsNoTracking().Where(x => x.AcademyId == academy && x.SourceKind == first.SourceKind && x.AccountTag == first.AccountTag && x.StatementReference == first.StatementReference && lineReferences.Contains(x.LineReference)).ToDictionaryAsync(x => x.LineReference);
        List<ComparedLine> result = [];
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (existing.TryGetValue(line.LineReference, out var saved))
            {
                if (saved.ContentHash != StatementComparisonRules.BodyHash(line)) throw new CashRejected($"السطر {i + 2}: مسجل ببيانات أخرى. لن يُستبدل السجل ولن تُحفظ بقية الأسطر.");
                result.Add(new(i + 2, saved, true));
            }
            else result.Add(new(i + 2, BuildComparison(line, academy, user, grouped[line.PaymentReference].Take(2).ToList()), false));
        }
        var matched = result.Where(x => x.Row.Result == "Matched").ToList();
        if (matched.GroupBy(x => x.Row.CollectionId).Any(g => g.Count() > 1)) throw new CashRejected("يربط الكشف أكثر من سطر بالتحصيل نفسه. راجع المراجع؛ لا تُحفظ أي أسطر.");
        var newMatchedIds = matched.Where(x => !x.Reused).Select(x => x.Row.CollectionId).ToArray();
        if (newMatchedIds.Length > 0 && await db.SettlementComparisons.AnyAsync(x => x.AcademyId == academy && x.SourceKind == first.SourceKind && x.AccountTag == first.AccountTag && newMatchedIds.Contains(x.CollectionId) && x.Result == "Matched"))
            throw new CashRejected("أحد التحصيلات مطابق سابقًا للحساب نفسه. راجع السجل؛ لا تُحفظ أي أسطر.");
        return result;
    }
    private static string ImportQuote(Guid academy, Guid actor, List<ComparedLine> rows) => StatementComparisonRules.Hash(new { academy, actor, rows = rows.Select(x => new { x.Number, quote = Quote(x.Row), x.Reused }).ToArray() });
    private static object ImportRows(List<ComparedLine> rows) => rows.Select(x => new { number = x.Number, id = x.Reused ? (Guid?)x.Row.Id : null, x.Reused, x.Row.LineReference, x.Row.PaymentReference, x.Row.SettlementDate, x.Row.Result, gross = CashRegisterService.Money(x.Row.GrossAmount), fee = CashRegisterService.Money(x.Row.FeeAmount), net = CashRegisterService.Money(x.Row.NetAmount), x.Row.Currency, expected = x.Row.ExpectedAmount is { } amount ? CashRegisterService.Money(amount) : null, x.Row.ExpectedCurrency }).ToArray();
    public async Task<object> PreviewImport(StatementImportWrite body)
    {
        var (t, user) = await Context(); var lines = await ValidateImport(body, t.AcademyId);
        var rows = await CompareImport(lines, t.AcademyId, user);
        return new { quoteHash = ImportQuote(t.AcademyId, user, rows), count = rows.Count, reused = rows.Count(x => x.Reused), matched = rows.Count(x => x.Row.Result == "Matched"), differences = rows.Count(x => x.Row.Result != "Matched"), items = ImportRows(rows) };
    }
    public async Task<object> PostImport(StatementImportWrite body)
    {
        var (t, user) = await Context(); var lines = await ValidateImport(body, t.AcademyId);
        if (!Text(body.QuoteHash, 64) || body.QuoteHash!.Length != 64) throw new CashRejected("عاين الكشف أولًا ثم أكد حفظ الدفعة.", 422);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"statement-comparison:{t.AcademyId}"},0))");
        var rows = await CompareImport(lines, t.AcademyId, user);
        if (rows.Any(x => !x.Reused) && ImportQuote(t.AcademyId, user, rows) != body.QuoteHash) throw new CashRejected("تغيرت مقارنة الكشف. أعد المعاينة؛ لم تُحفظ أي أسطر.");
        var fresh = rows.Where(x => !x.Reused).Select(x => x.Row).ToArray();
        db.SettlementComparisons.AddRange(fresh); await db.SaveChangesAsync(); await tx.CommitAsync();
        return new { count = rows.Count, created = fresh.Length, reused = rows.Count - fresh.Length, items = rows.Select(x => new { x.Number, x.Row.Id, x.Row.LineReference, x.Row.Result }).ToArray() };
    }
}
