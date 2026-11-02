using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;
using Academy.Infrastructure.Identity;
using Npgsql;

namespace Academy.IntegrationTests;

public sealed partial class V2StaffScopeTests
{
    private const string RoleTemplates = "/api/v1/manage/staff/role-templates";
    private static StaffRoleTemplateWrite RoleBody(string? name = null) => new(name ?? "قالب اختبار اصطناعي " + Guid.NewGuid().ToString("N")[..12], "قالب للقراءة من بيانات اختبار فقط", StaffAccessTemplates.All.Single(t => t.Key == "read-only").Actions);
    private static async Task<JsonElement> CreateTemplate(HttpClient owner, StaffRoleTemplateWrite body)
    {
        var result = await Write(owner, RoleTemplates, body, false); Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        return await result.Content.ReadFromJsonAsync<JsonElement>();
    }
    [Fact]
    public async Task V3_custom_template_demo_preserves_owner_archive_and_never_applies_it_automatically()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>(); var options = scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;
        await SportScopeDemoSeed.Seed(db, users, env, options); await SportScopeDemoSeed.Seed(db, users, env, options);
        var row = await db.StaffRoleTemplates.SingleAsync(r => r.Id == SportScopeDemoSeed.TemplateId); var original = row.IsActive;
        var member = await db.AcademyMemberships.SingleAsync(m => m.Id == SportScopeDemoSeed.MembershipId);
        Assert.Null(member.PermissionTemplateId);
        try { row.IsActive = false; await db.SaveChangesAsync(); await SportScopeDemoSeed.Seed(db, users, env, options); await db.Entry(row).ReloadAsync(); Assert.False(row.IsActive); }
        finally { row.IsActive = original; await db.SaveChangesAsync(); }
        Assert.Equal(1, await db.StaffRoleTemplateAudits.CountAsync(a => a.TemplateId == row.Id));
    }
    [Fact]
    public async Task V3_custom_template_is_owner_only_tenant_bound_validated_versioned_and_append_only_audited()
    {
        var fixture = await CreateAdmin(); using var owner = fixture.Owner; using var admin = fixture.Admin; using var foreign = await Staff(DemoSeed.FutureOwnerEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(RoleTemplates)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, RoleTemplates, RoleBody(), false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(RoleTemplates, RoleBody())).StatusCode);
        var body = RoleBody(); var row = await CreateTemplate(owner, body); var id = row.GetProperty("id").GetGuid();
        Assert.Equal(1, row.GetProperty("template").GetProperty("revision").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"{RoleTemplates}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(foreign, $"{RoleTemplates}/{id}", body with { Revision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, RoleTemplates, body with { Name = "  " + body.Name + "  " }, false)).StatusCode);
        foreach (var invalid in new[] { body with { Name = " " }, body with { Name = new string('x', 81) }, body with { Actions = body.Actions[..^1] }, body with { Actions = body.Actions.Select(a => a.Module == "payroll" ? a with { Read = false, Add = true } : a).ToArray() } })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, RoleTemplates, invalid, false)).StatusCode);
        (await Write(owner, $"{RoleTemplates}/{id}", body with { Revision = 1, Description = "وصف محدث" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"{RoleTemplates}/{id}", body with { Revision = 1 })).StatusCode);
        var detailResponse = await owner.GetAsync($"{RoleTemplates}/{id}"); Assert.Contains("no-store", detailResponse.Headers.CacheControl!.ToString());
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(2, detail.GetProperty("history").GetArrayLength());
        Assert.Equal(2, detail.GetProperty("template").GetProperty("revision").GetInt32());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var audit = await db.StaffRoleTemplateAudits.FirstAsync(a => a.TemplateId == id); audit.After = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StaffRoleTemplateAudits\" SET \"After\"='{{}}'::jsonb WHERE \"Id\"={audit.Id}"));
        Assert.Equal("23514", rejected.SqlState);
    }
    [Fact]
    public async Task V3_custom_template_requires_current_revision_and_explicit_apply_never_unions_or_changes_account_role()
    {
        var fixture = await CreateAdmin(); using var owner = fixture.Owner; using var admin = fixture.Admin; var data = await Data();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var sport = await db.TrainingGroups.Where(g => g.Id == data.GroupA).Select(g => g.SportId).SingleAsync();
        var body = RoleBody("دور مالك بالاسم فقط " + Guid.NewGuid().ToString("N")[..8]); var role = await CreateTemplate(owner, body); var id = role.GetProperty("id").GetGuid(); var key = role.GetProperty("template").GetProperty("key").GetString()!;
        var grant = new StaffAccessWrite(false, [data.A], body.Actions, key, false, [sport], 1);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", grant with { TemplateRevision = null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/people/players")).StatusCode);
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", grant)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/people/players")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(RoleTemplates)).StatusCode);
        Assert.Equal(AcademyRole.AcademyAdmin, (await db.AcademyMemberships.AsNoTracking().SingleAsync(m => m.Id == fixture.Member)).Role);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/people/players/{data.PlayerB}")).StatusCode);
        var next = body.Actions.Select(a => a.Module == "players" ? a with { Read = false, Add = false, Edit = false, Delete = false } : a).ToArray();
        (await Write(owner, $"{RoleTemplates}/{id}", body with { Revision = 1, Actions = next })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/people/players")).StatusCode); // definition edit is NOT live grant propagation
        var source = (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff/{fixture.Member}/access")).GetProperty("source");
        Assert.Equal(1, source.GetProperty("appliedRevision").GetInt32()); Assert.Equal(2, source.GetProperty("currentRevision").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", grant)).StatusCode);
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", grant with { Actions = next, TemplateRevision = 2 })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/people/players")).StatusCode);
        var membership = await db.AcademyMemberships.AsNoTracking().SingleAsync(m => m.Id == fixture.Member);
        Assert.Equal(id, membership.PermissionTemplateId); Assert.Equal(2, membership.PermissionTemplateRevision); Assert.False(membership.PermissionTemplateCustomized); Assert.False(membership.AllSports); Assert.False(membership.AllBranches);
        (await Write(owner, $"{RoleTemplates}/{id}", body with { Revision = 2, Actions = next, IsActive = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/attendance/sessions?allDates=true")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", grant with { Actions = next, TemplateRevision = 3 })).StatusCode);
        var page = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff/{fixture.Member}/access");
        Assert.DoesNotContain(page.GetProperty("templates").EnumerateArray(), t => t.GetProperty("key").GetString() == key);
        Assert.False(page.GetProperty("source").GetProperty("isActive").GetBoolean());
    }
    [Fact]
    public async Task V3_template_provenance_marks_individual_edits_preserves_scope_only_changes_and_rejects_foreign_source()
    {
        var fixture = await CreateAdmin(); using var owner = fixture.Owner; using var admin = fixture.Admin; using var foreign = await Staff(DemoSeed.FutureOwnerEmail); var data = await Data();
        var body = RoleBody(); var role = await CreateTemplate(owner, body); var id = role.GetProperty("id").GetGuid();
        var otherRole = await CreateTemplate(foreign, RoleBody());
        var grant = new StaffAccessWrite(false, [data.A], body.Actions, role.GetProperty("template").GetProperty("key").GetString(), TemplateRevision: 1);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", grant with { TemplateKey = otherRole.GetProperty("template").GetProperty("key").GetString() })).StatusCode);
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", grant)).EnsureSuccessStatusCode();
        var customized = body.Actions.Select(a => a.Module == "players" ? a with { Add = true } : a).ToArray();
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", grant with { TemplateKey = null, TemplateRevision = null, Actions = customized })).EnsureSuccessStatusCode();
        var source = (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff/{fixture.Member}/access")).GetProperty("source");
        Assert.True(source.GetProperty("customized").GetBoolean());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var after = await db.StaffAccessAudits.Where(a => a.MembershipId == fixture.Member).OrderByDescending(a => a.CreatedAtUtc).Select(a => a.After).FirstAsync();
        var applied = JsonDocument.Parse(after).RootElement.GetProperty("AppliedTemplate");
        Assert.Equal(id, applied.GetProperty("PermissionTemplateId").GetGuid()); Assert.True(applied.GetProperty("PermissionTemplateCustomized").GetBoolean());
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", grant with { TemplateKey = null, TemplateRevision = null, Actions = customized, Branches = [data.B] })).EnsureSuccessStatusCode();
        var detail = await owner.GetFromJsonAsync<JsonElement>($"{RoleTemplates}/{id}");
        Assert.Equal(fixture.Member, detail.GetProperty("applied")[0].GetProperty("id").GetGuid()); Assert.True(detail.GetProperty("applied")[0].GetProperty("customized").GetBoolean());
        (await Write(owner, $"/api/v1/manage/staff/{fixture.Member}/access", grant with { TemplateKey = "read-only", TemplateRevision = null })).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff/{fixture.Member}/access")).GetProperty("source").ValueKind);
    }
}
