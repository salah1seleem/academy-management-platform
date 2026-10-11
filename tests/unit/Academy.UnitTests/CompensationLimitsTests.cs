using Academy.Infrastructure.People;
namespace Academy.UnitTests;
public class CompensationLimitsTests
{
    [Fact] public void Compensation_has_inclusive_expiry_and_bounded_validity()
    {
        var today = new DateOnly(2026, 10, 11);
        Assert.True(CompensationLimits.Validity(today, today, today.AddDays(90)));
        Assert.False(CompensationLimits.Validity(today, today.AddDays(-1), today));
        Assert.False(CompensationLimits.Validity(today, today, today.AddDays(91)));
        Assert.False(CompensationLimits.Validity(today, today, today.AddDays(-1)));
    }
    [Fact] public void One_source_session_can_be_one_session_or_bounded_minutes_never_unlimited_service()
    {
        Assert.Equal(1, CompensationLimits.Units(CompensationUnit.Session, new(10, 0), new(12, 0)));
        Assert.Equal(120, CompensationLimits.Units(CompensationUnit.Minute, new(10, 0), new(12, 0)));
        Assert.Equal(0, CompensationLimits.Units(CompensationUnit.Minute, new(12, 0), new(10, 0)));
        Assert.True(CompensationLimits.CanAllocate(120, 60, 60)); Assert.False(CompensationLimits.CanAllocate(120, 120, 60));
        Assert.False(CompensationLimits.CanAllocate(1, 1, 1)); Assert.False(CompensationLimits.CanAllocate(1, 0, 0));
    }
}
