using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class V2StaffScopeTests
{
    [Fact] public async Task V4_daily_actions_narrow_CRUD_independently_and_are_revalidated_under_write_lock()
    {
        var fixture=await CreateAdmin();using var owner=fixture.Owner;using var admin=fixture.Admin;
        var actions=StaffAccessRules.Modules.Select(m=>new Academy.Api.Operations.StaffActionValue(m,true,m!="followups",true,false)).ToArray();
        var allowed=StaffWorkflowAccess.Actions.Where(a=>a.Key is not ("workflow.attendance.players" or "workflow.players.create" or "workflow.subscriptions.renew")).Select(a=>a.Key).ToArray();
        var body=new Academy.Api.Operations.StaffAccessWrite(true,[],actions,WorkflowActions:allowed);
        (await Write(owner,$"/api/v1/manage/staff/{fixture.Member}/access",body)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden,(await Write(admin,"/api/v1/people/registrations",new{},false)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Write(admin,"/api/v1/subscriptions/admin-renewals",new{},false)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Write(admin,$"/api/v1/attendance/sessions/{Guid.NewGuid()}/players",new{})).StatusCode);
        var own=await admin.GetFromJsonAsync<JsonElement>("/api/v1/my-access");var daily=own.GetProperty("workflowActions").EnumerateArray().ToDictionary(a=>a.GetProperty("key").GetString()!,a=>a.GetProperty("allowed").GetBoolean());
        Assert.True(daily["workflow.attendance.staff"]);Assert.False(daily["workflow.attendance.players"]);Assert.True(daily["workflow.players.edit"]);Assert.False(daily["workflow.players.create"]);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var member=await db.AcademyMemberships.SingleAsync(m=>m.Id==fixture.Member);
        db.BindRequestScope(DemoSeed.NogoomAcademyId,member.UserId,fixture.Member,null,module:"attendance",operation:"Edit",workflowRequirements:StaffWorkflowAccess.Requirements("/api/v1/attendance/sessions/unused/players","PUT"));
        db.Branches.Add(new Branch{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,ArabicName="فرع غير محفوظ لاختبار منع السحب",CreatedAtUtc=DateTimeOffset.UtcNow,UpdatedAtUtc=DateTimeOffset.UtcNow});
        await Assert.ThrowsAsync<ResourceScopeException>(()=>db.SaveChangesAsync());
        var review=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff/{fixture.Member}/access");Assert.NotEmpty(review.GetProperty("history").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound,(await Write(owner,$"/api/v1/manage/staff/{DemoSeed.NogoomAcademyId}/access",body)).StatusCode);
    }
}
