using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class StaffDeviceCheckInTests : IAsyncLifetime
{
    private Factory factory = null!;
    private Guid branchId, coachId;
    private double? priorLat, priorLon;
    private Guid[] existingSiteAuditIds = [];
    private readonly DateOnly today = new(2026, 10, 14);
    public Task InitializeAsync()
    {
        factory = new(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Isolated test database required"));
        _ = factory.CreateClient();
        return Task.CompletedTask;
    }
    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        if (branchId != Guid.Empty)
        {
            await db.StaffDailyAttendances.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.MembershipId == coachId && x.WorkDate == today).ExecuteDeleteAsync();
            await db.BranchCheckInSiteAudits.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.BranchId == branchId && !existingSiteAuditIds.Contains(x.Id)).ExecuteDeleteAsync();
            var branch = await db.Branches.SingleAsync(x => x.Id == branchId);
            branch.CheckInLatitude = priorLat; branch.CheckInLongitude = priorLon;
            await db.SaveChangesAsync();
        }
        await factory.DisposeAsync();
    }
    private async Task<HttpClient> Owner(string email = DemoSeed.OwnerEmail)
    {
        var client = factory.CreateClient();
        (await Send(client, "/api/v1/auth/login", new { email, password = "Demo-Only-123!" })).EnsureSuccessStatusCode();
        return client;
    }
    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, object body, bool put = false)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        var request = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Coach_needs_assigned_configured_branch_biometric_and_precise_50m_location()
    {
        using var owner = await Owner(); using var foreign = await Owner(DemoSeed.FutureOwnerEmail);
        using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var user = await db.Users.SingleAsync(x => x.NormalizedEmail == DemoSeed.CoachEmail.ToUpperInvariant());
            coachId = await db.AcademyMemberships.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.UserId == user.Id && x.Role == AcademyRole.Coach).Select(x => x.Id).SingleAsync();
            branchId = await db.StaffGroupAssignments.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.AcademyMembershipId == coachId && x.IsActive).Select(x => x.TrainingGroup.BranchId).FirstAsync();
            var branch = await db.Branches.SingleAsync(x => x.Id == branchId); priorLat = branch.CheckInLatitude; priorLon = branch.CheckInLongitude;
            existingSiteAuditIds = await db.BranchCheckInSiteAudits.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.BranchId == branchId).Select(x => x.Id).ToArrayAsync();
        }
        var path = $"/api/v1/manage/branches/{branchId}/check-in-site";
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(coach, path, new { latitude = 30.05, longitude = 31.3 }, put: true)).StatusCode);
        (await Send(owner, path, new { latitude = 30.05, longitude = 31.3 }, put: true)).EnsureSuccessStatusCode();
        var site = await owner.GetFromJsonAsync<JsonElement>(path);
        Assert.Contains(site.GetProperty("history").EnumerateArray(), x => x.GetProperty("afterLatitude").GetDouble() == 30.05);
        var sites = await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/check-in/sites");
        Assert.Contains(sites.EnumerateArray(), x => x.GetProperty("id").GetGuid() == branchId);
        var body = new DeviceCheckInWrite(branchId, 30.05, 31.3, 5, true);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(coach, "/api/v1/staff/check-in/", body with { ClientBiometricAcknowledged = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(coach, "/api/v1/staff/check-in/", body with { Latitude = 30.06 })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(coach, "/api/v1/staff/check-in/", body with { AccuracyMeters = 26 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(coach, "/api/v1/staff/check-in/", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(coach, "/api/v1/staff/check-in/", body)).StatusCode);
        await using var verify = factory.Services.CreateAsyncScope(); var check = verify.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var row = await check.StaffDailyAttendances.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.MembershipId == coachId && x.WorkDate == today);
        Assert.Equal("Present", row.Status); Assert.Equal(0, row.ApprovedDeduction);
    }

    private sealed class Factory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Demo"); builder.UseSetting("ConnectionStrings:Default", connection);
            builder.UseSetting("Demo:SeedProfile", "LegacyRegression"); builder.UseSetting("Demo:SeedEnabled", "true");
            builder.UseSetting("Demo:FixedOtpEnabled", "true"); builder.UseSetting("Demo:FixedOtp", "246810");
            builder.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); builder.UseSetting("Demo:ReferenceDate", "2026-09-28");
            builder.ConfigureServices(s => { s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(new TestTime()); });
        }
    }
    private sealed class TestTime : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-10-14T06:00:00Z"); }
}
