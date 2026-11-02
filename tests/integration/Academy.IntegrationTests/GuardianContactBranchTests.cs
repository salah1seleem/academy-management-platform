using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;
public sealed partial class V2StaffScopeTests
{
    [Fact]
    public async Task Guardian_contacts_require_the_administrators_branch_and_player_read_grant()
    {
        var f=await CreateAdmin();using var owner=f.Owner;using var admin=f.Admin;var data=await Data();
        var access=Access(data.A) with {WorkflowActions=["workflow.players.read"]};
        (await Write(owner,$"/api/v1/manage/staff/{f.Member}/access",access,true)).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var playerA=await db.SportEnrollments.Where(e=>e.BranchId==data.A).Select(e=>e.PlayerId).SingleAsync();
        Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync($"/api/v1/people/players/{playerA}/guardian-contacts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/api/v1/people/players/{data.PlayerB}/guardian-contacts")).StatusCode);
        var revoke=access with {Actions=StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,false,false,false,false)).ToArray(),WorkflowActions=[]};
        (await Write(owner,$"/api/v1/manage/staff/{f.Member}/access",revoke,true)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync($"/api/v1/people/players/{playerA}/guardian-contacts")).StatusCode);
    }
}
