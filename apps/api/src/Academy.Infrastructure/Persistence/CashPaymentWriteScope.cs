using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    private CashPaymentLimit? cashPaymentLimit;
    private CashFamilyLimit? cashFamilyLimit;
    // Only a separately owned, trusted-academy context may receive this purpose
    // limit. HTTP request contexts keep their original branch restrictions.
    public void LimitToCashPayment(Guid session,Guid branch,Guid? enrollment=null,Guid? plan=null)
    {
        if(!RequestScopeEnabled||BranchScopeRestricted||cashPaymentLimit is not null||cashFamilyLimit is not null||session==Guid.Empty||branch==Guid.Empty||enrollment.HasValue!=plan.HasValue)
            throw new InvalidOperationException("Invalid isolated cash payment scope");
        cashPaymentLimit=new(session,branch,enrollment,plan);
    }
    public void LimitToCashFamilyPayment(Guid session,Guid branch,Guid guardian,IReadOnlyDictionary<Guid,Guid> choices)
    {
        if(!RequestScopeEnabled||BranchScopeRestricted||cashPaymentLimit is not null||cashFamilyLimit is not null||session==Guid.Empty||branch==Guid.Empty||guardian==Guid.Empty||choices.Count is <1 or >20||choices.Any(x=>x.Key==Guid.Empty||x.Value==Guid.Empty))
            throw new InvalidOperationException("Invalid isolated family cash scope");
        cashFamilyLimit=new(session,branch,guardian,new(choices));
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
    private sealed record CashFamilyLimit(Guid Session,Guid Branch,Guid Guardian,Dictionary<Guid,Guid> Choices)
    {
        public Dictionary<Guid,Guid> Renewals {get;set;}=[];
        public Dictionary<Guid,Guid> Payments {get;set;}=[];
        public Dictionary<Guid,Guid> Collections {get;set;}=[];
        public Dictionary<Guid,Guid> Receipts {get;set;}=[];
        public HashSet<Guid> Periods {get;set;}=[];
        public HashSet<Guid> Posted {get;set;}=[];
    }
    private void GuardCashFamilyPurpose()
    {
        if(cashFamilyLimit is not {} limit)return;
        var changes=ChangeTracker.Entries().Where(e=>e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        if(changes.Length==0)return;
        if(changes.Any(e=>e.State==EntityState.Deleted))throw new ResourceScopeException();
        var renewals=new Dictionary<Guid,Guid>(limit.Renewals);var payments=new Dictionary<Guid,Guid>(limit.Payments);
        var collections=new Dictionary<Guid,Guid>(limit.Collections);var receipts=new Dictionary<Guid,Guid>(limit.Receipts);
        var periods=new HashSet<Guid>(limit.Periods);var posted=new HashSet<Guid>(limit.Posted);
        foreach(var r in changes.Where(e=>e.State==EntityState.Added).Select(e=>e.Entity).OfType<RenewalRequest>())
        {
            if(!limit.Choices.TryGetValue(r.SportEnrollmentId,out var plan)||r.SubscriptionPlanId!=plan||r.AcademyId!=RequestAcademyId||r.RequestedByUserId!=RequestUserId||renewals.ContainsValue(r.SportEnrollmentId))throw new ResourceScopeException();
            renewals.Add(r.Id,r.SportEnrollmentId);
        }
        foreach(var p in changes.Where(e=>e.State==EntityState.Added).Select(e=>e.Entity).OfType<PaymentRequest>())
        {
            if(!renewals.TryGetValue(p.RenewalRequestId,out var enrollment)||p.AcademyId!=RequestAcademyId||p.Provider!="Manual"||p.ProviderEnvironment!="Manual"||payments.ContainsValue(enrollment))throw new ResourceScopeException();
            payments.Add(p.Id,enrollment);
        }
        foreach(var c in changes.Where(e=>e.State==EntityState.Added).Select(e=>e.Entity).OfType<PaymentCollection>())
        {
            if(!payments.TryGetValue(c.PaymentRequestId,out var enrollment)||!renewals.TryGetValue(c.RenewalRequestId,out var renewalEnrollment)||enrollment!=renewalEnrollment||c.SportEnrollmentId!=enrollment||c.AcademyId!=RequestAcademyId||c.PaymentMethod!="Cash"||c.ConfirmedBy!=RequestUserId.ToString()||collections.ContainsValue(enrollment))throw new ResourceScopeException();
            collections.Add(c.Id,enrollment);
        }
        foreach(var r in changes.Where(e=>e.State==EntityState.Added).Select(e=>e.Entity).OfType<Receipt>())
        {
            if(!collections.TryGetValue(r.CollectionId,out var enrollment)||r.AcademyId!=RequestAcademyId||r.SportEnrollmentId!=enrollment||r.SubscriptionPlanId!=limit.Choices[enrollment]||r.PaymentMethod!="Cash"||receipts.ContainsValue(enrollment))throw new ResourceScopeException();
            receipts.Add(r.Id,enrollment);
        }
        foreach(var entry in changes)
        {
            var added=entry.State==EntityState.Added;
            var allowed=entry.Entity switch
            {
                RenewalRequest r=>renewals.TryGetValue(r.Id,out var e)&&e==r.SportEnrollmentId&&r.SubscriptionPlanId==limit.Choices[e]&&r.AcademyId==RequestAcademyId&&r.RequestedByUserId==RequestUserId,
                PaymentRequest p=>payments.TryGetValue(p.Id,out var e)&&renewals.TryGetValue(p.RenewalRequestId,out var r)&&e==r&&p.AcademyId==RequestAcademyId&&p.Provider=="Manual"&&p.ProviderEnvironment=="Manual",
                PaymentCollection c=>added&&collections.TryGetValue(c.Id,out var e)&&c.SportEnrollmentId==e&&c.AcademyId==RequestAcademyId,
                SubscriptionPeriod p=>added&&p.CollectionId.HasValue&&collections.TryGetValue(p.CollectionId.Value,out var e)&&p.SportEnrollmentId==e&&p.SubscriptionPlanId==limit.Choices[e]&&p.AcademyId==RequestAcademyId&&p.Origin==SubscriptionOrigin.Collected&&p.CreatedByUserId==RequestUserId&&periods.Add(e),
                Receipt r=>added&&receipts.TryGetValue(r.Id,out var e)&&r.SportEnrollmentId==e&&r.SubscriptionPlanId==limit.Choices[e]&&r.AcademyId==RequestAcademyId,
                CashReceiptPosting p=>added&&receipts.TryGetValue(p.ReceiptId,out var e)&&collections.TryGetValue(p.CollectionId,out var c)&&e==c&&p.AcademyId==RequestAcademyId&&p.SessionId==limit.Session&&p.BranchId==limit.Branch&&p.ActorUserId==RequestUserId&&posted.Add(e),
                _=>false
            };
            if(!allowed)throw new ResourceScopeException();
        }
        limit.Renewals=renewals;limit.Payments=payments;limit.Collections=collections;limit.Receipts=receipts;limit.Periods=periods;limit.Posted=posted;
    }
}
