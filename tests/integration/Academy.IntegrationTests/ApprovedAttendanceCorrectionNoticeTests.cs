using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2PlanPublicationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Approved_attendance_decision_notice_opens_exact_request_for_requester_role_once(bool approve)
    {
        SetVisitClock();
        using var initial = await Staff();
        var f = await AttendanceReady(initial);
        var email = $"correction-notice-{Guid.NewGuid():N}@example.test";
        var created = await Write(initial, "/api/v1/manage/staff", new StaffWrite("إداري تصحيح اصطناعي", email,
            "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, null));
        created.EnsureSuccessStatusCode();
        var membership = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        factory.VisitClock.Now = factory.VisitClock.GetUtcNow().AddDays(1);
        using var owner = await Staff();
        using var admin = await Staff(email);
        var actions = StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m == "attendance", false, m == "attendance", false)).ToArray();
        (await Write(owner, $"/api/v1/manage/staff/{membership}/access", new StaffAccessWrite(false, [f.Branch], actions), true)).EnsureSuccessStatusCode();
        var id = await RequestCorrection(admin, f.Next, CorrectionBody(f.First));
        var version = await CorrectionVersion(owner, id);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var collections = await db.Collections.CountAsync();
        (await DecideCorrection(owner, id, version, approve)).EnsureSuccessStatusCode();
        (await DecideCorrection(owner, id, version, approve)).EnsureSuccessStatusCode();
        var notice = await db.AcademyNotices.SingleAsync(n => n.TargetKind == "AttendanceCorrection" && n.TargetId == id);
        Assert.Equal("AcademyAdmin", notice.AudienceRole);
        var requester = await db.AcademyMemberships.Where(m => m.Id == membership).Select(m => m.UserId).SingleAsync();
        Assert.Equal(requester, notice.UserId);
        var inbox = await admin.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
        Assert.Contains(inbox.GetProperty("items").EnumerateArray(), n => n.GetProperty("id").GetGuid() == notice.Id &&
            n.GetProperty("targetKind").GetString() == "AttendanceCorrection" && n.GetProperty("targetId").GetGuid() == id);
        var own = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/attendance/corrections/{id}");
        Assert.Equal(approve ? "Approved" : "Rejected", own.GetProperty("state").GetString());
        Assert.False(own.GetProperty("canApprove").GetBoolean());
        var ownerInbox = await owner.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
        Assert.DoesNotContain(ownerInbox.GetProperty("items").EnumerateArray(), n => n.GetProperty("id").GetGuid() == notice.Id);
        using var foreign = await Staff("owner.future@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/api/v1/attendance/corrections/{id}")).StatusCode);
        using var guardian = await Guardian();
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync($"/api/v1/attendance/corrections/{id}")).StatusCode);
        Assert.Equal(collections, await db.Collections.CountAsync());
    }
}
