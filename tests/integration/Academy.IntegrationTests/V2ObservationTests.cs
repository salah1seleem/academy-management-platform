using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Facilities;
using Academy.Api.Operations;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Academy.Api.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    private async Task<(PlacementFixture F, Guid Resource, Guid Booking)> ObservationReady(HttpClient owner)
    {
        var f = await AttendanceReady(owner); var extra = new List<Guid>();
        await using (var scope = factory.Services.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); (await db.TrainingGroups.SingleAsync(g => g.Id == f.Group)).Capacity = 6;
            for (var n=0;n<4;n++) { var player = Guid.NewGuid(); var enrollment = Guid.NewGuid(); extra.Add(enrollment); db.Players.Add(new() { Id=player, AcademyId=DemoSeed.NogoomAcademyId, PlayerCode="OBS-"+player.ToString("N")[..10], ArabicName="لاعب عد اصطناعي", DateOfBirth=new(2017,1,1) }); db.SportEnrollments.Add(new() { Id=enrollment, AcademyId=DemoSeed.NogoomAcademyId, PlayerId=player, BranchId=f.Branch, SportId=Academy.Api.Slice2.Slice2DemoSeed.SwimmingId, TrainingGroupId=f.Group, GroupAssignedFrom=PlacementToday, TrainingLevelId=f.Level }); }
            await db.SaveChangesAsync();
        }
        (await AttendanceWrite(owner, f.Next, new[] {f.First,f.Second}.Concat(extra.Take(3)).Select(e => new PlayerAttendanceChange(e, AttendanceStatus.Present, 0)).ToArray())).EnsureSuccessStatusCode();
        var resource = await FacilityFixture(owner, branch:f.Branch); var booked = await BookFacility(owner, FacilityBody(resource,6,10,11) with { Date=PlacementToday, ServiceKind="Training", GroupId=f.Group, CoachId=f.Coach, SessionId=f.Next }, Guid.NewGuid().ToString()); Assert.True(booked.IsSuccessStatusCode, await booked.Content.ReadAsStringAsync()); return (f,resource,(await booked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }
    private static async Task<HttpResponseMessage> Observe(HttpClient c, Guid booking, int count, string? key = null) {
        var csrf = await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var req = new HttpRequestMessage(HttpMethod.Post,$"/api/v1/facilities/bookings/{booking}/observations") { Content=JsonContent.Create(new ObservationWrite(count)) }; req.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString()); req.Headers.Add("Idempotency-Key",key ?? Guid.NewGuid().ToString()); return await c.SendAsync(req);
    }
    [Fact]
    public async Task Observation_expected6_recorded5_observed7_requires_review_without_attendance_or_financial_effect()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await ObservationReady(owner); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var before = (await db.PlayerAttendances.CountAsync(),await db.Collections.CountAsync(),await db.SubscriptionSessionMovements.CountAsync()); var key=Guid.NewGuid().ToString(); var r=await Observe(owner,f.Booking,7,key); r.EnsureSuccessStatusCode(); var value=await r.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(6,value.GetProperty("expectedSnapshot").GetInt32()); Assert.Equal(5,value.GetProperty("recordedPresentSnapshot").GetInt32()); Assert.True(value.GetProperty("needsReview").GetBoolean()); Assert.Equal(1,value.GetProperty("deltaExpected").GetInt32()); Assert.Equal(2,value.GetProperty("deltaPresent").GetInt32()); Assert.Equal("Manual",value.GetProperty("source").GetString());
        (await Observe(owner,f.Booking,7,key)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Conflict,(await Observe(owner,f.Booking,8,key)).StatusCode); Assert.Single(await db.ResourceObservations.Where(o=>o.ResourceBookingId==f.Booking).ToArrayAsync()); Assert.Equal(before,(await db.PlayerAttendances.CountAsync(),await db.Collections.CountAsync(),await db.SubscriptionSessionMovements.CountAsync()));
        var board=await owner.GetFromJsonAsync<JsonElement>("/api/v1/facilities/board"); var row=board.GetProperty("bookings").EnumerateArray().Single(b=>b.GetProperty("id").GetGuid()==f.Booking); Assert.Equal(7,row.GetProperty("observation").GetProperty("count").GetInt32()); Assert.Equal(5,row.GetProperty("present").GetInt32());
    }
    [Fact]
    public async Task Observation_history_is_append_only_and_snapshot_does_not_change_after_attendance_correction()
    {
        await using var licensed=await FacilityLicenseScope.Begin(factory.Services); using var owner=await Staff(); var f=await ObservationReady(owner); (await Observe(owner,f.Booking,7)).EnsureSuccessStatusCode(); (await AttendanceWrite(owner,f.F.Next,new PlayerAttendanceChange(f.F.First,AttendanceStatus.Excused,await AttendanceVersion(owner,f.F.Next,f.F.First)))).EnsureSuccessStatusCode(); (await Observe(owner,f.Booking,6)).EnsureSuccessStatusCode();
        var history=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/facilities/bookings/{f.Booking}/observations"); Assert.Equal(2,history.GetArrayLength()); Assert.Equal(2,history[0].GetProperty("sequence").GetInt32()); Assert.Equal(4,history[0].GetProperty("recordedPresentSnapshot").GetInt32()); Assert.Equal(5,history[1].GetProperty("recordedPresentSnapshot").GetInt32()); await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var row=await db.ResourceObservations.FirstAsync(o=>o.ResourceBookingId==f.Booking); Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"ResourceObservations\" WHERE \"Id\"={row.Id}"))).SqlState); row.Count=9; await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
    }
    [Fact]
    public async Task Observation_concurrent_records_have_distinct_sequences_and_board_selects_latest_without_overwrite()
    {
        await using var licensed=await FacilityLicenseScope.Begin(factory.Services); using var owner=await Staff(); var f=await ObservationReady(owner); var results=await Task.WhenAll(Observe(owner,f.Booking,6),Observe(owner,f.Booking,7)); Assert.All(results,r=>r.EnsureSuccessStatusCode()); var history=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/facilities/bookings/{f.Booking}/observations"); Assert.Equal(new[]{2,1},history.EnumerateArray().Select(o=>o.GetProperty("sequence").GetInt32())); var board=await owner.GetFromJsonAsync<JsonElement>("/api/v1/facilities/board"); var row=board.GetProperty("bookings").EnumerateArray().Single(b=>b.GetProperty("id").GetGuid()==f.Booking); Assert.Equal(history[0].GetProperty("count").GetInt32(),row.GetProperty("observation").GetProperty("count").GetInt32());
    }
    [Fact]
    public async Task Observation_rejects_future_past_cancelled_invalid_counts_CSRF_tenant_and_unprivileged_roles()
    {
        await using var licensed=await FacilityLicenseScope.Begin(factory.Services); using var owner=await Staff(); var resource=await FacilityFixture(owner); var booked=await BookFacility(owner,FacilityBody(resource),Guid.NewGuid().ToString()); booked.EnsureSuccessStatusCode(); var id=(await booked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); var path=$"/api/v1/facilities/bookings/{id}/observations";
        Assert.Equal(HttpStatusCode.Conflict,(await Observe(owner,id,4)).StatusCode); Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Observe(owner,id,-1)).StatusCode); Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(path,new ObservationWrite(4))).StatusCode); using var guardian=await Guardian(); using var foreign=await Staff("owner.future@example.test"); Assert.Equal(HttpStatusCode.Forbidden,(await Observe(guardian,id,4)).StatusCode); Assert.False((await Observe(foreign,id,4)).IsSuccessStatusCode);
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var row=await db.ResourceBookings.SingleAsync(b=>b.Id==id); row.Date=PlacementToday.AddDays(-1); await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Conflict,(await Observe(owner,id,4)).StatusCode); row.Date=PlacementToday; row.State=ResourceBookingState.Cancelled; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Conflict,(await Observe(owner,id,4)).StatusCode); Assert.False(await db.ResourceObservations.AnyAsync(o=>o.ResourceBookingId==id));
    }
    [Fact]
    public async Task Observation_admin_add_branch_and_feature_gates_cannot_be_bypassed()
    {
        await using var licensed=await FacilityLicenseScope.Begin(factory.Services); using var owner=await Staff(); var f=await ObservationReady(owner); var email="observe.scope."+Guid.NewGuid().ToString("N")+"@example.test"; var created=await Write(owner,"/api/v1/manage/staff",new StaffWrite("إداري عد اصطناعي",email,"010"+Random.Shared.Next(10000000,99999999),"Demo-Only-123!",AcademyRole.AcademyAdmin,"اختبار")); created.EnsureSuccessStatusCode(); var member=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); using var admin=await Staff(email);
        StaffAccessWrite Access(Guid branch,bool add)=>new(false,[branch],StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,true,m=="facilities"&&add,false,false)).ToArray());
        (await Write(owner,$"/api/v1/manage/staff/{member}/access",Access(f.F.Branch,false),true)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync($"/api/v1/facilities/bookings/{f.Booking}/observations")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden,(await Observe(admin,f.Booking,7)).StatusCode);
        (await Write(owner,$"/api/v1/manage/staff/{member}/access",Access(f.F.OtherBranch,true),true)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.NotFound,(await Observe(admin,f.Booking,7)).StatusCode); Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/api/v1/facilities/bookings/{f.Booking}/observations")).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var config=await db.AcademyConfigurationVersions.SingleAsync(c=>c.AcademyId==DemoSeed.NogoomAcademyId&&c.State==ConfigurationState.Active); var settings=AcademyRuntimeSettings.Read(config.Payload); settings.Features["facilities"]=false; config.Payload=settings.Serialize(); config.ContentHash=AcademyConfigurationService.Hash(config.Payload); await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Forbidden,(await Observe(owner,f.Booking,7)).StatusCode); Assert.False(await db.ResourceObservations.AnyAsync(o=>o.ResourceBookingId==f.Booking));
    }
    [Fact]
    public async Task Observation_board_candidate_counts_preserve_dated_moves_selected_sessions_and_compensation()
    {
        using var owner=await Staff(); var moved=await OccurrenceReady(owner); var preview=await SessionReview(owner,moved.F.First,new SessionTransferPair(moved.F.Next,moved.TargetSession)); (await SessionSave(owner,moved.F.First,preview)).EnsureSuccessStatusCode(); await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var day=await db.TrainingSessions.Where(s=>s.Id==moved.TargetSession).Select(s=>s.SessionDate).SingleAsync(); var counts=await FacilityRosterCounts.Read(db,DemoSeed.NogoomAcademyId,[moved.F.Group,moved.Target],[moved.F.Next,moved.TargetSession],day); Assert.Equal(0,counts.Sessions[moved.F.Next]); Assert.Equal(1,counts.Sessions[moved.TargetSession]); Assert.Equal(1,counts.Groups[moved.F.Group]); Assert.Equal(0,counts.Groups[moved.Target]);
        var dated=await OccurrenceReady(owner); (await TransferSave(owner,dated.F.First,await TransferReview(owner,dated.F.First,dated.Target))).EnsureSuccessStatusCode(); counts=await FacilityRosterCounts.Read(db,DemoSeed.NogoomAcademyId,[dated.F.Group,dated.Target],[dated.F.Next,dated.TargetSession],day); Assert.Equal(0,counts.Sessions[dated.F.Next]); Assert.Equal(1,counts.Sessions[dated.TargetSession]); Assert.Equal(1,counts.Groups[dated.Target]);
        var makeup=await CompensationReady(owner); var grant=await CompensationGrant(owner,makeup.F.First,makeup.F.Past); (await CompensationCommand(owner,$"/api/v1/people/compensations/{grant}/booking",await CompensationBookReview(owner,grant,makeup.TargetSession))).EnsureSuccessStatusCode(); counts=await FacilityRosterCounts.Read(db,DemoSeed.NogoomAcademyId,[makeup.Target],[makeup.TargetSession],day); Assert.Equal(1,counts.Sessions[makeup.TargetSession]); Assert.Equal(0,counts.Groups[makeup.Target]);
    }
    [Fact]
    public async Task Observation_demo_seed_is_repeatable_preserves_reviewed_count_and_has_no_unknown_seventh_player()
    {
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var collections=scope.ServiceProvider.GetRequiredService<CollectionService>(); await FacilityDemoSeed.Seed(db); await GroupPlacementDemoSeed.Seed(db,collections); await ObservationDemoSeed.Seed(db,collections,TimeProvider.System); var booking=ObservationDemoSeed.DailyId(PlacementToday,"booking"); Assert.True(await db.ResourceBookings.AnyAsync(b=>b.Id==booking)); using var owner=await Staff(); (await Observe(owner,booking,6)).EnsureSuccessStatusCode(); var before=(await db.Collections.CountAsync(),await db.Players.CountAsync(),await db.ResourceObservations.CountAsync(),await db.PlayerAttendances.CountAsync()); await ObservationDemoSeed.Seed(db,collections,TimeProvider.System); Assert.Equal(before,(await db.Collections.CountAsync(),await db.Players.CountAsync(),await db.ResourceObservations.CountAsync(),await db.PlayerAttendances.CountAsync())); Assert.Equal(6,await db.ResourceObservations.Where(o=>o.ResourceBookingId==booking).OrderByDescending(o=>o.Sequence).Select(o=>o.Count).FirstAsync()); Assert.Equal(6,await db.SportEnrollments.CountAsync(e=>e.TrainingGroupId==GroupPlacementDemoSeed.Id(160))); Assert.Equal(5,await db.PlayerAttendances.CountAsync(a=>a.TrainingSessionId==ObservationDemoSeed.DailyId(PlacementToday,"session")));
    }
    [Fact]
    public async Task Observation_non_training_count_uses_reserved_seats_without_inventing_training_attendance()
    {
        await using var licensed=await FacilityLicenseScope.Begin(factory.Services); using var owner=await Staff(); var resource=await FacilityFixture(owner); var booked=await BookFacility(owner,FacilityBody(resource) with { Date=PlacementToday },Guid.NewGuid().ToString()); booked.EnsureSuccessStatusCode(); var id=(await booked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); var r=await Observe(owner,id,4); r.EnsureSuccessStatusCode(); var value=await r.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("ReservedSeats",value.GetProperty("expectedBasis").GetString()); Assert.Equal(JsonValueKind.Null,value.GetProperty("recordedPresentSnapshot").ValueKind); Assert.False(value.GetProperty("needsReview").GetBoolean());
    }
}
