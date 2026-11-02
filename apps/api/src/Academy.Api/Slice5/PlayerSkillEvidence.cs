using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Slice5;
internal sealed record SkillEvidence(PlayerSkillPlan Plan,SkillCurriculum Curriculum,SkillRequirement[] Requirements,PlayerSkillReview[] Latest,int Head,bool Current,bool Eligible);
internal static class PlayerSkillEvidence
{
    internal static async Task<SkillEvidence> Read(PlayerSkillPlan plan,Guid? currentLevel,FoundationDbContext db)
    {
        var c=await db.SkillCurricula.AsNoTracking().SingleAsync(c=>c.Id==plan.CurriculumId&&c.AcademyId==plan.AcademyId);
        var req=await db.SkillRequirements.AsNoTracking().Where(r=>r.CurriculumId==c.Id&&r.AcademyId==plan.AcademyId).OrderBy(r=>r.DisplayOrder).ToArrayAsync();
        var latest=await db.SkillRequirements.Where(r=>r.CurriculumId==c.Id&&r.AcademyId==plan.AcademyId).SelectMany(r=>db.PlayerSkillReviews.Where(v=>v.PlanId==plan.Id&&v.RequirementId==r.Id&&v.AcademyId==plan.AcademyId).OrderByDescending(v=>v.ObservedOn).ThenByDescending(v=>v.Sequence).Take(1)).AsNoTracking().ToArrayAsync();
        var head=await db.PlayerSkillReviews.Where(r=>r.PlanId==plan.Id&&r.AcademyId==plan.AcademyId).Select(r=>(int?)r.Sequence).MaxAsync()??0;
        var current=plan.IsActive&&currentLevel==c.TrainingLevelId&&c.State=="Published"
            &&await db.TrainingLevels.AnyAsync(l=>l.Id==currentLevel&&l.IsActive)&&await db.SportEnrollments.AnyAsync(e=>e.Id==plan.SportEnrollmentId&&e.LevelReviewedAtUtc!=null&&e.LevelReviewedByUserId!=null);
        return new(plan,c,req,latest,head,current,current&&req.Length>0&&latest.Length==req.Length&&latest.All(r=>r.State=="Mastered"));
    }
}
