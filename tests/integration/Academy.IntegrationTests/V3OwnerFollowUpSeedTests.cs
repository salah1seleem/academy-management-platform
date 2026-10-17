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
            b.UseSetting("Demo:SeedProfile","Football");b.UseSetting("Demo:SeedEnabled","true");b.UseSetting("Demo:OwnerFollowUpSeedEnabled","true");b.UseSetting("Demo:StaffPassword","Demo-Only-123!");b.UseSetting("Demo:ReferenceDate","2026-09-28");
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
        }
        await using(var second=new SeedFactory())
        {
            using var c=second.CreateClient();await using var scope=second.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(ids,await db.OwnerFollowUps.Where(f=>f.AcademyId==FootballDemoSeed.AcademyId).OrderBy(f=>f.Id).Select(f=>f.Id).ToArrayAsync());Assert.Equal(2,await db.OwnerFollowUpChanges.CountAsync(f=>f.AcademyId==FootballDemoSeed.AcademyId));Assert.Equal(collections,await db.Collections.CountAsync());
            Assert.Equal(1,await db.TrainingSessions.CountAsync(s=>s.Id==FootballDemoSeed.Id("owner-followup-demo/coverage-session")));
        }
    }
}
