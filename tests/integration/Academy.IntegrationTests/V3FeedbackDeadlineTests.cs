using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact]
    public async Task Feedback_deadline_published_version_snapshot_replay_revocation_and_isolation()
    {
        using var owner = await Staff(); using var admin = await Staff(DemoSeed.AdminEmail);
        using var family = await Mobile(AcademyRole.Guardian); using var foreign = await Staff(DemoSeed.FutureOwnerEmail);
        var (child, group, today) = await TriageOptions(family);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var original = await db.AcademyConfigurationVersions.AsNoTracking().SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.State == ConfigurationState.Active);
        var settings = AcademyRuntimeSettings.Read(original.Payload);
        var money = await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28");
        async Task<Guid> Draft(int? days) {
            var response = await Send(owner, "/api/v1/manage/configuration/drafts", settings with { FeedbackFollowUpDays = days }); response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
        async Task Publish(Guid id) {
            foreach (var target in new[] {"Validated", "Staged", "Active"}) {
                var version = await db.AcademyConfigurationVersions.AsNoTracking().Where(x => x.Id == id).Select(x => x.Version).SingleAsync();
                (await Send(owner, $"/api/v1/manage/configuration/{id}/transition", new {target, version})).EnsureSuccessStatusCode();
            }
        }
        var path = $"/api/v1/guardian/children/{child}/feedback";
        var body = new FeedbackWrite(group, null, "Complaint", null, "طلب متابعة اصطناعي للتحقق من المهلة المحفوظة");
        async Task<Guid> Intake(string key) {
            var response = await Send(family, path, body, key: key); response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
        try {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, "/api/v1/manage/configuration/drafts", settings with { FeedbackFollowUpDays = 31 })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, "/api/v1/manage/configuration/drafts", settings with { FeedbackFollowUpDays = 2 })).StatusCode);
            var draft = await Draft(2); var oldId = await Intake(Guid.NewGuid().ToString());
            Assert.Null((await db.GuardianFeedbacks.AsNoTracking().SingleAsync(x => x.Id == oldId)).FollowUpPolicyEvidence);
            await Publish(draft);
            var key = Guid.NewGuid().ToString(); var id = await Intake(key); var row = await db.GuardianFeedbacks.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.Equal(today.AddDays(2), row.DueOn); Assert.Null(row.DueSetByUserId);
            var evidence = JsonDocument.Parse(row.FollowUpPolicyEvidence!).RootElement;
            Assert.Equal(draft, evidence.GetProperty("configurationId").GetGuid()); Assert.Equal(2, evidence.GetProperty("days").GetInt32());
            Assert.Equal("Africa/Cairo", evidence.GetProperty("timeZone").GetString());
            var detail = $"/api/v1/manage/feedback/{id}";
            Assert.Equal(row.FollowUpPolicyEvidence, (await owner.GetFromJsonAsync<JsonElement>(detail)).GetProperty("followUpPolicyEvidence").GetString());
            Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(detail)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await family.GetAsync(detail)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, detail + "/due", new FeedbackDueDate(today.AddDays(4)))).StatusCode);
            var options = await family.GetFromJsonAsync<JsonElement>(path + "-options");
            Assert.All(options.GetProperty("mine").EnumerateArray(), x => Assert.False(x.TryGetProperty("followUpPolicyEvidence", out _)));
            await Publish(await Draft(null));
            Assert.Equal(id, await Intake(key)); Assert.Equal(row.FollowUpPolicyEvidence, (await db.GuardianFeedbacks.AsNoTracking().SingleAsync(x => x.Id == id)).FollowUpPolicyEvidence);
            var noPolicy = await Intake(Guid.NewGuid().ToString()); Assert.Null((await db.GuardianFeedbacks.AsNoTracking().SingleAsync(x => x.Id == noPolicy)).DueOn);
            var rejected = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.GuardianFeedbacks.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.FollowUpPolicyEvidence, (string?)null))); Assert.Equal("23514", rejected.SqlState);
            Assert.Equal(money, await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
        } finally {
            await Publish(await Draft(settings.FeedbackFollowUpDays));
        }
    }
}
