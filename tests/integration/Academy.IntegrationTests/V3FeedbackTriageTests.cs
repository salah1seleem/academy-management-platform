using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    private static async Task<(Guid Child, Guid Group, DateOnly Today)> TriageOptions(HttpClient family)
    {
        var child = Slice2DemoSeed.OmarPlayerId; var options = await family.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{child}/feedback-options");
        return (child, options.GetProperty("groups")[0].GetProperty("id").GetGuid(), DateOnly.Parse(options.GetProperty("asOfDate").GetString()!));
    }
    [Fact] public async Task Feedback_triage_safety_intake_replay_private_review_keeps_original_and_requires_human_acknowledgement()
    {
        using var family = await Mobile(AcademyRole.Guardian); using var owner = await Staff(); var (child, group, today) = await TriageOptions(family);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var notices = await db.AcademyNotices.CountAsync(); var owners = await db.AcademyMemberships.Where(m => m.AcademyId == DemoSeed.NogoomAcademyId && m.IsActive && m.User.IsActive && m.Role == AcademyRole.AcademyOwner).Select(m => m.UserId).Distinct().CountAsync();
        var money = await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28");
        var body = new FeedbackWrite(group, null, "Complaint", null, "بلاغ سلامة اصطناعي خاص للمراجعة، ليس واقعة حقيقية", "Water", "Safety", today.AddDays(-1)); var key = Guid.NewGuid().ToString();
        var path = $"/api/v1/guardian/children/{child}/feedback"; var made = await Send(family, path, body, key: key); Assert.Equal(HttpStatusCode.Created, made.StatusCode); var id = (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Send(family, path, body, key: key)).EnsureSuccessStatusCode(); Assert.Equal(notices + owners, await db.AcademyNotices.CountAsync());
        var noticeRows = await db.AcademyNotices.OrderByDescending(n => n.CreatedAtUtc).Take(owners).ToArrayAsync(); Assert.All(noticeRows, n => { Assert.DoesNotContain(body.Message!, n.Message); Assert.DoesNotContain(child.ToString(), n.Message); });
        var source = $"/api/v1/manage/feedback/{id}"; var row = await owner.GetFromJsonAsync<JsonElement>(source); Assert.True(row.GetProperty("safety").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, source + "/resolve", new FeedbackResolution("رد دون فرز بشري ممنوع"))).StatusCode);
        var review = new FeedbackTriageWrite("Facility", "Routine", row.GetProperty("version").GetUInt32(), Guid.NewGuid());
        Assert.All(await Task.WhenAll(Send(owner, source + "/triage", review), Send(owner, source + "/triage", review)), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, source + "/triage", review with { Note = "محتوى مختلف" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, source + "/triage", review with { RequestId = Guid.NewGuid() })).StatusCode);
        row = await owner.GetFromJsonAsync<JsonElement>(source); Assert.Equal("Routine", row.GetProperty("urgency").GetString()); Assert.Equal("Safety", row.GetProperty("reportedUrgency").GetString()); Assert.Equal("Water", row.GetProperty("reportedSubject").GetString()); Assert.True(row.GetProperty("safety").GetBoolean()); Assert.NotEqual(JsonValueKind.Null, row.GetProperty("triagedAtUtc").ValueKind);
        var history = Assert.Single(row.GetProperty("history").EnumerateArray()); Assert.Equal("Safety", history.GetProperty("beforeUrgency").GetString()); Assert.Equal("Routine", history.GetProperty("afterUrgency").GetString()); Assert.Equal(JsonValueKind.Null, history.GetProperty("internalNote").ValueKind);
        (await Send(family, path, body, key: key)).EnsureSuccessStatusCode(); Assert.Equal(notices + owners, await db.AcademyNotices.CountAsync());
        var mine = await family.GetFromJsonAsync<JsonElement>(path + "-options"); var visible = mine.GetProperty("mine").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id); Assert.Equal("Safety", visible.GetProperty("reportedUrgency").GetString()); Assert.False(visible.TryGetProperty("urgency", out _)); Assert.False(visible.TryGetProperty("triagedAtUtc", out _));
        var list = await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/feedback?safetyOnly=true&status=Unresolved"); Assert.Contains(list.EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        async Task<int> SafetyCount() => (await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/owner-summary?from=2026-09-01&to=2026-09-28")).GetProperty("operations").GetProperty("openSafetyFeedback").GetInt32();
        var count = await SafetyCount(); Assert.True(count > 0);
        (await Send(owner, source + "/resolve", new FeedbackResolution("رد اصطناعي للأسرة بعد الفرز البشري"))).EnsureSuccessStatusCode(); Assert.Equal(count - 1, await SafetyCount());
        row = await owner.GetFromJsonAsync<JsonElement>(source); (await Send(owner, source + "/transition", new FeedbackCaseWrite("Reopened", row.GetProperty("version").GetUInt32(), Guid.NewGuid()))).EnsureSuccessStatusCode();
        Assert.Equal(count, await SafetyCount()); Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, source + "/resolve", new FeedbackResolution("إعادة الحل تتطلب فرزًا جديدًا"))).StatusCode);
        Assert.Equal(money, await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
    }
    [Fact] public async Task Feedback_triage_rejects_invalid_future_rating_foreign_roles_and_preserves_intake_against_sql()
    {
        using var family = await Mobile(AcademyRole.Guardian); using var owner = await Staff(); using var foreign = await Staff(DemoSeed.FutureOwnerEmail); using var admin = await Staff(DemoSeed.AdminEmail); using var coach = await Mobile(AcademyRole.Coach);
        var (child, group, today) = await TriageOptions(family); var path = $"/api/v1/guardian/children/{child}/feedback";
        var body = new FeedbackWrite(group, null, "Complaint", null, "بلاغ اصطناعي لاختبار التحقق والعزل", "Schedule", "Urgent", today);
        foreach (var invalid in new[] { body with { Subject = "Unknown" }, body with { Urgency = "AutoResolved" }, body with { IncidentOn = today.AddDays(1) }, body with { Kind = "Rating", Score = 4 } }) Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(family, path, invalid, key: Guid.NewGuid().ToString())).StatusCode);
        var made = await Send(family, path, body, key: Guid.NewGuid().ToString()); made.EnsureSuccessStatusCode(); var id = (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); var source = $"/api/v1/manage/feedback/{id}";
        var row = await owner.GetFromJsonAsync<JsonElement>(source); var review = new FeedbackTriageWrite("Schedule", "Urgent", row.GetProperty("version").GetUInt32(), Guid.NewGuid());
        foreach (var c in new[] { family, admin, coach }) { Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/v1/manage/feedback?safetyOnly=true")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Send(c, source + "/triage", review)).StatusCode); }
        Assert.Equal(HttpStatusCode.NotFound, (await Send(foreign, source + "/triage", review)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, source + "/triage", review with { Subject = "invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, source + "/triage", review with { Note = new string('x', 501) })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var denied = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.GuardianFeedbacks.Where(f => f.Id == id).ExecuteUpdateAsync(s => s.SetProperty(f => f.ReportedUrgency, "Routine"))); Assert.Equal("23514", denied.SqlState);
        var invalidFilter = await owner.GetAsync("/api/v1/manage/feedback?subject=Unknown"); Assert.Equal(HttpStatusCode.BadRequest, invalidFilter.StatusCode);
        var reportBefore = await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"); (await Send(owner, source + "/triage", review)).EnsureSuccessStatusCode(); Assert.Equal(reportBefore, await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
    }
}
