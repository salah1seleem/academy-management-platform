using Academy.Api.Slice3;
using Academy.Api.Slice5;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;
// Invoked only from V4AcceptanceSeed, after its explicit environment/database/
// synthetic-tenant guard and inside its transaction. Additive deterministic data.
internal static class V4RefinementSeed
{
    internal static async Task Add(FoundationDbContext db,DateOnly today,DateTimeOffset now)
    {
        var academy=FootballDemoSeed.AcademyId;
        Guid Id(string key)=>FootballDemoSeed.Id("v41/"+key);
        // Real confirmed financial graphs in the previous comparison period.
        // No fake percentage, dashboard fixture total, or payment-provider call.
        for(var i=0;i<6;i++){
            var enrollment=await db.SportEnrollments.SingleAsync(e=>e.Id==FootballDemoSeed.Id($"enrollment/{i}"));
            var start=today.AddMonths(-2).AddDays(3+i);
            var paid=new DateTimeOffset(today.AddMonths(-1).AddDays(-10+i).ToDateTime(new(9,0)),TimeSpan.Zero);
            await Slice3DemoSeed.Confirmed(db,Id($"previous-collection/{i}"),enrollment,FootballDemoSeed.Id("plan/month"),FootballDemoSeed.Id("user/owner"),start,start.AddDays(29),paid,now,default);
        }
        // Two genuinely unspent sessions, not a fabricated remaining-session counter.
        var shortPlan=Id("two-session-plan");
        await Slice3DemoSeed.Plan(db,shortPlan,academy,FootballDemoSeed.Id("sport"),"باقة لقاءين تدريبيين",SubscriptionPlanType.Sessions,300,null,2,6,now,default);
        var child=await db.SportEnrollments.SingleAsync(e=>e.Id==FootballDemoSeed.Id("enrollment/0"));
        await Slice3DemoSeed.Confirmed(db,Id("two-session-collection"),child,shortPlan,FootballDemoSeed.Id("user/owner"),today,today.AddDays(29),now,now,default);

        // New comparable evaluations; no published snapshot is overwritten.
        var criteria=await db.EvaluationCriteria.Where(c=>c.AcademyId==academy&&c.IsActive&&c.SportId==FootballDemoSeed.Id("sport")).OrderBy(c=>c.DisplayOrder).ToListAsync();
        for(var i=0;i<2;i++){
            var enrollment=await db.SportEnrollments.SingleAsync(e=>e.Id==FootballDemoSeed.Id($"enrollment/{i}"));
            await Slice5DemoSeed.Evaluation(db,Id($"evaluation/{i}"),enrollment,FootballDemoSeed.Id("user/coach1"),today,"متابعة التطور الشهري",EvaluationStatus.Published,"تقييم اصطناعي كامل لقبول الواجهة.",criteria,criteria.Select(_=>(int?)(i==0?85:87)).ToArray(),now,default);
        }
        // Existing tournament engine, real completed results and upcoming fixtures
        // across all existing branches; no complex generator or new module.
        for(var b=0;b<3;b++){
            var team=Id($"team/{b}");
            if(await db.MatchTeams.AnyAsync(t=>t.Id==team))continue;
            var coach=FootballDemoSeed.Id($"member/coach{b%3+1}");
            db.MatchTeams.Add(new(){Id=team,AcademyId=academy,Name=$"منتخب {2018-b*2}",SportId=FootballDemoSeed.Id("sport"),BranchId=FootballDemoSeed.Id($"branch/{b}"),AgeCategoryId=FootballDemoSeed.Id($"age/{b*2}"),CoachMembershipId=coach,CreatedAtUtc=now,UpdatedAtUtc=now});
            for(var p=0;p<4;p++)db.MatchTeamPlayers.Add(new(){Id=Id($"roster/{b}/{p}"),AcademyId=academy,TeamId=team,EnrollmentId=FootballDemoSeed.Id($"enrollment/{b*10+p}"),CreatedAtUtc=now,UpdatedAtUtc=now});
            for(var m=0;m<4;m++){
                var match=Id($"match/{b}/{m}");
                db.AcademyMatches.Add(new(){Id=match,AcademyId=academy,TeamId=team,TournamentId=FootballDemoSeed.Id("matches/tournament"),Opponent=new[]{"نجوم الشرق","أبطال المدينة","شباب المستقبل","براعم النخيل"}[m],MatchDate=today.AddDays(m==3?4+b:-12+m*4),StartTime=new(17+b,0),Venue=$"ملعب الفرع {b+1}",OurScore=m==3?null:m==0?3:m==1?1:0,OpponentScore=m==3?null:m==0?1:m==1?1:2,PublishedAtUtc=now,PublishedFingerprint="v41-isolated-synthetic-fixture",CreatedAtUtc=now,UpdatedAtUtc=now});
                db.MatchCoachAssignments.Add(new(){Id=Id($"match-coach/{b}/{m}"),AcademyId=academy,MatchId=match,CoachMembershipId=coach,AssignedByMembershipId=FootballDemoSeed.Id("member/owner"),Sequence=1,Source="DemoFixture",CreatedAtUtc=now,UpdatedAtUtc=now});
                for(var p=0;p<4;p++)db.MatchParticipants.Add(new(){Id=Id($"match-participant/{b}/{m}/{p}"),AcademyId=academy,MatchId=match,PlayerId=FootballDemoSeed.Id($"player/{b*10+p}"),IsBench=p==3,CreatedAtUtc=now,UpdatedAtUtc=now});
            }
        }
        await db.SaveChangesAsync();
    }
}
