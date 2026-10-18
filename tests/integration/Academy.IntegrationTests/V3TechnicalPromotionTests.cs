using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice5;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    private async Task<(TechnicalFixture F,Guid Request,Guid Target)> PromotionSetup(HttpClient owner,HttpClient coach,FoundationDbContext db,Academy.Infrastructure.Structure.TrainingGroup? selected=null)
    {
        var f=await TechnicalSetup(owner,coach,db,selected);await MasterAll(coach,f);
        var response=await Send(coach,TechnicalPath(f.Plan),TechnicalBody(await TechnicalView(coach,f.Plan)));response.EnsureSuccessStatusCode();var rid=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var detail=await owner.GetFromJsonAsync<JsonElement>($"{Technical}/{rid}");(await Send(owner,$"{Technical}/{rid}/decision",new TechnicalDecisionWrite(detail.GetProperty("request").GetProperty("version").GetUInt32(),"ReadyForPlacement",f.Next,null,Guid.NewGuid()))).EnsureSuccessStatusCode();
        var source=await db.TrainingGroups.AsNoTracking().SingleAsync(g=>g.Id==f.Group);var target=Guid.NewGuid();var actor=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync();var coachId=await db.StaffGroupAssignments.Where(a=>a.TrainingGroupId==f.Group).Select(a=>a.AcademyMembershipId).SingleAsync();
        db.TrainingGroups.Add(new(){Id=target,AcademyId=source.AcademyId,BranchId=source.BranchId,SportId=source.SportId,AgeCategoryId=source.AgeCategoryId,ArabicName="خطوة تقدم اصطناعية "+target.ToString("N")[..6],Capacity=1,TrainingLevelId=f.Next});db.StaffGroupAssignments.Add(new(){Id=Guid.NewGuid(),AcademyId=source.AcademyId,TrainingGroupId=target,AcademyMembershipId=coachId});
        // Distinct date avoids unrelated legacy coach fixtures. The synthetic
        // payment is real application setup, before the no-financial-change check.
        var day=f.Today.AddDays(240);while(await db.TrainingSessions.AnyAsync(s=>s.SessionDate==day&&FoundationDbContext.IsSessionStaffAssigned(source.AcademyId,coachId,s.Id)))day=day.AddDays(1);
        db.TrainingSessions.Add(new(){Id=Guid.NewGuid(),AcademyId=source.AcademyId,TrainingGroupId=target,BranchId=source.BranchId,SportId=source.SportId,AgeCategoryId=source.AgeCategoryId,SessionDate=day,StartTime=new(7,0),EndTime=new(8,0),CreatedByUserId=actor});
        var plan=await db.SubscriptionPlans.FirstAsync(p=>p.SportId==source.SportId&&p.IsActive&&p.ServiceKind==AcademyServiceKind.Training&&p.PlanType==SubscriptionPlanType.Duration);
        await db.SaveChangesAsync();
        await using(var paid=factory.Services.CreateAsyncScope()){var paidDb=paid.ServiceProvider.GetRequiredService<FoundationDbContext>();await using var tx=await paidDb.Database.BeginTransactionAsync();await paid.ServiceProvider.GetRequiredService<CollectionService>().CreateCashAsync(source.AcademyId,actor,f.Enrollment,plan.Id,"promotion-fixture-"+Guid.NewGuid(),"Cash");await tx.CommitAsync();}
        return(f,rid,target);
    }
    private static async Task<TechnicalPromotionWrite> PromotionBody(HttpClient owner,Guid request,Guid group)
    {
        var options=await owner.GetFromJsonAsync<JsonElement>($"{Technical}/{request}/promotion");var body=new TechnicalPromotionWrite(group,options.GetProperty("enrollmentVersion").GetUInt32(),null,Guid.NewGuid());var preview=await Send(owner,$"{Technical}/{request}/promotion/preview",body);Assert.True(preview.IsSuccessStatusCode,await preview.Content.ReadAsStringAsync());var p=await preview.Content.ReadFromJsonAsync<JsonElement>();Assert.Empty(p.GetProperty("preview").GetProperty("conflicts").EnumerateArray());return body with{Fingerprint=p.GetProperty("fingerprint").GetString()};
    }
    [Fact]public async Task Promotion_applies_level_and_dated_group_atomically_replays_and_family_sees_same_source_without_financial_changes()
    {
        using var owner=await Staff();using var coach=await Mobile(AcademyRole.Coach);using var family=await Mobile(AcademyRole.Guardian);using var foreign=await Staff(DemoSeed.FutureOwnerEmail);await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var(f,rid,target)=await PromotionSetup(owner,coach,db);var url=$"{Technical}/{rid}/promotion";
        var cash=await db.Collections.CountAsync();var subs=await db.SubscriptionPeriods.CountAsync();var attendance=await db.PlayerAttendances.CountAsync();var report=await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28");var b=await PromotionBody(owner,rid,target);
        Assert.Equal(HttpStatusCode.Forbidden,(await Send(coach,url,b)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await Send(family,url,b)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync(url)).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(url,b)).StatusCode);
        var saved=await Send(owner,url,b);Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());Assert.True((await(await Send(owner,url,b)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean());Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,url,b with{Fingerprint="changed"})).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,url,b with{RequestId=Guid.NewGuid()})).StatusCode);
        var e=await db.SportEnrollments.AsNoTracking().SingleAsync(e=>e.Id==f.Enrollment);Assert.Equal(f.Next,e.TrainingLevelId);Assert.Equal(f.Group,e.TrainingGroupId);Assert.Equal(f.Group,await db.SportEnrollments.Where(x=>x.Id==e.Id).Select(x=>FoundationDbContext.AssignedGroupAt(x.AcademyId,x.Id,f.Today.AddDays(-1))).SingleAsync());Assert.Equal(target,await db.SportEnrollments.Where(x=>x.Id==e.Id).Select(x=>FoundationDbContext.AssignedGroupAt(x.AcademyId,x.Id,f.Today)).SingleAsync());
        var h=await db.PlayerLevelReviews.AsNoTracking().Where(x=>x.SportEnrollmentId==e.Id).OrderByDescending(x=>x.Revision).FirstAsync();Assert.NotNull(h.TechnicalDecisionId);Assert.NotNull(h.GroupTransferId);Assert.Equal(f.Level,h.FromLevelId);Assert.Equal(f.Next,h.ToLevelId);
        var familyData=await family.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{f.Player}/skills/plans/{f.Plan}/technical-reviews");Assert.Equal(h.ToLevelName,familyData.GetProperty("items")[0].GetProperty("application").GetProperty("toLevelName").GetString());var profile=await family.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{f.Player}/profile");var targetSession=await db.TrainingSessions.Where(s=>s.TrainingGroupId==target).Select(s=>s.Id).SingleAsync();Assert.Contains(profile.GetProperty("enrollments").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==e.Id).GetProperty("upcoming").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==targetSession);
        Assert.Equal(cash,await db.Collections.CountAsync());Assert.Equal(subs,await db.SubscriptionPeriods.CountAsync());Assert.Equal(attendance,await db.PlayerAttendances.CountAsync());Assert.Equal(report,await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
    }
    [Fact]public async Task Promotion_requires_separate_live_group_edit_permission_in_the_scoped_admin_context()=>await DelegateFixture(async(owner,admin,db,group,member)=>{
        using var coach=await Mobile(AcademyRole.Coach);var f=await TechnicalSetup(owner,coach,db,group);await MasterAll(owner,f);var create=await Send(owner,TechnicalPath(f.Plan),TechnicalBody(await TechnicalView(owner,f.Plan)));create.EnsureSuccessStatusCode();var rid=(await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();var detail=await owner.GetFromJsonAsync<JsonElement>($"{Technical}/{rid}");(await Send(owner,$"{Technical}/{rid}/decision",new TechnicalDecisionWrite(detail.GetProperty("request").GetProperty("version").GetUInt32(),"ReadyForPlacement",f.Next,null,Guid.NewGuid()))).EnsureSuccessStatusCode();
        var names=new[]{"players","academy","evaluations"};var grants=await db.StaffActionGrants.Where(g=>g.MembershipId==member&&names.Contains(g.Module)).ToListAsync();var saved=grants.ToDictionary(g=>g.Module,g=>JsonSerializer.Serialize(g));
        foreach(var name in names){var g=grants.SingleOrDefault(g=>g.Module==name);if(g==null){g=new(){Id=Guid.NewGuid(),AcademyId=group.AcademyId,MembershipId=member,Module=name};db.StaffActionGrants.Add(g);grants.Add(g);}g.IsActive=true;g.Read=true;g.Edit=name=="players";g.Add=false;g.Delete=false;}
        try{await db.SaveChangesAsync();Assert.False((await admin.GetFromJsonAsync<JsonElement>($"{Technical}/{rid}")).GetProperty("canReviewPlacement").GetBoolean());Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync($"{Technical}/{rid}/promotion")).StatusCode);var academy=grants.Single(g=>g.Module=="academy");academy.Edit=true;await db.SaveChangesAsync();Assert.True((await admin.GetFromJsonAsync<JsonElement>($"{Technical}/{rid}")).GetProperty("canReviewPlacement").GetBoolean());(await admin.GetAsync($"{Technical}/{rid}/promotion")).EnsureSuccessStatusCode();academy.Edit=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,$"{Technical}/{rid}/promotion/preview",new TechnicalPromotionWrite(group.Id,1,null,Guid.NewGuid()))).StatusCode);Assert.False(await db.PlayerLevelReviews.AnyAsync(h=>h.SportEnrollmentId==f.Enrollment&&h.TechnicalDecisionId!=null));}
        finally{foreach(var g in grants){if(saved.TryGetValue(g.Module,out var text))db.Entry(g).CurrentValues.SetValues(JsonSerializer.Deserialize<Academy.Infrastructure.Tenancy.StaffActionGrant>(text)!);else db.StaffActionGrants.Remove(g);}await db.SaveChangesAsync();}
    });
    [Fact]public async Task Promotion_changed_evidence_or_capacity_invalidates_preview_without_partial_level_or_transfer()
    {
        using var owner=await Staff();using var coach=await Mobile(AcademyRole.Coach);await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var(f,rid,target)=await PromotionSetup(owner,coach,db);var b=await PromotionBody(owner,rid,target);var url=$"{Technical}/{rid}/promotion";
        var group=await db.TrainingGroups.SingleAsync(g=>g.Id==target);group.Capacity=0;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,url,b)).StatusCode);group.Capacity=1;await db.SaveChangesAsync();b=await PromotionBody(owner,rid,target);
        (await Send(coach,$"{Skills}/plans/{f.Plan}/reviews",new SkillReviewWrite(f.Requirements[0],"Learning",null,f.Today,"دليل أحدث قبل التنفيذ",Guid.NewGuid()))).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,url,b)).StatusCode);Assert.Equal(f.Level,(await db.SportEnrollments.AsNoTracking().SingleAsync(e=>e.Id==f.Enrollment)).TrainingLevelId);Assert.False(await db.EnrollmentGroupTransfers.AnyAsync(t=>t.SportEnrollmentId==f.Enrollment));Assert.False(await db.PlayerLevelReviews.AnyAsync(t=>t.SportEnrollmentId==f.Enrollment&&t.TechnicalDecisionId!=null));
        // A forged same-tenant EF write with otherwise valid foreign keys must
        // roll back its level and transfer too, not merely fail HTTP validation.
        var d=await db.PlayerTechnicalReviewDecisions.AsNoTracking().SingleAsync(d=>d.TechnicalRequestId==rid);var before=await db.SportEnrollments.AsNoTracking().SingleAsync(e=>e.Id==f.Enrollment);var now=DateTimeOffset.UtcNow;var hash="isolated-promotion-forgery";var actor=d.ActorUserId;
        await using(var tx=await db.Database.BeginTransactionAsync()){
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SportEnrollments\" SET \"TrainingLevelId\"={f.Next},\"LevelReviewedByUserId\"={actor},\"LevelReviewedAtUtc\"={now},\"UpdatedAtUtc\"={now} WHERE \"Id\"={f.Enrollment}");
            var tr=new Academy.Infrastructure.People.EnrollmentGroupTransfer{Id=Guid.NewGuid(),AcademyId=before.AcademyId,SportEnrollmentId=before.Id,BranchId=before.BranchId,SportId=before.SportId,SourceGroupId=f.Group,TargetGroupId=target,EffectiveDate=f.Today,ActorUserId=actor,RequestKey="forged-"+Guid.NewGuid(),RequestHash=hash,Snapshot="{}",CreatedAtUtc=now,UpdatedAtUtc=now};db.EnrollmentGroupTransfers.Add(tr);
            db.PlayerLevelReviews.Add(new(){Id=Guid.NewGuid(),AcademyId=before.AcademyId,SportEnrollmentId=before.Id,SportId=before.SportId,TechnicalDecisionId=d.Id,GroupTransferId=tr.Id,Revision=2,FromLevelId=f.Level,FromLevelName=await db.TrainingLevels.Where(l=>l.Id==f.Level).Select(l=>l.ArabicName).SingleAsync(),ToLevelId=f.Next,ToLevelName=await db.TrainingLevels.Where(l=>l.Id==f.Next).Select(l=>l.ArabicName).SingleAsync(),ActorUserId=actor,ActorName="مراجع اختبار اصطناعي",Action="Changed",RequestId=Guid.NewGuid(),RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>tx.CommitAsync());Assert.Equal("23514",error.SqlState);
        }
        db.ChangeTracker.Clear();Assert.Equal(f.Level,(await db.SportEnrollments.AsNoTracking().SingleAsync(e=>e.Id==f.Enrollment)).TrainingLevelId);Assert.False(await db.EnrollmentGroupTransfers.AnyAsync(t=>t.SportEnrollmentId==f.Enrollment));
    }
}
