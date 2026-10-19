using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;

public sealed class VipRewardConflict(string message):Exception(message);
public static partial class VipRewards
{
    public const string Conditions="خصم لباقة واحدة في اشتراك أو طلب تجديد واحد، دون جمع العروض. يطبق فقط إذا خفّض أفضل سعر قائم. السعر المحجوز قبل الانتهاء محفوظ حتى حسم الطلب؛ لا يعاد الاستحقاق بعد السداد أو الاسترداد.";
    internal static IQueryable<VipRewardUse> BlockingUses(FoundationDbContext db,Guid academy)=>db.VipRewardUses.Where(u=>u.AcademyId==academy&&u.ReleasedAtUtc==null&&(u.UsedAtUtc!=null||
        !(u.SourceKind=="Family"&&db.FamilyRenewalRequests.Any(f=>f.Id==u.SourceId&&f.AcademyId==academy&&(f.Status==FamilyRenewalStatus.Cancelled||f.Status==FamilyRenewalStatus.Rejected))
        ||u.SourceKind=="Enrollment"&&db.NewEnrollmentRequests.Any(r=>r.Id==u.SourceId&&r.AcademyId==academy&&(r.Status==NewEnrollmentRequestStatus.Cancelled||r.Status==NewEnrollmentRequestStatus.Rejected))
        ||u.SourceKind=="Renewal"&&db.RenewalRequests.Any(r=>r.Id==u.SourceId&&r.AcademyId==academy&&db.PaymentRequests.Any(p=>p.Id==r.PaymentRequestId&&p.AcademyId==academy&&(p.Status==PaymentRequestStatus.Cancelled||p.Status==PaymentRequestStatus.Failed||p.Status==PaymentRequestStatus.Expired))))));
    internal static IQueryable<VipReward> Available(FoundationDbContext db,Guid academy,DateOnly day)=>db.VipRewards.AsNoTracking().Where(r=>r.AcademyId==academy&&r.CancelledAtUtc==null&&r.IssuedOn<=day&&r.ExpiresOn>=day&&!BlockingUses(db,academy).Any(u=>u.RewardId==r.Id));
    internal static async Task<DiscountRule[]> RulesForPlayer(FoundationDbContext db,Guid academy,Guid player,Guid actor,DateOnly day,Guid? guardian=null)
    {
        var staff=await db.AcademyMemberships.AsNoTracking().AnyAsync(m=>m.AcademyId==academy&&m.UserId==actor&&m.IsActive&&m.User.IsActive&&(m.Role==AcademyRole.AcademyOwner||m.Role==AcademyRole.AcademyAdmin));
        // Internal eligibility only: no foreign branch's child or guardian identity is returned.
        var guardians=db.GuardianProfiles.IgnoreQueryFilters().Where(g=>g.AcademyId==academy&&g.IsActive&&(staff||g.UserId==actor)&&(!guardian.HasValue||g.Id==guardian)&&
            (player==Guid.Empty&&guardian.HasValue||db.GuardianPlayerLinks.IgnoreQueryFilters().Any(l=>l.AcademyId==academy&&l.GuardianId==g.Id&&l.PlayerId==player&&l.IsActive&&l.Player.IsActive))).Select(g=>g.Id);
        var ids=Available(db,academy,day).Where(r=>guardians.Contains(r.GuardianId)).Select(r=>r.DiscountRuleId);
        return await db.DiscountRules.IgnoreQueryFilters().AsNoTracking().Where(r=>r.AcademyId==academy&&r.IsActive&&ids.Contains(r.Id)).ToArrayAsync();
    }
    internal static async Task<Guid?> Reserve(FoundationDbContext db,Guid academy,Guid? rule,string kind,Guid source,Guid actor,DateOnly day,DateTimeOffset now,Guid? player=null,Guid? guardian=null)
    {
        if(!rule.HasValue)return null;
        var reward=await db.VipRewards.AsNoTracking().SingleOrDefaultAsync(r=>r.AcademyId==academy&&r.DiscountRuleId==rule);if(reward is null)return null;
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("VIP reservation requires the checkout transaction.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"vip-reward:{academy}:{reward.Id}"},0))");
        reward=await db.VipRewards.AsNoTracking().SingleAsync(r=>r.Id==reward.Id);
        var previous=await db.VipRewardUses.Where(u=>u.RewardId==reward.Id&&u.ReleasedAtUtc==null).SingleOrDefaultAsync();
        if(previous is not null&&previous.SourceKind==kind&&previous.SourceId==source)return previous.Id;
        if(previous is not null){if(await BlockingUses(db,academy).AnyAsync(u=>u.Id==previous.Id))throw new VipRewardConflict("المكافأة مستخدمة أو محجوزة لطلب آخر. راجع السعر قبل المتابعة.");previous.ReleasedAtUtc=now;previous.UpdatedAtUtc=now;await db.SaveChangesAsync();}
        if(reward.CancelledAtUtc!=null||reward.ExpiresOn<day||reward.IssuedOn>day)throw new VipRewardConflict("انتهى استحقاق المكافأة أو أُلغي. حدّث السعر قبل الدفع.");
        var eligible=await RulesForPlayer(db,academy,player??Guid.Empty,actor,day,guardian);
        if(!eligible.Any(r=>r.Id==rule))throw new VipRewardConflict("المكافأة لا تخص هذه الأسرة أو تغير استحقاقها.");
        var use=new VipRewardUse{Id=Guid.NewGuid(),AcademyId=academy,RewardId=reward.Id,SourceKind=kind,SourceId=source,ActorUserId=actor,CreatedAtUtc=now,UpdatedAtUtc=now};db.VipRewardUses.Add(use);return use.Id;
    }
    internal static async Task Consume(FoundationDbContext db,RenewalRequest r,DateTimeOffset now)
    {
        if(!r.DiscountRuleIdSnapshot.HasValue)return;
        var reward=await db.VipRewards.AsNoTracking().SingleOrDefaultAsync(x=>x.AcademyId==r.AcademyId&&x.DiscountRuleId==r.DiscountRuleIdSnapshot);if(reward is null)return;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"vip-reward:{r.AcademyId}:{reward.Id}"},0))");
        var use=await db.VipRewardUses.SingleOrDefaultAsync(u=>u.Id==r.VipRewardUseId&&u.RewardId==reward.Id&&u.ReleasedAtUtc==null);
        if(use is null||reward.CancelledAtUtc!=null||use.SourceKind=="Renewal"&&use.SourceId!=r.Id||use.SourceKind=="Family"&&!await db.FamilyRenewalRequests.AnyAsync(f=>f.Id==use.SourceId&&f.VipRewardUseId==use.Id&&f.Status==FamilyRenewalStatus.Pending))throw new VipRewardConflict("تغير حجز المكافأة؛ لا يمكن تأكيد هذا السعر دون مراجعة الطلب.");
        if(use.SourceKind=="Enrollment"&&!await db.NewEnrollmentRequests.AnyAsync(e=>e.Id==use.SourceId&&e.CreatedSportEnrollmentId==r.SportEnrollmentId&&e.Status==NewEnrollmentRequestStatus.Approved&&e.DiscountRuleIdSnapshot==reward.DiscountRuleId))throw new VipRewardConflict("طلب الاشتراك لا يطابق حجز المكافأة.");
        var player=await db.SportEnrollments.Where(e=>e.Id==r.SportEnrollmentId).Select(e=>e.PlayerId).SingleAsync();
        if(!await db.GuardianPlayerLinks.IgnoreQueryFilters().AnyAsync(l=>l.AcademyId==r.AcademyId&&l.GuardianId==reward.GuardianId&&l.PlayerId==player&&l.IsActive&&l.Guardian.IsActive&&l.Player.IsActive))throw new VipRewardConflict("تغير ارتباط الأسرة باللاعب؛ راجع الطلب قبل التحصيل.");
        if(use.UsedAtUtc==null){use.UsedAtUtc=now;use.UpdatedAtUtc=now;}
    }
}
