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
    [Fact] public async Task Guardian_request_seed_is_guarded_idempotent_and_preserves_review_without_money()
    {
        await using var factory=new Factory();using var client=factory.CreateClient();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var options=new DemoOptions{SeedEnabled=true,SeedProfile="Football"};var env=new HostingEnvironment{EnvironmentName="Demo"};var id=FootballDemoSeed.Id("feedback-request-demo/0");var money=await db.Collections.CountAsync();var notices=await db.AcademyNotices.CountAsync();
        await FeedbackFollowUpDemoSeed.SeedRequests(db,env,options);var row=await db.GuardianFeedbacks.SingleAsync(x=>x.Id==id);Assert.Equal("Request",row.Kind);var before=row.Status;row.Status="Reviewed";await db.SaveChangesAsync();
        try{await FeedbackFollowUpDemoSeed.SeedRequests(db,env,options);Assert.Equal("Reviewed",(await db.GuardianFeedbacks.AsNoTracking().SingleAsync(x=>x.Id==id)).Status);Assert.Equal(1,await db.GuardianFeedbacks.CountAsync(x=>x.IdempotencyKey=="feedback-request-demo/0"));Assert.Equal(money,await db.Collections.CountAsync());Assert.Equal(notices,await db.AcademyNotices.CountAsync());await Assert.ThrowsAsync<InvalidOperationException>(()=>FeedbackFollowUpDemoSeed.SeedRequests(db,new HostingEnvironment{EnvironmentName="Production"},options));}
        finally{row.Status=before;await db.SaveChangesAsync();}
    }
    [Fact] public async Task Feedback_incident_seed_is_optional_idempotent_preserves_review_and_refuses_production()
    {
        await using var factory=new Factory(); using var client=factory.CreateClient(); await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var env=new HostingEnvironment{EnvironmentName="Demo"}; var options=new DemoOptions{SeedEnabled=true,SeedProfile="Football"}; var notices=await db.AcademyNotices.CountAsync(); var money=await db.Collections.CountAsync();
        await FeedbackFollowUpDemoSeed.SeedIncident(db,env,options); var id=FootballDemoSeed.Id("feedback-incident-demo/0"); var f=await db.GuardianFeedbacks.SingleAsync(f=>f.Id==id); Assert.NotNull(f.IncidentTrainingSessionId); Assert.NotNull(f.IncidentSessionEvidence); var old=f.Status; f.Status="Reviewed"; await db.SaveChangesAsync();
        try { await FeedbackFollowUpDemoSeed.SeedIncident(db,env,options); Assert.Equal("Reviewed",(await db.GuardianFeedbacks.AsNoTracking().SingleAsync(f=>f.Id==id)).Status); Assert.Equal(notices,await db.AcademyNotices.CountAsync()); Assert.Equal(money,await db.Collections.CountAsync()); await Assert.ThrowsAsync<InvalidOperationException>(()=>FeedbackFollowUpDemoSeed.SeedIncident(db,new HostingEnvironment{EnvironmentName="Production"},options)); }
        finally {f.Status=old; await db.SaveChangesAsync();}
    }
    [Fact] public async Task Feedback_triage_seed_is_optional_preserves_manual_review_and_refuses_production()
    {
        await using var factory = new Factory(); using var client = factory.CreateClient(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var env = new HostingEnvironment { EnvironmentName = "Demo" }; var options = new DemoOptions { SeedEnabled = true, SeedProfile = "Football" };
        var notices = await db.AcademyNotices.CountAsync(); var money = await db.Collections.CountAsync(); await FeedbackFollowUpDemoSeed.SeedTriage(db, env, options);
        var f = await db.GuardianFeedbacks.SingleAsync(x => x.Id == FootballDemoSeed.Id("feedback-triage-demo/0")); var before = f.Status; f.Status = "Reviewed"; await db.SaveChangesAsync();
        try { await FeedbackFollowUpDemoSeed.SeedTriage(db, env, options); Assert.Equal("Reviewed", (await db.GuardianFeedbacks.AsNoTracking().SingleAsync(x => x.Id == f.Id)).Status); Assert.Equal("Safety", f.ReportedUrgency); Assert.Equal(2, await db.GuardianFeedbacks.CountAsync(x => x.IdempotencyKey.StartsWith("feedback-triage-demo/"))); Assert.Equal(notices, await db.AcademyNotices.CountAsync()); Assert.Equal(money, await db.Collections.CountAsync()); await Assert.ThrowsAsync<InvalidOperationException>(() => FeedbackFollowUpDemoSeed.SeedTriage(db, new HostingEnvironment { EnvironmentName = "Production" }, options)); }
        finally { f.Status = before; await db.SaveChangesAsync(); }
    }
    [Fact] public async Task Feedback_investigation_seed_keeps_manual_brief_and_never_regrants_or_notifies()
    {
        await using var factory = new Factory(); using var client = factory.CreateClient(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var env = new HostingEnvironment { EnvironmentName = "Demo" }; var options = new DemoOptions { SeedEnabled = true, SeedProfile = "Football", StaffPassword = "Demo-Only-123!" };
        await AssignedFollowUpDemoSeed.Seed(db, scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Academy.Infrastructure.Identity.ApplicationUser>>(), env, options);
        var money = await db.Collections.CountAsync(); var notices = await db.AcademyNotices.CountAsync();
        await FeedbackFollowUpDemoSeed.SeedInvestigation(db, env, options); var f = await db.GuardianFeedbacks.SingleAsync(f => f.Id == FootballDemoSeed.Id("feedback-investigation-demo/0"));
        var history = await db.FeedbackChanges.CountAsync(c => c.FeedbackId == f.Id); Assert.True(history > 0);
        var before = f.InvestigationBrief; f.InvestigationBrief = "موجز عدله المالك يدويًا في بيانات العرض المعزولة"; await db.SaveChangesAsync();
        try {
            await FeedbackFollowUpDemoSeed.SeedInvestigation(db, env, options); Assert.Equal(f.InvestigationBrief, (await db.GuardianFeedbacks.AsNoTracking().SingleAsync(x => x.Id == f.Id)).InvestigationBrief);
            Assert.Equal(history, await db.FeedbackChanges.CountAsync(c => c.FeedbackId == f.Id)); Assert.Equal(money, await db.Collections.CountAsync()); Assert.Equal(notices, await db.AcademyNotices.CountAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => FeedbackFollowUpDemoSeed.SeedInvestigation(db, new HostingEnvironment { EnvironmentName = "Production" }, options));
        } finally { f.InvestigationBrief = before; await db.SaveChangesAsync(); }
    }
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
