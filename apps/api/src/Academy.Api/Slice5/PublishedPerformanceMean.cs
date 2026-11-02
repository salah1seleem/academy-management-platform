using Academy.Infrastructure.Evaluations;
namespace Academy.Api.Slice5;

// Presentation metric for one published assessment, distinct from the existing
// weighted axis/power calculator. Every recorded criterion is already 0..100.
public sealed record PerformanceMean(decimal? Value,int ScoredCriteria,int ApplicableCriteria,decimal CoveragePercent);
public static class PublishedPerformanceMean
{
    public static PerformanceMean Read(IEnumerable<EvaluationScore> scores)
    {
        var applicable=scores.Where(s=>s.IsActive).ToArray();
        var values=applicable.Where(s=>s.Score is >=0 and <=100).Select(s=>(decimal)s.Score!.Value).ToArray();
        return new(values.Length==0?null:Math.Round(values.Average(),1,MidpointRounding.AwayFromZero),values.Length,applicable.Length,
            applicable.Length==0?0:Math.Round(100m*values.Length/applicable.Length,1,MidpointRounding.AwayFromZero));
    }
}
