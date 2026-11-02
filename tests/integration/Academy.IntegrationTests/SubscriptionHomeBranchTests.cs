using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Operations;
using Academy.Api.Auth;
namespace Academy.IntegrationTests;
public sealed partial class V2StaffScopeTests
{
    [Fact]
    public async Task Subscription_home_is_branch_scoped_without_an_owner_finance_or_player_contact_grant()
    {
        var f=await CreateAdmin();using var owner=f.Owner;using var admin=f.Admin;var data=await Data();var access=Access(data.A) with{Actions=StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,m=="subscriptions",false,false,false)).ToArray(),WorkflowActions=[]};(await Write(owner,$"/api/v1/manage/staff/{f.Member}/access",access,true)).EnsureSuccessStatusCode();
        var home=await admin.GetAsync("/api/v1/subscriptions/home");home.EnsureSuccessStatusCode();var body=await home.Content.ReadFromJsonAsync<JsonElement>();Assert.DoesNotContain(body.GetProperty("dimensions").EnumerateArray(),d=>d.GetString()=="branches");Assert.Equal(data.A,Assert.Single(body.GetProperty("branches").EnumerateArray()).GetProperty("id").GetGuid());Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/api/v1/subscriptions/home?branchId={data.B}")).StatusCode);var list=await admin.GetFromJsonAsync<JsonElement>("/api/v1/subscriptions/home/items");Assert.False(list.GetProperty("canOpenPlayer").GetBoolean());Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("/api/v1/reports/owner-summary")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync($"/api/v1/people/players/{data.PlayerB}/guardian-contacts")).StatusCode);
    }
    [Fact]
    public async Task Narrow_admin_can_manage_only_own_profile_without_media_or_settings_grants()
    {
        var f=await CreateAdmin();using var owner=f.Owner;using var admin=f.Admin;var data=await Data();
        var access=Access(data.A) with{Actions=StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,m=="subscriptions",false,false,false)).ToArray(),WorkflowActions=[]};
        (await Write(owner,$"/api/v1/manage/staff/{f.Member}/access",access,true)).EnsureSuccessStatusCode();
        var profile=await admin.GetAsync("/api/v1/me/profile");profile.EnsureSuccessStatusCode();
        (await Write(admin,"/api/v1/me/profile",new{gender="Female"},true)).EnsureSuccessStatusCode();
        Assert.Equal("Female",(await admin.GetFromJsonAsync<JsonElement>("/api/v1/me/profile")).GetProperty("gender").GetString());
        using var form=new MultipartFormDataContent();form.Add(new StringContent("Media"),"purpose");
        var csrf=await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/me/profile/photo"){Content=form};request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.SendAsync(request)).StatusCode);
        using var portraitForm=new MultipartFormDataContent();portraitForm.Add(new StringContent("SelfPhoto"),"purpose");
        var png=new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII="));png.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");portraitForm.Add(png,"file","synthetic-self.png");
        using var portraitRequest=new HttpRequestMessage(HttpMethod.Post,"/api/v1/me/profile/photo"){Content=portraitForm};portraitRequest.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());
        var saved=await admin.SendAsync(portraitRequest);Assert.Equal(HttpStatusCode.Created,saved.StatusCode);
        var url=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
        Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("/api/v1/assets/gallery")).StatusCode);
    }
}
