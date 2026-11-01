using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Mobile;
using Academy.Api.Operations;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class BrandingMatchesTests
{
    [Fact]
    public async Task Coach_tasks_reuse_history_enforce_scope_and_replay_without_attendance_evaluation_or_money()
    {
        using var owner = await Staff(); using var coach = await Mobile(AcademyRole.Coach);
        using var guardian = await Mobile(AcademyRole.Guardian); using var other = await Staff(DemoSeed.FutureOwnerEmail);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var academy = DemoSeed.NogoomAcademyId;
        var coachIdentity = await coach.GetFromJsonAsync<JsonElement>("/api/v1/me");
        var member = await db.AcademyMemberships.SingleAsync(m => m.Id == coachIdentity.GetProperty("membershipId").GetGuid());
        var assignment = await db.StaffGroupAssignments.AsNoTracking().FirstAsync(a => a.AcademyId == academy && a.AcademyMembershipId == member.Id && a.IsActive);
        var group = assignment.TrainingGroupId;
        var anotherCoach = await db.AcademyMemberships.Include(m => m.User).FirstAsync(m => m.AcademyId == academy && m.Role == AcademyRole.Coach && m.IsActive && m.User.IsActive && m.PrimaryMembershipId == null && m.UserId != member.UserId);
        using var stranger = factory.CreateClient();
        var token = MobileAuthenticationHandler.NewToken(); var now = DateTimeOffset.UtcNow;
        db.MobileSessions.Add(new Academy.Infrastructure.Identity.MobileSession { Id = Guid.NewGuid(), UserId = anotherCoach.UserId, MembershipId = anotherCoach.Id, AccessTokenHash = MobileAuthenticationHandler.Hash(token), RefreshTokenHash = MobileAuthenticationHandler.Hash(MobileAuthenticationHandler.NewToken()), SecurityStamp = anotherCoach.User.SecurityStamp ?? "", DeviceName = "اختبار عزل مهام مدرب اصطناعي", CreatedAtUtc = now, LastUsedAtUtc = now, AccessExpiresAtUtc = now.AddMinutes(10), ExpiresAtUtc = now.AddHours(1) });
        await db.SaveChangesAsync();
        stranger.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var counts = (await db.PlayerAttendances.CountAsync(), await db.PlayerEvaluations.CountAsync(), await db.Collections.CountAsync());
        var body = new CoachTaskCreate(group, member.Id, "Attendance", Guid.NewGuid(), Note: "متابعة حضور اصطناعية");
        Guid? id = null;
        try
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, "/api/v1/manage/coach-tasks", body with { Kind = "Finance" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(coach, "/api/v1/manage/coach-tasks", body)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(other, "/api/v1/manage/coach-tasks", body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/v1/my-coach-tasks")).StatusCode);
            var created = await Send(owner, "/api/v1/manage/coach-tasks", body); created.EnsureSuccessStatusCode();
            id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var route = $"/api/v1/my-coach-tasks/{id}";
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/manage/coach-tasks/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/manage/owner-followups/{id}")).StatusCode);
            Assert.True((await (await Send(owner, "/api/v1/manage/coach-tasks", body)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replay").GetBoolean());
            Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, "/api/v1/manage/coach-tasks", body with { Note = "محاولة مختلفة" })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, "/api/v1/manage/coach-tasks", body with { RequestId = Guid.NewGuid() })).StatusCode);
            var detail = await coach.GetFromJsonAsync<JsonElement>(route);
            Assert.Equal("Attendance", detail.GetProperty("kind").GetString());
            Assert.Equal(group, detail.GetProperty("group").GetProperty("id").GetGuid());
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(stranger, route, new CoachTaskChange("InProgress", detail.GetProperty("version").GetUInt32(), Guid.NewGuid()))).StatusCode);
            var change = new CoachTaskChange("InProgress", detail.GetProperty("version").GetUInt32(), Guid.NewGuid(), Note: "بدأت المتابعة الاصطناعية");
            (await Send(coach, route, change)).EnsureSuccessStatusCode();
            Assert.True((await (await Send(coach, route, change)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replay").GetBoolean());
            Assert.Equal(HttpStatusCode.Conflict, (await Send(coach, route, change with { RequestId = Guid.NewGuid() })).StatusCode);
            detail = await coach.GetFromJsonAsync<JsonElement>(route);
            Assert.Equal(2, detail.GetProperty("history").GetArrayLength());
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(coach, route, new CoachTaskChange("Snoozed", detail.GetProperty("version").GetUInt32(), Guid.NewGuid()))).StatusCode);
            var close = new CoachTaskChange("Closed", detail.GetProperty("version").GetUInt32(), Guid.NewGuid());
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(coach, route, close)).StatusCode);
            (await Send(coach, route, close with { ResolutionReason = "انتهت المتابعة فقط دون تسجيل حضور آلي" })).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await coach.GetAsync("/api/v1/my-coach-tasks?pageSize=101")).StatusCode);
            var page = await coach.GetFromJsonAsync<JsonElement>("/api/v1/my-coach-tasks?page=1&pageSize=1");
            Assert.Equal(1, page.GetProperty("pageSize").GetInt32());
            Assert.Equal(counts, (await db.PlayerAttendances.CountAsync(), await db.PlayerEvaluations.CountAsync(), await db.Collections.CountAsync()));
        }
        finally
        {
            if (id.HasValue)
            {
                var latest = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/coach-tasks/{id}");
                if (latest.GetProperty("state").GetString() is not ("Closed" or "Rejected"))
                    (await Send(owner, $"/api/v1/manage/coach-tasks/{id}", new CoachTaskChange("Closed", latest.GetProperty("version").GetUInt32(), Guid.NewGuid(), ResolutionReason: "إغلاق تجهيز الاختبار الاصطناعي"))).EnsureSuccessStatusCode();
            }
        }
    }

    [Fact]
    public async Task Coach_tasks_require_explicit_admin_permission_and_lose_access_when_assignment_is_revoked()
    {
        using var owner = await Staff(); using var admin = await Staff(DemoSeed.AdminEmail); using var coach = await Mobile(AcademyRole.Coach);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var academy = DemoSeed.NogoomAcademyId;
        var adminIdentity = await admin.GetFromJsonAsync<JsonElement>("/api/v1/me");
        var coachIdentity = await coach.GetFromJsonAsync<JsonElement>("/api/v1/me");
        var membership = await db.AcademyMemberships.SingleAsync(m => m.Id == adminIdentity.GetProperty("membershipId").GetGuid());
        var coachMember = await db.AcademyMemberships.SingleAsync(m => m.Id == coachIdentity.GetProperty("membershipId").GetGuid());
        var assignment = await db.StaffGroupAssignments.FirstAsync(a => a.AcademyId == academy && a.AcademyMembershipId == coachMember.Id && a.IsActive);
        var original = (membership.UseActionGrants, membership.AllBranches, membership.AllSports);
        var module = await db.StaffActionGrants.SingleOrDefaultAsync(g => g.MembershipId == membership.Id && g.Module == "followups");
        var leaf = await db.StaffGrants.SingleOrDefaultAsync(g => g.MembershipId == membership.Id && g.Permission == CoachTaskEndpoints.AssignPermission);
        var priorModule = module is null ? null : new { module.IsActive, module.Read, module.Add, module.Edit, module.Delete };
        var priorLeaf = leaf is null ? null : new { leaf.IsActive, leaf.Allowed };
        Guid? id = null;
        try
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/manage/coach-tasks/options")).StatusCode);
            var catalogue = await admin.GetFromJsonAsync<JsonElement>("/api/v1/my-access");
            Assert.False(catalogue.GetProperty("workflowActions").EnumerateArray().Single(g => g.GetProperty("key").GetString() == CoachTaskEndpoints.AssignPermission).GetProperty("allowed").GetBoolean());
            membership.UseActionGrants = true; membership.AllBranches = true; membership.AllSports = true;
            if (module is null) { module = new() { Id = Guid.NewGuid(), AcademyId = academy, MembershipId = membership.Id, Module = "followups" }; db.StaffActionGrants.Add(module); }
            module.IsActive = true; module.Read = true; module.Add = false; module.Edit = true;
            if (leaf is null) { leaf = new() { Id = Guid.NewGuid(), AcademyId = academy, MembershipId = membership.Id, Permission = CoachTaskEndpoints.AssignPermission }; db.StaffGrants.Add(leaf); }
            leaf.IsActive = true; leaf.Allowed = false; await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/manage/coach-tasks/options")).StatusCode);
            leaf.Allowed = true; await db.SaveChangesAsync();
            var optionsResponse = await admin.GetAsync("/api/v1/manage/coach-tasks/options?pageSize=1");
            Assert.True(optionsResponse.IsSuccessStatusCode, $"Options failed: {optionsResponse.StatusCode}: {await optionsResponse.Content.ReadAsStringAsync()}");
            var options = await optionsResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, options.GetProperty("items").GetArrayLength());
            var body = new CoachTaskCreate(assignment.TrainingGroupId, coachMember.Id, "Evaluation", Guid.NewGuid());
            Assert.Equal(HttpStatusCode.NotFound, (await Send(admin, "/api/v1/manage/coach-tasks", body with { CoachId = Guid.NewGuid() })).StatusCode);
            var created = await Send(admin, "/api/v1/manage/coach-tasks", body); created.EnsureSuccessStatusCode();
            id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var route = $"/api/v1/my-coach-tasks/{id}";
            var detail = await coach.GetFromJsonAsync<JsonElement>(route);
            leaf.Allowed = false; await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/v1/manage/coach-tasks/{id}")).StatusCode);
            assignment.IsActive = false; await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.NotFound, (await coach.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(coach, route, new CoachTaskChange("InProgress", detail.GetProperty("version").GetUInt32(), Guid.NewGuid()))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/manage/coach-tasks/{id}")).StatusCode);
        }
        finally
        {
            assignment.IsActive = true; await db.SaveChangesAsync();
            if (id.HasValue)
            {
                var latest = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/coach-tasks/{id}");
                (await Send(owner, $"/api/v1/manage/coach-tasks/{id}", new CoachTaskChange("Closed", latest.GetProperty("version").GetUInt32(), Guid.NewGuid(), ResolutionReason: "إغلاق تجهيز الاختبار الاصطناعي"))).EnsureSuccessStatusCode();
            }
            (membership.UseActionGrants, membership.AllBranches, membership.AllSports) = original;
            if (module is not null) { module.IsActive = priorModule?.IsActive ?? false; module.Read = priorModule?.Read ?? false; module.Add = priorModule?.Add ?? false; module.Edit = priorModule?.Edit ?? false; module.Delete = priorModule?.Delete ?? false; }
            if (leaf is not null) { leaf.IsActive = priorLeaf?.IsActive ?? false; leaf.Allowed = priorLeaf?.Allowed ?? false; }
            await db.SaveChangesAsync();
        }
    }
}
