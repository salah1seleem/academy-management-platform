using Academy.Api.Slice5;
using Academy.Infrastructure.Evaluations;
namespace Academy.UnitTests;
public sealed class PublishedPerformanceMeanTests
{
    private static EvaluationScore Score(int? value,decimal weight=1)=>new(){Score=value,WeightSnapshot=weight,CriterionNameSnapshot="معيار خيالي",FootballAxisSnapshot=FootballAxis.Speed};
    [Fact]
    public void Mean_is_not_the_weighted_power_score_and_does_not_mutate_it(){var rows=new[]{Score(100,9),Score(0)};var calculator=new EvaluationReportCalculator();var before=calculator.Calculate(rows).OverallScore;var mean=PublishedPerformanceMean.Read(rows);Assert.Equal(50m,mean.Value);Assert.Equal(90m,before);Assert.Equal(before,calculator.Calculate(rows).OverallScore);Assert.Equal(100m,mean.CoveragePercent);}
    [Fact]
    public void Missing_criteria_are_excluded_instead_of_becoming_zero_and_coverage_is_explicit(){var mean=PublishedPerformanceMean.Read([Score(80),Score(null),Score(100)]);Assert.Equal(90m,mean.Value);Assert.Equal(2,mean.ScoredCriteria);Assert.Equal(3,mean.ApplicableCriteria);Assert.Equal(66.7m,mean.CoveragePercent);}
    [Fact]
    public void No_recorded_criteria_has_no_invented_rating(){var mean=PublishedPerformanceMean.Read([Score(null),Score(null)]);Assert.Null(mean.Value);Assert.Equal(0,mean.ScoredCriteria);}
}
