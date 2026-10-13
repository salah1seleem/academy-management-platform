using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public sealed record CashCollectionResultRequest(string Key,string? Reference=null);
public sealed partial class CashRegisterService
{
    public async Task<object> CollectionResult(Guid id,CashCollectionResultRequest body,CashPaymentReferences references)
    {
        var(t,user)=await Context();var session=await Session(id);
        if(string.IsNullOrWhiteSpace(body.Key)||body.Key.Length is <8 or >100)throw new CashRejected("معرف المحاولة غير صالح.",422);
        if(session.OpenedByMembershipId!=t.MembershipId)throw new CashRejected("استعادة نتيجة محاولات ورديتك الشخصية فقط.",403);
        await using var tx=await db.Database.BeginTransactionAsync();
        // Wait behind any in-flight request with this exact actor/key before
        // reporting an unknown outcome. Never create a payment by checking it.
        var key=await Request(t.AcademyId,user,"collect",body.Key);
        var prior=await db.CashReceiptPostings.AsNoTracking().Where(p=>p.SessionId==id&&p.RequestKey==key).Select(p=>new{p.ReceiptId}).SingleOrDefaultAsync();
        if(prior is not null)return new{status="Confirmed",receiptId=prior.ReceiptId,canDiscard=false};
        var closed=await db.CashSessions.Where(s=>s.Id==id).Select(s=>s.ClosedAtUtc.HasValue).SingleAsync();
        var expired=false;
        if(body.Reference is not null)expired=references.Resolve(body.Reference,new(t,user,id,session.BranchId),false).ExpiresAt<=time.GetUtcNow();
        // A late old request cannot newly collect after a closed shift or an
        // expired protected reference. An unresolved OPEN normal request is not discarded.
        return new{status="NotRecorded",canDiscard=closed||expired};
    }
}
