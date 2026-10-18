using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Facilities;
using Academy.Api.Slice4;
using Academy.Api.Slice5;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.People;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public sealed record TechnicalPromotionWrite(Guid GroupId,uint EnrollmentVersion,string? Fingerprint,Guid RequestId);
// Reuses dated membership and level-review history; no second placement engine.
public sealed partial class GroupPlacementService
{
    public async Task<object> TechnicalPromotionOptions(Guid requestId,HttpContext h)
    {
        await using var tx=await db.Database.BeginTransactionAsync();
        var t=await Authority("players","Edit");await Authority("academy","Edit");
        if(!await PlayerTechnicalReviewEndpoints.CanDecide(t,db,h))throw new FacilityRejected("تنفيذ التوصية يحتاج قراءة التقييمات وتعديل اللاعبين.",403);
        var r=await db.PlayerTechnicalReviewRequests.AsNoTracking().SingleOrDefaultAsync(r=>r.Id==requestId)??throw new FacilityRejected("المراجعة غير متاحة.",404);
        var e=await db.SportEnrollments.AsNoTracking().SingleOrDefaultAsync(e=>e.Id==r.SportEnrollmentId&&e.IsActive&&e.Player.IsActive)??throw new FacilityRejected("التسجيل غير متاح.",404);
        var d=await db.PlayerTechnicalReviewDecisions.AsNoTracking().SingleOrDefaultAsync(d=>d.TechnicalRequestId==r.Id&&d.Outcome=="ReadyForPlacement")??throw new FacilityRejected("لا توجد توصية استعداد قابلة للتنفيذ.",409);
        var applied=await db.PlayerLevelReviews.AsNoTracking().Where(x=>x.TechnicalDecisionId==d.Id).Select(x=>new{x.Id,x.ToLevelName,x.GroupTransferId,x.CreatedAtUtc}).SingleOrDefaultAsync();
        var p=await db.PlayerSkillPlans.AsNoTracking().SingleAsync(p=>p.Id==r.PlanId);var evidence=await PlayerSkillEvidence.Read(p,e.TrainingLevelId,db);
        var groups=await db.TrainingGroups.AsNoTracking().Where(g=>g.IsActive&&g.BranchId==e.BranchId&&g.SportId==e.SportId&&g.TrainingLevelId==d.RecommendedLevelId&&g.RegistrationFormat==e.RegistrationFormat&&g.AgeCategory.IsActive).OrderBy(g=>g.ArabicName).Select(g=>new{g.Id,name=g.ArabicName,g.Capacity}).Take(101).ToArrayAsync();
        return new{enrollmentVersion=e.Version,effectiveDate=await AttendanceCalendar.TodayAsync(t.AcademyId,db,time),recommendedLevel=d.RecommendedLevelName,applied,canPreview=applied==null&&evidence.Eligible&&evidence.Head==r.SourceReviewSequence,groups=groups.Take(100),hasMore=groups.Length>100};
    }
    public async Task<object> TechnicalPromotion(Guid requestId,TechnicalPromotionWrite b,bool save,HttpContext h)
    {
        if(b.GroupId==Guid.Empty||b.RequestId==Guid.Empty)throw new FacilityRejected("اختر مجموعة ومعرف عملية صالحين.",422);
        await using var tx=await db.Database.BeginTransactionAsync();var t=await Authority("players","Edit");await Authority("academy","Edit");
        if(!await PlayerTechnicalReviewEndpoints.CanDecide(t,db,h))throw new FacilityRejected("صلاحية تنفيذ التوصية غير متاحة حاليًا.",403);
        var r=await db.PlayerTechnicalReviewRequests.AsNoTracking().SingleOrDefaultAsync(r=>r.Id==requestId)??throw new FacilityRejected("المراجعة غير متاحة.",404);
        var e=await db.SportEnrollments.AsNoTracking().SingleOrDefaultAsync(e=>e.Id==r.SportEnrollmentId&&e.IsActive&&e.Player.IsActive)??throw new FacilityRejected("التسجيل غير متاح.",404);
        await Authority("players","Edit",e.BranchId);
        var target=await db.TrainingGroups.AsNoTracking().SingleOrDefaultAsync(g=>g.Id==b.GroupId&&g.IsActive)??throw new FacilityRejected("المجموعة غير متاحة.",404);await Authority("academy","Edit",target.BranchId);
        var d=await db.PlayerTechnicalReviewDecisions.AsNoTracking().SingleOrDefaultAsync(d=>d.TechnicalRequestId==r.Id&&d.Outcome=="ReadyForPlacement")??throw new FacilityRejected("لا توجد توصية استعداد قابلة للتنفيذ.",409);
        var hash=AcademyConfigurationService.Hash(JsonSerializer.Serialize(new{requestId,b}));
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"technical-promotion:{t.AcademyId}:{db.RequestUserId}:{b.RequestId}"},0))");
        var prior=await db.PlayerLevelReviews.AsNoTracking().SingleOrDefaultAsync(x=>x.ActorUserId==db.RequestUserId&&x.RequestId==b.RequestId);
        if(prior!=null){if(prior.TechnicalDecisionId!=d.Id||prior.RequestHash!=hash)throw new FacilityRejected("معرف العملية مستخدم لبيانات أخرى.",409);return new{prior.Id,prior.ToLevelName,prior.GroupTransferId,replayed=true};}
        if(await db.PlayerLevelReviews.AnyAsync(x=>x.TechnicalDecisionId==d.Id))throw new FacilityRejected("نفذت هذه التوصية بالفعل؛ افتح سجل المستوى.",409);
        var today=await AttendanceCalendar.TodayAsync(t.AcademyId,db,time);
        // Immediate, explicitly confirmed promotion only. Future level changes
        // require dated level semantics, not a premature current-level update.
        var preview=await InspectTransfer(e.Id,new(b.GroupId,today),save,d.RecommendedLevelId);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"PlayerSkillPlans\" WHERE \"Id\"={r.PlanId} AND \"AcademyId\"={t.AcademyId} FOR SHARE");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SkillCurricula\" WHERE \"Id\"={r.CurriculumId} AND \"AcademyId\"={t.AcademyId} FOR SHARE");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"TrainingLevels\" WHERE \"AcademyId\"={t.AcademyId} AND (\"Id\"={r.TrainingLevelId} OR \"Id\"={d.RecommendedLevelId}) ORDER BY \"Id\" FOR SHARE");
        e=await db.SportEnrollments.AsNoTracking().SingleAsync(x=>x.Id==e.Id);
        var p=await db.PlayerSkillPlans.AsNoTracking().SingleAsync(p=>p.Id==r.PlanId);var source=await PlayerSkillEvidence.Read(p,e.TrainingLevelId,db);
        var level=await db.TrainingLevels.AsNoTracking().SingleOrDefaultAsync(l=>l.Id==d.RecommendedLevelId&&l.SportId==r.SportId&&l.IsActive);
        if(!source.Eligible||source.Head!=r.SourceReviewSequence||e.TrainingLevelId!=r.TrainingLevelId||level==null||level.Rank<=await db.TrainingLevels.Where(l=>l.Id==r.TrainingLevelId).Select(l=>l.Rank).SingleAsync())throw new FacilityRejected("تغيرت أدلة الاستعداد أو المستوى؛ يلزم طلب ومراجعة حديثان قبل التنفيذ.",409);
        if(e.Version!=b.EnrollmentVersion)throw new FacilityRejected("تغير تسجيل اللاعب؛ أعد فتح المعاينة.",409);
        var fingerprint=AcademyConfigurationService.Hash(JsonSerializer.Serialize(new{request=r.Id,decision=d.Id,enrollmentVersion=e.Version,source.Head,levelVersion=level.Version,preview.Fingerprint}));
        if(!save)return new{fingerprint,preview,level=level.ArabicName,enrollmentVersion=e.Version};
        if(b.Fingerprint!=fingerprint||preview.Conflicts.Length>0)throw new FacilityRejected(preview.Conflicts.Length>0?string.Join(" ",preview.Conflicts):"تغيرت بيانات المعاينة؛ راجع الترقية من جديد.",409);
        var tracked=await db.SportEnrollments.SingleAsync(x=>x.Id==e.Id);var now=time.GetUtcNow();var previous=tracked.LevelReviewedAtUtc;
        tracked.TrainingLevelId=level.Id;tracked.LevelReviewedAtUtc=now;tracked.LevelReviewedByUserId=db.RequestUserId;tracked.UpdatedAtUtc=now;
        var transfer=new EnrollmentGroupTransfer{Id=Guid.NewGuid(),AcademyId=t.AcademyId,SportEnrollmentId=e.Id,BranchId=e.BranchId,SportId=e.SportId,SourceGroupId=preview.SourceGroupId,TargetGroupId=b.GroupId,EffectiveDate=today,ActorUserId=db.RequestUserId,RequestKey="technical-"+b.RequestId,RequestHash=hash,Snapshot=JsonSerializer.Serialize(new{technicalRequestId=r.Id,decisionId=d.Id,preview,fingerprint}),CreatedAtUtc=now,UpdatedAtUtc=now};
        var history=new PlayerLevelReview{Id=Guid.NewGuid(),AcademyId=t.AcademyId,SportEnrollmentId=e.Id,SportId=e.SportId,TechnicalDecisionId=d.Id,GroupTransferId=transfer.Id,Revision=(await db.PlayerLevelReviews.Where(x=>x.SportEnrollmentId==e.Id).Select(x=>(int?)x.Revision).MaxAsync()??0)+1,FromLevelId=r.TrainingLevelId,FromLevelName=await db.TrainingLevels.Where(l=>l.Id==r.TrainingLevelId).Select(l=>l.ArabicName).SingleAsync(),ToLevelId=level.Id,ToLevelName=level.ArabicName,ActorUserId=db.RequestUserId,ActorName=await db.Users.Where(u=>u.Id==db.RequestUserId).Select(u=>u.DisplayName).SingleAsync(),Action="Changed",PreviousRecordedReviewAtUtc=previous,RequestId=b.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};
        db.EnrollmentGroupTransfers.Add(transfer);db.PlayerLevelReviews.Add(history);await db.SaveChangesAsync();await tx.CommitAsync();return new{history.Id,history.ToLevelName,history.GroupTransferId,replayed=false};
    }
}
