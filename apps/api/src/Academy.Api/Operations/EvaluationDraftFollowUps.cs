using System.Text.Json;
using System.Text.Json.Serialization;
using Academy.Api.Configuration;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static partial class OwnerFollowUpEndpoints
{
    private const string EvaluationDraftCondition = "GroupEvaluationDrafts";
    internal sealed record DraftReviewSummary(int DraftCount, int PlayerCount, DateTimeOffset? OldestCreatedAtUtc,
        DateTimeOffset CutoffUtc, [property: JsonIgnore] Guid[] EvaluationIds);

    private static async Task<Dictionary<Guid, DraftReviewSummary>?> DraftReviewSources(FoundationDbContext db, Guid academy, Guid[] groups, DateOnly today, DateTimeOffset now)
    {
        // Seven elapsed days is an explicit review rule, not an invented evaluation SLA.
        // Evaluations belong to the existing training entitlement; do not invent a new license.
        if (!await new AcademyConfigurationService(db, TimeProvider.System).Enabled(academy, "training")) return null;
        var cutoff = now.AddDays(-7);
        var drafts = await db.PlayerEvaluations.AsNoTracking().Where(e => e.AcademyId == academy && groups.Contains(e.TrainingGroupId)
            && e.IsActive && e.Status == EvaluationStatus.Draft && e.PublishedAtUtc == null && e.CreatedAtUtc <= cutoff && e.EvaluationDate <= today
            && e.SportEnrollment.IsActive && e.SportEnrollment.Player.IsActive)
            .OrderBy(e => e.CreatedAtUtc).ThenBy(e => e.Id)
            .Select(e => new { e.Id, e.TrainingGroupId, e.CreatedAtUtc, e.SportEnrollment.PlayerId }).ToArrayAsync();
        return groups.ToDictionary(g => g, g => {
            var rows = drafts.Where(d => d.TrainingGroupId == g).ToArray();
            return new DraftReviewSummary(rows.Length, rows.Select(d => d.PlayerId).Distinct().Count(), rows.Select(d => (DateTimeOffset?)d.CreatedAtUtc).Min(), cutoff, rows.Select(d => d.Id).ToArray());
        });
    }

    private static JsonElement FollowUpEvidence(OwnerFollowUp f, bool includeEvaluationIds)
    {
        // Assigned operational responsibility is not permission to read a child's draft.
        var evidence = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(f.EvidenceJson)!;
        if (!includeEvaluationIds) {
            evidence.Remove("evaluationIds");
            evidence.Remove("absenceEvidence");
        }
        return JsonSerializer.SerializeToElement(evidence);
    }
}
