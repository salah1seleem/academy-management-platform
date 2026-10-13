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
    private sealed record CashPlanChoice(Guid Id, string Name, Guid SportId, TrainingFormat TrainingFormat);
    private sealed record PricedCashCollection(SubscriptionPlan Plan, RenewalFinancialSnapshot Price, string Hash, Guid EnrollmentBranchId, Guid CollectionBranchId, string EnrollmentBranch, string CollectionBranch);
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
        // Owner already has trusted academy-wide visibility. No branch or tenant
        // filter is disabled here; scoped administrators retain the same-branch path.
        var crossBranch=t.Role==AcademyRole.AcademyOwner;
        var collectionBranch=await db.Branches.Where(b=>b.Id==session.BranchId).Select(b=>b.ArabicName).SingleAsync();
        if(string.IsNullOrEmpty(search)||search.Length<2)return new{items=Array.Empty<object>(),crossBranch,collectionBranch};
        var items=await db.SportEnrollments.AsNoTracking().Where(e=>(crossBranch||e.BranchId==session.BranchId)&&e.IsActive&&e.Status==EnrollmentStatus.Active&&e.Player.IsActive&&e.Sport.IsActive&&e.Branch.IsActive&&(e.Player.ArabicName.Contains(search)||e.Player.PlayerCode.Contains(search)))
            .OrderBy(e=>e.Player.ArabicName).ThenBy(e=>e.Id).Take(30).Select(e=>new{enrollmentId=e.Id,player=e.Player.ArabicName,code=e.Player.PlayerCode,sport=e.Sport.ArabicName,e.SportId,e.BranchId,enrollmentBranch=e.Branch.ArabicName,e.RegistrationFormat}).ToListAsync();
        var plansByBranch=new Dictionary<Guid,List<CashPlanChoice>>();
        foreach(var branch in items.Select(e=>e.BranchId).Distinct())
            plansByBranch[branch]=await db.SubscriptionPlans.TrainingOnly().ForBranch(branch).Where(p=>p.IsActive&&p.Sport.IsActive&&p.Currency==register.Currency).OrderBy(p=>p.ArabicName).Select(p=>new CashPlanChoice(p.Id,p.ArabicName,p.SportId,p.TrainingFormat)).ToListAsync();
        return new{crossBranch,collectionBranch,items=items.Select(e=>new{e.enrollmentId,e.player,e.code,e.sport,e.BranchId,e.enrollmentBranch,plans=plansByBranch[e.BranchId].Where(p=>p.SportId==e.SportId&&PlanCatalog.FormatMatches(e.RegistrationFormat,p.TrainingFormat)).Select(p=>new{p.Id,name=p.Name})})};
    }
    private async Task<PricedCashCollection> Price(Guid id,CashCollectionQuote body,TenantMembership t,Guid user,DiscountRules discounts)
    {
        var(session,register)=await CollectingSession(id,t);
        var enrollment=await db.SportEnrollments.AsNoTracking().Include(e=>e.Branch).SingleOrDefaultAsync(e=>e.Id==body.EnrollmentId&&(t.Role==AcademyRole.AcademyOwner||e.BranchId==session.BranchId)&&e.IsActive&&e.Status==EnrollmentStatus.Active&&e.Player.IsActive&&e.Sport.IsActive&&e.Branch.IsActive);
        if(enrollment is null)throw new CashRejected("تسجيل اللاعب غير متاح لفرع الصندوق.",404);
        var plan=await db.SubscriptionPlans.TrainingOnly().AsNoTracking().ForBranch(enrollment.BranchId).SingleOrDefaultAsync(p=>p.Id==body.PlanId&&p.SportId==enrollment.SportId&&p.IsActive&&p.Currency==register.Currency);
        if(plan is null)throw new CashRejected("الباقة غير متاحة لهذا التسجيل.",404);
        if(!PlanCatalog.FormatMatches(enrollment.RegistrationFormat,plan.TrainingFormat))throw new CashRejected("اختر باقة تطابق نوع تسجيل اللاعب.",422);
        var day=await AttendanceCalendar.TodayAsync(t.AcademyId,db,time);
        var assignedGroupId=await db.SportEnrollments.Where(e=>e.Id==enrollment.Id).Select(e=>FoundationDbContext.AssignedGroupAt(t.AcademyId,e.Id,day)).SingleAsync();
        if(!await db.TrainingGroups.AnyAsync(g=>g.Id==assignedGroupId&&g.IsActive&&g.BranchId==enrollment.BranchId&&g.SportId==enrollment.SportId))throw new CashRejected("أسند اللاعب لمجموعة فعالة قبل التحصيل.");
        var price=await discounts.CalculateAsync(t.AcademyId,enrollment.PlayerId,plan.Price,user,plan:plan,branchId:enrollment.BranchId);
        if(price.FinalAmount<=0)throw new CashRejected("الخدمة بلا مبلغ نقدي مستحق؛ لا تنشئ إيصال قبض صفريًا.",422);
        // A second key must not collect the same reviewed renewal twice. The
        // enrollment row is locked during collection, so period/attendance/transfer
        // changes invalidate a review before any financial write.
        var service=await db.SubscriptionPeriods.AsNoTracking().Where(p=>p.SportEnrollmentId==enrollment.Id).OrderBy(p=>p.Id)
            .Select(p=>new{p.Id,p.Version,p.StartDate,p.EndDate,p.Status,p.RemainingSessions}).ToArrayAsync();
        var hash=AcademyConfigurationService.Hash(JsonSerializer.Serialize(new{id,body.EnrollmentId,body.PlanId,user,enrollment.BranchId,collectionBranchId=session.BranchId,assignedGroupId,subscriptionClock.Today,service,terms=PlanTerms.From(plan),plan.Price,plan.Currency,price.OriginalAmount,price.DiscountType,price.DiscountValue,price.DiscountAmount,price.FinalAmount,price.DiscountReason,price.DiscountRuleId,price.DiscountRuleRevision}));
        var collectionBranch=await db.Branches.Where(b=>b.Id==session.BranchId).Select(b=>b.ArabicName).SingleAsync();
        return new(plan,price,hash,enrollment.BranchId,session.BranchId,enrollment.Branch.ArabicName,collectionBranch);
    }
    public async Task<object> CollectionQuote(Guid id,CashCollectionQuote body,DiscountRules discounts)
    {
        var(t,user)=await Context(write:true);var q=await Price(id,body,t,user,discounts);
        return new{quoteHash=q.Hash,planName=q.Plan.ArabicName,originalAmount=Money(q.Price.OriginalAmount),discountAmount=Money(q.Price.DiscountAmount),amount=Money(q.Price.FinalAmount),q.Plan.Currency,q.Price.DiscountReason,q.EnrollmentBranchId,q.CollectionBranchId,q.EnrollmentBranch,q.CollectionBranch};
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
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"renewal-enrollment:{t.AcademyId:N}:{body.EnrollmentId:N}"}, 0))");
        // Existing confirmations take this common lock before inserting periods.
        // Take it before the row lock to avoid a row/FK versus confirmation cycle.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"collection-enrollment:{t.AcademyId}:{body.EnrollmentId}"}, 0))");
        // Group transfers also lock this enrollment row before writing their
        // dated assignment. Keep the reviewed service location stable through commit.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SportEnrollments\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\"={body.EnrollmentId} FOR UPDATE");
        var q=await Price(id,new(body.EnrollmentId,body.PlanId),t,user,discounts);
        if(q.Hash!=body.QuoteHash)throw new CashRejected("تغير الاشتراك أو السعر أو الخصم أو شروط الباقة أو تسكين اللاعب. حدّث المراجعة قبل التأكيد.");
        var session=await Session(id);
        var paymentId=await collections.CreateCashAsync(t.AcademyId,user,body.EnrollmentId,body.PlanId,"cash-"+AcademyConfigurationService.Hash(request),"Cash",fixedSnapshot:q.Price,fixedTerms:PlanTerms.From(q.Plan));
        var receipt=await db.Receipts.SingleAsync(r=>r.Collection.PaymentRequestId==paymentId);
        var now=time.GetUtcNow();var posting=new CashReceiptPosting{Id=Guid.NewGuid(),AcademyId=t.AcademyId,SessionId=id,BranchId=session.BranchId,CollectionId=receipt.CollectionId,ReceiptId=receipt.Id,Amount=receipt.Amount,Currency=receipt.Currency,ActorUserId=user,RequestKey=request,RequestHash=payload,CreatedAtUtc=now,UpdatedAtUtc=now};
        db.CashReceiptPostings.Add(posting);await db.SaveChangesAsync();await tx.CommitAsync();
        return new{receiptId=receipt.Id,postingId=posting.Id,amount=Money(posting.Amount),posting.Currency};
    }
}
