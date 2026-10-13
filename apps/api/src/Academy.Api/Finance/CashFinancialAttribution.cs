using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

internal static class CashFinancialAttribution
{
    // Internal attribution only. Absence from a staff branch scope must NOT be
    // mistaken for absence of a posting and reassigned to the enrollment branch.
    // Tenant filtering stays enabled; each query also pins the trusted academy.
    // Callers must first authorize the receipt/collection, never expose this as search.
    internal static IQueryable<CashReceiptPosting> ReceiptSources(FoundationDbContext db,Guid academy) =>
        db.CashReceiptPostings.IgnoreQueryFilters(["trusted-branches"]).Where(p=>p.AcademyId==academy);
    internal static IQueryable<CashRefundPosting> RefundSources(FoundationDbContext db,Guid academy) =>
        db.CashRefundPostings.IgnoreQueryFilters(["trusted-branches"]).Where(p=>p.AcademyId==academy);
    internal static IQueryable<Branch> BranchLabels(FoundationDbContext db,Guid academy) =>
        db.Branches.IgnoreQueryFilters(["trusted-branches"]).Where(b=>b.AcademyId==academy);
}
