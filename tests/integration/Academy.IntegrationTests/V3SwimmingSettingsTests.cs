using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Facilities;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    private static async Task<JsonElement> SwimmingPolicy(HttpClient owner, Guid branch) =>
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/sport-settings/{Slice2DemoSeed.SwimmingId}/swimming"))
        .GetProperty("policies").EnumerateArray().Single(x => x.GetProperty("branchId").GetGuid() == branch);
    private static async Task<HttpResponseMessage> SaveSwimming(HttpClient owner, Guid branch, SwimmingPolicyWrite body) =>
        await Write(owner, $"/api/v1/manage/sport-settings/{Slice2DemoSeed.SwimmingId}/swimming/{branch}", body, true);

    [Fact]
    public async Task V3_swimming_configuration_rejects_football_other_tenant_coach_and_guardian()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff();
        var options = await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/sport-settings");
        Assert.Contains(options.GetProperty("sports").EnumerateArray(), x => x.GetProperty("id").GetGuid() == Slice2DemoSeed.FootballId && x.GetProperty("kind").GetString() == "Football");
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/manage/sport-settings/{Slice2DemoSeed.FootballId}/swimming")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(owner, $"/api/v1/manage/sport-settings/{Slice2DemoSeed.FootballId}/swimming/{Slice2DemoSeed.CityBranchId}", new SwimmingPolicyWrite(true, true, true, true, 0), true)).StatusCode);
        using var guardian = await Guardian();
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/v1/manage/sport-settings")).StatusCode);
        using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services);
        Assert.Equal(HttpStatusCode.Forbidden, (await coach.GetAsync("/api/v1/manage/sport-settings")).StatusCode);
        using var foreign = await Staff(DemoSeed.FutureOwnerEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/api/v1/manage/sport-settings/{Slice2DemoSeed.SwimmingId}/swimming")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var foreignBranch = await db.Branches.Where(x => x.AcademyId == DemoSeed.FutureAcademyId).Select(x => x.Id).FirstAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await SaveSwimming(owner, foreignBranch, new(true, true, true, true, 0))).StatusCode);
        var facility = await Write(owner, "/api/v1/facilities", new FacilityWrite("مرفق كرة قدم " + Guid.NewGuid().ToString("N")[..8], Slice2DemoSeed.CityBranchId)); facility.EnsureSuccessStatusCode();
        var facilityId = (await facility.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(owner, "/api/v1/facilities/spaces", new SpaceWrite("مسبح غير صالح", facilityId, Slice2DemoSeed.FootballId, Academy.Infrastructure.Facilities.FacilitySpaceKind.Pool))).StatusCode);
        Assert.False(await db.SwimmingBranchPolicies.AnyAsync(x => x.SportId == Slice2DemoSeed.FootballId));
    }

    [Fact]
    public async Task V3_swimming_disable_blocks_new_bookings_only_in_its_branch_and_preserves_history()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await Fixture();
        var a = await FacilityFixture(owner, branch: f.A); var b = await FacilityFixture(owner, branch: f.B);
        var prior = await BookFacility(owner, FacilityBody(a, start: 16, end: 17), Guid.NewGuid().ToString()); prior.EnsureSuccessStatusCode();
        var priorId = (await prior.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var guardian = await Guardian();
        var planResponse = await Write(owner, "/api/v1/subscriptions/plans", Body(f.A, "زيارة اختبار فصل الرياضة " + Guid.NewGuid().ToString("N")[..8], 125, type: SubscriptionPlanType.Duration) with { ServiceKind = AcademyServiceKind.DayPass, DurationDays = 1 });
        planResponse.EnsureSuccessStatusCode(); var plan = (await planResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var choice = new DayPassChoice(await PlayerFor(f.Enrollment), priorId, plan);
        (await Write(guardian, "/api/v1/guardian/day-pass/quote", choice)).EnsureSuccessStatusCode();
        var policyA = await SwimmingPolicy(owner, f.A); var version = policyA.GetProperty("version").GetUInt32();
        (await SaveSwimming(owner, f.A, new(false, true, true, true, version))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await BookFacility(owner, FacilityBody(a), Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(guardian, "/api/v1/guardian/day-pass/quote", choice)).StatusCode);
        var offered = await guardian.GetFromJsonAsync<JsonElement>("/api/v1/guardian/day-pass/options");
        Assert.DoesNotContain(offered.GetProperty("bookings").EnumerateArray(), x => x.GetProperty("bookingId").GetGuid() == priorId);
        (await BookFacility(owner, FacilityBody(b), Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        Assert.True((await SwimmingPolicy(owner, f.B)).GetProperty("enabled").GetBoolean());
        var board = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/facilities/board?date={FacilityDay:yyyy-MM-dd}&branchId={f.A}");
        Assert.Contains(board.GetProperty("bookings").EnumerateArray(), x => x.GetProperty("id").GetGuid() == priorId);
        Assert.Equal(HttpStatusCode.Conflict, (await SaveSwimming(owner, f.A, new(true, true, true, true, version))).StatusCode);
        var history = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/sport-settings/{Slice2DemoSeed.SwimmingId}/swimming");
        Assert.Contains(history.GetProperty("history").EnumerateArray(), x => x.GetProperty("branchId").GetGuid() == f.A);
        var csrfMissing = await owner.PutAsJsonAsync($"/api/v1/manage/sport-settings/{Slice2DemoSeed.SwimmingId}/swimming/{f.A}", new SwimmingPolicyWrite(true, true, true, true, 0));
        Assert.Equal(HttpStatusCode.BadRequest, csrfMissing.StatusCode);
    }

    [Fact]
    public async Task V3_swimming_service_flags_gate_operations_without_changing_football_or_creating_money()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await Fixture();
        var resource = await FacilityFixture(owner, branch: f.A); var policy = await SwimmingPolicy(owner, f.A);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var money = await db.Collections.CountAsync();
        (await SaveSwimming(owner, f.A, new(true, false, false, false, policy.GetProperty("version").GetUInt32()))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await BookFacility(owner, FacilityBody(resource), Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await BookFacility(owner, FacilityBody(resource) with { ServiceKind = "OpenSwim" }, Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(money, await db.Collections.CountAsync());
        Assert.Equal(Academy.Infrastructure.Structure.SportKind.Football, (await db.Sports.SingleAsync(x => x.Id == Slice2DemoSeed.FootballId)).Kind);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/manage/sport-settings/{Slice2DemoSeed.SwimmingId}/kind", new SportKindWrite(Academy.Infrastructure.Structure.SportKind.Football, (await db.Sports.AsNoTracking().SingleAsync(x => x.Id == Slice2DemoSeed.SwimmingId)).Version), true)).StatusCode);
    }

    [Fact]
    public async Task V3_swimming_demo_job_respects_disabled_policy_and_does_not_reenable_it()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await FacilityDemoSeed.Seed(db);
        var policy = await db.SwimmingBranchPolicies.SingleAsync(x => x.SportId == Slice2DemoSeed.SwimmingId && x.BranchId == Slice2DemoSeed.FifthBranchId);
        var enabled = policy.Enabled;
        try
        {
            policy.Enabled = false; await db.SaveChangesAsync();
            var before = await db.ResourceBookings.CountAsync();
            factory.VisitClock.Now = factory.VisitClock.GetUtcNow().AddDays(37);
            await DayPassDemoSeed.Seed(db, factory.VisitClock);
            await FacilityDemoSeed.Seed(db);
            Assert.Equal(before, await db.ResourceBookings.CountAsync());
            await db.Entry(policy).ReloadAsync(); Assert.False(policy.Enabled);
        }
        finally { policy.Enabled = enabled; await db.SaveChangesAsync(); }
    }
}
