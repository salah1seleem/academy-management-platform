using Academy.Infrastructure.Finance;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;
public static partial class ReportingEndpoints
{
    private static IQueryable<ExternalAgreementReceipt> ExternalFinancialQuery(Guid academy,string currency,DateTimeOffset start,DateTimeOffset end,Guid? sport,Guid? branch,Guid? plan,string? method,string? search,FoundationDbContext db)
    {
        var q=db.ExternalAgreementReceipts.AsNoTracking().Where(r=>r.AcademyId==academy&&r.Currency==currency&&r.ReceivedAtUtc>=start&&r.ReceivedAtUtc<end);
        if(plan.HasValue)return q.Where(r=>false);if(sport.HasValue)q=q.Where(r=>r.SportId==sport);if(branch.HasValue)q=q.Where(r=>r.BranchId==branch);
        if(!string.IsNullOrWhiteSpace(method))q=method is "Cash" or "Online"?q.Where(r=>r.PaymentMethod==method):method=="Manual"?q:q.Where(r=>false);
        if(!string.IsNullOrWhiteSpace(search)){var term=search.Trim();q=q.Where(r=>r.Agreement.CounterpartyName.Contains(term)||r.Agreement.Title.Contains(term));}
        return q;
    }
    private static IQueryable<FinancialRow> ExternalFinancialRows(IQueryable<ExternalAgreementReceipt> q,FoundationDbContext db)=>
        from r in q join b in db.Branches on r.BranchId equals b.Id join s in db.Sports on r.SportId equals s.Id
        select new FinancialRow(r.Id,r.AgreementId,r.ReceiptNumber,r.Agreement.CounterpartyName,s.ArabicName,b.ArabicName,r.Agreement.Kind==ExternalAgreementKind.PoolRental?"تأجير مسبح / حارة":"برنامج خارجي",r.Agreement.Title,r.Amount,r.Currency,r.PaymentMethod,"Manual",r.PaymentReference??"",r.ReceivedAtUtc,0,"External");
}
