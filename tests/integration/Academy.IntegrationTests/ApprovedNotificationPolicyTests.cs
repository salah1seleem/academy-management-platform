using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class V2StaffScopeTests
{
    [Fact] public async Task Approved_notice_policy_stale_rejected_suppressed_history_and_role_isolation()
    {
        var f=await CreateAdmin();using var owner=f.Owner;using var admin=f.Admin;
        var initial=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/notifications/templates");var before=initial.EnumerateArray().Single(t=>t.GetProperty("key").GetString()=="DaysAdded");
        var message=before.GetProperty("message").GetString()!;var enabled=before.GetProperty("enabled").GetBoolean();
        var body=new{message="أضيف {count} يوم لاشتراك {اسم_اللاعب}",enabled=false,expectedVersion=before.GetProperty("version").GetUInt32()};
        (await Write(owner,"/api/v1/manage/notifications/templates/DaysAdded",body)).EnsureSuccessStatusCode();
        try{
            Assert.Equal(HttpStatusCode.Conflict,(await Write(owner,"/api/v1/manage/notifications/templates/DaysAdded",body)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Write(owner,"/api/v1/manage/notifications/templates/DaysAdded",new{message="{غير_معروف}"})).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Write(owner,"/api/v1/manage/notifications/templates/Expiring",new{message="قرب الانتهاء",timingDays=0})).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("/api/v1/manage/notifications/history")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Write(admin,"/api/v1/manage/notifications/templates/DaysAdded",new{message="رفض"})).StatusCode);
            await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var user=await db.AcademyMemberships.Where(m=>m.AcademyId==DemoSeed.NogoomAcademyId&&m.Role==Academy.Infrastructure.Tenancy.AcademyRole.AcademyOwner).Select(m=>m.UserId).FirstAsync();
            var notice=new AcademyNotice{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,UserId=user,AudienceRole="AcademyOwner",CreatedAtUtc=DateTimeOffset.UtcNow,UpdatedAtUtc=DateTimeOffset.UtcNow,EventKey="DaysAdded",Message="حدث اصطناعي معطل",TargetKind="SubscriptionPeriod",TargetId=Guid.NewGuid()};
            db.AcademyNotices.Add(notice);await db.SaveChangesAsync();Assert.False(notice.IsActive);
            var inbox=await owner.GetFromJsonAsync<JsonElement>("/api/v1/notifications");Assert.DoesNotContain(inbox.GetProperty("items").EnumerateArray(),n=>n.GetProperty("id").GetGuid()==notice.Id);
            var history=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/notifications/history?eventKey=DaysAdded&page=1");Assert.Contains(history.GetProperty("items").EnumerateArray(),n=>n.GetProperty("id").GetGuid()==notice.Id&&n.GetProperty("status").GetString()=="Disabled");
            Assert.True(history.GetProperty("items").GetArrayLength()<=25);
        }finally{
            var current=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/notifications/templates");var version=current.EnumerateArray().Single(t=>t.GetProperty("key").GetString()=="DaysAdded").GetProperty("version").GetUInt32();
            (await Write(owner,"/api/v1/manage/notifications/templates/DaysAdded",new{message,enabled,expectedVersion=version})).EnsureSuccessStatusCode();
        }
    }
    [Fact] public async Task Approved_cash_purpose_cannot_write_or_bind_an_unrelated_notice()
    {
        var f=await CreateAdmin();using var owner=f.Owner;using var admin=f.Admin;
        foreach(var family in new[]{false,true}){
            await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var m=await db.AcademyMemberships.AsNoTracking().FirstAsync(m=>m.AcademyId==DemoSeed.NogoomAcademyId&&m.Role==Academy.Infrastructure.Tenancy.AcademyRole.AcademyOwner);
            db.BindRequestScope(m.AcademyId,m.UserId,m.Id,null,module:"payments",operation:"Add");
            var enrollment=Guid.NewGuid();var plan=Guid.NewGuid();
            if(family)db.LimitToCashFamilyPayment(Guid.NewGuid(),Academy.Api.Slice2.Slice2DemoSeed.CityBranchId,Guid.NewGuid(),new Dictionary<Guid,Guid>{{enrollment,plan}});
            else db.LimitToCashPayment(Guid.NewGuid(),Academy.Api.Slice2.Slice2DemoSeed.CityBranchId,enrollment,plan);
            var n=new AcademyNotice{Id=Guid.NewGuid(),AcademyId=m.AcademyId,UserId=m.UserId,AudienceRole="Guardian",EventKey="SingleRenewal",TargetKind="SubscriptionPeriod",TargetId=Guid.NewGuid(),Message="تنبيه غير مرتبط لا يسمح به",CreatedAtUtc=DateTimeOffset.UtcNow,UpdatedAtUtc=DateTimeOffset.UtcNow};
            db.AcademyNotices.Add(n);
            await Assert.ThrowsAsync<ResourceScopeException>(()=>db.BindCashLifecycleNotice(n,enrollment));
            await Assert.ThrowsAsync<ResourceScopeException>(()=>db.SaveChangesAsync());
        }
    }

}
