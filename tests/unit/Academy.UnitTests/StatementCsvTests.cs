using Academy.Api.Finance;

namespace Academy.UnitTests;

public sealed class StatementCsvTests
{
    private static StatementImportWrite Body(string text) => new(Guid.NewGuid(), "Bank", "DEMO", "S1", "InternalTest", text);
    [Fact]
    public void Csv_preserves_arabic_quoted_commas_quotes_bom_and_crlf()
    {
        var rows = StatementCsv.Parse(Body("\uFEFF" + StatementCsv.Header + "\r\n1,P1,2026-09-28,900.00,5,895.0,EGP,\"مراجعة, \"\"تجريبية\"\"\"\r\n"));
        Assert.Single(rows); Assert.Equal("مراجعة, \"تجريبية\"", rows[0].Note); Assert.Equal(900m, rows[0].GrossAmount); Assert.Equal(895m, rows[0].NetAmount);
    }
    [Fact]
    public void Csv_accepts_250_rows_but_never_more()
    {
        var body = StatementCsv.Header + "\n" + string.Join('\n', Enumerable.Range(1, 250).Select(i => $"{i},P{i},2026-09-28,10.00,0,10,EGP,"));
        Assert.Equal(250, StatementCsv.Parse(Body(body)).Count);
        Assert.Throws<CashRejected>(() => StatementCsv.Parse(Body(body + "\n251,P251,2026-09-28,10,0,10,EGP,")));
    }
    [Theory]
    [InlineData("1,P1,2026-09-28,10,0,10,EGP,\"unclosed")]
    [InlineData("1,P1,2026-09-28,10,0,10,EGP,\"closed\"extra")]
    [InlineData("1,P1,2026-09-28,1e2,0,100,EGP,")]
    [InlineData("1,P1,2026-09-28,-1,0,0,EGP,")]
    [InlineData("1,P1,2026-09-28,1.001,0,1,EGP,")]
    [InlineData("1,P1,28/09/2026,10,0,10,EGP,")]
    [InlineData("1,P1,2026-09-28,10,0,10,EGP,=SUM(A1)")]
    [InlineData("1,+formula,2026-09-28,10,0,10,EGP,")]
    [InlineData("1,P1,2026-09-28,10,0,10,EGP,\u0000")]
    [InlineData("1,P1,2026-09-28,10,0,10,EGP,extra,column")]
    [InlineData("1,P1,2026-09-28,10,0,10,EGP,\n\n2,P2,2026-09-28,10,0,10,EGP,")]
    [InlineData("1,P1,2026-09-28,10,0,10,EGP,\n1 ,P2,2026-09-28,10,0,10,EGP,")]
    public void Csv_rejects_invalid_rows_without_partial_results(string row) => Assert.Throws<CashRejected>(() => StatementCsv.Parse(Body(StatementCsv.Header + "\n" + row)));
    [Fact] public void Csv_rejects_empty_changed_headers_and_oversized_utf8()
    {
        foreach (var text in new[] { "", StatementCsv.Header, "other\n1,P,2026-09-28,10,0,10,EGP,", new string('م', StatementCsv.MaxBytes) }) Assert.Throws<CashRejected>(() => StatementCsv.Parse(Body(text)));
    }
}
