namespace Academy.Api.Operations;

public static class PayrollCalculator
{
    public static (int Days, decimal Due) BaseDue(decimal salary, DateOnly contractStart, DateOnly? contractEnd, DateOnly month)
    {
        var first = new DateOnly(month.Year, month.Month, 1); var last = first.AddMonths(1).AddDays(-1);
        var start = contractStart > first ? contractStart : first; var end = contractEnd is { } finish && finish < last ? finish : last;
        var days = Math.Max(0, end.DayNumber - start.DayNumber + 1);
        return (days, decimal.Round(salary * days / DateTime.DaysInMonth(month.Year, month.Month), 2, MidpointRounding.AwayFromZero));
    }
}
