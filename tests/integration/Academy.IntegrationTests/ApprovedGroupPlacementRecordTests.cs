using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    [Fact]
    public async Task Approved_group_placement_revision_readback_is_bound_to_group_tenant_and_exact_selected_enrollments()
    {
        using var owner=await Staff();var f=await PlacementReady(owner);
        var review=await ReviewPlacement(owner,f.Group,f.First);Assert.Empty(review.Conflicts);
        var placed=await Place(owner,f.Group,review);placed.EnsureSuccessStatusCode();
        var command=await placed.Content.ReadFromJsonAsync<JsonElement>();var id=command.GetProperty("id").GetGuid();
        var path=$"/api/v1/manage/groups/{f.Group}/placement-revisions/{id}";
        var read=await owner.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(id,read.GetProperty("id").GetGuid());Assert.Equal(f.Group,read.GetProperty("trainingGroupId").GetGuid());
        Assert.Equal(1,read.GetProperty("placed").GetInt32());Assert.Equal(PlacementToday.ToString("yyyy-MM-dd"),read.GetProperty("effectiveDate").GetString());
        Assert.Equal(f.First,Assert.Single(read.GetProperty("assignedEnrollmentIds").EnumerateArray()).GetGuid());
        Assert.False(read.TryGetProperty("requestKey",out _));Assert.False(read.TryGetProperty("snapshot",out _));
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail);using var guardian=await Guardian();
        Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await guardian.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync($"/api/v1/manage/groups/{f.Group}/placement-revisions/{Guid.NewGuid()}")).StatusCode);
        var other=await PlacementReady(owner);
        Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync($"/api/v1/manage/groups/{other.Group}/placement-revisions/{id}")).StatusCode);
    }
    [Fact]
    public async Task Placement_search_finds_assigned_player_without_silently_reassigning_or_crossing_academies()
    {
        using var owner=await Staff();var f=await PlacementReady(owner);
        var preview=await ReviewPlacement(owner,f.Group,f.First);(await Place(owner,f.Group,preview)).EnsureSuccessStatusCode();
        string code;
        await using(var scope=factory.Services.CreateAsyncScope())code=await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().SportEnrollments.Where(e=>e.Id==f.First).Select(e=>e.Player.PlayerCode).SingleAsync();
        var path=$"/api/v1/manage/groups/{f.Group}/placement-candidates?search={Uri.EscapeDataString(code.ToLowerInvariant())}&offset=0";
        var result=await owner.GetFromJsonAsync<JsonElement>(path);var player=Assert.Single(result.GetProperty("players").EnumerateArray());
        Assert.Equal(f.First,player.GetProperty("id").GetGuid());Assert.Equal(f.Group,player.GetProperty("trainingGroupId").GetGuid());Assert.NotEmpty(player.GetProperty("reasons").EnumerateArray());
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Group_creation_and_placement_accept_no_training_level_without_changing_money()
    {
        using var owner=await Staff();var fixture=await NewGroupReady(owner);var body=fixture.Body with{LevelId=null};
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var money=await db.Collections.CountAsync();
        var review=await CreationReview(owner,body);Assert.Empty(review.Conflicts);
        var saved=await CreationSave(owner,body,review);Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
        var id=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Null(await db.TrainingGroups.Where(g=>g.Id==id).Select(g=>g.TrainingLevelId).SingleAsync());
        Assert.Equal(2,await db.SportEnrollments.CountAsync(e=>e.TrainingGroupId==id));Assert.Equal(money,await db.Collections.CountAsync());
    }
    [Fact]
    public async Task Expiry_reminder_action_is_owner_only_scoped_and_repeating_it_never_recollects()
    {
        using var owner=await Staff();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var money=await db.Collections.CountAsync();
        var first=await CompensationCommand(owner,"/api/v1/subscriptions/home/expiry-notices",new{});first.EnsureSuccessStatusCode();
        var second=await CompensationCommand(owner,"/api/v1/subscriptions/home/expiry-notices",new{});second.EnsureSuccessStatusCode();Assert.Equal(0,(await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("periods").GetInt32());
        Assert.Equal(money,await db.Collections.CountAsync());
        Assert.Equal(HttpStatusCode.NotFound,(await CompensationCommand(owner,"/api/v1/subscriptions/home/expiry-notices",new{branchId=Guid.NewGuid()})).StatusCode);
        using var admin=await Staff(DemoSeed.AdminEmail);Assert.Equal(HttpStatusCode.Forbidden,(await CompensationCommand(admin,"/api/v1/subscriptions/home/expiry-notices",new{})).StatusCode);
        using var guardian=await Guardian();Assert.Equal(HttpStatusCode.Forbidden,(await CompensationCommand(guardian,"/api/v1/subscriptions/home/expiry-notices",new{})).StatusCode);
    }

}
