using System.Text.Json;
using Academy.Api.Configuration;
using Academy.Api.Operations;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public sealed record CashReceiptVoidWrite(Guid? CashSessionId, string? Reason = null, string? QuoteHash = null);

public sealed partial class CashRegisterService
{
    private static readonly JsonSerializerOptions VoidJson = new(JsonSerializerDefaults.Web);
    private static void VoidReason(CashReceiptVoidWrite body) { if(body.Reason?.Length>500) throw new CashRejected("السبب لا يتجاوز 500 حرف.",422); }
    private static string VoidRequestHash(Guid receipt, CashReceiptVoidWrite body) => AcademyConfigurationService.Hash(JsonSerializer.Serialize(new {receipt,body.CashSessionId,reason=Reason(body.Reason),body.QuoteHash}));
    private async Task<(Receipt Receipt,PaymentCollection Collection,SubscriptionPeriod Period)> VoidSource(Guid id)
    {
        var receipt=await db.Receipts.AsNoTracking().SingleOrDefaultAsync(r=>r.Id==id) ?? throw new CashRejected("الإيصال غير متاح.",404);
        var collection=await db.Collections.AsNoTracking().Include(c=>c.SportEnrollment).SingleOrDefaultAsync(c=>c.Id==receipt.CollectionId) ?? throw new CashRejected("التحصيل غير متاح.",404);
        if(receipt.PaymentMethod!="Cash" || !await db.CashReceiptPostings.AnyAsync(p=>p.ReceiptId==id && p.CollectionId==collection.Id))
            throw new CashRejected("هذا المسار لإيصال اشتراك نقدي مقيد بالصندوق فقط. لا يرحّل إيصالًا تاريخيًا تلقائيًا.");
        var periods=await db.SubscriptionPeriods.AsNoTracking().Where(p=>p.CollectionId==collection.Id).Take(2).ToArrayAsync();
        if(periods.Length!=1) throw new CashRejected("يلزم مراجعة فترة الخدمة المرتبطة بالإيصال قبل عكسه.");
        return(receipt,collection,periods[0]);
    }
    private async Task UnusedVoidSource(Receipt receipt,PaymentCollection collection,SubscriptionPeriod period)
    {
        if(await db.CashReceiptVoids.AnyAsync(v=>v.ReceiptId==receipt.Id)) throw new CashRejected("الإيصال معكوس بالفعل؛ راجع حركة العكس المسجلة.");
        var used=await CollectionFunds.Used(db,collection.AcademyId,collection.Id);
        if(used.Refunded!=0 || used.Allocated!=0) throw new CashRejected("للتحصيل استرداد أو تخصيص سابق. لا يعكس الإيصال كاملًا؛ راجع التسوية المالية.");
        if(period.Status is SubscriptionPeriodStatus.Cancelled or SubscriptionPeriodStatus.Frozen || period.Origin!=SubscriptionOrigin.Collected ||
           await db.SubscriptionAdjustments.AnyAsync(a=>a.SubscriptionPeriodId==period.Id) ||
           await db.AccessEntitlements.AnyAsync(a=>a.SubscriptionPeriodId==period.Id) ||
           await db.CompensationGrants.AnyAsync(g=>g.SourcePeriodId==period.Id) ||
           period.RemainingSessions!=period.InitialSessions || await db.SubscriptionSessionMovements.AnyAsync(m=>m.SubscriptionPeriodId==period.Id) ||
           await db.PlayerAttendances.AnyAsync(a=>a.SportEnrollmentId==period.SportEnrollmentId && (a.Status==AttendanceStatus.Present||a.Status==AttendanceStatus.Late) &&
               (a.ConsumedSubscriptionPeriodId==period.Id || a.ConsumedSubscriptionPeriodId==null && a.TrainingSession.SessionDate>=period.StartDate && (period.EndDate==null||a.TrainingSession.SessionDate<=period.EndDate))))
            throw new CashRejected("فترة الاشتراك مستخدمة أو معدلة. لا يمكن عكس إيصالها كاملًا؛ استخدم مراجعة الاسترداد والتسوية.");
    }
    private static RefundWrite VoidRefund(PaymentCollection c,CashReceiptVoidWrite body,string? quote=null) => new(c.Id,c.Amount,Reason(body.Reason),body.CashSessionId,quote);
    private static string VoidQuoteHash(Guid receipt,SubscriptionPeriod p,string cashHash,CashReceiptVoidWrite body) => AcademyConfigurationService.Hash(JsonSerializer.Serialize(new {purpose="cash-receipt-void-v1",receipt,p.Id,p.Version,p.Status,p.StartDate,p.EndDate,p.RemainingSessions,cashHash,reason=Reason(body.Reason)}));
    private async Task<object> VoidReview(Guid id,CashReceiptVoidWrite body,bool selected)
    {
        await Context(owner:true,write:true);VoidReason(body);
        if(selected && body.CashSessionId is null) throw new CashRejected("اختر وردية الصرف أولًا.",422);
        var (receipt,collection,period)=await VoidSource(id);await UnusedVoidSource(receipt,collection,period);
        var cash=JsonSerializer.SerializeToElement(await RefundQuote(collection.SportEnrollment.PlayerId,VoidRefund(collection,body)),VoidJson);
        var hash=cash.GetProperty("quoteHash").GetString();
        return new {receipt.Id,receipt.ReceiptNumber,playerId=collection.SportEnrollment.PlayerId,player=receipt.PlayerNameSnapshot,plan=receipt.PlanNameSnapshot,
            amount=Money(collection.Amount),collection.Currency,periodId=period.Id,period.StartDate,period.EndDate,period.RemainingSessions,
            sessions=cash.GetProperty("sessions"),cashEnabled=cash.GetProperty("cashEnabled"),balance=cash.GetProperty("balance"),
            quoteHash=hash is null?null:VoidQuoteHash(id,period,hash,body),subscriptionWillBeCancelled=true,originalReceiptPreserved=true,debtUnchanged=true};
    }
    public Task<object> VoidOptions(Guid id) => VoidReview(id,new(null),false);
    public Task<object> VoidQuote(Guid id,CashReceiptVoidWrite body) => VoidReview(id,body,true);
    public async Task<object> VoidReceipt(Guid id,CashReceiptVoidWrite body,string key)
    {
        var (t,user)=await Context(owner:true,write:true);VoidReason(body);
        if(string.IsNullOrWhiteSpace(key)||key.Length is <8 or >100||body.CashSessionId is not Guid session || body.QuoteHash?.Length!=64)
            throw new CashRejected("اختر ورديتك وراجع العكس ثم أعد الإرسال بمعرف صحيح.",422);
        await using var tx=await db.Database.BeginTransactionAsync();
        await Request(t.AcademyId,user,"void",key);var hash=VoidRequestHash(id,body);
        var prior=await db.CashReceiptVoids.AsNoTracking().SingleOrDefaultAsync(v=>v.ActorUserId==user && v.RequestKey==key);
        if(prior is not null) {Same(prior.RequestHash,hash);return new {prior.Id,prior.ReceiptId,prior.RefundId,prior.SubscriptionPeriodId,replay=true};}
        var source=await VoidSource(id);var collection=source.Collection;
        await CollectionFunds.Lock(db,t.AcademyId,collection.Id);
        // Pool -> cash shift -> enrollment -> period. Attendance locks the same
        // enrollment before delivering service; cash collection uses shift first.
        await Lock(t.AcademyId,$"session:{session}");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SportEnrollments\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\"={collection.SportEnrollmentId} FOR UPDATE");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SubscriptionPeriods\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\"={source.Period.Id} FOR UPDATE");
        var period=await db.SubscriptionPeriods.SingleAsync(p=>p.Id==source.Period.Id);
        await UnusedVoidSource(source.Receipt,collection,period);
        var refundBody=VoidRefund(collection,body);var review=JsonSerializer.SerializeToElement(await RefundQuote(collection.SportEnrollment.PlayerId,refundBody),VoidJson);
        var cashHash=review.GetProperty("quoteHash").GetString()!;
        if(body.QuoteHash!=VoidQuoteHash(id,period,cashHash,body)) throw new CashRejected("تغير الاشتراك أو رصيد الوردية. حدّث المراجعة قبل العكس.");
        refundBody=refundBody with {QuoteHash=cashHash};
        await PrepareRefund(collection.SportEnrollment.PlayerId,refundBody,collection,collection.Amount);
        var at=time.GetUtcNow();var refund=new CollectionRefund {Id=Guid.NewGuid(),AcademyId=t.AcademyId,CollectionId=collection.Id,PlayerId=collection.SportEnrollment.PlayerId,
            Amount=collection.Amount,Currency=collection.Currency,PayoutMethod="Cash",Reason=Reason(body.Reason),PerformedByUserId=user,IdempotencyKey=$"void:{key}",CreatedAtUtc=at,UpdatedAtUtc=at};
        var snapshot=JsonSerializer.Serialize(new {period.Id,period.Version,period.StartDate,period.EndDate,period.RemainingSessions,beforeStatus=period.Status.ToString(),afterStatus="Cancelled",amount=Money(collection.Amount),collection.Currency});
        var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at,TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")).DateTime);
        var adjustment=new SubscriptionAdjustment {Id=Guid.NewGuid(),AcademyId=t.AcademyId,SubscriptionPeriodId=period.Id,AdjustmentType=SubscriptionAdjustmentType.Cancelled,
            EffectiveDate=day,OldEndDate=period.EndDate,Reason=Reason(body.Reason)??"",PerformedByUserId=user,IdempotencyKey=$"void:{key}",PerformedAtUtc=at,CreatedAtUtc=at,UpdatedAtUtc=at};
        // The underlying field is limited to100, including our operation prefix.
        refund.IdempotencyKey=AcademyConfigurationService.Hash($"void:{key}");adjustment.IdempotencyKey=refund.IdempotencyKey;
        period.Status=SubscriptionPeriodStatus.Cancelled;period.FrozenFromDate=null;period.UpdatedAtUtc=at;
        db.CollectionRefunds.Add(refund);db.SubscriptionAdjustments.Add(adjustment);await db.SaveChangesAsync();
        await PostRefund(refundBody,refund);
        var posting=await db.CashRefundPostings.SingleAsync(p=>p.RefundId==refund.Id);
        var row=new CashReceiptVoid {Id=Guid.NewGuid(),AcademyId=t.AcademyId,ReceiptId=id,RefundId=refund.Id,RefundPostingId=posting.Id,SubscriptionPeriodId=period.Id,
            AdjustmentId=adjustment.Id,BranchId=posting.BranchId,ActorUserId=user,RequestKey=key,RequestHash=hash,Snapshot=snapshot,CreatedAtUtc=at,UpdatedAtUtc=at};
        db.CashReceiptVoids.Add(row);await db.SaveChangesAsync();await tx.CommitAsync();
        return new {row.Id,row.ReceiptId,row.RefundId,row.SubscriptionPeriodId,replay=false};
    }
}
