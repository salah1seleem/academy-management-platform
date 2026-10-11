using Academy.Api.Operations;
namespace Academy.UnitTests;
public sealed class PlacementPreferenceRankTests
{
    [Theory] [InlineData(6, 0, 1)] [InlineData(0, 6, 1)] [InlineData(6, 2, 3)] [InlineData(1, 1, 0)]
    public void Day_distance_wraps_the_week_without_shifting_weekday_identity(int wanted, int actual, int expected) => Assert.Equal(expected, PlacementPreferenceRank.Calculate([wanted], null, false, [(actual, new(17, 0))], 0, 8).DayDistance);
    [Fact] public void Each_requested_day_is_assessed_not_just_one_lucky_match() => Assert.Equal(5, PlacementPreferenceRank.Calculate([6, 1, 3], null, false, [(6, new(17, 0))], 0, 8).DayDistance);
    [Fact] public void Time_preference_uses_requested_days_before_other_days() => Assert.Equal(30, PlacementPreferenceRank.Calculate([6], new(17, 0), false, [(6, new(17, 30)), (1, new(17, 0))], 0, 8).TimeDistance);
    [Fact] public void No_preferences_are_neutral_and_occupancy_is_not_fake_revenue() { var r = PlacementPreferenceRank.Calculate([], null, false, [(6, new(17, 0))], 3, 4); Assert.Equal((0, 0, 0, .75m), (r.DayDistance, r.TimeDistance, r.CoachPenalty, r.Occupancy)); }
    [Fact] public void Coach_is_a_soft_penalty_and_zero_capacity_never_divides() { var r = PlacementPreferenceRank.Calculate([], null, true, [], 0, 0); Assert.Equal(1, r.CoachPenalty); Assert.Equal(0, r.Occupancy); }
}
