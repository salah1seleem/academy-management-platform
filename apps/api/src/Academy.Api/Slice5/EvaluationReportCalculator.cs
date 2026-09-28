using Academy.Infrastructure.Evaluations;

namespace Academy.Api.Slice5;

public interface IEvaluationReportCalculator
{
    EvaluationCalculation Calculate(IEnumerable<EvaluationScore> scores);
}

public sealed class EvaluationReportCalculator : IEvaluationReportCalculator
{
    private static readonly FootballAxis[] OrderedAxes =
    [
        FootballAxis.Passing, FootballAxis.Dribbling, FootballAxis.Speed,
        FootballAxis.Defending, FootballAxis.Physical, FootballAxis.Shooting
    ];

    public EvaluationCalculation Calculate(IEnumerable<EvaluationScore> scores)
    {
        var rows = scores.ToList();
        var axes = OrderedAxes.Select(axis =>
        {
            var scored = rows.Where(x => x.FootballAxisSnapshot == axis && x.Score.HasValue).ToList();
            decimal? value = scored.Count == 0 ? null : scored.Sum(x => x.Score!.Value * x.WeightSnapshot) / scored.Sum(x => x.WeightSnapshot);
            return new AxisCalculation(axis, Round(value));
        }).ToArray();
        var available = axes.Where(x => x.Value.HasValue).Select(x => x.Value!.Value).ToArray();
        var scoredCriteria = rows.Count(x => x.Score.HasValue);
        return new EvaluationCalculation(
            Round(available.Length == 0 ? null : available.Average()),
            scoredCriteria,
            rows.Count,
            rows.Count == 0 ? 0m : Round(100m * scoredCriteria / rows.Count)!.Value,
            available.Length,
            axes);
    }

    private static decimal? Round(decimal? value) => value.HasValue
        ? Math.Round(value.Value, 1, MidpointRounding.AwayFromZero)
        : null;
}

public sealed record AxisCalculation(FootballAxis Axis, decimal? Value);
public sealed record EvaluationCalculation(decimal? OverallScore, int ScoredCriteria, int TotalApplicableCriteria,
    decimal CompletenessPercentage, int AvailableAxes, IReadOnlyList<AxisCalculation> Axes);
