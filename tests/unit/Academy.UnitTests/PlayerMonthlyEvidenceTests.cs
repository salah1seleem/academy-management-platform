using Academy.Api.Slice5;
namespace Academy.UnitTests;
public sealed class PlayerMonthlyEvidenceTests
{
 [Fact]public void Export_admission_never_silently_truncates_at_the_boundary(){Assert.True(PlayerMonthlyEvidence.ExportFits(2000,2000,2000));Assert.True(PlayerMonthlyEvidence.ExportFits(0,0,0));Assert.False(PlayerMonthlyEvidence.ExportFits(2001,0,0));Assert.False(PlayerMonthlyEvidence.ExportFits(0,2001,0));Assert.False(PlayerMonthlyEvidence.ExportFits(0,0,2001));Assert.False(PlayerMonthlyEvidence.ExportFits(-1,0,0));}
 [Fact]public void Month_uses_actual_calendar_month_and_never_future_or_malformed_input(){var today=new DateOnly(2026,10,18);Assert.Equal(new DateOnly(2026,10,1),PlayerMonthlyEvidence.Month(null,today));Assert.Equal(new DateOnly(2024,2,1),PlayerMonthlyEvidence.Month("2024-02",today));foreach(var bad in new[]{"2026-11","2026-1","2026-13","1899-12","","2026-10-01","01/2026"})Assert.Null(PlayerMonthlyEvidence.Month(bad,today));}
 [Theory][InlineData("=SUM(A1)")][InlineData("  @SUM(A1)")][InlineData("\t-1+1")][InlineData("\r+1")][InlineData("\n =2")]
 public void Export_neutralizes_spreadsheet_formulas_even_after_leading_whitespace(string text)=>Assert.StartsWith("\"'",PlayerMonthlyEvidence.Cell(text));
 [Fact]public void Export_quotes_arabic_newlines_and_commas_without_losing_the_evidence(){Assert.Equal("\"قيمة, \"\"ملاحظة\"\"\nسطر\"",PlayerMonthlyEvidence.Cell("قيمة, \"ملاحظة\"\nسطر"));Assert.Contains("\"أحمد اصطناعي\",\"0\"\r\n",PlayerMonthlyEvidence.Csv([new[]{"أحمد اصطناعي","0"}]));Assert.Equal("متر",PlayerMonthlyEvidence.Unit("Metres"));}
}
