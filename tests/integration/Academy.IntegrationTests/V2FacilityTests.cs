using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Facilities;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    private sealed class FacilityLicenseScope(AsyncServiceScope scope, AcademyConfigurationVersion row, string original, AcademyFeatureEntitlement license, bool enabled) : IAsyncDisposable
    {
        public static async Task<FacilityLicenseScope> Begin(IServiceProvider services)
        {
            var scope = services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var row = await db.AcademyConfigurationVersions.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.State == ConfigurationState.Active); var original = row.Payload;
            var license = await db.AcademyFeatureEntitlements.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Feature == "facilities"); var enabled = license.Enabled;
            var settings = AcademyRuntimeSettings.Read(row.Payload); settings.Features["facilities"] = true; row.Payload = settings.Serialize(); row.ContentHash = AcademyConfigurationService.Hash(row.Payload); license.Enabled = true; await db.SaveChangesAsync(); return new(scope, row, original, license, enabled);
        }
        public async ValueTask DisposeAsync() { var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); await db.Entry(row).ReloadAsync(); await db.Entry(license).ReloadAsync(); row.Payload = original; row.ContentHash = AcademyConfigurationService.Hash(original); license.Enabled = enabled; await db.SaveChangesAsync(); await scope.DisposeAsync(); }
    }
    private async Task<Guid> FacilityFixture(HttpClient owner, bool approve = true, Guid? branch = null)
    {
        var facility = await Write(owner, "/api/v1/facilities", new FacilityWrite("مرفق اختبار " + Guid.NewGuid().ToString("N")[..8], branch ?? Slice2DemoSeed.FifthBranchId)); facility.EnsureSuccessStatusCode(); var id = (await facility.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var space = await Write(owner, "/api/v1/facilities/spaces", new SpaceWrite("مسبح اختبار", id, Slice2DemoSeed.SwimmingId, FacilitySpaceKind.Pool)); space.EnsureSuccessStatusCode(); id = (await space.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var resource = await Write(owner, "/api/v1/facilities/resources", new ResourceWrite("A", id, 8, true, [0, 1, 2, 3, 4, 5, 6], new(8, 0), new(22, 0), ["Training", "DayPass", "OpenSwim"])); resource.EnsureSuccessStatusCode(); id = (await resource.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        if (approve) (await Write(owner, $"/api/v1/facilities/resources/{id}/approve", new CapacityApproval("مراجعة اصطناعية من مسؤول مؤهل في اختبار فقط", true))).EnsureSuccessStatusCode(); return id;
    }
    private static DateOnly FacilityDay => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")).DateTime).AddDays(20);
    private static BookingWrite FacilityBody(Guid id, int seats = 4, int start = 17, int end = 18) => new(id, FacilityDay, new(start, 0), new(end, 0), seats, false, "DayPass");
    private static async Task<HttpResponseMessage> BookFacility(HttpClient client, BookingWrite body, string key)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/facilities/bookings") { Content = JsonContent.Create(body) }; req.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); req.Headers.Add("Idempotency-Key", key); return await client.SendAsync(req);
    }
    [Fact]
    public async Task Resource_capacity_requires_explicit_qualified_policy_approval_not_an_assumed_safety_ratio()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var resource = await FacilityFixture(owner, false);
        Assert.Equal(HttpStatusCode.Conflict, (await BookFacility(owner, FacilityBody(resource), Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, $"/api/v1/facilities/resources/{resource}/approve", new CapacityApproval("test", false))).StatusCode);
        (await Write(owner, $"/api/v1/facilities/resources/{resource}/approve", new CapacityApproval("اعتماد اختبار وليس سياسة سلامة حقيقية", true))).EnsureSuccessStatusCode();
        (await BookFacility(owner, FacilityBody(resource), Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Last_capacity_is_atomic_retries_are_idempotent_and_reservation_creates_no_cash_or_attendance()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var resource = await FacilityFixture(owner); var body = FacilityBody(resource, 5); var keys = new[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var cash = await db.Collections.CountAsync(); var attendance = await db.PlayerAttendances.CountAsync();
        var results = await Task.WhenAll(keys.Select(key => BookFacility(owner, body, key))); Assert.Single(results, r => r.IsSuccessStatusCode); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        var index = Array.FindIndex(results, x => x.IsSuccessStatusCode); var first = (await results[index].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var retry = await BookFacility(owner, body, keys[index]); retry.EnsureSuccessStatusCode(); Assert.Equal(first, (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()); Assert.Equal(HttpStatusCode.Conflict, (await BookFacility(owner, body with { Seats = 1 }, keys[index])).StatusCode);
        Assert.Single(await db.ResourceBookings.Where(b => b.ResourceId == resource).ToListAsync()); Assert.Equal(cash, await db.Collections.CountAsync()); Assert.Equal(attendance, await db.PlayerAttendances.CountAsync());
        var board = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/facilities/board?date={FacilityDay:yyyy-MM-dd}"); Assert.Contains(board.GetProperty("bookings").EnumerateArray(), b => b.GetProperty("id").GetGuid() == first);
    }
    [Fact]
    public async Task Shared_adjacent_bookings_and_closure_conflicts_preserve_history_and_never_infer_absence()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var resource = await FacilityFixture(owner);
        var first = await BookFacility(owner, FacilityBody(resource, 8, 16, 17), Guid.NewGuid().ToString()); first.EnsureSuccessStatusCode();
        (await BookFacility(owner, FacilityBody(resource, 8, 17, 18), Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/facilities/resources/{resource}/closures", new ClosureWrite(FacilityDay, FacilityDay, new(16, 0), new(17, 0)))).StatusCode);
        var id = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); (await Write(owner, $"/api/v1/facilities/bookings/{id}/cancel", new { })).EnsureSuccessStatusCode(); (await Write(owner, $"/api/v1/facilities/bookings/{id}/cancel", new { })).EnsureSuccessStatusCode();
        (await Write(owner, $"/api/v1/facilities/resources/{resource}/closures", new ClosureWrite(FacilityDay, FacilityDay, new(16, 0), new(17, 0)))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await BookFacility(owner, FacilityBody(resource, 1, 16, 17), Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await BookFacility(owner, FacilityBody(resource) with { Date = FacilityDay.AddDays(-22) }, Guid.NewGuid().ToString())).StatusCode);
    }
    [Fact]
    public async Task Coach_conflicts_are_checked_across_resources_and_bookings_keep_dated_session_identity()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await Fixture(); var resource = await FacilityFixture(owner, branch: f.A); var other = await FacilityFixture(owner, branch: f.A);
        var coachResponse = await Write(owner, "/api/v1/manage/staff", new StaffWrite("مدرب حارات اصطناعي", $"lane-{Guid.NewGuid():N}@example.test", "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.Coach, null)); coachResponse.EnsureSuccessStatusCode(); var coach = (await coachResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        db.StaffGroupAssignments.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingGroupId = f.Group, AcademyMembershipId = coach });
        var session = new Academy.Infrastructure.Attendance.TrainingSession { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, BranchId = f.A, SportId = Slice2DemoSeed.SwimmingId, TrainingGroupId = f.Group, AgeCategoryId = await db.TrainingGroups.Where(g => g.Id == f.Group).Select(g => g.AgeCategoryId).SingleAsync(), SessionDate = FacilityDay, StartTime = new(17, 0), EndTime = new(18, 0), Source = Academy.Infrastructure.Attendance.TrainingSessionSource.Manual, CreatedByUserId = f.Owner };
        db.TrainingSessions.Add(session); await db.SaveChangesAsync();
        var body = FacilityBody(resource) with { ServiceKind = "Training", GroupId = f.Group, CoachId = coach, SessionId = session.Id };
        (await BookFacility(owner, body, Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await BookFacility(owner, body with { ResourceId = other }, Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await BookFacility(owner, body with { Start = new(18, 0), End = new(19, 0) }, Guid.NewGuid().ToString())).StatusCode);
        var board = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/facilities/board?date={FacilityDay:yyyy-MM-dd}"); var row = board.GetProperty("bookings").EnumerateArray().Single(b => b.GetProperty("resourceId").GetGuid() == resource); Assert.Equal(session.Id, row.GetProperty("trainingSessionId").GetGuid()); Assert.Equal(1, row.GetProperty("expectedPlayers").GetInt32()); Assert.Equal(0, row.GetProperty("present").GetInt32());
    }
    [Fact]
    public async Task Facility_tenant_branch_role_csrf_and_live_feature_revocation_are_enforced()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var resource = await FacilityFixture(owner);
        using var guardian = await Guardian(); using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/v1/facilities")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await coach.GetAsync("/api/v1/facilities")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/facilities/bookings", FacilityBody(resource))).StatusCode);
        using var other = await Staff("owner.future@example.test"); Assert.False((await BookFacility(other, FacilityBody(resource), Guid.NewGuid().ToString())).IsSuccessStatusCode);
        var email = "facility-admin-" + Guid.NewGuid().ToString("N")[..8] + "@example.test";
        var r = await Write(owner, "/api/v1/manage/staff", new StaffWrite("إداري مرافق صناعي", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, null)); r.EnsureSuccessStatusCode(); var member = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var actions = StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m == "facilities", m == "facilities", false, false)).ToArray();
        (await Write(owner, $"/api/v1/manage/staff/{member}/access", new StaffAccessWrite(false, [Slice2DemoSeed.CityBranchId], actions), true)).EnsureSuccessStatusCode(); using var admin = await Staff(email);
        var hidden = await admin.GetFromJsonAsync<JsonElement>("/api/v1/facilities"); Assert.DoesNotContain(hidden.GetProperty("resources").EnumerateArray(), x => x.GetProperty("id").GetGuid() == resource);
        Assert.Equal(HttpStatusCode.NotFound, (await BookFacility(admin, FacilityBody(resource), Guid.NewGuid().ToString())).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var license = await db.AcademyFeatureEntitlements.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Feature == "facilities"); license.Enabled = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/facilities")).StatusCode);
    }
}
