using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Slice4;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    [Fact]
    public async Task Attendance_draft_demo_seed_is_repeatable_keeps_records_and_does_not_add_money_or_attendance()
    {
        SetVisitClock();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var collections=scope.ServiceProvider.GetRequiredService<CollectionService>();
        await GroupPlacementDemoSeed.Seed(db,collections);await AttendanceStatesDemoSeed.Seed(db,collections);
        var before=(await db.Collections.CountAsync(),await db.PlayerAttendances.CountAsync(),await db.StaffAttendances.CountAsync());
        await AttendanceDraftDemoSeed.Seed(db,factory.VisitClock);var count=await db.TrainingSessions.CountAsync();await AttendanceDraftDemoSeed.Seed(db,factory.VisitClock);
        Assert.Equal(count,await db.TrainingSessions.CountAsync());Assert.Equal(before,(await db.Collections.CountAsync(),await db.PlayerAttendances.CountAsync(),await db.StaffAttendances.CountAsync()));
        Assert.True(await db.TrainingSessions.AnyAsync(s=>s.AcademyId==DemoSeed.NogoomAcademyId&&s.SessionDate==PlacementToday&&s.TrainingGroupId==GroupPlacementDemoSeed.Id(130)&&s.StartTime==new TimeOnly(6,45)));
        Assert.False(await db.TrainingSessions.AnyAsync(s=>s.AcademyId==DemoSeed.FutureAcademyId&&s.Id==AttendanceDraftDemoSeed.SessionId(PlacementToday)));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Attendance_draft_lease_rejects_expiry_tampering_other_session_and_other_login_without_effects(bool staff)
    {
        SetVisitClock(); using var owner=await Staff(); var f=await AttendanceReady(owner);
        var roster=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions/{f.Next}/players"); var lease=roster.GetProperty("draftLease").GetString(); Assert.False(string.IsNullOrEmpty(lease));
        var part=staff?"staff":"players";
        object Body(string? token) => staff ? new StaffAttendanceBatch([new(f.Coach,AttendanceStatus.Present,0)],token) : new PlayerAttendanceBatch([new(f.First,AttendanceStatus.Present,0)],token);
        Task<HttpResponseMessage> Save(HttpClient client,Guid id,string? token)=>Write(client,$"/api/v1/attendance/sessions/{id}/{part}",Body(token),true);
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail); Assert.Equal(HttpStatusCode.NotFound,(await Save(foreign,f.Next,lease)).StatusCode);
        using var guardian=await Guardian(); Assert.Equal(HttpStatusCode.Forbidden,(await Save(guardian,f.Next,lease)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PutAsJsonAsync($"/api/v1/attendance/sessions/{f.Next}/{part}",Body(lease))).StatusCode);
        using var anotherLogin=await Staff(); Assert.Equal(HttpStatusCode.Conflict,(await Save(anotherLogin,f.Next,lease)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Save(owner,f.Next,lease+"tampered")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Save(owner,f.Past,lease)).StatusCode);
        factory.VisitClock.Now=factory.VisitClock.GetUtcNow().AddMinutes(16);
        Assert.Equal(HttpStatusCode.Conflict,(await Save(owner,f.Next,lease)).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.False(await db.PlayerAttendances.AnyAsync(a=>a.TrainingSessionId==f.Next)); Assert.False(await db.StaffAttendances.AnyAsync(a=>a.TrainingSessionId==f.Next));
        var fresh=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/sessions/{f.Next}/players"); var freshLease=fresh.GetProperty("draftLease").GetString();
        (await Save(owner,f.Next,freshLease)).EnsureSuccessStatusCode(); (await Save(owner,f.Next,freshLease)).EnsureSuccessStatusCode();
        if(staff) Assert.Single(await db.StaffAttendances.Where(a=>a.TrainingSessionId==f.Next).ToArrayAsync());
        else { Assert.Single(await db.PlayerAttendances.Where(a=>a.TrainingSessionId==f.Next).ToArrayAsync()); Assert.Single(await db.SubscriptionSessionMovements.Where(m=>m.PlayerAttendance.TrainingSessionId==f.Next).ToArrayAsync()); }
    }
}
