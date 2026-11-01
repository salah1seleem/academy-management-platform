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
    [Fact] public async Task Today_plan_reads_recorded_cases_with_owner_and_branch_isolation_without_side_effects()
    {
        using var owner=await Staff();using var other=await Staff(DemoSeed.FutureOwnerEmail);
        using var admin=await Staff(DemoSeed.AdminEmail);using var guardian=await Mobile(AcademyRole.Guardian);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var group=await FollowUpGroup(db);var path=$"/api/v1/reports/owner-cockpit?branchId={group.BranchId}";
        async Task<JsonElement> Plan()=>(await owner.GetFromJsonAsync<JsonElement>(path)).GetProperty("todayPlan");
        var before=await Plan();await AbsenceRecords(db,group);
        var created=await Send(owner,"/api/v1/manage/owner-followups/",new OwnerFollowUpCreate(group.Id,"GroupRecordedAbsences",Guid.NewGuid()));created.EnsureSuccessStatusCode();
        var money=await db.Collections.CountAsync();var notices=await db.AcademyNotices.CountAsync();
        var after=await Plan();Assert.Equal(before.GetProperty("openFollowUps").GetInt32()+1,after.GetProperty("openFollowUps").GetInt32());
        Assert.Equal(before.GetProperty("recordedAbsenceGroups").GetInt32()+1,after.GetProperty("recordedAbsenceGroups").GetInt32());
        Assert.DoesNotContain("PlayerId",after.GetRawText());Assert.DoesNotContain("absenceEvidence",after.GetRawText());
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync(path)).StatusCode);
        foreach(var actor in new[]{admin,guardian})Assert.Equal(HttpStatusCode.Forbidden,(await actor.GetAsync(path)).StatusCode);
        Assert.Equal(money,await db.Collections.CountAsync());Assert.Equal(notices,await db.AcademyNotices.CountAsync());
    }
}
