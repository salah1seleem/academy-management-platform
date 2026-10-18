using Academy.Api.Demo;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;

namespace Academy.IntegrationTests;
[Collection("Tenant authentication database")]
public sealed class V3OwnerFollowUpSeedTests
{
    private sealed class SeedFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment("Demo");b.UseSetting("ConnectionStrings:Default",Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")??throw new InvalidOperationException("Explicit isolated DB required"));
            b.UseSetting("Demo:SeedProfile","Football");b.UseSetting("Demo:SeedEnabled","true");b.UseSetting("Demo:OwnerFollowUpSeedEnabled","true");b.UseSetting("Demo:AssignedFollowUpSeedEnabled","true");b.UseSetting("Demo:StaffPassword","Demo-Only-123!");b.UseSetting("Demo:ReferenceDate","2026-09-28");
        }
    }
    [Fact] public async Task Owner_followup_synthetic_seed_is_marked_optional_idempotent_and_refuses_production()
    {
        int collections;Guid[] ids;
        await using(var first=new SeedFactory())
        {
            using var c=first.CreateClient();await using var scope=first.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            ids=await db.OwnerFollowUps.Where(f=>f.AcademyId==FootballDemoSeed.AcademyId).OrderBy(f=>f.Id).Select(f=>f.Id).ToArrayAsync();Assert.Equal(2,ids.Length);Assert.Equal(1,await db.OwnerFollowUps.CountAsync(f=>f.AcademyId==FootballDemoSeed.AcademyId&&f.State=="Closed"));Assert.Equal(2,await db.OwnerFollowUpChanges.CountAsync(f=>f.AcademyId==FootballDemoSeed.AcademyId));collections=await db.Collections.CountAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(()=>OwnerFollowUpDemoSeed.Seed(db,new HostingEnvironment{EnvironmentName="Production"},new DemoOptions{SeedEnabled=true,SeedProfile="Football"},TimeProvider.System));
            Assert.True(await db.TrainingSessions.AnyAsync(s=>s.Id==FootballDemoSeed.Id("owner-followup-demo/coverage-session")&&s.TrainingGroupId==FootballDemoSeed.Id("owner-followup-demo/coverage-group")));
            var draft=await db.PlayerEvaluations.Include(e=>e.Scores).SingleAsync(e=>e.Id==FootballDemoSeed.Id("owner-followup-demo/evaluation/0"));
            Assert.Equal(Academy.Infrastructure.Evaluations.EvaluationStatus.Draft,draft.Status);Assert.Single(draft.Scores);draft.IsActive=false;await db.SaveChangesAsync();
            var absent=await db.PlayerAttendances.SingleAsync(x=>x.Id==FootballDemoSeed.Id("owner-followup-demo/absence-record/0"));Assert.Equal(Academy.Infrastructure.Attendance.AttendanceStatus.Absent,absent.Status);
            Assert.Equal(4,await db.PlayerAttendances.CountAsync(x=>x.TrainingGroupId==FootballDemoSeed.Id("owner-followup-demo/absence-group")));absent.Status=Academy.Infrastructure.Attendance.AttendanceStatus.Excused;await db.SaveChangesAsync();
            var delegateMember=await db.AcademyMemberships.Include(m=>m.User).SingleAsync(m=>m.Id==AssignedFollowUpDemoSeed.MembershipId);
            Assert.Equal(EgyptPhoneNormalizer.Normalize(AssignedFollowUpDemoSeed.Phone),delegateMember.User.PhoneNumber);
            Assert.False(delegateMember.AllBranches);Assert.False(delegateMember.AllSports);Assert.True(delegateMember.UseActionGrants);
            var delegateGrant=await db.StaffActionGrants.SingleAsync(g=>g.MembershipId==delegateMember.Id);
            Assert.Equal("followups",delegateGrant.Module);Assert.True(delegateGrant.Read);Assert.True(delegateGrant.Edit);Assert.False(delegateGrant.Add);Assert.False(delegateGrant.Delete);
            Assert.Single(await db.MembershipBranchAccesses.Where(b=>b.MembershipId==delegateMember.Id).ToArrayAsync());Assert.Single(await db.MembershipSportAccesses.Where(b=>b.MembershipId==delegateMember.Id).ToArrayAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(()=>AssignedFollowUpDemoSeed.Seed(db,scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Academy.Infrastructure.Identity.ApplicationUser>>(),new HostingEnvironment{EnvironmentName="Production"},new DemoOptions{SeedEnabled=true,SeedProfile="Football"}));
            delegateGrant.Edit=false;delegateMember.IsActive=false;await db.SaveChangesAsync();
        }
        await using(var second=new SeedFactory())
        {
            using var c=second.CreateClient();await using var scope=second.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(ids,await db.OwnerFollowUps.Where(f=>f.AcademyId==FootballDemoSeed.AcademyId).OrderBy(f=>f.Id).Select(f=>f.Id).ToArrayAsync());Assert.Equal(2,await db.OwnerFollowUpChanges.CountAsync(f=>f.AcademyId==FootballDemoSeed.AcademyId));Assert.Equal(collections,await db.Collections.CountAsync());
            Assert.Equal(1,await db.TrainingSessions.CountAsync(s=>s.Id==FootballDemoSeed.Id("owner-followup-demo/coverage-session")));
            Assert.False(await db.PlayerEvaluations.Where(e=>e.Id==FootballDemoSeed.Id("owner-followup-demo/evaluation/0")).Select(e=>e.IsActive).SingleAsync());
            Assert.Equal(2,await db.PlayerEvaluations.CountAsync(e=>e.Id==FootballDemoSeed.Id("owner-followup-demo/evaluation/0")||e.Id==FootballDemoSeed.Id("owner-followup-demo/evaluation/1")));
            var restoredDraft=await db.PlayerEvaluations.SingleAsync(e=>e.Id==FootballDemoSeed.Id("owner-followup-demo/evaluation/0"));restoredDraft.IsActive=true;await db.SaveChangesAsync();
            var restoredAbsence=await db.PlayerAttendances.SingleAsync(x=>x.Id==FootballDemoSeed.Id("owner-followup-demo/absence-record/0"));Assert.Equal(Academy.Infrastructure.Attendance.AttendanceStatus.Excused,restoredAbsence.Status);
            Assert.Equal(4,await db.TrainingSessions.CountAsync(x=>x.TrainingGroupId==FootballDemoSeed.Id("owner-followup-demo/absence-group")));restoredAbsence.Status=Academy.Infrastructure.Attendance.AttendanceStatus.Absent;await db.SaveChangesAsync();
            Assert.False(await db.AcademyMemberships.Where(m=>m.Id==AssignedFollowUpDemoSeed.MembershipId).Select(m=>m.IsActive).SingleAsync());
            Assert.False(await db.StaffActionGrants.Where(g=>g.MembershipId==AssignedFollowUpDemoSeed.MembershipId).Select(g=>g.Edit).SingleAsync());
            // Restore only this synthetic test account so a repeated test run starts from its fixture state.
            var member=await db.AcademyMemberships.SingleAsync(m=>m.Id==AssignedFollowUpDemoSeed.MembershipId);member.IsActive=true;
            var grant=await db.StaffActionGrants.SingleAsync(g=>g.MembershipId==member.Id);grant.Edit=true;await db.SaveChangesAsync();
        }
    }
}
