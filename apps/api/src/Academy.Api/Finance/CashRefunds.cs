using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public sealed partial class CashRegisterService
{
    public async Task AuthorizeRefund(bool readOnly = false)
    {
        var (t, _) = await Context(write: !readOnly);
        if (t.Role != AcademyRole.AcademyOwner &&
            (!await db.StaffActionGrants.AnyAsync(g => g.MembershipId == t.MembershipId && g.IsActive && g.Module == "subscriptions" && g.Read && (readOnly || g.Edit)) ||
             !await db.StaffActionGrants.AnyAsync(g => g.MembershipId == t.MembershipId && g.IsActive && g.Module == "players" && g.Read)))
            throw new CashRejected("يلزم تفويض الاسترداد المالي وتعديل الاشتراك وقراءة اللاعب.", 403);
    }
    public static string RefundHash(RefundWrite body) => AcademyConfigurationService.Hash(JsonSerializer.Serialize(body with { Reason = Reason(body.Reason) }));

    private async Task<PaymentCollection> RefundCollection(Guid player, Guid collection) =>
        await db.Collections.Include(c=>c.SportEnrollment).SingleOrDefaultAsync(c=>c.Id==collection && c.SportEnrollment.PlayerId==player)
        ?? throw new CashRejected("التحصيل غير متاح لهذا اللاعب.",404);

    private async Task<Guid> SourceCollectionBranch(PaymentCollection collection) =>
        await CashFinancialAttribution.ReceiptSources(db,collection.AcademyId).Where(p=>p.CollectionId==collection.Id).Select(p=>(Guid?)p.BranchId).SingleOrDefaultAsync()
        ?? collection.SportEnrollment.BranchId;

    public async Task<object> RefundQuote(Guid player, RefundWrite body)
    {
        await AuthorizeRefund(); var (t, _) = await Context(write:true); Amount(body.Amount,false);
        if(body.Reason?.Length>500) throw new CashRejected("السبب لا يتجاوز 500 حرف.",422);
        var collection = await RefundCollection(player,body.CollectionId);
        // A posted cash receipt is refunded at its collecting branch, even if
        // the player trains elsewhere. No arbitrary cross-branch payout is inferred.
        var collectionBranch=await SourceCollectionBranch(collection);
        var enabled=(await config.Active(t.AcademyId))?.CashEnabled==true;
        var sessions=await (from s in db.CashSessions.AsNoTracking() join r in db.CashRegisters on s.RegisterId equals r.Id
            where enabled && s.BranchId==collectionBranch && s.ClosedAtUtc==null && s.OpenedByMembershipId==t.MembershipId && r.IsActive && r.Currency==collection.Currency &&
                db.Branches.Any(b=>b.Id==r.BranchId&&b.IsActive) && db.CashierAssignments.Any(a=>a.RegisterId==r.Id&&a.MembershipId==t.MembershipId&&a.IsActive)
            orderby r.Name select new {s.Id,name=r.Name}).ToArrayAsync();
        var used=await CollectionFunds.Used(db,t.AcademyId,collection.Id);
        var available=CollectionFunds.Available(collection.Amount,used.Refunded,used.Allocated);
        string? quoteHash=null, balance=null;
        if(body.CashSessionId is Guid id) {
            var session=await RefundingSession(id,collection); var money=await Balance(session);
            if(body.Amount>available) throw new CashRejected("المبلغ يتجاوز المتاح غير المخصص. راجع الحركات المالية أولًا.");
            if(body.Amount>money) throw new CashRejected("رصيد الوردية لا يكفي لصرف الاسترداد.");
            quoteHash=RefundQuoteHash(t,player,body,collection.Currency,available,money); balance=Money(money);
        }
        return new {amount=Money(body.Amount),available=Money(available),collection.Currency,sessions,cashEnabled=enabled,quoteHash,balance,
            subscriptionUnchanged=true,debtUnchanged=true};
    }
    private static string RefundQuoteHash(TenantMembership t,Guid player,RefundWrite body,string currency,decimal available,decimal balance) =>
        AcademyConfigurationService.Hash(JsonSerializer.Serialize(new {purpose="cash-refund-v1",t.AcademyId,t.MembershipId,player,body.CollectionId,body.CashSessionId,amount=Money(body.Amount),currency,available=Money(available),balance=Money(balance)}));

    private async Task<CashSession> RefundingSession(Guid id,PaymentCollection collection)
    {
        var(t,_)=await Context(write:true); var session=await Session(id); var register=await Register(session.RegisterId);
        await Enabled(t.AcademyId); await Usable(register); await Cashier(register,t);
        if(session.ClosedAtUtc.HasValue) throw new CashRejected("الوردية مقفلة. اختر ورديتك المفتوحة للصرف.");
        if(session.OpenedByMembershipId!=t.MembershipId) throw new CashRejected("الصرف من ورديتك الشخصية فقط.",403);
        if(session.BranchId!=await SourceCollectionBranch(collection) || register.Currency!=collection.Currency) throw new CashRejected("اختر وردية من فرع التحصيل وبعملته.");
        return session;
    }
    public async Task PrepareRefund(Guid player,RefundWrite body,PaymentCollection collection,decimal available)
    {
        if(db.Database.CurrentTransaction is null) throw new InvalidOperationException("Refund transaction required.");
        await AuthorizeRefund();var(t,_)=await Context(write:true);
        if(body.CashSessionId is not Guid id || body.QuoteHash?.Length!=64) throw new CashRejected("اختر ورديتك وراجع المبلغ قبل صرف الاسترداد النقدي.",422);
        // Collection pool -> session; close/transfer never acquire collection pools.
        await Lock(t.AcademyId,$"session:{id}"); var session=await RefundingSession(id,collection);var balance=await Balance(session);
        if(body.Amount>balance) throw new CashRejected("رصيد الوردية لا يكفي لصرف الاسترداد.");
        if(body.QuoteHash!=RefundQuoteHash(t,player,body,collection.Currency,available,balance)) throw new CashRejected("تغير المتاح أو رصيد الوردية. حدّث المراجعة قبل الصرف.");
    }
    public async Task PostRefund(RefundWrite body,Academy.Infrastructure.Operations.CollectionRefund refund)
    {
        if(db.Database.CurrentTransaction is null) throw new InvalidOperationException("Refund transaction required.");
        var(t,user)=await Context(write:true);var session=await Session(body.CashSessionId!.Value);var at=refund.CreatedAtUtc;
        db.CashRefundPostings.Add(new() {Id=Guid.NewGuid(),AcademyId=t.AcademyId,SessionId=session.Id,BranchId=session.BranchId,RefundId=refund.Id,
            Amount=refund.Amount,Currency=refund.Currency,ActorUserId=user,RequestHash=RefundHash(body),CreatedAtUtc=at,UpdatedAtUtc=at});
        await db.SaveChangesAsync();
    }
}
