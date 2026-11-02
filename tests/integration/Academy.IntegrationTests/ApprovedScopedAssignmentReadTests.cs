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

public sealed partial class V2StaffScopeTests
{
    [Fact]
    public async Task Scoped_assignment_readback_preserves_narrow_admin_branch_and_sport_without_shared_coach_profile_access()
    {
        var f = await CreateAdmin(); using var owner = f.Owner; using var admin = f.Admin;
        var data = await Data();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var coach = await db.AcademyMemberships.Where(m => m.AcademyId == DemoSeed.NogoomAcademyId && m.Role == AcademyRole.Coach && m.IsActive).Select(m => m.Id).FirstAsync();
        var anchor = await db.TrainingGroups.AsNoTracking().SingleAsync(g => g.Id == data.GroupA); var sport = anchor.SportId;
        // A narrow administrator may choose an already-visible coach, not import
        // an arbitrary coach from another branch. The owner establishes visibility.
        (await Write(owner, $"/api/v1/manage/groups/{data.GroupA}/coaches", new { membershipId = coach }, false)).EnsureSuccessStatusCode();
        var groupResponse = await Write(owner, "/api/v1/manage/groups", new { arabicName = "مجموعة ثانية لقبول إسناد الإداري " + Guid.NewGuid().ToString("N")[..6], branchId = data.A, sportId = sport, ageCategoryId = anchor.AgeCategoryId, capacity = 12 }, false); groupResponse.EnsureSuccessStatusCode();
        var target = await groupResponse.Content.ReadFromJsonAsync<Guid>();
        var pathA = $"/api/v1/manage/groups/{target}/coaches"; var pathB = $"/api/v1/manage/groups/{data.GroupB}/coaches";
        (await Write(owner, pathB, new { membershipId = coach }, false)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"{pathA}/{coach}")).StatusCode);
        var actions = StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m == "academy", m == "academy", m == "academy", false)).ToArray();
        var access = new StaffAccessWrite(false, [data.A], actions, AllSports: false, Sports: [sport], WorkflowActions: ["workflow.groups.manage"]);
        (await Write(owner, $"/api/v1/manage/staff/{f.Member}/access", access, true)).EnsureSuccessStatusCode();
        // Group editing does not imply the separate coach-assignment action.
        Assert.Equal(HttpStatusCode.Forbidden,(await Write(admin,pathA,new{membershipId=coach},false)).StatusCode);
        access=access with{WorkflowActions=["workflow.groups.manage","workflow.coaches.assign"]};
        (await Write(owner,$"/api/v1/manage/staff/{f.Member}/access",access,true)).EnsureSuccessStatusCode();
        try
        {
            var created = await Write(admin, pathA, new { membershipId = coach }, false); created.EnsureSuccessStatusCode();
            var id = await created.Content.ReadFromJsonAsync<Guid>();
            var read = await admin.GetFromJsonAsync<JsonElement>($"{pathA}/{coach}");
            Assert.Equal(id, read.GetProperty("id").GetGuid()); Assert.Equal(target, read.GetProperty("trainingGroupId").GetGuid());
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{pathB}/{coach}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/v1/manage/structure/coaches/{coach}")).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(admin, pathB, new { membershipId = coach }, false)).StatusCode);
        }
        finally
        {
            (await Write(owner, $"/api/v1/manage/staff/{f.Member}/access", access with { Actions = StaffAccessRules.Modules.Select(m => new StaffActionValue(m, false, false, false, false)).ToArray(), WorkflowActions = [] }, true)).EnsureSuccessStatusCode();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"{pathA}/{coach}")).StatusCode);
    }
}
