using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Facilities;
using Academy.Api.Slice4;
using Academy.Api.Operations;
using Academy.Api.Configuration;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Academy.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;
public sealed class VisitTestClock : TimeProvider
{
    public DateTimeOffset? Now { get; set; }
    public override DateTimeOffset GetUtcNow() => Now ?? TimeProvider.System.GetUtcNow();
}
public sealed partial class V2PlanPublicationTests
{
    private void SetVisitClock() => factory.VisitClock.Now = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(PlacementToday.ToDateTime(new TimeOnly(10,15)), TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")));
    private async Task<(PlacementFixture F, Guid Resource, Guid Booking)> VisitReady(HttpClient owner)
    {
        var f = await AttendanceReady(owner); var resource = await FacilityFixture(owner, branch: f.Branch);
        var booked = await BookFacility(owner, FacilityBody(resource,2,10,11) with { Date=PlacementToday, ServiceKind="Training", GroupId=f.Group, CoachId=f.Coach, SessionId=f.Next }, Guid.NewGuid().ToString()); Assert.True(booked.IsSuccessStatusCode, await booked.Content.ReadAsStringAsync());
        // Clock is explicit and test-only; production uses the real academy day.
        return (f,resource,(await booked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }
    private static async Task<HttpResponseMessage> VisitCommand(HttpClient c,string path,object body,string? key=null)
    {
        var csrf=await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)}; request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString()); request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString()); return await c.SendAsync(request);
    }
    private static Task<HttpResponseMessage> EnterVisit(HttpClient c,Guid booking,Guid enrollment,string? key=null)=>VisitCommand(c,$"/api/v1/facilities/bookings/{booking}/entry",new EntryWrite(enrollment,IdentityConfirmed:true),key);
    [Fact]
    public async Task Visit_entry_retry_exit_version_and_quality_are_separate_from_attendance_money_and_balance()
    {
        await using var licensed=await FacilityLicenseScope.Begin(factory.Services); SetVisitClock(); using var owner=await Staff(); var f=await VisitReady(owner); await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var before=(await db.PlayerAttendances.CountAsync(),await db.Collections.CountAsync(),await db.SubscriptionSessionMovements.CountAsync()); var balance=await db.SubscriptionPeriods.Where(p=>p.SportEnrollmentId==f.F.First).Select(p=>p.RemainingSessions).SingleAsync(); var key=Guid.NewGuid().ToString();
        var entered=await EnterVisit(owner,f.Booking,f.F.First,key); Assert.True(entered.IsSuccessStatusCode,await entered.Content.ReadAsStringAsync()); var v=await entered.Content.ReadFromJsonAsync<JsonElement>(); var id=v.GetProperty("id").GetGuid(); var version=v.GetProperty("version").GetUInt32(); Assert.Equal("TrainingSubscription",v.GetProperty("basis").GetString()); Assert.Equal("Participant",v.GetProperty("personKind").GetString());
        (await EnterVisit(owner,f.Booking,f.F.First,key)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Conflict,(await EnterVisit(owner,f.Booking,f.F.Second,key)).StatusCode); Assert.Equal(HttpStatusCode.Conflict,(await EnterVisit(owner,f.Booking,f.F.First)).StatusCode); Assert.Single(await db.EntryVisits.Where(x=>x.ResourceBookingId==f.Booking).ToArrayAsync());
        var board=await owner.GetFromJsonAsync<JsonElement>("/api/v1/facilities/board"); var booking=board.GetProperty("bookings").EnumerateArray().Single(b=>b.GetProperty("id").GetGuid()==f.Booking); Assert.Equal(1,booking.GetProperty("occupancy").GetProperty("participants").GetInt32()); Assert.True(booking.GetProperty("occupancy").GetProperty("estimated").GetBoolean());
        factory.VisitClock.Now=factory.VisitClock.Now!.Value.AddHours(2); (await Write(owner,"/api/v1/auth/login",new { email=DemoSeed.OwnerEmail,password="Demo-Only-123!" })).EnsureSuccessStatusCode(); var history=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/facilities/bookings/{f.Booking}/visits"); Assert.True(history.GetProperty("visits")[0].GetProperty("staleOpen").GetBoolean());
        var exitPath=$"/api/v1/facilities/visits/{id}/exit"; Assert.Equal(HttpStatusCode.Conflict,(await VisitCommand(owner,exitPath,new ExitWrite(0))).StatusCode); var exitKey=Guid.NewGuid().ToString(); (await VisitCommand(owner,exitPath,new ExitWrite(version),exitKey)).EnsureSuccessStatusCode(); (await VisitCommand(owner,exitPath,new ExitWrite(version),exitKey)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Conflict,(await VisitCommand(owner,exitPath,new ExitWrite(version))).StatusCode);
        Assert.Equal(before,(await db.PlayerAttendances.CountAsync(),await db.Collections.CountAsync(),await db.SubscriptionSessionMovements.CountAsync())); Assert.Equal(balance,await db.SubscriptionPeriods.Where(p=>p.SportEnrollmentId==f.F.First).Select(p=>p.RemainingSessions).SingleAsync()); Assert.Single(await db.AccessEntitlements.Where(x=>x.ResourceBookingId==f.Booking).ToArrayAsync());
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"EntryVisits\" WHERE \"Id\"={id}"))).SqlState);
        var right=await db.AccessEntitlements.SingleAsync(x=>x.ResourceBookingId==f.Booking); right.Basis="changed"; await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
    }
    [Fact]
    public async Task Visit_companion_requires_linked_child_open_visit_and_never_consumes_a_seat_or_a_session()
    {
        await using var licensed=await FacilityLicenseScope.Begin(factory.Services); SetVisitClock(); using var owner=await Staff(); var f=await VisitReady(owner); var entry=await EnterVisit(owner,f.Booking,f.F.First); entry.EnsureSuccessStatusCode(); var child=await entry.Content.ReadFromJsonAsync<JsonElement>(); var childId=child.GetProperty("id").GetGuid(); var history=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/facilities/bookings/{f.Booking}/visits"); var companion=history.GetProperty("companions")[0]; var body=new EntryWrite(null,companion.GetProperty("guardianId").GetGuid(),childId,true); var path=$"/api/v1/facilities/bookings/{f.Booking}/entry";
        Assert.Equal(HttpStatusCode.NotFound,(await VisitCommand(owner,path,body with{GuardianId=Guid.NewGuid()})).StatusCode); var r=await VisitCommand(owner,path,body); r.EnsureSuccessStatusCode(); var v=await r.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("Companion",v.GetProperty("personKind").GetString()); Assert.Equal(HttpStatusCode.Conflict,(await VisitCommand(owner,path,body)).StatusCode);
        var board=await owner.GetFromJsonAsync<JsonElement>("/api/v1/facilities/board"); var row=board.GetProperty("bookings").EnumerateArray().Single(b=>b.GetProperty("id").GetGuid()==f.Booking); Assert.Equal(1,row.GetProperty("occupancy").GetProperty("participants").GetInt32()); Assert.Equal(1,row.GetProperty("occupancy").GetProperty("companions").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict,(await VisitCommand(owner,$"/api/v1/facilities/visits/{childId}/exit",new ExitWrite(child.GetProperty("version").GetUInt32()))).StatusCode);
        (await VisitCommand(owner,$"/api/v1/facilities/visits/{v.GetProperty("id").GetGuid()}/exit",new ExitWrite(v.GetProperty("version").GetUInt32()))).EnsureSuccessStatusCode(); (await VisitCommand(owner,$"/api/v1/facilities/visits/{childId}/exit",new ExitWrite(child.GetProperty("version").GetUInt32()))).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Visit_rejects_unpaid_frozen_exhausted_foreign_future_outside_window_and_unconfirmed_identity()
    {
        await using var licensed=await FacilityLicenseScope.Begin(factory.Services); SetVisitClock(); using var owner=await Staff(); var f=await VisitReady(owner); var path=$"/api/v1/facilities/bookings/{f.Booking}/entry"; Assert.Equal(HttpStatusCode.UnprocessableEntity,(await VisitCommand(owner,path,new EntryWrite(f.F.First))).StatusCode); Assert.Equal(HttpStatusCode.NotFound,(await EnterVisit(owner,f.Booking,Guid.NewGuid())).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var period=await db.SubscriptionPeriods.SingleAsync(p=>p.SportEnrollmentId==f.F.First); period.Status=SubscriptionPeriodStatus.Frozen; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Conflict,(await EnterVisit(owner,f.Booking,f.F.First)).StatusCode); period.Status=SubscriptionPeriodStatus.Active; period.RemainingSessions=0; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Conflict,(await EnterVisit(owner,f.Booking,f.F.First)).StatusCode);
        period.RemainingSessions=8; period.EndDate=PlacementToday.AddDays(-1); await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Conflict,(await EnterVisit(owner,f.Booking,f.F.First)).StatusCode); period.EndDate=PlacementToday.AddDays(10); await db.SaveChangesAsync();
        factory.VisitClock.Now=factory.VisitClock.Now!.Value.AddDays(-1); Assert.Equal(HttpStatusCode.Conflict,(await EnterVisit(owner,f.Booking,f.F.First)).StatusCode); factory.VisitClock.Now=factory.VisitClock.Now.Value.AddDays(1).AddHours(-2); Assert.Equal(HttpStatusCode.Conflict,(await EnterVisit(owner,f.Booking,f.F.First)).StatusCode);
        using var guardian=await Guardian(); using var foreign=await Staff("owner.future@example.test"); Assert.Equal(HttpStatusCode.Forbidden,(await EnterVisit(guardian,f.Booking,f.F.First)).StatusCode); Assert.False((await EnterVisit(foreign,f.Booking,f.F.First)).IsSuccessStatusCode); Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(path,new EntryWrite(f.F.First,IdentityConfirmed:true))).StatusCode); Assert.False(await db.EntryVisits.AnyAsync(v=>v.ResourceBookingId==f.Booking));
    }
    [Fact]
    public async Task Visit_recorded_last_session_is_still_an_entitlement_but_another_session_cannot_reuse_it()
    {
        await using var licensed=await FacilityLicenseScope.Begin(factory.Services); SetVisitClock(); using var owner=await Staff(); var f=await VisitReady(owner); await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); (await db.SubscriptionPeriods.SingleAsync(p=>p.SportEnrollmentId==f.F.First)).RemainingSessions=1; await db.SaveChangesAsync(); (await AttendanceWrite(owner,f.F.Next,new PlayerAttendanceChange(f.F.First,AttendanceStatus.Present,0))).EnsureSuccessStatusCode(); Assert.Equal(0,await db.SubscriptionPeriods.Where(p=>p.SportEnrollmentId==f.F.First).Select(p=>p.RemainingSessions).SingleAsync()); (await EnterVisit(owner,f.Booking,f.F.First)).EnsureSuccessStatusCode(); Assert.Single(await db.SubscriptionSessionMovements.Where(m=>m.SubscriptionPeriod.SportEnrollmentId==f.F.First).ToArrayAsync());
    }
    [Fact]
    public async Task Visit_admin_read_add_edit_branch_and_feature_gates_remain_independent()
    {
        await using var licensed=await FacilityLicenseScope.Begin(factory.Services); SetVisitClock(); using var owner=await Staff(); var f=await VisitReady(owner); var email="visit.scope."+Guid.NewGuid().ToString("N")+"@example.test";
        var created=await Write(owner,"/api/v1/manage/staff",new StaffWrite("إداري بوابة اصطناعي",email,"010"+Random.Shared.Next(10000000,99999999),"Demo-Only-123!",AcademyRole.AcademyAdmin,"اختبار")); created.EnsureSuccessStatusCode(); var member=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); using var admin=await Staff(email);
        StaffAccessWrite Access(Guid branch,bool add,bool edit)=>new(false,[branch],StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,true,m=="facilities"&&add,m=="facilities"&&edit,false)).ToArray());
        (await Write(owner,$"/api/v1/manage/staff/{member}/access",Access(f.F.Branch,false,false),true)).EnsureSuccessStatusCode(); var path=$"/api/v1/facilities/bookings/{f.Booking}/visits"; Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync(path)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden,(await EnterVisit(admin,f.Booking,f.F.First)).StatusCode);
        (await Write(owner,$"/api/v1/manage/staff/{member}/access",Access(f.F.Branch,true,false),true)).EnsureSuccessStatusCode(); var entered=await EnterVisit(admin,f.Booking,f.F.First); entered.EnsureSuccessStatusCode(); var v=await entered.Content.ReadFromJsonAsync<JsonElement>(); var exit=$"/api/v1/facilities/visits/{v.GetProperty("id").GetGuid()}/exit"; Assert.Equal(HttpStatusCode.Forbidden,(await VisitCommand(admin,exit,new ExitWrite(v.GetProperty("version").GetUInt32()))).StatusCode);
        (await Write(owner,$"/api/v1/manage/staff/{member}/access",Access(f.F.OtherBranch,true,true),true)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync(path)).StatusCode); Assert.Equal(HttpStatusCode.NotFound,(await VisitCommand(admin,exit,new ExitWrite(v.GetProperty("version").GetUInt32()))).StatusCode);
        using var coach=factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach,factory.Services); Assert.Equal(HttpStatusCode.Forbidden,(await coach.GetAsync(path)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden,(await EnterVisit(coach,f.Booking,f.F.Second)).StatusCode);
        (await Write(owner,$"/api/v1/manage/staff/{member}/access",Access(f.F.Branch,false,true),true)).EnsureSuccessStatusCode(); (await VisitCommand(admin,exit,new ExitWrite(v.GetProperty("version").GetUInt32()))).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var config=await db.AcademyConfigurationVersions.SingleAsync(c=>c.AcademyId==DemoSeed.NogoomAcademyId&&c.State==ConfigurationState.Active); var settings=AcademyRuntimeSettings.Read(config.Payload); settings.Features["facilities"]=false; config.Payload=settings.Serialize(); config.ContentHash=AcademyConfigurationService.Hash(config.Payload); await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Forbidden,(await EnterVisit(owner,f.Booking,f.F.Second)).StatusCode);
    }
    [Fact]
    public async Task Visit_last_operational_seat_and_duplicate_subject_are_serialized_without_false_success()
    {
        await using var licensed=await FacilityLicenseScope.Begin(factory.Services); SetVisitClock(); using var owner=await Staff(); var f=await VisitReady(owner); await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        // Deliberately simulate a reduced approved operating capacity after reservation.
        (await db.FacilityResources.SingleAsync(r=>r.Id==f.Resource)).Capacity=1; await db.SaveChangesAsync(); var results=await Task.WhenAll(EnterVisit(owner,f.Booking,f.F.First),EnterVisit(owner,f.Booking,f.F.Second)); Assert.Single(results,r=>r.IsSuccessStatusCode); Assert.Single(results,r=>r.StatusCode==HttpStatusCode.Conflict); Assert.Single(await db.EntryVisits.Where(v=>v.ResourceBookingId==f.Booking).ToArrayAsync());
    }
    [Fact]
    public async Task Visit_card_lookup_is_purpose_scoped_and_does_not_admit_or_reveal_financial_or_medical_data()
    {
        await using var licensed=await FacilityLicenseScope.Begin(factory.Services); SetVisitClock(); using var owner=await Staff(); var f=await VisitReady(owner); var player=await PlayerFor(f.F.First); var card=await IssueCard(owner,player); var path=$"/api/v1/facilities/bookings/{f.Booking}/player-card/resolve"; var body=new VisitCardWrite(card.GetProperty("code").GetString()!); var resolved=await Write(owner,path,body); resolved.EnsureSuccessStatusCode(); var value=await resolved.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(f.F.First,value.GetProperty("player").GetProperty("enrollmentId").GetGuid()); Assert.True(value.GetProperty("identityCheckRequired").GetBoolean()); Assert.False(value.GetProperty("player").TryGetProperty("medical",out _)); Assert.False(value.GetProperty("player").TryGetProperty("amount",out _)); Assert.Contains("no-store",resolved.Headers.CacheControl!.ToString());
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.False(await db.EntryVisits.AnyAsync(v=>v.ResourceBookingId==f.Booking)); (await Write(owner,CardPath(player)+"/revoke",new PlayerCardWrite(card.GetProperty("version").GetUInt32()),true)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.NotFound,(await Write(owner,path,body)).StatusCode); (await EnterVisit(owner,f.Booking,f.F.First)).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Visit_non_training_reservation_is_not_a_free_admission_right()
    {
        await using var licensed=await FacilityLicenseScope.Begin(factory.Services); SetVisitClock(); using var owner=await Staff(); var f=await VisitReady(owner); var resource=await FacilityFixture(owner,branch:f.F.Branch); var booked=await BookFacility(owner,FacilityBody(resource,2,10,11) with{Date=PlacementToday},Guid.NewGuid().ToString()); booked.EnsureSuccessStatusCode(); var id=(await booked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); Assert.Equal(HttpStatusCode.Conflict,(await EnterVisit(owner,id,f.F.First)).StatusCode); await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.False(await db.AccessEntitlements.AnyAsync(e=>e.ResourceBookingId==id));
    }
    [Fact]
    public async Task Visit_demo_seed_preserves_reviewed_visits_and_does_not_automatically_assume_presence()
    {
        await using var licensed=await FacilityLicenseScope.Begin(factory.Services); SetVisitClock(); using var owner=await Staff(); await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var collections=scope.ServiceProvider.GetRequiredService<CollectionService>(); await FacilityDemoSeed.Seed(db); await GroupPlacementDemoSeed.Seed(db,collections); await VisitDemoSeed.Seed(db,users,collections,factory.VisitClock); var booking=VisitDemoSeed.SlotId(PlacementToday,10,"booking"); Assert.True(await db.ResourceBookings.AnyAsync(b=>b.Id==booking));
        var before=(await db.Players.CountAsync(),await db.Collections.CountAsync(),await db.EntryVisits.CountAsync(),await db.PlayerAttendances.CountAsync()); await VisitDemoSeed.Seed(db,users,collections,factory.VisitClock); Assert.Equal(before,(await db.Players.CountAsync(),await db.Collections.CountAsync(),await db.EntryVisits.CountAsync(),await db.PlayerAttendances.CountAsync()));
        if(!await db.EntryVisits.AnyAsync(v=>v.ResourceBookingId==booking)) (await EnterVisit(owner,booking,GroupPlacementDemoSeed.Id(271))).EnsureSuccessStatusCode(); var retained=await db.EntryVisits.CountAsync(v=>v.ResourceBookingId==booking); await VisitDemoSeed.Seed(db,users,collections,factory.VisitClock); Assert.Equal(retained,await db.EntryVisits.CountAsync(v=>v.ResourceBookingId==booking)); Assert.False(await db.PlayerAttendances.AnyAsync(a=>a.TrainingSessionId==VisitDemoSeed.SlotId(PlacementToday,10,"session")));
    }
    [Fact]
    public async Task Visit_compensation_access_is_distinct_from_the_original_subscription_and_does_not_consume_it()
    {
        await using var licensed=await FacilityLicenseScope.Begin(factory.Services); SetVisitClock(); using var owner=await Staff(); var f=await CompensationReady(owner);
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var session=await db.TrainingSessions.SingleAsync(s=>s.Id==f.TargetSession); session.SessionDate=PlacementToday; await db.SaveChangesAsync();
        var grant=await CompensationGrant(owner,f.F.First,f.F.Past); (await CompensationCommand(owner,$"/api/v1/people/compensations/{grant}/booking",await CompensationBookReview(owner,grant,f.TargetSession))).EnsureSuccessStatusCode();
        var coach=await db.StaffGroupAssignments.Where(a=>a.TrainingGroupId==f.Target&&a.IsActive).Select(a=>a.AcademyMembershipId).FirstAsync(); var resource=await FacilityFixture(owner,branch:f.F.Branch); var booked=await BookFacility(owner,new BookingWrite(resource,PlacementToday,session.StartTime,session.EndTime,2,false,"Training",f.Target,coach,session.Id),Guid.NewGuid().ToString()); Assert.True(booked.IsSuccessStatusCode,await booked.Content.ReadAsStringAsync()); var booking=(await booked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        factory.VisitClock.Now=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(PlacementToday.ToDateTime(session.StartTime).AddMinutes(5),TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"))); (await Write(owner,"/api/v1/auth/login",new{email=DemoSeed.OwnerEmail,password="Demo-Only-123!"})).EnsureSuccessStatusCode(); var before=(await db.CompensationMovements.CountAsync(),await db.SubscriptionSessionMovements.CountAsync(),await db.Collections.CountAsync());
        var admitted=await EnterVisit(owner,booking,f.F.First); Assert.True(admitted.IsSuccessStatusCode,await admitted.Content.ReadAsStringAsync()); var right=await db.AccessEntitlements.SingleAsync(e=>e.ResourceBookingId==booking); Assert.Equal("ApprovedCompensation",right.Basis); Assert.Null(right.SubscriptionPeriodId); Assert.NotNull(right.CompensationBookingId); Assert.Equal(before,(await db.CompensationMovements.CountAsync(),await db.SubscriptionSessionMovements.CountAsync(),await db.Collections.CountAsync()));
    }
    [Fact]
    public async Task Visit_guardian_sees_only_linked_child_history_without_staff_keys_companions_or_financial_fields()
    {
        await using var licensed=await FacilityLicenseScope.Begin(factory.Services); SetVisitClock(); using var owner=await Staff(); var f=await VisitReady(owner); (await EnterVisit(owner,f.Booking,f.F.First)).EnsureSuccessStatusCode(); var player=await PlayerFor(f.F.First); using var guardian=await Guardian(); var path=$"/api/v1/guardian/children/{player}/visits"; var response=await guardian.GetAsync(path); response.EnsureSuccessStatusCode(); Assert.Contains("no-store",response.Headers.CacheControl!.ToString()); var history=await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(100,history.GetProperty("historyLimit").GetInt32()); Assert.Single(history.GetProperty("visits").EnumerateArray()); var v=history.GetProperty("visits")[0]; Assert.False(v.TryGetProperty("entryKey",out _)); Assert.False(v.TryGetProperty("enteredByUserId",out _)); Assert.False(v.TryGetProperty("subscriptionPeriodId",out _)); Assert.False(v.TryGetProperty("guardianId",out _));
        Assert.Equal(HttpStatusCode.Forbidden,(await owner.GetAsync(path)).StatusCode); Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync($"/api/v1/guardian/children/{await PlayerFor(f.F.Second)}/visits")).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var link=await db.GuardianPlayerLinks.SingleAsync(l=>l.PlayerId==player); link.IsActive=false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync(path)).StatusCode);
    }
}
