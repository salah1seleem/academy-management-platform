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
public sealed class V3SkillSeedTests
{
    private sealed class Factory(bool enabled):WebApplicationFactory<Program>{protected override void ConfigureWebHost(IWebHostBuilder b){b.UseEnvironment("Demo");b.UseSetting("ConnectionStrings:Default",Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")??throw new InvalidOperationException("Explicit QA DB required"));b.UseSetting("Demo:SeedEnabled","true");b.UseSetting("Demo:SeedProfile","Football");b.UseSetting("Demo:SkillCurriculumSeedEnabled",enabled.ToString());b.UseSetting("Demo:StaffPassword","Demo-Only-123!");b.UseSetting("Demo:ReferenceDate","2026-09-28");}}
    [Fact] public async Task Skill_seed_is_opt_in_deterministic_does_not_assign_players_and_preserves_retirement()
    {
        var academy=FootballDemoSeed.AcademyId;string before;int collections,notices;
        await using(var off=new Factory(false)){using var client=off.CreateClient();await using var s=off.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.False(await db.SkillCurricula.AnyAsync(c=>c.AcademyId==academy));before=System.Text.Json.JsonSerializer.Serialize(await db.SportEnrollments.AsNoTracking().Where(e=>e.AcademyId==academy).OrderBy(e=>e.Id).Select(e=>new{e.Id,e.TrainingLevelId,e.TrainingGroupId,e.Status}).ToArrayAsync());collections=await db.Collections.CountAsync();notices=await db.AcademyNotices.CountAsync();}
        await using(var first=new Factory(true)){using var client=first.CreateClient();await using var s=first.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(2,await db.SkillRequirements.CountAsync(r=>r.AcademyId==academy));Assert.False(await db.PlayerSkillPlans.AnyAsync(p=>p.AcademyId==academy));var c=await db.SkillCurricula.SingleAsync(c=>c.AcademyId==academy);Assert.Equal("Published",c.State);c.State="Retired";c.RetiredAtUtc=DateTimeOffset.UtcNow;c.RetiredByUserId=c.CreatedByUserId;c.EditRevision++;await db.SaveChangesAsync();await Assert.ThrowsAsync<InvalidOperationException>(()=>SkillCurriculumDemoSeed.Seed(db,new HostingEnvironment{EnvironmentName="Production"},new DemoOptions{SeedEnabled=true,SeedProfile="Football"}));}
        await using(var second=new Factory(true)){using var client=second.CreateClient();await using var s=second.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal("Retired",(await db.SkillCurricula.SingleAsync(c=>c.AcademyId==academy)).State);Assert.Equal(2,await db.SkillRequirements.CountAsync(r=>r.AcademyId==academy));Assert.Equal(before,System.Text.Json.JsonSerializer.Serialize(await db.SportEnrollments.AsNoTracking().Where(e=>e.AcademyId==academy).OrderBy(e=>e.Id).Select(e=>new{e.Id,e.TrainingLevelId,e.TrainingGroupId,e.Status}).ToArrayAsync()));Assert.Equal(collections,await db.Collections.CountAsync());Assert.Equal(notices,await db.AcademyNotices.CountAsync());}
    }
}
