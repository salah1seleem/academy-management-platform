using Academy.Api.Auth;
using Academy.Api.Demo;
using Academy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;

namespace Academy.IntegrationTests;
[Collection("Tenant authentication database")]
public sealed class V3FeedbackFollowUpSeedTests
{
    private sealed class Factory:WebApplicationFactory<Program>{protected override void ConfigureWebHost(IWebHostBuilder b){b.UseEnvironment("Demo");b.UseSetting("ConnectionStrings:Default",Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")??throw new InvalidOperationException("Explicit QA DB required"));b.UseSetting("Demo:SeedEnabled","true");b.UseSetting("Demo:SeedProfile","Football");b.UseSetting("Demo:FeedbackFollowUpSeedEnabled","true");b.UseSetting("Demo:OwnerFollowUpSeedEnabled","true");b.UseSetting("Demo:StaffPassword","Demo-Only-123!");b.UseSetting("Demo:ReferenceDate","2026-09-28");}}
    [Fact]public async Task Feedback_followup_seed_is_optional_idempotent_keeps_review_and_refuses_production(){
        int collections,notices;var id=FootballDemoSeed.Id("feedback-followup-demo/0");
        await using(var first=new Factory()){
            using var c=first.CreateClient();await using var scope=first.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            collections=await db.Collections.CountAsync();notices=await db.AcademyNotices.CountAsync();
            var item=await db.GuardianFeedbacks.SingleAsync(f=>f.Id==id);Assert.Equal("Open",item.Status);Assert.Equal(new DateOnly(2026,10,10),item.DueOn);
            Assert.Equal(2,await db.GuardianFeedbacks.CountAsync(f=>f.IdempotencyKey.StartsWith("feedback-followup-demo/")));
            Assert.Single(await db.GuardianFeedbacks.Where(f=>f.IdempotencyKey.StartsWith("feedback-followup-demo/")).Select(f=>f.GuardianUserId).Distinct().ToArrayAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(()=>FeedbackFollowUpDemoSeed.Seed(db,new HostingEnvironment{EnvironmentName="Production"},new DemoOptions{SeedEnabled=true,SeedProfile="Football"}));
            item.Status="Reviewed";await db.SaveChangesAsync();
        }
        await using(var second=new Factory()){
            using var c=second.CreateClient();await using var scope=second.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var item=await db.GuardianFeedbacks.SingleAsync(f=>f.Id==id);Assert.Equal("Reviewed",item.Status);Assert.Equal(2,await db.GuardianFeedbacks.CountAsync(f=>f.IdempotencyKey.StartsWith("feedback-followup-demo/")));Assert.Equal(collections,await db.Collections.CountAsync());Assert.Equal(notices,await db.AcademyNotices.CountAsync());
            item.Status="Open";await db.SaveChangesAsync(); // Restore only this isolated synthetic fixture.
        }
    }
}
