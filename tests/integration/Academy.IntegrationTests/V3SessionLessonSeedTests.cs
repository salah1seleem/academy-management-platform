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
public sealed class V3SessionLessonSeedTests
{
    private sealed class Factory(bool enabled):WebApplicationFactory<Program>{protected override void ConfigureWebHost(IWebHostBuilder b){b.UseEnvironment("Demo");b.UseSetting("ConnectionStrings:Default",Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")??throw new InvalidOperationException("Explicit QA DB required"));b.UseSetting("Demo:SeedEnabled","true");b.UseSetting("Demo:SeedProfile","Football");b.UseSetting("Demo:SessionLessonSeedEnabled",enabled.ToString());b.UseSetting("Demo:StaffPassword","Demo-Only-123!");b.UseSetting("Demo:ReferenceDate","2026-09-28");}}
    [Fact] public async Task Session_lesson_seed_is_opt_in_deterministic_sixty_minutes_never_invents_technical_approval_or_reassigns_groups()
    {
        var academy=FootballDemoSeed.AcademyId;string before;
        await using(var off=new Factory(false)){using var c=off.CreateClient();await using var s=off.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.False(await db.SessionLessonTemplates.AnyAsync(p=>p.AcademyId==academy));before=System.Text.Json.JsonSerializer.Serialize(await db.TrainingGroups.Where(g=>g.AcademyId==academy).OrderBy(g=>g.Id).Select(g=>new{g.Id,g.TrainingLevelId}).ToArrayAsync());}
        for(var i=0;i<2;i++){await using var on=new Factory(true);using var c=on.CreateClient();await using var s=on.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();var p=await db.SessionLessonTemplates.SingleAsync(p=>p.AcademyId==academy);Assert.Equal("Draft",p.State);Assert.Null(p.ReviewedAtUtc);Assert.Equal(60,p.DeclaredMinutes);Assert.Equal(60,await db.SessionLessonSteps.Where(x=>x.TemplateId==p.Id).SumAsync(x=>x.Minutes));Assert.Equal(5,await db.TrainingExercises.CountAsync(e=>e.AcademyId==academy&&e.State=="Draft"));Assert.False(await db.SessionLessonAssignments.AnyAsync(a=>a.AcademyId==academy));Assert.Equal(before,System.Text.Json.JsonSerializer.Serialize(await db.TrainingGroups.Where(g=>g.AcademyId==academy).OrderBy(g=>g.Id).Select(g=>new{g.Id,g.TrainingLevelId}).ToArrayAsync()));await Assert.ThrowsAsync<InvalidOperationException>(()=>SessionLessonDemoSeed.Seed(db,new HostingEnvironment{EnvironmentName="Production"},new DemoOptions{SeedEnabled=true,SeedProfile="Football"}));}
    }
}
