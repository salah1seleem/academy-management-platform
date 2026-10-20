using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;
public static partial class ReportingEndpoints
{
    private sealed class SummaryMoney { public decimal amount {get;init;} public string? PaymentMethod {get;init;} public DateTimeOffset ConfirmedAtUtc {get;init;} }
    private static async Task<List<SummaryMoney>> SummaryCollectionRows(Guid academy,string currency,DateTimeOffset start,DateTimeOffset end,Guid? branch,FoundationDbContext db)
    {
        var rows=await FinancialQuery(academy,currency,start,end,null,branch,null,null,null,db).Select(x=>new SummaryMoney{amount=x.Amount,PaymentMethod=x.PaymentMethod,ConfirmedAtUtc=x.ConfirmedAtUtc}).ToListAsync();
        rows.AddRange(await ProductFinancialQuery(academy,currency,start,end,null,branch,null,null,db).Select(x=>new SummaryMoney{amount=x.Amount,PaymentMethod=x.PaymentMethod,ConfirmedAtUtc=x.ConfirmedAtUtc!.Value}).ToListAsync());
        rows.AddRange(await TournamentQuery(academy,currency,start,end,null,branch,null,null,db).Select(x=>new SummaryMoney{amount=x.Amount,PaymentMethod=x.PaymentMethod,ConfirmedAtUtc=x.ConfirmedAtUtc!.Value}).ToListAsync());
        rows.AddRange(await DayPassFinancialQuery(academy,currency,start,end,null,branch,null,null,null,db).Select(x=>new SummaryMoney{amount=x.Amount,PaymentMethod="Online",ConfirmedAtUtc=x.ReviewedAtUtc!.Value}).ToListAsync());
        rows.AddRange(await ExternalFinancialQuery(academy,currency,start,end,null,branch,null,null,null,db).Select(x=>new SummaryMoney{amount=x.Amount,PaymentMethod=x.PaymentMethod,ConfirmedAtUtc=x.ReceivedAtUtc}).ToListAsync());
        rows.AddRange(await RefundFinancialQuery(academy,currency,start,end,null,branch,null,null,null,db).Select(x=>new SummaryMoney{amount=-x.Amount,PaymentMethod=x.PayoutMethod??(db.CashRefundPostings.Any(p=>p.RefundId==x.Id)?"Cash":"Unspecified"),ConfirmedAtUtc=x.CreatedAtUtc}).ToListAsync());
        return rows;
    }
}
