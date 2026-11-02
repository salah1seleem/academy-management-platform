using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;
public static partial class ReportingEndpoints
{
    // Fixed, explicitly labelled windows ending at the selected local date.
    // Aggregate in SQL; never load individual financial records for a sparkline.
    private static async Task<object> WeeklyCollectionTrend(Guid academy, string currency, DateOnly end, string zone, Guid? branch, FoundationDbContext db)
    {
        if(end.Year<2)return Array.Empty<object>();
        var first=end.AddDays(-55);
        var bounds=Enumerable.Range(0,8).Select(i=>UtcRange(first.AddDays(i*7),first.AddDays(i*7+6),zone)).ToArray();
        var start=bounds[0].Start;var finish=bounds[7].EndExclusive;
        var b1=bounds[1].Start;var b2=bounds[2].Start;var b3=bounds[3].Start;var b4=bounds[4].Start;var b5=bounds[5].Start;var b6=bounds[6].Start;var b7=bounds[7].Start;
        var amounts=FinancialQuery(academy,currency,start,finish,null,branch,null,null,null,db).Select(c=>new TrendAmount{At=c.ConfirmedAtUtc,Amount=c.Amount})
            .Concat(ProductFinancialQuery(academy,currency,start,finish,null,branch,null,null,db).Select(c=>new TrendAmount{At=c.ConfirmedAtUtc!.Value,Amount=c.Amount}))
            .Concat(TournamentQuery(academy,currency,start,finish,null,branch,null,null,db).Select(c=>new TrendAmount{At=c.ConfirmedAtUtc!.Value,Amount=c.Amount}))
            .Concat(DayPassFinancialQuery(academy,currency,start,finish,null,branch,null,null,null,db).Select(c=>new TrendAmount{At=c.ReviewedAtUtc!.Value,Amount=c.Amount}))
            .Concat(ExternalFinancialQuery(academy,currency,start,finish,null,branch,null,null,null,db).Select(c=>new TrendAmount{At=c.ReceivedAtUtc,Amount=c.Amount}))
            .Concat(RefundFinancialQuery(academy,currency,start,finish,null,branch,null,null,null,db).Select(c=>new TrendAmount{At=c.CreatedAtUtc,Amount=-c.Amount}));
        var totals=await amounts.GroupBy(c=>c.At<b1?0:c.At<b2?1:c.At<b3?2:c.At<b4?3:c.At<b5?4:c.At<b6?5:c.At<b7?6:7)
            .Select(g=>new{index=g.Key,amount=g.Sum(c=>c.Amount)}).ToDictionaryAsync(g=>g.index,g=>g.amount);
        return Enumerable.Range(0,8).Select(i=>new{from=first.AddDays(i*7),to=first.AddDays(i*7+6),amount=totals.GetValueOrDefault(i)}).ToArray();
    }
    private sealed class TrendAmount {public DateTimeOffset At {get;init;} public decimal Amount {get;init;}}

    private static async Task<object> SubscriptionCountTrend(Guid academy, DateOnly end, Guid? branch, FoundationDbContext db)
        => await SubscriptionHomeEndpoints.Trend(db,academy,end,branch);
}
