using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Facilities;
using Academy.Api.Operations;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice5;
public static class PlayerTechnicalReviewEndpoints
{
    private const string Root="/api/v1/evaluations/technical-reviews";
    public static void MapPlayerTechnicalReviews(this WebApplication app)
    {
        var g=app.MapGroup(Root).RequireAuthorization(AcademyPermissions.EvaluationRead);
        g.MapGet("/inbox",Inbox).RequireAuthorization(AcademyPermissions.PeopleManage);
        g.MapGet("/{id:guid}",Detail);
        g.MapPost("/{id:guid}/decision",Decide).RequireAuthorization(AcademyPermissions.PeopleManage).AddEndpointFilter<CsrfFilter>().AddEndpointFilter<TechnicalConflictFilter>();
        app.MapGet("/api/v1/evaluations/skills/plans/{id:guid}/technical-reviews",PlanRequests).RequireAuthorization(AcademyPermissions.EvaluationRead);
        app.MapPost("/api/v1/evaluations/skills/plans/{id:guid}/technical-reviews",RequestReview).RequireAuthorization(AcademyPermissions.EvaluationManage).AddEndpointFilter<CsrfFilter>().AddEndpointFilter<TechnicalConflictFilter>();
        app.MapGet("/api/v1/guardian/children/{playerId:guid}/skills/plans/{id:guid}/technical-reviews",FamilyRequests).RequireAuthorization(AcademyPermissions.GuardianEvaluationRead);
    }
    private static IResult Changed(string message="تغيرت أدلة المهارات أو المراجعة؛ أعد فتحها قبل الحفظ.")=>Results.Conflict(new{message});
    private static string Hash(object o)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o))));
    private static object RequestRow(PlayerTechnicalReviewRequest r)=>new{r.Id,r.PlanId,r.SportEnrollmentId,r.Sequence,r.SourceReviewSequence,r.LevelName,r.CurriculumTitle,requirements=r.EvidenceReviewIds.Length,r.ActorName,r.CreatedAtUtc,r.Version};
    private static object DecisionRow(PlayerTechnicalReviewDecision d)=>new{d.Id,d.Outcome,d.RecommendedLevelId,d.RecommendedLevelName,d.FamilyNote,d.ActorName,d.CreatedAtUtc};
    private static async Task<bool> CanDecide(TenantMembership t,FoundationDbContext db,HttpContext h)
    {
        if(t.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin)||!(await h.RequestServices.GetRequiredService<IAuthorizationService>().AuthorizeAsync(h.User,h,AcademyPermissions.PeopleManage)).Succeeded)return false;
        if(t.Role==AcademyRole.AcademyOwner)return true;
        var member=await db.AcademyMemberships.AsNoTracking().SingleAsync(m=>m.Id==t.MembershipId);
        return member.UseActionGrants?await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.IsActive&&g.Module=="players"&&g.Read&&g.Edit)&&await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.IsActive&&g.Module=="evaluations"&&g.Read):!await db.StaffGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.IsActive&&(g.Permission==AcademyPermissions.PeopleManage||g.Permission==AcademyPermissions.EvaluationRead)&&!g.Allowed);
    }
    private static async Task<object[]> Rows(PlayerTechnicalReviewRequest[] requests,FoundationDbContext db)
    {
        var ids=requests.Select(r=>r.Id).ToArray();var decisions=await db.PlayerTechnicalReviewDecisions.AsNoTracking().Where(d=>ids.Contains(d.TechnicalRequestId)).ToDictionaryAsync(d=>d.TechnicalRequestId);
        return requests.Select(r=>(object)new{request=RequestRow(r),decision=decisions.TryGetValue(r.Id,out var d)?DecisionRow(d):null}).ToArray();
    }
    private static async Task<object> PlanView(PlayerSkillPlan plan,PlayerGoalEndpoints.Context e,TenantMembership t,FoundationDbContext db,HttpContext h,bool family)
    {
        var source=await PlayerSkillEvidence.Read(plan,e.TrainingLevelId,db);var requests=await db.PlayerTechnicalReviewRequests.AsNoTracking().Where(r=>r.PlanId==plan.Id).OrderByDescending(r=>r.Sequence).Take(21).ToArrayAsync();var last=requests.FirstOrDefault();
        var closed=last==null||await db.PlayerTechnicalReviewDecisions.AnyAsync(d=>d.TechnicalRequestId==last.Id);
        return new{plan=new{plan.Id,plan.Version},enrollment=e,source=new{source.Eligible,source.Head,requirements=source.Requirements.Length,mastered=source.Latest.Count(r=>r.State=="Mastered")},latestRequestId=last?.Id,
            canRequest=!family&&source.Eligible&&closed&&(last==null||source.Head>last.SourceReviewSequence)&&await PlayerGoalEndpoints.CanWrite(t,db,h,true),canDecide=!family&&await CanDecide(t,db,h),items=await Rows(requests.Take(20).ToArray(),db),hasMore=requests.Length>20};
    }
    private static async Task<IResult> PlanRequests(Guid id,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider time)
    {
        h.Response.Headers.CacheControl="private, no-store";var t=(await tenant.ResolveAsync())!;var p=await db.PlayerSkillPlans.AsNoTracking().SingleOrDefaultAsync(p=>p.Id==id);if(p==null)return Results.NotFound();var e=await PlayerGoalEndpoints.Enrollment(p.SportEnrollmentId,t,db,time);return e==null?Results.NotFound():Results.Ok(await PlanView(p,e,t,db,h,false));
    }
    private static async Task<IResult> FamilyRequests(Guid playerId,Guid id,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider time)
    {
        h.Response.Headers.CacheControl="private, no-store";var t=(await tenant.ResolveAsync())!;
        if(!await db.GuardianPlayerLinks.AnyAsync(l=>l.PlayerId==playerId&&l.IsActive&&l.Guardian.IsActive&&l.Guardian.UserId==db.RequestUserId&&l.Player.IsActive))return Results.NotFound();
        var p=await db.PlayerSkillPlans.AsNoTracking().SingleOrDefaultAsync(p=>p.Id==id&&db.SportEnrollments.Any(e=>e.Id==p.SportEnrollmentId&&e.PlayerId==playerId));if(p==null)return Results.NotFound();var e=await PlayerGoalEndpoints.Enrollment(p.SportEnrollmentId,t,db,time);return e==null?Results.NotFound():Results.Ok(await PlanView(p,e,t,db,h,true));
    }
    private static async Task<IResult> RequestReview(Guid id,TechnicalReviewWrite r,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider time)
    {
        var t=(await tenant.ResolveAsync())!;if(r.RequestId==Guid.Empty)return Results.BadRequest(new{message="أعد فتح خطة المهارات قبل إرسال الطلب."});var p=await db.PlayerSkillPlans.AsNoTracking().SingleOrDefaultAsync(p=>p.Id==id);if(p==null)return Results.NotFound();
        await using var tx=await db.Database.BeginTransactionAsync();if(!await PlayerGoalEndpoints.LockAuthority(t,db,h,p.SportEnrollmentId,r.RequestId,true))return Results.Forbid();var e=await PlayerGoalEndpoints.Enrollment(p.SportEnrollmentId,t,db,time);if(e==null)return Results.NotFound();
        var hash=Hash(new{id,r});var replay=await db.PlayerTechnicalReviewRequests.AsNoTracking().SingleOrDefaultAsync(x=>x.ActorUserId==db.RequestUserId&&x.RequestId==r.RequestId);if(replay!=null)return replay.RequestHash==hash?Results.Ok(new{replay.Id,replay=true}):Changed();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"PlayerSkillPlans\" WHERE \"Id\"={id} AND \"AcademyId\"={t.AcademyId} FOR SHARE");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SkillCurricula\" WHERE \"Id\"={p.CurriculumId} AND \"AcademyId\"={t.AcademyId} FOR SHARE");
        p=await db.PlayerSkillPlans.AsNoTracking().SingleAsync(p=>p.Id==id);var source=await PlayerSkillEvidence.Read(p,e.TrainingLevelId,db);var last=await db.PlayerTechnicalReviewRequests.AsNoTracking().Where(x=>x.PlanId==id).OrderByDescending(x=>x.Sequence).FirstOrDefaultAsync();
        if(p.Version!=r.PlanVersion||!source.Eligible||source.Head!=r.SourceReviewSequence||last?.Id!=r.PreviousRequestId||last!=null&&(!await db.PlayerTechnicalReviewDecisions.AnyAsync(d=>d.TechnicalRequestId==last.Id)||source.Head<=last.SourceReviewSequence))return Changed("يلزم إتقان كل متطلبات الخطة الحالية وأدلة حديثة، دون طلب مفتوح آخر. أعد تحميل الخطة.");
        var now=time.GetUtcNow();var row=new PlayerTechnicalReviewRequest{Id=Guid.NewGuid(),AcademyId=t.AcademyId,PlanId=id,CurriculumId=p.CurriculumId,SportId=p.SportId,SportEnrollmentId=p.SportEnrollmentId,TrainingLevelId=source.Curriculum.TrainingLevelId,LevelName=await db.TrainingLevels.Where(l=>l.Id==e.TrainingLevelId).Select(l=>l.ArabicName).SingleAsync(),CurriculumTitle=source.Curriculum.Title,Sequence=(last?.Sequence??0)+1,SourceReviewSequence=source.Head,EvidenceReviewIds=source.Latest.OrderBy(r=>r.RequirementId).Select(r=>r.Id).ToArray(),PreviousRequestId=last?.Id,ActorUserId=db.RequestUserId,ActorName=await db.Users.Where(u=>u.Id==db.RequestUserId).Select(u=>u.DisplayName).SingleAsync(),RequestId=r.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};db.PlayerTechnicalReviewRequests.Add(row);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created($"{Root}/{row.Id}",new{row.Id});
    }
    private static async Task<IResult> Inbox(bool? includeDecided,Guid? branchId,Guid? sportId,CurrentTenant tenant,FoundationDbContext db,HttpContext h)
    {
        h.Response.Headers.CacheControl="private, no-store";var t=(await tenant.ResolveAsync())!;if(t.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin))return Results.Forbid();
        var q=db.PlayerTechnicalReviewRequests.AsNoTracking().Where(r=>db.SportEnrollments.Any(e=>e.Id==r.SportEnrollmentId&&e.IsActive&&e.Player.IsActive&&e.Branch.IsActive&&e.Sport.IsActive));if(includeDecided!=true)q=q.Where(r=>!db.PlayerTechnicalReviewDecisions.Any(d=>d.TechnicalRequestId==r.Id));
        if(branchId.HasValue)q=q.Where(r=>db.SportEnrollments.Any(e=>e.Id==r.SportEnrollmentId&&e.BranchId==branchId));if(sportId.HasValue)q=q.Where(r=>r.SportId==sportId);
        var requests=await q.OrderBy(r=>r.CreatedAtUtc).ThenBy(r=>r.Id).Take(101).ToArrayAsync();var ids=requests.Take(100).Select(r=>r.SportEnrollmentId).ToArray();var children=await db.SportEnrollments.AsNoTracking().Where(e=>ids.Contains(e.Id)).Select(e=>new{e.Id,player=e.Player.ArabicName,branch=e.Branch.ArabicName,sport=e.Sport.ArabicName}).ToDictionaryAsync(e=>e.Id,e=>new{e.player,e.branch,e.sport});
        return Results.Ok(new{items=await Rows(requests.Take(100).ToArray(),db),contexts=children,hasMore=requests.Length>100,canDecide=await CanDecide(t,db,h)});
    }
    private static async Task<IResult> Detail(Guid id,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider time)
    {
        h.Response.Headers.CacheControl="private, no-store";var t=(await tenant.ResolveAsync())!;var r=await db.PlayerTechnicalReviewRequests.AsNoTracking().SingleOrDefaultAsync(r=>r.Id==id);if(r==null)return Results.NotFound();var e=await PlayerGoalEndpoints.Enrollment(r.SportEnrollmentId,t,db,time);if(e==null)return Results.NotFound();var p=await db.PlayerSkillPlans.AsNoTracking().SingleAsync(p=>p.Id==r.PlanId);var source=await PlayerSkillEvidence.Read(p,e.TrainingLevelId,db);var d=await db.PlayerTechnicalReviewDecisions.AsNoTracking().SingleOrDefaultAsync(d=>d.TechnicalRequestId==id);
        var evidence=await(from v in db.PlayerSkillReviews.AsNoTracking() join s in db.SkillRequirements.AsNoTracking() on v.RequirementId equals s.Id where r.EvidenceReviewIds.Contains(v.Id) orderby s.DisplayOrder select new{s.Name,s.Unit,s.Protocol,v.State,v.MeasuredValue,v.ObservedOn,v.ActorName}).ToArrayAsync();
        var currentLevel=await db.TrainingLevels.AsNoTracking().SingleAsync(l=>l.Id==r.TrainingLevelId);var choices=await db.TrainingLevels.Where(l=>l.IsActive&&l.SportId==r.SportId&&l.Rank>currentLevel.Rank).OrderBy(l=>l.Rank).Select(l=>new{l.Id,name=l.ArabicName}).ToArrayAsync();
        return Results.Ok(new{request=RequestRow(r),decision=d==null?null:DecisionRow(d),enrollment=e,evidence,currentEligible=source.Eligible&&source.Head==r.SourceReviewSequence,contextChanged=!source.Eligible||source.Head!=r.SourceReviewSequence,canDecide=d==null&&await CanDecide(t,db,h),levels=choices});
    }
    private static async Task<IResult> Decide(Guid id,TechnicalDecisionWrite r,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider time,GroupPlacementService placement)
    {
        if(r.RequestId==Guid.Empty||r.Outcome is not ("ReadyForPlacement" or "NeedsPractice" or "ContextChanged")||r.FamilyNote?.Length>500)return Results.BadRequest(new{message="اختر قرار المراجعة ونصًا للأسرة حتى500حرف عند الحاجة."});
        try{
            await using var tx=await db.Database.BeginTransactionAsync();var t=await placement.Authority("players","Edit");if(!await CanDecide(t,db,h))return Results.Forbid();var request=await db.PlayerTechnicalReviewRequests.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id);if(request==null)return Results.NotFound();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SportEnrollments\" WHERE \"Id\"={request.SportEnrollmentId} AND \"AcademyId\"={t.AcademyId} FOR UPDATE");var e=await PlayerGoalEndpoints.Enrollment(request.SportEnrollmentId,t,db,time);if(e==null)return Results.NotFound();await placement.Authority("players","Edit",(await db.SportEnrollments.Where(e=>e.Id==request.SportEnrollmentId).Select(e=>e.BranchId).SingleAsync()));
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"technical-decision:{t.AcademyId}:{db.RequestUserId}:{r.RequestId}"},0))");
            var hash=Hash(new{id,r});var old=await db.PlayerTechnicalReviewDecisions.AsNoTracking().SingleOrDefaultAsync(d=>d.ActorUserId==db.RequestUserId&&d.RequestId==r.RequestId);if(old!=null)return old.RequestHash==hash?Results.Ok(new{old.Id,replay=true}):Changed();
            if(request.Version!=r.Version||await db.PlayerTechnicalReviewDecisions.AnyAsync(d=>d.TechnicalRequestId==id))return Changed();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"PlayerSkillPlans\" WHERE \"Id\"={request.PlanId} AND \"AcademyId\"={t.AcademyId} FOR SHARE");await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SkillCurricula\" WHERE \"Id\"={request.CurriculumId} AND \"AcademyId\"={t.AcademyId} FOR SHARE");
            var p=await db.PlayerSkillPlans.AsNoTracking().SingleAsync(p=>p.Id==request.PlanId);var source=await PlayerSkillEvidence.Read(p,e.TrainingLevelId,db);var same=source.Eligible&&source.Head==request.SourceReviewSequence;
            if(r.Outcome=="ReadyForPlacement"&&!same||r.Outcome=="ContextChanged"&&same)return Changed("تغيرت الأدلة أو الخطة؛ لا يعتمد الاستعداد على طلب قديم، ويمكن إغلاقه لتغير السياق.");
            Academy.Infrastructure.Structure.TrainingLevel? next=null;
            if(r.Outcome=="ReadyForPlacement"){
                var rank=await db.TrainingLevels.Where(l=>l.Id==request.TrainingLevelId).Select(l=>l.Rank).SingleAsync();next=await db.TrainingLevels.SingleOrDefaultAsync(l=>l.Id==r.RecommendedLevelId&&l.IsActive&&l.SportId==request.SportId&&l.Rank>rank);if(next==null)return Results.BadRequest(new{message="اختر مستوى فعالًا أعلى لنفس الرياضة كتوصية فنية، دون نقل تلقائي."});
            }else if(r.RecommendedLevelId.HasValue)return Results.BadRequest(new{message="التوصية بمستوى تخص قرار الاستعداد فقط."});
            var now=time.GetUtcNow();var d=new PlayerTechnicalReviewDecision{Id=Guid.NewGuid(),AcademyId=t.AcademyId,TechnicalRequestId=id,SportEnrollmentId=request.SportEnrollmentId,SportId=request.SportId,Outcome=r.Outcome,RecommendedLevelId=next?.Id,RecommendedLevelName=next?.ArabicName,FamilyNote=string.IsNullOrWhiteSpace(r.FamilyNote)?null:r.FamilyNote.Trim(),ActorUserId=db.RequestUserId,ActorName=await db.Users.Where(u=>u.Id==db.RequestUserId).Select(u=>u.DisplayName).SingleAsync(),RequestId=r.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};db.PlayerTechnicalReviewDecisions.Add(d);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{d.Id});
        }catch(FacilityRejected ex){return Results.Problem(statusCode:ex.Status,title:ex.Message);}
    }
}
public sealed record TechnicalReviewWrite(uint PlanVersion,int SourceReviewSequence,Guid? PreviousRequestId,Guid RequestId);
public sealed record TechnicalDecisionWrite(uint Version,string Outcome,Guid? RecommendedLevelId,string? FamilyNote,Guid RequestId);
public sealed class TechnicalConflictFilter:IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context,EndpointFilterDelegate next)
    {
        try{return await next(context);}
        catch(DbUpdateConcurrencyException){return Results.Conflict(new{message="تغيرت المراجعة أثناء الحفظ؛ أعد فتحها قبل المحاولة."});}
        catch(DbUpdateException ex)when(ex.InnerException is Npgsql.PostgresException{SqlState:"23505" or "23514"}){return Results.Conflict(new{message="تغيرت أدلة المراجعة أو سبق حفظ القرار؛ أعد فتح المصدر قبل المحاولة."});}
    }
}
