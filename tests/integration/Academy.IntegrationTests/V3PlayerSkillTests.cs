using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Api.Slice5;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    private const string Skills = "/api/v1/evaluations/skills";
    private static async Task<SportEnrollment> FreshSkillChild(FoundationDbContext db, SportEnrollment source, Guid? guardian=null)
    {
        var p=new Player {Id=Guid.NewGuid(),AcademyId=source.AcademyId,PlayerCode="SKL-"+Guid.NewGuid().ToString("N")[..16],ArabicName="لاعب رحلة مهارات اصطناعي",DateOfBirth=new(2016,1,1)};
        var e=new SportEnrollment {Id=Guid.NewGuid(),AcademyId=source.AcademyId,PlayerId=p.Id,SportId=source.SportId,BranchId=source.BranchId,TrainingGroupId=source.TrainingGroupId};
        db.Players.Add(p);db.SportEnrollments.Add(e);if(guardian.HasValue)db.GuardianPlayerLinks.Add(new(){Id=Guid.NewGuid(),AcademyId=source.AcademyId,PlayerId=p.Id,GuardianId=guardian.Value});await db.SaveChangesAsync();return e;
    }
    private async Task<(Guid Curriculum, Guid Level, CurriculumWrite Body)> SkillDefinition(HttpClient owner, Guid sport)
    {
        var level = await Send(owner, "/api/v1/manage/training-levels", new { sportId = sport, arabicName = "مستوى مهارات اصطناعي " + Guid.NewGuid().ToString("N")[..6], rank = 50 }); level.EnsureSuccessStatusCode();
        var levelId = (await level.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var body = new CurriculumWrite(levelId, "خطوات تقدم اصطناعية", [new("تحكم بالكرة", "Checklist", "ملاحظة المدرب في التمرين نفسه دون حكم آلي"), new("عدد التمريرات", "Count", "عدد المحاولات الناجحة من عشر محاولات في التمرين نفسه")], Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest,(await Send(owner,Skills+"/curricula",new{trainingLevelId=levelId,title="متطلبات غير صالحة",requirements=new object?[]{null},requestId=Guid.NewGuid()})).StatusCode);
        var create = await Send(owner, Skills + "/curricula", body); create.EnsureSuccessStatusCode(); var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(id, (await (await Send(owner, Skills + "/curricula", body)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        return (id, levelId, body);
    }
    private static async Task PublishSkills(HttpClient owner, Guid id)
    {
        var d = await owner.GetFromJsonAsync<JsonElement>($"{Skills}/curricula/{id}"); var body = new CurriculumAction(d.GetProperty("item").GetProperty("version").GetUInt32(), Guid.NewGuid());
        (await Send(owner, $"{Skills}/curricula/{id}/publish", body)).EnsureSuccessStatusCode(); (await Send(owner, $"{Skills}/curricula/{id}/publish", body)).EnsureSuccessStatusCode();
    }
    [Fact] public async Task Skill_curriculum_versioned_journey_family_evidence_and_no_automatic_promotion_or_finance()
    {
        using var owner = await Staff(); using var family = await Mobile(AcademyRole.Guardian); using var foreign = await Staff(DemoSeed.FutureOwnerEmail);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var link = await db.GuardianPlayerLinks.Where(l => l.IsActive && l.Guardian.User.PhoneNumber == DemoSeed.GuardianPhone).FirstAsync();
        var source = await db.SportEnrollments.AsNoTracking().FirstAsync(e => e.PlayerId == link.PlayerId && e.IsActive);
        var e = await FreshSkillChild(db,source,link.GuardianId);var player=e.PlayerId;
        var (id, level, draft) = await SkillDefinition(owner, e.SportId); var today = await AttendanceCalendar.TodayAsync(e.AcademyId, db, TimeProvider.System);
        Assert.Equal(HttpStatusCode.Forbidden, (await family.GetAsync(Skills + "/curricula")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"{Skills}/curricula/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await family.GetAsync($"/api/v1/guardian/children/{Guid.NewGuid()}/skills")).StatusCode);
        var planBody = new SkillPlanWrite(id, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"{Skills}/enrollments/{e.Id}/skill-plan", planBody)).StatusCode);
        // Use the existing reviewed-level workflow, not a new promotion mechanism.
        (await Send(owner, $"/api/v1/people/enrollments/{e.Id}/placement-level", new { levelId = level }, true)).EnsureSuccessStatusCode();
        var before = await db.SportEnrollments.AsNoTracking().SingleAsync(x => x.Id == e.Id); var periods = await db.SubscriptionPeriods.CountAsync(); var notices = await db.AcademyNotices.CountAsync(); var financial = await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28");
        await PublishSkills(owner, id);
        var published = await owner.GetFromJsonAsync<JsonElement>($"{Skills}/curricula/{id}");
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"{Skills}/curricula/{id}", draft with { Version = published.GetProperty("item").GetProperty("version").GetUInt32(), RequestId = Guid.NewGuid() }, true)).StatusCode);
        var assign = await Send(owner, $"{Skills}/enrollments/{e.Id}/skill-plan", planBody); assign.EnsureSuccessStatusCode(); var plan = (await assign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(plan, (await (await Send(owner, $"{Skills}/enrollments/{e.Id}/skill-plan", planBody)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        async Task<JsonElement> Read() => await owner.GetFromJsonAsync<JsonElement>($"{Skills}/plans/{plan}");
        var initial = await Read(); Assert.Equal(0, initial.GetProperty("progress").GetProperty("assessed").GetInt32()); Assert.Equal(JsonValueKind.Null, initial.GetProperty("skills")[0].GetProperty("latest").ValueKind);
        var a = initial.GetProperty("skills")[0].GetProperty("requirement").GetProperty("id").GetGuid(); var b = initial.GetProperty("skills")[1].GetProperty("requirement").GetProperty("id").GetGuid();
        var review = new SkillReviewWrite(a, "Mastered", null, today, "مراجعة فنية اصطناعية منشورة", Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, $"{Skills}/plans/{plan}/reviews", review with { ObservedOn = today.AddDays(1) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, $"{Skills}/plans/{plan}/reviews", review with { MeasuredValue = 1 })).StatusCode);
        (await Send(owner, $"{Skills}/plans/{plan}/reviews", review)).EnsureSuccessStatusCode(); (await Send(owner, $"{Skills}/plans/{plan}/reviews", review)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Send(owner, $"{Skills}/plans/{plan}/reviews", review with { Note = "طلب مختلف بنفس المفتاح" })).StatusCode);
        var numeric = new SkillReviewWrite(b, "NotAssessed", 0, today, null, Guid.NewGuid()); Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, $"{Skills}/plans/{plan}/reviews", numeric)).StatusCode);
        (await Send(owner, $"{Skills}/plans/{plan}/reviews", numeric with { MeasuredValue = null })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, $"{Skills}/plans/{plan}/reviews", numeric with { State = "Learning", MeasuredValue = 1.5m, RequestId = Guid.NewGuid() })).StatusCode);
        (await Send(owner, $"{Skills}/plans/{plan}/reviews", numeric with { State = "Mastered", MeasuredValue = 8, RequestId = Guid.NewGuid() })).EnsureSuccessStatusCode();
        var full = await Read(); Assert.Equal(100, full.GetProperty("progress").GetProperty("percentage").GetDecimal()); Assert.True(full.GetProperty("progress").GetProperty("readyForTechnicalReview").GetBoolean()); Assert.Equal(3, full.GetProperty("history").GetArrayLength());
        var mine = await family.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{player}/skills/plans/{plan}"); Assert.False(mine.GetProperty("canReview").GetBoolean()); Assert.Equal(100, mine.GetProperty("progress").GetProperty("percentage").GetDecimal());
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(family, $"{Skills}/plans/{plan}/reviews", review with { RequestId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"{Skills}/plans/{plan}")).StatusCode);
        var sql = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SkillRequirements\" SET \"Name\"='changed' WHERE \"Id\"={a}")); Assert.Equal("23514", sql.SqlState);
        var immutable = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PlayerSkillReviews\" WHERE \"PlanId\"={plan}")); Assert.Equal("23514", immutable.SqlState);
        // A new version never changes the learner's denominator or historic evidence.
        var copy = await Send(owner, Skills + "/curricula", draft with { Requirements = [..draft.Requirements, new("تحرك دون كرة", "Checklist", "مراجعة أصلية مستقلة")], RequestId = Guid.NewGuid(), CopiedFromId = id, SourceVersion = published.GetProperty("item").GetProperty("version").GetUInt32() }); copy.EnsureSuccessStatusCode(); var second = (await copy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); await PublishSkills(owner, second);
        Assert.Equal(2, (await Read()).GetProperty("progress").GetProperty("total").GetInt32());
        var replace = new SkillPlanWrite(second, Guid.NewGuid(), plan, full.GetProperty("plan").GetProperty("version").GetUInt32());
        (await Send(owner, $"{Skills}/enrollments/{e.Id}/skill-plan", replace)).EnsureSuccessStatusCode();
        var old = await Read(); Assert.True(old.GetProperty("contextChanged").GetBoolean()); Assert.Equal(JsonValueKind.Null, old.GetProperty("progress").GetProperty("percentage").ValueKind); Assert.Equal(100, old.GetProperty("progress").GetProperty("sourcePercentage").GetDecimal()); Assert.Equal(3, old.GetProperty("history").GetArrayLength());
        var after = await db.SportEnrollments.AsNoTracking().SingleAsync(x => x.Id == e.Id); Assert.Equal(before.TrainingLevelId, after.TrainingLevelId); Assert.Equal(before.TrainingGroupId, after.TrainingGroupId); Assert.Equal(before.Status, after.Status); Assert.Equal(periods, await db.SubscriptionPeriods.CountAsync()); Assert.Equal(notices, await db.AcademyNotices.CountAsync()); Assert.Equal(financial, await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
    }
    [Fact] public async Task Skill_curriculum_scoped_admin_requires_separate_add_edit_and_cannot_edit_global_definitions() => await DelegateFixture(async(owner, admin, db, group, member) => {
        var (id,level,body)=await SkillDefinition(owner,group.SportId); await PublishSkills(owner,id);
        var p=new Player {Id=Guid.NewGuid(),AcademyId=group.AcademyId,PlayerCode="SKILL-"+Guid.NewGuid().ToString("N")[..15],ArabicName="لاعب مهارات اصطناعي",DateOfBirth=new(2016,1,1)};
        var e=new SportEnrollment{Id=Guid.NewGuid(),AcademyId=group.AcademyId,PlayerId=p.Id,SportId=group.SportId,BranchId=group.BranchId,TrainingGroupId=group.Id};db.Players.Add(p);db.SportEnrollments.Add(e);await db.SaveChangesAsync();
        (await Send(owner,$"/api/v1/people/enrollments/{e.Id}/placement-level",new{levelId=level},true)).EnsureSuccessStatusCode();
        var grant=await db.StaffActionGrants.SingleOrDefaultAsync(g=>g.MembershipId==member&&g.Module=="evaluations");var original=grant==null?null:JsonSerializer.Serialize(grant);grant??=new(){Id=Guid.NewGuid(),AcademyId=group.AcademyId,MembershipId=member,Module="evaluations"};if(original==null)db.StaffActionGrants.Add(grant);
        try {
            grant.IsActive=true;grant.Read=true;grant.Add=false;grant.Edit=false;await db.SaveChangesAsync();
            var journey=await admin.GetFromJsonAsync<JsonElement>($"{Skills}/enrollments/{e.Id}/journey");Assert.False(journey.GetProperty("canAssign").GetBoolean());
            var assign=new SkillPlanWrite(id,Guid.NewGuid());Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,$"{Skills}/enrollments/{e.Id}/skill-plan",assign)).StatusCode);
            grant.Add=true;await db.SaveChangesAsync();var created=await Send(admin,$"{Skills}/enrollments/{e.Id}/skill-plan",assign);created.EnsureSuccessStatusCode();var plan=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var data=await admin.GetFromJsonAsync<JsonElement>($"{Skills}/plans/{plan}");Assert.False(data.GetProperty("canReview").GetBoolean());var review=new SkillReviewWrite(data.GetProperty("skills")[0].GetProperty("requirement").GetProperty("id").GetGuid(),"Learning",null,DateOnly.Parse(data.GetProperty("asOfDate").GetString()!),null,Guid.NewGuid());
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,$"{Skills}/plans/{plan}/reviews",review)).StatusCode);grant.Edit=true;await db.SaveChangesAsync();(await Send(admin,$"{Skills}/plans/{plan}/reviews",review)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,Skills+"/curricula",body with{RequestId=Guid.NewGuid()})).StatusCode);
            var s=await db.MembershipSportAccesses.SingleAsync(s=>s.MembershipId==member&&s.SportId==group.SportId);s.IsActive=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"{Skills}/plans/{plan}")).StatusCode);s.IsActive=true;await db.SaveChangesAsync();
            grant.Read=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync($"{Skills}/plans/{plan}")).StatusCode);
        } finally {if(original==null)db.StaffActionGrants.Remove(grant);else db.Entry(grant).CurrentValues.SetValues(JsonSerializer.Deserialize<StaffActionGrant>(original)!);await db.SaveChangesAsync();}
    });
    [Fact] public async Task Skill_curriculum_coach_can_only_review_current_group_and_not_manage_shared_definitions()
    {
        using var owner = await Staff(); using var coach = await Mobile(AcademyRole.Coach);
        var options = await coach.GetFromJsonAsync<JsonElement>("/api/v1/evaluations/options");
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var sourceId=options.GetProperty("enrollments")[0].GetProperty("id").GetGuid();var child=await FreshSkillChild(db,await db.SportEnrollments.AsNoTracking().SingleAsync(e=>e.Id==sourceId));var e=child.Id;
        var sport = child.SportId; var (id, level, body) = await SkillDefinition(owner, sport);
        Assert.Equal(HttpStatusCode.NotFound, (await coach.GetAsync($"{Skills}/curricula/{id}")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Send(coach, Skills + "/curricula", body with { RequestId = Guid.NewGuid() })).StatusCode);
        (await Send(owner, $"/api/v1/people/enrollments/{e}/placement-level", new { levelId = level }, true)).EnsureSuccessStatusCode(); await PublishSkills(owner, id);
        (await Send(coach, $"{Skills}/enrollments/{e}/skill-plan", new SkillPlanWrite(id, Guid.NewGuid()))).EnsureSuccessStatusCode();
        var journey = await coach.GetFromJsonAsync<JsonElement>($"{Skills}/enrollments/{e}/journey"); var plan = journey.GetProperty("active").GetProperty("plan").GetProperty("id").GetGuid(); var req = journey.GetProperty("active").GetProperty("skills")[0].GetProperty("requirement").GetProperty("id").GetGuid(); var today = DateOnly.Parse(journey.GetProperty("asOfDate").GetString()!);
        (await Send(coach, $"{Skills}/plans/{plan}/reviews", new SkillReviewWrite(req, "Learning", null, today, "مراجعة مدرب المجموعة", Guid.NewGuid()))).EnsureSuccessStatusCode();
        var allowed = options.GetProperty("enrollments").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).Append(e).ToArray(); var other = await db.SportEnrollments.FirstAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.IsActive && !allowed.Contains(x.Id) && x.TrainingGroupId!=child.TrainingGroupId);
        Assert.Equal(HttpStatusCode.NotFound, (await coach.GetAsync($"{Skills}/enrollments/{other.Id}/journey")).StatusCode);
        var version = (await owner.GetFromJsonAsync<JsonElement>($"{Skills}/curricula/{id}")).GetProperty("item").GetProperty("version").GetUInt32(); (await Send(owner, $"{Skills}/curricula/{id}/retire", new CurriculumAction(version, Guid.NewGuid()))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Send(coach, $"{Skills}/plans/{plan}/reviews", new SkillReviewWrite(req, "Mastered", null, today, null, Guid.NewGuid()))).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await coach.GetFromJsonAsync<JsonElement>($"{Skills}/plans/{plan}")).GetProperty("progress").GetProperty("percentage").ValueKind);
    }
}
