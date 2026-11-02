namespace Academy.Api.Operations;
public static class MonthlyStarRanking
{
    public static decimal Index(decimal evaluation,int monthlyStars)
    {
        if(evaluation is <0 or >100||monthlyStars<0)throw new ArgumentOutOfRangeException();
        return .5m*evaluation+50m*Math.Min(monthlyStars,3)/3;
    }
}
