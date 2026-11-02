using Academy.Api.Operations;
namespace Academy.UnitTests;
public sealed class PrivateCompensationCalculatorTests
{
    [Fact]
    public void V3_partial_package_earns_only_delivered_and_allocated_portion()
    {
        var portion = PrivateCompensationCalculator.Portion(2000, 8, 1);
        var half = PrivateCompensationCalculator.Assess(portion, 2000, 1000, "Percent", 40, "DeliveredCollected");
        Assert.Equal((125m, 50m, 50m), half); // two delivered units => eligible250/earned100/contingent100
        var full = PrivateCompensationCalculator.Assess(portion, 2000, 2000, "Percent", 40, "DeliveredCollected");
        Assert.Equal((250m, 100m, 0m), full); Assert.Equal(50m, full.Earned - half.Earned);
        Assert.Equal(full, PrivateCompensationCalculator.Assess(portion, 2000, 2500, "Percent", 40, "DeliveredCollected"));
    }
    [Fact]
    public void V3_fixed_ordinals_conserve_every_cent_and_do_not_round_the_percentage_base_early()
    {
        Assert.Equal(new[] { 3.34m, 3.33m, 3.33m }, Enumerable.Range(1, 3).Select(i => PrivateCompensationCalculator.Portion(10m, 3, i)));
        Assert.Equal(10m, Enumerable.Range(1, 3).Sum(i => PrivateCompensationCalculator.Portion(10m, 3, i)));
        Assert.Equal(0.01m, PrivateCompensationCalculator.Assess(0.03m, 0.03m, 0.01m, "Percent", 50, "DeliveredCollected").Earned);
        Assert.Equal((0m, 0m, 0m), PrivateCompensationCalculator.Assess(0, 0, 0, "PerSession", 120, "Delivered"));
    }
    [Theory]
    [InlineData("Percent", 40, "Delivered", 100, 0)]
    [InlineData("PerSession", 120, "DeliveredCollected", 60, 60)]
    [InlineData("PerSession", 120, "Delivered", 120, 0)]
    [InlineData("Percent", 0, "DeliveredCollected", 0, 0)]
    public void V3_explicit_contract_controls_eligibility(string method, decimal value, string eligibility, decimal earned, decimal contingent)
    {
        var c = PrivateCompensationCalculator.Assess(250, 2000, 1000, method, value, eligibility); Assert.Equal(earned, c.Earned); Assert.Equal(contingent, c.Contingent);
    }
}
