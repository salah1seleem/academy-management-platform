using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Mobile;
using Academy.Api.Slice4;
using Academy.Api.Slice5;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    private const string Goals = "/api/v1/evaluations/development-goals";
    [Fact] public async Task Player_goal_published_history_replay_review_and_achievement_do_not_change_level_subscription_or_money()
    {
        using var owner = await Staff(); using var guardian = await Mobile(AcademyRole.Guardian);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var player = await db.GuardianPlayerLinks.Where(l => l.IsActive && l.Guardian.User.PhoneNumber == DemoSeed.GuardianPhone).Select(l => l.PlayerId).FirstAsync();
        var e = await db.SportEnrollments.AsNoTracking().FirstAsync(e => e.PlayerId == player && e.IsActive); var today = await AttendanceCalendar.TodayAsync(e.AcademyId, db, TimeProvider.System);
        var financial = await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"); var periods = await db.SubscriptionPeriods.CountAsync(); var notices = await db.AcademyNotices.CountAsync();
        var body = new GoalDraft("تمرير دقيق — سيناريو اصطناعي", "مراجعة دقة التمرير في التقييم القادم؛ لا ترقية تلقائية.", today.AddDays(14), Guid.NewGuid());
        var create = await Send(owner, $"{Goals}/enrollments/{e.Id}", body); create.EnsureSuccessStatusCode(); var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var replay = await Send(owner, $"{Goals}/enrollments/{e.Id}", body); replay.EnsureSuccessStatusCode(); Assert.Equal(id, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"{Goals}/enrollments/{e.Id}", body with { Title = "طلب مختلف بنفس المفتاح" })).StatusCode);
        async Task<JsonElement> Read() => await owner.GetFromJsonAsync<JsonElement>($"{Goals}/{id}");
        async Task<JsonElement[]> Mine() => (await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{player}/development-goals")).GetProperty("items").EnumerateArray().Where(x => x.GetProperty("goal").GetProperty("id").GetGuid() == id).ToArray();
        Assert.Empty(await Mine()); var draft = (await Read()).GetProperty("item");
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"{Goals}/{id}/transition", new GoalTransition("Published", null, Guid.NewGuid(), 0))).StatusCode);
        (await Send(owner, $"{Goals}/{id}", body with { RequestId = Guid.NewGuid(), Version = draft.GetProperty("version").GetUInt32(), Target = "خطوة فنية معدلة قبل النشر" }, true)).EnsureSuccessStatusCode();
        var edited = (await Read()).GetProperty("item");
        var publish = new GoalTransition("Published", null, Guid.NewGuid(), edited.GetProperty("version").GetUInt32());
        (await Send(owner, $"{Goals}/{id}/transition", publish)).EnsureSuccessStatusCode(); (await Send(owner, $"{Goals}/{id}/transition", publish)).EnsureSuccessStatusCode();
        var mine = Assert.Single(await Mine()); Assert.Equal("خطوة فنية معدلة قبل النشر", mine.GetProperty("goal").GetProperty("target").GetString()); Assert.Single(mine.GetProperty("reviews").EnumerateArray()); Assert.Equal("Published", mine.GetProperty("reviews")[0].GetProperty("action").GetString());
        var published = (await Read()).GetProperty("item");
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"{Goals}/{id}", body with { Version = published.GetProperty("version").GetUInt32(), RequestId = Guid.NewGuid() }, true)).StatusCode);
        var review = new GoalTransition("Reviewed", "مراجعة اصطناعية: تحسن ملحوظ يحتاج متابعة", Guid.NewGuid(), published.GetProperty("version").GetUInt32());
        (await Send(owner, $"{Goals}/{id}/transition", review)).EnsureSuccessStatusCode(); (await Send(owner, $"{Goals}/{id}/transition", review)).EnsureSuccessStatusCode();
        var read = await Read(); Assert.Equal(4, read.GetProperty("history").GetArrayLength()); Assert.Equal(2, Assert.Single(await Mine()).GetProperty("reviews").GetArrayLength());
        var before = await db.PlayerGoalChanges.CountAsync(c => c.GoalId == id);
        var sql = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PlayerDevelopmentGoals\" SET \"Revision\"=\"Revision\"+1 WHERE \"Id\"={id}")); Assert.Equal("23514", sql.SqlState);
        var historySql = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PlayerGoalChanges\" WHERE \"GoalId\"={id}")); Assert.Equal("23514", historySql.SqlState); Assert.Equal(before, await db.PlayerGoalChanges.CountAsync(c => c.GoalId == id));
        var version = read.GetProperty("item").GetProperty("version").GetUInt32();
        (await Send(owner, $"{Goals}/{id}/transition", new GoalTransition("Achieved", null, Guid.NewGuid(), version))).EnsureSuccessStatusCode();
        var achieved = (await Read()).GetProperty("item"); Assert.Equal("Achieved", Assert.Single(await Mine()).GetProperty("goal").GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"{Goals}/{id}/transition", new GoalTransition("Reviewed", "تعديل بعد الإنهاء", Guid.NewGuid(), achieved.GetProperty("version").GetUInt32()))).StatusCode);
        var after = await db.SportEnrollments.AsNoTracking().SingleAsync(x => x.Id == e.Id); Assert.Equal(e.TrainingLevelId, after.TrainingLevelId); Assert.Equal(e.TrainingGroupId, after.TrainingGroupId); Assert.Equal(e.Status, after.Status);
        Assert.Equal(periods, await db.SubscriptionPeriods.CountAsync()); Assert.Equal(notices, await db.AcademyNotices.CountAsync()); Assert.Equal(financial, await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
        var goal = await db.PlayerDevelopmentGoals.SingleAsync(g => g.Id == id); goal.Target = "تغيير المصدر المنشور"; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
    }
    [Fact] public async Task Player_goal_coach_is_current_group_only_guardian_cannot_write_foreign_tenant_is_hidden_and_withdrawal_hides_source()
    {
        using var owner = await Staff(); using var coach = await Mobile(AcademyRole.Coach); using var guardian = await Mobile(AcademyRole.Guardian); using var foreign = await Staff(DemoSeed.FutureOwnerEmail);
        var options = await coach.GetFromJsonAsync<JsonElement>("/api/v1/evaluations/options"); var e = options.GetProperty("enrollments")[0].GetProperty("id").GetGuid();
        var list = await coach.GetFromJsonAsync<JsonElement>($"{Goals}/enrollments/{e}"); var today = DateOnly.Parse(list.GetProperty("asOfDate").GetString()!);
        var body = new GoalDraft("هدف المدرب الاصطناعي", "خطوة فنية تخص مجموعته فقط", today, Guid.NewGuid()); var create = await Send(coach, $"{Goals}/enrollments/{e}", body); create.EnsureSuccessStatusCode(); var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var detail = await coach.GetFromJsonAsync<JsonElement>($"{Goals}/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(guardian, $"{Goals}/{id}/transition", new GoalTransition("Published", null, Guid.NewGuid(), detail.GetProperty("item").GetProperty("version").GetUInt32()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"{Goals}/{id}?academyId={DemoSeed.NogoomAcademyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(foreign, $"{Goals}/enrollments/{e}", body with { RequestId = Guid.NewGuid() })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var allowed = options.GetProperty("enrollments").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray(); var other = await db.SportEnrollments.FirstAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.IsActive && !allowed.Contains(x.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await coach.GetAsync($"{Goals}/enrollments/{other.Id}")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await Send(coach, $"{Goals}/enrollments/{other.Id}", body with { RequestId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync($"/api/v1/guardian/children/{Guid.NewGuid()}/development-goals")).StatusCode);
        var bad = body with { ReviewOn = today.AddDays(-1), RequestId = Guid.NewGuid() }; Assert.Equal(HttpStatusCode.BadRequest, (await Send(coach, $"{Goals}/enrollments/{e}", bad)).StatusCode);
        (await Send(coach, $"{Goals}/{id}/transition", new GoalTransition("Withdrawn", null, Guid.NewGuid(), detail.GetProperty("item").GetProperty("version").GetUInt32()))).EnsureSuccessStatusCode();
        var changed = await coach.GetFromJsonAsync<JsonElement>($"{Goals}/{id}"); Assert.Equal("Withdrawn", changed.GetProperty("item").GetProperty("state").GetString()); Assert.Equal(2, changed.GetProperty("history").GetArrayLength());
        var hash = MobileAuthenticationHandler.Hash(coach.DefaultRequestHeaders.Authorization!.Parameter!); var member = await db.MobileSessions.Where(s => s.AccessTokenHash == hash).Select(s => s.MembershipId).SingleAsync();
        var group = options.GetProperty("enrollments")[0].GetProperty("groupId").GetGuid(); var assignment = await db.StaffGroupAssignments.SingleAsync(a => a.AcademyMembershipId == member && a.TrainingGroupId == group && a.IsActive);
        try { assignment.IsActive = false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.NotFound, (await coach.GetAsync($"{Goals}/{id}")).StatusCode); }
        finally { assignment.IsActive = true; await db.SaveChangesAsync(); }
    }
    [Fact] public async Task Player_goal_narrow_admin_requires_separate_read_add_edit_and_live_sport_scope() => await DelegateFixture(async(owner, admin, db, group, member) => {
        var p = new Player { Id = Guid.NewGuid(), AcademyId = group.AcademyId, PlayerCode = "GOAL-" + Guid.NewGuid().ToString("N")[..16], ArabicName = "لاعب أهداف اصطناعي", DateOfBirth = new(2016, 1, 1) };
        var e = new SportEnrollment { Id = Guid.NewGuid(), AcademyId = group.AcademyId, PlayerId = p.Id, SportId = group.SportId, BranchId = group.BranchId, TrainingGroupId = group.Id };
        db.Players.Add(p); db.SportEnrollments.Add(e); await db.SaveChangesAsync();
        var grant = await db.StaffActionGrants.SingleOrDefaultAsync(g => g.MembershipId == member && g.Module == "evaluations"); var original = grant == null ? null : JsonSerializer.Serialize(grant);
        grant ??= new StaffActionGrant { Id = Guid.NewGuid(), AcademyId = group.AcademyId, MembershipId = member, Module = "evaluations" }; if (original == null) db.StaffActionGrants.Add(grant);
        try {
            grant.IsActive = true; grant.Read = true; grant.Add = false; grant.Edit = false; await db.SaveChangesAsync();
            var list = await admin.GetFromJsonAsync<JsonElement>($"{Goals}/enrollments/{e.Id}"); Assert.False(list.GetProperty("canCreate").GetBoolean()); var body = new GoalDraft("هدف إداري مخول", "مراجعة خطوة فنية في الرياضة المخولة", DateOnly.Parse(list.GetProperty("asOfDate").GetString()!), Guid.NewGuid());
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, $"{Goals}/enrollments/{e.Id}", body)).StatusCode);
            grant.Add = true; await db.SaveChangesAsync(); var created = await Send(admin, $"{Goals}/enrollments/{e.Id}", body); created.EnsureSuccessStatusCode(); var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var detail = await admin.GetFromJsonAsync<JsonElement>($"{Goals}/{id}"); Assert.False(detail.GetProperty("canEdit").GetBoolean()); var publish = new GoalTransition("Published", null, Guid.NewGuid(), detail.GetProperty("item").GetProperty("version").GetUInt32());
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, $"{Goals}/{id}/transition", publish)).StatusCode); grant.Edit = true; await db.SaveChangesAsync(); (await Send(admin, $"{Goals}/{id}/transition", publish)).EnsureSuccessStatusCode();
            var sports = await db.MembershipSportAccesses.SingleAsync(s => s.MembershipId == member && s.SportId == group.SportId); sports.IsActive = false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Goals}/{id}")).StatusCode); sports.IsActive = true; await db.SaveChangesAsync();
            grant.Read = false; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"{Goals}/{id}")).StatusCode);
        } finally { if (original == null) db.StaffActionGrants.Remove(grant); else db.Entry(grant).CurrentValues.SetValues(JsonSerializer.Deserialize<StaffActionGrant>(original)!); await db.SaveChangesAsync(); }
    });
}
