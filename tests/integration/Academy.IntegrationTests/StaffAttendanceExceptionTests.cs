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
        var home=branch??await db.Branches.Where(b=>b.AcademyId==DemoSeed.NogoomAcademyId&&b.IsActive).Select(b=>b.Id).FirstAsync();
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
    private static async Task<HttpResponseMessage> Manual(HttpClient owner, Guid member, DailyWrite body)
    {
        var csrf = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var command = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/manage/staff-attendance/{member}") { Content = JsonContent.Create(body) };
        command.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); return await owner.SendAsync(command);
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
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var branches=await db.Branches.Where(b=>b.AcademyId==DemoSeed.NogoomAcademyId&&b.IsActive).Select(b=>b.Id).Take(2).ToArrayAsync();
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
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var branches=await db.Branches.Where(b=>b.AcademyId==DemoSeed.NogoomAcademyId&&b.IsActive).Select(b=>b.Id).Take(2).ToArrayAsync();var a=await Staff(AcademyRole.AcademyAdmin,branches[0]);using var admin=a.Client;var first=await Staff(branch:branches[0]);var second=await Staff(branch:branches[1]);using var fc=first.Client;using var sc=second.Client;
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
