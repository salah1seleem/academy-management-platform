using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Api.Operations;
using Academy.Infrastructure.Tenancy;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    private static string CardPath(Guid player) => $"/api/v1/attendance/players/{player}/card";
    private static async Task<JsonElement> IssueCard(HttpClient owner, Guid player, uint version = 0, string? key = null) {
        var csrf = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var req = new HttpRequestMessage(HttpMethod.Post, CardPath(player)) { Content = JsonContent.Create(new PlayerCardWrite(version)) }; req.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); req.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString()); var r = await owner.SendAsync(req); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Content.ReadFromJsonAsync<JsonElement>();
    }
    [Fact]
    public async Task Player_card_is_opaque_encrypted_printable_idempotent_and_never_changes_attendance_or_money()
    {
        using var owner = await Staff(); var f = await AttendanceReady(owner); var player = await PlayerFor(f.First); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var before = (await db.PlayerAttendances.CountAsync(), await db.Collections.CountAsync(), await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == f.First).Select(p => p.RemainingSessions).SingleAsync()); var key = Guid.NewGuid().ToString(); var card = await IssueCard(owner, player, key: key); var code = card.GetProperty("code").GetString()!;
        Assert.StartsWith("academity:player:v1:", code); Assert.Equal(84, code.Length); Assert.DoesNotContain(player.ToString(), code); Assert.DoesNotContain("PUB-", code); Assert.Equal(new byte[] { 137,80,78,71,13,10,26,10 }, Convert.FromBase64String(card.GetProperty("pngBase64").GetString()!)[..8]); Assert.Equal(code, (await IssueCard(owner, player, key: key)).GetProperty("code").GetString()); Assert.Single(await db.PlayerCards.Where(c => c.PlayerId == player).ToListAsync());
        var saved = await db.PlayerCards.SingleAsync(c => c.PlayerId == player); Assert.DoesNotContain(code, saved.ProtectedToken); Assert.NotEqual(code, saved.TokenHash);
        var resolve = await Write(owner, $"/api/v1/attendance/sessions/{f.Next}/player-card/resolve", new PlayerCardResolve(code)); resolve.EnsureSuccessStatusCode(); var data = await resolve.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(data.GetProperty("identityCheckRequired").GetBoolean()); Assert.Equal(f.First, data.GetProperty("player").GetProperty("sportEnrollmentId").GetGuid()); Assert.False(data.GetProperty("player").TryGetProperty("medical", out _)); Assert.Equal(before, (await db.PlayerAttendances.CountAsync(), await db.Collections.CountAsync(), await db.SubscriptionPeriods.Where(p => p.SportEnrollmentId == f.First).Select(p => p.RemainingSessions).SingleAsync()));
        Assert.Contains("no-store", (await owner.GetAsync(CardPath(player))).Headers.CacheControl!.ToString());
    }
    [Fact]
    public async Task Player_card_rotation_and_revocation_reject_old_card_and_stale_version_without_duplicating_history()
    {
        using var owner = await Staff(); var f = await AttendanceReady(owner); var player = await PlayerFor(f.First); var card = await IssueCard(owner, player); var second = await IssueCard(owner, player, card.GetProperty("version").GetUInt32()); var resolve = $"/api/v1/attendance/sessions/{f.Next}/player-card/resolve";
        Assert.Equal(HttpStatusCode.NotFound, (await Write(owner, resolve, new PlayerCardResolve(card.GetProperty("code").GetString()!))).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, CardPath(player)+"/revoke", new PlayerCardWrite(0), true)).StatusCode); (await Write(owner, CardPath(player)+"/revoke", new PlayerCardWrite(second.GetProperty("version").GetUInt32()), true)).EnsureSuccessStatusCode(); (await Write(owner, CardPath(player)+"/revoke", new PlayerCardWrite(0), true)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.NotFound, (await Write(owner, resolve, new PlayerCardResolve(second.GetProperty("code").GetString()!))).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Equal(2, await db.PlayerCards.CountAsync(c => c.PlayerId == player)); Assert.False(await db.PlayerCards.AnyAsync(c => c.PlayerId == player && c.IsActive)); var row = await db.PlayerCards.FirstAsync(c => c.PlayerId == player); Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PlayerCards\" WHERE \"Id\"={row.Id}"))).SqlState); row.IsActive = true; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task Player_card_rejects_other_tenant_unlinked_guardian_coach_default_unknown_code_CSRF_future_and_cancelled()
    {
        using var owner = await Staff(); var f = await AttendanceReady(owner); var player = await PlayerFor(f.First); var card = await IssueCard(owner, player); var code = new PlayerCardResolve(card.GetProperty("code").GetString()!); var resolve = $"/api/v1/attendance/sessions/{f.Next}/player-card/resolve"; using var foreign = await Staff("owner.future@example.test"); using var guardian = await Guardian(); using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(resolve, code)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Write(guardian, resolve, code)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Write(coach, resolve, code)).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await Write(foreign, resolve, code)).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(CardPath(player))).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await Write(owner, resolve, new PlayerCardResolve("academity:player:v1:" + new string('A',64)))).StatusCode);
        var mine = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{player}/card"); Assert.Equal(code.Code, mine.GetProperty("code").GetString()); var other = await Fixture(false); Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync($"/api/v1/guardian/children/{await PlayerFor(other.Enrollment)}/card")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var s = await db.TrainingSessions.SingleAsync(s => s.Id == f.Next); s.SessionDate = PlacementToday.AddDays(1); await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, resolve, code)).StatusCode); s.SessionDate = PlacementToday; s.Status = TrainingSessionStatus.Cancelled; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, resolve, code)).StatusCode);
    }
    [Fact]
    public async Task Player_card_is_only_valid_for_its_assigned_session_and_manual_roster_still_works()
    {
        using var owner = await Staff(); var f = await AttendanceReady(owner); var other = await AttendanceReady(owner); var player = await PlayerFor(f.First); var card = await IssueCard(owner, player); Assert.Equal(HttpStatusCode.NotFound, (await Write(owner, $"/api/v1/attendance/sessions/{other.Next}/player-card/resolve", new PlayerCardResolve(card.GetProperty("code").GetString()!))).StatusCode);
        (await AttendanceWrite(owner, f.Next, new PlayerAttendanceChange(f.First, AttendanceStatus.Late, 0))).EnsureSuccessStatusCode(); Assert.NotEqual(0u, await AttendanceVersion(owner, f.Next, f.First));
    }
    [Fact]
    public async Task Player_card_concurrent_issuance_has_one_winner_and_one_active_card()
    {
        using var owner = await Staff(); var f = await AttendanceReady(owner); var player = await PlayerFor(f.First); var results = await Task.WhenAll(Write(owner, CardPath(player), new PlayerCardWrite(0)), Write(owner, CardPath(player), new PlayerCardWrite(0))); Assert.Single(results, r => r.IsSuccessStatusCode); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); Assert.Single(await db.PlayerCards.Where(c => c.PlayerId == player && c.IsActive).ToArrayAsync());
    }
    [Fact]
    public async Task Player_card_branch_and_independent_read_add_edit_permissions_are_rechecked_each_request()
    {
        using var owner = await Staff(); var f = await AttendanceReady(owner); var player = await PlayerFor(f.First); var email = "card.scope." + Guid.NewGuid().ToString("N") + "@example.test"; var created = await Write(owner, "/api/v1/manage/staff", new StaffWrite("إداري بطاقات اصطناعي", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, "اختبار")); created.EnsureSuccessStatusCode(); var member = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); using var admin = await Staff(email);
        StaffAccessWrite Access(Guid branch, bool read = true, bool add = false, bool edit = false) => new(false, [branch], StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m == "attendance" ? read : true, m == "attendance" && add, m == "attendance" && edit, false)).ToArray());
        (await Write(owner, $"/api/v1/manage/staff/{member}/access", Access(f.Branch), true)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(CardPath(player))).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, CardPath(player), new PlayerCardWrite(0))).StatusCode);
        (await Write(owner, $"/api/v1/manage/staff/{member}/access", Access(f.OtherBranch, add:true, edit:true), true)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(CardPath(player))).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await Write(admin, CardPath(player), new PlayerCardWrite(0))).StatusCode);
        (await Write(owner, $"/api/v1/manage/staff/{member}/access", Access(f.Branch, add:true), true)).EnsureSuccessStatusCode(); var card = await IssueCard(admin, player); Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, CardPath(player)+"/revoke", new PlayerCardWrite(card.GetProperty("version").GetUInt32()), true)).StatusCode);
        (await Write(owner, $"/api/v1/manage/staff/{member}/access", Access(f.Branch, read:false), true)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(CardPath(player))).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, $"/api/v1/attendance/sessions/{f.Next}/player-card/resolve", new PlayerCardResolve(card.GetProperty("code").GetString()!))).StatusCode);
    }
    [Fact]
    public async Task Player_card_expired_token_cannot_resolve_and_read_does_not_silently_issue_a_replacement()
    {
        using var owner = await Staff(); var f = await AttendanceReady(owner); var player = await PlayerFor(f.First); var card = await IssueCard(owner, player); (await Write(owner, CardPath(player)+"/revoke", new PlayerCardWrite(card.GetProperty("version").GetUInt32()), true)).EnsureSuccessStatusCode(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var prior = await db.PlayerCards.SingleAsync(c => c.PlayerId == player); var expiredCode = PlayerCardCode.Create(); var row = new PlayerCard { Id = Guid.NewGuid(), AcademyId = prior.AcademyId, PlayerId = player, ProtectedToken = prior.ProtectedToken, TokenHash = PlayerCardCode.Hash(expiredCode), ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(-1), IssuedByUserId = prior.IssuedByUserId, RequestKey = Guid.NewGuid().ToString(), CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-2), UpdatedAtUtc = DateTimeOffset.UtcNow.AddDays(-2) }; db.PlayerCards.Add(row); await db.SaveChangesAsync(); var before = await db.PlayerCards.CountAsync(c => c.PlayerId == player); var read = await owner.GetFromJsonAsync<JsonElement>(CardPath(player)); Assert.False(read.GetProperty("active").GetBoolean()); Assert.Equal(JsonValueKind.Null, read.GetProperty("code").ValueKind); Assert.Equal(JsonValueKind.Null, read.GetProperty("pngBase64").ValueKind); Assert.Equal(before, await db.PlayerCards.CountAsync(c => c.PlayerId == player));
        Assert.Equal(HttpStatusCode.NotFound, (await Write(owner, $"/api/v1/attendance/sessions/{f.Next}/player-card/resolve", new PlayerCardResolve(expiredCode))).StatusCode);
    }
}
