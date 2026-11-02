using Academy.Infrastructure.Evaluations;
using Academy.Api.Slice5;

namespace Academy.Api.Slice6;

// One published assessment and enrollment context; the legacy power score remains separate.
internal static class GuardianPerformanceProjection
{
    internal static object? Read(PlayerEvaluation? evaluation, IEvaluationReportCalculator calculator)
    {
        if (evaluation is null || evaluation.Status != EvaluationStatus.Published) return null;
        var calculated = calculator.Calculate(evaluation.Scores);
        var mean = PublishedPerformanceMean.Read(evaluation.Scores);
        var football = evaluation.Sport.Kind == Academy.Infrastructure.Structure.SportKind.Football || evaluation.Sport.EnglishName == "Football" || evaluation.Sport.ArabicName == "كرة القدم";
        return new {
            evaluationId = evaluation.Id, evaluation.EvaluationDate, calculated.OverallScore, meanPerformance = mean.Value, mean.ScoredCriteria, mean.ApplicableCriteria, mean.CoveragePercent, isFootballReport = football,
            axes = football ? calculated.Axes.Where(a => a.Value.HasValue).Select(a => new { axis = a.Axis.ToString(), a.Value }).ToArray() : [],
            criteria = evaluation.Scores.Where(s => s.Score is >= 0 and <= 100).OrderBy(s => s.CriterionNameSnapshot).Select(s => new { name = s.CriterionNameSnapshot, value = s.Score }).ToArray()
        };
    }
}
