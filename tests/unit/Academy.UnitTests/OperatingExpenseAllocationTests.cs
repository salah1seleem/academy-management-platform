using Academy.Api.Finance;

namespace Academy.UnitTests;

public sealed class OperatingExpenseAllocationTests
{
    [Fact]
    public void One_hundred_thousand_across_one_hundred_groups_conserves_amount()
    {
        var groups = Enumerable.Range(1, 100).Select(i => new Guid(i, 0, 0, new byte[8]));
        var shares = OperatingExpenseAllocation.Equal(100_000m, groups);
        Assert.Equal(100, shares.Count);
        Assert.All(shares, s => Assert.Equal(1000m, s.Amount));
        Assert.Equal(100_000m, shares.Sum(s => s.Amount));
    }

    [Fact]
    public void Three_groups_split_a_hundred_by_stable_largest_remainder()
    {
        var groups = new[] { new Guid(3,0,0,new byte[8]), new Guid(1,0,0,new byte[8]), new Guid(2,0,0,new byte[8]) };
        var first = OperatingExpenseAllocation.Equal(100m, groups);
        var replay = OperatingExpenseAllocation.Equal(100m, groups.Reverse());
        Assert.Equal(new[] { 33.34m, 33.33m, 33.33m }, first.Select(s => s.Amount));
        Assert.Equal(first, replay);
        Assert.Equal(100m, first.Sum(s => s.Amount));
    }

    [Fact]
    public void No_operating_group_remains_visible_as_unallocated()
    {
        var share = Assert.Single(OperatingExpenseAllocation.Equal(100m, []));
        Assert.Null(share.GroupId);
        Assert.Equal(100m, share.Amount);
    }
}
