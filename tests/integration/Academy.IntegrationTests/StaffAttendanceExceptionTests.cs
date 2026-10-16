using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class StaffAttendanceExceptionTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026,10,16,10,0,0,TimeSpan.Zero);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment("Demo"); b.UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Isolated database required"));
            b.UseSetting("Demo:SeedProfile","LegacyRegression"); b.UseSetting("Demo:SeedEnabled","true"); b.UseSetting("Demo:StaffPassword","Demo-Only-123!"); b.UseSetting("Demo:ReferenceDate","2026-09-28");
            b.ConfigureServices(s=>{s.RemoveAll<TimeProvider>();s.AddSingleton<TimeProvider>(new Clock());});
        }
    }
    private Factory factory = null!;
    public Task InitializeAsync() { factory=new(); _=factory.CreateClient(); return Task.CompletedTask; }
    public Task DisposeAsync() => factory.DisposeAsync().AsTask();
    private async Task<HttpClient> Owner(string email=DemoSeed.OwnerEmail)
    {
        var c=factory.CreateClient(); (await Write(c,"/api/v1/auth/login",new{email,password="Demo-Only-123!"})).EnsureSuccessStatusCode();return c;
    }
    private async Task<(HttpClient Client,Guid Member,Guid Employee,Guid Branch,Guid User)> Staff(AcademyRole role=AcademyRole.Coach,Guid? branch=null)
    {
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        // A policy-specific synthetic branch from another journey is not this fixture's default.
        var home=branch??Academy.Api.Slice2.Slice2DemoSeed.CityBranchId;
        var user=new ApplicationUser{Id=Guid.NewGuid(),DisplayName="موظف استثناء اصطناعي",Email=$"exception.{Guid.NewGuid():N}@example.test",SecurityStamp=Guid.NewGuid().ToString(),UserName=Guid.NewGuid().ToString()};user.NormalizedEmail=user.Email.ToUpperInvariant();
        var member=new AcademyMembership{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,UserId=user.Id,Role=role};
        var employee=new Employee{Id=Guid.NewGuid(),AcademyId=member.AcademyId,MembershipId=member.Id,BranchId=home,Name=user.DisplayName,Phone="015"+Random.Shared.Next(10_000_000,100_000_000),JobTitle="وظيفة عرض",MonthlySalary=1000,Currency="EGP",ContractStart=new(2026,1,1)};
        db.Users.Add(user);db.AcademyMemberships.Add(member);db.Employees.Add(employee);await db.SaveChangesAsync();
        var c=factory.CreateClient();await MobileCoachTestSession.SignInAsync(c,factory.Services,user.Email,role);return(c,member.Id,employee.Id,home,user.Id);
    }
    private static async Task<HttpResponseMessage> Write(HttpClient c,string path,object body,string? key=null)
    {
        var csrf=await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");using var req=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};req.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());if(key is not null)req.Headers.Add("Idempotency-Key",key);return await c.SendAsync(req);
    }
    private static StaffExceptionWrite Claim(Guid branch,string operation="CheckIn",DateTimeOffset? time=null)=>new(branch,operation,time??Now.AddHours(-1),"تعذر الحصول على موقع دقيق — مثال اصطناعي");
    private static async Task<JsonElement> Request(HttpClient c,StaffExceptionWrite body,string? key=null) {var r=await Write(c,StaffAttendanceExceptions.SelfPath+"/",body,key??Guid.NewGuid().ToString());Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());return (await r.Content.ReadFromJsonAsync<JsonElement>());}
    private static async Task<HttpResponseMessage> Review(HttpClient owner,JsonElement r,bool approve=true,string? note=null,string? key=null)=>await Write(owner,$"{StaffAttendanceExceptions.ManagePath}/{r.GetProperty("id").GetGuid()}/review",new StaffExceptionReview(approve,note,r.GetProperty("version").GetUInt32()),key??Guid.NewGuid().ToString());
    private static async Task<HttpResponseMessage> Manual(HttpClient owner, Guid member, DailyWrite body, bool readVersion = true, string? key = null)
    {
        var csrf = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        var path = $"/api/v1/manage/staff-attendance/{member}";
        using var command = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(readVersion ? await DailyAttendanceTestCommand.WithCurrentVersion(owner, path, body) : body) };
        command.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); if (key is not null) command.Headers.Add("Idempotency-Key", key); return await owner.SendAsync(command);
    }

    private static Task<JsonElement> PeriodDay(HttpClient c,Guid member,string date="2026-10-15")=>c.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff-attendance/{member}/periods?date={date}")!;
    private static async Task<HttpResponseMessage> PeriodWrite(HttpClient c,Guid member,WorkPeriodWrite body,Guid? period=null,string? key=null)
    {
        var csrf=await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var r=new HttpRequestMessage(period.HasValue?HttpMethod.Put:HttpMethod.Post,$"/api/v1/manage/staff-attendance/{member}/periods{(period.HasValue?$"/{period}":"")}"){Content=JsonContent.Create(body)};
        r.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());r.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());return await c.SendAsync(r);
    }
    [Fact]
    public async Task Branch_nominated_reviewer_is_live_enforced_without_new_permissions_or_self_review_and_owner_keeps_fallback()
    {
        using var owner=await Owner();
        Guid branch;await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var b=new Academy.Infrastructure.Structure.Branch{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,ArabicName=$"فرع مسؤول الاستثناءات {Guid.NewGuid():N}"};db.Branches.Add(b);await db.SaveChangesAsync();branch=b.Id;}
        var subject=await Staff(branch:branch);using var coach=subject.Client;var one=await Staff(AcademyRole.AcademyAdmin,branch);using var a=one.Client;var two=await Staff(AcademyRole.AcademyAdmin,branch);using var bclient=two.Client;
        async Task SetReviewer(Guid member)
        {
            var path=$"/api/v1/manage/branches/{branch}/check-in-site";var data=await owner.GetFromJsonAsync<JsonElement>(path);var csrf=await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");using var req=new HttpRequestMessage(HttpMethod.Put,path){Content=JsonContent.Create(new CheckInSiteWrite(null,null,new(false,50,30,30,120,true,true,false,member),data.GetProperty("version").GetUInt32()))};req.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());var response=await owner.SendAsync(req);Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        }
        await SetReviewer(one.Member);var first=await Request(coach,Claim(branch));var path=$"{StaffAttendanceExceptions.ManagePath}/{first.GetProperty("id").GetGuid()}";
        Assert.True((await a.GetFromJsonAsync<JsonElement>(path)).GetProperty("canReview").GetBoolean());Assert.False((await bclient.GetFromJsonAsync<JsonElement>(path)).GetProperty("canReview").GetBoolean());Assert.Equal(HttpStatusCode.Forbidden,(await Review(bclient,first)).StatusCode);
        // A detail read never reserves review rights: changing the policy is rechecked at decision time.
        await SetReviewer(two.Member);Assert.Equal(HttpStatusCode.Forbidden,(await Review(a,first)).StatusCode);(await Review(bclient,first)).EnsureSuccessStatusCode();
        var own=await Request(bclient,Claim(branch));Assert.Equal(HttpStatusCode.Forbidden,(await Review(bclient,own)).StatusCode);(await Review(owner,own)).EnsureSuccessStatusCode();
        var rejected=await Request(a,Claim(branch));(await Review(owner,rejected,false,"رفض اصطناعي للتأكد من البديل")).EnsureSuccessStatusCode();
        await using var verify=factory.Services.CreateAsyncScope();var check=verify.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(2,await check.StaffDailyAttendances.CountAsync(d=>d.MembershipId==subject.Member||d.MembershipId==two.Member));Assert.False(await check.StaffDailyAttendances.AnyAsync(d=>d.MembershipId==one.Member));
    }
    [Fact]
    public async Task Second_shift_claim_preserves_original_and_checkout_binds_exact_child_with_one_daily_deduction()
    {
        var s=await Staff();using var c=s.Client;using var owner=await Owner();var date=new DateOnly(2026,10,15);
        (await Manual(owner,s.Member,new(date,"Present",new(8,0),new(10,0),25,"خصم إداري محفوظ"))).EnsureSuccessStatusCode();
        var first=await Request(c,Claim(s.Branch,time:DateTimeOffset.Parse("2026-10-15T08:00:00Z")));
        Assert.NotNull(first.GetProperty("targetAttendanceId").GetString());Assert.Equal(HttpStatusCode.Forbidden,(await Review(c,first)).StatusCode);(await Review(owner,first)).EnsureSuccessStatusCode();
        var day=await PeriodDay(owner,s.Member);Assert.Equal(2,day.GetProperty("items").GetArrayLength());Assert.Equal("08:00:00",day.GetProperty("items")[0].GetProperty("startTime").GetString());
        var open=(await c.GetFromJsonAsync<JsonElement>(StaffAttendanceExceptions.SelfPath+"/options")).GetProperty("openAttendance");
        var body=Claim(s.Branch,"CheckOut",DateTimeOffset.Parse("2026-10-15T09:00:00Z")) with{TargetAttendanceId=open.GetProperty("id").GetGuid(),TargetWorkPeriodId=open.GetProperty("targetWorkPeriodId").GetGuid(),TargetWorkPeriodVersion=open.GetProperty("targetWorkPeriodVersion").GetUInt32()};
        Assert.Equal(HttpStatusCode.NotFound,(await Write(c,StaffAttendanceExceptions.SelfPath+"/",body with{TargetWorkPeriodId=Guid.NewGuid()},Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Write(c,StaffAttendanceExceptions.SelfPath+"/",body with{TargetWorkPeriodVersion=1},Guid.NewGuid().ToString())).StatusCode);
        var key=Guid.NewGuid().ToString();var claim=await Request(c,body,key);var id=claim.GetProperty("id").GetGuid();Assert.Equal(body.TargetWorkPeriodId,claim.GetProperty("targetPeriod").GetProperty("id").GetGuid());
        using(var foreign=await Owner(DemoSeed.FutureOwnerEmail)){Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync($"{StaffAttendanceExceptions.ManagePath}/{id}")).StatusCode);}
        var reviewKey=Guid.NewGuid().ToString();(await Review(owner,claim,key:reviewKey)).EnsureSuccessStatusCode();(await Review(owner,claim,key:reviewKey)).EnsureSuccessStatusCode();
        var third=await Request(c,Claim(s.Branch,time:DateTimeOffset.Parse("2026-10-15T11:00:00Z")));(await Review(owner,third)).EnsureSuccessStatusCode();Assert.Equal("Approved",(await Request(c,body,key)).GetProperty("status").GetString());
        day=await PeriodDay(owner,s.Member);Assert.Equal(3,day.GetProperty("items").GetArrayLength());Assert.Null(day.GetProperty("items")[2].GetProperty("endTime").GetString());Assert.Equal("12:00:00",day.GetProperty("items")[1].GetProperty("endTime").GetString());
        var report=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/staff-daily?coachId={s.Member}&allDates=true");Assert.Equal(1,report.GetProperty("totalCount").GetInt32());Assert.Equal(25,report.GetProperty("totalDeductions").GetDecimal());Assert.Equal(3600,report.GetProperty("items")[0].GetProperty("workPeriods").GetProperty("knownWorkedSeconds").GetInt64());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(4,await db.StaffAttendanceChanges.CountAsync(a=>a.MembershipId==s.Member));
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StaffAttendanceExceptions\" SET \"TargetWorkPeriodVersion\"=1 WHERE \"Id\"={id}"))).SqlState);
    }
    [Fact]
    public async Task Bound_child_change_and_closed_month_reject_review_without_partial_period_or_evidence()
    {
        var s=await Staff();using var c=s.Client;using var owner=await Owner();var date=new DateOnly(2026,10,15);
        (await Manual(owner,s.Member,new(date,"Present",new(8,0),new(10,0),0,null))).EnsureSuccessStatusCode();var first=await Request(c,Claim(s.Branch,time:DateTimeOffset.Parse("2026-10-15T11:00:00Z")));(await Review(owner,first)).EnsureSuccessStatusCode();
        var day=await PeriodDay(owner,s.Member);var p=day.GetProperty("items")[1];var body=Claim(s.Branch,"CheckOut",DateTimeOffset.Parse("2026-10-16T06:00:00Z")) with{TargetAttendanceId=first.GetProperty("targetAttendanceId").GetGuid(),TargetWorkPeriodId=p.GetProperty("id").GetGuid(),TargetWorkPeriodVersion=p.GetProperty("version").GetUInt32()};
        var pending=await Request(c,body);(await PeriodWrite(owner,s.Member,new(date,day.GetProperty("version").GetUInt32(),new(15,0),null,null,p.GetProperty("version").GetUInt32(),"تصحيح بداية الوردية الاصطناعية"),p.GetProperty("id").GetGuid())).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Review(owner,pending)).StatusCode);(await Review(owner,pending,false,"تغيرت الفترة؛ يعاد إرسال الطلب الصحيح")).EnsureSuccessStatusCode();day=await PeriodDay(owner,s.Member);p=day.GetProperty("items")[1];var newer=await Request(c,body with{TargetWorkPeriodVersion=p.GetProperty("version").GetUInt32()});
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var actor=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync();var count=await db.StaffAttendanceChanges.CountAsync(a=>a.MembershipId==s.Member);
        var academy=await db.Academies.SingleAsync(a=>a.Id==DemoSeed.NogoomAcademyId);var originalZone=academy.TimeZone;academy.TimeZone="UTC";await db.SaveChangesAsync();try{Assert.Equal(HttpStatusCode.Conflict,(await Review(owner,newer)).StatusCode);}finally{academy.TimeZone=originalZone;await db.SaveChangesAsync();}
        var membership=await db.AcademyMemberships.SingleAsync(m=>m.Id==s.Member);membership.IsActive=false;await db.SaveChangesAsync();try{Assert.Equal(HttpStatusCode.Conflict,(await Review(owner,newer)).StatusCode);}finally{membership.IsActive=true;await db.SaveChangesAsync();}
        db.PayrollSettlements.Add(new(){Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,EmployeeId=s.Employee,Month=new(2026,10,1),Currency="EGP",PaidAtUtc=Now,PaidByUserId=actor,IdempotencyKey=Guid.NewGuid().ToString()});await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict,(await Review(owner,newer)).StatusCode);Assert.Equal(count,await db.StaffAttendanceChanges.CountAsync(a=>a.MembershipId==s.Member));var periodId=p.GetProperty("id").GetGuid();Assert.Null((await db.StaffWorkPeriods.AsNoTracking().SingleAsync(a=>a.Id==periodId)).EndTime);
    }
    [Fact]
    public async Task Two_second_shift_claims_cannot_open_two_periods_and_target_day_changes_are_not_overwritten()
    {
        var s=await Staff();using var c=s.Client;using var owner=await Owner();using var rival=await Owner();var date=new DateOnly(2026,10,15);(await Manual(owner,s.Member,new(date,"Present",new(8,0),new(10,0),0,null))).EnsureSuccessStatusCode();
        var a=await Request(c,Claim(s.Branch,time:DateTimeOffset.Parse("2026-10-15T11:00:00Z")));var b=await Request(c,Claim(s.Branch,time:DateTimeOffset.Parse("2026-10-15T12:00:00Z")));var results=await Task.WhenAll(Review(owner,a),Review(rival,b));Assert.Single(results,x=>x.IsSuccessStatusCode);Assert.Single(results,x=>x.StatusCode==HttpStatusCode.Conflict);
        var day=await PeriodDay(owner,s.Member);Assert.Equal(2,day.GetProperty("items").GetArrayLength());Assert.Single(day.GetProperty("items").EnumerateArray(),x=>x.GetProperty("endTime").ValueKind==JsonValueKind.Null);Assert.Equal("08:00:00",day.GetProperty("items")[0].GetProperty("startTime").GetString());
    }
    [Fact]
    public async Task Preserved_periods_keep_gaps_legacy_uncertainty_daily_deduction_and_immutable_evidence_consistent()
    {
        var s=await Staff();using var c=s.Client;using var owner=await Owner();using var foreign=await Owner(DemoSeed.FutureOwnerEmail);var date=new DateOnly(2026,10,15);
        (await Manual(owner,s.Member,new(date,"Present",new(8,0),new(10,0),50,"خصم إداري مرة واحدة"))).EnsureSuccessStatusCode();
        var day=await PeriodDay(owner,s.Member);Assert.Equal("LegacyLocal",day.GetProperty("items")[0].GetProperty("trustSource").GetString());Assert.Equal(JsonValueKind.Null,day.GetProperty("totalWorkedSeconds").ValueKind);
        var body=new WorkPeriodWrite(date,day.GetProperty("version").GetUInt32(),new(14,0),new(16,0),date);var key=Guid.NewGuid().ToString();var result=await PeriodWrite(owner,s.Member,body,key:key);Assert.True(result.IsSuccessStatusCode,await result.Content.ReadAsStringAsync());
        day=await PeriodDay(owner,s.Member);Assert.Equal(2,day.GetProperty("items").GetArrayLength());Assert.Equal(7200,day.GetProperty("knownWorkedSeconds").GetInt64());Assert.Equal(JsonValueKind.Null,day.GetProperty("totalWorkedSeconds").ValueKind);
        var first=day.GetProperty("items")[0];Assert.Equal(JsonValueKind.Null,first.GetProperty("startUtc").ValueKind);Assert.Equal("08:00:00",first.GetProperty("startTime").GetString());Assert.Equal("10:00:00",first.GetProperty("endTime").GetString());
        var verify=new WorkPeriodWrite(date,day.GetProperty("version").GetUInt32(),new(8,0),new(10,0),date,first.GetProperty("version").GetUInt32(),"أكدت الإدارة وقت الفترة الأصلية الاصطناعية");
        (await PeriodWrite(owner,s.Member,verify,first.GetProperty("id").GetGuid())).EnsureSuccessStatusCode();day=await PeriodDay(owner,s.Member);Assert.Equal(14400,day.GetProperty("totalWorkedSeconds").GetInt64());Assert.Equal("Africa/Cairo",day.GetProperty("items")[0].GetProperty("timeZoneId").GetString());
        (await PeriodWrite(owner,s.Member,body,key:key)).EnsureSuccessStatusCode();Assert.Equal(2,(await PeriodDay(owner,s.Member)).GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict,(await Manual(owner,s.Member,new(date,"Absent",null,null,50,"تغيير غير مسموح"))).StatusCode);
        var report=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/staff-daily?coachId={s.Member}&allDates=true");Assert.Equal(1,report.GetProperty("totalCount").GetInt32());Assert.Equal(50,report.GetProperty("totalDeductions").GetDecimal());var row=report.GetProperty("items")[0];Assert.Equal("08:00:00",row.GetProperty("checkIn").GetString());Assert.Equal("16:00:00",row.GetProperty("checkOut").GetString());Assert.Equal(14400,row.GetProperty("workPeriods").GetProperty("totalWorkedSeconds").GetInt64());
        var csv=await owner.GetStringAsync($"/api/v1/reports/staff-daily/export?coachId={s.Member}&allDates=true");Assert.Contains("عدد الفترات المحفوظة",csv);Assert.Contains("240",csv);
        var own=await c.GetFromJsonAsync<JsonElement>("/api/v1/staff/work-periods?date=2026-10-15");Assert.Equal(2,own.GetProperty("items").GetArrayLength());Assert.False(own.GetProperty("canEdit").GetBoolean());Assert.DoesNotContain("deduction",own.GetRawText(),StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("salary",own.GetRawText(),StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync($"/api/v1/manage/staff-attendance/{s.Member}/periods?date=2026-10-15")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await PeriodWrite(foreign,s.Member,body)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await PeriodWrite(c,s.Member,body)).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var period=await db.StaffWorkPeriods.SingleAsync(p=>p.MembershipId==s.Member&&p.StartTime==new TimeOnly(14,0));var oldVersion=period.Version;
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"StaffWorkPeriods\" WHERE \"Id\"={period.Id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StaffWorkPeriods\" SET \"StartTime\"='14:30',\"StartUtc\"=\"StartUtc\"+interval '30 minutes' WHERE \"Id\"={period.Id}"))).SqlState);
        Assert.Equal(oldVersion,await db.StaffWorkPeriods.AsNoTracking().Where(p=>p.Id==period.Id).Select(p=>p.Version).SingleAsync());
        var history=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff-attendance/{s.Member}/history?date=2026-10-15");Assert.Equal(3,history.GetProperty("total").GetInt32());Assert.Equal(2,history.GetProperty("items")[0].GetProperty("after").GetProperty("periods").GetArrayLength());Assert.Contains("أكدت الإدارة",history.GetProperty("items")[0].GetProperty("after").GetProperty("changeReason").GetString());
        (await Manual(owner,s.Member,new(date,"Present",new(8,0),new(16,0),75,"تعديل خصم اليوم فقط",date))).EnsureSuccessStatusCode();Assert.Equal(2,(await PeriodDay(owner,s.Member)).GetProperty("items").GetArrayLength());
    }
    [Fact]
    public async Task Preserved_periods_reject_overlap_future_stale_or_closed_sources_and_keep_overnight_open_until_explicit_close()
    {
        var s=await Staff();using var c=s.Client;using var owner=await Owner();var date=new DateOnly(2026,10,15);(await Manual(owner,s.Member,new(date,"Present",new(8,0),new(10,0),0,null))).EnsureSuccessStatusCode();
        var day=await PeriodDay(owner,s.Member);var v=day.GetProperty("version").GetUInt32();var body=new WorkPeriodWrite(date,v,new(18,0),null,null);var key=Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await PeriodWrite(owner,s.Member,body with{Date=new(2026,10,17)})).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await PeriodWrite(owner,s.Member,body with{StartTime=new(9,0),EndTime=new(11,0),EndDate=date})).StatusCode);
        var parallel=await Task.WhenAll(PeriodWrite(owner,s.Member,body,key:key),PeriodWrite(owner,s.Member,body,key:key));Assert.All(parallel,r=>Assert.True(r.IsSuccessStatusCode));day=await PeriodDay(owner,s.Member);Assert.Equal(2,day.GetProperty("items").GetArrayLength());Assert.Null(day.GetProperty("totalWorkedSeconds").Deserialize<long?>());
        Assert.Equal(HttpStatusCode.Conflict,(await PeriodWrite(owner,s.Member,body,key:Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await PeriodWrite(owner,s.Member,body with{Version=day.GetProperty("version").GetUInt32(),StartTime=new(19,0)})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Manual(owner,s.Member,new(new(2026,10,16),"Present",new(12,0),null,0,null))).StatusCode);
        var p=day.GetProperty("items")[1];var close=body with{Version=day.GetProperty("version").GetUInt32(),PeriodVersion=p.GetProperty("version").GetUInt32(),EndDate=new(2026,10,16),EndTime=new(10,0),Reason="انصراف ليلي إداري اصطناعي"};
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await PeriodWrite(owner,s.Member,close with{Reason=null},p.GetProperty("id").GetGuid())).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await PeriodWrite(owner,s.Member,close,Guid.NewGuid())).StatusCode);
        (await PeriodWrite(owner,s.Member,close,p.GetProperty("id").GetGuid())).EnsureSuccessStatusCode();day=await PeriodDay(owner,s.Member);Assert.Equal("2026-10-16",day.GetProperty("items")[1].GetProperty("endDate").GetString());Assert.Equal(57600,day.GetProperty("knownWorkedSeconds").GetInt64());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var actor=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync();db.PayrollSettlements.Add(new(){Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,EmployeeId=s.Employee,Month=new(2026,10,1),Currency="EGP",PaidAtUtc=Now,PaidByUserId=actor,IdempotencyKey=Guid.NewGuid().ToString()});await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict,(await PeriodWrite(owner,s.Member,new(date,day.GetProperty("version").GetUInt32(),new(12,0),new(14,0),date))).StatusCode);(await PeriodWrite(owner,s.Member,body,key:key)).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Managed_day_cannot_be_closed_by_an_old_exception_or_client_biometric_boolean()
    {
        var s=await Staff();using var c=s.Client;using var owner=await Owner();var date=new DateOnly(2026,10,16);
        (await Manual(owner,s.Member,new(date,"Present",new(8,0),null,0,null))).EnsureSuccessStatusCode();var pending=await Request(c,Claim(s.Branch,"CheckOut",Now.AddHours(-4)));
        (await Manual(owner,s.Member,new(date,"Present",new(8,0),new(10,0),0,null))).EnsureSuccessStatusCode();var day=await PeriodDay(owner,s.Member,"2026-10-16");(await PeriodWrite(owner,s.Member,new(date,day.GetProperty("version").GetUInt32(),new(11,0),null,null))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Review(owner,pending)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await Write(c,StaffAttendanceExceptions.SelfPath+"/",Claim(s.Branch,"CheckOut"),Guid.NewGuid().ToString())).StatusCode);
        var options=await c.GetFromJsonAsync<JsonElement>(StaffAttendanceExceptions.SelfPath+"/options");Assert.False(options.GetProperty("requiresSourceReview").GetBoolean());Assert.NotEqual(Guid.Empty,options.GetProperty("openAttendance").GetProperty("targetWorkPeriodId").GetGuid());
        day=await PeriodDay(owner,s.Member,"2026-10-16");var openPeriod=day.GetProperty("items")[1];
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await PeriodWrite(owner,s.Member,new(date,day.GetProperty("version").GetUInt32(),new(11,0),new(14,0),date,openPeriod.GetProperty("version").GetUInt32(),"محاولة انصراف مستقبلي مرفوضة"),openPeriod.GetProperty("id").GetGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await PeriodWrite(owner,s.Member,new(date,day.GetProperty("version").GetUInt32(),new(14,0),null,null))).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var branch=await db.Branches.SingleAsync(b=>b.Id==s.Branch);var lat=branch.CheckInLatitude;var lon=branch.CheckInLongitude;branch.CheckInLatitude=30;branch.CheckInLongitude=31;var group=await db.TrainingGroups.Where(g=>g.BranchId==s.Branch&&g.IsActive).Select(g=>g.Id).FirstAsync();var assignment=new Academy.Infrastructure.Structure.StaffGroupAssignment{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,AcademyMembershipId=s.Member,TrainingGroupId=group};db.StaffGroupAssignments.Add(assignment);await db.SaveChangesAsync();
        try{Assert.Equal(HttpStatusCode.Conflict,(await Write(c,"/api/v1/staff/check-in/",new DeviceCheckInWrite(s.Branch,30,31,0,true))).StatusCode);Assert.Null((await PeriodDay(owner,s.Member,"2026-10-16")).GetProperty("items")[1].GetProperty("endTime").GetString());}
        finally{branch.CheckInLatitude=lat;branch.CheckInLongitude=lon;await db.SaveChangesAsync();await db.StaffGroupAssignments.Where(a=>a.Id==assignment.Id).ExecuteDeleteAsync();}
    }

    [Fact]
    public async Task Period_permissions_timezone_and_csrf_are_rechecked_without_widening_own_history()
    {
        var s=await Staff();using var c=s.Client;using var owner=await Owner();var a=await Staff(AcademyRole.AcademyAdmin,s.Branch);using var admin=a.Client;var g=await Staff(AcademyRole.Guardian);using var guardian=g.Client;
        var date=new DateOnly(2026,10,15);(await Manual(owner,s.Member,new(date,"Present",new(8,0),new(10,0),0,null))).EnsureSuccessStatusCode();var day=await PeriodDay(owner,s.Member);var body=new WorkPeriodWrite(date,day.GetProperty("version").GetUInt32(),new(14,0),new(16,0),date);(await PeriodWrite(owner,s.Member,body)).EnsureSuccessStatusCode();day=await PeriodDay(owner,s.Member);body=body with{Version=day.GetProperty("version").GetUInt32(),StartTime=new(17,0),EndTime=new(18,0)};
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var member=await db.AcademyMemberships.SingleAsync(m=>m.Id==a.Member);member.UseActionGrants=true;member.AllBranches=false;db.MembershipBranchAccesses.Add(new(){Id=Guid.NewGuid(),AcademyId=member.AcademyId,MembershipId=member.Id,BranchId=s.Branch});db.StaffActionGrants.Add(new(){Id=Guid.NewGuid(),AcademyId=member.AcademyId,MembershipId=member.Id,Module="staff",Read=true,Edit=false});await db.SaveChangesAsync();
        Assert.False((await PeriodDay(admin,s.Member)).GetProperty("canEdit").GetBoolean());Assert.Equal(HttpStatusCode.Forbidden,(await PeriodWrite(admin,s.Member,body)).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync($"/api/v1/manage/staff-attendance/{s.Member}/periods",body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await guardian.GetAsync("/api/v1/staff/work-periods?date=2026-10-15")).StatusCode);var own=await admin.GetFromJsonAsync<JsonElement>($"/api/v1/staff/work-periods?date=2026-10-15&membershipId={s.Member}");Assert.Empty(own.GetProperty("items").EnumerateArray());
        var academy=await db.Academies.SingleAsync(x=>x.Id==DemoSeed.NogoomAcademyId);var previous=academy.TimeZone;academy.TimeZone="America/New_York";await db.SaveChangesAsync();
        try {Assert.Equal(HttpStatusCode.Conflict,(await PeriodWrite(owner,s.Member,body)).StatusCode);Assert.Equal(HttpStatusCode.UnprocessableEntity,(await PeriodWrite(owner,s.Member,body with{Date=new(2025,11,2),StartTime=new(1,30),EndTime=null,EndDate=null})).StatusCode);Assert.Equal(HttpStatusCode.UnprocessableEntity,(await PeriodWrite(owner,s.Member,body with{Date=new(2026,3,8),StartTime=new(2,30),EndTime=null,EndDate=null})).StatusCode);Assert.Equal("Africa/Cairo",(await PeriodDay(owner,s.Member)).GetProperty("items")[1].GetProperty("timeZoneId").GetString());}
        finally {academy.TimeZone=previous;await db.SaveChangesAsync();}
    }
    [Fact]
    public async Task Manual_source_versions_block_lost_updates_and_replays_preserve_original_evidence_and_daily_deduction()
    {
        var staff = await Staff(); using var coach = staff.Client; using var owner = await Owner(); using var foreign = await Owner(DemoSeed.FutureOwnerEmail);
        var day = new DateOnly(2026,10,15); var path = $"/api/v1/manage/staff-attendance/{staff.Member}";
        var empty = await owner.GetFromJsonAsync<JsonElement>($"{path}?date={day:yyyy-MM-dd}"); Assert.Equal(0u, empty.GetProperty("version").GetUInt32());
        var original = new DailyWrite(day,"Present",new(8,0),new(10,0),50,"خصم إداري اصطناعي مستقل"); var key = Guid.NewGuid().ToString();
        var saved = await Manual(owner,staff.Member,original,false,key); saved.EnsureSuccessStatusCode(); var first = await saved.Content.ReadFromJsonAsync<JsonElement>(); var version = first.GetProperty("version").GetUInt32(); Assert.True(version>0);
        var a = original with { Version = version, Status="Absent", CheckIn=null, CheckOut=null, Reason="غياب اصطناعي معتمد" };
        var b = original with { Version = version, CheckIn=new(7,0), Reason="تصحيح وقت الحضور اصطناعي" };
        var concurrent = await Task.WhenAll(Manual(owner,staff.Member,a,false,Guid.NewGuid().ToString()),Manual(owner,staff.Member,b,false,Guid.NewGuid().ToString()));
        Assert.Single(concurrent,r=>r.IsSuccessStatusCode); Assert.Single(concurrent,r=>r.StatusCode==HttpStatusCode.Conflict);
        var current = await owner.GetFromJsonAsync<JsonElement>($"{path}?date={day:yyyy-MM-dd}"); Assert.NotEqual(version,current.GetProperty("version").GetUInt32());
        var replay = await Manual(owner,staff.Member,original,false,key); replay.EnsureSuccessStatusCode(); Assert.Equal(version,(await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetUInt32());
        Assert.Equal(HttpStatusCode.Conflict,(await Manual(owner,staff.Member,original with { Reason="محتوى آخر" },false,key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Manual(owner,staff.Member,original,false)).StatusCode); // old clients must read before replacing a row
        var history = await owner.GetFromJsonAsync<JsonElement>($"{path}/history?date={day:yyyy-MM-dd}"); Assert.Equal(2,history.GetProperty("total").GetInt32());
        var items = history.GetProperty("items").EnumerateArray().ToArray(); Assert.Contains(items,c=>c.GetProperty("before").ValueKind==JsonValueKind.Null); Assert.Contains(items,c=>c.GetProperty("beforeVersion").GetUInt32()==version && c.GetProperty("before").GetProperty("checkIn").GetString()=="08:00:00");
        Assert.All(items,c=>Assert.Equal(c.GetProperty("afterVersion").GetUInt32(),c.GetProperty("after").GetProperty("version").GetUInt32()));
        var report = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/staff-daily?coachId={staff.Member}&allDates=true"); Assert.Equal(1,report.GetProperty("totalCount").GetInt32()); Assert.Equal(50,report.GetProperty("totalDeductions").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync($"{path}/history?date={day:yyyy-MM-dd}")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden,(await coach.GetAsync($"{path}/history?date={day:yyyy-MM-dd}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PutAsJsonAsync(path,original with {Version=current.GetProperty("version").GetUInt32()})).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var evidence=await db.StaffAttendanceChanges.FirstAsync(c=>c.MembershipId==staff.Member);
        var source=await db.StaffDailyAttendances.AsNoTracking().SingleAsync(r=>r.MembershipId==staff.Member);
        db.PayrollSettlements.Add(new(){Id=Guid.NewGuid(),AcademyId=source.AcademyId,EmployeeId=staff.Employee,Month=new(2026,10,1),Currency="EGP",PaidAtUtc=Now,PaidByUserId=evidence.ActorUserId,IdempotencyKey=Guid.NewGuid().ToString()}); await db.SaveChangesAsync();
        var noChange=new DailyWrite(day,source.Status,source.CheckIn,source.CheckOut,source.ApprovedDeduction,source.DeductionReason,source.CheckOutDate,source.Version);
        Assert.Equal(HttpStatusCode.Conflict,(await Manual(owner,staff.Member,noChange,false,Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Manual(owner,staff.Member,noChange with {Reason="سبب مختلف بعد الاعتماد"},false,Guid.NewGuid().ToString())).StatusCode);
        (await Manual(owner,staff.Member,original,false,key)).EnsureSuccessStatusCode();
        var closed=await db.StaffDailyAttendances.AsNoTracking().SingleAsync(r=>r.MembershipId==staff.Member); Assert.Equal(source.Version,closed.Version); Assert.Equal(source.UpdatedAtUtc,closed.UpdatedAtUtc); Assert.Equal(2,await db.StaffAttendanceChanges.CountAsync(r=>r.MembershipId==staff.Member));
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StaffAttendanceChanges\" SET \"BeforeVersion\"=0 WHERE \"Id\"={evidence.Id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"StaffAttendanceChanges\" WHERE \"Id\"={evidence.Id}"))).SqlState);
        evidence.BeforeSnapshot="null"; evidence.RequestHash="changed"; await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
    }

    [Fact]
    public async Task Manual_history_and_replay_recheck_branch_and_edit_authority_and_hidden_keys_roll_back_the_source()
    {
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var branches=await db.Branches.Where(b=>b.AcademyId==DemoSeed.NogoomAcademyId && b.IsActive).Select(b=>b.Id).Take(2).ToArrayAsync();
        var a=await Staff(branch:branches[0]); var b=await Staff(branch:branches[1]); var manager=await Staff(AcademyRole.AcademyAdmin,branches[0]); using var c=manager.Client; using var ac=a.Client; using var bc=b.Client;
        var membership=await db.AcademyMemberships.SingleAsync(m=>m.Id==manager.Member); membership.AllBranches=false; membership.UseActionGrants=true;
        var access=new MembershipBranchAccess{Id=Guid.NewGuid(),AcademyId=membership.AcademyId,MembershipId=membership.Id,BranchId=branches[0]}; db.MembershipBranchAccesses.Add(access);
        var grant=new StaffActionGrant{Id=Guid.NewGuid(),AcademyId=membership.AcademyId,MembershipId=membership.Id,Module=StaffAccessRules.ForPath("/api/v1/manage/staff-attendance")!,Read=true,Edit=false}; db.StaffActionGrants.Add(grant); await db.SaveChangesAsync();
        var body=new DailyWrite(new(2026,10,15),"Present",new(8,0),new(10,0),0,null); var key=Guid.NewGuid().ToString(); var path=$"/api/v1/manage/staff-attendance/{a.Member}";
        Assert.Equal(HttpStatusCode.Forbidden,(await Manual(c,a.Member,body,false,key)).StatusCode); Assert.False(await db.StaffDailyAttendances.AnyAsync(r=>r.MembershipId==a.Member));
        grant.Edit=true; await db.SaveChangesAsync(); (await Manual(c,a.Member,body,false,key)).EnsureSuccessStatusCode();
        Assert.Equal(1,(await c.GetFromJsonAsync<JsonElement>($"{path}/history?date=2026-10-15")).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync($"/api/v1/manage/staff-attendance/{b.Member}/history?date=2026-10-15")).StatusCode);
        access.IsActive=false; db.MembershipBranchAccesses.Add(new(){Id=Guid.NewGuid(),AcademyId=membership.AcademyId,MembershipId=membership.Id,BranchId=branches[1]}); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync($"{path}/history?date=2026-10-15")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Manual(c,a.Member,body,false,key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Manual(c,b.Member,body,false,key)).StatusCode); Assert.False(await db.StaffDailyAttendances.AnyAsync(r=>r.MembershipId==b.Member));
        Assert.Equal(1,await db.StaffAttendanceChanges.CountAsync(r=>r.ActorUserId==manager.User));
        // A forged evidence payload cannot be inserted even by bypassing EF guards.
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"StaffAttendanceChanges\" (\"Id\",\"AttendanceId\",\"MembershipId\",\"ActorMembershipId\",\"ActorUserId\",\"WorkDate\",\"BeforeVersion\",\"AfterVersion\",\"RequestKey\",\"RequestHash\",\"BeforeSnapshot\",\"AfterSnapshot\",\"AcademyId\",\"IsActive\",\"CreatedAtUtc\",\"UpdatedAtUtc\") SELECT {Guid.NewGuid()},\"AttendanceId\",\"MembershipId\",\"ActorMembershipId\",\"ActorUserId\",\"WorkDate\",\"BeforeVersion\",\"AfterVersion\",{Guid.NewGuid().ToString()},\"RequestHash\",\"BeforeSnapshot\",jsonb_set(\"AfterSnapshot\",'{{status}}','\"Absent\"'),\"AcademyId\",\"IsActive\",\"CreatedAtUtc\",\"UpdatedAtUtc\" FROM \"StaffAttendanceChanges\" WHERE \"ActorUserId\"={manager.User}"))).SqlState);
    }

    [Fact]
    public async Task Pending_claim_review_and_checkout_use_existing_attendance_with_no_automatic_financial_or_player_effect()
    {
        using var owner=await Owner();var staff=await Staff();using var c=staff.Client;var key=Guid.NewGuid().ToString();var body=Claim(staff.Branch);
        var r=await Request(c,body,key);Assert.Equal("Pending",r.GetProperty("status").GetString());var replay=await Request(c,body,key);Assert.Equal(r.GetProperty("id").GetGuid(),replay.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict,(await Write(c,StaffAttendanceExceptions.SelfPath+"/",body with{Reason="سبب آخر مختلف"},key)).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var before=(await db.Collections.CountAsync(),await db.PayrollAdjustments.CountAsync(),await db.PlayerAttendances.CountAsync());Assert.False(await db.StaffDailyAttendances.AnyAsync(a=>a.MembershipId==staff.Member));
        var reviewKey=Guid.NewGuid().ToString();var accepted=await Review(owner,r,key:reviewKey);Assert.True(accepted.IsSuccessStatusCode,await accepted.Content.ReadAsStringAsync());(await Review(owner,r,key:reviewKey)).EnsureSuccessStatusCode();
        var applied=await db.StaffDailyAttendances.AsNoTracking().SingleAsync(a=>a.MembershipId==staff.Member);Assert.Equal("Present",applied.Status);Assert.Equal(new TimeOnly(12,0),applied.CheckIn);Assert.Null(applied.CheckOut);Assert.Equal(0,applied.ApprovedDeduction);Assert.Equal(0,applied.SuggestedDeduction);
        var outClaim=await Request(c,Claim(staff.Branch,"CheckOut",Now.AddMinutes(-30)));(await Review(owner,outClaim)).EnsureSuccessStatusCode();var after=await db.StaffDailyAttendances.AsNoTracking().SingleAsync(a=>a.Id==applied.Id);Assert.Equal(new TimeOnly(12,30),after.CheckOut);Assert.Equal(applied.CheckIn,after.CheckIn);
        var own=await c.GetFromJsonAsync<JsonElement>(StaffAttendanceExceptions.SelfPath+"/");Assert.All(own.EnumerateArray(),x=>Assert.Equal("Approved",x.GetProperty("status").GetString()));Assert.Equal(before,(await db.Collections.CountAsync(),await db.PayrollAdjustments.CountAsync(),await db.PlayerAttendances.CountAsync()));
        var detail=await owner.GetFromJsonAsync<JsonElement>($"{StaffAttendanceExceptions.ManagePath}/{outClaim.GetProperty("id").GetGuid()}");Assert.Contains("Present",detail.GetProperty("beforeSnapshot").GetString());Assert.Contains("12:30",detail.GetProperty("afterSnapshot").GetString());
    }
    [Fact]
    public async Task Future_bad_source_csrf_and_cross_tenant_are_denied_and_rejection_never_records_attendance()
    {
        using var owner=await Owner();using var foreign=await Owner(DemoSeed.FutureOwnerEmail);var staff=await Staff();using var c=staff.Client;
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Write(c,StaffAttendanceExceptions.SelfPath+"/",Claim(staff.Branch,time:Now.AddSeconds(1)),Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Write(c,StaffAttendanceExceptions.SelfPath+"/",Claim(staff.Branch) with{Reason=""},Guid.NewGuid().ToString())).StatusCode);
        var request=await Request(c,Claim(staff.Branch));var id=request.GetProperty("id").GetGuid();Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync($"{StaffAttendanceExceptions.ManagePath}/{id}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await Review(foreign,request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync($"{StaffAttendanceExceptions.ManagePath}/{id}/review",new StaffExceptionReview(true,null,request.GetProperty("version").GetUInt32()))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Review(owner,request,false)).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();(await db.Employees.SingleAsync(e=>e.Id==staff.Employee)).IsActive=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.Conflict,(await Review(owner,request)).StatusCode);(await Review(owner,request,false,"لم يتم التحقق من الحضور المدعى")).EnsureSuccessStatusCode();Assert.False(await db.StaffDailyAttendances.AnyAsync(a=>a.MembershipId==staff.Member));
        var other=await Staff();using var otherClient=other.Client;var own=await otherClient.GetFromJsonAsync<JsonElement>(StaffAttendanceExceptions.SelfPath+"/");Assert.Empty(own.EnumerateArray());
        var text=(await c.GetStringAsync(StaffAttendanceExceptions.SelfPath+"/"));Assert.Contains("Rejected",text);Assert.DoesNotContain("monthlySalary",text);Assert.DoesNotContain("beforeSnapshot",text);
    }
    [Fact]
    public async Task Staff_can_request_for_self_without_admin_crud_but_cannot_self_review_even_as_admin()
    {
        var s=await Staff(AcademyRole.AcademyAdmin);using var c=s.Client;using var owner=await Owner();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var m=await db.AcademyMemberships.SingleAsync(m=>m.Id==s.Member);m.UseActionGrants=true;
        db.StaffActionGrants.Add(new(){Id=Guid.NewGuid(),AcademyId=m.AcademyId,MembershipId=m.Id,Module="staff",Read=false,Add=false,Edit=false});await db.SaveChangesAsync();
        var r=await Request(c,Claim(s.Branch));Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(StaffAttendanceExceptions.ManagePath+"/")).StatusCode);
        var employee=await db.Employees.SingleAsync(e=>e.Id==s.Employee);var csrf=await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");using var relink=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/manage/employees/{s.Employee}"){Content=JsonContent.Create(new EmployeeWrite(employee.Name,employee.Phone,employee.JobTitle,employee.BranchId,employee.MonthlySalary,employee.ContractStart,employee.ContractEnd,null,null))};relink.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());Assert.Equal(HttpStatusCode.Conflict,(await owner.SendAsync(relink)).StatusCode);Assert.Equal(s.Member,await db.Employees.Where(e=>e.Id==s.Employee).Select(e=>e.MembershipId).SingleAsync());
        var grant=await db.StaffActionGrants.SingleAsync(g=>g.MembershipId==m.Id);grant.Read=true;grant.Edit=true;await db.SaveChangesAsync();
        var detail=await c.GetFromJsonAsync<JsonElement>($"{StaffAttendanceExceptions.ManagePath}/{r.GetProperty("id").GetGuid()}");Assert.False(detail.GetProperty("canReview").GetBoolean());Assert.Equal(HttpStatusCode.Forbidden,(await Review(c,r)).StatusCode);
        db.AcademyMemberships.Add(new(){Id=Guid.NewGuid(),AcademyId=m.AcademyId,UserId=s.User,Role=AcademyRole.AcademyOwner});await db.SaveChangesAsync();using var alias=factory.CreateClient();var email=await db.Users.Where(u=>u.Id==s.User).Select(u=>u.Email).SingleAsync();await MobileCoachTestSession.SignInAsync(alias,factory.Services,email!,AcademyRole.AcademyOwner);Assert.Equal(HttpStatusCode.Forbidden,(await Review(alias,r)).StatusCode);
        (await Review(owner,r)).EnsureSuccessStatusCode();Assert.True(await db.StaffDailyAttendances.AnyAsync(a=>a.MembershipId==m.Id));
        using var coach=factory.CreateClient();await MobileCoachTestSession.SignInAsync(coach,factory.Services);Assert.Equal(HttpStatusCode.Forbidden,(await coach.GetAsync(StaffAttendanceExceptions.ManagePath+"/")).StatusCode);
    }
    [Fact]
    public async Task Scoped_readonly_reviewer_cannot_see_another_branch_or_review_without_edit()
    {
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var branches=new[]{Academy.Api.Slice2.Slice2DemoSeed.CityBranchId,Academy.Api.Slice2.Slice2DemoSeed.FifthBranchId};
        var s=await Staff(branch:branches[0]);var outside=await Staff(branch:branches[1]);using var c=s.Client;using var oc=outside.Client;var a=await Staff(AcademyRole.AcademyAdmin,branches[0]);using var admin=a.Client;
        var m=await db.AcademyMemberships.SingleAsync(m=>m.Id==a.Member);m.AllBranches=false;m.UseActionGrants=true;db.MembershipBranchAccesses.Add(new(){Id=Guid.NewGuid(),AcademyId=m.AcademyId,MembershipId=m.Id,BranchId=branches[0]});var grant=new StaffActionGrant{Id=Guid.NewGuid(),AcademyId=m.AcademyId,MembershipId=m.Id,Module="staff",Read=true,Edit=false};db.StaffActionGrants.Add(grant);await db.SaveChangesAsync();
        var r=await Request(c,Claim(s.Branch));var hidden=await Request(oc,Claim(outside.Branch));var inbox=await admin.GetFromJsonAsync<JsonElement>(StaffAttendanceExceptions.ManagePath+"/");Assert.Contains(inbox.GetProperty("items").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==r.GetProperty("id").GetGuid());Assert.DoesNotContain(inbox.GetProperty("items").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==hidden.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"{StaffAttendanceExceptions.ManagePath}/{hidden.GetProperty("id").GetGuid()}")).StatusCode);var detail=await admin.GetFromJsonAsync<JsonElement>($"{StaffAttendanceExceptions.ManagePath}/{r.GetProperty("id").GetGuid()}");Assert.False(detail.GetProperty("canReview").GetBoolean());Assert.Equal(HttpStatusCode.Forbidden,(await Review(admin,r)).StatusCode);
        grant.Edit=true;await db.SaveChangesAsync();(await Review(admin,r)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await Review(admin,hidden)).StatusCode);
    }
    [Fact]
    public async Task Two_decisions_have_one_winner_and_terminal_source_is_immutable_in_ef_and_sql()
    {
        var s=await Staff();using var c=s.Client;using var a=await Owner();using var b=await Owner();var r=await Request(c,Claim(s.Branch));var responses=await Task.WhenAll(Review(a,r),Review(b,r));Assert.Single(responses,x=>x.IsSuccessStatusCode);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.Conflict);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var id=r.GetProperty("id").GetGuid();var row=await db.StaffAttendanceExceptions.SingleAsync(x=>x.Id==id);Assert.NotNull(row.BeforeSnapshot);Assert.NotNull(row.AfterSnapshot);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"StaffAttendanceExceptions\" WHERE \"Id\"={id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StaffAttendanceExceptions\" SET \"Reason\"='سبب تم تغييره' WHERE \"Id\"={id}"))).SqlState);
        row.ReviewNote="تحرير غير مسموح";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
    }
    [Fact]
    public async Task A_review_key_from_a_now_hidden_branch_cannot_leave_partial_attendance()
    {
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var branches=new[]{Academy.Api.Slice2.Slice2DemoSeed.CityBranchId,Academy.Api.Slice2.Slice2DemoSeed.FifthBranchId};var a=await Staff(AcademyRole.AcademyAdmin,branches[0]);using var admin=a.Client;var first=await Staff(branch:branches[0]);var second=await Staff(branch:branches[1]);using var fc=first.Client;using var sc=second.Client;
        var member=await db.AcademyMemberships.SingleAsync(m=>m.Id==a.Member);member.AllBranches=false;member.UseActionGrants=true;var access=new MembershipBranchAccess{Id=Guid.NewGuid(),AcademyId=member.AcademyId,MembershipId=member.Id,BranchId=branches[0]};db.MembershipBranchAccesses.Add(access);db.StaffActionGrants.Add(new(){Id=Guid.NewGuid(),AcademyId=member.AcademyId,MembershipId=member.Id,Module="staff",Read=true,Edit=true});await db.SaveChangesAsync();
        var original=await Request(fc,Claim(first.Branch));var outside=await Request(sc,Claim(second.Branch));var key=Guid.NewGuid().ToString();(await Review(admin,original,key:key)).EnsureSuccessStatusCode();access.BranchId=branches[1];await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.Conflict,(await Review(admin,outside,key:key)).StatusCode);Assert.False(await db.StaffDailyAttendances.AnyAsync(r=>r.MembershipId==second.Member));var stillPending=await db.StaffAttendanceExceptions.AsNoTracking().SingleAsync(r=>r.Id==outside.GetProperty("id").GetGuid());Assert.Equal("Pending",stillPending.Status);Assert.Null(stillPending.AttendanceId);
    }
    [Fact]
    public async Task An_open_previous_day_blocks_new_exception_and_manual_checkin_without_inventing_checkout()
    {
        var s=await Staff();using var c=s.Client;using var owner=await Owner();var previous=await Request(c,Claim(s.Branch,time:Now.AddDays(-1)));(await Review(owner,previous)).EnsureSuccessStatusCode();var next=await Request(c,Claim(s.Branch));Assert.Equal(HttpStatusCode.Conflict,(await Review(owner,next)).StatusCode);
        var csrf=await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");using var manual=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/manage/staff-attendance/{s.Member}"){Content=JsonContent.Create(new DailyWrite(new(2026,10,16),"Present",new(12,0),null,0,null))};manual.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());Assert.Equal(HttpStatusCode.Conflict,(await owner.SendAsync(manual)).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var rows=await db.StaffDailyAttendances.Where(a=>a.MembershipId==s.Member).ToArrayAsync();Assert.Single(rows);Assert.Null(rows[0].CheckOut);Assert.Equal(new DateOnly(2026,10,15),rows[0].WorkDate);
    }
    [Fact]
    public async Task Overnight_checkout_binds_original_day_and_replays_after_close_without_a_second_daily_record()
    {
        var s = await Staff(); using var c = s.Client; using var owner = await Owner();
        var first = await Request(c, Claim(s.Branch, time: Now.AddHours(-15))); (await Review(owner, first)).EnsureSuccessStatusCode();
        var options = await c.GetFromJsonAsync<JsonElement>(StaffAttendanceExceptions.SelfPath + "/options");
        var source = options.GetProperty("openAttendance"); Assert.Equal("2026-10-15", source.GetProperty("workDate").GetString());
        var key = Guid.NewGuid().ToString(); var body = Claim(s.Branch, "CheckOut", Now.AddHours(-10)) with { TargetAttendanceId = source.GetProperty("id").GetGuid() };
        var claim = await Request(c, body, key); Assert.Equal("2026-10-15", claim.GetProperty("workDate").GetString());
        Assert.Equal(source.GetProperty("id").GetGuid(), claim.GetProperty("targetAttendanceId").GetGuid());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var money = (await db.Collections.CountAsync(), await db.PayrollAdjustments.CountAsync(), await db.PlayerAttendances.CountAsync());
        Assert.Null((await db.StaffDailyAttendances.AsNoTracking().SingleAsync(a => a.MembershipId == s.Member)).CheckOut);
        (await Review(owner, claim)).EnsureSuccessStatusCode();
        var row = await db.StaffDailyAttendances.AsNoTracking().SingleAsync(a => a.MembershipId == s.Member);
        Assert.Equal(source.GetProperty("id").GetGuid(), row.Id); Assert.Equal(new DateOnly(2026,10,15), row.WorkDate); Assert.Equal(new DateOnly(2026,10,16), row.CheckOutDate);
        Assert.Equal(new TimeOnly(22,0), row.CheckIn); Assert.Equal(new TimeOnly(3,0), row.CheckOut); Assert.Equal(0, row.ApprovedDeduction);
        Assert.Equal("Approved", (await Request(c, body, key)).GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, (await c.GetFromJsonAsync<JsonElement>(StaffAttendanceExceptions.SelfPath + "/options")).GetProperty("openAttendance").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(c, StaffAttendanceExceptions.SelfPath + "/", body, Guid.NewGuid().ToString())).StatusCode);
        var report = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/staff-daily?coachId={s.Member}&allDates=true");
        Assert.Equal(1, report.GetProperty("totalCount").GetInt32()); Assert.Equal("2026-10-16", report.GetProperty("items")[0].GetProperty("checkOutDate").GetString());
        var csv = await owner.GetStringAsync($"/api/v1/reports/staff-daily/export?coachId={s.Member}&allDates=true"); Assert.Contains("تاريخ الانصراف", csv); Assert.Contains("2026-10-15", csv); Assert.Contains("2026-10-16", csv);
        Assert.Equal(HttpStatusCode.Conflict,(await Manual(owner,s.Member,new(new(2026,10,16),"Present",new(2,0),new(4,0),0,null))).StatusCode);
        var overlap = await Request(c,Claim(s.Branch,time:DateTimeOffset.Parse("2026-10-15T23:30:00Z"))); Assert.Equal(HttpStatusCode.Conflict,(await Review(owner,overlap)).StatusCode);
        Assert.Equal(money, (await db.Collections.CountAsync(), await db.PayrollAdjustments.CountAsync(), await db.PlayerAttendances.CountAsync()));
    }
    [Fact]
    public async Task Checkout_cannot_target_foreign_or_changed_source_and_stale_pending_can_be_rejected()
    {
        var s = await Staff(); using var c = s.Client; using var owner = await Owner();
        var first = await Request(c, Claim(s.Branch, time: Now.AddHours(-15))); (await Review(owner, first)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await Write(c, StaffAttendanceExceptions.SelfPath + "/", Claim(s.Branch, "CheckOut") with { TargetAttendanceId = Guid.NewGuid() }, Guid.NewGuid().ToString())).StatusCode);
        var other = await Staff(); using var otherClient = other.Client; var otherClaim = await Request(otherClient,Claim(other.Branch,time:Now.AddHours(-15))); (await Review(owner,otherClaim)).EnsureSuccessStatusCode();
        var otherOpen=(await otherClient.GetFromJsonAsync<JsonElement>(StaffAttendanceExceptions.SelfPath+"/options")).GetProperty("openAttendance").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound,(await Write(c,StaffAttendanceExceptions.SelfPath+"/",Claim(s.Branch,"CheckOut") with {TargetAttendanceId=otherOpen},Guid.NewGuid().ToString())).StatusCode);
        var pending = await Request(c, Claim(s.Branch, "CheckOut"));
        (await Manual(owner, s.Member, new(new(2026,10,15), "Present", new(21,0), null, 0, null))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Review(owner, pending)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Null((await db.StaffDailyAttendances.AsNoTracking().SingleAsync(a => a.MembershipId == s.Member)).CheckOut);
        (await Review(owner, pending, false, "تغير أصل الحضور؛ راجع الوقت الصحيح قبل إنشاء طلب آخر")).EnsureSuccessStatusCode();
        var newer = await Request(c, Claim(s.Branch, "CheckOut")); (await Review(owner, newer)).EnsureSuccessStatusCode();
        var sourceId = newer.GetProperty("targetAttendanceId").GetGuid();
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StaffAttendanceExceptions\" SET \"TargetAttendanceVersion\"=1 WHERE \"Id\"={newer.GetProperty("id").GetGuid()}"))).SqlState);
        Assert.Equal(sourceId, (await db.StaffDailyAttendances.AsNoTracking().SingleAsync(a => a.MembershipId == s.Member)).Id);
    }
    [Fact]
    public async Task Manual_overnight_has_explicit_checkout_date_and_rejects_future_times_and_invalid_dates()
    {
        var s = await Staff(); using var c = s.Client; using var owner = await Owner();
        var body = new DailyWrite(new(2026,10,15), "Present", new(23,30), new(4,0), 50, "خصم إداري اصطناعي مستقل", new(2026,10,16));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Manual(owner, s.Member, body with { CheckOutDate = new(2026,10,14) })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Manual(owner, s.Member, body with { CheckOutDate = new(2026,10,17) })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Manual(owner, s.Member, body with { CheckOut = new(14,0) })).StatusCode);
        (await Manual(owner, s.Member, body)).EnsureSuccessStatusCode();
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff-attendance/{s.Member}?date=2026-10-15"); Assert.Equal("2026-10-16", detail.GetProperty("checkOutDate").GetString());
        var report = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/staff-daily?coachId={s.Member}&allDates=true"); Assert.Equal(50, report.GetProperty("totalDeductions").GetDecimal()); Assert.Equal(1, report.GetProperty("totalCount").GetInt32());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StaffDailyAttendances\" SET \"CheckOutDate\"=\"WorkDate\" WHERE \"MembershipId\"={s.Member}"))).SqlState);
        (await Manual(owner, s.Member, body with { Status = "Absent", CheckIn = null, CheckOut = null, CheckOutDate = null })).EnsureSuccessStatusCode();
        Assert.Null((await db.StaffDailyAttendances.AsNoTracking().SingleAsync(a => a.MembershipId == s.Member)).CheckOutDate);
    }
    [Fact]
    public async Task Month_boundary_checkout_does_not_reopen_a_closed_source_month()
    {
        var s = await Staff(); using var c = s.Client; using var owner = await Owner();
        var first = await Request(c, Claim(s.Branch, time: DateTimeOffset.Parse("2026-09-30T20:00:00Z"))); (await Review(owner, first)).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var ownerId = await db.AcademyMemberships.Where(m => m.AcademyId == DemoSeed.NogoomAcademyId && m.Role == AcademyRole.AcademyOwner).Select(m => m.UserId).FirstAsync();
        db.PayrollSettlements.Add(new() { Id=Guid.NewGuid(), AcademyId=DemoSeed.NogoomAcademyId, EmployeeId=s.Employee, Month=new(2026,9,1), Currency="EGP", PaidAtUtc=Now, PaidByUserId=ownerId, IdempotencyKey=Guid.NewGuid().ToString() }); await db.SaveChangesAsync();
        var pending = await Request(c, Claim(s.Branch, "CheckOut", DateTimeOffset.Parse("2026-09-30T22:00:00Z"))); Assert.Equal("2026-09-30", pending.GetProperty("workDate").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Review(owner, pending)).StatusCode);
        var source = await db.StaffDailyAttendances.AsNoTracking().SingleAsync(a => a.MembershipId == s.Member); Assert.Null(source.CheckOut); Assert.Equal(new DateOnly(2026,9,30), source.WorkDate);
        (await Review(owner, pending, false, "مصدر شهر معتمد؛ يحتاج مراجعة مستقلة لا تغييرًا صامتًا")).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Closed_payroll_month_and_existing_daily_record_are_not_silently_changed()
    {
        var s=await Staff();using var c=s.Client;using var owner=await Owner();var r=await Request(c,Claim(s.Branch));
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var ownerId=await db.AcademyMemberships.Where(m=>m.AcademyId==DemoSeed.NogoomAcademyId&&m.Role==AcademyRole.AcademyOwner).Select(m=>m.UserId).FirstAsync();
        db.PayrollSettlements.Add(new(){Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,EmployeeId=s.Employee,Month=new(2026,10,1),Currency="EGP",PaidAtUtc=Now,PaidByUserId=ownerId,IdempotencyKey=Guid.NewGuid().ToString()});await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict,(await Review(owner,r)).StatusCode);Assert.False(await db.StaffDailyAttendances.AnyAsync(a=>a.MembershipId==s.Member));(await Review(owner,r,false,"طلب مراجعة مستقل لشهر معتمد")).EnsureSuccessStatusCode();
        var t=await Staff();using var tc=t.Client;db.StaffDailyAttendances.Add(new(){Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,MembershipId=t.Member,WorkDate=new(2026,10,16),Status="Absent",RecordedByUserId=ownerId});await db.SaveChangesAsync();var another=await Request(tc,Claim(t.Branch));Assert.Equal(HttpStatusCode.Conflict,(await Review(owner,another)).StatusCode);Assert.Equal("Absent",await db.StaffDailyAttendances.Where(a=>a.MembershipId==t.Member).Select(a=>a.Status).SingleAsync());
    }
}
