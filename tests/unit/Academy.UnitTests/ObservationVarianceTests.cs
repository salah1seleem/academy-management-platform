using Academy.Infrastructure.Facilities;
namespace Academy.UnitTests;
public class ObservationVarianceTests
{
    [Theory]
    [InlineData(6,5,7,true)] [InlineData(6,5,6,true)] [InlineData(6,6,6,false)]
    [InlineData(6,null,6,false)] [InlineData(6,null,7,true)] [InlineData(0,0,0,false)]
    public void Physical_count_is_independent_of_roster_and_recorded_presence(int expected, int? recorded, int observed, bool review) => Assert.Equal(review, ObservationVariance.NeedsReview(expected, recorded, observed));
}
