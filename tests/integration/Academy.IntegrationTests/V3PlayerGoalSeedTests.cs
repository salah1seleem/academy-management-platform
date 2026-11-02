using Academy.Api.Auth;
using Academy.Api.Demo;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Evaluations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;

namespace Academy.IntegrationTests;
[Collection("Tenant authentication database")]
public sealed class V3PlayerGoalSeedTests
{
    private sealed class Factory(bool enabled = true) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b) { b.UseEnvironment("Demo"); b.UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Explicit QA DB required")); b.UseSetting("Demo:SeedEnabled", "true"); b.UseSetting("Demo:SeedProfile", "Football"); b.UseSetting("Demo:PlayerGoalSeedEnabled", enabled.ToString()); b.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); b.UseSetting("Demo:ReferenceDate", "2026-09-28"); }
    }
    [Fact] public async Task Player_goal_seed_is_opt_in_idempotent_preserves_sources_and_refuses_production()
    {
        int collections, notices; string source;
        await using (var off = new Factory(false)) { using var c = off.CreateClient(); await using var scope = off.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Equal(0, await db.PlayerDevelopmentGoals.CountAsync(g => g.AcademyId == FootballDemoSeed.AcademyId)); }
        await using (var first = new Factory()) {
            using var c = first.CreateClient(); await using var scope = first.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            Assert.Equal(2, await db.PlayerDevelopmentGoals.CountAsync(g => g.AcademyId == FootballDemoSeed.AcademyId)); Assert.Equal(2, await db.PlayerGoalChanges.CountAsync(c => c.AcademyId == FootballDemoSeed.AcademyId)); collections = await db.Collections.CountAsync(); notices = await db.AcademyNotices.CountAsync();
            // Simulate a saved user withdrawal in this isolated, known synthetic seed.
            // The history must accompany the change; neither seed nor restart reopens it.
            var draft = await db.PlayerDevelopmentGoals.SingleAsync(g => g.Id == FootballDemoSeed.Id("player-development-goal/1"));
            draft.State = "Withdrawn"; draft.Revision++; draft.UpdatedAtUtc = DateTimeOffset.UtcNow;
            db.PlayerGoalChanges.Add(new PlayerGoalChange { Id = Guid.NewGuid(), AcademyId = draft.AcademyId, GoalId = draft.Id, SportId = draft.SportId, ActorUserId = draft.CreatedByUserId, ActorName = "مالك عرض اصطناعي", RequestId = Guid.NewGuid(), RequestHash = "isolated-withdrawal-fixture", Revision = draft.Revision, Action = "Withdrawn", SnapshotJson = "{\"State\":\"Withdrawn\"}", CreatedAtUtc = draft.UpdatedAtUtc, UpdatedAtUtc = draft.UpdatedAtUtc });
            await db.SaveChangesAsync();
            source = System.Text.Json.JsonSerializer.Serialize(await db.PlayerDevelopmentGoals.AsNoTracking().Where(g => g.AcademyId == FootballDemoSeed.AcademyId).OrderBy(g => g.Id).Select(g => new { g.Id, g.Title, g.Target, g.ReviewOn, g.State, g.PublishedAtUtc }).ToArrayAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => PlayerGoalDemoSeed.Seed(db, new HostingEnvironment { EnvironmentName = "Production" }, new DemoOptions { SeedEnabled = true, SeedProfile = "Football" }));
        }
        await using (var second = new Factory()) {
            using var c = second.CreateClient(); await using var scope = second.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            Assert.Equal(source, System.Text.Json.JsonSerializer.Serialize(await db.PlayerDevelopmentGoals.AsNoTracking().Where(g => g.AcademyId == FootballDemoSeed.AcademyId).OrderBy(g => g.Id).Select(g => new { g.Id, g.Title, g.Target, g.ReviewOn, g.State, g.PublishedAtUtc }).ToArrayAsync())); Assert.Equal(3, await db.PlayerGoalChanges.CountAsync(c => c.AcademyId == FootballDemoSeed.AcademyId)); Assert.Equal(collections, await db.Collections.CountAsync()); Assert.Equal(notices, await db.AcademyNotices.CountAsync());
        }
    }
}
