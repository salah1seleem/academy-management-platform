using System.Security.Cryptography;
using System.Text;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Operations;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Facilities;
// Opt-in synthetic entrance rehearsal. Never invoked for customer/production profiles.
public static class VisitDemoSeed
{
    public static Guid SlotId(DateOnly day, int hour, string kind) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"academity-demo-entry:{day:yyyy-MM-dd}:{hour}:{kind}"))[..16]);
    public static async Task Seed(FoundationDbContext db, UserManager<ApplicationUser> users, CollectionService collections, TimeProvider clock)
    {
        var academy = DemoSeed.NogoomAcademyId; var now = clock.GetUtcNow(); var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")); var day = DateOnly.FromDateTime(local.DateTime); var hour = local.Hour / 2 * 2;
        var sessionId = SlotId(day,hour,"session"); var resourceId = GroupPlacementDemoSeed.Id(264); var groupId = GroupPlacementDemoSeed.Id(260);
        await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:"+academy},0))");
        var source = await db.TrainingGroups.SingleOrDefaultAsync(g=>g.Id==GroupPlacementDemoSeed.Id(1));
        if (source is null || !source.IsActive || !await db.Branches.AnyAsync(b=>b.Id==source.BranchId&&b.IsActive) || !await db.Sports.AnyAsync(s=>s.Id==source.SportId&&s.IsActive) || !await db.Academies.AnyAsync(a=>a.Id==academy && a.Slug=="nogoom-demo" && a.IsActive) || !await db.FacilitySpaces.AnyAsync(s=>s.Id==FacilityDemoSeed.SpaceId && s.IsActive && db.Facilities.Any(f=>f.Id==s.FacilityId&&f.IsActive)) || await db.TrainingSessions.AnyAsync(s=>s.Id==sessionId)) return;
        var payload=await db.AcademyConfigurationVersions.Where(c=>c.AcademyId==academy&&c.State==ConfigurationState.Active).Select(c=>c.Payload).SingleAsync(); var settings=AcademyRuntimeSettings.Read(payload);
        if(!settings.Features.GetValueOrDefault("training") || !settings.Features.GetValueOrDefault("facilities") || await db.AcademyFeatureEntitlements.CountAsync(e=>e.AcademyId==academy&&e.IsActive&&e.Enabled&&(e.Feature=="training"||e.Feature=="facilities"))!=2) return;
        var actor=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync(); var start=new TimeOnly(hour,0); var end=hour==22?new TimeOnly(23,59):new TimeOnly(hour+2,0);
        if (!await db.TrainingGroups.AnyAsync(g=>g.Id==groupId)) {
            var user=new ApplicationUser{Id=GroupPlacementDemoSeed.Id(261),UserName="coach.entry.demo@example.test",Email="coach.entry.demo@example.test",EmailConfirmed=true,DisplayName="مدرب محاكاة الدخول التجريبي",PhoneNumber="01099900990",PhoneNumberConfirmed=true,IsActive=true};
            var result=await users.CreateAsync(user,"Demo-Only-123!"); if(!result.Succeeded) throw new InvalidOperationException("تعذر إنشاء مدرب محاكاة الدخول التجريبي.");
            db.AcademyMemberships.Add(new(){Id=GroupPlacementDemoSeed.Id(262),AcademyId=academy,UserId=user.Id,Role=AcademyRole.Coach,CreatedAtUtc=now,UpdatedAtUtc=now});
            db.TrainingGroups.Add(new(){Id=groupId,AcademyId=academy,BranchId=source.BranchId,SportId=source.SportId,AgeCategoryId=source.AgeCategoryId,TrainingLevelId=source.TrainingLevelId,Capacity=2,ArabicName="بوابة الدخول — محاكاة تجريبية",CreatedAtUtc=now,UpdatedAtUtc=now});
            db.StaffGroupAssignments.Add(new(){Id=GroupPlacementDemoSeed.Id(263),AcademyId=academy,TrainingGroupId=groupId,AcademyMembershipId=GroupPlacementDemoSeed.Id(262),CreatedAtUtc=now,UpdatedAtUtc=now});
            db.FacilityResources.Add(new(){Id=resourceId,AcademyId=academy,BranchId=source.BranchId,FacilitySpaceId=FacilityDemoSeed.SpaceId,Name="E · محاكاة الدخول",Capacity=2,SharedBookingsAllowed=false,OperatingDays=[0,1,2,3,4,5,6],OpensAt=new(0,0),ClosesAt=new(23,59),ServiceKinds=["Training"],ApprovedAtUtc=now,ApprovedByUserId=actor,ApprovalEvidence="اعتماد اصطناعي لمحاكاة بوابة الدخول؛ ليس اعتماد سلامة لمسبح حقيقي.",CreatedAtUtc=now,UpdatedAtUtc=now});
            var guardian=await db.GuardianProfiles.Where(g=>g.AcademyId==academy&&g.User.PhoneNumber==DemoSeed.GuardianPhone&&g.IsActive).Select(g=>g.Id).SingleAsync();
            foreach(var n in new[]{270,280}) {
                db.Players.Add(new(){Id=GroupPlacementDemoSeed.Id(n),AcademyId=academy,ArabicName="لاعب محاكاة الدخول الاصطناعي "+n,PlayerCode="DEMO-ENTRY-"+n,DateOfBirth=new(2017,1,1),CreatedAtUtc=now,UpdatedAtUtc=now});
                db.SportEnrollments.Add(new(){Id=GroupPlacementDemoSeed.Id(n+1),AcademyId=academy,PlayerId=GroupPlacementDemoSeed.Id(n),BranchId=source.BranchId,SportId=source.SportId,TrainingGroupId=groupId,TrainingLevelId=source.TrainingLevelId,GroupAssignedFrom=day,LevelReviewedByUserId=actor,LevelReviewedAtUtc=now,CreatedAtUtc=now,UpdatedAtUtc=now});
                db.GuardianPlayerLinks.Add(new(){Id=GroupPlacementDemoSeed.Id(n+2),AcademyId=academy,PlayerId=GroupPlacementDemoSeed.Id(n),GuardianId=guardian,CreatedByUserId=actor,CreatedAtUtc=now,UpdatedAtUtc=now});
            }
            await db.SaveChangesAsync(); foreach(var n in new[]{270,280}) await collections.CreateCashAsync(academy,actor,GroupPlacementDemoSeed.Id(n+1),GroupPlacementDemoSeed.Id(5),"demo-entry-player-"+n,"Cash");
        }
        var r=await db.FacilityResources.SingleAsync(r=>r.Id==resourceId); var group=await db.TrainingGroups.SingleAsync(g=>g.Id==groupId);
        if(!r.IsActive || r.ApprovedAtUtc is null || r.Capacity<2 || r.BranchId!=group.BranchId || group.BranchId!=source.BranchId || group.SportId!=source.SportId || !r.ServiceKinds.Contains("Training") || !r.OperatingDays.Contains((int)day.DayOfWeek) || start<r.OpensAt || end>r.ClosesAt || !group.IsActive || !await db.StaffGroupAssignments.AnyAsync(a=>a.TrainingGroupId==groupId&&a.IsActive&&a.AcademyMembershipId==GroupPlacementDemoSeed.Id(262)&&a.AcademyMembership.IsActive&&a.AcademyMembership.User.IsActive)
            || await db.TrainingSessions.AnyAsync(s=>s.AcademyId==academy&&s.SessionDate==day&&s.Status!=TrainingSessionStatus.Cancelled&&s.StartTime<end&&start<s.EndTime&&db.StaffGroupAssignments.Any(a=>a.TrainingGroupId==s.TrainingGroupId&&a.AcademyMembershipId==GroupPlacementDemoSeed.Id(262)&&a.IsActive))
            || await db.ResourceBookings.AnyAsync(b=>b.ResourceId==resourceId&&b.Date==day&&b.State==ResourceBookingState.Reserved&&b.StartTime<end&&start<b.EndTime)
            || await db.ResourceClosures.AnyAsync(c=>c.ResourceId==resourceId&&c.IsActive&&c.FromDate<=day&&c.ToDate>=day&&c.StartTime<end&&start<c.EndTime)) return;
        db.TrainingSessions.Add(new(){Id=sessionId,AcademyId=academy,TrainingGroupId=groupId,BranchId=source.BranchId,SportId=source.SportId,AgeCategoryId=source.AgeCategoryId,SessionDate=day,StartTime=start,EndTime=end,CreatedByUserId=actor,CreatedAtUtc=now,UpdatedAtUtc=now});
        db.ResourceBookings.Add(new(){Id=SlotId(day,hour,"booking"),AcademyId=academy,ResourceId=resourceId,BranchId=source.BranchId,TrainingGroupId=groupId,TrainingSessionId=sessionId,CoachMembershipId=GroupPlacementDemoSeed.Id(262),Date=day,StartTime=start,EndTime=end,ReservedSeats=2,ApprovedCapacitySnapshot=r.Capacity,ServiceKind="Training",Exclusive=true,ActorUserId=actor,RequestKey=$"demo-entry-{day}-{hour}",RequestHash="demo-entry-slot",CreatedAtUtc=now,UpdatedAtUtc=now});
        await db.SaveChangesAsync(); await tx.CommitAsync();
        // No entry/exit/attendance is assumed. The reviewer explicitly records each action.
    }
}
