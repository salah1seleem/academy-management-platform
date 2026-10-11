using Academy.Api.Operations;

namespace Academy.UnitTests;

public sealed class PayrollCalculatorTests
{
    [Theory]
    [InlineData(2026, 10, 1, 31, 3100, 31, 3100)]
    [InlineData(2026, 10, 10, 20, 3100, 11, 1100)]
    [InlineData(2026, 2, 14, 28, 2800, 15, 1500)]
    [InlineData(2028, 2, 14, 29, 2900, 16, 1600)]
    [InlineData(2026, 9, 15, 15, 3000, 1, 100)]
    public void Salary_uses_actual_calendar_days_and_inclusive_contract_days(int year, int month, int start, int end, decimal salary, int days, decimal due)
    {
        var result = PayrollCalculator.BaseDue(salary, new(year, month, start), new(year, month, end), new(year, month, 1));
        Assert.Equal(days, result.Days); Assert.Equal(due, result.Due);
    }

    [Fact]
    public void Permanent_contracts_no_overlap_and_precision_are_explicit()
    {
        Assert.Equal((31, 3100m), PayrollCalculator.BaseDue(3100, new(2020, 1, 1), null, new(2026, 10, 1)));
        Assert.Equal((0, 0m), PayrollCalculator.BaseDue(3100, new(2026, 11, 1), null, new(2026, 10, 1)));
        Assert.Equal((0, 0m), PayrollCalculator.BaseDue(3100, new(2020, 1, 1), new(2026, 9, 30), new(2026, 10, 1)));
        Assert.Equal((1, 32.26m), PayrollCalculator.BaseDue(1000, new(2026, 10, 1), new(2026, 10, 1), new(2026, 10, 1)));
    }
}
