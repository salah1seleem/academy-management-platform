using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

// Explicit opt-in LegacyRegression fixture; no receipt, collection, reset,
// cash entitlement or real household is created by seeding.
public static class CrossBranchCashDemoSeed
{
    public static readonly Guid PlayerId=Guid.Parse("84000018-0000-0000-0000-000000000001");
    public static readonly Guid EnrollmentId=Guid.Parse("84000018-0000-0000-0000-000000000002");
    public static readonly Guid PlanId=Guid.Parse("84000018-0000-0000-0000-000000000004");
    public static async Task Seed(FoundationDbContext db)
    {
        await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"v2-demo-cross-branch-cash"},0))");
        if(await db.Players.AnyAsync(p=>p.Id==PlayerId))return;
        var academy=DemoSeed.NogoomAcademyId;
        var group=await db.TrainingGroups.SingleOrDefaultAsync(g=>g.AcademyId==academy&&g.Id==Slice2DemoSeed.FootballGroupId&&g.IsActive);
        var guardian=await db.GuardianProfiles.SingleOrDefaultAsync(g=>g.AcademyId==academy&&g.User.PhoneNumber==DemoSeed.GuardianPhone&&g.IsActive);
        var owner=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync();
        if(group is null||guardian is null)return;
        var at=new DateTimeOffset(2026,9,27,6,0,0,TimeSpan.Zero);
        db.Players.Add(new(){Id=PlayerId,AcademyId=academy,ArabicName="لاعب التحصيل بين الفروع — اصطناعي",PlayerCode="CB-DEMO-1",DateOfBirth=new(2017,1,1),CreatedAtUtc=at,UpdatedAtUtc=at});
        db.SportEnrollments.Add(new(){Id=EnrollmentId,AcademyId=academy,PlayerId=PlayerId,SportId=group.SportId,BranchId=group.BranchId,TrainingGroupId=group.Id,CreatedAtUtc=at,UpdatedAtUtc=at});
        db.GuardianPlayerLinks.Add(new(){Id=Guid.Parse("84000018-0000-0000-0000-000000000003"),AcademyId=academy,GuardianId=guardian.Id,PlayerId=PlayerId,CreatedByUserId=owner,CreatedAtUtc=at,UpdatedAtUtc=at});
        db.SubscriptionPlans.Add(new(){Id=PlanId,AcademyId=academy,SportId=group.SportId,ArabicName="شهري فرع التسجيل — تحصيل اصطناعي",PlanType=SubscriptionPlanType.Duration,DurationDays=30,Price=500,Currency="EGP",AllBranches=false,CreatedAtUtc=at,UpdatedAtUtc=at});
        db.PlanBranchPublications.Add(new(){Id=Guid.Parse("84000018-0000-0000-0000-000000000005"),AcademyId=academy,SubscriptionPlanId=PlanId,BranchId=group.BranchId,CreatedAtUtc=at,UpdatedAtUtc=at});
        await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
