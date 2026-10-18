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
public sealed partial class BrandingMatchesTests
{
    [Fact]
    public async Task Feedback_case_reopen_snapshots_legacy_reply_without_inventing_an_old_investigation_or_notifying()
    {
        using var owner=await Staff();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var f=(await OverdueFamilyNotes(db,await FollowUpGroup(db)))[4];
        // A genuine pre-journal shape in isolated QA: resolved source, no case history.
        f.OwnerResponse="رد تاريخي اصطناعي يسبق إضافة سجل التحقيق";await db.SaveChangesAsync();
        var path=$"/api/v1/manage/feedback/{f.Id}";var before=await owner.GetFromJsonAsync<JsonElement>(path);Assert.Empty(before.GetProperty("history").EnumerateArray());
        var notices=await db.AcademyNotices.CountAsync();
        (await Send(owner,path+"/transition",new FeedbackCaseWrite("Reopened",before.GetProperty("version").GetUInt32(),Guid.NewGuid()))).EnsureSuccessStatusCode();
        var reopened=await owner.GetFromJsonAsync<JsonElement>(path);var snapshot=Assert.Single(reopened.GetProperty("history").EnumerateArray());
        Assert.Equal("Transition",snapshot.GetProperty("action").GetString());Assert.Equal("Reopened",snapshot.GetProperty("toStatus").GetString());Assert.Equal(f.OwnerResponse,snapshot.GetProperty("publicResponse").GetString());Assert.Equal(notices,await db.AcademyNotices.CountAsync());
        (await Send(owner,path+"/resolve",new FeedbackResolution("رد جديد بعد مراجعة الملاحظة القديمة"))).EnsureSuccessStatusCode();
        var history=(await owner.GetFromJsonAsync<JsonElement>(path)).GetProperty("history");Assert.Equal(2,history.GetArrayLength());Assert.Contains(history.EnumerateArray(),c=>c.GetProperty("publicResponse").GetString()==f.OwnerResponse);
    }
    [Fact]
    public async Task Feedback_case_journal_returns_latest_fifty_without_erasing_older_evidence()
    {
        using var owner=await Staff();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var f=(await OverdueFamilyNotes(db,await FollowUpGroup(db)))[0];var path=$"/api/v1/manage/feedback/{f.Id}";
        Guid first=Guid.Empty;
        for(var i=0;i<51;i++) {
            var row=await owner.GetFromJsonAsync<JsonElement>(path);
            var body=new FeedbackCaseWrite(i%2==0?"InProgress":"AwaitingInfo",row.GetProperty("version").GetUInt32(),Guid.NewGuid());
            (await Send(owner,path+"/transition",body)).EnsureSuccessStatusCode();
            if(i==0)first=await db.FeedbackChanges.Where(c=>c.FeedbackId==f.Id).Select(c=>c.Id).SingleAsync();
        }
        var result=await owner.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(50,result.GetProperty("historyLimit").GetInt32());Assert.True(result.GetProperty("historyHasMore").GetBoolean());Assert.Equal(50,result.GetProperty("history").GetArrayLength());
        Assert.DoesNotContain(result.GetProperty("history").EnumerateArray(),c=>c.GetProperty("id").GetGuid()==first);Assert.Equal(51,await db.FeedbackChanges.CountAsync(c=>c.FeedbackId==f.Id));
    }
    [Fact]
    public async Task Feedback_case_lifecycle_preserves_private_evidence_reopens_live_sources_and_does_not_touch_money()
    {
        using var owner = await Staff(); using var family = await Mobile(AcademyRole.Guardian);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var group = await FollowUpGroup(db); var f = (await OverdueFamilyNotes(db, group))[0]; var path = $"/api/v1/manage/feedback/{f.Id}";
        async Task<JsonElement> Read() => await owner.GetFromJsonAsync<JsonElement>(path);
        async Task<int> OpenCount() => (await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/owner-summary?from=2026-10-01&to=2026-10-31")).GetProperty("operations").GetProperty("openComplaints").GetInt32();
        var openBefore = await OpenCount();
        Assert.Equal(await db.GuardianFeedbacks.CountAsync(x=>x.AcademyId==group.AcademyId&&x.IsActive&&x.Kind=="Complaint"&&x.Status!="Resolved"&&x.Status!="Closed"),openBefore);
        async Task<FeedbackCaseWrite> Body(string status, string? note = null) => new(status, (await Read()).GetProperty("version").GetUInt32(), Guid.NewGuid(), note);
        async Task<bool> Candidate() => (await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/owner-followups/options")).GetProperty("candidates").EnumerateArray().Any(c => c.GetProperty("condition").GetString() == "GroupOverdueFeedback" && c.GetProperty("source").GetProperty("groupId").GetGuid() == group.Id && c.GetProperty("source").GetProperty("overdueFeedback").GetProperty("complaintCount").GetInt32() == 1);
        var report = await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"); var notices = await db.AcademyNotices.CountAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path + "/transition", await Body("Closed"))).StatusCode);
        var start = await Body("InProgress", "مراجعة داخلية اصطناعية لا تظهر للأسرة أو المدرب");
        var concurrent = await Task.WhenAll(Send(owner, path + "/transition", start), Send(owner, path + "/transition", start));
        Assert.All(concurrent, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Single((await Read()).GetProperty("history").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path + "/transition", start with { InternalNote = "نص مختلف" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path + "/transition", start with { Status = "AwaitingInfo", RequestId = Guid.NewGuid() })).StatusCode);
        Assert.True(await Candidate());
        Assert.Contains((await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/feedback?status=Unresolved&overdue=true")).EnumerateArray(), x => x.GetProperty("id").GetGuid() == f.Id);
        (await Send(owner, path + "/transition", await Body("AwaitingInfo"))).EnsureSuccessStatusCode(); // optional note
        Assert.Equal(openBefore,await OpenCount());
        Assert.Equal(notices, await db.AcademyNotices.CountAsync());
        var mine = await family.GetStringAsync($"/api/v1/guardian/children/{f.PlayerId}/feedback-options");
        Assert.DoesNotContain("مراجعة داخلية", System.Text.RegularExpressions.Regex.Unescape(mine));
        Assert.DoesNotContain("internalNote", mine); Assert.DoesNotContain("history", mine);
        var response = new FeedbackResolution("الرد الأول للأسرة الاصطناعية بعد مراجعة الموعد");
        (await Send(owner, path + "/resolve", response)).EnsureSuccessStatusCode();
        (await Send(owner, path + "/resolve", response)).EnsureSuccessStatusCode();
        Assert.Equal(notices + 1, await db.AcademyNotices.CountAsync());
        Assert.False(await Candidate());
        Assert.Equal(openBefore-1,await OpenCount());
        (await Send(owner, path + "/transition", await Body("Closed"))).EnsureSuccessStatusCode();
        Assert.False((await Read()).GetProperty("overdue").GetBoolean());
        Assert.DoesNotContain((await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/feedback?status=Unresolved")).EnumerateArray(), x => x.GetProperty("id").GetGuid() == f.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path + "/resolve", response)).StatusCode);
        (await Send(owner, path + "/transition", await Body("Reopened", "استكمال تحقيق داخلي"))).EnsureSuccessStatusCode();
        Assert.True(await Candidate()); Assert.True((await Read()).GetProperty("overdue").GetBoolean());
        Assert.Equal(openBefore,await OpenCount());
        Assert.Equal(response.Response, (await Read()).GetProperty("ownerResponse").GetString());
        Assert.Equal(notices + 1, await db.AcademyNotices.CountAsync());
        var response2 = new FeedbackResolution("الرد الثاني للأسرة بعد استكمال التحقيق الاصطناعي");
        (await Send(owner, path + "/resolve", response2)).EnsureSuccessStatusCode();
        Assert.Equal(notices + 2, await db.AcademyNotices.CountAsync());
        var history = (await Read()).GetProperty("history"); Assert.Equal(6, history.GetArrayLength());
        Assert.Equal(new[] { response.Response, response2.Response }.Order(), history.EnumerateArray().Where(c => c.GetProperty("action").GetString() == "Resolve").Select(c => c.GetProperty("publicResponse").GetString()).Order());
        var visible = (await family.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{f.PlayerId}/feedback-options")).GetProperty("mine").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == f.Id);
        Assert.Equal(response2.Response, visible.GetProperty("ownerResponse").GetString()); Assert.False(visible.TryGetProperty("internalNote", out _));
        Assert.Equal(report, await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
    }

    [Fact]
    public async Task Feedback_case_denies_other_roles_tenants_invalid_transitions_and_history_rewrites()
    {
        using var owner = await Staff(); using var admin = await Staff(DemoSeed.AdminEmail); using var coach = await Mobile(AcademyRole.Coach); using var guardian = await Mobile(AcademyRole.Guardian); using var foreign = await Staff(DemoSeed.FutureOwnerEmail);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var group = await FollowUpGroup(db); var notes = await OverdueFamilyNotes(db, group); var f = notes[0]; var path = $"/api/v1/manage/feedback/{f.Id}";
        var d = await owner.GetFromJsonAsync<JsonElement>(path); var b = new FeedbackCaseWrite("InProgress", d.GetProperty("version").GetUInt32(), Guid.NewGuid());
        foreach (var c in new[] { admin, coach, guardian }) { Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(path)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Send(c, path + "/transition", b)).StatusCode); }
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(path)).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await Send(foreign, path + "/transition", b)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync($"/api/v1/manage/feedback/{notes[5].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Send(owner,$"/api/v1/manage/feedback/{notes[5].Id}/transition",b)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Send(owner,$"/api/v1/manage/feedback/{notes[5].Id}/resolve",new FeedbackResolution("لا تنشر ردًا لملاحظة مؤرشفة"))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, path + "/transition", b with { RequestId = Guid.Empty })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, path + "/transition", b with { InternalNote = new string('x', 501) })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, path + "/transition", b with { Status = "Resolved" })).StatusCode);
        var rating = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/feedback/{notes[6].Id}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(owner, $"/api/v1/manage/feedback/{notes[6].Id}/transition", b with { Version = rating.GetProperty("version").GetUInt32() })).StatusCode);
        (await Send(owner, path + "/transition", b)).EnsureSuccessStatusCode();
        var change = await db.FeedbackChanges.SingleAsync(c => c.FeedbackId == f.Id);
        change.InternalNote = "محو الدليل ممنوع";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        var update = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.FeedbackChanges.Where(c => c.FeedbackId == f.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.InternalNote, "تغيير مباشر ممنوع"))); Assert.Equal("23514", update.SqlState);
        var deletion = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.FeedbackChanges.Where(c => c.FeedbackId == f.Id).ExecuteDeleteAsync()); Assert.Equal("23514", deletion.SqlState);
        var adminId = await db.AcademyMemberships.Where(m => m.AcademyId == group.AcademyId && m.User.Email == DemoSeed.AdminEmail).Select(m => m.Id).SingleAsync();
        var forged = new Academy.Infrastructure.Operations.FeedbackChange { Id = Guid.NewGuid(), AcademyId = group.AcademyId, FeedbackId = f.Id, ActorMembershipId = adminId, Action = "Transition", FromStatus = "Open", ToStatus = "InProgress", AfterDueOn = f.DueOn, RequestId = Guid.NewGuid(), RequestHash = new string('a', 64), CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
        db.FeedbackChanges.Add(forged); var denied = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal("23514", Assert.IsType<Npgsql.PostgresException>(denied.InnerException).SqlState); db.ChangeTracker.Clear();
    }
}
