using System.Globalization;
using System.Text;

namespace Academy.Api.Finance;

public sealed record StatementImportWrite(Guid BranchId, string SourceKind, string AccountTag, string StatementReference,
    string Provider, string CsvText, string? QuoteHash = null);

// Deliberately narrow template, not an arbitrary spreadsheet or provider-specific bank adapter.
public static class StatementCsv
{
    public const int MaxBytes = 131072;
    public const int MaxRows = 250;
    public const string Header = "line_reference,payment_reference,settlement_date,gross,fee,net,currency,note";
    public const string DemoTemplate = Header + "\n1,DEMO-REFERENCE,2026-09-28,900.00,5.00,895.00,EGP,مثال اصطناعي: استبدل المرجع والمبالغ قبل المعاينة\n";

    public static IReadOnlyList<StatementLineWrite> Parse(StatementImportWrite body)
    {
        if (string.IsNullOrEmpty(body.CsvText) || Encoding.UTF8.GetByteCount(body.CsvText) > MaxBytes)
            throw new CashRejected("ارفع كشف CSV نصيًا لا يتجاوز 128 KiB.", 422);
        var text = body.CsvText.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        var lines = text.Split('\n');
        if (lines.Length is < 2 or > MaxRows + 1 || lines[0] != Header)
            throw new CashRejected("استخدم أعمدة القالب بالترتيب نفسه، من سطر واحد إلى 250 سطرًا. لا تُقبل ملفات Excel أو أسطر فارغة.", 422);
        List<StatementLineWrite> result = []; HashSet<string> identities = new(StringComparer.Ordinal);
        for (var i = 1; i < lines.Length; i++)
        {
            var cells = Cells(lines[i], i + 1);
            if (cells.Count != 8 || cells.Any(x => x.Any(char.IsControl) || x.TrimStart().StartsWith('=') || x.TrimStart().StartsWith('+') || x.TrimStart().StartsWith('@')))
                throw Invalid(i + 1);
            if (!DateOnly.TryParseExact(cells[2], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !Amount(cells[3], out var gross) || !Amount(cells[4], out var fee) || !Amount(cells[5], out var net)) throw Invalid(i + 1);
            if (!identities.Add(cells[0].Trim())) throw new CashRejected($"السطر {i + 1}: مرجع السطر مكرر داخل الكشف. لا تُحفظ أي أسطر.", 422);
            result.Add(new(body.BranchId, body.SourceKind, body.AccountTag, body.StatementReference, cells[0], body.Provider,
                cells[1], date, gross, fee, net, cells[6], cells[7]));
        }
        return result;
    }
    private static bool Amount(string cell, out decimal value) => decimal.TryParse(cell, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) && StatementComparisonRules.Money(value);
    private static CashRejected Invalid(int row) => new($"السطر {row}: راجع الأعمدة الثمانية والتاريخ والمبالغ. استخدم نقطة عشرية، ولا تستخدم صيغًا أو نصًا متعدد الأسطر. لا تُحفظ أي أسطر.", 422);
    private static List<string> Cells(string line, int row)
    {
        List<string> cells = []; StringBuilder cell = new(); bool quoted = false, closed = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c != '"') cell.Append(c);
                else if (i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else { quoted = false; closed = true; }
            }
            else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); closed = false; }
            else if (c == '"' && cell.Length == 0 && !closed) quoted = true;
            else if (c == '"' || closed) throw Invalid(row);
            else cell.Append(c);
        }
        if (quoted) throw Invalid(row);
        cells.Add(cell.ToString()); return cells;
    }
}
