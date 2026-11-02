using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class BrandingMatchesTests
{
    [Fact]
    public async Task Urgent_match_preview_and_paged_inbox_share_live_family_role_and_recipient_scope_without_deleting_history()
    {
        using var owner = await Staff(); using var guardian = await Mobile(AcademyRole.Guardian); using var other = await Staff(DemoSeed.FutureOwnerEmail);
        var fixture = await Fixtures(owner);
        var create = await Send(owner, "/api/v1/manage/matches/", new MatchWrite(fixture.Tournament, fixture.Team, "منافس رحلة التنبيهات الاصطناعية", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2), new TimeOnly(17, 0), "ملعب عرض اصطناعي", null, null, null, [], true, true));
        create.EnsureSuccessStatusCode(); var match = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var me = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/me"); var user = me.GetProperty("userId").GetGuid(); var academy = DemoSeed.NogoomAcademyId;
        var now = DateTimeOffset.UtcNow;
        var baseline = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/notifications?urgentMatchesOnly=true");
        var counts = (await db.PlayerAttendances.CountAsync(), await db.PlayerEvaluations.CountAsync(), await db.Collections.CountAsync());
        var link = await db.GuardianPlayerLinks.FirstAsync(l => l.AcademyId == academy && l.Guardian.UserId == user && l.PlayerId == fixture.Player && l.IsActive);
        var stranger = await db.AcademyMemberships.Where(m => m.AcademyId == academy && m.Role == AcademyRole.Guardian && m.IsActive && m.UserId != user).Select(m => m.UserId).FirstAsync();
        var notices = Enumerable.Range(0, 34).Select(i => new AcademyNotice { Id = Guid.NewGuid(), AcademyId = academy, UserId = i == 33 ? stranger : user, AudienceRole = i == 32 ? "Coach" : "Guardian", EventKey = "Match", MatchId = match, Message = $"تحديث موعد اصطناعي لاختبار الصفحات {i}", IsRead = i == 31, UrgentUntilUtc = i == 30 ? now.AddMinutes(-1) : now.AddHours(1), CreatedAtUtc = now.AddMilliseconds(i), UpdatedAtUtc = now }).ToArray();
        db.AcademyNotices.AddRange(notices); await db.SaveChangesAsync();
        Assert.All(notices, n => Assert.True(n.IsActive));
        try
        {
            var page = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/notifications?urgentMatchesOnly=true&page=1");
            Assert.Equal(baseline.GetProperty("total").GetInt32() + 30, page.GetProperty("total").GetInt32());
            Assert.Equal(25, page.GetProperty("items").GetArrayLength());
            var found = new HashSet<Guid>();
            for (var p = 1; p <= (page.GetProperty("total").GetInt32() + 24) / 25; p++)
            {
                var next = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/notifications?urgentMatchesOnly=true&page={p}");
                foreach (var n in next.GetProperty("items").EnumerateArray()) Assert.True(found.Add(n.GetProperty("id").GetGuid()));
            }
            Assert.All(notices.Take(30), n => Assert.Contains(n.Id, found));
            Assert.All(notices.Skip(30), n => Assert.DoesNotContain(n.Id, found));
            var home = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/guardian/home");
            Assert.Equal(3, home.GetProperty("urgentAlerts").GetArrayLength());
            Assert.All(home.GetProperty("urgentAlerts").EnumerateArray(), n => Assert.Contains(n.GetProperty("id").GetGuid(), found));
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/notifications?urgentMatchesOnly=true")).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await guardian.GetAsync("/api/v1/notifications?urgentMatchesOnly=true&page=0")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(other, $"/api/v1/notifications/{notices[0].Id}/read", new { isRead = true }, true)).StatusCode);
            var foreign = await other.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
            Assert.DoesNotContain(foreign.GetProperty("items").EnumerateArray(), n => notices.Any(x => x.Id == n.GetProperty("id").GetGuid()));
            (await Send(guardian, $"/api/v1/notifications/{notices[0].Id}/read", new { isRead = true }, true)).EnsureSuccessStatusCode();
            var read = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/notifications?urgentMatchesOnly=true");
            Assert.Equal(page.GetProperty("total").GetInt32() - 1, read.GetProperty("total").GetInt32());
            link.IsActive = false; await db.SaveChangesAsync();
            var unlinked = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/notifications?urgentMatchesOnly=true");
            Assert.DoesNotContain(unlinked.GetProperty("items").EnumerateArray(), n => n.GetProperty("matchId").GetGuid() == match);
            Assert.Equal(34, await db.AcademyNotices.CountAsync(n => notices.Select(x => x.Id).Contains(n.Id)));
            Assert.Equal(counts, (await db.PlayerAttendances.CountAsync(), await db.PlayerEvaluations.CountAsync(), await db.Collections.CountAsync()));
        }
        finally
        {
            link.IsActive = true;
            foreach (var n in notices) { await db.Entry(n).ReloadAsync(); n.IsRead = true; }
            await db.SaveChangesAsync();
        }
    }
}
