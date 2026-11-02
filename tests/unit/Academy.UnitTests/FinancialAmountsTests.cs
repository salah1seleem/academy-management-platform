using Academy.Infrastructure.Subscriptions;

namespace Academy.UnitTests;

public class FinancialAmountsTests
{
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:000000000000}");
    [Fact] public void Paid_expired_subscriptions_are_potential_not_receivables()
    {
        Assert.Equal(12700m, 8 * 600m + 5 * 540m + 4 * 700m + 3 * 800m);
        Assert.Equal(0m, FinancialAmounts.Outstanding(12700, 0, 12700));
    }
    [Fact] public void Partial_credit_and_future_balances_keep_their_definitions()
    {
        var overdue = FinancialAmounts.Outstanding(540, 0, 200) + FinancialAmounts.Outstanding(700, 0, 700) + FinancialAmounts.Outstanding(1000, 100, 0);
        Assert.Equal(1240, overdue); Assert.Equal(1740, overdue + 500);
    }
    [Fact] public void Equal_allocation_preserves_every_penny_and_tie_order()
    {
        var shares = FinancialAmounts.Allocate(100, new Dictionary<Guid, decimal> { [Id(3)] = 1, [Id(2)] = 1, [Id(1)] = 1 });
        Assert.Equal(33.34m, shares[Id(1)]); Assert.Equal(33.33m, shares[Id(2)]); Assert.Equal(33.33m, shares[Id(3)]); Assert.Equal(100, shares.Values.Sum());
        var hundred = FinancialAmounts.Allocate(100000, Enumerable.Range(1, 100).ToDictionary(Id, _ => 1m));
        Assert.All(hundred.Values, x => Assert.Equal(1000, x));
    }
    [Fact] public void Zero_weight_returns_unallocated_instead_of_fake_zero_cost()
    {
        Assert.Empty(FinancialAmounts.Allocate(100, new Dictionary<Guid, decimal>()));
        Assert.Empty(FinancialAmounts.Allocate(100, new Dictionary<Guid, decimal> { [Id(1)] = 0 }));
    }
    [Fact] public void Weighted_salary_split_is_stable()
    {
        var shares = FinancialAmounts.Allocate(5000, new Dictionary<Guid, decimal> { [Id(1)] = 10, [Id(2)] = 15, [Id(3)] = 5 });
        Assert.Equal(1666.67m, shares[Id(1)]); Assert.Equal(2500, shares[Id(2)]); Assert.Equal(833.33m, shares[Id(3)]); Assert.Equal(5000, shares.Values.Sum());
    }
    [Fact] public void Approved_discounts_cannot_create_negative_charge_and_round_once()
    {
        Assert.Equal(0, FinancialAmounts.Net(100, [60m, 60m]));
        Assert.Equal(66.67m, FinancialAmounts.Net(100, [33.335m]));
        Assert.Throws<ArgumentOutOfRangeException>(() => FinancialAmounts.Net(100, [-1m]));
        Assert.Throws<ArgumentOutOfRangeException>(() => FinancialAmounts.Allocate(10.001m, new Dictionary<Guid, decimal> { [Id(1)] = 1 }));
    }
}
