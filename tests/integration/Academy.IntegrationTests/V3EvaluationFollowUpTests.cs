using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    private static async Task<PlayerEvaluation[]> ReviewDrafts(FoundationDbContext db, TrainingGroup g)
    {
        var now=DateTimeOffset.UtcNow;var today=await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(g.AcademyId,db,TimeProvider.System);
        var user=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync();
        var player=new Player{Id=Guid.NewGuid(),AcademyId=g.AcademyId,PlayerCode="REVIEW-"+Guid.NewGuid().ToString("N")[..12],ArabicName="لاعب مراجعة اصطناعي",DateOfBirth=new(2016,1,1),CreatedAtUtc=now,UpdatedAtUtc=now};
        var enrollment=new SportEnrollment{Id=Guid.NewGuid(),AcademyId=g.AcademyId,PlayerId=player.Id,BranchId=g.BranchId,SportId=g.SportId,TrainingGroupId=g.Id,CreatedAtUtc=now,UpdatedAtUtc=now};db.Players.Add(player);db.SportEnrollments.Add(enrollment);
        var criterion=await db.EvaluationCriteria.FirstAsync(c=>c.AcademyId==g.AcademyId&&c.SportId==g.SportId);
        var evaluations=Enumerable.Range(0,7).Select(i=>new PlayerEvaluation{Id=Guid.NewGuid(),AcademyId=g.AcademyId,SportEnrollmentId=enrollment.Id,SportId=g.SportId,TrainingGroupId=g.Id,EvaluatedByUserId=user,EvaluationDate=i==6?today.AddDays(1):today.AddDays(-8-i),ReportingPeriod="مراجعة اصطناعية "+Guid.NewGuid().ToString("N"),GeneralNotes="ملاحظة خاصة لا تظهر في صندوق المتابعة",Status=i==2?EvaluationStatus.Published:i==3?EvaluationStatus.Superseded:EvaluationStatus.Draft,PublishedAtUtc=i==2?now.AddDays(-7):null,PublishedByUserId=i==2?user:null,IsActive=i!=4,CreatedAtUtc=now.AddDays(i==5?-2:-8),UpdatedAtUtc=now}).ToArray();
        foreach(var e in evaluations.Take(2))e.Scores.Add(new(){Id=Guid.NewGuid(),AcademyId=g.AcademyId,PlayerEvaluationId=e.Id,EvaluationCriterionId=criterion.Id,SportId=g.SportId,CriterionNameSnapshot=criterion.ArabicName,WeightSnapshot=1,Score=75,CreatedAtUtc=now,UpdatedAtUtc=now});
        db.PlayerEvaluations.AddRange(evaluations);await db.SaveChangesAsync();return evaluations;
    }
    [Fact] public async Task Evaluation_followup_counts_real_old_drafts_and_unique_players_and_closure_never_publishes()
    {
        using var owner=await Staff();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var g=await FollowUpGroup(db);var drafts=await ReviewDrafts(db,g);
        async Task<JsonElement[]> Candidates()=> (await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/owner-followups/options")).GetProperty("candidates").EnumerateArray().Where(c=>c.GetProperty("condition").GetString()=="GroupEvaluationDrafts"&&c.GetProperty("source").GetProperty("groupId").GetGuid()==g.Id).ToArray();
        var source=Assert.Single(await Candidates()).GetProperty("source").GetProperty("evaluationDrafts");Assert.Equal(2,source.GetProperty("draftCount").GetInt32());Assert.Equal(1,source.GetProperty("playerCount").GetInt32());Assert.False(source.TryGetProperty("evaluationIds",out _));
        var r=await Send(owner,"/api/v1/manage/owner-followups/",new OwnerFollowUpCreate(g.Id,"GroupEvaluationDrafts",Guid.NewGuid()));r.EnsureSuccessStatusCode();var id=(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        async Task<JsonElement> Read()=> (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/owner-followups/{id}")).GetProperty("item");
        var item=await Read();Assert.Equal(drafts.Take(2).Select(d=>d.Id).Order(),item.GetProperty("evidence").GetProperty("evaluationIds").EnumerateArray().Select(x=>x.GetGuid()).Order());var evidence=item.GetProperty("evidence").GetRawText();var report=await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28");
        g.IsActive=false;await db.SaveChangesAsync();Assert.False((await Read()).GetProperty("conditionActive").GetBoolean());Assert.Empty(await Candidates());
        g.IsActive=true;await db.SaveChangesAsync();Assert.True((await Read()).GetProperty("conditionActive").GetBoolean());Assert.Equal(evidence,(await Read()).GetProperty("evidence").GetRawText());
        (await Send(owner,$"/api/v1/manage/owner-followups/{id}",new OwnerFollowUpWrite("Closed",item.GetProperty("version").GetUInt32(),Guid.NewGuid(),ResolutionReason:"متابعة بشرية فقط؛ لم ينشر التقييم"))).EnsureSuccessStatusCode();Assert.True((await Read()).GetProperty("conditionActive").GetBoolean());
        foreach(var d in drafts.Take(2)){
            var evaluation=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/evaluations/{d.Id}");Assert.Equal("Draft",evaluation.GetProperty("status").GetString());
            (await Send(owner,$"/api/v1/evaluations/{d.Id}/publish",new Academy.Api.Slice5.PublishEvaluationRequest(evaluation.GetProperty("version").GetUInt32()))).EnsureSuccessStatusCode();
        }
        Assert.Empty(await Candidates());item=await Read();Assert.False(item.GetProperty("conditionActive").GetBoolean());Assert.Equal(evidence,item.GetProperty("evidence").GetRawText());Assert.Equal("Closed",item.GetProperty("state").GetString());Assert.Equal(report,await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
        Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,"/api/v1/manage/owner-followups/",new OwnerFollowUpCreate(g.Id,"GroupEvaluationDrafts",Guid.NewGuid()))).StatusCode);
    }
    [Fact] public async Task Evaluation_followup_delegate_gets_counts_not_child_drafts_and_live_training_revocation_denies_reads() => await DelegateFixture(async(owner,admin,db,g,member)=>{
        var drafts=await ReviewDrafts(db,g);var r=await Send(owner,"/api/v1/manage/owner-followups/",new OwnerFollowUpCreate(g.Id,"GroupEvaluationDrafts",Guid.NewGuid(),member));r.EnsureSuccessStatusCode();var id=(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var raw=await admin.GetStringAsync($"/api/v1/my-followups/{id}");foreach(var d in drafts)Assert.DoesNotContain(d.Id.ToString(),raw);Assert.DoesNotContain("ملاحظة خاصة",raw);Assert.DoesNotContain("لاعب مراجعة اصطناعي",raw);Assert.DoesNotContain(drafts[0].SportEnrollmentId.ToString(),raw);
        var detail=JsonDocument.Parse(raw).RootElement;Assert.Equal(2,detail.GetProperty("item").GetProperty("source").GetProperty("evaluationDrafts").GetProperty("draftCount").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync($"/api/v1/evaluations/{drafts[0].Id}")).StatusCode);
        (await Send(admin,$"/api/v1/my-followups/{id}",new AssignedFollowUpWrite("Closed",detail.GetProperty("item").GetProperty("version").GetUInt32(),Guid.NewGuid(),ResolutionReason:"راجعت المتابعة دون صلاحية نشر التقييم"))).EnsureSuccessStatusCode();
        using var other=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/manage/owner-followups/{id}")).StatusCode);
        var license=await db.AcademyFeatureEntitlements.SingleAsync(l=>l.AcademyId==g.AcademyId&&l.Feature=="training");var enabled=license.Enabled;
        try{license.Enabled=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.Forbidden,(await owner.GetAsync($"/api/v1/manage/owner-followups/{id}")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync($"/api/v1/my-followups/{id}")).StatusCode);}finally{license.Enabled=enabled;await db.SaveChangesAsync();}
        foreach(var d in drafts.Take(2))Assert.Equal(EvaluationStatus.Draft,(await db.PlayerEvaluations.AsNoTracking().SingleAsync(e=>e.Id==d.Id)).Status);
    });
}
