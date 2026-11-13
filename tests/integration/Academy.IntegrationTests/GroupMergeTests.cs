using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2PlanPublicationTests
{
    [Fact]
    public async Task Group_merge_preserves_history_is_atomic_and_replays_once()
    {
        using var owner = await Staff();
        using var foreign = await Staff("owner.future@example.test");
        var fixture = await PlacementReady(owner);
        (await Place(owner, fixture.Group, await ReviewPlacement(owner, fixture.Group, fixture.First, fixture.Second)))
            .EnsureSuccessStatusCode();
        var destination = await TransferTarget(owner, fixture, 2);
        var write = new GroupMergeWrite(fixture.Group, destination, PlacementToday.AddDays(1));
        const string path = "/api/v1/manage/groups/merge";
        Assert.Equal(HttpStatusCode.NotFound, (await Write(foreign, path + "/preview", write)).StatusCode);
        var previewResponse = await Write(owner, path + "/preview", write);
        Assert.True(previewResponse.IsSuccessStatusCode, await previewResponse.Content.ReadAsStringAsync());
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(preview.GetProperty("conflicts").EnumerateArray());
        Assert.Equal(2, preview.GetProperty("count").GetInt32());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var target = await db.TrainingGroups.SingleAsync(g => g.Id == destination);
        var key = Guid.NewGuid().ToString();
        async Task<HttpResponseMessage> Save(string fingerprint)
        {
            var csrf = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(write with { Fingerprint = fingerprint }) };
            request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
            request.Headers.Add("Idempotency-Key", key);
            return await owner.SendAsync(request);
        }

        target.Capacity = 1;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await Save(preview.GetProperty("fingerprint").GetString()!)).StatusCode);
        Assert.False(await db.EnrollmentGroupTransfers.AnyAsync(x => x.TargetGroupId == destination));

        target.Capacity = 2;
        await db.SaveChangesAsync();
        var fresh = await Write(owner, path + "/preview", write);
        fresh.EnsureSuccessStatusCode();
        var fingerprint = (await fresh.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fingerprint").GetString()!;
        var collections = await db.Collections.CountAsync();
        var attendance = await db.PlayerAttendances.CountAsync();
        var saved = await Save(fingerprint);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        Assert.True((await (await Save(fingerprint)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());
        Assert.Equal(2, await db.EnrollmentGroupTransfers.CountAsync(x => x.TargetGroupId == destination));
        Assert.Equal(collections, await db.Collections.CountAsync());
        Assert.Equal(attendance, await db.PlayerAttendances.CountAsync());
        Assert.Equal(2, await db.SportEnrollments.CountAsync(e => (e.Id == fixture.First || e.Id == fixture.Second)
            && FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, PlacementToday) == fixture.Group
            && FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, write.EffectiveDate) == destination));
    }
}
