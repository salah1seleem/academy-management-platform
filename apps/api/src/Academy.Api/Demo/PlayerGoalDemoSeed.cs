using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;
public static class PlayerGoalDemoSeed
{
    public static async Task Seed(FoundationDbContext db, IHostEnvironment env, DemoOptions options)
    {
        var academy = FootballDemoSeed.AcademyId;
        if (!env.IsEnvironment("Demo") || !options.SeedEnabled || options.SeedProfile != "Football" || !await db.Academies.AnyAsync(a => a.Id == academy && a.Slug == FootballDemoSeed.Slug && a.EnglishName == FootballDemoSeed.Marker)) throw new InvalidOperationException("أهداف العرض الاصطناعية تتطلب Football Demo معلوم الهوية.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"football-player-goals-demo"},0))");
        var player = FootballDemoSeed.Id("player/10");
        var enrollment = await db.SportEnrollments.SingleAsync(e => e.AcademyId == academy && e.PlayerId == player && e.IsActive);
        var owner = FootballDemoSeed.Id("user/owner"); var name = await db.Users.Where(u => u.Id == owner).Select(u => u.DisplayName).SingleAsync();
        var today = await AttendanceCalendar.TodayAsync(academy, db, TimeProvider.System); var now = TimeProvider.System.GetUtcNow();
        for (var i = 0; i < 2; i++)
        {
            var id = FootballDemoSeed.Id($"player-development-goal/{i}");
            if (await db.PlayerDevelopmentGoals.AnyAsync(g => g.Id == id)) continue;
            var goal = new PlayerDevelopmentGoal { Id = id, AcademyId = academy, SportEnrollmentId = enrollment.Id, SportId = enrollment.SportId, CreatedByUserId = owner,
                Title = i == 0 ? "دقة التمرير — هدف اصطناعي" : "التحرك دون كرة — مسودة اصطناعية", Target = i == 0 ? "نتابع دقة التمرير بالقدمين في التقييم الفني القادم، دون وعد بترقية آلية." : "مسودة عمل يراجعها فريق التدريب قبل نشرها للأسرة.", ReviewOn = today.AddDays(14), State = i == 0 ? "Published" : "Draft", PublishedAtUtc = i == 0 ? now : null, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.PlayerDevelopmentGoals.Add(goal);
            db.PlayerGoalChanges.Add(new() { Id = FootballDemoSeed.Id($"player-development-change/{i}"), AcademyId = academy, GoalId = id, SportId = enrollment.SportId, ActorUserId = owner, ActorName = name, Revision = 1,
                RequestId = id, RequestHash = "synthetic-seed", Action = i == 0 ? "Published" : "Created", SnapshotJson = JsonSerializer.Serialize(new { goal.Title, goal.Target, goal.ReviewOn, goal.State, goal.Revision }), CreatedAtUtc = now, UpdatedAtUtc = now });
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
