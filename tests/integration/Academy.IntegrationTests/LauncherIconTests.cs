using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Platform;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;
public sealed partial class PlatformSetupImportTests
{
    [Fact]
    public async Task Launcher_icon_mapping_is_platform_only_versioned_and_validates_live_membership_without_leaking_other_choices()
    {
        using var platform=await Platform();
        var a=await Create(platform,"01095"+Random.Shared.Next(100000,999999));
        var b=await Create(platform,"01096"+Random.Shared.Next(100000,999999));
        string email;
        await using(var scope=factory.Services.CreateAsyncScope()) {
            var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            foreach(var id in new[]{a,b}) { var academy=await db.Academies.SingleAsync(x=>x.Id==id);academy.IsActive=true;db.AcademyPlatformProfiles.Add(new(){AcademyId=id,EditVersion=1}); }
            email=(await db.AcademyMemberships.Where(m=>m.AcademyId==a&&m.Role==AcademyRole.AcademyOwner).Select(m=>m.User.Email).SingleAsync())!;
            await db.SaveChangesAsync();
        }
        var path=$"/api/v1/platform/academies/{a}/launcher-icon";
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(platform,path,new LauncherIcons.IconWrite(1,true,"downloaded-url"),true)).StatusCode);
        (await Send(platform,path,new LauncherIcons.IconWrite(1,true,LauncherIcons.Professional),true)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Send(platform,path,new LauncherIcons.IconWrite(1,false,null),true)).StatusCode);
        (await Send(platform,$"/api/v1/platform/academies/{b}/launcher-icon",new LauncherIcons.IconWrite(1,true,LauncherIcons.Professional),true)).EnsureSuccessStatusCode();
        using var owner=factory.CreateClient();
        (await Send(owner,"/api/v1/auth/login",new {email,password="Demo-Only-123!"},platform:false)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized,(await owner.GetAsync("/api/v1/platform/launcher-icons")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await Send(owner,path,new LauncherIcons.IconWrite(2,false,null),true,platform:false)).StatusCode);
        var choice=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/academy/launcher-icon?selectionAcademyId={a}");
        Assert.Equal(a,choice.GetProperty("academyId").GetGuid());Assert.True(choice.GetProperty("selectedAllowed").GetBoolean());Assert.Equal(LauncherIcons.Professional,choice.GetProperty("key").GetString());
        var foreign=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/academy/launcher-icon?selectionAcademyId={b}");Assert.False(foreign.GetProperty("selectedAllowed").GetBoolean());Assert.False(foreign.TryGetProperty("icons",out _));
        (await Send(platform,path,new LauncherIcons.IconWrite(2,false,null),true)).EnsureSuccessStatusCode();
        choice=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/academy/launcher-icon?selectionAcademyId={a}");Assert.Equal("Standard",choice.GetProperty("state").GetString());Assert.False(choice.GetProperty("selectedAllowed").GetBoolean());Assert.Equal(JsonValueKind.Null,choice.GetProperty("key").ValueKind);
        await using(var scope=factory.Services.CreateAsyncScope()) { var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(2,await db.PlatformAudits.CountAsync(x=>x.TargetAcademyId==a&&x.Action=="academy.launcher-icon"));var m=await db.AcademyMemberships.SingleAsync(m=>m.AcademyId==a&&m.Role==AcademyRole.AcademyOwner);m.IsActive=false;await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Forbidden,(await owner.GetAsync("/api/v1/academy/launcher-icon")).StatusCode);
    }
}
