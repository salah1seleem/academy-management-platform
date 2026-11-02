namespace Academy.Infrastructure.Subscriptions;

// EGP currently has two minor digits. No binary floating point enters financial authority.
public static class FinancialAmounts
{
    public static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    public static decimal Net(decimal price, IEnumerable<decimal> approvedDiscounts)
    {
        if (price < 0 || price != Round(price)) throw new ArgumentOutOfRangeException(nameof(price));
        var discounts = approvedDiscounts.ToArray();
        if (discounts.Any(x => x < 0)) throw new ArgumentOutOfRangeException(nameof(approvedDiscounts));
        return Round(Math.Max(0, price - discounts.Sum()));
    }
    public static decimal Outstanding(decimal charges, decimal credits, decimal allocations)
    {
        if (charges < 0 || credits < 0 || allocations < 0) throw new ArgumentOutOfRangeException(nameof(charges));
        return Round(charges - credits - allocations);
    }
    // Stable ID order breaks ties; report filtering must never recalculate this denominator.
    public static IReadOnlyDictionary<Guid, decimal> Allocate(decimal amount, IReadOnlyDictionary<Guid, decimal> weights)
    {
        if (amount < 0 || amount != Round(amount) || weights.Any(x => x.Key == Guid.Empty || x.Value < 0)) throw new ArgumentOutOfRangeException(nameof(amount));
        if (weights.Count == 0 || weights.Values.Sum() == 0) return new Dictionary<Guid, decimal>();
        var total = weights.Values.Sum(); var pennies = amount * 100;
        var shares = weights.Select(x => new { x.Key, Floor = decimal.Floor(pennies * x.Value / total), Fraction = pennies * x.Value / total % 1 }).ToArray();
        var remaining = (int)(pennies - shares.Sum(x => x.Floor));
        var extra = shares.OrderByDescending(x => x.Fraction).ThenBy(x => x.Key).Take(remaining).Select(x => x.Key).ToHashSet();
        return shares.ToDictionary(x => x.Key, x => (x.Floor + (extra.Contains(x.Key) ? 1 : 0)) / 100m);
    }
}
