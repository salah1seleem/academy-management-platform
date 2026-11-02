using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Operations;
namespace Academy.IntegrationTests;
public sealed partial class V2StaffScopeTests
{
    [Fact]
    public async Task Group_communication_requires_explicit_named_grant_and_never_crosses_branch_scope()
    {
        var f=await CreateAdmin();using var owner=f.Owner;using var admin=f.Admin;var data=await Data();
        var allowed=Access(data.A,edit:true) with{WorkflowActions=["workflow.groups.communicate"]};
        (await Write(owner,$"/api/v1/manage/staff/{f.Member}/access",allowed,true)).EnsureSuccessStatusCode();
        var a=$"/api/v1/communication/groups/{data.GroupA}";var b=$"/api/v1/communication/groups/{data.GroupB}";
        Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync(a)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync(b)).StatusCode);
        var preview=await Write(admin,a+"/preview",new{message="تذكير عام للمجموعة",requestId=Guid.NewGuid()},false);preview.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound,(await Write(admin,b+"/preview",new{message="تذكير",requestId=Guid.NewGuid()},false)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Write(admin,a+"/whatsapp",new{inviteUrl=(string?)null,expectedVersion=0,coachCanSend=true},true)).StatusCode);
        (await Write(owner,$"/api/v1/manage/staff/{f.Member}/access",allowed with{WorkflowActions=[]},true)).EnsureSuccessStatusCode();
        Assert.False((await admin.GetFromJsonAsync<JsonElement>(a)).GetProperty("canSend").GetBoolean());Assert.Equal(HttpStatusCode.Forbidden,(await Write(admin,a+"/preview",new{message="تذكير",requestId=Guid.NewGuid()},false)).StatusCode);
    }
}
