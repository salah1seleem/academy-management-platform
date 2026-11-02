using Academy.Api.Finance;

namespace Academy.UnitTests;

public sealed class StatementComparisonTests
{
    [Fact] public void Equivalent_decimal_scale_and_quote_do_not_change_source_identity()
    {
        var body = new StatementLineWrite(Guid.NewGuid(), "Bank", "DEMO", "statement", "1", "provider", "payment", new(2026, 9, 28), 100m, 2m, 98m, "EGP");
        Assert.Equal(StatementComparisonRules.BodyHash(body), StatementComparisonRules.BodyHash(body with { GrossAmount = 100.00m, FeeAmount = 2.0m, NetAmount = 98.000m, QuoteHash = "another-preview" }));
        Assert.NotEqual(StatementComparisonRules.BodyHash(body), StatementComparisonRules.BodyHash(body with { PaymentReference = "other" }));
    }
    [Theory]
    [InlineData(100, 2, 98, "EGP", 100, "EGP", "Matched")]
    [InlineData(101, 2, 99, "EGP", 100, "EGP", "AmountMismatch")]
    [InlineData(100, 2, 98, "USD", 100, "EGP", "CurrencyMismatch")]
    [InlineData(100, 2, 100, "EGP", 100, "EGP", "NetMismatch")]
    public void Statement_is_compared_without_creating_money(decimal gross, decimal fee, decimal net, string currency, decimal expected, string expectedCurrency, string result) => Assert.Equal(result, StatementComparisonRules.Compare(gross, fee, net, currency, expected, expectedCurrency));
    [Fact] public void Unknown_or_ambiguous_source_is_not_matched() { Assert.Equal("Unmatched", StatementComparisonRules.Compare(100, 0, 100, "EGP", null, null)); Assert.Equal("Ambiguous", StatementComparisonRules.Compare(100, 0, 100, "EGP", null, null, true)); }
    [Theory] [InlineData(-1)] [InlineData(.001)] [InlineData(1000000000)] public void Invalid_money_is_never_rounded(decimal value) => Assert.False(StatementComparisonRules.Money(value));
}
