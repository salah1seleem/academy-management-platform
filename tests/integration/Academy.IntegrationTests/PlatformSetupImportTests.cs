using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Importing;
using Academy.Api.Platform;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed partial class PlatformSetupImportTests : IAsyncLifetime
{
    private Factory factory = null!;
    public Task InitializeAsync() { factory = new(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Separate test database required")); _ = factory.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await factory.DisposeAsync();
    private static async Task<HttpResponseMessage> Send(HttpClient client,string path,object body,bool put=false,bool platform=true)
    {
        var csrf=await client.GetFromJsonAsync<JsonElement>(platform?"/api/v1/platform/auth/csrf":"/api/v1/auth/csrf");
        var request=new HttpRequestMessage(put?HttpMethod.Put:HttpMethod.Post,path){Content=JsonContent.Create(body)};
        request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString()); return await client.SendAsync(request);
    }
    private async Task<HttpClient> Platform()
    {
        var client=factory.CreateClient(); (await Send(client,"/api/v1/platform/auth/login",new {email=PlatformIdentity.DemoEmail,password="Demo-Only-123!",code="246810"})).EnsureSuccessStatusCode();return client;
    }
    private static async Task<Guid> Create(HttpClient client,string phone)
    {
        var r=await Send(client,"/api/v1/platform/academies",new PlatformAcademyWrite(Guid.NewGuid(),"setup-"+Guid.NewGuid().ToString("N")[..12],"أكاديمية تجربة خيالية","EGP","Africa/Cairo","مالك اختبار خيالي",Guid.NewGuid()+"@example.test","Demo-Only-123!","multi-sport",phone));r.EnsureSuccessStatusCode();return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private static async Task<ImportPreviewResult> Preview(HttpClient client,Guid id,byte[] bytes)
    {
        var csrf=await client.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf");using var form=new MultipartFormDataContent();form.Add(new ByteArrayContent(bytes),"file","fictional.xlsx");var request=new HttpRequestMessage(HttpMethod.Post,$"/api/v1/platform/academies/{id}/import/preview"){Content=form};request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());var r=await client.SendAsync(request);r.EnsureSuccessStatusCode();return (await r.Content.ReadFromJsonAsync<ImportPreviewResult>())!;
    }
    private static async Task<HttpResponseMessage> Stage(HttpClient client,Guid id,byte[] bytes,ImportPreviewResult preview,bool platform=true)
    {
        var csrf=await client.GetFromJsonAsync<JsonElement>(platform?"/api/v1/platform/auth/csrf":"/api/v1/auth/csrf");using var form=new MultipartFormDataContent();form.Add(new ByteArrayContent(bytes),"file","fictional.xlsx");
        foreach(var pair in new Dictionary<string,string>{{"ExpectedFileHash",preview.FileHash},{"ExpectedReferenceStamp",preview.ReferenceStamp},{"GuardianLinksApproved","true"},{"OpeningBalancesApproved","true"},{"AcceptFinancialReview","true"},{"GuardianResolutions","[]"}})form.Add(new StringContent(pair.Value),pair.Key);
        var request=new HttpRequestMessage(HttpMethod.Post,platform?$"/api/v1/platform/academies/{id}/import/commit":"/api/v1/people/import/commit"){Content=form};request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());request.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());return await client.SendAsync(request);
    }
    [Fact]
    public async Task Draft_import_is_platform_attributed_atomic_and_owner_reupload_reuses_the_same_job()
    {
        using var platform=await Platform();var phone="01091"+Random.Shared.Next(100000,999999);var id=await Create(platform,phone);var path=$"/api/v1/platform/academies/{id}";
        var licenses=await platform.GetFromJsonAsync<JsonElement>(path+"/entitlements");var features=licenses.EnumerateArray().ToDictionary(x=>x.GetProperty("feature").GetString()!,x=>x.GetProperty("feature").GetString() is "training" or "matches");
        (await Send(platform,path+"/entitlements",new {planCode="Trial",features},true)).EnsureSuccessStatusCode();
        var bytes=ImportXlsx.Write(ConnectedImportSchema.Template(id,"EGP",new DateOnly(2026,10,25),true,10,"platform-setup-"+id.ToString("N"),Random.Shared.Next(1_000_000,2_000_000)));
        var preview=await Preview(platform,id,bytes);Assert.True(preview.CanApprove,string.Join(";",preview.Errors.Select(x=>x.Message)));Assert.Equal(10,preview.PlayerCount);Assert.Equal(11,preview.RowCount);
        var stage=await Stage(platform,id,bytes,preview);Assert.Equal(HttpStatusCode.Accepted,stage.StatusCode);var jobId=(await stage.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using(var scope=factory.Services.CreateAsyncScope()){
            var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var job=await db.AcademyImportJobs.SingleAsync(j=>j.Id==jobId);Assert.True(job.PlatformInitiated);Assert.Null(job.MembershipId);Assert.Equal(PlatformIdentity.DemoUserId,job.ActorUserId);Assert.False(await db.AcademyMemberships.AnyAsync(m=>m.UserId==PlatformIdentity.DemoUserId));Assert.False(await db.Players.AnyAsync(p=>p.AcademyId==id));job.State=ImportJobState.Running;await db.SaveChangesAsync();}
        await using(var scope=factory.Services.CreateAsyncScope())await scope.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(jobId);
        await using(var scope=factory.Services.CreateAsyncScope()){
            var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(10,await db.Players.CountAsync(p=>p.AcademyId==id));Assert.Equal(11,await db.SportEnrollments.CountAsync(p=>p.AcademyId==id));Assert.False(await db.Academies.Where(a=>a.Id==id).Select(a=>a.IsActive).SingleAsync());Assert.False(await db.Collections.AnyAsync(c=>c.AcademyId==id));Assert.False(await db.Receipts.AnyAsync(c=>c.AcademyId==id));Assert.True(await db.PlatformAudits.AnyAsync(x=>x.TargetAcademyId==id&&x.Action=="academy.import.complete"));}
        var detail=await platform.GetFromJsonAsync<JsonElement>(path+"/setup");Assert.NotEmpty(detail.GetProperty("readiness").EnumerateArray());
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(platform,path+"/state",new {target="Active",version=0},true)).StatusCode);
        (await Send(platform,path+"/setup",new PlatformSetup.SetupWrite(0,"أكاديمية تجربة خيالية","القاهرة — موقع خيالي",phone,phone,"Africa/Cairo","Trial",1000,"EGP","Monthly",new DateOnly(2026,10,25),new DateOnly(2026,11,25)),true)).EnsureSuccessStatusCode();
        (await Send(platform,path+"/configuration",new {features},true)).EnsureSuccessStatusCode();
        var logo=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");
        var csrf=await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf");using(var form=new MultipartFormDataContent()){var image=new ByteArrayContent(logo);image.Headers.ContentType=new("image/png");form.Add(image,"file","logo.png");using var req=new HttpRequestMessage(HttpMethod.Post,path+"/logo"){Content=form};req.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());(await platform.SendAsync(req)).EnsureSuccessStatusCode();}
        detail=await platform.GetFromJsonAsync<JsonElement>(path+"/setup");Assert.Empty(detail.GetProperty("readiness").EnumerateArray());var version=detail.GetProperty("profile").GetProperty("editVersion").GetInt32();
        (await Send(platform,path+"/state",new {target="Active",version},true)).EnsureSuccessStatusCode();
        using var owner=factory.CreateClient();string email;
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();email=(await db.AcademyMemberships.Where(m=>m.AcademyId==id&&m.Role==AcademyRole.AcademyOwner).Select(m=>m.User.Email).SingleAsync())!;}
        (await Send(owner,"/api/v1/auth/login",new {email,password="Demo-Only-123!"},platform:false)).EnsureSuccessStatusCode();
        var replay=await Stage(owner,id,bytes,preview,false);replay.EnsureSuccessStatusCode();var result=await replay.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(jobId,result.GetProperty("id").GetGuid());Assert.True(result.GetProperty("replayedRequest").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized,(await owner.GetAsync(path+"/setup")).StatusCode);
        var listing=await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/academies/directory?search="+Uri.EscapeDataString("أكاديمية تجربة خيالية")+"&sort=players&direction=desc");Assert.Contains(listing.GetProperty("items").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==id&&x.GetProperty("players").GetInt32()==10);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await platform.GetAsync("/api/v1/platform/academies/directory?sort=fee")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await platform.GetAsync("/api/v1/platform/academies/directory?sort=fee&currency=EGP&period=Monthly")).StatusCode);
    }
    [Fact]
    public async Task Platform_revocation_before_worker_publication_rejects_and_other_academy_file_is_never_accepted()
    {
        using var platform=await Platform();var id=await Create(platform,"01092"+Random.Shared.Next(100000,999999));var other=await Create(platform,"01093"+Random.Shared.Next(100000,999999));var path=$"/api/v1/platform/academies/{id}";
        var all=await platform.GetFromJsonAsync<JsonElement>(path+"/entitlements");var features=all.EnumerateArray().ToDictionary(x=>x.GetProperty("feature").GetString()!,x=>x.GetProperty("feature").GetString()=="training");
        (await Send(platform,path+"/entitlements",new {planCode="Trial",features},true)).EnsureSuccessStatusCode();
        var bytes=ImportXlsx.Write(ConnectedImportSchema.Template(id,"EGP",new DateOnly(2026,10,25),true,2,"revoked-"+id.ToString("N"),Random.Shared.Next(3_000_000,4_000_000)));
        var preview=await Preview(platform,id,bytes);Assert.True(preview.CanApprove,string.Join(";",preview.Errors.Select(x=>x.Message)));var r=await Stage(platform,id,bytes,preview);r.EnsureSuccessStatusCode();var job=(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        // Even with its own licence, a second academy cannot borrow workbook context or reference IDs.
        (await Send(platform,$"/api/v1/platform/academies/{other}/entitlements",new {planCode="Trial",features},true)).EnsureSuccessStatusCode();
        var wrongTenant = await Preview(platform,other,bytes); Assert.False(wrongTenant.CanApprove); Assert.Contains(wrongTenant.Errors,x=>x.Message.Contains("الأكاديمية"));
        var sampleA = ImportXlsx.Read(await platform.GetByteArrayAsync(path+"/import/connected-sample"));
        var sampleB = ImportXlsx.Read(await platform.GetByteArrayAsync($"/api/v1/platform/academies/{other}/import/connected-sample"));
        Assert.NotEqual(sampleA.Sheet("المدربون").Rows[1][2].Value,sampleB.Sheet("المدربون").Rows[1][2].Value);
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var j=await db.AcademyImportJobs.SingleAsync(j=>j.Id==job);j.State=ImportJobState.Running;var grant=await db.PlatformAdministrators.SingleAsync(x=>x.UserId==PlatformIdentity.DemoUserId);grant.IsActive=false;await db.SaveChangesAsync();}
        try {await using var scope=factory.Services.CreateAsyncScope();var error=await Assert.ThrowsAsync<ImportRejected>(()=>scope.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(job));Assert.Equal("authority",error.Code);var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.False(await db.Players.IgnoreQueryFilters().AnyAsync(x=>x.AcademyId==id));Assert.Equal(HttpStatusCode.Forbidden,(await platform.GetAsync(path+"/setup")).StatusCode);}
        finally{await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var grant=await db.PlatformAdministrators.SingleAsync(x=>x.UserId==PlatformIdentity.DemoUserId);grant.IsActive=true;var j=await db.AcademyImportJobs.SingleAsync(j=>j.Id==job);j.State=ImportJobState.Failed;j.StagedFile=null;j.ErrorCode="authority";await db.SaveChangesAsync();}
    }
    private sealed class Factory(string connection):WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b){b.UseEnvironment("Demo");b.UseSetting("ConnectionStrings:Default",connection);b.UseSetting("Demo:SeedProfile","LegacyRegression");b.UseSetting("Demo:SeedEnabled","true");b.UseSetting("Demo:StaffPassword","Demo-Only-123!");b.UseSetting("Demo:FixedOtpEnabled","true");b.UseSetting("Demo:FixedOtp","246810");b.UseSetting("Demo:ReferenceDate","2026-10-25");b.UseSetting("Imports:WorkerEnabled","false");}
    }
}
