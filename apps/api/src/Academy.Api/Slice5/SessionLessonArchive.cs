using Academy.Api.Auth;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice5;
public sealed record LessonArchiveWrite(uint Version,string? Note,Guid RequestId);
public static partial class SessionLessonEndpoints
{
    private static object? ArchiveRow(TrainingContentArchive? a)=>a==null?null:new{a.ActorName,a.Note,a.CreatedAtUtc};
    private static IQueryable<TrainingExercise> UsableExercises(IQueryable<TrainingExercise> q,FoundationDbContext db)=>q.Where(e=>!db.TrainingContentArchives.Any(a=>a.ExerciseId==e.Id));
    private static IQueryable<SessionLessonTemplate> UsableTemplates(IQueryable<SessionLessonTemplate> q,FoundationDbContext db)=>q.Where(p=>!db.TrainingContentArchives.Any(a=>a.TemplateId==p.Id)&&!db.SessionLessonSteps.Any(s=>s.TemplateId==p.Id&&db.TrainingContentArchives.Any(a=>a.ExerciseId==s.ExerciseId)));
    private static Task ContentLock(FoundationDbContext db,Guid academy)=>db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"lesson-content:{academy}"},0))");
    private static Task<IResult> ArchiveExercise(Guid id,LessonArchiveWrite r,CurrentTenant t,FoundationDbContext db,HttpContext h,TimeProvider c)=>Archive(id,r,false,t,db,h,c);
    private static Task<IResult> ArchiveTemplate(Guid id,LessonArchiveWrite r,CurrentTenant t,FoundationDbContext db,HttpContext h,TimeProvider c)=>Archive(id,r,true,t,db,h,c);
    private static async Task<IResult> Archive(Guid id,LessonArchiveWrite r,bool template,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        if(r.RequestId==Guid.Empty||r.Note?.Length>500)return Bad("الملاحظة اختيارية حتى 500 حرف؛ أعد فتح المحتوى قبل الأرشفة.");
        var t=(await tenant.ResolveAsync())!;await using var tx=await db.Database.BeginTransactionAsync();
        if(!Global(t,db)||!await PlayerGoalEndpoints.LockAuthority(t,db,h,null,r.RequestId,false,AcademyPermissions.EvaluationCriteriaManage))return Results.Forbid();
        if(!await db.AcademyMemberships.AsNoTracking().AnyAsync(m=>m.Id==t.MembershipId&&m.Role==t.Role&&(m.Role==AcademyRole.AcademyOwner||m.AllBranches)))return Results.Forbid();
        await ContentLock(db,t.AcademyId);
        var e=template?null:await Exercises(t,db).SingleOrDefaultAsync(e=>e.Id==id);var p=template?await Templates(t,db).SingleOrDefaultAsync(p=>p.Id==id):null;
        if(e==null&&p==null||!await Levels(t,db).AnyAsync(l=>l.Id==(template?p!.TrainingLevelId:e!.TrainingLevelId)))return Results.NotFound();
        var hash=Hash(new{id,r,template});var old=await db.TrainingContentArchives.AsNoTracking().SingleOrDefaultAsync(a=>a.ActorUserId==db.RequestUserId&&a.RequestId==r.RequestId);
        if(old!=null)return old.RequestHash==hash?Results.Ok(new{old.Id,replay=true}):Changed();
        if((template?p!.Version:e!.Version)!=r.Version||await db.TrainingContentArchives.AnyAsync(a=>template?a.TemplateId==id:a.ExerciseId==id))return Changed();
        var now=clock.GetUtcNow();var a=new TrainingContentArchive{Id=Guid.NewGuid(),AcademyId=t.AcademyId,ExerciseId=template?null:id,TemplateId=template?id:null,TrainingLevelId=template?p!.TrainingLevelId:e!.TrainingLevelId,SportId=template?p!.SportId:e!.SportId,ActorUserId=db.RequestUserId,ActorName=await db.Users.Where(u=>u.Id==db.RequestUserId).Select(u=>u.DisplayName).SingleAsync(),Note=string.IsNullOrWhiteSpace(r.Note)?null:r.Note.Trim(),RequestId=r.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};
        db.TrainingContentArchives.Add(a);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{a.Id});
    }
    public sealed class ArchiveConflict:IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext c,EndpointFilterDelegate next){try{return await next(c);}catch(DbUpdateException e)when(e is DbUpdateConcurrencyException||e.InnerException is Npgsql.PostgresException{SqlState:"23505" or "23514"}){return Changed();}}
    }
}
