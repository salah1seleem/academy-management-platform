using System.Formats.Cbor;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Structure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class StaffPasskeyTests : IAsyncLifetime
{
    private sealed class Clock:TimeProvider {public DateTimeOffset Now=new(2026,10,16,10,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>Now;}
    private sealed class Factory(Clock clock,bool enabled=true,string rp="academy.test",string origin="https://academy.test"):WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment("Demo");b.UseSetting("ConnectionStrings:Default",Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")??throw new InvalidOperationException("Isolated database required"));
            b.UseSetting("Demo:SeedProfile","LegacyRegression");b.UseSetting("Demo:SeedEnabled","true");b.UseSetting("Demo:StaffPassword","Demo-Only-123!");b.UseSetting("Demo:ReferenceDate","2026-09-28");
            b.UseSetting("StaffPasskeys:Enabled",enabled.ToString());b.UseSetting("StaffPasskeys:RpId",rp);b.UseSetting("StaffPasskeys:Origins:0",origin);
            b.ConfigureServices(s=>{s.RemoveAll<TimeProvider>();s.AddSingleton<TimeProvider>(clock);});
        }
    }
    private readonly Clock clock=new();private Factory factory=null!;
    public Task InitializeAsync(){factory=new(clock);_=factory.CreateClient();return Task.CompletedTask;}
    public Task DisposeAsync()=>factory.DisposeAsync().AsTask();
    private async Task<(HttpClient Client,Guid Member)> Staff(AcademyRole role=AcademyRole.Coach,bool narrow=false)
    {
        await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();var id=Guid.NewGuid();var u=new ApplicationUser{Id=Guid.NewGuid(),UserName=$"passkey-{id:N}",Email=$"passkey-{id:N}@example.test",DisplayName="موظف تحقق اصطناعي",SecurityStamp=Guid.NewGuid().ToString()};u.NormalizedEmail=u.Email.ToUpperInvariant();
        var m=new AcademyMembership{Id=id,AcademyId=DemoSeed.NogoomAcademyId,UserId=u.Id,Role=role,UseActionGrants=narrow,AllSports=!narrow};db.Users.Add(u);db.AcademyMemberships.Add(m);
        if(narrow){var sport=await db.Sports.Where(x=>x.AcademyId==m.AcademyId&&x.IsActive).Select(x=>x.Id).FirstAsync();db.MembershipSportAccesses.Add(new(){Id=Guid.NewGuid(),AcademyId=m.AcademyId,MembershipId=m.Id,SportId=sport});}
        await db.SaveChangesAsync();var c=factory.CreateClient();await MobileCoachTestSession.SignInAsync(c,factory.Services,u.Email,role);return(c,id);
    }
    private static Task<HttpResponseMessage> Post(HttpClient c,string path,object body)=>c.PostAsJsonAsync(StaffPasskeys.Path+path,body);
    private static async Task<JsonElement> Options(HttpClient c,string kind="register"){var r=await Post(c,$"/{kind}/options",new{name="وسيلة عرض اصطناعية"});Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());return await r.Content.ReadFromJsonAsync<JsonElement>();}
    private static async Task<JsonElement> Finish(HttpClient c,JsonElement o,object credential,string kind="register"){var r=await Post(c,$"/{kind}/complete",new{challengeId=o.GetProperty("challengeId").GetGuid(),credential});Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());return await r.Content.ReadFromJsonAsync<JsonElement>();}

    private async Task<Guid> AttendanceSite(Guid member)
    {
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var branch=new Branch{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,ArabicName=$"فرع تحقق اصطناعي {member:N}",CheckInLatitude=30.05,CheckInLongitude=31.3};db.Branches.Add(branch);
        db.Employees.Add(new(){Id=Guid.NewGuid(),AcademyId=branch.AcademyId,MembershipId=member,BranchId=branch.Id,Name="موظف تحقق اصطناعي",Phone="010888"+Random.Shared.Next(10000,99999),JobTitle="تدريب",MonthlySalary=0,Currency="EGP",ContractStart=new(2026,9,1)});await db.SaveChangesAsync();return branch.Id;
    }
    private StaffAttendanceIntent Intent(Guid site,string operation="CheckIn")=>new(site,operation,30.05,31.3,5,clock.Now);
    private async Task RenewSession(HttpClient c,Guid member){await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var email=await db.AcademyMemberships.Where(m=>m.Id==member).Select(m=>m.User.Email).SingleAsync();await MobileCoachTestSession.SignInAsync(c,factory.Services,email!);}
    private static async Task<JsonElement> AttendanceOptions(HttpClient c,StaffAttendanceIntent body)
    {var r=await Post(c,"/attendance/options",body);Assert.True(r.IsSuccessStatusCode,$"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");return await r.Content.ReadFromJsonAsync<JsonElement>();}
    private static async Task<(JsonElement Receipt,StaffAttendanceProof Command)> Attend(HttpClient c,Authenticator a,StaffAttendanceIntent body,uint counter)
    {var o=await AttendanceOptions(c,body);var command=new StaffAttendanceProof(o.GetProperty("challengeId").GetGuid(),body,JsonSerializer.SerializeToElement(a.Verify(o,counter)));var r=await Post(c,"/attendance/complete",command);Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());return(await r.Content.ReadFromJsonAsync<JsonElement>(),command);}

    [Theory]
    [InlineData("academy.test","https://academy.test",true)]
    [InlineData("localhost","http://localhost:3190",false)]
    [InlineData("academy.test","https://other.academy.test",false)]
    public async Task Native_iOS_origin_hint_requires_exact_HTTPS_RP_and_never_records_attendance(string rp,string origin,bool expected)
    {
        var s=await Staff();using var f=new Factory(clock,rp:rp,origin:origin);using var c=f.CreateClient();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var email=await db.AcademyMemberships.Where(m=>m.Id==s.Member).Select(m=>m.User.Email).SingleAsync();await MobileCoachTestSession.SignInAsync(c,f.Services,email!);
        var data=await c.GetFromJsonAsync<JsonElement>(StaffPasskeys.Path+"/");Assert.True(data.GetProperty("configured").GetBoolean());Assert.Equal(rp,data.GetProperty("rpId").GetString());Assert.Equal(expected,data.GetProperty("nativeIosOriginConfigured").GetBoolean());Assert.Empty(data.GetProperty("items").EnumerateArray());Assert.False(await db.StaffDailyAttendances.AnyAsync(a=>a.MembershipId==s.Member));
    }
    [Fact]
    public async Task Changing_RP_keeps_old_keys_without_offering_them_as_current_attendance_credentials()
    {
        var s=await Staff();using var c=s.Client;var site=await AttendanceSite(s.Member);using var a=new Authenticator();var reg=await Options(c);await Finish(c,reg,a.Register(reg));Assert.True((await c.GetFromJsonAsync<JsonElement>(StaffPasskeys.Path+"/attendance")).GetProperty("hasCredential").GetBoolean());
        using var changed=new Factory(clock,rp:"other.test",origin:"https://other.test");using var other=changed.CreateClient();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var email=await db.AcademyMemberships.Where(m=>m.Id==s.Member).Select(m=>m.User.Email).SingleAsync();await MobileCoachTestSession.SignInAsync(other,changed.Services,email!);
        var state=await other.GetFromJsonAsync<JsonElement>(StaffPasskeys.Path+"/attendance");Assert.False(state.GetProperty("hasCredential").GetBoolean());Assert.Equal("other.test",state.GetProperty("rpId").GetString());Assert.Equal(HttpStatusCode.Conflict,(await Post(other,"/attendance/options",Intent(site))).StatusCode);Assert.Single(await db.StaffPasskeys.Where(k=>k.MembershipId==s.Member&&k.IsActive).ToArrayAsync());Assert.False(await db.StaffDailyAttendances.AnyAsync(d=>d.MembershipId==s.Member));
    }

    [Fact]
    public async Task Branch_policy_is_owner_saved_versioned_audited_tenant_scoped_and_applied_to_signed_operations()
    {
        var s=await Staff();using var c=s.Client;using var a=new Authenticator();var site=await AttendanceSite(s.Member);var reg=await Options(c);await Finish(c,reg,a.Register(reg));
        using var owner=factory.CreateClient();await MobileCoachTestSession.SignInAsync(owner,factory.Services,DemoSeed.OwnerEmail,AcademyRole.AcademyOwner);
        var path=$"/api/v1/manage/branches/{site}/check-in-site";var initial=await owner.GetFromJsonAsync<JsonElement>(path);var policy=new StaffAttendancePolicy(true,10,5,5,30,true,false,false,null);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.PutAsJsonAsync(path,new CheckInSiteWrite(30.05,31.3,policy,initial.GetProperty("version").GetUInt32()))).StatusCode);
        using var foreign=factory.CreateClient();var csrf=await foreign.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");using var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email=DemoSeed.FutureOwnerEmail,password="Demo-Only-123!"})};login.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());(await foreign.SendAsync(login)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync(path)).StatusCode);
        async Task<HttpResponseMessage> Save(StaffAttendancePolicy p,uint? v=null)=>await owner.PutAsJsonAsync(path,new CheckInSiteWrite(30.05,31.3,p,v??(await owner.GetFromJsonAsync<JsonElement>(path)).GetProperty("version").GetUInt32()));
        foreach(var invalid in new[]{policy with{RadiusMeters=9},policy with{RadiusMeters=501},policy with{MaxAccuracyMeters=11},policy with{MaxAgeSeconds=61},policy with{ChallengeSeconds=121}})Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Save(invalid)).StatusCode);
        (await Save(policy)).EnsureSuccessStatusCode();var saved=await owner.GetFromJsonAsync<JsonElement>(path);Assert.NotEqual(initial.GetProperty("version").GetUInt32(),saved.GetProperty("version").GetUInt32());var history=saved.GetProperty("history");Assert.Single(history.EnumerateArray());Assert.Equal(10,JsonSerializer.Deserialize<JsonElement>(history[0].GetProperty("afterPolicy").GetString()!).GetProperty("radiusMeters").GetInt32());
        (await Save(policy)).EnsureSuccessStatusCode();Assert.Single((await owner.GetFromJsonAsync<JsonElement>(path)).GetProperty("history").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict,(await Save(policy with{RadiusMeters=20},initial.GetProperty("version").GetUInt32())).StatusCode);
        var read=await c.GetFromJsonAsync<JsonElement>(StaffPasskeys.Path+"/attendance");var b=read.GetProperty("branches")[0];Assert.Equal(10,b.GetProperty("radiusMeters").GetInt32());Assert.True(b.GetProperty("attendanceRequired").GetBoolean());Assert.DoesNotContain("latitude",read.GetRawText(),StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/attendance/options",Intent(site) with{Latitude=30.0501})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/attendance/options",Intent(site) with{AccuracyMeters=6})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/attendance/options",Intent(site) with{CapturedAtUtc=clock.Now.AddSeconds(-6)})).StatusCode);
        var intent=Intent(site);var pending=await AttendanceOptions(c,intent);Assert.Equal(clock.Now.AddSeconds(30),pending.GetProperty("expiresAtUtc").GetDateTimeOffset());
        (await Save(policy with{RadiusMeters=20})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/attendance/complete",new StaffAttendanceProof(pending.GetProperty("challengeId").GetGuid(),intent,JsonSerializer.SerializeToElement(a.Verify(pending))))).StatusCode);
        var started=await Attend(c,a,Intent(site) with{Latitude=30.0501},1);(await Save(policy with{RadiusMeters=20,Enabled=false})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/attendance/options",Intent(site,"CheckOut"))).StatusCode);
        var disabled=await c.GetFromJsonAsync<JsonElement>(StaffPasskeys.Path+"/attendance");Assert.False(disabled.GetProperty("branches")[0].GetProperty("configured").GetBoolean());Assert.True(disabled.GetProperty("hasOpenAttendance").GetBoolean());
        Assert.Equal(started.Receipt.GetRawText(),(await (await Post(c,"/attendance/complete",started.Command)).Content.ReadFromJsonAsync<JsonElement>()).GetRawText()); // same receipt survives a later policy change
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Single(await db.StaffWorkPeriods.Where(p=>p.MembershipId==s.Member).ToArrayAsync());Assert.Null(await db.StaffWorkPeriods.Where(p=>p.MembershipId==s.Member).Select(p=>p.EndUtc).SingleAsync());Assert.Equal(0,await db.StaffDailyAttendances.Where(d=>d.MembershipId==s.Member).Select(d=>d.ApprovedDeduction).SingleAsync());
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Branches\" SET \"StaffCheckInRadiusMeters\"=0 WHERE \"Id\"={site}"))).SqlState);
        var foreignMember=await db.AcademyMemberships.Where(m=>m.AcademyId!=DemoSeed.NogoomAcademyId).Select(m=>m.Id).FirstAsync();Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Save(policy with{ReviewerMembershipId=foreignMember})).StatusCode);
        Assert.Equal("23503",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Branches\" SET \"StaffCheckInReviewerMembershipId\"={foreignMember} WHERE \"Id\"={site}"))).SqlState);
    }

    [Fact]
    public async Task Signed_attendance_preserves_two_periods_own_history_and_exact_replay_without_money_or_player_attendance()
    {
        var s=await Staff();using var c=s.Client;using var a=new Authenticator();var site=await AttendanceSite(s.Member);var register=await Options(c);await Finish(c,register,a.Register(register));
        await using var baselineScope=factory.Services.CreateAsyncScope();var baseline=baselineScope.ServiceProvider.GetRequiredService<FoundationDbContext>();var financialBefore=new[]{await baseline.Collections.CountAsync(),await baseline.PayrollAdjustments.CountAsync(),await baseline.PlayerAttendances.CountAsync()};
        var first=await Attend(c,a,Intent(site),1);Assert.True(first.Receipt.GetProperty("attendanceRecorded").GetBoolean());var id=first.Receipt.GetProperty("attendanceId").GetGuid();
        clock.Now=clock.Now.AddMinutes(60);await RenewSession(c,s.Member);var end=await Attend(c,a,Intent(site,"CheckOut"),2);Assert.Equal(id,end.Receipt.GetProperty("attendanceId").GetGuid());
        clock.Now=clock.Now.AddMinutes(60);await RenewSession(c,s.Member);await Attend(c,a,Intent(site),3);clock.Now=clock.Now.AddMinutes(60);await RenewSession(c,s.Member);await Attend(c,a,Intent(site,"CheckOut"),4);
        var replay=await Post(c,"/attendance/complete",first.Command);Assert.True(replay.IsSuccessStatusCode,await replay.Content.ReadAsStringAsync());Assert.Equal(first.Receipt.GetRawText(),(await replay.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var row=await db.StaffDailyAttendances.SingleAsync(r=>r.MembershipId==s.Member);Assert.Equal(id,row.Id);Assert.Equal(0,row.ApprovedDeduction);Assert.Equal(2,await db.StaffWorkPeriods.CountAsync(p=>p.AttendanceId==id));Assert.Equal(4,await db.StaffAttendanceChanges.CountAsync(p=>p.AttendanceId==id&&p.VerificationChallengeId!=null));
        var periods=await db.StaffWorkPeriods.Where(p=>p.AttendanceId==id).ToArrayAsync();Assert.All(periods,p=>Assert.Equal("ServerVerified",p.TrustSource));Assert.Equal(7200,periods.Sum(p=>(p.EndUtc!.Value-p.StartUtc!.Value).TotalSeconds));
        Assert.Equal(financialBefore,new[]{await db.Collections.CountAsync(),await db.PayrollAdjustments.CountAsync(),await db.PlayerAttendances.CountAsync()});
        var own=await c.GetFromJsonAsync<JsonElement>("/api/v1/staff/work-periods?date=2026-10-16");Assert.Equal(7200,own.GetProperty("totalWorkedSeconds").GetInt64());
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StaffPasskeyChallenges\" SET \"OperationJson\"='{{}}' WHERE \"Id\"={first.Command.ChallengeId}"))).SqlState);
    }
    [Theory]
    [InlineData("outside")][InlineData("boundary")][InlineData("accuracy")][InlineData("stale")][InlineData("future")][InlineData("unconfigured")]
    public async Task Location_policy_does_not_accept_uncertain_outside_old_or_unconfigured_coordinates(string issue)
    {
        var s=await Staff();using var c=s.Client;using var a=new Authenticator();var site=await AttendanceSite(s.Member);var register=await Options(c);await Finish(c,register,a.Register(register));var b=Intent(site);
        b=issue switch{"outside"=>b with{Latitude=30.06},"boundary"=>b with{Latitude=30.0504,AccuracyMeters=20},"accuracy"=>b with{AccuracyMeters=31},"stale"=>b with{CapturedAtUtc=clock.Now.AddSeconds(-31)},"future"=>b with{CapturedAtUtc=clock.Now.AddSeconds(3)},_=>b};
        if(issue=="unconfigured"){await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var branch=await db.Branches.SingleAsync(x=>x.Id==site);branch.CheckInLatitude=null;branch.CheckInLongitude=null;await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/attendance/options",b)).StatusCode);await using var verify=factory.Services.CreateAsyncScope();var check=verify.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.False(await check.StaffDailyAttendances.AnyAsync(x=>x.MembershipId==s.Member));
    }
    [Fact]
    public async Task Changed_payload_foreign_identity_regular_verify_and_parallel_targets_cannot_record_another_operation()
    {
        var s=await Staff();using var c=s.Client;using var a=new Authenticator();var site=await AttendanceSite(s.Member);var register=await Options(c);await Finish(c,register,a.Register(register));
        Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/attendance/complete",new{challengeId=Guid.NewGuid(),intent=(object?)null,credential=new{}})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await c.PostAsJsonAsync("/api/v1/staff/check-in/",new DeviceCheckInWrite(site,30.05,31.3,5,true))).StatusCode);
        var b=Intent(site);var o=await AttendanceOptions(c,b);var command=new StaffAttendanceProof(o.GetProperty("challengeId").GetGuid(),b,JsonSerializer.SerializeToElement(a.Verify(o)));
        var other=await Staff();using var oc=other.Client;Assert.Equal(HttpStatusCode.NotFound,(await Post(oc,"/attendance/complete",command)).StatusCode);
        using var foreign=factory.CreateClient();var fc=await foreign.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");using var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email=DemoSeed.FutureOwnerEmail,password="Demo-Only-123!"})};login.Headers.Add("X-CSRF-TOKEN",fc.GetProperty("token").GetString());(await foreign.SendAsync(login)).EnsureSuccessStatusCode();fc=await foreign.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");using var attack=new HttpRequestMessage(HttpMethod.Post,StaffPasskeys.Path+"/attendance/complete"){Content=JsonContent.Create(command)};attack.Headers.Add("X-CSRF-TOKEN",fc.GetProperty("token").GetString());Assert.Equal(HttpStatusCode.NotFound,(await foreign.SendAsync(attack)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/attendance/complete",command with{Intent=b with{Latitude=30.05001}})).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/attendance/complete",command)).StatusCode);
        var regular=await Options(c,"verify");Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/attendance/complete",new StaffAttendanceProof(regular.GetProperty("challengeId").GetGuid(),b,JsonSerializer.SerializeToElement(a.Verify(regular))))).StatusCode);
        var one=await AttendanceOptions(c,b);var two=await AttendanceOptions(c,b);var results=await Task.WhenAll(Post(c,"/attendance/complete",new StaffAttendanceProof(one.GetProperty("challengeId").GetGuid(),b,JsonSerializer.SerializeToElement(a.Verify(one)))),Post(c,"/attendance/complete",new StaffAttendanceProof(two.GetProperty("challengeId").GetGuid(),b,JsonSerializer.SerializeToElement(a.Verify(two)))));
        Assert.Single(results,r=>r.IsSuccessStatusCode);Assert.Single(results,r=>r.StatusCode==HttpStatusCode.Conflict);
        await using var sourceScope=factory.Services.CreateAsyncScope();var sourceDb=sourceScope.ServiceProvider.GetRequiredService<FoundationDbContext>();var newSite=new Branch{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,ArabicName=$"فرع انتقال اصطناعي {s.Member:N}",CheckInLatitude=30.05,CheckInLongitude=31.3};sourceDb.Branches.Add(newSite);var employee=await sourceDb.Employees.SingleAsync(e=>e.MembershipId==s.Member);employee.BranchId=newSite.Id;await sourceDb.SaveChangesAsync();Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/attendance/options",Intent(newSite.Id,"CheckOut"))).StatusCode);
    }
    [Fact]
    public async Task Server_checkout_crosses_midnight_without_another_day_and_revoked_or_expired_proof_fails()
    {
        var s=await Staff();using var c=s.Client;using var a=new Authenticator();var site=await AttendanceSite(s.Member);var register=await Options(c);var key=await Finish(c,register,a.Register(register));clock.Now=new(2026,10,16,20,59,0,TimeSpan.Zero);await RenewSession(c,s.Member);
        var first=await Attend(c,a,Intent(site),1);clock.Now=clock.Now.AddMinutes(2);var closed=await Attend(c,a,Intent(site,"CheckOut"),2);Assert.Equal(first.Receipt.GetProperty("workDate").GetString(),closed.Receipt.GetProperty("workDate").GetString());
        var next=Intent(site);var expired=await AttendanceOptions(c,next);clock.Now=clock.Now.AddSeconds(121);Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/attendance/complete",new StaffAttendanceProof(expired.GetProperty("challengeId").GetGuid(),next,JsonSerializer.SerializeToElement(a.Verify(expired,3))))).StatusCode);
        next=Intent(site);var pending=await AttendanceOptions(c,next);(await Post(c,$"/credentials/{key.GetProperty("credential").GetProperty("id").GetGuid()}/revoke",new{})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/attendance/complete",new StaffAttendanceProof(pending.GetProperty("challengeId").GetGuid(),next,JsonSerializer.SerializeToElement(a.Verify(pending,3))))).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var row=await db.StaffDailyAttendances.SingleAsync(r=>r.MembershipId==s.Member);Assert.Equal(new DateOnly(2026,10,16),row.WorkDate);Assert.Equal(new DateOnly(2026,10,17),row.CheckOutDate);Assert.Equal(1,await db.StaffWorkPeriods.CountAsync(p=>p.AttendanceId==row.Id));
    }
    [Fact]
    public async Task Narrow_admin_can_record_only_own_authorized_branch_without_attendance_crud_and_live_site_changes_invalidate_proof()
    {
        var s=await Staff(AcademyRole.AcademyAdmin,true);using var c=s.Client;using var a=new Authenticator();var site=await AttendanceSite(s.Member);
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var member=await db.AcademyMemberships.SingleAsync(m=>m.Id==s.Member);member.AllBranches=false;db.MembershipBranchAccesses.Add(new(){Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,MembershipId=s.Member,BranchId=site});await db.SaveChangesAsync();}
        var reg=await Options(c);await Finish(c,reg,a.Register(reg));var b=Intent(site);var pending=await AttendanceOptions(c,b);
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var branch=await db.Branches.SingleAsync(x=>x.Id==site);branch.CheckInLatitude+=0.000001;await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/attendance/complete",new StaffAttendanceProof(pending.GetProperty("challengeId").GetGuid(),b,JsonSerializer.SerializeToElement(a.Verify(pending))))).StatusCode);
        await Attend(c,a,Intent(site),1);Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/v1/manage/staff-attendance/")).StatusCode);
        var read=await c.GetFromJsonAsync<JsonElement>(StaffPasskeys.Path+"/attendance");Assert.Single(read.GetProperty("branches").EnumerateArray());Assert.Single(read.GetProperty("recent").EnumerateArray());Assert.DoesNotContain("latitude",read.GetRawText(),StringComparison.OrdinalIgnoreCase);
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();await db.MembershipBranchAccesses.Where(x=>x.MembershipId==s.Member).ExecuteUpdateAsync(x=>x.SetProperty(a=>a.IsActive,false));}
        Assert.Equal(HttpStatusCode.NotFound,(await Post(c,"/attendance/options",Intent(site,"CheckOut"))).StatusCode);
    }
    [Fact]
    public async Task Signed_checkout_of_admin_started_period_does_not_claim_a_verified_start_or_invent_legacy_utc()
    {
        var s=await Staff();using var c=s.Client;using var a=new Authenticator();var site=await AttendanceSite(s.Member);var reg=await Options(c);await Finish(c,reg,a.Register(reg));using var owner=factory.CreateClient();await MobileCoachTestSession.SignInAsync(owner,factory.Services,DemoSeed.OwnerEmail,AcademyRole.AcademyOwner);owner.DefaultRequestHeaders.Add("Idempotency-Key",Guid.NewGuid().ToString());
        var root=$"/api/v1/manage/staff-attendance/{s.Member}";var saved=await owner.PutAsJsonAsync(root,new{date="2026-10-16",version=0,status="Present",checkIn="08:00:00",checkOut="09:00:00",checkOutDate="2026-10-16",approvedDeduction=0});Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
        var source=await owner.GetFromJsonAsync<JsonElement>(root+"?date=2026-10-16");owner.DefaultRequestHeaders.Remove("Idempotency-Key");owner.DefaultRequestHeaders.Add("Idempotency-Key",Guid.NewGuid().ToString());
        var added=await owner.PostAsJsonAsync(root+"/periods",new WorkPeriodWrite(new(2026,10,16),source.GetProperty("version").GetUInt32(),new(10,0),null,null));Assert.True(added.IsSuccessStatusCode,await added.Content.ReadAsStringAsync());
        var result=await Attend(c,a,Intent(site,"CheckOut"),1);await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var period=await db.StaffWorkPeriods.SingleAsync(p=>p.Id==result.Receipt.GetProperty("workPeriodId").GetGuid());Assert.Equal("MixedVerifiedEnd",period.TrustSource);Assert.Equal(new TimeOnly(10,0),period.StartTime);Assert.Equal(new DateTimeOffset(2026,10,16,7,0,0,TimeSpan.Zero),period.StartUtc);
        var legacy=await db.StaffWorkPeriods.SingleAsync(p=>p.AttendanceId==period.AttendanceId&&p.TrustSource=="LegacyLocal");Assert.Null(legacy.StartUtc);Assert.Null(legacy.EndUtc);var own=await c.GetFromJsonAsync<JsonElement>("/api/v1/staff/work-periods?date=2026-10-16");Assert.Equal(JsonValueKind.Null,own.GetProperty("totalWorkedSeconds").ValueKind);
    }

    // Ephemeral standards-format authenticator for verifier tests, not production
    // code or a claim that physical Face ID/GPS has been tested. Browser E2E is separate.
    private sealed class Authenticator:IDisposable
    {
        private readonly ECDsa key=ECDsa.Create(ECCurve.NamedCurves.nistP256);private readonly byte[] id=RandomNumberGenerator.GetBytes(32);
        private static string B(byte[] b)=>Convert.ToBase64String(b).TrimEnd('=').Replace('+','-').Replace('/','_');
        private static byte[] Decode(string s)=>Convert.FromBase64String(s.Replace('-','+').Replace('_','/').PadRight((s.Length+3)/4*4,'='));
        private static byte[] Client(JsonElement o,string type,string origin,string? challenge)=>JsonSerializer.SerializeToUtf8Bytes(new{type,origin,challenge=challenge??o.GetProperty("options").GetProperty("challenge").GetString(),crossOrigin=false});
        private static byte[] Header(byte flags,uint count,string rp="academy.test"){var b=new byte[37];SHA256.HashData(Encoding.UTF8.GetBytes(rp)).CopyTo(b,0);b[32]=flags;System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(33),count);return b;}
        public object Register(JsonElement o,string origin="https://academy.test",bool uv=true)
        {
            var p=key.ExportParameters(false);var cose=new CborWriter();cose.WriteStartMap(5);cose.WriteInt32(1);cose.WriteInt32(2);cose.WriteInt32(3);cose.WriteInt32(-7);cose.WriteInt32(-1);cose.WriteInt32(1);cose.WriteInt32(-2);cose.WriteByteString(p.Q.X!);cose.WriteInt32(-3);cose.WriteByteString(p.Q.Y!);cose.WriteEndMap();
            var length=new byte[2];System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(length,(ushort)id.Length);var auth=Header((byte)(uv?0x45:0x41),0).Concat(new byte[16]).Concat(length).Concat(id).Concat(cose.Encode()).ToArray();
            var att=new CborWriter();att.WriteStartMap(3);att.WriteTextString("fmt");att.WriteTextString("none");att.WriteTextString("attStmt");att.WriteStartMap(0);att.WriteEndMap();att.WriteTextString("authData");att.WriteByteString(auth);att.WriteEndMap();
            return new{id=B(id),rawId=B(id),type="public-key",response=new{clientDataJSON=B(Client(o,"webauthn.create",origin,null)),attestationObject=B(att.Encode()),transports=new[]{"internal"}},extensions=new{}};
        }
        public object Verify(JsonElement o,uint count=1,bool uv=true,string origin="https://academy.test",string? challenge=null,bool badSignature=false,string rp="academy.test",string? userHandle=null)
        {
            var client=Client(o,"webauthn.get",origin,challenge);var auth=Header((byte)(uv?5:1),count,rp);var signature=key.SignData(auth.Concat(SHA256.HashData(client)).ToArray(),HashAlgorithmName.SHA256,DSASignatureFormat.Rfc3279DerSequence);if(badSignature)signature[^1]^=1;
            return new{id=B(id),rawId=B(id),type="public-key",response=new{clientDataJSON=B(client),authenticatorData=B(auth),signature=B(signature),userHandle=userHandle??(o.GetProperty("options").TryGetProperty("user",out var u)?u.GetProperty("id").GetString():null)},extensions=new{}};
        }
        public void Dispose()=>key.Dispose();
    }
    [Fact]
    public async Task Registration_signed_verification_replay_revocation_and_storage_guards_never_record_attendance()
    {
        var s=await Staff();using var c=s.Client;using var a=new Authenticator();var o=await Options(c);Assert.Equal("required",o.GetProperty("options").GetProperty("authenticatorSelection").GetProperty("userVerification").GetString());
        var registered=await Finish(c,o,a.Register(o));Assert.False(registered.GetProperty("attendanceRecorded").GetBoolean());var id=registered.GetProperty("credential").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/register/complete",new{challengeId=o.GetProperty("challengeId").GetGuid(),credential=a.Register(o)})).StatusCode);
        var verify=await Options(c,"verify");Assert.Equal("required",verify.GetProperty("options").GetProperty("userVerification").GetString());var proof=a.Verify(verify);var saved=await Finish(c,verify,proof,"verify");Assert.True(saved.GetProperty("verified").GetBoolean());Assert.False(saved.GetProperty("attendanceRecorded").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/verify/complete",new{challengeId=verify.GetProperty("challengeId").GetGuid(),credential=proof})).StatusCode);
        var list=await c.GetFromJsonAsync<JsonElement>(StaffPasskeys.Path+"/");Assert.Single(list.GetProperty("items").EnumerateArray());Assert.NotNull(list.GetProperty("items")[0].GetProperty("lastVerifiedAtUtc").GetString());Assert.DoesNotContain("publicKey",list.GetRawText());Assert.DoesNotContain("credentialHash",list.GetRawText());
        var pending=await Options(c,"verify");(await Post(c,$"/credentials/{id}/revoke",new{})).EnsureSuccessStatusCode();(await Post(c,$"/credentials/{id}/revoke",new{})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(c,"/verify/complete",new{challengeId=pending.GetProperty("challengeId").GetGuid(),credential=a.Verify(pending,2)})).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.False(await db.StaffDailyAttendances.AnyAsync(x=>x.MembershipId==s.Member));Assert.Equal(1,await db.StaffPasskeys.Where(k=>k.Id==id).Select(k=>k.SignCount).SingleAsync());
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StaffPasskeys\" SET \"IsActive\"=true,\"RevokedAtUtc\"=null WHERE \"Id\"={id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StaffPasskeyChallenges\" SET \"OptionsJson\"='{{}}' WHERE \"Id\"={o.GetProperty("challengeId").GetGuid()}"))).SqlState);
    }
    [Theory]
    [InlineData("origin")][InlineData("uv")][InlineData("challenge")][InlineData("signature")][InlineData("rp")][InlineData("handle")]
    public async Task Wrong_origin_uv_challenge_or_signature_is_consumed_without_acceptance(string defect)
    {
        var s=await Staff();using var c=s.Client;using var a=new Authenticator();var create=await Options(c);await Finish(c,create,a.Register(create));var o=await Options(c,"verify");
        var proof=a.Verify(o,uv:defect!="uv",origin:defect=="origin"?"https://attacker.test":"https://academy.test",challenge:defect=="challenge"?Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)):null,badSignature:defect=="signature",rp:defect=="rp"?"attacker.test":"academy.test",userHandle:defect=="handle"?Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)):null);var body=new{challengeId=o.GetProperty("challengeId").GetGuid(),credential=proof};Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(c,"/verify/complete",body)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/verify/complete",body)).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Null(await db.StaffPasskeys.Where(k=>k.MembershipId==s.Member).Select(k=>k.LastVerifiedAtUtc).SingleAsync());
    }
    [Fact]
    public async Task Registration_requires_uv_and_a_client_boolean_is_not_device_proof()
    {
        var s=await Staff();using var c=s.Client;using var a=new Authenticator();var o=await Options(c);Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(c,"/register/complete",new{challengeId=o.GetProperty("challengeId").GetGuid(),credential=a.Register(o,uv:false)})).StatusCode);
        var next=await Options(c);Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(c,"/register/complete",new{challengeId=next.GetProperty("challengeId").GetGuid(),credential=new{biometricPassed=true}})).StatusCode);Assert.Empty((await c.GetFromJsonAsync<JsonElement>(StaffPasskeys.Path+"/")).GetProperty("items").EnumerateArray());
    }
    [Fact]
    public async Task Unchanged_signature_counter_and_disabled_configuration_do_not_mark_verified()
    {
        var s=await Staff();using var c=s.Client;using var a=new Authenticator();var create=await Options(c);await Finish(c,create,a.Register(create));var one=await Options(c,"verify");await Finish(c,one,a.Verify(one),"verify");var two=await Options(c,"verify");Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(c,"/verify/complete",new{challengeId=two.GetProperty("challengeId").GetGuid(),credential=a.Verify(two)})).StatusCode);
        await using var disabled=new Factory(clock,false);using var dc=disabled.CreateClient();dc.DefaultRequestHeaders.Authorization=c.DefaultRequestHeaders.Authorization;Assert.False((await dc.GetFromJsonAsync<JsonElement>(StaffPasskeys.Path+"/")).GetProperty("configured").GetBoolean());Assert.Equal(HttpStatusCode.Conflict,(await Post(dc,"/verify/options",new{})).StatusCode);
    }
    [Fact]
    public async Task Expired_wrong_purpose_cross_identity_and_parallel_challenges_fail_closed()
    {
        var s=await Staff();using var c=s.Client;var other=await Staff();using var oc=other.Client;using var a=new Authenticator();var create=await Options(c);await Finish(c,create,a.Register(create));var o=await Options(c,"verify");var proof=a.Verify(o);var body=new{challengeId=o.GetProperty("challengeId").GetGuid(),credential=proof};
        Assert.Equal(HttpStatusCode.NotFound,(await Post(oc,"/verify/complete",body)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/register/complete",body)).StatusCode);
        var expired=await Options(c,"verify");clock.Now=clock.Now.AddSeconds(121);Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/verify/complete",new{challengeId=expired.GetProperty("challengeId").GetGuid(),credential=a.Verify(expired)})).StatusCode);
        var fresh=await Options(c,"verify");var command=new{challengeId=fresh.GetProperty("challengeId").GetGuid(),credential=a.Verify(fresh)};var results=await Task.WhenAll(Post(c,"/verify/complete",command),Post(c,"/verify/complete",command));Assert.Single(results,r=>r.IsSuccessStatusCode);Assert.Single(results,r=>r.StatusCode==HttpStatusCode.Conflict);
    }
    [Fact]
    public async Task Narrow_admin_owns_keys_without_staff_crud_and_a_foreign_academy_or_guardian_cannot_use_them()
    {
        var s=await Staff(AcademyRole.AcademyAdmin,true);using var c=s.Client;using var a=new Authenticator();var o=await Options(c);var result=await Finish(c,o,a.Register(o));var id=result.GetProperty("credential").GetProperty("id").GetGuid();Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/v1/manage/staff/")).StatusCode);
        Assert.False(StaffPasskeys.IsOwnPath(StaffPasskeys.Path+"/credentials/revoke"));Assert.False(StaffPasskeys.IsOwnPath(StaffPasskeys.Path+"/credentials/not-a-guid/revoke"));Assert.True(StaffPasskeys.IsOwnPath(StaffPasskeys.Path+$"/credentials/{id}/revoke"));
        var g=await Staff(AcademyRole.Guardian);using var guardian=g.Client;Assert.Equal(HttpStatusCode.Forbidden,(await guardian.GetAsync(StaffPasskeys.Path+"/")).StatusCode);
        using var foreign=factory.CreateClient();var csrf=await foreign.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");using var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email=DemoSeed.FutureOwnerEmail,password="Demo-Only-123!"})};login.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());(await foreign.SendAsync(login)).EnsureSuccessStatusCode();
        Assert.Empty((await foreign.GetFromJsonAsync<JsonElement>(StaffPasskeys.Path+"/")).GetProperty("items").EnumerateArray());Assert.Equal(HttpStatusCode.BadRequest,(await Post(foreign,$"/credentials/{id}/revoke",new{})).StatusCode);
        var fc=await foreign.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");using var revoke=new HttpRequestMessage(HttpMethod.Post,StaffPasskeys.Path+$"/credentials/{id}/revoke"){Content=JsonContent.Create(new{})};revoke.Headers.Add("X-CSRF-TOKEN",fc.GetProperty("token").GetString());Assert.Equal(HttpStatusCode.NotFound,(await foreign.SendAsync(revoke)).StatusCode);
    }
}
