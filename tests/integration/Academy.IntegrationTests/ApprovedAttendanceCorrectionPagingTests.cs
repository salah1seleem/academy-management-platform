using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Slice4;
using Academy.Api.Auth;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2PlanPublicationTests
{
    [Fact]
    public async Task Approved_correction_history_pages_beyond_100_with_stable_ties_and_tenant_and_requester_isolation()
    {
        SetVisitClock();
        using var first = await Staff();
        var f = await AttendanceReady(first);
        factory.VisitClock.Now = factory.VisitClock.GetUtcNow().AddDays(1);
        using var owner = await Staff();
        var original = await RequestCorrection(owner, f.Next, CorrectionBody(f.First));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var source = await db.AttendanceCorrections.AsNoTracking().SingleAsync(c => c.Id == original);
        var unchanged = (await db.Collections.CountAsync(), await db.PlayerAttendances.CountAsync());
        // Isolated integration DB fixture only: pending snapshots, no applied attendance or notices.
        using var foreign = await Staff("owner.future@example.test");
        var foreignBefore = (await Page(foreign, 1)).GetProperty("total").GetInt32();
        var fixtureCreatedAt = (await db.AttendanceCorrections.MaxAsync(c => (DateTimeOffset?)c.CreatedAtUtc) ?? factory.VisitClock.GetUtcNow()).AddMinutes(1);
        var history = Enumerable.Range(0, 107).Select(i => new AttendanceCorrection {
            Id = Guid.NewGuid(), AcademyId = source.AcademyId, TrainingSessionId = source.TrainingSessionId,
            TrainingGroupId = source.TrainingGroupId, BranchId = source.BranchId, SessionDate = source.SessionDate,
            TrainingSessionVersion = source.TrainingSessionVersion, Kind = source.Kind, Payload = source.Payload,
            Reason = "سجل تصحيح اصطناعي لاختبار الصفحات " + i,
            RequestedByUserId = source.RequestedByUserId, RequestedByMembershipId = source.RequestedByMembershipId,
            RequestKey = Guid.NewGuid().ToString("N"), RequestHash = source.RequestHash,
            State = AttendanceCorrectionState.Pending, ExpiresAtUtc = fixtureCreatedAt.AddDays(7),
            CreatedAtUtc = fixtureCreatedAt, UpdatedAtUtc = factory.VisitClock.GetUtcNow()
        }).ToArray();
        db.AttendanceCorrections.AddRange(history); await db.SaveChangesAsync();
        async Task<JsonElement> Page(HttpClient c, int page) => await c.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/corrections?page={page}");
        var seen = new List<Guid>();
        for (var page = 1; page <= 5; page++) {
            var view = await Page(owner, page);
            Assert.Equal(page, view.GetProperty("page").GetInt32()); Assert.Equal(25, view.GetProperty("pageSize").GetInt32());
            Assert.True(view.GetProperty("total").GetInt32() >= 108);
            var items = view.GetProperty("items").EnumerateArray().ToArray(); Assert.InRange(items.Length, 1, 25);
            seen.AddRange(items.Select(r => r.GetProperty("id").GetGuid()));
        }
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.All(history, r => Assert.Contains(r.Id, seen));
        var again = await Page(owner, 1);
        Assert.Equal(seen.Take(25), again.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()));
        var legacy = await owner.GetFromJsonAsync<JsonElement>("/api/v1/attendance/corrections"); Assert.Equal(100, legacy.GetArrayLength());
        var beyond = await Page(owner, 100000); Assert.Empty(beyond.GetProperty("items").EnumerateArray());
        var other = await Page(foreign, 1); Assert.Equal(foreignBefore, other.GetProperty("total").GetInt32()); Assert.DoesNotContain(other.GetProperty("items").EnumerateArray(), r => history.Any(h => h.Id == r.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/api/v1/attendance/corrections/{history.Last().Id}")).StatusCode);
        using var admin = await Staff(DemoSeed.AdminEmail);
        var adminPage = await Page(admin, 1); Assert.DoesNotContain(adminPage.GetProperty("items").EnumerateArray(), r => history.Any(h => h.Id == r.GetProperty("id").GetGuid()));
        using var guardian = await Guardian(); Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/v1/attendance/corrections?page=2")).StatusCode);
        Assert.Equal(unchanged, (await db.Collections.CountAsync(), await db.PlayerAttendances.CountAsync()));
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(100001)]
    public async Task Approved_correction_history_rejects_unbounded_or_invalid_pages(int page)
    {
        using var owner = await Staff();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/api/v1/attendance/corrections?page={page}")).StatusCode);
    }
}
