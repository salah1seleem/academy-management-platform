using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;
public static partial class ReportingEndpoints
{
    private sealed class ClassifiedSubscription { public PaymentCollection Collection {get;init;}=null!; public string Kind {get;init;}="Unclassified"; }
    // Explicit creation sources first. Old rows without an earlier service source
    // remain unclassified; a RenewalRequest table name is not proof of renewal.
    // Scalar subqueries never remove a valid collecting-branch financial fact
    // when its operational child/enrollment is outside the caller's scope.
    private static IQueryable<ClassifiedSubscription> SubscriptionKinds(IQueryable<PaymentCollection> query,FoundationDbContext db) => query.Select(c=>new ClassifiedSubscription {
        Collection=c,
        Kind=db.RenewalRequests.Where(r=>r.AcademyId==c.AcademyId&&r.Id==c.RenewalRequestId).Select(r=>r.RegistrationPayloadHash).FirstOrDefault()!=null
            ||db.NewEnrollmentRequests.Any(n=>n.AcademyId==c.AcademyId&&n.PaymentRequestId==c.PaymentRequestId)?"Enrollment"
            :db.SubscriptionPeriods.Any(p=>p.AcademyId==c.AcademyId&&p.SportEnrollmentId==c.SportEnrollmentId&&p.CollectionId!=c.Id&&p.CreatedAtUtc<=c.ConfirmedAtUtc)?"Renewal":"Unclassified"
    });
    private sealed class SummaryMoney {
        public decimal amount {get;init;} public string? PaymentMethod {get;init;}
        public DateTimeOffset ConfirmedAtUtc {get;init;} public Guid? BranchId {get;init;}
        public string Source {get;init;}="Unclassified";
    }
    private sealed record CollectionSourceItem(string Key,string Label,decimal Amount,int Count,string CollectionType,string? SubscriptionKind=null);
    private static CollectionSourceItem[] CollectionSources(IEnumerable<SummaryMoney> rows)=>rows.GroupBy(r=>r.Source).Select(g=>new CollectionSourceItem(g.Key,g.Key switch{
        "Enrollment"=>"اشتراكات جديدة","Renewal"=>"تجديد الاشتراكات","Unclassified"=>"اشتراكات غير مصنفة",
        "Product"=>"المنتجات الرياضية","Tournament"=>"البطولات","DayPass"=>"الزيارات اليومية","External"=>"اتفاقيات خارجية","Refund"=>"استردادات الاشتراكات",_=>"مصادر أخرى"
    },g.Sum(r=>r.amount),g.Count(),g.Key is "Enrollment" or "Renewal" or "Unclassified"?"Subscription":g.Key,
        g.Key is "Enrollment" or "Renewal" or "Unclassified"?g.Key:null)).OrderBy(r=>r.Key).ToArray();
    private sealed record CollectionBranchItem(Guid? Id,string Name,decimal Amount,int Count);
    private static CollectionBranchItem[] CollectionBranches(IEnumerable<SummaryMoney> rows,IReadOnlyDictionary<Guid,string> names)=>rows.GroupBy(r=>r.BranchId)
        .Select(g=>new CollectionBranchItem(g.Key,g.Key is {} id?names.GetValueOrDefault(id,"فرع غير متاح"):"غير محدد",g.Sum(r=>r.amount),g.Count())).OrderBy(r=>r.Name).ToArray();
    private static async Task<List<SummaryMoney>> SummaryCollectionRows(Guid academy,string currency,DateTimeOffset start,DateTimeOffset end,Guid? branch,FoundationDbContext db)
    {
        var rows=await SubscriptionKinds(FinancialQuery(academy,currency,start,end,null,branch,null,null,null,db),db).Select(x=>new SummaryMoney{
            amount=x.Collection.Amount,PaymentMethod=x.Collection.PaymentMethod,ConfirmedAtUtc=x.Collection.ConfirmedAtUtc,Source=x.Kind,
            BranchId=db.CashReceiptPostings.Where(p=>p.AcademyId==academy&&p.CollectionId==x.Collection.Id).Select(p=>(Guid?)p.BranchId).FirstOrDefault()
                ??db.SportEnrollments.Where(e=>e.AcademyId==academy&&e.Id==x.Collection.SportEnrollmentId).Select(e=>(Guid?)e.BranchId).FirstOrDefault()
        }).ToListAsync();
        rows.AddRange(await ProductFinancialQuery(academy,currency,start,end,null,branch,null,null,db).Select(x=>new SummaryMoney{amount=x.Amount,PaymentMethod=x.PaymentMethod,ConfirmedAtUtc=x.ConfirmedAtUtc!.Value,BranchId=x.BranchId,Source="Product"}).ToListAsync());
        rows.AddRange(await TournamentQuery(academy,currency,start,end,null,branch,null,null,db).Select(x=>new SummaryMoney{amount=x.Amount,PaymentMethod=x.PaymentMethod,ConfirmedAtUtc=x.ConfirmedAtUtc!.Value,BranchId=db.SportEnrollments.Where(e=>e.AcademyId==academy&&e.Id==x.EnrollmentId).Select(e=>(Guid?)e.BranchId).FirstOrDefault(),Source="Tournament"}).ToListAsync());
        rows.AddRange(await DayPassFinancialQuery(academy,currency,start,end,null,branch,null,null,null,db).Select(x=>new SummaryMoney{amount=x.Amount,PaymentMethod="Online",ConfirmedAtUtc=x.ReviewedAtUtc!.Value,BranchId=x.BranchId,Source="DayPass"}).ToListAsync());
        rows.AddRange(await ExternalFinancialQuery(academy,currency,start,end,null,branch,null,null,null,db).Select(x=>new SummaryMoney{amount=x.Amount,PaymentMethod=x.PaymentMethod,ConfirmedAtUtc=x.ReceivedAtUtc,BranchId=x.BranchId,Source="External"}).ToListAsync());
        rows.AddRange(await RefundFinancialQuery(academy,currency,start,end,null,branch,null,null,null,db).Select(x=>new SummaryMoney{
            amount=-x.Amount,PaymentMethod=x.PayoutMethod??(db.CashRefundPostings.Any(p=>p.RefundId==x.Id)?"Cash":"Unspecified"),ConfirmedAtUtc=x.CreatedAtUtc,Source="Refund",
            BranchId=db.CashRefundPostings.Where(p=>p.AcademyId==academy&&p.RefundId==x.Id).Select(p=>(Guid?)p.BranchId).FirstOrDefault()
                ??db.CashReceiptPostings.Where(p=>p.AcademyId==academy&&p.CollectionId==x.CollectionId).Select(p=>(Guid?)p.BranchId).FirstOrDefault()
                ??db.Collections.Where(c=>c.AcademyId==academy&&c.Id==x.CollectionId).Select(c=>(Guid?)c.SportEnrollment.BranchId).FirstOrDefault()
        }).ToListAsync());
        return rows;
    }
}
