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
    [Fact] public async Task Approved_subscription_period_read_alone_never_exposes_financial_snapshot_or_receipt()
    {
        var f=await CreateAdmin();using var owner=f.Owner;using var admin=f.Admin;
        var actions=StaffAccessRules.Modules.Select(m=>new Academy.Api.Operations.StaffActionValue(m,m is "players" or "subscriptions",false,false,false)).ToArray();
        var access=new Academy.Api.Operations.StaffAccessWrite(true,[],actions,AllSports:true,Sports:[],WorkflowActions:["workflow.players.read"]);
        (await Write(owner,$"/api/v1/manage/staff/{f.Member}/access",access)).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var id=await db.SubscriptionPeriods.Where(p=>p.AcademyId==DemoSeed.NogoomAcademyId&&p.CollectionId!=null).Select(p=>p.Id).FirstAsync();
        var hidden=await admin.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/periods/{id}");
        Assert.False(hidden.GetProperty("financialValuesVisible").GetBoolean());
        foreach(var field in new[]{"priceSnapshot","currencySnapshot","receiptId","pricing"})Assert.Equal(JsonValueKind.Null,hidden.GetProperty(field).ValueKind);
        Assert.NotNull(hidden.GetProperty("startDate").GetString());Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("/api/v1/reports/financial")).StatusCode);
        actions=StaffAccessRules.Modules.Select(m=>new Academy.Api.Operations.StaffActionValue(m,m is "players" or "subscriptions" or "payments",false,false,false)).ToArray();
        (await Write(owner,$"/api/v1/manage/staff/{f.Member}/access",access with{Actions=actions,WorkflowActions=["workflow.players.read","workflow.payments.read"]})).EnsureSuccessStatusCode();
        var visible=await admin.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/periods/{id}");
        Assert.True(visible.GetProperty("financialValuesVisible").GetBoolean());Assert.Equal(JsonValueKind.Number,visible.GetProperty("priceSnapshot").ValueKind);Assert.NotEqual(Guid.Empty,visible.GetProperty("receiptId").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("/api/v1/reports/financial")).StatusCode);
    }
    [Fact] public async Task V4_admin_can_record_scoped_expense_without_financial_report_or_approval_permission()
    {
        var f=await CreateAdmin();using var owner=f.Owner;using var admin=f.Admin;var data=await Data();
        var grants=StaffAccessRules.Modules.Select(m=>new Academy.Api.Operations.StaffActionValue(m,m is "finance" or "academy",m=="finance",m=="finance",false)).ToArray();
        (await Write(owner,$"/api/v1/manage/staff/{f.Member}/access",new Academy.Api.Operations.StaffAccessWrite(false,[data.A],grants,WorkflowActions:["workflow.expenses.record"])) ).EnsureSuccessStatusCode();
        var body=new Academy.Api.Finance.OperatingExpenseWrite(data.A,Academy.Infrastructure.Finance.OperatingExpenseScope.Branch,data.A,"Supplies",new(2026,10,1),new(2026,10,19),new(2026,10,19),100,"EGP","مصروف اصطناعي مخول");
        var csrf=await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");using var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/finance/operating-expenses/"){Content=JsonContent.Create(body)};request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());request.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());var created=await admin.SendAsync(request);created.EnsureSuccessStatusCode();var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("/api/v1/reports/financial")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Write(admin,$"/api/v1/finance/operating-expenses/{id}/approve",new{},false)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Write(admin,$"/api/v1/finance/operating-expenses/{id}/post",new{quoteHash=new string('a',64)},false)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/api/v1/finance/operating-expenses/{Guid.NewGuid()}")).StatusCode);
        Assert.False((await admin.GetFromJsonAsync<JsonElement>("/api/v1/finance/operating-expenses/options")).GetProperty("canApprove").GetBoolean());
        Assert.True((await admin.GetFromJsonAsync<JsonElement>("/api/v1/finance/operating-expenses/options")).GetProperty("canRecord").GetBoolean());
    }
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

public sealed partial class V2StaffScopeTests
{
    [Fact]
    public async Task Approved_people_read_without_financial_grant_redacts_values_and_rejects_financial_sort()
    {
        var fixture=await CreateAdmin();using var owner=fixture.Owner;using var admin=fixture.Admin;
        var actions=StaffAccessRules.Modules.Select(m=>new Academy.Api.Operations.StaffActionValue(m,m is "players" or "attendance",false,m=="attendance",false)).ToArray();
        (await Write(owner,$"/api/v1/manage/staff/{fixture.Member}/access",new Academy.Api.Operations.StaffAccessWrite(true,[],actions,AllSports:true,Sports:[],WorkflowActions:["workflow.players.read","workflow.attendance.players"]))).EnsureSuccessStatusCode();
        foreach(var path in new[]{"players","guardians"}){
            var data=await admin.GetFromJsonAsync<JsonElement>($"/api/v1/people/{path}?page=1&pageSize=25");Assert.False(data.GetProperty("financialValuesVisible").GetBoolean());Assert.NotEmpty(data.GetProperty("items").EnumerateArray());
            Assert.All(data.GetProperty("items").EnumerateArray(),r=>{Assert.Equal(JsonValueKind.Null,r.GetProperty("monthlySubscriptionValue").ValueKind);Assert.Equal(JsonValueKind.Null,r.GetProperty("unpricedSubscriptions").ValueKind);});
            Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync($"/api/v1/people/{path}?page=1&sort=value_desc")).StatusCode);
            var ownerData=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/{path}?page=1&pageSize=25&sort=value_desc");Assert.True(ownerData.GetProperty("financialValuesVisible").GetBoolean());Assert.All(ownerData.GetProperty("items").EnumerateArray(),r=>Assert.Equal(JsonValueKind.Number,r.GetProperty("monthlySubscriptionValue").ValueKind));
        }
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("/api/v1/reports/financial")).StatusCode);
    }
    [Fact]
    public async Task Approved_period_transfer_and_compensation_actions_require_their_named_grants()
    {
        var fixture=await CreateAdmin();using var owner=fixture.Owner;using var admin=fixture.Admin;
        var actions=StaffAccessRules.Modules.Select(m=>new Academy.Api.Operations.StaffActionValue(m,true,m!="followups",true,false)).ToArray();
        (await Write(owner,$"/api/v1/manage/staff/{fixture.Member}/access",new Academy.Api.Operations.StaffAccessWrite(true,[],actions,AllSports:true,Sports:[],WorkflowActions:["workflow.players.read","workflow.players.edit"]))).EnsureSuccessStatusCode();
        foreach(var leaf in new[]{"workflow.subscriptions.cancel","workflow.subscriptions.days","workflow.subscriptions.pause","workflow.players.session-transfer","workflow.players.compensation","workflow.attendance.review"}){
            var path=leaf switch{
                "workflow.subscriptions.cancel"=>$"/api/v1/subscriptions/periods/{Guid.NewGuid()}/cancel",
                "workflow.subscriptions.days"=>$"/api/v1/subscriptions/periods/{Guid.NewGuid()}/days",
                "workflow.subscriptions.pause"=>$"/api/v1/subscriptions/periods/{Guid.NewGuid()}/freeze",
                "workflow.players.session-transfer"=>$"/api/v1/people/enrollments/{Guid.NewGuid()}/session-transfer/preview",
                "workflow.players.compensation"=>$"/api/v1/people/enrollments/{Guid.NewGuid()}/compensation/preview",
                _=>$"{Academy.Api.Operations.StaffAttendanceExceptions.ManagePath}/{Guid.NewGuid()}/review"};
            Assert.Contains(leaf,StaffWorkflowAccess.Required(path,"POST"));
            Assert.Equal(HttpStatusCode.Forbidden,(await Write(admin,path,new{},false)).StatusCode);
        }

    }
    [Fact]
    public async Task Approved_denied_player_edit_cannot_replace_photo_through_asset_upload()
    {
        var f=await CreateAdmin();using var owner=f.Owner;using var admin=f.Admin;
        var actions=StaffAccessRules.Modules.Select(m=>new Academy.Api.Operations.StaffActionValue(m,true,m!="followups",true,false)).ToArray();
        (await Write(owner,$"/api/v1/manage/staff/{f.Member}/access",new Academy.Api.Operations.StaffAccessWrite(true,[],actions,AllSports:true,Sports:[],WorkflowActions:["workflow.players.read"]))).EnsureSuccessStatusCode();
        var players=await owner.GetFromJsonAsync<JsonElement>("/api/v1/people/players?page=1&pageSize=1");
        var playerId=players.GetProperty("items")[0].GetProperty("id").GetGuid();
        using var form=new MultipartFormDataContent();form.Add(new StringContent("Photo"),"purpose");form.Add(new StringContent(playerId.ToString()),"playerId");
        var image=new ByteArrayContent(new byte[16]);image.Headers.ContentType=new("image/png");form.Add(image,"file","synthetic-denied.png");
        var csrf=await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");using var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/assets/upload"){Content=form};request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.SendAsync(request)).StatusCode);
    }

}
