using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Academy.Infrastructure.Tenancy;

namespace Academy.IntegrationTests;

public sealed partial class BrandingMatchesTests
{
    private static async Task<JsonElement> HelpRead(HttpClient c, string suffix = "") => await c.GetFromJsonAsync<JsonElement>("/api/v1/help/lessons" + suffix);
    private static string[] HelpIds(JsonElement value) => value.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()!).ToArray();
    private static async Task HelpActivate(HttpClient owner, JsonElement settings)
    {
        var draft = await Send(owner, "/api/v1/manage/configuration/drafts", settings); draft.EnsureSuccessStatusCode(); var id = (await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        foreach (var state in new[] { "Validated", "Staged", "Active" })
        {
            var list = await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/configuration/"); var row = list.GetProperty("versions").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);
            (await Send(owner, $"/api/v1/manage/configuration/{id}/transition", new { target = state, version = row.GetProperty("version").GetUInt32() })).EnsureSuccessStatusCode();
        }
    }
    private async Task HelpFixture(Func<HttpClient, HttpClient, Task> action)
    {
        using var owner = await Staff(); using var platform = factory.CreateClient();
        async Task<HttpResponseMessage> PlatformWrite(string path, object body, bool put = false) { var csrf = await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/auth/csrf"); var r = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(body) }; r.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); return await platform.SendAsync(r); }
        (await PlatformWrite("/api/v1/platform/auth/login", new { email = Academy.Api.Platform.PlatformIdentity.DemoEmail, password = "Demo-Only-123!", code = "246810" })).EnsureSuccessStatusCode();
        var licenses = await platform.GetFromJsonAsync<JsonElement>($"/api/v1/platform/academies/{DemoSeed.NogoomAcademyId}/entitlements"); var before = licenses.EnumerateArray().ToDictionary(x => x.GetProperty("feature").GetString()!, x => x.GetProperty("enabled").GetBoolean()); var plan = licenses[0].GetProperty("planCode").GetString();
        var config = await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/configuration/"); var original = config.GetProperty("versions").EnumerateArray().Single(x => x.GetProperty("state").GetString() == "Active").GetProperty("settings").Clone();
        var settings = System.Text.Json.Nodes.JsonNode.Parse(original.GetRawText())!; settings["features"]!["learning_hub"] = true; settings["features"]!["facilities"] = true;
        try
        {
            var enabled = new Dictionary<string, bool>(before) { ["learning_hub"] = true, ["facilities"] = true };
            (await PlatformWrite($"/api/v1/platform/academies/{DemoSeed.NogoomAcademyId}/entitlements", new { planCode = "Premium", features = enabled }, true)).EnsureSuccessStatusCode();
            await HelpActivate(owner, JsonSerializer.SerializeToElement(settings)); await action(owner, platform);
        }
        finally
        {
            await HelpActivate(owner, original);
            (await PlatformWrite($"/api/v1/platform/academies/{DemoSeed.NogoomAcademyId}/entitlements", new { planCode = plan, features = before }, true)).EnsureSuccessStatusCode();
        }
    }
    [Fact]
    public async Task Help_four_roles_receive_only_eligible_written_guides_without_side_effects() => await HelpFixture(async (owner, _) =>
    {
        using var admin = await Staff(DemoSeed.AdminEmail); using var coach = await Mobile(AcademyRole.Coach); using var guardian = await Mobile(AcademyRole.Guardian);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        async Task<int[]> Counts() => [await db.SubscriptionPeriods.CountAsync(), await db.PlayerAttendances.CountAsync(), await db.StaffAttendances.CountAsync()];
        var before = await Counts(); var financial = await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28");
        Assert.Contains("owner-dashboard", HelpIds(await HelpRead(owner))); Assert.Contains("swimming-lanes", HelpIds(await HelpRead(owner)));
        Assert.DoesNotContain("owner-dashboard", HelpIds(await HelpRead(admin))); Assert.Contains("player-attendance", HelpIds(await HelpRead(admin)));
        var coachIds = HelpIds(await HelpRead(coach)); Assert.Contains("coach-sessions", coachIds); Assert.Contains("evaluations", coachIds); Assert.DoesNotContain("player-attendance", coachIds); Assert.DoesNotContain("payroll-substitutes", coachIds);
        Assert.Equal(new[] { "family-renewal", "family-progress" }, HelpIds(await HelpRead(guardian)));
        foreach (var c in new[] { owner, admin, coach, guardian }) foreach (var id in HelpIds(await HelpRead(c)))
        {
            var detail = await c.GetFromJsonAsync<JsonElement>("/api/v1/help/lessons/" + id); Assert.Equal("AwaitingUpload", detail.GetProperty("videoStatus").GetString()); Assert.Equal(JsonValueKind.Null, detail.GetProperty("videoUrl").ValueKind); Assert.True(detail.GetProperty("steps").GetArrayLength() > 0); Assert.Equal(1, detail.GetProperty("version").GetInt32());
        }
        Assert.Equal(before, await Counts()); Assert.Equal(financial, await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
    });
    [Fact]
    public async Task Help_arabic_search_section_limits_private_headers_and_cross_tenant_concealment() => await HelpFixture(async (owner, _) =>
    {
        using var guardian = await Mobile(AcademyRole.Guardian); using var other = await Staff(DemoSeed.FutureOwnerEmail); using var anonymous = factory.CreateClient();
        var search = await HelpRead(owner, "?search=" + Uri.EscapeDataString("إِنْشَاء بَاقَة")); Assert.Equal(new[] { "packages" }, HelpIds(search));
        Assert.All((await HelpRead(owner, "?section=operations")).GetProperty("items").EnumerateArray(), x => Assert.Equal("operations", x.GetProperty("section").GetString()));
        Assert.Empty(HelpIds(await HelpRead(owner, "?search=unmatched-synthetic-value")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.GetAsync("/api/v1/help/lessons?search=" + new string('x', 101))).StatusCode);
        var detail = await owner.GetAsync("/api/v1/help/lessons/packages"); Assert.True(detail.Headers.CacheControl!.Private); Assert.True(detail.Headers.CacheControl.NoStore);
        Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync("/api/v1/help/lessons/owner-dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync("/api/v1/help/lessons/owner-dashboard?academyId=" + DemoSeed.NogoomAcademyId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/v1/help/lessons?academyId=" + DemoSeed.NogoomAcademyId)).StatusCode);
        Assert.False((await other.GetFromJsonAsync<JsonElement>("/api/v1/help/availability")).GetProperty("available").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/api/v1/help/lessons/packages")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/help/lessons")).StatusCode);
    });
    [Fact]
    public async Task Help_revokes_immediately_on_license_or_permission_loss_and_does_not_self_license() => await HelpFixture(async (owner, _) =>
    {
        using var admin = await Staff(DemoSeed.AdminEmail); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var license = await db.AcademyFeatureEntitlements.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.Feature == "learning_hub");
        try { license.Enabled = false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/help/lessons")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/v1/help/lessons/owner-dashboard")).StatusCode); Assert.False((await owner.GetFromJsonAsync<JsonElement>("/api/v1/help/availability")).GetProperty("available").GetBoolean()); }
        finally { license.Enabled = true; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/v1/help/lessons/owner-dashboard")).StatusCode);
        var m = await db.AcademyMemberships.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.User.Email == DemoSeed.AdminEmail); var original = m.UseActionGrants;
        var grants = await db.StaffActionGrants.Where(x => x.MembershipId == m.Id).ToListAsync(); var reads = grants.ToDictionary(g => g.Id, g => g.Read);
        try { m.UseActionGrants = true; foreach (var g in grants) g.Read = false; await db.SaveChangesAsync(); Assert.DoesNotContain("player-attendance", HelpIds(await HelpRead(admin))); Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/help/lessons/player-attendance")).StatusCode); }
        finally { m.UseActionGrants = original; foreach (var g in grants) g.Read = reads[g.Id]; await db.SaveChangesAsync(); }
    });
    [Fact]
    public async Task Help_scoped_admin_can_read_only_granted_guidance_without_gaining_write_access() => await HelpFixture(async (owner, _) =>
    {
        using var admin = await Staff(DemoSeed.AdminEmail); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var m = await db.AcademyMemberships.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.User.Email == DemoSeed.AdminEmail); var before = (m.AllSports, m.UseActionGrants);
        var grants = await db.StaffActionGrants.Where(x => x.MembershipId == m.Id).ToListAsync(); var reads = grants.ToDictionary(g => g.Id, g => g.Read);
        var ownGrant = new StaffActionGrant { Id = Guid.NewGuid(), AcademyId = m.AcademyId, MembershipId = m.Id, Module = "attendance", Read = true };
        var grant = grants.FirstOrDefault(g => g.Module == "attendance" && g.IsActive); var ownSport = new MembershipSportAccess { Id = Guid.NewGuid(), AcademyId = m.AcademyId, MembershipId = m.Id, SportId = Academy.Api.Slice2.Slice2DemoSeed.FootballId };
        var addedSport = !await db.MembershipSportAccesses.AnyAsync(s => s.MembershipId == m.Id && s.SportId == ownSport.SportId && s.IsActive);
        try
        {
            m.AllSports = false; m.UseActionGrants = true; foreach (var g in grants) g.Read = false;
            if (grant == null) db.StaffActionGrants.Add(ownGrant); else grant.Read = true;
            if (addedSport) db.MembershipSportAccesses.Add(ownSport); await db.SaveChangesAsync();
            Assert.True((await admin.GetFromJsonAsync<JsonElement>("/api/v1/help/availability")).GetProperty("available").GetBoolean());
            Assert.Equal(new[] { "player-attendance" }, HelpIds(await HelpRead(admin)));
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/help/lessons/player-attendance")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/help/lessons/evaluations")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, "/api/v1/help/lessons/player-attendance", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, $"/api/v1/attendance/sessions/{Guid.NewGuid()}/players", new { items = Array.Empty<object>() }, true)).StatusCode);
        }
        finally { m.AllSports = before.AllSports; m.UseActionGrants = before.UseActionGrants; foreach (var g in grants) g.Read = reads[g.Id]; if (grant == null) db.StaffActionGrants.Remove(ownGrant); if (addedSport) db.MembershipSportAccesses.Remove(ownSport); await db.SaveChangesAsync(); }
    });
}
