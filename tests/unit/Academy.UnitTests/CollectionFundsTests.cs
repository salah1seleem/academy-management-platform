using Academy.Api.Finance;

namespace Academy.UnitTests;

public class CollectionFundsTests
{
    [Theory]
    [InlineData("100.00", "20.00", "70.00", "10.00")]
    [InlineData("0.30", "0.10", "0.20", "0.00")]
    [InlineData("999999999.99", "0.01", "0.01", "999999999.97")]
    [InlineData("100", "80", "80", "-60")]
    public void Pool_is_exact_and_does_not_hide_historical_overuse(string collected, string refunded, string allocated, string expected)
    {
        static decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Parse(expected), CollectionFunds.Available(Parse(collected), Parse(refunded), Parse(allocated)));
    }
}
