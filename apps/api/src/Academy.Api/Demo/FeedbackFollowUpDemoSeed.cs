using Academy.Api.Auth;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;
public static class FeedbackFollowUpDemoSeed
{
    public static async Task Seed(FoundationDbContext db, IHostEnvironment env, DemoOptions options)
    {
        var a=FootballDemoSeed.AcademyId;
        if(!env.IsEnvironment("Demo")||!options.SeedEnabled||options.SeedProfile!="Football"||!await db.Academies.AnyAsync(x=>x.Id==a&&x.Slug==FootballDemoSeed.Slug&&x.EnglishName==FootballDemoSeed.Marker))throw new InvalidOperationException("ملاحظات المتابعة الاصطناعية تتطلب Football Demo معلوم الهوية.");
        await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"football-feedback-followup-demo"},0))");
        var player=FootballDemoSeed.Id("player/10");
        var guardian=await db.GuardianPlayerLinks.Where(l=>l.AcademyId==a&&l.PlayerId==player&&l.IsActive).Select(l=>l.Guardian.UserId).SingleAsync();
        var at=DateTimeOffset.Parse("2026-10-08T06:00:00Z");
        for(var i=0;i<2;i++) {
            var id=FootballDemoSeed.Id($"feedback-followup-demo/{i}");if(await db.GuardianFeedbacks.AnyAsync(f=>f.Id==id))continue;
            db.GuardianFeedbacks.Add(new(){Id=id,AcademyId=a,PlayerId=player,GuardianUserId=guardian,TrainingGroupId=FootballDemoSeed.Id("group/2"),BranchId=FootballDemoSeed.Id("branch/1"),CoachMembershipId=FootballDemoSeed.Id("member/coach3"),Kind=i==0?"Complaint":"Suggestion",Message=i==0?"شكوى اصطناعية لمتابعة إشعار تغيير الحصة.":"اقتراح اصطناعي لمشاركة أهداف الأسبوع.",PeriodMonth=new(2026,10,1),Status="Open",DueOn=new(2026,10,10),DueSetByUserId=FootballDemoSeed.Id("user/owner"),DueSetAtUtc=at,IdempotencyKey=$"feedback-followup-demo/{i}",CreatedAtUtc=at,UpdatedAtUtc=at});
        }
        await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
