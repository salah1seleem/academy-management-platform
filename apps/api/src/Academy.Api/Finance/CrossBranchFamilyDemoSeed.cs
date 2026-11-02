using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

// Opt-in synthetic family for payment-only branch rehearsals. No collection,
// receipt, cash entitlement, reset, or production/customer data is created.
public static class CrossBranchFamilyDemoSeed
{
    public const string Phone="+201000000019";
    public static readonly Guid UserId=Guid.Parse("84000019-0000-0000-0000-000000000001");
    public static readonly Guid GuardianId=Guid.Parse("84000019-0000-0000-0000-000000000002");
    private static readonly Guid GroupId=Guid.Parse("84000019-0000-0000-0000-000000000003");
    public static async Task Seed(FoundationDbContext db,UserManager<ApplicationUser> users)
    {
        var academy=DemoSeed.NogoomAcademyId;var at=new DateTimeOffset(2026,9,28,7,0,0,TimeSpan.Zero);
        await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"v2-demo-cross-branch-family"},0))");
        var user=await users.FindByIdAsync(UserId.ToString());
        if(user is null)
        {
            user=new(){Id=UserId,UserName=Phone,PhoneNumber=Phone,PhoneNumberConfirmed=true,DisplayName="أسرة قبض فروع اصطناعية",CreatedAtUtc=at,UpdatedAtUtc=at};
            if(!(await users.CreateAsync(user)).Succeeded)throw new InvalidOperationException("Could not create synthetic cross-branch family");
        }
        if(!await db.AcademyMemberships.AnyAsync(m=>m.AcademyId==academy&&m.UserId==UserId))db.AcademyMemberships.Add(new(){Id=Guid.Parse("84000019-0000-0000-0000-000000000004"),AcademyId=academy,UserId=UserId,Role=AcademyRole.Guardian,CreatedAtUtc=at,UpdatedAtUtc=at});
        if(!await db.GuardianProfiles.AnyAsync(g=>g.Id==GuardianId))db.GuardianProfiles.Add(new(){Id=GuardianId,AcademyId=academy,UserId=UserId,DisplayName=user.DisplayName,ContactPhone=Phone,CreatedAtUtc=at,UpdatedAtUtc=at});
        if(!await db.TrainingGroups.AnyAsync(g=>g.Id==GroupId))db.TrainingGroups.Add(new(){Id=GroupId,AcademyId=academy,ArabicName="مجموعة أسرة القبض الاصطناعية",BranchId=Slice2DemoSeed.FifthBranchId,SportId=Slice2DemoSeed.FootballId,AgeCategoryId=Slice2DemoSeed.U10Id,Capacity=12,CreatedAtUtc=at,UpdatedAtUtc=at});
        var owner=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync();
        for(var i=1;i<=2;i++)
        {
            var player=Guid.Parse($"84000019-0000-0000-0001-{i:D12}");var link=Guid.Parse($"84000019-0000-0000-0002-{i:D12}");var enrollment=Guid.Parse($"84000019-0000-0000-0003-{i:D12}");
            if(!await db.Players.AnyAsync(p=>p.Id==player))db.Players.Add(new(){Id=player,AcademyId=academy,PlayerCode=$"CROSS-FAMILY-{i}",ArabicName=i==1?"آدم قبض الفروع الاصطناعي":"لينا قبض الفروع الاصطناعية",DateOfBirth=new(2016,i,1),CreatedAtUtc=at,UpdatedAtUtc=at});
            if(!await db.GuardianPlayerLinks.AnyAsync(l=>l.Id==link))db.GuardianPlayerLinks.Add(new(){Id=link,AcademyId=academy,GuardianId=GuardianId,PlayerId=player,CreatedByUserId=owner,CreatedAtUtc=at,UpdatedAtUtc=at});
            if(!await db.SportEnrollments.AnyAsync(e=>e.Id==enrollment))db.SportEnrollments.Add(new(){Id=enrollment,AcademyId=academy,PlayerId=player,SportId=Slice2DemoSeed.FootballId,BranchId=i==1?Slice2DemoSeed.CityBranchId:Slice2DemoSeed.FifthBranchId,TrainingGroupId=i==1?Slice2DemoSeed.FootballGroupId:GroupId,CreatedAtUtc=at,UpdatedAtUtc=at});
        }
        await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
