using Academy.Api.Auth;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
// Synthetic substitute account only; no automatic reassignment, pay, attendance or notification.
public static class CoachOverrideDemoSeed
{
    public static Guid MembershipId=>GroupPlacementDemoSeed.Id(296);
    public static async Task Seed(FoundationDbContext db,UserManager<ApplicationUser> users,TimeProvider clock)
    {
        var academy=DemoSeed.NogoomAcademyId;await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:"+academy},0))");
        if(!await db.Academies.AnyAsync(a=>a.Id==academy&&a.Slug=="nogoom-demo"&&a.IsActive))return;
        var group=await db.TrainingGroups.SingleOrDefaultAsync(g=>g.Id==GroupPlacementDemoSeed.Id(260)&&g.AcademyId==academy&&g.IsActive);if(group is null)return;
        // Forward correction of the unchanged, known synthetic seed phone only.
        var original=await db.Users.SingleOrDefaultAsync(u=>u.Id==GroupPlacementDemoSeed.Id(261)&&u.Email=="coach.entry.demo@example.test"&&u.PhoneNumber=="01099900990");
        if(original is not null&&!await db.AcademyMemberships.AnyAsync(m=>m.UserId==original.Id&&m.AcademyId!=academy)){original.PhoneNumber=EgyptPhoneNormalizer.Normalize("01099900990");original.UpdatedAtUtc=clock.GetUtcNow();await db.SaveChangesAsync();}
        if(await db.AcademyMemberships.AnyAsync(m=>m.Id==MembershipId)) {await tx.CommitAsync();return;}
        var now=clock.GetUtcNow();var user=new ApplicationUser{Id=GroupPlacementDemoSeed.Id(295),UserName="coach.override.demo@example.test",Email="coach.override.demo@example.test",EmailConfirmed=true,DisplayName="مدرب بديل الحصة التجريبي",PhoneNumber=EgyptPhoneNormalizer.Normalize("01099900991"),PhoneNumberConfirmed=true,IsActive=true,CreatedAtUtc=now,UpdatedAtUtc=now};var result=await users.CreateAsync(user,"Demo-Only-123!");if(!result.Succeeded)throw new InvalidOperationException("تعذر إنشاء حساب البديل الاصطناعي.");
        db.AcademyMemberships.Add(new(){Id=MembershipId,AcademyId=academy,UserId=user.Id,Role=AcademyRole.Coach,AllBranches=false,CreatedAtUtc=now,UpdatedAtUtc=now});
        db.MembershipBranchAccesses.Add(new(){Id=GroupPlacementDemoSeed.Id(297),AcademyId=academy,MembershipId=MembershipId,BranchId=group.BranchId,CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
