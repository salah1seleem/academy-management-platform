namespace Academy.Api.Finance;

public sealed record ExpenseShare(Guid? GroupId, int Weight, decimal Amount);

public static class OperatingExpenseAllocation
{
    // Stable largest-remainder split in minor units. A saved run, not a report filter,
    // determines the denominator and gives every last piaster one owner.
    public static IReadOnlyList<ExpenseShare> Equal(decimal amount, IEnumerable<Guid> eligibleGroups)
    {
        if (amount <= 0 || amount > 999999999m || decimal.Round(amount, 2) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount));
        var groups = eligibleGroups.Distinct().OrderBy(x => x.ToString("N"), StringComparer.Ordinal).ToArray();
        if (groups.Length == 0) return [new(null, 0, amount)];
        var cents = decimal.ToInt64(amount * 100m);
        var baseCents = cents / groups.Length;
        var remainder = cents % groups.Length;
        return groups.Select((id, index) => new ExpenseShare(id, 1, (baseCents + (index < remainder ? 1 : 0)) / 100m)).ToArray();
    }
}
