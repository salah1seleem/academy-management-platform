using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Mobile;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact] public async Task V41_inbox_scopes_role_tenant_user_and_persists_read_state()
    {
        using var owner=await Staff();using var other=await Staff(DemoSeed.FutureOwnerEmail);using var guardian=await Mobile(AcademyRole.Guardian);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var ownerId=await db.AcademyMemberships.Where(m=>m.AcademyId==DemoSeed.NogoomAcademyId&&m.Role==AcademyRole.AcademyOwner&&m.IsActive).Select(m=>m.UserId).FirstAsync();
        var mine=new AcademyNotice{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,UserId=ownerId,AudienceRole="AcademyOwner",Message="تنبيه اصطناعي خاص بالمالك",TargetKind="PaymentQueue",TargetId=Guid.NewGuid()};
        var wrongRole=new AcademyNotice{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,UserId=ownerId,AudienceRole="Guardian",Message="لا يظهر في دور المالك"};
        db.AcademyNotices.AddRange(mine,wrongRole);await db.SaveChangesAsync();
        var list=await owner.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
        Assert.Contains(list.GetProperty("items").EnumerateArray(),n=>n.GetProperty("id").GetGuid()==mine.Id);
        Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(),n=>n.GetProperty("id").GetGuid()==wrongRole.Id);
        foreach(var c in new[]{other,guardian}){
            var hidden=await c.GetFromJsonAsync<JsonElement>("/api/v1/notifications");Assert.DoesNotContain(hidden.GetProperty("items").EnumerateArray(),n=>n.GetProperty("id").GetGuid()==mine.Id);
            Assert.Equal(HttpStatusCode.NotFound,(await Send(c,$"/api/v1/notifications/{mine.Id}/read",new{isRead=true},true)).StatusCode);
        }
        (await Send(owner,$"/api/v1/notifications/{mine.Id}/read",new{isRead=true},true)).EnsureSuccessStatusCode();
        Assert.True(await db.AcademyNotices.AsNoTracking().Where(n=>n.Id==mine.Id).Select(n=>n.IsRead).SingleAsync());
        (await Send(owner,$"/api/v1/notifications/{mine.Id}/read",new{isRead=false},true)).EnsureSuccessStatusCode();
        Assert.False(await db.AcademyNotices.AsNoTracking().Where(n=>n.Id==mine.Id).Select(n=>n.IsRead).SingleAsync());
    }
    [Fact] public async Task V41_scoped_admin_own_inbox_calendar_and_help_do_not_open_management()
    {
        using var admin=await Staff(DemoSeed.AdminEmail);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var m=await db.AcademyMemberships.SingleAsync(m=>m.AcademyId==DemoSeed.NogoomAcademyId&&m.Role==AcademyRole.AcademyAdmin&&m.User.Email==DemoSeed.AdminEmail);var prior=m.UseActionGrants;m.UseActionGrants=true;await db.SaveChangesAsync();
        try{
            foreach(var path in new[]{"/api/v1/notifications","/api/v1/academy/calendar","/api/v1/academy/experience","/api/v1/page-help/home"})Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,"/api/v1/manage/notifications/send",new{})).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,"/api/v1/manage/page-help",new{},true)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("/api/v1/reports/featured-players")).StatusCode);
        }finally{m.UseActionGrants=prior;await db.SaveChangesAsync();}
    }
    [Fact] public async Task V41_video_configuration_selects_exact_role_and_screen_and_keeps_other_tenant_private()
    {
        using var owner=await Staff();using var other=await Staff(DemoSeed.FutureOwnerEmail);using var coach=await Mobile(AcademyRole.Coach);
        // URL is synthetic configuration only: no real video or network delivery claimed.
        (await Send(owner,"/api/v1/manage/page-help",new{role="AcademyOwner",screen="home",title="شرح اصطناعي للرئيسية",url="https://video.example.test/owner-home.mp4"},true)).EnsureSuccessStatusCode();
        var configured=await owner.GetFromJsonAsync<JsonElement>("/api/v1/page-help/home");Assert.True(configured.GetProperty("available").GetBoolean());Assert.Equal("شرح اصطناعي للرئيسية",configured.GetProperty("title").GetString());
        foreach(var c in new[]{other,coach}){var hidden=await c.GetFromJsonAsync<JsonElement>("/api/v1/page-help/home");Assert.False(hidden.GetProperty("available").GetBoolean());Assert.Equal(JsonValueKind.Null,hidden.GetProperty("url").ValueKind);}
        Assert.False((await owner.GetFromJsonAsync<JsonElement>("/api/v1/page-help/matches")).GetProperty("available").GetBoolean());
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,"/api/v1/manage/page-help",new{role="AcademyOwner",screen="home",title="مرفوض",url="http://127.0.0.1/secret"},true)).StatusCode);
    }
}
