namespace Academy.Api.Operations;

// Decimal only; fixed ordinal allocation places leftover cents on the first units.
// Assessment, not a payroll posting. No amount is payable twice just by reading it.
public static class PrivateCompensationCalculator
{
    public static decimal Portion(decimal net, int count, int ordinal)
    {
        if (net < 0 || decimal.Round(net, 2) != net || count is < 1 or > 1000 || ordinal < 1 || ordinal > count) throw new ArgumentOutOfRangeException(nameof(net));
        var cents = net * 100; var each = decimal.Floor(cents / count); var remainder = cents - each * count;
        return (each + (ordinal <= remainder ? 1 : 0)) / 100;
    }
    public static (decimal Eligible, decimal Earned, decimal Contingent) Assess(decimal portion, decimal netCharge, decimal collected, string method, decimal value, string eligibility)
    {
        if (portion < 0 || netCharge < portion || collected < 0 || value < 0 || method is not ("Percent" or "PerSession") || method == "Percent" && value > 100 || eligibility is not ("Delivered" or "DeliveredCollected")) throw new ArgumentOutOfRangeException(nameof(portion));
        if (portion == 0 || netCharge == 0) return (0, 0, 0); // free/fully credited service is not a second sale
        var ratio = eligibility == "Delivered" ? 1 : Math.Min(1, collected / netCharge);
        var eligible = portion * ratio; // do not round the base before calculating compensation
        var full = Round(method == "Percent" ? portion * value / 100 : value);
        var earned = Round(method == "Percent" ? eligible * value / 100 : value * ratio);
        return (Round(eligible), earned, full - earned);
    }
    private static decimal Round(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);
}
