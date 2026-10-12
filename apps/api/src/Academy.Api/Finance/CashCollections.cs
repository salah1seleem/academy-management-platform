using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Operations;
using Academy.Api.Slice3;
using Academy.Api.Slice4;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public sealed record CashCollectionQuote(Guid EnrollmentId, Guid PlanId);
public sealed record CashCollectionWrite(Guid EnrollmentId, Guid PlanId, string QuoteHash);

public sealed partial class CashRegisterService
{
    private async Task<bool> CollectionPermissions(TenantMembership t, bool reject)
    {
        var allowed = t.Role==AcademyRole.AcademyOwner ||
            await db.AcademyMemberships.AnyAsync(m=>m.Id==t.MembershipId&&m.UseActionGrants) &&
            await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.IsActive&&g.Module=="finance"&&g.Read&&g.Add) &&
            await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.IsActive&&g.Module=="subscriptions"&&g.Read&&g.Add) &&
            await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.IsActive&&g.Module=="players"&&g.Read);
        if(!allowed&&reject) throw new CashRejected("يلزم تفويض التحصيل وإضافة الاشتراكات وقراءة اللاعبين.",403);
        return allowed;
    }
    private async Task<(CashSession Session,CashRegister Register)> CollectingSession(Guid id,TenantMembership t)
    {
        await CollectionPermissions(t,true);
        var session=await Session(id); var register=await Register(session.RegisterId);
        await Enabled(t.AcademyId);await Usable(register);await Cashier(register,t);
        if(session.ClosedAtUtc.HasValue)throw new CashRejected("الوردية مقفلة. افتح وردية جديدة قبل التحصيل.");
        if(session.OpenedByMembershipId!=t.MembershipId)throw new CashRejected("التحصيل من ورديتك المفتوحة فقط.",403);
        return(session,register);
    }
    public async Task<object> CollectionOptions(Guid id,string? search)
    {
        var(t,_)=await Context(write:true);var(session,register)=await CollectingSession(id,t);
        search=search?.Trim();if(search?.Length>100)throw new CashRejected("البحث حتى100حرف.",422);
        if(string.IsNullOrEmpty(search)||search.Length<2)return new{items=Array.Empty<object>()};
        var items=await db.SportEnrollments.AsNoTracking().Where(e=>e.BranchId==session.BranchId&&e.IsActive&&e.Status==EnrollmentStatus.Active&&e.Player.IsActive&&e.Sport.IsActive&&(e.Player.ArabicName.Contains(search)||e.Player.PlayerCode.Contains(search)))
            .OrderBy(e=>e.Player.ArabicName).ThenBy(e=>e.Id).Take(30).Select(e=>new{enrollmentId=e.Id,player=e.Player.ArabicName,code=e.Player.PlayerCode,sport=e.Sport.ArabicName,e.RegistrationFormat}).ToListAsync();
        var plans=await db.SubscriptionPlans.TrainingOnly().ForBranch(session.BranchId).Where(p=>p.IsActive&&p.Sport.IsActive&&p.Currency==register.Currency).OrderBy(p=>p.ArabicName).Select(p=>new{p.Id,name=p.ArabicName,p.SportId,p.TrainingFormat}).ToListAsync();
        var ids=items.Select(i=>i.enrollmentId).ToArray();
        var enrollments=await db.SportEnrollments.Where(e=>ids.Contains(e.Id)).Select(e=>new{e.Id,e.SportId}).ToDictionaryAsync(e=>e.Id);
        return new{items=items.Select(e=>new{e.enrollmentId,e.player,e.code,e.sport,plans=plans.Where(p=>p.SportId==enrollments[e.enrollmentId].SportId&&PlanCatalog.FormatMatches(e.RegistrationFormat,p.TrainingFormat)).Select(p=>new{p.Id,p.name})})};
    }
    private async Task<(SubscriptionPlan Plan,RenewalFinancialSnapshot Price,string Hash)> Price(Guid id,CashCollectionQuote body,TenantMembership t,Guid user,DiscountRules discounts)
    {
        var(session,register)=await CollectingSession(id,t);
        var enrollment=await db.SportEnrollments.AsNoTracking().SingleOrDefaultAsync(e=>e.Id==body.EnrollmentId&&e.BranchId==session.BranchId&&e.IsActive&&e.Status==EnrollmentStatus.Active&&e.Player.IsActive&&e.Sport.IsActive);
        if(enrollment is null)throw new CashRejected("تسجيل اللاعب غير متاح لفرع الصندوق.",404);
        var plan=await db.SubscriptionPlans.TrainingOnly().AsNoTracking().ForBranch(session.BranchId).SingleOrDefaultAsync(p=>p.Id==body.PlanId&&p.SportId==enrollment.SportId&&p.IsActive&&p.Currency==register.Currency);
        if(plan is null)throw new CashRejected("الباقة غير متاحة لهذا التسجيل.",404);
        if(!PlanCatalog.FormatMatches(enrollment.RegistrationFormat,plan.TrainingFormat))throw new CashRejected("اختر باقة تطابق نوع تسجيل اللاعب.",422);
        var day=await AttendanceCalendar.TodayAsync(t.AcademyId,db,time);
        if(!await db.TrainingGroups.AnyAsync(g=>g.Id==FoundationDbContext.AssignedGroupAt(t.AcademyId,enrollment.Id,day)&&g.IsActive&&g.BranchId==enrollment.BranchId&&g.SportId==enrollment.SportId))throw new CashRejected("أسند اللاعب لمجموعة فعالة قبل التحصيل.");
        var price=await discounts.CalculateAsync(t.AcademyId,enrollment.PlayerId,plan.Price,user,plan:plan,branchId:session.BranchId);
        if(price.FinalAmount<=0)throw new CashRejected("الخدمة بلا مبلغ نقدي مستحق؛ لا تنشئ إيصال قبض صفريًا.",422);
        // Time of quote is not part of the fingerprint; only contractual values.
        var hash=AcademyConfigurationService.Hash(JsonSerializer.Serialize(new{id,body.EnrollmentId,body.PlanId,user,terms=PlanTerms.From(plan),plan.Price,plan.Currency,price.OriginalAmount,price.DiscountType,price.DiscountValue,price.DiscountAmount,price.FinalAmount,price.DiscountReason,price.DiscountRuleId,price.DiscountRuleRevision}));
        return(plan,price,hash);
    }
    public async Task<object> CollectionQuote(Guid id,CashCollectionQuote body,DiscountRules discounts)
    {
        var(t,user)=await Context(write:true);var(plan,price,hash)=await Price(id,body,t,user,discounts);
        return new{quoteHash=hash,planName=plan.ArabicName,originalAmount=Money(price.OriginalAmount),discountAmount=Money(price.DiscountAmount),amount=Money(price.FinalAmount),plan.Currency,price.DiscountReason};
    }
    public async Task<object> Collect(Guid id,CashCollectionWrite body,string key,DiscountRules discounts,CollectionService collections)
    {
        var(t,user)=await Context(write:true);await CollectionPermissions(t,true);
        if(body.QuoteHash?.Length!=64)throw new CashRejected("راجع السعر قبل التحصيل.",422);
        await using var tx=await db.Database.BeginTransactionAsync();var request=await Request(t.AcademyId,user,"collect",key);
        var payload=AcademyConfigurationService.Hash(JsonSerializer.Serialize(new{id,body.EnrollmentId,body.PlanId,body.QuoteHash}));
        var prior=await db.CashReceiptPostings.SingleOrDefaultAsync(p=>p.RequestKey==request);
        if(prior is not null){Same(prior.RequestHash,payload);return new{receiptId=prior.ReceiptId,postingId=prior.Id,replay=true};}
        // Fixed ordering: plan -> cash session -> enrollment. Close/transfer take only
        // session locks; no reverse acquisition of a plan lock from those paths.
        await PlanCatalog.LockAsync(db,t.AcademyId,body.PlanId);await Lock(t.AcademyId,$"session:{id}");
        var(plan,price,hash)=await Price(id,new(body.EnrollmentId,body.PlanId),t,user,discounts);
        if(hash!=body.QuoteHash)throw new CashRejected("تغير السعر أو الخصم أو شروط الباقة. حدّث المراجعة ثم أكد المبلغ الجديد.");
        var session=await Session(id);
        var paymentId=await collections.CreateCashAsync(t.AcademyId,user,body.EnrollmentId,body.PlanId,"cash-"+AcademyConfigurationService.Hash(request),"Cash",fixedSnapshot:price,fixedTerms:PlanTerms.From(plan));
        var receipt=await db.Receipts.SingleAsync(r=>r.Collection.PaymentRequestId==paymentId);
        var now=time.GetUtcNow();var posting=new CashReceiptPosting{Id=Guid.NewGuid(),AcademyId=t.AcademyId,SessionId=id,BranchId=session.BranchId,CollectionId=receipt.CollectionId,ReceiptId=receipt.Id,Amount=receipt.Amount,Currency=receipt.Currency,ActorUserId=user,RequestKey=request,RequestHash=payload,CreatedAtUtc=now,UpdatedAtUtc=now};
        db.CashReceiptPostings.Add(posting);await db.SaveChangesAsync();await tx.CommitAsync();
        return new{receiptId=receipt.Id,postingId=posting.Id,amount=Money(posting.Amount),posting.Currency};
    }
}
