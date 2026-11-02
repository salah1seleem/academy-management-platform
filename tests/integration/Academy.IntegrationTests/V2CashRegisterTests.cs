using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Finance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;

public sealed partial class V2ReceivablesTests
{
    private const string CashRoot = "/api/v1/finance/cash-registers";
    private async Task<(Guid Register, Guid Member, Guid Branch)> CashRegisterFixture(HttpClient owner)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var branch = await db.Branches.Where(b=>b.AcademyId==DemoSeed.NogoomAcademyId&&b.IsActive).Select(b=>b.Id).FirstAsync();
        var member = await db.AcademyMemberships.Where(m=>m.AcademyId==DemoSeed.NogoomAcademyId&&m.User.Email==DemoSeed.OwnerEmail).Select(m=>m.Id).SingleAsync();
        var response=await Post(owner,CashRoot,new{branchId=branch,name="صندوق اختبار "+Guid.NewGuid().ToString("N")[..10]}); response.EnsureSuccessStatusCode();
        return ((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(),member,branch);
    }
    private static async Task<HttpResponseMessage> CashAssign(HttpClient owner,Guid register,Guid member,bool enabled=true)
    {
        var csrf=await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");var request=new HttpRequestMessage(HttpMethod.Put,$"{CashRoot}/{register}/cashiers"){Content=JsonContent.Create(new{membershipId=member,enabled})};request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());return await owner.SendAsync(request);
    }
    private async Task<Guid> CashOpen(HttpClient owner,Guid register,string opening)
    {
        var response=await Post(owner,$"{CashRoot}/{register}/open",new{openingAmount=opening});response.EnsureSuccessStatusCode();return(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private Task<HttpResponseMessage> CashTransfer(HttpClient owner,Guid from,Guid to,string amount,string? key=null)=>Post(owner,$"{CashRoot}/transfers",new{fromSessionId=from,toSessionId=to,amount,reason=(string?)null},key);
    private async Task<AcademyRuntimeSettings> CashSettings()
    {
        await using var s=factory.Services.CreateAsyncScope();return AcademyRuntimeSettings.Read((await s.ServiceProvider.GetRequiredService<FoundationDbContext>().AcademyConfigurationVersions.SingleAsync(v=>v.AcademyId==DemoSeed.NogoomAcademyId&&v.State==ConfigurationState.Active)).Payload);
    }
    private async Task<Guid> CashSetConfiguration(HttpClient owner,AcademyRuntimeSettings settings)
    {
        var draft=await Post(owner,"/api/v1/manage/configuration/drafts",settings);draft.EnsureSuccessStatusCode();var id=(await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        foreach(var target in new[]{ConfigurationState.Validated,ConfigurationState.Staged,ConfigurationState.Active})
        {
            await using var s=factory.Services.CreateAsyncScope();var version=await s.ServiceProvider.GetRequiredService<FoundationDbContext>().AcademyConfigurationVersions.Where(v=>v.Id==id).Select(v=>v.Version).SingleAsync();
            (await Post(owner,$"/api/v1/manage/configuration/{id}/transition",new{target=target.ToString(),version})).EnsureSuccessStatusCode();
        }
        return id;
    }
    [Fact]
    public async Task Cash_requires_explicit_cashier_and_open_is_exactly_once_without_revenue()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRegisterFixture(owner);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post(owner,$"{CashRoot}/{f.Register}/open",new{openingAmount="100.00"})).StatusCode);
        (await CashAssign(owner,f.Register,f.Member)).EnsureSuccessStatusCode();
        var key=Guid.NewGuid().ToString();var responses=await Task.WhenAll(Post(owner,$"{CashRoot}/{f.Register}/open",new{openingAmount="100.00"},key),Post(owner,$"{CashRoot}/{f.Register}/open",new{openingAmount="100"},key));
        Assert.All(responses,r=>r.EnsureSuccessStatusCode());var id=(await responses[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();Assert.Equal(id,(await responses[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,$"{CashRoot}/{f.Register}/open",new{openingAmount="101.00"},key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,$"{CashRoot}/{f.Register}/open",new{openingAmount="100.00"})).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Single(await db.CashSessions.Where(s=>s.RegisterId==f.Register).ToListAsync());Assert.False(await db.CashMovements.AnyAsync(m=>m.SessionId==id));
        // The register list is bounded; accumulated fixtures need not be on its first page.
        var read=await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{id}");Assert.Equal(f.Register,read.GetProperty("session").GetProperty("registerId").GetGuid());Assert.Equal("100.00",read.GetProperty("balance").GetString());
    }
    [Fact]
    public async Task Cash_transfers_conserve_funds_replay_and_reject_excess_or_invalid_money()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var a=await CashRegisterFixture(owner);var b=await CashRegisterFixture(owner);(await CashAssign(owner,a.Register,a.Member)).EnsureSuccessStatusCode();(await CashAssign(owner,b.Register,b.Member)).EnsureSuccessStatusCode();var from=await CashOpen(owner,a.Register,"200.00");var to=await CashOpen(owner,b.Register,"50.00");
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var collections=await db.Collections.CountAsync();var receipts=await db.Receipts.CountAsync();var key=Guid.NewGuid().ToString();
        (await CashTransfer(owner,from,to,"100.00",key)).EnsureSuccessStatusCode();(await CashTransfer(owner,from,to,"100",key)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await CashTransfer(owner,from,to,"101",key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await CashTransfer(owner,from,to,"100.01")).StatusCode);
        foreach(var amount in new[]{"0","-1",".001","1000000000"})Assert.Equal(HttpStatusCode.UnprocessableEntity,(await CashTransfer(owner,from,to,amount)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await CashTransfer(owner,from,from,"1")).StatusCode);
        Assert.Equal("100.00",(await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{from}")).GetProperty("balance").GetString());Assert.Equal("150.00",(await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{to}")).GetProperty("balance").GetString());
        var moves=await db.CashMovements.Where(m=>m.SessionId==from||m.SessionId==to).ToListAsync();Assert.Equal(2,moves.Count);Assert.Equal(0,moves.Sum(m=>m.Amount));Assert.Equal(collections,await db.Collections.CountAsync());Assert.Equal(receipts,await db.Receipts.CountAsync());
        var failure=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CashMovements\" SET \"Amount\"=1 WHERE \"SessionId\"={to}"));Assert.Equal("23514",failure.SqlState);
        failure=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"CashTransfers\" WHERE \"FromSessionId\"={from}"));Assert.Equal("23514",failure.SqlState);
    }
    [Fact]
    public async Task Cash_concurrent_transfers_cannot_overdraw_and_close_serializes_with_transfer()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var a=await CashRegisterFixture(owner);var b=await CashRegisterFixture(owner);(await CashAssign(owner,a.Register,a.Member)).EnsureSuccessStatusCode();(await CashAssign(owner,b.Register,b.Member)).EnsureSuccessStatusCode();var from=await CashOpen(owner,a.Register,"100");var to=await CashOpen(owner,b.Register,"0");
        var writes=await Task.WhenAll(CashTransfer(owner,from,to,"80"),CashTransfer(owner,from,to,"80"));Assert.Single(writes,r=>r.IsSuccessStatusCode);Assert.Single(writes,r=>r.StatusCode==HttpStatusCode.Conflict);
        var race=await Task.WhenAll(Post(owner,$"{CashRoot}/sessions/{from}/close",new{countedAmount="20",reason=(string?)null}),CashTransfer(owner,from,to,"10"));race[0].EnsureSuccessStatusCode();Assert.True(race[1].IsSuccessStatusCode||race[1].StatusCode==HttpStatusCode.Conflict);
        var detail=await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{from}");Assert.Equal(race[1].IsSuccessStatusCode?"10":"20",detail.GetProperty("session").GetProperty("expectedAmount").GetString()!.Split('.')[0]);Assert.Equal(race[1].IsSuccessStatusCode?"10.00":"0.00",detail.GetProperty("variance").GetString());Assert.Equal(HttpStatusCode.Conflict,(await CashTransfer(owner,from,to,"1")).StatusCode);
    }
    [Fact]
    public async Task Cash_close_variance_is_not_income_and_posted_records_are_immutable()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var a=await CashRegisterFixture(owner);(await CashAssign(owner,a.Register,a.Member)).EnsureSuccessStatusCode();var id=await CashOpen(owner,a.Register,"100");var key=Guid.NewGuid().ToString();
        var closed=await Post(owner,$"{CashRoot}/sessions/{id}/close",new{countedAmount="90",reason="  "},key);closed.EnsureSuccessStatusCode();Assert.Equal("-10.00",(await closed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("variance").GetString());(await Post(owner,$"{CashRoot}/sessions/{id}/close",new{countedAmount="90.00",reason=(string?)null},key)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,$"{CashRoot}/sessions/{id}/close",new{countedAmount="91",reason=(string?)null},key)).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.False(await db.CashMovements.AnyAsync(m=>m.SessionId==id));var row=await db.CashSessions.SingleAsync(s=>s.Id==id);row.CountedAmount=99;await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.Entry(row).State=EntityState.Unchanged;
        var failure=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CashSessions\" SET \"CountedAmount\"=99 WHERE \"Id\"={id}"));Assert.Equal("23514",failure.SqlState);
        failure=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"CashSessions\" WHERE \"Id\"={id}"));Assert.Equal("23514",failure.SqlState);
        var read=await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{id}");Assert.Equal(id,read.GetProperty("session").GetProperty("id").GetGuid());Assert.Equal(90m,decimal.Parse(read.GetProperty("session").GetProperty("countedAmount").GetString()!,System.Globalization.CultureInfo.InvariantCulture));Assert.Equal("-10.00",read.GetProperty("variance").GetString());
    }
    [Fact]
    public async Task Cash_disabled_blocks_open_and_transfer_but_owner_can_reconcile_existing_shift()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var settings=await CashSettings();var a=await CashRegisterFixture(owner);var b=await CashRegisterFixture(owner);(await CashAssign(owner,a.Register,a.Member)).EnsureSuccessStatusCode();(await CashAssign(owner,b.Register,b.Member)).EnsureSuccessStatusCode();var from=await CashOpen(owner,a.Register,"50");var to=await CashOpen(owner,b.Register,"0");
        try
        {
            await CashSetConfiguration(owner,settings with{CashEnabled=false});Assert.Equal(HttpStatusCode.Conflict,(await CashTransfer(owner,from,to,"1")).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,$"{CashRoot}/{a.Register}/open",new{openingAmount="0"})).StatusCode);
            (await CashAssign(owner,a.Register,a.Member,false)).EnsureSuccessStatusCode();(await Post(owner,$"{CashRoot}/sessions/{from}/close",new{countedAmount="50",reason=(string?)null})).EnsureSuccessStatusCode();
        }
        finally{await CashSetConfiguration(owner,settings);}
    }
    [Fact]
    public async Task Cash_tenant_role_csrf_and_composite_relationship_isolation()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);using var other=await Staff(DemoSeed.FutureOwnerEmail);using var guardian=await Guardian();using var admin=await Staff(DemoSeed.AdminEmail);var a=await CashRegisterFixture(owner);(await CashAssign(owner,a.Register,a.Member)).EnsureSuccessStatusCode();var id=await CashOpen(owner,a.Register,"0");
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"{CashRoot}/sessions/{id}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await Post(other,$"{CashRoot}/{a.Register}/open",new{openingAmount="0"})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await guardian.GetAsync(CashRoot)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync(CashRoot)).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync($"{CashRoot}/{a.Register}/open",new{openingAmount="0"})).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var branch=await db.Branches.Where(b=>b.AcademyId==DemoSeed.FutureAcademyId).Select(b=>b.Id).FirstAsync();
        var error=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CashierAssignments\" SET \"BranchId\"={branch} WHERE \"RegisterId\"={a.Register}"));Assert.Equal("23503",error.SqlState);
    }
    [Fact]
    public async Task Cash_parallel_distinct_open_requests_have_only_one_winner()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var a=await CashRegisterFixture(owner);(await CashAssign(owner,a.Register,a.Member)).EnsureSuccessStatusCode();var results=await Task.WhenAll(Post(owner,$"{CashRoot}/{a.Register}/open",new{openingAmount="0"}),Post(owner,$"{CashRoot}/{a.Register}/open",new{openingAmount="0"}));Assert.Single(results,r=>r.IsSuccessStatusCode);Assert.Single(results,r=>r.StatusCode==HttpStatusCode.Conflict);
    }
    [Fact]
    public async Task Cash_inactive_branch_blocks_open_without_changing_the_register()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var a=await CashRegisterFixture(owner);(await CashAssign(owner,a.Register,a.Member)).EnsureSuccessStatusCode();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var branch=await db.Branches.SingleAsync(b=>b.Id==a.Branch);
        try{branch.IsActive=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,$"{CashRoot}/{a.Register}/open",new{openingAmount="0"})).StatusCode);Assert.False(await db.CashSessions.AnyAsync(s=>s.RegisterId==a.Register));}
        finally{branch.IsActive=true;await db.SaveChangesAsync();}
    }
    [Fact]
    public async Task Cash_demo_seed_is_repeatable_retains_reconciliation_and_never_collects()
    {
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var count=await db.Collections.CountAsync();var receipts=await db.Receipts.CountAsync();await CashRegisterDemoSeed.Seed(db);await CashRegisterDemoSeed.Seed(db);
        var session=await db.CashSessions.SingleAsync(s=>s.Id==CashRegisterDemoSeed.SessionId);Assert.Equal(200,session.ExpectedAmount);Assert.Equal(190,session.CountedAmount);Assert.Equal(count,await db.Collections.CountAsync());Assert.Equal(receipts,await db.Receipts.CountAsync());Assert.False(await db.CashMovements.AnyAsync(m=>m.SessionId==session.Id));
    }
    [Fact]
    public async Task Cash_branch_admin_requires_finance_add_scope_and_current_cashier_grant()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var a=await CashRegisterFixture(owner);var email=$"cash.{Guid.NewGuid():N}@example.test";var created=await Post(owner,"/api/v1/manage/staff",new{name="أمين اصطناعي",email,phone="010"+Random.Shared.Next(10000000,99999999),temporaryPassword="Demo-Only-123!",role="AcademyAdmin",jobTitle="اختبار"});created.EnsureSuccessStatusCode();var member=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        async Task Access(bool add)
        {
            var options=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff/{member}/access");var actions=options.GetProperty("modules").EnumerateArray().Select(x=>new{module=x.GetString(),read=x.GetString() is "finance" or "settings",add=add&&x.GetString() is "finance" or "settings",edit=x.GetString()=="settings",delete=false}).ToArray();
            var csrf=await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");var r=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/manage/staff/{member}/access"){Content=JsonContent.Create(new{allBranches=false,branches=new[]{a.Branch},actions})};r.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());(await owner.SendAsync(r)).EnsureSuccessStatusCode();
        }
        await Access(false);using var admin=await Staff(email);Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync(CashRoot)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await Post(admin,$"{CashRoot}/{a.Register}/open",new{openingAmount="0"})).StatusCode);
        await Access(true);Assert.Equal(HttpStatusCode.Forbidden,(await Post(admin,$"{CashRoot}/{a.Register}/open",new{openingAmount="0"})).StatusCode);(await CashAssign(owner,a.Register,member)).EnsureSuccessStatusCode();var id=await CashOpen(admin,a.Register,"0");
        Assert.Equal(HttpStatusCode.Forbidden,(await Post(admin,CashRoot,new{branchId=a.Branch,name="غير مخول"})).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await CashTransfer(admin,id,Guid.NewGuid(),"1")).StatusCode);
        var list=await admin.GetFromJsonAsync<JsonElement>(CashRoot);Assert.All(list.GetProperty("items").EnumerateArray(),r=>Assert.Equal(a.Branch,r.GetProperty("branchId").GetGuid()));
        await using(var scope=factory.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var anotherBranch=await db.Branches.Where(b=>b.AcademyId==DemoSeed.NogoomAcademyId&&b.Id!=a.Branch&&b.IsActive).Select(b=>b.Id).FirstAsync();
            var other=await Post(owner,CashRoot,new{branchId=anotherBranch,name="صندوق خارج نطاق الأمين "+Guid.NewGuid().ToString("N")[..10]});other.EnsureSuccessStatusCode();var otherId=(await other.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.NotFound,(await Post(admin,$"{CashRoot}/{otherId}/open",new{openingAmount="0"})).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await CashAssign(owner,otherId,member)).StatusCode);
        }
        var settings=await CashSettings();Assert.Equal(HttpStatusCode.Forbidden,(await Post(admin,"/api/v1/manage/configuration/drafts",settings with{CashEnabled=!settings.CashEnabled})).StatusCode);
        // Even an already validated owner draft cannot be activated or suspended
        // by a delegated settings admin to bypass owner-only cash policy.
        var draft=await Post(owner,"/api/v1/manage/configuration/drafts",settings with{CashEnabled=!settings.CashEnabled});draft.EnsureSuccessStatusCode();var draftId=(await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        foreach(var target in new[]{"Validated","Staged","Active"})
        {
            await using var s=factory.Services.CreateAsyncScope();var version=await s.ServiceProvider.GetRequiredService<FoundationDbContext>().AcademyConfigurationVersions.Where(v=>v.Id==draftId).Select(v=>v.Version).SingleAsync();
            var transition=await Post(target=="Active"?admin:owner,$"/api/v1/manage/configuration/{draftId}/transition",new{target,version});if(target=="Active")Assert.Equal(HttpStatusCode.Forbidden,transition.StatusCode);else transition.EnsureSuccessStatusCode();
        }
        (await CashAssign(owner,a.Register,member,false)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await Post(admin,$"{CashRoot}/sessions/{id}/close",new{countedAmount="0",reason=(string?)null})).StatusCode);(await Post(owner,$"{CashRoot}/sessions/{id}/close",new{countedAmount="0",reason=(string?)null})).EnsureSuccessStatusCode();
    }
}
