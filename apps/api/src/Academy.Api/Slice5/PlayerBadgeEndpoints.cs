using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Slice5;
public sealed record BadgeRuleWrite(Guid SportId,string Title,string Criterion,int Points,bool ConfirmPublication,Guid RequestId);
public sealed record BadgeActionWrite(uint Version,string? Note,Guid RequestId);
public sealed record BadgeGrantWrite(Guid RuleId,uint EnrollmentVersion,DateOnly ObservedOn,string? FamilyNote,Guid RequestId);
public static class PlayerBadgeEndpoints
{
    private const string Root="/api/v1/evaluations/badges";
    public static void MapPlayerBadges(this WebApplication app)
    {
        var g=app.MapGroup(Root).RequireAuthorization(AcademyPermissions.EvaluationRead);
        g.MapGet("/rules",Rules);
        g.MapPost("/rules",CreateRule).RequireAuthorization(AcademyPermissions.EvaluationCriteriaManage).AddEndpointFilter<CsrfFilter>().AddEndpointFilter<BadgeConflict>();
        g.MapPost("/rules/{id:guid}/retire",Retire).RequireAuthorization(AcademyPermissions.EvaluationCriteriaManage).AddEndpointFilter<CsrfFilter>().AddEndpointFilter<BadgeConflict>();
        g.MapGet("/enrollments/{id:guid}",StaffRead);
        g.MapPost("/enrollments/{id:guid}/grant",Grant).RequireAuthorization(AcademyPermissions.EvaluationManage).AddEndpointFilter<CsrfFilter>().AddEndpointFilter<BadgeConflict>();
        g.MapPost("/grants/{id:guid}/revoke",Revoke).RequireAuthorization(AcademyPermissions.EvaluationManage).AddEndpointFilter<CsrfFilter>().AddEndpointFilter<BadgeConflict>();
        app.MapGet("/api/v1/guardian/children/{playerId:guid}/badges",FamilyRead).RequireAuthorization(AcademyPermissions.GuardianEvaluationRead);
    }
    private static bool Global(TenantMembership t,FoundationDbContext db)=>t.Role==AcademyRole.AcademyOwner||t.Role==AcademyRole.AcademyAdmin&&!db.BranchScopeRestricted;
    private static async Task<bool> Define(TenantMembership t,FoundationDbContext db,HttpContext h,bool add)=>Global(t,db)&&await PlayerGoalEndpoints.CanWrite(t,db,h,add,AcademyPermissions.EvaluationCriteriaManage);
    private static async Task<bool> DefineLock(TenantMembership t,FoundationDbContext db,HttpContext h,Guid request,bool add)=>Global(t,db)&&await PlayerGoalEndpoints.LockAuthority(t,db,h,null,request,add,AcademyPermissions.EvaluationCriteriaManage)&&await db.AcademyMemberships.AsNoTracking().AnyAsync(m=>m.Id==t.MembershipId&&m.Role==t.Role&&(m.Role==AcademyRole.AcademyOwner||m.AllBranches));
    private static Task RuleLock(FoundationDbContext db,Guid academy,Guid rule)=>db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"badge-rule:{academy}:{rule}"},0))");
    private static string Hash(object b)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(b))));
    private static IResult Changed()=>Results.Conflict(new{message="تغيرت الشارة أو القاعدة أو التسجيل؛ أعد فتح الصفحة قبل الحفظ."});
    private static bool Text(string? s,int n)=>!string.IsNullOrWhiteSpace(s)&&s.Trim().Length<=n;
    private static string? Note(string? s)=>string.IsNullOrWhiteSpace(s)?null:s.Trim();
    private static Task<string> Actor(FoundationDbContext db)=>db.Users.Where(u=>u.Id==db.RequestUserId).Select(u=>u.DisplayName).SingleAsync();
    private static object RuleRow(PlayerBadgeRule r,bool retired)=>new{r.Id,r.SportId,r.Title,r.Criterion,r.Points,r.Version,r.CreatedAtUtc,retired};
    private static IQueryable<PlayerBadgeRule> Available(FoundationDbContext db)=>db.PlayerBadgeRules.AsNoTracking().Where(r=>!db.PlayerBadgeRuleRetirements.Any(t=>t.RuleId==r.Id)&&db.Sports.Any(s=>s.Id==r.SportId&&s.IsActive));
    private static async Task<IResult> Rules(CurrentTenant tenant,FoundationDbContext db,HttpContext h)
    {
        h.Response.Headers.CacheControl="private, no-store";var t=(await tenant.ResolveAsync())!;var sports=db.Sports.AsNoTracking().Where(s=>s.IsActive);if(t.Role==AcademyRole.Coach)sports=sports.Where(s=>db.StaffGroupAssignments.Any(a=>a.AcademyMembershipId==t.MembershipId&&a.IsActive&&a.TrainingGroup.IsActive&&a.TrainingGroup.SportId==s.Id));
        var ids=sports.Select(s=>s.Id);var q=Global(t,db)?db.PlayerBadgeRules.AsNoTracking():Available(db);var rules=await q.Where(r=>ids.Contains(r.SportId)).OrderByDescending(r=>r.CreatedAtUtc).Take(101).ToArrayAsync();var keys=rules.Select(r=>r.Id).ToArray();var retired=await db.PlayerBadgeRuleRetirements.Where(r=>keys.Contains(r.RuleId)).Select(r=>r.RuleId).ToArrayAsync();
        return Results.Ok(new{sports=await sports.OrderBy(s=>s.ArabicName).Select(s=>new{s.Id,name=s.ArabicName}).ToArrayAsync(),items=rules.Take(100).Select(r=>RuleRow(r,retired.Contains(r.Id))),hasMore=rules.Length>100,canCreate=await Define(t,db,h,true),canRetire=await Define(t,db,h,false)});
    }
    private static async Task<IResult> CreateRule(BadgeRuleWrite b,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        if(!b.ConfirmPublication||b.RequestId==Guid.Empty||!Text(b.Title,100)||!Text(b.Criterion,500)||b.Points is <0 or >1000)return Results.BadRequest(new{message="راجع قاعدة التشجيع ونقاطها من 0 إلى 1000، ثم أكد نشرها. النقاط ليست أموالًا أو قوة لاعب."});var t=(await tenant.ResolveAsync())!;await using var tx=await db.Database.BeginTransactionAsync();if(!await DefineLock(t,db,h,b.RequestId,true))return Results.Forbid();if(!await db.Sports.AnyAsync(s=>s.Id==b.SportId&&s.IsActive))return Results.NotFound();
        var hash=Hash(b);var old=await db.PlayerBadgeRules.AsNoTracking().SingleOrDefaultAsync(r=>r.ActorUserId==db.RequestUserId&&r.RequestId==b.RequestId);if(old!=null)return old.RequestHash==hash?Results.Ok(new{old.Id,replay=true}):Changed();var now=clock.GetUtcNow();var r=new PlayerBadgeRule{Id=Guid.NewGuid(),AcademyId=t.AcademyId,SportId=b.SportId,Title=b.Title.Trim(),Criterion=b.Criterion.Trim(),Points=b.Points,ActorUserId=db.RequestUserId,ActorName=await Actor(db),RequestId=b.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};db.PlayerBadgeRules.Add(r);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created($"{Root}/rules",new{r.Id});
    }
    private static async Task<IResult> Retire(Guid id,BadgeActionWrite b,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        if(b.RequestId==Guid.Empty||b.Note?.Length>500)return Results.BadRequest(new{message="الملاحظة اختيارية حتى 500 حرف."});var t=(await tenant.ResolveAsync())!;await using var tx=await db.Database.BeginTransactionAsync();if(!await DefineLock(t,db,h,b.RequestId,false))return Results.Forbid();await RuleLock(db,t.AcademyId,id);var rule=await db.PlayerBadgeRules.AsNoTracking().SingleOrDefaultAsync(r=>r.Id==id);if(rule==null)return Results.NotFound();
        var hash=Hash(new{id,b});var old=await db.PlayerBadgeRuleRetirements.AsNoTracking().SingleOrDefaultAsync(r=>r.ActorUserId==db.RequestUserId&&r.RequestId==b.RequestId);if(old!=null)return old.RequestHash==hash?Results.Ok(new{old.Id,replay=true}):Changed();if(rule.Version!=b.Version||await db.PlayerBadgeRuleRetirements.AnyAsync(r=>r.RuleId==id))return Changed();var now=clock.GetUtcNow();var r=new PlayerBadgeRuleRetirement{Id=Guid.NewGuid(),AcademyId=t.AcademyId,RuleId=id,SportId=rule.SportId,Note=Note(b.Note),ActorUserId=db.RequestUserId,ActorName=await Actor(db),RequestId=b.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};db.PlayerBadgeRuleRetirements.Add(r);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{r.Id});
    }
    private static async Task<object> View(Guid id,TenantMembership t,FoundationDbContext db,HttpContext h,TimeProvider clock,bool family)
    {
        var e=(await PlayerGoalEndpoints.Enrollment(id,t,db,clock))!;var version=await db.SportEnrollments.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync();var grants=await db.PlayerBadgeGrants.AsNoTracking().Where(g=>g.SportEnrollmentId==id).OrderByDescending(g=>g.CreatedAtUtc).Take(101).ToArrayAsync();var ids=grants.Select(g=>g.RuleId).ToArray();var rules=await db.PlayerBadgeRules.AsNoTracking().Where(r=>ids.Contains(r.Id)).ToDictionaryAsync(r=>r.Id);var grantIds=grants.Select(g=>g.Id).ToArray();var revocations=await db.PlayerBadgeRevocations.AsNoTracking().Where(r=>grantIds.Contains(r.GrantId)).ToDictionaryAsync(r=>r.GrantId);
        var points=await(from g in db.PlayerBadgeGrants join r in db.PlayerBadgeRules on g.RuleId equals r.Id where g.SportEnrollmentId==id&&!db.PlayerBadgeRevocations.Any(v=>v.GrantId==g.Id) select(long)r.Points).SumAsync();var choices=family?[]:await Available(db).Where(r=>r.SportId==e.SportId&&!db.PlayerBadgeGrants.Any(g=>g.SportEnrollmentId==id&&g.RuleId==r.Id)).OrderBy(r=>r.Title).Take(100).ToArrayAsync();
        return new{enrollment=e,enrollmentVersion=version,asOfDate=await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock),points,canGrant=!family&&await PlayerGoalEndpoints.CanWrite(t,db,h,true),canRevoke=!family&&await PlayerGoalEndpoints.CanWrite(t,db,h,false),rules=choices.Select(r=>RuleRow(r,false)),hasMore=grants.Length>100,items=grants.Take(100).Select(g=>new{g.Id,g.Version,g.RuleId,g.ObservedOn,g.FamilyNote,g.ActorName,g.CreatedAtUtc,title=rules[g.RuleId].Title,criterion=rules[g.RuleId].Criterion,points=rules[g.RuleId].Points,state=revocations.ContainsKey(g.Id)?"Revoked":"Granted",revocation=revocations.TryGetValue(g.Id,out var v)?new{v.ActorName,v.FamilyNote,v.CreatedAtUtc}:null})};
    }
    private static async Task<IResult> StaffRead(Guid id,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock){h.Response.Headers.CacheControl="private, no-store";var t=(await tenant.ResolveAsync())!;if(await PlayerGoalEndpoints.Enrollment(id,t,db,clock)==null)return Results.NotFound();return Results.Ok(new{enrollments=new[]{await View(id,t,db,h,clock,false)},hasMore=false});}
    private static async Task<IResult> FamilyRead(Guid playerId,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        h.Response.Headers.CacheControl="private, no-store";var t=(await tenant.ResolveAsync())!;if(!await db.GuardianPlayerLinks.AnyAsync(l=>l.PlayerId==playerId&&l.IsActive&&l.Player.IsActive&&l.Guardian.IsActive&&l.Guardian.UserId==db.RequestUserId))return Results.NotFound();var ids=await db.SportEnrollments.AsNoTracking().Where(e=>e.PlayerId==playerId&&e.IsActive&&e.Sport.IsActive&&e.Branch.IsActive).OrderBy(e=>e.Id).Select(e=>e.Id).Take(21).ToArrayAsync();var data=new List<object>();foreach(var id in ids.Take(20))data.Add(await View(id,t,db,h,clock,true));return Results.Ok(new{enrollments=data,hasMore=ids.Length>20});
    }
    private static async Task<IResult> Grant(Guid id,BadgeGrantWrite b,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        if(b.RequestId==Guid.Empty||b.FamilyNote?.Length>500)return Results.BadRequest(new{message="ملاحظة الأسرة اختيارية حتى 500 حرف."});var t=(await tenant.ResolveAsync())!;await using var tx=await db.Database.BeginTransactionAsync();if(!await PlayerGoalEndpoints.LockAuthority(t,db,h,id,b.RequestId,true))return Results.Forbid();var e=await PlayerGoalEndpoints.Enrollment(id,t,db,clock);if(e==null)return Results.NotFound();await RuleLock(db,t.AcademyId,b.RuleId);
        var hash=Hash(new{id,b});var old=await db.PlayerBadgeGrants.AsNoTracking().SingleOrDefaultAsync(g=>g.ActorUserId==db.RequestUserId&&g.RequestId==b.RequestId);if(old!=null)return old.RequestHash==hash?Results.Ok(new{old.Id,replay=true}):Changed();var source=await db.SportEnrollments.AsNoTracking().Where(e=>e.Id==id).Select(e=>new{e.Version,dob=e.Player.DateOfBirth}).SingleAsync();if(source.Version!=b.EnrollmentVersion)return Changed();var today=await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock);if(b.ObservedOn>today||b.ObservedOn<source.dob)return Results.BadRequest(new{message="تاريخ الملاحظة من تاريخ ميلاد اللاعب حتى اليوم الفعلي، وليس المستقبل."});var rule=await Available(db).SingleOrDefaultAsync(r=>r.Id==b.RuleId&&r.SportId==e.SportId);if(rule==null)return Results.NotFound();if(await db.PlayerBadgeGrants.AnyAsync(g=>g.SportEnrollmentId==id&&g.RuleId==b.RuleId))return Changed();
        var now=clock.GetUtcNow();var g=new PlayerBadgeGrant{Id=Guid.NewGuid(),AcademyId=t.AcademyId,RuleId=rule.Id,SportEnrollmentId=id,SportId=e.SportId,ObservedOn=b.ObservedOn,FamilyNote=Note(b.FamilyNote),ActorUserId=db.RequestUserId,ActorName=await Actor(db),RequestId=b.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};db.PlayerBadgeGrants.Add(g);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created($"{Root}/enrollments/{id}",new{g.Id});
    }
    private static async Task<IResult> Revoke(Guid id,BadgeActionWrite b,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        if(b.RequestId==Guid.Empty||b.Note?.Length>500)return Results.BadRequest(new{message="ملاحظة السحب للأسرة اختيارية حتى 500 حرف."});var t=(await tenant.ResolveAsync())!;var grant=await db.PlayerBadgeGrants.AsNoTracking().SingleOrDefaultAsync(g=>g.Id==id);if(grant==null)return Results.NotFound();await using var tx=await db.Database.BeginTransactionAsync();if(!await PlayerGoalEndpoints.LockAuthority(t,db,h,grant.SportEnrollmentId,b.RequestId,false))return Results.Forbid();if(await PlayerGoalEndpoints.Enrollment(grant.SportEnrollmentId,t,db,clock)==null)return Results.NotFound();
        var hash=Hash(new{id,b});var old=await db.PlayerBadgeRevocations.AsNoTracking().SingleOrDefaultAsync(r=>r.ActorUserId==db.RequestUserId&&r.RequestId==b.RequestId);if(old!=null)return old.RequestHash==hash?Results.Ok(new{old.Id,replay=true}):Changed();if(grant.Version!=b.Version||await db.PlayerBadgeRevocations.AnyAsync(r=>r.GrantId==id))return Changed();var now=clock.GetUtcNow();var r=new PlayerBadgeRevocation{Id=Guid.NewGuid(),AcademyId=t.AcademyId,GrantId=id,SportEnrollmentId=grant.SportEnrollmentId,SportId=grant.SportId,FamilyNote=Note(b.Note),ActorUserId=db.RequestUserId,ActorName=await Actor(db),RequestId=b.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};db.PlayerBadgeRevocations.Add(r);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{r.Id});
    }
    public sealed class BadgeConflict:IEndpointFilter{public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext c,EndpointFilterDelegate next){try{return await next(c);}catch(DbUpdateException e)when(e is DbUpdateConcurrencyException||e.InnerException is Npgsql.PostgresException{SqlState:"23505" or "23514"}){return Changed();}}}
}
