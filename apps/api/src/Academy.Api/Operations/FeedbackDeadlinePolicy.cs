using System.Text.Json;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class FeedbackDeadlinePolicy
{
    // Same lock as configuration publication: an intake observes one committed version.
    public static async Task Apply(FoundationDbContext db, GuardianFeedback item, string zone, DateOnly today)
    {
        if (item.Kind == "Rating") return;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + item.AcademyId}, 0))");
        var config = await db.AcademyConfigurationVersions.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == item.AcademyId && x.State == ConfigurationState.Active);
        if (config == null || AcademyRuntimeSettings.Read(config.Payload).FeedbackFollowUpDays is not int days) return;
        if (days is < 1 or > 30) throw new InvalidOperationException("مهلة المتابعة المنشورة غير صالحة.");
        item.DueOn = today.AddDays(days);
        item.FollowUpPolicyEvidence = JsonSerializer.Serialize(new { configurationId = config.Id, revision = config.Revision, days, timeZone = zone, receivedOn = today, dueOn = item.DueOn });
    }
}
