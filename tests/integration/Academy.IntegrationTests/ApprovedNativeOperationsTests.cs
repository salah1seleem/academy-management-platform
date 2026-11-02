using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact] public async Task Native_coach_assignment_concurrent_retries_create_one_assignment_and_reject_other_academies()
    {
        using var owner=await Staff();using var foreign=await Staff(DemoSeed.FutureOwnerEmail);using var guardian=await Mobile(AcademyRole.Guardian);
        var options=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/structure/options");
        var group=options.GetProperty("groups")[0].GetProperty("id").GetGuid();
        var token=Guid.NewGuid().ToString("N");
        var response=await Send(owner,"/api/v1/manage/coaches",new {displayName="مدرب عمليات اصطناعي "+token[..6],phoneNumber="010"+Random.Shared.NextInt64(10000000,99999999),email=(string?)null,isActive=true,groupIds=Array.Empty<Guid>()});
        response.EnsureSuccessStatusCode();var coach=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("membershipId").GetGuid();
        var body=new {membershipId=coach};var path=$"/api/v1/manage/groups/{group}/coaches";
        var attempts=await Task.WhenAll(Send(owner,path,body),Send(owner,path,body));
        Assert.Single(attempts,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(attempts,r=>r.StatusCode==HttpStatusCode.OK);
        var id=await attempts[0].Content.ReadFromJsonAsync<Guid>();Assert.Equal(id,await attempts[1].Content.ReadFromJsonAsync<Guid>());
        var saved=await owner.GetFromJsonAsync<JsonElement>($"{path}/{coach}");
        Assert.Equal(id,saved.GetProperty("id").GetGuid());Assert.Equal(group,saved.GetProperty("trainingGroupId").GetGuid());Assert.Equal(coach,saved.GetProperty("academyMembershipId").GetGuid());Assert.True(saved.GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync($"{path}/{coach}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await guardian.GetAsync($"{path}/{coach}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(foreign,path,body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Send(guardian,path,body)).StatusCode);
        // A previous soft-unassignment must reuse its unique historical pair.
        var disabled = await Send(owner, $"/api/v1/manage/group-assignments/{id}/status", new { isActive = false }, put: true);
        disabled.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{path}/{coach}")).StatusCode);
        var reactivated = await Task.WhenAll(Send(owner, path, body), Send(owner, path, body));
        foreach (var replay in reactivated) { Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(id, await replay.Content.ReadFromJsonAsync<Guid>()); }
        Assert.True((await owner.GetFromJsonAsync<JsonElement>($"{path}/{coach}")).GetProperty("isActive").GetBoolean());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Single(await db.StaffGroupAssignments.Where(a=>a.AcademyId==DemoSeed.NogoomAcademyId&&a.TrainingGroupId==group&&a.AcademyMembershipId==coach&&a.IsActive).ToArrayAsync());
    }
}
