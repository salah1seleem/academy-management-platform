using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;
public static class OwnerFollowUpDemoSeed
{
    public static async Task Seed(FoundationDbContext db, IHostEnvironment env, DemoOptions options, TimeProvider clock)
    {
        var a=FootballDemoSeed.AcademyId;
        if(!env.IsEnvironment("Demo")||!options.SeedEnabled||options.SeedProfile!="Football"||!await db.Academies.AnyAsync(x=>x.Id==a&&x.Slug==FootballDemoSeed.Slug&&x.EnglishName==FootballDemoSeed.Marker))throw new InvalidOperationException("المتابعات الاصطناعية تتطلب Football Demo معلوم الهوية.");
        await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"football-owner-followup-demo"},0))");
        var owner=FootballDemoSeed.Id("member/owner");if(!await db.AcademyMemberships.AnyAsync(m=>m.Id==owner&&m.AcademyId==a&&m.Role==AcademyRole.AcademyOwner&&m.IsActive&&m.User.IsActive))return;
        var today=await AttendanceCalendar.TodayAsync(a,db,clock);var now=clock.GetUtcNow();
        var sources=(await OwnerFollowUpEndpoints.Signals(db,a,today)).Where(s=>s.Applies("GroupLowOccupancy")).OrderBy(s=>s.GroupId).Take(2).ToArray();
        for(var i=0;i<sources.Length;i++)
        {
            var id=FootballDemoSeed.Id($"owner-followup-demo/{i}");var source=sources[i];
            if(await db.OwnerFollowUps.AnyAsync(f=>f.Id==id||f.AcademyId==a&&f.TrainingGroupId==source.GroupId&&f.Condition=="GroupLowOccupancy"&&f.State!="Closed"&&f.State!="Rejected"))continue;
            var closed=i==1;var f=new OwnerFollowUp{Id=id,AcademyId=a,TrainingGroupId=source.GroupId,CreatedByMembershipId=owner,AssignedToMembershipId=owner,Condition="GroupLowOccupancy",State=closed?"Closed":"Assigned",CreatedOn=today,DueOn=today.AddDays(-1),Note="متابعة اصطناعية: مراجعة المقاعد المسندة، وليست حساب ربح أو سداد.",ResolutionReason=closed?"مثال إغلاق متابعة بشرية مع بقاء مصدر الحالة الحقيقي":null,RequestId=id,RequestHash=new string('0',64),EvidenceJson=JsonSerializer.Serialize(new{source,observedAtUtc=now,asOfDate=today}),CreatedAtUtc=now,UpdatedAtUtc=now};
            db.OwnerFollowUps.Add(f);db.OwnerFollowUpChanges.Add(new(){Id=FootballDemoSeed.Id($"owner-followup-demo/change/{i}"),AcademyId=a,FollowUpId=id,ActorMembershipId=owner,RequestId=id,RequestHash=f.RequestHash,BeforeJson="{}",AfterJson=JsonSerializer.Serialize(new{f.State,f.AssignedToMembershipId,f.DueOn,f.Note,f.ResolutionReason}),CreatedAtUtc=now,UpdatedAtUtc=now});
        }
        var coverageGroup=FootballDemoSeed.Id("owner-followup-demo/coverage-group");
        if(!await db.TrainingGroups.AnyAsync(g=>g.Id==coverageGroup)){
            db.TrainingGroups.Add(new(){Id=coverageGroup,AcademyId=a,ArabicName="مجموعة تحتاج مراجعة تغطية — عرض اصطناعي",BranchId=FootballDemoSeed.Id("branch/1"),SportId=FootballDemoSeed.Id("sport"),AgeCategoryId=FootballDemoSeed.Id("age/2"),Capacity=16,CreatedAtUtc=now,UpdatedAtUtc=now});
            db.TrainingSessions.Add(new(){Id=FootballDemoSeed.Id("owner-followup-demo/coverage-session"),AcademyId=a,TrainingGroupId=coverageGroup,BranchId=FootballDemoSeed.Id("branch/1"),SportId=FootballDemoSeed.Id("sport"),AgeCategoryId=FootballDemoSeed.Id("age/2"),SessionDate=today.AddDays(1),StartTime=new(17,0),EndTime=new(18,0),Status=TrainingSessionStatus.Scheduled,Source=TrainingSessionSource.Manual,CreatedByUserId=await db.AcademyMemberships.Where(m=>m.Id==owner).Select(m=>m.UserId).SingleAsync(),Notes="حصة اصطناعية لمراجعة إسناد المدرب، لا حضور أو خصم أو إرسال حقيقي.",CreatedAtUtc=now,UpdatedAtUtc=now});
        }
        // Stable, deliberately old drafts with synthetic scores; never auto-publish them.
        var createdAt = DateTimeOffset.Parse("2026-09-20T09:00:00Z");
        for(var i=0;i<2;i++) {
            var draftId=FootballDemoSeed.Id($"owner-followup-demo/evaluation/{i}");
            if(await db.PlayerEvaluations.AnyAsync(e=>e.Id==draftId))continue;
            var criterion=await db.EvaluationCriteria.Where(c=>c.AcademyId==a&&c.SportId==FootballDemoSeed.Id("sport")).OrderBy(c=>c.DisplayOrder).ThenBy(c=>c.Id).FirstAsync();
            var draft=new PlayerEvaluation{Id=draftId,AcademyId=a,TrainingGroupId=FootballDemoSeed.Id("group/2"),SportId=FootballDemoSeed.Id("sport"),SportEnrollmentId=FootballDemoSeed.Id("enrollment/10"),EvaluatedByUserId=FootballDemoSeed.Id("user/coach3"),EvaluationDate=new(2026,9,20+i),ReportingPeriod=$"مراجعة تشغيل اصطناعية {i+1}",CreatedAtUtc=createdAt.AddHours(i),UpdatedAtUtc=createdAt.AddHours(i)};
            draft.Scores.Add(new(){Id=FootballDemoSeed.Id($"owner-followup-demo/evaluation/score/{i}"),AcademyId=a,PlayerEvaluationId=draftId,EvaluationCriterionId=criterion.Id,SportId=draft.SportId,CriterionNameSnapshot=criterion.ArabicName,WeightSnapshot=criterion.Weight,FootballAxisSnapshot=criterion.FootballAxis,Score=70+i,CreatedAtUtc=createdAt,UpdatedAtUtc=createdAt});db.PlayerEvaluations.Add(draft);
        }
        await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
