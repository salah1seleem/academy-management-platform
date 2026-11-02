using Academy.Infrastructure.Finance;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;
public sealed partial class CashRegisterService
{
    public async Task<object> ExternalReceiptOptions(Guid branch,string currency)
    {
        var(t,_)=await Context();var enabled=(await config.Active(t.AcademyId))?.CashEnabled==true;
        var sessions=await(from s in db.CashSessions.AsNoTracking() join r in db.CashRegisters on s.RegisterId equals r.Id where enabled&&s.BranchId==branch&&s.ClosedAtUtc==null&&s.OpenedByMembershipId==t.MembershipId&&r.IsActive&&r.Currency==currency&&db.CashierAssignments.Any(a=>a.RegisterId==r.Id&&a.MembershipId==t.MembershipId&&a.IsActive) orderby r.Name select new{s.Id,name=r.Name}).ToArrayAsync();
        return new{cashEnabled=enabled,sessions};
    }
    public async Task PrepareExternalReceipt(Guid? sessionId,Guid branch,string currency)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("External receipt transaction required.");
        if(!sessionId.HasValue||sessionId==Guid.Empty)throw new CashRejected("اختر ورديتك المفتوحة لاستلام التحصيل نقديًا.",422);
        var(t,_)=await Context(write:true);await Lock(t.AcademyId,$"session:{sessionId.Value}");
        var s=await Session(sessionId.Value);var r=await Register(s.RegisterId);await Enabled(t.AcademyId);await Usable(r);await Cashier(r,t);
        if(s.ClosedAtUtc.HasValue||s.OpenedByMembershipId!=t.MembershipId||s.BranchId!=branch||r.Currency!=currency)throw new CashRejected("الوردية لا تطابق الموظف والفرع والعملة أو أصبحت مقفلة.");
    }
    public async Task PostExternalReceipt(Guid sessionId,Guid receiptId)
    {
        var(t,user)=await Context(write:true);var receipt=await db.ExternalAgreementReceipts.SingleAsync(r=>r.Id==receiptId);
        await PrepareExternalReceipt(sessionId,receipt.BranchId,receipt.Currency);
        if(!receipt.CashPostingRequired||receipt.PaymentMethod!="Cash"||receipt.ActorUserId!=user)throw new CashRejected("الإيصال لا يطابق التحصيل النقدي الحالي.");
        var now=time.GetUtcNow();db.CashExternalReceiptPostings.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,ReceiptId=receiptId,SessionId=sessionId,BranchId=receipt.BranchId,Amount=receipt.Amount,Currency=receipt.Currency,ActorUserId=user,CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();
    }
}
