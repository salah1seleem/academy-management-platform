using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Importing;
using Academy.Api.Operations;
using Academy.Api.Auth;
using Academy.Api.Platform;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Academy.Infrastructure.Evaluations;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class FriendlyImportTests
{
    private sealed class InterruptedHistory : SaveChangesInterceptor
    {
        public bool Enabled {get;set;}
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken token = default)
        {
            if(Enabled && e.Context!.ChangeTracker.Entries<PlayerEvaluation>().Any(x=>x.State==EntityState.Added)) { Enabled=false; throw new IOException("Controlled import interruption after structure and sessions, before commit"); }
            return base.SavingChangesAsync(e,result,token);
        }
    }
    private sealed class Factory : WebApplicationFactory<Program>
    {
        public InterruptedHistory Interruption {get;} = new();
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.ConfigureServices(s=>s.AddDbContext<FoundationDbContext>(options=>options.AddInterceptors(Interruption)));
            b.UseEnvironment("Demo"); b.UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Separate database required"));
            b.UseSetting("Demo:SeedEnabled", "true"); b.UseSetting("Demo:SeedProfile", "LegacyRegression"); b.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); b.UseSetting("Demo:FixedOtpEnabled", "true"); b.UseSetting("Demo:FixedOtp", "246810"); b.UseSetting("Demo:ReferenceDate", "2026-10-25"); b.UseSetting("Imports:WorkerEnabled", "false"); b.UseSetting("Notifications:WorkerEnabled", "false");
        }
    }
    private static async Task<HttpResponseMessage> Write(HttpClient c, string path, object body, bool put = false)
    {
        var csrf = await c.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf"); var request = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); return await c.SendAsync(request);
    }
    private static async Task<JsonElement> Upload(HttpClient c, string path, byte[] bytes, ImportPreviewResult? preview = null, bool approvePayments = true)
    {
        var csrf = await c.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf"); using var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(bytes), "file", "fictional.xlsx");
        if (preview != null) foreach (var p in new Dictionary<string, string> { ["ExpectedFileHash"] = preview.FileHash, ["ExpectedReferenceStamp"] = preview.ReferenceStamp, ["GuardianLinksApproved"] = "true", ["OpeningBalancesApproved"] = "true", ["AcceptFinancialReview"] = "true", ["HistoricalPaymentsApproved"] = approvePayments ? "true" : "false", ["GuardianResolutions"] = "[]" }) form.Add(new StringContent(p.Value), p.Key);
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form }; request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await c.SendAsync(request); var body = await response.Content.ReadAsStringAsync(); Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}"); return JsonSerializer.Deserialize<JsonElement>(body);
    }
    [Fact]
    public async Task Friendly_history_publishes_once_with_siblings_multiple_guardians_and_explicit_coach_guardian_identity()
    {
        await using var factory = new Factory(); using var client = factory.CreateClient();
        (await Write(client, "/api/v1/platform/auth/login", new { email = PlatformIdentity.DemoEmail, password = "Demo-Only-123!", code = "246810" })).EnsureSuccessStatusCode();
        var created = await Write(client, "/api/v1/platform/academies", new PlatformAcademyWrite(Guid.NewGuid(), "friendly-" + Guid.NewGuid().ToString("N")[..12], "أكاديمية تاريخ خيالية", "EGP", "Africa/Cairo", "مالك خيالي", Guid.NewGuid() + "@example.test", "Demo-Only-123!", "multi-sport", "01094" + Random.Shared.Next(100000, 999999)));
        created.EnsureSuccessStatusCode(); var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); var path = $"/api/v1/platform/academies/{id}";
        var all = await client.GetFromJsonAsync<JsonElement>(path + "/entitlements"); var features = all.EnumerateArray().ToDictionary(x => x.GetProperty("feature").GetString()!, x => x.GetProperty("feature").GetString() == "training");
        (await Write(client, path + "/entitlements", new { planCode = "Trial", features }, true)).EnsureSuccessStatusCode();
        var book = FriendlyImportRehearsal.Create(id, new(2026, 10, 25), 10, Random.Shared.Next(30_000_000, 39_000_000));
        var bytes = ImportXlsx.Write(book);
        var report = await Upload(client, path + "/import/preview", bytes); var preview = report.Deserialize<ImportPreviewResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(preview.CanApprove, string.Join(";", preview.Errors.Select(e => $"{e.Sheet}:{e.Row}:{e.Column}:{e.Message}"))); Assert.Empty(preview.GuardianMatches); Assert.Equal(10, preview.PlayerCount); Assert.True(preview.HistoricalPayments > 0);
        ImportWorkbook Changed(string sheet, int row, string key, string value) { var column=Array.FindIndex(FriendlyImportSchema.Sheets[sheet],f=>f.Key==key);return new(book.Sheets.Select(s=>s.Name!=sheet?s:s with{Rows=s.Rows.Select((r,i)=>i!=row?r:r.Select((c,n)=>n==column?new ImportCell(value,true):c).ToArray()).ToArray()}).ToArray()); }
        var negativeIndex = 0;
        foreach(var bad in new[]{("التسجيلات","Plan","PK-OTHER-TENANT"),("المجموعات","Coaches","CO-UNKNOWN"),("التسجيلات","Start","2027-02-30"),("فترات سابقة اختيارية","Net","-10"),("التسجيلات","Remaining","-1"),("درجات اختيارية","Score","101"),("درجات اختيارية","Criterion","SP02-CR1")}) {
            ImportPreviewResult rejected;
            var negative=ImportXlsx.Write(Changed(bad.Item1,1,bad.Item2,bad.Item3));
            if(negativeIndex++ < 2) rejected=(await Upload(client,path+"/import/preview",negative)).Deserialize<ImportPreviewResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            else { await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();db.BindRequestScope(id,PlatformIdentity.DemoUserId,Guid.Empty,null);rejected=(await ConnectedImportPlan.Build(ImportXlsx.Read(negative),db,id,TimeProvider.System)).Validate("negative",new HashSet<string>()); }
            Assert.False(rejected.CanApprove);Assert.NotEmpty(rejected.Errors);Assert.All(rejected.Errors,e=>Assert.True(e.Row>0));
        }
        foreach (var variant in new[] { "identity", "links" }) {
            await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();db.BindRequestScope(id,PlatformIdentity.DemoUserId,Guid.Empty,null);
            var rejected=(await ConnectedImportPlan.Build(FriendlyImportRehearsal.Correction(id,new(2026,10,25),variant),db,id,TimeProvider.System)).Validate("bad-structure",new HashSet<string>());
            Assert.False(rejected.CanApprove); Assert.All(rejected.Errors,e=>{Assert.True(e.Row>0);Assert.NotEmpty(e.Column);Assert.NotEqual("Metadata",e.Sheet);});
        }
        var staged = await Upload(client, path + "/import/commit", bytes, preview); var jobId = staged.GetProperty("id").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var job = await db.AcademyImportJobs.SingleAsync(j => j.Id == jobId); job.State = ImportJobState.Running; await db.SaveChangesAsync(); }
        factory.Interruption.Enabled=true;
        await using (var scope = factory.Services.CreateAsyncScope()) await Assert.ThrowsAsync<IOException>(()=>scope.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(jobId));
        await using (var scope = factory.Services.CreateAsyncScope()) {
            var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            Assert.False(await db.Players.AnyAsync(p=>p.AcademyId==id));Assert.False(await db.ImportedHouseholds.AnyAsync(h=>h.AcademyId==id));Assert.False(await db.TrainingGroups.AnyAsync(g=>g.AcademyId==id));Assert.False(await db.Collections.AnyAsync(c=>c.AcademyId==id));Assert.Equal(ImportJobState.Running,await db.AcademyImportJobs.Where(j=>j.Id==jobId).Select(j=>j.State).SingleAsync());
        }
        await using (var scope = factory.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(jobId);
        Guid admin, branch;
        await using(var scope=factory.Services.CreateAsyncScope()) {
            var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var owner=await db.AcademyMemberships.SingleAsync(m=>m.AcademyId==id&&m.Role==AcademyRole.AcademyOwner);
            admin=Guid.NewGuid();branch=await db.TrainingGroups.Where(g=>g.AcademyId==id).Select(g=>g.BranchId).FirstAsync();
            db.AcademyMemberships.Add(new(){Id=admin,AcademyId=id,UserId=owner.UserId,Role=AcademyRole.AcademyAdmin,IsActive=true,AllBranches=false,AllSports=true});await db.SaveChangesAsync();
        }
        var adminAccess=$"/api/v1/platform/academies/{id}/users/{admin}/access";
        var actions=StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,m is "subscriptions" or "attendance",false,false,false)).ToArray();
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Write(client,adminAccess,new StaffAccessWrite(false,[Guid.NewGuid()],actions,AllSports:true,Sports:[]),true)).StatusCode);
        (await Write(client,adminAccess,new StaffAccessWrite(false,[branch],actions,AllSports:true,Sports:[],SensitivePermissions:[],WorkflowActions:[]),true)).EnsureSuccessStatusCode();
        var access=await client.GetFromJsonAsync<JsonElement>(adminAccess);Assert.Single(access.GetProperty("selectedBranches").EnumerateArray());
        Guid[] members;
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();members=await db.AcademyMemberships.Where(m=>m.AcademyId==id&&m.IsActive).GroupBy(m=>m.Role).Select(g=>g.OrderBy(m=>m.CreatedAtUtc).Select(m=>m.Id).First()).ToArrayAsync();}
        foreach(var member in members) {
            var previewRequest=Guid.NewGuid();var response=await Write(client,path+"/role-preview",new {requestId=previewRequest,membershipId=member});response.EnsureSuccessStatusCode();
            var view=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.True(view.GetProperty("readOnly").GetBoolean());Assert.True(view.GetProperty("preparation").GetBoolean());Assert.False(response.Headers.Contains("Set-Cookie"));
            var role=view.GetProperty("role").GetString();
            if(role is "AcademyOwner" or "AcademyAdmin")Assert.Equal(10,view.GetProperty("subscriptions").GetProperty("total").GetInt32());
            if(role=="Guardian")Assert.InRange(view.GetProperty("guardian").GetProperty("children").GetArrayLength(),1,2);
            if(role=="Coach")Assert.InRange(view.GetProperty("nextSession").GetArrayLength(),0,1);
            (await Write(client,path+"/role-preview",new {requestId=previewRequest,membershipId=member})).EnsureSuccessStatusCode();
            await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(1,await db.PlatformAudits.CountAsync(a=>a.Id==previewRequest));Assert.False(await db.AcademyMemberships.AnyAsync(m=>m.UserId==PlatformIdentity.DemoUserId));
        }
        Assert.Equal(HttpStatusCode.NotFound,(await Write(client,$"/api/v1/platform/academies/{Guid.NewGuid()}/role-preview",new{requestId=Guid.NewGuid(),membershipId=admin})).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            Assert.Equal(10, await db.Players.CountAsync(p => p.AcademyId == id)); Assert.Equal(10, await db.SportEnrollments.CountAsync(p => p.AcademyId == id));
            Assert.Equal(11, await db.GuardianPlayerLinks.CountAsync(l => l.AcademyId == id)); Assert.Equal(10, await db.GuardianProfiles.CountAsync(g => g.AcademyId == id));
            var coachUser = await db.AcademyMemberships.Where(m => m.AcademyId == id && m.Role == AcademyRole.Coach).Select(m => m.UserId).SingleAsync(); Assert.True(await db.AcademyMemberships.AnyAsync(m => m.AcademyId == id && m.UserId == coachUser && m.Role == AcademyRole.Guardian));
            Assert.Equal(preview.HistoricalPayments, await db.Collections.CountAsync(c => c.AcademyId == id)); Assert.Equal(preview.HistoricalPaymentTotal, await db.Collections.Where(c => c.AcademyId == id).SumAsync(c => c.Amount)); Assert.Equal(preview.HistoricalPayments, await db.Receipts.CountAsync(c => c.AcademyId == id));
            Assert.True(await db.PlayerAttendances.AnyAsync(a => a.AcademyId == id)); Assert.True(await db.PlayerEvaluations.AnyAsync(e => e.AcademyId == id)); Assert.All(await db.Players.Where(p => p.AcademyId == id).ToArrayAsync(), p => Assert.NotNull(p.RegisteredOn));
            Assert.False(await db.Academies.Where(a => a.Id == id).Select(a => a.IsActive).SingleAsync());
        }
        var replay = await Upload(client, path + "/import/commit", bytes, preview); Assert.Equal(jobId, replay.GetProperty("id").GetGuid()); Assert.True(replay.GetProperty("replayedRequest").GetBoolean());
    }
}
