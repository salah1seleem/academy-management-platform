using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    private static async Task<JsonElement> AssignCase(HttpClient owner, Guid id, Guid? member, string? brief = "راجع تنظيم وقت التدريب دون الرجوع لبيانات الأسر")
    {
        var path = $"/api/v1/manage/feedback/{id}";
        var row = await owner.GetFromJsonAsync<JsonElement>(path);
        (await Send(owner, path + "/assignment", new FeedbackAssignmentWrite(member, member == null ? null : brief, row.GetProperty("version").GetUInt32(), Guid.NewGuid()))).EnsureSuccessStatusCode();
        return await owner.GetFromJsonAsync<JsonElement>(path);
    }
    [Fact] public async Task Feedback_investigation_private_brief_findings_replay_audit_and_cancel_do_not_publish_or_change_money() => await DelegateFixture(async (owner, admin, db, group, member) => {
        var notes = await OverdueFamilyNotes(db, group); var f = notes[0]; var path = $"/api/v1/manage/feedback/{f.Id}";
        var report = await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"); var notices = await db.AcademyNotices.CountAsync();
        var options = await owner.GetFromJsonAsync<JsonElement>(path + "/investigators"); Assert.Contains(options.GetProperty("items").EnumerateArray(), m => m.GetProperty("id").GetGuid() == member);
        var before = await owner.GetFromJsonAsync<JsonElement>(path);
        var assignment = new FeedbackAssignmentWrite(member, "موجز مستقل اصطناعي: راجع تنظيم وقت التدريب", before.GetProperty("version").GetUInt32(), Guid.NewGuid());
        Assert.All(await Task.WhenAll(Send(owner, path + "/assignment", assignment), Send(owner, path + "/assignment", assignment)), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path + "/assignment", assignment with { Brief = "موجز مختلف" })).StatusCode);
        var source = await owner.GetFromJsonAsync<JsonElement>(path); var id = source.GetProperty("investigationId").GetGuid(); Assert.NotEqual(f.Id, id);
        var delegatePath = $"/api/v1/my-followups/investigations/{id}";
        var inbox = await admin.GetFromJsonAsync<JsonElement>("/api/v1/my-followups/investigations"); Assert.Contains(inbox.GetProperty("items").EnumerateArray(), x => x.GetProperty("assignmentId").GetGuid() == id);
        var response = await admin.GetAsync(delegatePath); Assert.Contains("no-store", response.Headers.CacheControl!.ToString()); var raw = await response.Content.ReadAsStringAsync(); var detail = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(assignment.Brief, detail.GetProperty("brief").GetString()); Assert.True(detail.GetProperty("canEdit").GetBoolean());
        foreach (var secret in new[] { f.Id.ToString(), f.PlayerId.ToString(), f.GuardianUserId.ToString(), group.Id.ToString(), "playerName", "guardian", "message", "ownerResponse", "history", "نص أسرة خاص" }) Assert.DoesNotContain(secret, System.Text.RegularExpressions.Regex.Unescape(raw));
        var findings = new FeedbackFindingsWrite("نتيجة داخلية اصطناعية للمالك فقط؛ اقترح تعديل توزيع الوقت", detail.GetProperty("version").GetUInt32(), Guid.NewGuid());
        Assert.All(await Task.WhenAll(Send(admin, delegatePath + "/transition", findings), Send(admin, delegatePath + "/transition", findings)), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, delegatePath + "/transition", findings with { Note = "نتيجة أخرى" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, delegatePath + "/transition", findings with { RequestId = Guid.NewGuid() })).StatusCode);
        var updated = await owner.GetFromJsonAsync<JsonElement>(path); Assert.Equal("Open", updated.GetProperty("status").GetString()); Assert.Equal(JsonValueKind.Null, updated.GetProperty("ownerResponse").ValueKind); Assert.Equal(2, updated.GetProperty("history").GetArrayLength());
        Assert.Contains(updated.GetProperty("history").EnumerateArray(), c => c.GetProperty("action").GetString() == "Findings" && c.GetProperty("internalNote").GetString() == findings.Note);
        using var family = await Mobile(AcademyRole.Guardian); var mine = await family.GetStringAsync($"/api/v1/guardian/children/{f.PlayerId}/feedback-options");
        foreach (var secret in new[] { assignment.Brief!, findings.Note!, id.ToString(), "investigatorMembershipId", "investigationBrief" }) Assert.DoesNotContain(secret, System.Text.RegularExpressions.Regex.Unescape(mine));
        Assert.Equal(notices, await db.AcademyNotices.CountAsync()); Assert.Equal(report, await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
        await AssignCase(owner, f.Id, null);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(delegatePath)).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await Send(admin, delegatePath + "/transition", findings)).StatusCode);
        var reassigned = await AssignCase(owner, f.Id, member, "تكليف مستقل جديد لا يرث ملاحظات التكليف الملغى"); var newId = reassigned.GetProperty("investigationId").GetGuid(); Assert.NotEqual(id, newId);
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>($"/api/v1/my-followups/investigations/{newId}")).GetProperty("findings").EnumerateArray());
        (await Send(owner, path + "/resolve", new FeedbackResolution("رد اصطناعي صريح من المالك بعد التحقيق"))).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/my-followups/investigations/{newId}")).StatusCode);
        var resolved = await owner.GetFromJsonAsync<JsonElement>(path); Assert.Equal(JsonValueKind.Null, resolved.GetProperty("investigationId").ValueKind);
        (await Send(owner, path + "/transition", new FeedbackCaseWrite("Reopened", resolved.GetProperty("version").GetUInt32(), Guid.NewGuid()))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/my-followups/investigations/{newId}")).StatusCode);
    });
    [Fact] public async Task Feedback_investigation_rechecks_read_edit_scope_subject_user_and_immutable_findings() => await DelegateFixture(async (owner, admin, db, group, member) => {
        var f = (await OverdueFamilyNotes(db, group))[0]; var sourcePath = $"/api/v1/manage/feedback/{f.Id}"; var source = await AssignCase(owner, f.Id, member); var id = source.GetProperty("investigationId").GetGuid(); var path = $"/api/v1/my-followups/investigations/{id}";
        var row = await admin.GetFromJsonAsync<JsonElement>(path); var b = new FeedbackFindingsWrite("نتيجة اختبار صلاحية حية", row.GetProperty("version").GetUInt32(), Guid.NewGuid());
        var grant = await db.StaffActionGrants.SingleAsync(g => g.MembershipId == member && g.Module == "followups"); grant.Edit = false; await db.SaveChangesAsync(); Assert.False((await admin.GetFromJsonAsync<JsonElement>(path)).GetProperty("canEdit").GetBoolean()); Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, path + "/transition", b)).StatusCode);
        grant.Read = false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path)).StatusCode); grant.Read = true; grant.Edit = true; await db.SaveChangesAsync();
        var branch = await db.MembershipBranchAccesses.SingleAsync(a => a.MembershipId == member && a.BranchId == group.BranchId); branch.IsActive = false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(path)).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await Send(admin, path + "/transition", b)).StatusCode); branch.IsActive = true; await db.SaveChangesAsync();
        var sport = await db.MembershipSportAccesses.SingleAsync(a => a.MembershipId == member && a.SportId == group.SportId); sport.IsActive = false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(path)).StatusCode); sport.IsActive = true; await db.SaveChangesAsync();
        var adminMember = await db.AcademyMemberships.SingleAsync(m => m.Id == member);
        var coachContext = await db.AcademyMemberships.SingleOrDefaultAsync(m => m.AcademyId == group.AcademyId && m.UserId == adminMember.UserId && m.Role == AcademyRole.Coach);
        if (coachContext == null) { coachContext = new AcademyMembership { Id = Guid.NewGuid(), AcademyId = group.AcademyId, UserId = adminMember.UserId, Role = AcademyRole.Coach, AllBranches = true, AllSports = true }; db.AcademyMemberships.Add(coachContext); await db.SaveChangesAsync(); }
        try {
        await db.Entry(f).ReloadAsync(); f.CoachMembershipId = coachContext.Id; await db.SaveChangesAsync(); // same person under another trusted role context
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(path)).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await Send(admin, path + "/transition", b)).StatusCode);
        Assert.DoesNotContain((await owner.GetFromJsonAsync<JsonElement>(sourcePath + "/investigators")).GetProperty("items").EnumerateArray(), m => m.GetProperty("id").GetGuid() == member);
        var v = (await owner.GetFromJsonAsync<JsonElement>(sourcePath)).GetProperty("version").GetUInt32(); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, sourcePath + "/assignment", new FeedbackAssignmentWrite(member, "إسناد ممنوع لطرف في الشكوى", v, Guid.NewGuid()))).StatusCode);
        f.CoachMembershipId = null; await db.SaveChangesAsync(); b = b with { Version = (await admin.GetFromJsonAsync<JsonElement>(path)).GetProperty("version").GetUInt32() }; (await Send(admin, path + "/transition", b)).EnsureSuccessStatusCode();
        var update = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.FeedbackChanges.Where(c => c.InvestigationId == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.InternalNote, "تغيير مباشر ممنوع"))); Assert.Equal("23514", update.SqlState);
        var forged = new FeedbackChange { Id = Guid.NewGuid(), AcademyId = group.AcademyId, FeedbackId = f.Id, ActorMembershipId = member, InvestigationId = Guid.NewGuid(), InvestigatorMembershipId = member, Action = "Findings", FromStatus = f.Status, ToStatus = f.Status, BeforeDueOn = f.DueOn, AfterDueOn = f.DueOn, InternalNote = "دليل مزور لإسناد غير مطابق", RequestId = Guid.NewGuid(), RequestHash = new string('a', 64) };
        db.FeedbackChanges.Add(forged); var denied = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal("23514", Assert.IsType<Npgsql.PostgresException>(denied.InnerException).SqlState); db.Entry(forged).State = EntityState.Detached;
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(sourcePath)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, sourcePath + "/resolve", new FeedbackResolution("رد غير مخول"))).StatusCode);
        } finally {
            // Remove only this test's temporary extra role, including references left
            // by an interrupted earlier attempt in the same explicitly isolated QA DB.
            await db.GuardianFeedbacks.Where(x => x.CoachMembershipId == coachContext.Id && x.Message.StartsWith("نص أسرة خاص لا ينتقل إلى مهمة الإداري ")).ExecuteUpdateAsync(s => s.SetProperty(x => x.CoachMembershipId, (Guid?)null));
            await db.MobileSessions.Where(s => s.MembershipId == coachContext.Id && s.DeviceName == "Branding match test").ExecuteDeleteAsync();
            db.AcademyMemberships.Remove(coachContext); await db.SaveChangesAsync();
        }
    });
    [Fact] public async Task Feedback_investigation_denies_foreign_tenant_coach_guardian_invalid_brief_and_inactive_source() => await DelegateFixture(async (owner, admin, db, group, member) => {
        var notes = await OverdueFamilyNotes(db, group); var f = notes[0]; var sourcePath = $"/api/v1/manage/feedback/{f.Id}"; var source = await AssignCase(owner, f.Id, member); var id = source.GetProperty("investigationId").GetGuid(); var path = $"/api/v1/my-followups/investigations/{id}";
        using var foreign = await Staff(DemoSeed.FutureOwnerEmail); using var coach = await Mobile(AcademyRole.Coach); using var guardian = await Mobile(AcademyRole.Guardian);
        foreach (var c in new[] { owner, foreign, coach, guardian }) { Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(path)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Send(c, path + "/transition", new FeedbackFindingsWrite("نتيجة غير مخولة", 1, Guid.NewGuid()))).StatusCode); }
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(sourcePath + "/investigators")).StatusCode);
        var version = source.GetProperty("version").GetUInt32();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, sourcePath + "/assignment", new FeedbackAssignmentWrite(member, "", version, Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, sourcePath + "/assignment", new FeedbackAssignmentWrite(member, new string('x', 501), version, Guid.NewGuid()))).StatusCode);
        var email = $"investigator-{Guid.NewGuid():N}@example.test";
        var create = await Send(foreign, "/api/v1/manage/staff/", new StaffWrite("إداري تحقيق أكاديمية ثانية — اصطناعي", email, "010999" + Random.Shared.Next(10000, 99999), "Demo-Only-123!", AcademyRole.AcademyAdmin, "متابعة خاصة")); create.EnsureSuccessStatusCode();
        var secondMember = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var actions = StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m == "followups", false, m == "followups", false)).ToArray();
        (await Send(foreign, $"/api/v1/manage/staff/{secondMember}/access", new StaffAccessWrite(true, [], actions, AllSports: true, Sports: []), true)).EnsureSuccessStatusCode();
        using var second = await Staff(email);
        (await second.GetAsync("/api/v1/my-followups/investigations")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync(path + $"?academyId={group.AcademyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(second, path + "/transition", new FeedbackFindingsWrite("تجاوز عزل الأكاديمية ممنوع", 1, Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, sourcePath + "/assignment", new FeedbackAssignmentWrite(secondMember, "إسناد لأكاديمية أخرى ممنوع", version, Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(owner, $"/api/v1/manage/feedback/{notes[5].Id}/assignment", new FeedbackAssignmentWrite(member, "إسناد مؤرشف ممنوع", version, Guid.NewGuid()))).StatusCode);
        var license = await db.AcademyFeatureEntitlements.SingleAsync(l => l.AcademyId == group.AcademyId && l.Feature == "training"); try { license.Enabled = false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path)).StatusCode); } finally { license.Enabled = true; await db.SaveChangesAsync(); }
        await db.Entry(f).ReloadAsync(); f.IsActive = false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(path)).StatusCode);
    });
}
