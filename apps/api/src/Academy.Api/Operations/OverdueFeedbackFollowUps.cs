using System.Text.Json.Serialization;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public static partial class OwnerFollowUpEndpoints
{
    private const string OverdueFeedbackCondition = "GroupOverdueFeedback";
    internal sealed record OverdueFeedbackSummary(int ComplaintCount, int SuggestionCount, int SubmitterCount,
        DateOnly? OldestDueOn, DateOnly AsOfDate, [property: JsonIgnore] Guid[] FeedbackIds);
    private static async Task<Dictionary<Guid, OverdueFeedbackSummary>> OverdueFeedbackSources(FoundationDbContext db, Guid academy, Guid[] groups, DateOnly today)
    {
        // A real, explicitly owner-set deadline, not a guessed SLA from creation time.
        // Never carry family text, identities or child IDs into a delegated summary.
        var feedback = await db.GuardianFeedbacks.AsNoTracking().Where(f => f.AcademyId == academy && groups.Contains(f.TrainingGroupId)
            && f.IsActive && (f.Kind == "Complaint" || f.Kind == "Suggestion") && (f.Status == "Open" || f.Status == "Reviewed")
            && f.DueOn != null && f.DueOn < today)
            .OrderBy(f => f.DueOn).ThenBy(f => f.Id)
            .Select(f => new { f.Id, f.TrainingGroupId, f.Kind, f.GuardianUserId, f.DueOn }).ToArrayAsync();
        return groups.ToDictionary(g => g, g => {
            var rows = feedback.Where(f => f.TrainingGroupId == g).ToArray();
            return new OverdueFeedbackSummary(rows.Count(f => f.Kind == "Complaint"), rows.Count(f => f.Kind == "Suggestion"),
                rows.Select(f => f.GuardianUserId).Distinct().Count(), rows.Select(f => f.DueOn).Min(), today, rows.Select(f => f.Id).ToArray());
        });
    }
}
