using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Importing;
using Academy.Api.Platform;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class PlatformRepairTests
{
    private sealed class Clock:TimeProvider {public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
    private sealed class Factory:WebApplicationFactory<Program>
    {
        public Clock Clock {get;}=new();
        protected override void ConfigureWebHost(IWebHostBuilder b){b.UseEnvironment("Demo");b.UseSetting("ConnectionStrings:Default",Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")??throw new InvalidOperationException("Separate test DB required"));b.UseSetting("Demo:SeedEnabled","true");b.UseSetting("Demo:SeedProfile","LegacyRegression");b.UseSetting("Demo:StaffPassword","Demo-Only-123!");b.UseSetting("Demo:FixedOtpEnabled","true");b.UseSetting("Demo:FixedOtp","246810");b.UseSetting("Demo:ReferenceDate","2026-10-28");b.UseSetting("Imports:WorkerEnabled","false");b.UseSetting("Notifications:WorkerEnabled","false");b.ConfigureServices(s=>{s.AddSingleton<TimeProvider>(Clock);s.PostConfigure<CookieAuthenticationOptions>(PlatformIdentity.Scheme,o=>o.TimeProvider=Clock);});}
    }
    private static async Task<HttpResponseMessage> Write(HttpClient c,string path,object body,bool put=false,bool platform=true){var csrf=await c.GetFromJsonAsync<JsonElement>(platform?"/api/v1/platform/auth/csrf":"/api/v1/auth/csrf");var req=new HttpRequestMessage(put?HttpMethod.Put:HttpMethod.Post,path){Content=JsonContent.Create(body)};req.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());return await c.SendAsync(req);}
    private static async Task<HttpClient> Login(Factory f){var c=f.CreateClient();(await Write(c,"/api/v1/platform/auth/login",new{email=PlatformIdentity.DemoEmail,password="Demo-Only-123!",code="246810"})).EnsureSuccessStatusCode();return c;}
    private static async Task<Guid> Create(HttpClient c){var r=await Write(c,"/api/v1/platform/academies",new PlatformAcademyWrite(Guid.NewGuid(),"repair-"+Guid.NewGuid().ToString("N")[..12],"أكاديمية إقفال خيالية","EGP","Africa/Cairo","مالك إقفال خيالي",Guid.NewGuid()+"@example.test","Demo-Only-123!","multi-sport","01082"+Random.Shared.Next(100000,999999)));r.EnsureSuccessStatusCode();return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();}
    private static async Task<HttpResponseMessage> Upload(HttpClient c,string path,byte[] bytes,ImportPreviewResult? p=null){var csrf=await c.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf");var form=new MultipartFormDataContent();form.Add(new ByteArrayContent(bytes),"file","academy.xlsx");if(p!=null)foreach(var pair in new Dictionary<string,string>{{"ExpectedFileHash",p.FileHash},{"ExpectedReferenceStamp",p.ReferenceStamp},{"GuardianLinksApproved","true"},{"OpeningBalancesApproved","false"},{"AcceptFinancialReview","true"},{"HistoricalPaymentsApproved","false"},{"GuardianResolutions","[]"}})form.Add(new StringContent(pair.Value),pair.Key);var req=new HttpRequestMessage(HttpMethod.Post,path){Content=form};req.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());req.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());return await c.SendAsync(req);}
    [Fact]
    public async Task Worker_rejects_unapproved_links_and_identity_changed_after_staging_without_partial_data()
    {
        await using var f=new Factory();using var c=await Login(f);var id=await Create(c);var path=$"/api/v1/platform/academies/{id}";
        (await Write(c,path+"/prepare-operation",new{})).EnsureSuccessStatusCode();
        var bytes=await c.GetByteArrayAsync(path+"/import/connected-sample");
        var preview=await Upload(c,path+"/import/preview",bytes);preview.EnsureSuccessStatusCode();var p=(await preview.Content.ReadFromJsonAsync<ImportPreviewResult>())!;Assert.True(p.CanApprove);
        var staged=await Upload(c,path+"/import/commit",bytes,p);staged.EnsureSuccessStatusCode();var job=(await staged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using(var sc=f.Services.CreateAsyncScope()){
            var db=sc.ServiceProvider.GetRequiredService<FoundationDbContext>();var j=await db.AcademyImportJobs.SingleAsync(x=>x.Id==job);j.GuardianLinksApproved=false;
            var rejected=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.Equal("CK_ImportJob_Approval",Assert.IsType<Npgsql.PostgresException>(rejected.InnerException).ConstraintName);
            db.ChangeTracker.Clear();j=await db.AcademyImportJobs.SingleAsync(x=>x.Id==job);Assert.True(j.GuardianLinksApproved);j.State=ImportJobState.RunningV3;await db.SaveChangesAsync();
        }
        await using(var sc=f.Services.CreateAsyncScope()){
            var db=sc.ServiceProvider.GetRequiredService<FoundationDbContext>();var j=await db.AcademyImportJobs.SingleAsync(x=>x.Id==job);j.GuardianLinksApproved=true;
            var row=ImportXlsx.Read(bytes).Sheet("المدربون").Rows[1];var phone=EgyptPhoneNormalizer.Normalize(row[2].Value)!;
            db.Users.Add(new(){Id=Guid.NewGuid(),UserName=phone,NormalizedUserName=phone.ToUpperInvariant(),PhoneNumber=phone,DisplayName=row[1].Value,IsActive=false,SecurityStamp=Guid.NewGuid().ToString("N")});await db.SaveChangesAsync();
        }
        await using(var sc=f.Services.CreateAsyncScope()){
            await Assert.ThrowsAsync<ImportRejected>(()=>sc.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(job));
            var db=sc.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.False(await db.Players.AnyAsync(x=>x.AcademyId==id));Assert.False(await db.GuardianProfiles.AnyAsync(x=>x.AcademyId==id));Assert.False(await db.TrainingGroups.AnyAsync(x=>x.AcademyId==id));
            var j=await db.AcademyImportJobs.SingleAsync(x=>x.Id==job);j.State=ImportJobState.Failed;j.ErrorCode="identity";await db.SaveChangesAsync();
        }
    }
    [Fact]
    public async Task Draft_swimming_import_prepares_once_reviews_shared_accounts_and_keeps_academies_isolated()
    {
        await using var f=new Factory();using var c=await Login(f);
        var first=await Create(c);var second=await Create(c);var third=await Create(c);
        string PathFor(Guid id)=>$"/api/v1/platform/academies/{id}";
        var bytes=await c.GetByteArrayAsync(PathFor(first)+"/import/connected-sample");
        (await Write(c,PathFor(first)+"/prepare-operation",new{})).EnsureSuccessStatusCode();
        var importWindow=System.Diagnostics.Stopwatch.StartNew();
        var p=(await (await Upload(c,PathFor(first)+"/import/preview",bytes)).Content.ReadFromJsonAsync<ImportPreviewResult>())!;
        Assert.True(p.CanApprove,string.Join(";",p.Errors));
        var staged=await Upload(c,PathFor(first)+"/import/commit",bytes,p);staged.EnsureSuccessStatusCode();
        var firstJob=(await staged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(firstJob,await ImportWorker.Claim(f.Services.GetRequiredService<IServiceScopeFactory>(),f.Clock));
        await using(var sc=f.Services.CreateAsyncScope())await sc.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(firstJob);
        var book=AcademyWorkbookSchema.BindSample(ImportXlsx.Read(bytes),second,false);
        book=book with{Sheets=book.Sheets.Select(sheet=>sheet.Name=="الرياضات"?sheet with{Rows=sheet.Rows.Select((row,index)=>index==0?row:row.Select((cell,col)=>col is 1 or 2?new ImportCell("السباحة",true):cell).ToArray()).ToArray()}:sheet).ToArray()};
        bytes=ImportXlsx.Write(book);
        Guid swimming;
        Dictionary<Guid,string?> stamps;
        await using(var sc=f.Services.CreateAsyncScope()){
            var db=sc.ServiceProvider.GetRequiredService<FoundationDbContext>();
            swimming=Guid.NewGuid();db.Sports.Add(new(){Id=swimming,AcademyId=second,ArabicName="Swimming",Kind=Academy.Infrastructure.Structure.SportKind.Swimming});await db.SaveChangesAsync();
            stamps=await db.Users.ToDictionaryAsync(u=>u.Id,u=>u.SecurityStamp);
        }
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync(PathFor(second)+"/prepare-operation",new{})).StatusCode);
        using(var anonymous=f.CreateClient())Assert.Equal(HttpStatusCode.Unauthorized,(await Write(anonymous,PathFor(second)+"/prepare-operation",new{})).StatusCode);
        (await Write(c,PathFor(second)+"/prepare-operation",new{})).EnsureSuccessStatusCode();
        (await Write(c,PathFor(second)+"/prepare-operation",new{})).EnsureSuccessStatusCode();
        var preview=await Upload(c,PathFor(second)+"/import/preview",bytes);preview.EnsureSuccessStatusCode();p=(await preview.Content.ReadFromJsonAsync<ImportPreviewResult>())!;
        Assert.True(p.CanApprove,string.Join(";",p.Errors));Assert.Equal(34,p.AccountLinks.Length);Assert.Equal(50,p.PlayerCount);
        await using(var sc=f.Services.CreateAsyncScope()){
            var db=sc.ServiceProvider.GetRequiredService<FoundationDbContext>();
            Assert.False(await db.Players.AnyAsync(x=>x.AcademyId==second));Assert.False(await db.AcademyMemberships.AnyAsync(x=>x.AcademyId==second&&x.Role==AcademyRole.Coach));
            Assert.Equal(1,await db.AcademyConfigurationVersions.CountAsync(x=>x.AcademyId==second&&x.State==ConfigurationState.Active));
            Assert.False(await db.AcademyConfigurationVersions.AnyAsync(x=>x.AcademyId==third&&x.State==ConfigurationState.Active));
            var phone=p.AccountLinks.First(x=>x.Role=="مدرب").Phone;var user=await db.Users.SingleAsync(u=>u.PhoneNumber==phone);user.IsActive=false;await db.SaveChangesAsync();
            var bad=(await (await Upload(c,PathFor(second)+"/import/preview",bytes)).Content.ReadFromJsonAsync<ImportPreviewResult>())!;Assert.False(bad.CanApprove);
            user.IsActive=true;var original=user.DisplayName;user.DisplayName="هوية مختلفة خيالية";await db.SaveChangesAsync();
            bad=(await (await Upload(c,PathFor(second)+"/import/preview",bytes)).Content.ReadFromJsonAsync<ImportPreviewResult>())!;Assert.False(bad.CanApprove);
            user.DisplayName=original;await db.SaveChangesAsync();
        }
        // A preview must not authorize account or child links without the explicit confirmation.
        var csrf=await c.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf");using(var form=new MultipartFormDataContent()){
            form.Add(new ByteArrayContent(bytes),"file","academy.xlsx");form.Add(new StringContent(p.FileHash),"ExpectedFileHash");form.Add(new StringContent(p.ReferenceStamp),"ExpectedReferenceStamp");form.Add(new StringContent("false"),"GuardianLinksApproved");
            var req=new HttpRequestMessage(HttpMethod.Post,PathFor(second)+"/import/commit"){Content=form};req.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());req.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());Assert.Equal(HttpStatusCode.UnprocessableEntity,(await c.SendAsync(req)).StatusCode);
        }
        // Six import requests deliberately exercise different validation paths.
        // Keep the real 6/minute protection; the successful commit waits for its
        // next window instead of treating a rate-limit rejection as validation.
        var remaining=TimeSpan.FromSeconds(61)-importWindow.Elapsed;
        if(remaining>TimeSpan.Zero)await Task.Delay(remaining);
        staged=await Upload(c,PathFor(second)+"/import/commit",bytes,p);staged.EnsureSuccessStatusCode();var job=(await staged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(job,await ImportWorker.Claim(f.Services.GetRequiredService<IServiceScopeFactory>(),f.Clock));
        await using(var sc=f.Services.CreateAsyncScope())await sc.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(job);
        await using(var sc=f.Services.CreateAsyncScope()){
            var db=sc.ServiceProvider.GetRequiredService<FoundationDbContext>();
            Assert.Equal(50,await db.Players.CountAsync(x=>x.AcademyId==first));Assert.Equal(50,await db.Players.CountAsync(x=>x.AcademyId==second));Assert.False(await db.Players.AnyAsync(x=>x.AcademyId==third));
            Assert.Single(await db.Sports.Where(x=>x.AcademyId==second).ToListAsync());Assert.All(await db.SportEnrollments.Where(x=>x.AcademyId==second).ToListAsync(),x=>Assert.Equal(swimming,x.SportId));
            Assert.Equal(50,await db.GuardianPlayerLinks.CountAsync(x=>x.AcademyId==second));Assert.False(await db.GuardianPlayerLinks.AnyAsync(x=>x.AcademyId==second&&x.Player.AcademyId!=second));
            Assert.Equal(stamps.Count,await db.Users.CountAsync());foreach(var u in await db.Users.ToListAsync())Assert.Equal(stamps[u.Id],u.SecurityStamp);
            Assert.Equal(ImportJobState.Completed,(await db.AcademyImportJobs.SingleAsync(x=>x.Id==job)).State);
        }
        var replay=await Upload(c,PathFor(second)+"/import/commit",bytes,p);replay.EnsureSuccessStatusCode();Assert.Equal(job,(await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }
    [Fact]
    public async Task Structure_save_initializes_core_operation_once_without_a_licensing_section()
    {
        await using var f=new Factory();using var c=await Login(f);var id=await Create(c);var other=await Create(c);var path=$"/api/v1/platform/academies/{id}";
        var structure=new {branches=new[]{new{id=Guid.NewGuid(),name="فرع النخيل",address="القاهرة"}},sports=new[]{new{id=Guid.NewGuid(),name="كرة القدم",kind="Football"}}};
        (await Write(c,path+"/structure",structure,true)).EnsureSuccessStatusCode();
        var detail=await c.GetFromJsonAsync<JsonElement>(path+"/setup");Assert.DoesNotContain(detail.GetProperty("readiness").EnumerateArray(),x=>x.GetString()!.Contains("تهيئة تشغيل"));
        var licenses=await c.GetFromJsonAsync<JsonElement>(path+"/entitlements");Assert.All(licenses.EnumerateArray(),x=>Assert.Equal(x.GetProperty("feature").GetString() is "training" or "matches",x.GetProperty("enabled").GetBoolean()));
        var features=licenses.EnumerateArray().ToDictionary(x=>x.GetProperty("feature").GetString()!,x=>false);
        (await Write(c,path+"/configuration",new{features},true)).EnsureSuccessStatusCode();
        (await Write(c,path+"/structure",structure,true)).EnsureSuccessStatusCode();
        await using var scope=f.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(2,await db.AcademyConfigurationVersions.CountAsync(x=>x.AcademyId==id));
        Assert.False(await db.AcademyConfigurationVersions.AnyAsync(x=>x.AcademyId==other&&x.State==Academy.Infrastructure.Tenancy.ConfigurationState.Active));
        Assert.False(await db.AcademyFeatureEntitlements.AnyAsync(x=>x.AcademyId==other&&x.Enabled));
        var active=await db.AcademyConfigurationVersions.SingleAsync(x=>x.AcademyId==id&&x.State==Academy.Infrastructure.Tenancy.ConfigurationState.Active);Assert.False(Academy.Infrastructure.Tenancy.AcademyRuntimeSettings.Read(active.Payload).Features["training"]);
    }
    [Fact]
    public async Task Platform_hour_renews_during_activity_expires_when_idle_and_still_honors_revocation()
    {
        await using var f=new Factory();using var c=await Login(f);
        await using var scope=f.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var session=await db.UserSessions.Where(s=>s.UserId==PlatformIdentity.DemoUserId&&s.RevokedAtUtc==null).OrderByDescending(s=>s.CreatedAtUtc).FirstAsync();
        var start=session.CreatedAtUtc;Assert.InRange((session.ExpiresAtUtc-start).TotalMinutes,59.9,60.1);var absolute=session.AbsoluteExpiresAtUtc;
        f.Clock.Now=start.AddMinutes(40);Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/api/v1/platform/me")).StatusCode);await db.Entry(session).ReloadAsync();Assert.True(session.ExpiresAtUtc>=start.AddMinutes(99));Assert.Equal(absolute,session.AbsoluteExpiresAtUtc);
        f.Clock.Now=start.AddMinutes(80);Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/api/v1/platform/me")).StatusCode);
        var grant=await db.PlatformAdministrators.SingleAsync(x=>x.UserId==PlatformIdentity.DemoUserId);grant.IsActive=false;await db.SaveChangesAsync();
        try{Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/v1/platform/me")).StatusCode);}finally{grant.IsActive=true;await db.SaveChangesAsync();}
        f.Clock.Now=start.AddMinutes(141);Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/v1/platform/me")).StatusCode);
    }
    [Fact]
    public async Task Subscription_saves_without_identity_fields_and_suspension_is_reversible_with_owner_access()
    {
        await using var f=new Factory();using var c=await Login(f);var id=await Create(c);var path=$"/api/v1/platform/academies/{id}";
        var body=new PlatformSetup.SubscriptionWrite(0,"Subscribed",1200,"EGP","Monthly",new(2026,10,28),new(2026,11,28));
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PutAsJsonAsync(path+"/subscription",body)).StatusCode);
        (await Write(c,path+"/subscription",body,true)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Write(c,path+"/subscription",body,true)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Write(c,path+"/subscription",body with{Version=1,SubscriptionTo=new(2026,9,1)},true)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Write(c,path+"/state",new{target="Active",version=1},true)).StatusCode);
        await using var scope=f.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var a=await db.Academies.SingleAsync(a=>a.Id==id);var p=await db.AcademyPlatformProfiles.SingleAsync(p=>p.AcademyId==id);
        Assert.Equal("",p.Location);Assert.Equal(1200,p.PlatformFee);var owner=await db.AcademyMemberships.Include(m=>m.User).SingleAsync(m=>m.AcademyId==id&&m.Role==AcademyRole.AcademyOwner);
        // Existing active legacy academy may have incomplete new setup fields.
        a.IsActive=true;p.OperationalState="Active";await db.SaveChangesAsync();
        using var member=f.CreateClient();(await Write(member,"/api/v1/auth/login",new{email=owner.User.Email,password="Demo-Only-123!"},platform:false)).EnsureSuccessStatusCode();
        (await Write(c,path+"/state",new{target="Suspended",version=1},true)).EnsureSuccessStatusCode();Assert.NotEqual(HttpStatusCode.OK,(await member.GetAsync("/api/v1/me")).StatusCode);
        (await Write(c,path+"/state",new{target="Active",version=2},true)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.OK,(await member.GetAsync("/api/v1/me")).StatusCode);
        using var other=f.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await other.GetAsync(path+"/setup")).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await member.GetAsync(path+"/setup")).StatusCode);
    }
    [Fact]
    public async Task Completed_50_downloads_before_licensing_then_publishes_linked_players_once_and_isolates_second_academy()
    {
        await using var f=new Factory();using var c=await Login(f);var id=await Create(c);var other=await Create(c);var path=$"/api/v1/platform/academies/{id}";
        var download=await c.GetAsync(path+"/import/connected-sample");download.EnsureSuccessStatusCode();Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",download.Content.Headers.ContentType!.MediaType);Assert.EndsWith(".xlsx",download.Content.Headers.ContentDisposition!.FileNameStar);
        var bytes=await download.Content.ReadAsByteArrayAsync();var book=ImportXlsx.Read(bytes);Assert.Equal(51,book.Sheet("اللاعبون").Rows.Count);Assert.DoesNotContain(book.Sheets,s=>s.Name=="أنماط التدريب"||s.Name=="مستويات التدريب"||s.Name=="العروض"||s.Name=="الأرصدة الافتتاحية");
        var blank=ImportXlsx.Read(await c.GetByteArrayAsync(path+"/import/connected-template"));Assert.Single(blank.Sheet("اللاعبون").Rows);
        var denied=await Upload(c,path+"/import/preview",bytes);Assert.False(denied.IsSuccessStatusCode);
        foreach(var academy in new[]{id,other}){var all=await c.GetFromJsonAsync<JsonElement>($"/api/v1/platform/academies/{academy}/entitlements");var features=all.EnumerateArray().ToDictionary(x=>x.GetProperty("feature").GetString()!,x=>x.GetProperty("feature").GetString() is "training" or "matches");(await Write(c,$"/api/v1/platform/academies/{academy}/entitlements",new{planCode="Trial",features},true)).EnsureSuccessStatusCode();}
        var r=await Upload(c,path+"/import/preview",bytes);r.EnsureSuccessStatusCode();var preview=(await r.Content.ReadFromJsonAsync<ImportPreviewResult>())!;Assert.True(preview.CanApprove,string.Join(";",preview.Errors.Select(e=>$"{e.Sheet}:{e.Row}:{e.Column}:{e.Message}")));Assert.Equal(50,preview.PlayerCount);Assert.Equal(25,preview.GuardianCount);Assert.Equal(50,preview.GuardianRelationships);Assert.Equal(12,preview.CoachAssignments);Assert.True(preview.TournamentParticipations>0);
        var wrong=await Upload(c,$"/api/v1/platform/academies/{other}/import/preview",bytes);wrong.EnsureSuccessStatusCode();Assert.False((await wrong.Content.ReadFromJsonAsync<ImportPreviewResult>())!.CanApprove);
        var stage=await Upload(c,path+"/import/commit",bytes,preview);stage.EnsureSuccessStatusCode();var job=(await stage.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using(var scope=f.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var j=await db.AcademyImportJobs.SingleAsync(x=>x.Id==job);Assert.Equal(ImportJobState.QueuedV3,j.State);Assert.Equal(0,await db.AcademyImportJobs.FromSqlInterpolated($"SELECT *, xmin FROM \"AcademyImportJobs\" WHERE \"Id\" = {job} AND (\"State\" = 'Queued' OR (\"State\" = 'Running' AND \"LeaseUntilUtc\" < {f.Clock.GetUtcNow()}))").CountAsync());}
        Assert.Equal(job,await ImportWorker.Claim(f.Services.GetRequiredService<IServiceScopeFactory>(),f.Clock));
        await using(var scope=f.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(ImportJobState.RunningV3,(await db.AcademyImportJobs.SingleAsync(j=>j.Id==job)).State);}
        await using(var scope=f.Services.CreateAsyncScope())await scope.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(job);
        var replay=await Upload(c,path+"/import/commit",bytes,preview);replay.EnsureSuccessStatusCode();Assert.Equal(job,(await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        await using(var scope=f.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(50,await db.Players.CountAsync(x=>x.AcademyId==id));Assert.True(await db.Players.AnyAsync(x=>x.AcademyId==id&&x.PlayerCode=="PL01"));Assert.Equal(50,await db.SportEnrollments.CountAsync(x=>x.AcademyId==id));Assert.Equal(50,await db.GuardianPlayerLinks.CountAsync(x=>x.AcademyId==id));Assert.False(await db.Players.AnyAsync(x=>x.AcademyId==other));Assert.False(await db.Collections.AnyAsync(x=>x.AcademyId==id));Assert.Equal(4,await db.SubscriptionPeriods.CountAsync(x=>x.AcademyId==id&&x.RemainingSessions==11));Assert.Equal(46,await db.SubscriptionPeriods.CountAsync(x=>x.AcademyId==id&&x.RemainingSessions==12));Assert.Equal(50,await db.Players.CountAsync(x=>x.AcademyId==id&&x.PhotoReference!=null));}
        var detail=await c.GetFromJsonAsync<JsonElement>(path+"/setup");
        foreach(var user in detail.GetProperty("users").EnumerateArray().Where(u=>u.GetProperty("role").GetString() is "Coach" or "Guardian").Take(12)) {
            var role=await Write(c,path+"/role-preview",new{membershipId=user.GetProperty("id").GetGuid(),requestId=Guid.NewGuid()});role.EnsureSuccessStatusCode();Assert.True((await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("readOnly").GetBoolean());
        }
    }
}
