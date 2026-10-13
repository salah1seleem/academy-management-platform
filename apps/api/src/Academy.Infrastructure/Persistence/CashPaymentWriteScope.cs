using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    private CashPaymentLimit? cashPaymentLimit;
    // Only a separately owned, trusted-academy context may receive this purpose
    // limit. HTTP request contexts keep their original branch restrictions.
    public void LimitToCashPayment(Guid session,Guid branch,Guid? enrollment=null,Guid? plan=null)
    {
        if(!RequestScopeEnabled||BranchScopeRestricted||cashPaymentLimit is not null||session==Guid.Empty||branch==Guid.Empty||enrollment.HasValue!=plan.HasValue)
            throw new InvalidOperationException("Invalid isolated cash payment scope");
        cashPaymentLimit=new(session,branch,enrollment,plan);
    }
    private sealed record CashPaymentLimit(Guid Session,Guid Branch,Guid? Enrollment,Guid? Plan)
    {
        public HashSet<Guid> Renewals {get;}=[];
        public HashSet<Guid> Payments {get;}=[];
        public HashSet<Guid> Collections {get;}=[];
        public HashSet<Guid> Receipts {get;}=[];
    }
    private void GuardCashPaymentPurpose()
    {
        if(cashPaymentLimit is not {} limit)return;
        var changes=ChangeTracker.Entries().Where(e=>e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        if(changes.Length==0)return;
        if(!limit.Enrollment.HasValue||!limit.Plan.HasValue||changes.Any(e=>e.State==EntityState.Deleted))throw new ResourceScopeException();
        // SaveChanges may occur more than once in the same atomic operation.
        // Only requests created by THIS context may transition to confirmed.
        var addedEntities=changes.Where(e=>e.State==EntityState.Added).Select(e=>e.Entity).ToArray();
        var renewals=limit.Renewals.Concat(addedEntities.OfType<RenewalRequest>().Select(r=>r.Id)).ToHashSet();
        var payments=limit.Payments.Concat(addedEntities.OfType<PaymentRequest>().Select(p=>p.Id)).ToHashSet();
        var collections=limit.Collections.Concat(addedEntities.OfType<PaymentCollection>().Select(c=>c.Id)).ToHashSet();
        var receipts=limit.Receipts.Concat(addedEntities.OfType<Receipt>().Select(r=>r.Id)).ToHashSet();
        if(renewals.Count>1||payments.Count>1||collections.Count>1||receipts.Count>1)throw new ResourceScopeException();
        foreach(var entry in changes)
        {
            var added=entry.State==EntityState.Added;
            var allowed=entry.Entity switch
            {
                RenewalRequest r=>renewals.Contains(r.Id)&&r.AcademyId==RequestAcademyId&&r.SportEnrollmentId==limit.Enrollment&&r.SubscriptionPlanId==limit.Plan&&r.RequestedByUserId==RequestUserId,
                PaymentRequest p=>payments.Contains(p.Id)&&p.AcademyId==RequestAcademyId&&renewals.Contains(p.RenewalRequestId)&&p.Provider=="Manual"&&p.ProviderEnvironment=="Manual",
                PaymentCollection c=>added&&c.AcademyId==RequestAcademyId&&c.SportEnrollmentId==limit.Enrollment&&payments.Contains(c.PaymentRequestId)&&renewals.Contains(c.RenewalRequestId)&&c.PaymentMethod=="Cash"&&c.ConfirmedBy==RequestUserId.ToString(),
                SubscriptionPeriod p=>added&&p.AcademyId==RequestAcademyId&&p.SportEnrollmentId==limit.Enrollment&&p.SubscriptionPlanId==limit.Plan&&p.CollectionId.HasValue&&collections.Contains(p.CollectionId.Value)&&p.Origin==SubscriptionOrigin.Collected&&p.CreatedByUserId==RequestUserId,
                Receipt r=>added&&r.AcademyId==RequestAcademyId&&r.SportEnrollmentId==limit.Enrollment&&r.SubscriptionPlanId==limit.Plan&&collections.Contains(r.CollectionId)&&r.PaymentMethod=="Cash",
                CashReceiptPosting p=>added&&p.AcademyId==RequestAcademyId&&p.SessionId==limit.Session&&p.BranchId==limit.Branch&&p.ActorUserId==RequestUserId&&collections.Contains(p.CollectionId)&&receipts.Contains(p.ReceiptId),
                _=>false
            };
            if(!allowed)throw new ResourceScopeException();
        }
        limit.Renewals.UnionWith(renewals);limit.Payments.UnionWith(payments);limit.Collections.UnionWith(collections);limit.Receipts.UnionWith(receipts);
    }
}
