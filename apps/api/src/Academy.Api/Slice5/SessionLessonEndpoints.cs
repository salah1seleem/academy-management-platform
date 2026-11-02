using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice5;
public static partial class SessionLessonEndpoints
{
    private const string Root="/api/v1/evaluations/lessons";
    public static void MapSessionLessonEndpoints(this WebApplication app)
    {
        var g=app.MapGroup(Root).RequireAuthorization(AcademyPermissions.EvaluationRead);
        g.MapGet("/library",Library);
        g.MapPost("/exercises",CreateExercise).RequireAuthorization(AcademyPermissions.EvaluationCriteriaManage).AddEndpointFilter<CsrfFilter>();
        g.MapPost("/exercises/{id:guid}/review",ReviewExercise).RequireAuthorization(AcademyPermissions.EvaluationCriteriaManage).AddEndpointFilter<CsrfFilter>();
        g.MapPost("/templates",CreateTemplate).RequireAuthorization(AcademyPermissions.EvaluationCriteriaManage).AddEndpointFilter<CsrfFilter>();
        g.MapGet("/templates/{id:guid}",TemplateDetail);
        g.MapPost("/templates/{id:guid}/review",ReviewTemplate).RequireAuthorization(AcademyPermissions.EvaluationCriteriaManage).AddEndpointFilter<CsrfFilter>();
        g.MapGet("/sessions/{id:guid}",SessionDetail);
        g.MapPost("/sessions/{id:guid}",Assign).RequireAuthorization(AcademyPermissions.EvaluationManage).AddEndpointFilter<CsrfFilter>();
        g.MapPost("/exercises/{id:guid}/archive",ArchiveExercise).RequireAuthorization(AcademyPermissions.EvaluationCriteriaManage).AddEndpointFilter<CsrfFilter>().AddEndpointFilter<ArchiveConflict>();
        g.MapPost("/templates/{id:guid}/archive",ArchiveTemplate).RequireAuthorization(AcademyPermissions.EvaluationCriteriaManage).AddEndpointFilter<CsrfFilter>().AddEndpointFilter<ArchiveConflict>();
    }
    private static bool Global(TenantMembership t,FoundationDbContext db)=>t.Role==AcademyRole.AcademyOwner || t.Role==AcademyRole.AcademyAdmin&&!db.BranchScopeRestricted;
    private static async Task<bool> Define(TenantMembership t,FoundationDbContext db,HttpContext h,bool add)=>Global(t,db)&&await PlayerGoalEndpoints.CanWrite(t,db,h,add,AcademyPermissions.EvaluationCriteriaManage);
    private static bool Text(string? value,int max)=>!string.IsNullOrWhiteSpace(value)&&value.Trim().Length<=max;
    private static IResult Bad(string message)=>Results.BadRequest(new{message});
    private static IResult Changed()=>Results.Conflict(new{message="تغيرت الخطة أو الحصة؛ أعد فتحها قبل الحفظ."});
    private static string Hash(object value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static object ExerciseRow(TrainingExercise x,TrainingContentArchive? archive=null)=>new{x.Id,x.TrainingLevelId,x.SportId,x.Title,x.Skill,x.Instructions,x.Equipment,x.Minutes,x.RightsKind,x.RightsReference,x.State,x.CopiedFromId,x.Version,x.ReviewedAtUtc,archive=ArchiveRow(archive)};
    private static object TemplateRow(SessionLessonTemplate x,TrainingContentArchive? archive=null)=>new{x.Id,x.TrainingLevelId,x.SportId,x.Title,x.DeclaredMinutes,x.State,x.CopiedFromId,x.Version,x.ReviewedAtUtc,archive=ArchiveRow(archive)};
    private static IQueryable<TrainingExercise> Exercises(TenantMembership t,FoundationDbContext db)=>db.TrainingExercises.AsNoTracking().Where(e=>e.AcademyId==t.AcademyId&&(e.State=="Reviewed"||Global(t,db)));
    private static IQueryable<SessionLessonTemplate> Templates(TenantMembership t,FoundationDbContext db)=>db.SessionLessonTemplates.AsNoTracking().Where(e=>e.AcademyId==t.AcademyId&&(e.State=="Reviewed"||Global(t,db)));
    private static IQueryable<Academy.Infrastructure.Structure.TrainingLevel> Levels(TenantMembership t,FoundationDbContext db)
    {
        var q=db.TrainingLevels.AsNoTracking().Where(l=>l.AcademyId==t.AcademyId&&l.IsActive&&db.Sports.Any(s=>s.Id==l.SportId&&s.IsActive));
        if(t.Role==AcademyRole.Coach)q=q.Where(l=>db.StaffGroupAssignments.Any(a=>a.AcademyMembershipId==t.MembershipId&&a.IsActive&&a.TrainingGroup.IsActive&&a.TrainingGroup.SportId==l.SportId));
        return q;
    }
    private static async Task<IResult> Library(Guid? levelId,bool? includeArchived,CurrentTenant tenant,FoundationDbContext db,HttpContext h)
    {
        h.Response.Headers.CacheControl="private, no-store";var t=(await tenant.ResolveAsync())!;
        var levels=Levels(t,db);var ids=levels.Select(l=>l.Id);var es=Exercises(t,db).Where(e=>ids.Contains(e.TrainingLevelId));var ps=Templates(t,db).Where(e=>ids.Contains(e.TrainingLevelId));
        if(includeArchived==true&&!Global(t,db))return Results.Forbid();
        if(includeArchived!=true){es=UsableExercises(es,db);ps=UsableTemplates(ps,db);}
        if(levelId.HasValue){es=es.Where(e=>e.TrainingLevelId==levelId);ps=ps.Where(e=>e.TrainingLevelId==levelId);}
        var exercises=await es.OrderByDescending(e=>e.CreatedAtUtc).ThenBy(e=>e.Id).Take(101).ToArrayAsync();var plans=await ps.OrderByDescending(e=>e.CreatedAtUtc).ThenBy(e=>e.Id).Take(101).ToArrayAsync();
        var exIds=exercises.Select(e=>e.Id).ToArray();var planIds=plans.Select(p=>p.Id).ToArray();var archives=await db.TrainingContentArchives.AsNoTracking().Where(a=>exIds.Contains(a.ExerciseId??Guid.Empty)||planIds.Contains(a.TemplateId??Guid.Empty)).ToArrayAsync();
        return Results.Ok(new{levels=await levels.OrderBy(l=>l.Rank).Select(l=>new{l.Id,name=l.ArabicName,sport=db.Sports.Where(s=>s.Id==l.SportId).Select(s=>s.ArabicName).First()}).ToArrayAsync(),exercises=exercises.Take(100).Select(e=>ExerciseRow(e,archives.SingleOrDefault(a=>a.ExerciseId==e.Id))),templates=plans.Take(100).Select(p=>TemplateRow(p,archives.SingleOrDefault(a=>a.TemplateId==p.Id))),hasMore=exercises.Length>100||plans.Length>100,canCreate=await Define(t,db,h,true),canReview=await Define(t,db,h,false)});
    }
    private static async Task<IResult> CreateExercise(ExerciseWrite r,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        if(r.RequestId==Guid.Empty||!Text(r.Title,100)||!Text(r.Skill,120)||!Text(r.Instructions,2000)||!Text(r.Equipment,300)||r.Minutes is <1 or >180||r.RightsKind is not ("Original" or "Licensed")||!Text(r.RightsReference,500))return Bad("أدخل تمرينًا أصليًا أو مرخصًا بمهارة وأدوات ومدة ومصدر حقوق واضح.");
        var t=(await tenant.ResolveAsync())!;await using var tx=await db.Database.BeginTransactionAsync();if(!Global(t,db)||!await PlayerGoalEndpoints.LockAuthority(t,db,h,null,r.RequestId,true,AcademyPermissions.EvaluationCriteriaManage))return Results.Forbid();
        var level=await Levels(t,db).SingleOrDefaultAsync(l=>l.Id==r.TrainingLevelId);if(level==null)return Results.NotFound();
        var hash=Hash(r);var old=await db.TrainingExercises.SingleOrDefaultAsync(x=>x.CreatedByUserId==db.RequestUserId&&x.RequestId==r.RequestId);if(old!=null)return old.RequestHash==hash?Results.Ok(new{old.Id,replay=true}):Changed();
        if(r.CopiedFromId.HasValue&&!await Exercises(t,db).AnyAsync(x=>x.Id==r.CopiedFromId&&x.TrainingLevelId==level.Id))return Results.NotFound();
        var now=clock.GetUtcNow();var e=new TrainingExercise{Id=Guid.NewGuid(),AcademyId=t.AcademyId,TrainingLevelId=level.Id,SportId=level.SportId,CopiedFromId=r.CopiedFromId,Title=r.Title.Trim(),Skill=r.Skill.Trim(),Instructions=r.Instructions.Trim(),Equipment=r.Equipment.Trim(),Minutes=r.Minutes,RightsKind=r.RightsKind,RightsReference=r.RightsReference.Trim(),CreatedByUserId=db.RequestUserId,RequestId=r.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};db.TrainingExercises.Add(e);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created($"{Root}/library",new{e.Id});
    }
    private static async Task<IResult> CreateTemplate(LessonTemplateWrite r,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        if(r.RequestId==Guid.Empty||!Text(r.Title,100)||r.DeclaredMinutes is <1 or >180||r.Steps is not {Count:>=1 and <=20}||r.Steps.Any(s=>s is null||s.Minutes is <1 or >180)||r.Steps.Sum(s=>s.Minutes)>180)return Bad("اختر من1إلى20 تمرينًا ومدة إجمالية لا تتجاوز180 دقيقة.");
        var t=(await tenant.ResolveAsync())!;await using var tx=await db.Database.BeginTransactionAsync();if(!Global(t,db)||!await PlayerGoalEndpoints.LockAuthority(t,db,h,null,r.RequestId,true,AcademyPermissions.EvaluationCriteriaManage))return Results.Forbid();
        await ContentLock(db,t.AcademyId);
        var level=await Levels(t,db).SingleOrDefaultAsync(l=>l.Id==r.TrainingLevelId);if(level==null)return Results.NotFound();
        var hash=Hash(r);var old=await db.SessionLessonTemplates.SingleOrDefaultAsync(x=>x.CreatedByUserId==db.RequestUserId&&x.RequestId==r.RequestId);if(old!=null)return old.RequestHash==hash?Results.Ok(new{old.Id,replay=true}):Changed();
        var ids=r.Steps.Select(s=>s.ExerciseId).Distinct().ToArray();if(await UsableExercises(Exercises(t,db),db).CountAsync(e=>ids.Contains(e.Id)&&e.TrainingLevelId==level.Id)!=ids.Length)return Bad("اختر تمارين غير مؤرشفة من الرياضة والمستوى المختارين.");
        if(r.CopiedFromId.HasValue&&!await Templates(t,db).AnyAsync(x=>x.Id==r.CopiedFromId&&x.TrainingLevelId==level.Id))return Results.NotFound();
        var now=clock.GetUtcNow();var p=new SessionLessonTemplate{Id=Guid.NewGuid(),AcademyId=t.AcademyId,TrainingLevelId=level.Id,SportId=level.SportId,CopiedFromId=r.CopiedFromId,Title=r.Title.Trim(),DeclaredMinutes=r.DeclaredMinutes,CreatedByUserId=db.RequestUserId,RequestId=r.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};db.SessionLessonTemplates.Add(p);
        db.SessionLessonSteps.AddRange(r.Steps.Select((s,i)=>new SessionLessonStep{Id=Guid.NewGuid(),AcademyId=t.AcademyId,TemplateId=p.Id,ExerciseId=s.ExerciseId,TrainingLevelId=level.Id,SportId=level.SportId,Position=i,Minutes=s.Minutes,CreatedAtUtc=now,UpdatedAtUtc=now}));await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created($"{Root}/templates/{p.Id}",new{p.Id});
    }
    private static async Task<IResult> ReviewExercise(Guid id,LessonReview r,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        var t=(await tenant.ResolveAsync())!;if(!r.ConfirmTechnicalReview||r.RequestId==Guid.Empty)return Bad("راجع الملاءمة والحقوق والسلامة ثم أكد اعتمادك الفني.");
        await using var tx=await db.Database.BeginTransactionAsync();if(!Global(t,db)||!await PlayerGoalEndpoints.LockAuthority(t,db,h,null,r.RequestId,false,AcademyPermissions.EvaluationCriteriaManage))return Results.Forbid();
        await ContentLock(db,t.AcademyId);if(await db.TrainingContentArchives.AnyAsync(a=>a.ExerciseId==id))return Changed();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"TrainingExercises\" WHERE \"Id\"={id} AND \"AcademyId\"={t.AcademyId} FOR UPDATE");var e=await db.TrainingExercises.SingleOrDefaultAsync(x=>x.Id==id);if(e==null||!await Levels(t,db).AnyAsync(l=>l.Id==e.TrainingLevelId))return Results.NotFound();
        if(e.State=="Reviewed")return e.ReviewedByUserId==db.RequestUserId?Results.Ok(new{e.Id,alreadyReviewed=true}):Changed();if(e.Version!=r.Version)return Changed();e.State="Reviewed";e.ReviewedByUserId=db.RequestUserId;e.ReviewedAtUtc=clock.GetUtcNow();e.UpdatedAtUtc=clock.GetUtcNow();await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{e.Id});
    }
    private static async Task<object> Detail(SessionLessonTemplate p,FoundationDbContext db)
    {
        var source=await (from s in db.SessionLessonSteps.AsNoTracking() join e in db.TrainingExercises.AsNoTracking() on s.ExerciseId equals e.Id where s.TemplateId==p.Id orderby s.Position select new{s.Position,s.Minutes,exercise=e}).ToArrayAsync();var ids=source.Select(s=>s.exercise.Id).ToArray();var archives=await db.TrainingContentArchives.AsNoTracking().Where(a=>a.TemplateId==p.Id||ids.Contains(a.ExerciseId??Guid.Empty)).ToArrayAsync();
        var steps=source.Select(s=>new{s.Position,s.Minutes,exercise=ExerciseRow(s.exercise,archives.SingleOrDefault(a=>a.ExerciseId==s.exercise.Id))}).ToArray();var total=steps.Sum(s=>s.Minutes);return new{item=TemplateRow(p,archives.SingleOrDefault(a=>a.TemplateId==p.Id)),steps,contentRetired=archives.Length>0,computedMinutes=total,durationMatches=total==p.DeclaredMinutes};
    }
    private static async Task<IResult> TemplateDetail(Guid id,CurrentTenant tenant,FoundationDbContext db,HttpContext h)
    {h.Response.Headers.CacheControl="private, no-store";var t=(await tenant.ResolveAsync())!;var p=await Templates(t,db).SingleOrDefaultAsync(x=>x.Id==id&&Levels(t,db).Select(l=>l.Id).Contains(x.TrainingLevelId));return p==null?Results.NotFound():Results.Ok(await Detail(p,db));}
    private static async Task<IResult> ReviewTemplate(Guid id,LessonReview r,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        var t=(await tenant.ResolveAsync())!;if(!r.ConfirmTechnicalReview||r.RequestId==Guid.Empty)return Bad("أكد المراجعة الفنية للخطة قبل اعتمادها.");
        await using var tx=await db.Database.BeginTransactionAsync();if(!Global(t,db)||!await PlayerGoalEndpoints.LockAuthority(t,db,h,null,r.RequestId,false,AcademyPermissions.EvaluationCriteriaManage))return Results.Forbid();
        await ContentLock(db,t.AcademyId);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SessionLessonTemplates\" WHERE \"Id\"={id} AND \"AcademyId\"={t.AcademyId} FOR UPDATE");var p=await db.SessionLessonTemplates.SingleOrDefaultAsync(x=>x.Id==id);if(p==null||!await Levels(t,db).AnyAsync(l=>l.Id==p.TrainingLevelId))return Results.NotFound();
        if(!await UsableTemplates(Templates(t,db),db).AnyAsync(x=>x.Id==id))return Changed();
        if(p.State=="Reviewed")return p.ReviewedByUserId==db.RequestUserId?Results.Ok(new{p.Id,alreadyReviewed=true}):Changed();if(p.Version!=r.Version)return Changed();
        var steps=await db.SessionLessonSteps.Where(s=>s.TemplateId==id).ToArrayAsync();if(steps.Length is <1 or >20||steps.Sum(s=>s.Minutes)!=p.DeclaredMinutes)return Bad("مجموع مدد التمارين لا يطابق المدة المعلنة؛ أنشئ نسخة مصححة ولا تعتمد الرقم المكتوب.");
        var ids=steps.Select(s=>s.ExerciseId).Distinct().ToArray();if(await db.TrainingExercises.CountAsync(e=>ids.Contains(e.Id)&&e.State=="Reviewed")!=ids.Length)return Bad("اعتمد كل تمرين فنيًا وراجع حقوقه قبل اعتماد الخطة.");
        p.State="Reviewed";p.ReviewedByUserId=db.RequestUserId;p.ReviewedAtUtc=clock.GetUtcNow();p.UpdatedAtUtc=clock.GetUtcNow();await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{p.Id});
    }
    private static IQueryable<TrainingSession> Sessions(TenantMembership t,FoundationDbContext db)
    {var q=db.TrainingSessions.AsNoTracking().Where(s=>s.AcademyId==t.AcademyId&&s.IsActive&&s.TrainingGroup.IsActive&&s.Branch.IsActive&&s.Sport.IsActive);if(t.Role==AcademyRole.Coach)q=q.Where(s=>FoundationDbContext.IsSessionStaffAssigned(t.AcademyId,t.MembershipId,s.Id));return q;}
    private static async Task<IResult> SessionDetail(Guid id,CurrentTenant tenant,FoundationDbContext db,HttpContext h)
    {
        h.Response.Headers.CacheControl="private, no-store";var t=(await tenant.ResolveAsync())!;var s=await Sessions(t,db).Where(s=>s.Id==id).Select(s=>new{s.Id,s.SessionDate,s.StartTime,s.EndTime,status=s.Status.ToString(),group=s.TrainingGroup.ArabicName,levelId=s.TrainingGroup.TrainingLevelId,levelActive=db.TrainingLevels.Any(l=>l.Id==s.TrainingGroup.TrainingLevelId&&l.IsActive)}).SingleOrDefaultAsync();if(s==null)return Results.NotFound();
        var assignments=await db.SessionLessonAssignments.AsNoTracking().Where(a=>a.TrainingSessionId==id).OrderByDescending(a=>a.Revision).Take(21).ToArrayAsync();var a=assignments.FirstOrDefault();var p=a==null?null:await Templates(t,db).SingleOrDefaultAsync(p=>p.Id==a.TemplateId);
        var minutes=(int)(s.EndTime-s.StartTime).TotalMinutes;var available=await UsableTemplates(Templates(t,db),db).Where(p=>s.levelActive&&p.State=="Reviewed"&&p.TrainingLevelId==s.levelId&&p.DeclaredMinutes==minutes).OrderByDescending(p=>p.CreatedAtUtc).Take(100).ToArrayAsync();
        return Results.Ok(new{session=s,sessionMinutes=minutes,revision=a?.Revision??0,active=p==null?null:await Detail(p,db),contextChanged=p!=null&&(!s.levelActive||p.TrainingLevelId!=s.levelId||p.DeclaredMinutes!=minutes||s.status=="Cancelled"),available=available.Select(p=>TemplateRow(p)),canAssign=s.status=="Scheduled"&&s.levelActive&&await PlayerGoalEndpoints.CanWrite(t,db,h,a==null),history=assignments.Take(20).Select(x=>new{x.Id,x.TemplateId,x.Revision,x.ActorName,x.CreatedAtUtc}),historyHasMore=assignments.Length>20});
    }
    private static async Task<IResult> Assign(Guid id,LessonAssign r,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        var t=(await tenant.ResolveAsync())!;if(r.RequestId==Guid.Empty)return Bad("أعد فتح الحصة قبل الحفظ.");await using var tx=await db.Database.BeginTransactionAsync();
        var first=await Sessions(t,db).SingleOrDefaultAsync(s=>s.Id==id);if(first==null)return Results.NotFound();
        // Follow the existing calendar/grant order; do not acquire coach attendance rights.
        var previous=await db.SessionLessonAssignments.AsNoTracking().Where(a=>a.TrainingSessionId==id).OrderByDescending(a=>a.Revision).FirstOrDefaultAsync();
        if(!await PlayerGoalEndpoints.LockAuthority(t,db,h,null,r.RequestId,previous==null))return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:"+t.AcademyId},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"timetable:{t.AcademyId}:{first.TrainingGroupId}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"group-capacity:{t.AcademyId}:{first.TrainingGroupId}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"TrainingSessions\" WHERE \"Id\"={id} AND \"AcademyId\"={t.AcademyId} FOR UPDATE");
        // Recheck dated/substitute authority after the session/calendar lock, before replay.
        var session=await Sessions(t,db).Include(s=>s.TrainingGroup).SingleOrDefaultAsync(s=>s.Id==id);if(session==null)return Results.NotFound();
        var hash=Hash(new{id,r});var old=await db.SessionLessonAssignments.SingleOrDefaultAsync(a=>a.ActorUserId==db.RequestUserId&&a.RequestId==r.RequestId);if(old!=null)return old.RequestHash==hash?Results.Ok(new{old.Id,replay=true}):Changed();
        previous=await db.SessionLessonAssignments.AsNoTracking().Where(a=>a.TrainingSessionId==id).OrderByDescending(a=>a.Revision).FirstOrDefaultAsync();if((previous?.Revision??0)!=r.PreviousRevision||!await PlayerGoalEndpoints.CanWrite(t,db,h,previous==null))return Changed();
        if(session.Status!=TrainingSessionStatus.Scheduled)return Bad("لا تتغير خطة حصة منفذة أو ملغاة؛ يبقى تاريخها محفوظًا.");
        await ContentLock(db,t.AcademyId);var p=await UsableTemplates(Templates(t,db),db).SingleOrDefaultAsync(p=>p.Id==r.TemplateId&&p.State=="Reviewed");if(p==null)return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"TrainingLevels\" WHERE \"Id\"={p.TrainingLevelId} AND \"AcademyId\"={t.AcademyId} FOR SHARE");
        if(p.SportId!=session.SportId||p.TrainingLevelId!=session.TrainingGroup.TrainingLevelId||!await db.TrainingLevels.AnyAsync(l=>l.Id==p.TrainingLevelId&&l.IsActive)||p.DeclaredMinutes!=(int)(session.EndTime-session.StartTime).TotalMinutes)return Bad("اختر خطة معتمدة لنفس مستوى المجموعة ورياضتها ومدة الحصة الفعلية.");
        if(previous?.TemplateId==p.Id)return Results.Ok(new{previous.Id,unchanged=true});
        var now=clock.GetUtcNow();var a=new SessionLessonAssignment{Id=Guid.NewGuid(),AcademyId=t.AcademyId,TrainingSessionId=id,TemplateId=p.Id,SportId=session.SportId,Revision=r.PreviousRevision+1,ActorUserId=db.RequestUserId,ActorName=await db.Users.Where(u=>u.Id==db.RequestUserId).Select(u=>u.DisplayName).SingleAsync(),RequestId=r.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};db.SessionLessonAssignments.Add(a);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{a.Id});
    }
}
public sealed record ExerciseWrite(Guid TrainingLevelId,string Title,string Skill,string Instructions,string Equipment,int Minutes,string RightsKind,string RightsReference,Guid RequestId,Guid? CopiedFromId=null);
public sealed record LessonStepWrite(Guid ExerciseId,int Minutes);
public sealed record LessonTemplateWrite(Guid TrainingLevelId,string Title,int DeclaredMinutes,IReadOnlyList<LessonStepWrite> Steps,Guid RequestId,Guid? CopiedFromId=null);
public sealed record LessonReview(uint Version,bool ConfirmTechnicalReview,Guid RequestId);
public sealed record LessonAssign(Guid TemplateId,int PreviousRevision,Guid RequestId);
