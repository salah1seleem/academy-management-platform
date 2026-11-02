using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Slice5;
public sealed record CertificateIssueWrite(uint SourceVersion,Guid RequestId);
public sealed record CertificateWithdrawWrite(uint Version,string? FamilyNote,Guid RequestId);
public static class PlayerCertificateEndpoints
{
    private const string Root="/api/v1/evaluations/certificates";
    public static void MapPlayerCertificates(this WebApplication app)
    {
        var g=app.MapGroup(Root).RequireAuthorization(AcademyPermissions.EvaluationRead);
        g.MapGet("/sources/{id:guid}",Source);
        g.MapPost("/sources/{id:guid}/issue",Issue).RequireAuthorization(AcademyPermissions.EvaluationManage).AddEndpointFilter<CsrfFilter>().AddEndpointFilter<CertificateConflict>();
        g.MapGet("/{id:guid}",StaffRead);
        g.MapPost("/{id:guid}/withdraw",Withdraw).RequireAuthorization(AcademyPermissions.EvaluationManage).AddEndpointFilter<CsrfFilter>().AddEndpointFilter<CertificateConflict>();
        app.MapGet("/api/v1/guardian/children/{playerId:guid}/certificates",FamilyList).RequireAuthorization(AcademyPermissions.GuardianEvaluationRead);
        app.MapGet("/api/v1/guardian/children/{playerId:guid}/certificates/{id:guid}",FamilyRead).RequireAuthorization(AcademyPermissions.GuardianEvaluationRead);
    }
    private static string Hash(object o)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o))));
    private static IResult Changed()=>Results.Conflict(new{message="تغيرت الشهادة أو المصدر؛ أعد فتحه قبل المحاولة."});
    private static async Task<bool> CanManage(TenantMembership t,FoundationDbContext db,HttpContext h,bool add)=>t.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin&&await PlayerGoalEndpoints.CanWrite(t,db,h,add);
    private static Task<bool> Linked(Guid player,FoundationDbContext db)=>db.GuardianPlayerLinks.AnyAsync(l=>l.PlayerId==player&&l.IsActive&&l.Guardian.IsActive&&l.Guardian.UserId==db.RequestUserId&&l.Player.IsActive);
    private static async Task<object> CertificateView(PlayerAchievementCertificate c,FoundationDbContext db)
    {
        var w=await db.PlayerCertificateWithdrawals.AsNoTracking().SingleOrDefaultAsync(w=>w.CertificateId==c.Id);
        return new{c.Id,c.SourceLevelReviewId,c.SportEnrollmentId,c.AcademyName,c.PlayerName,c.SportName,c.LevelName,c.MilestoneOn,c.IssuedOn,c.ActorName,c.CreatedAtUtc,c.Version,number=c.Id.ToString("N")[..12].ToUpperInvariant(),state=w==null?"Issued":"Withdrawn",withdrawal=w==null?null:new{w.ActorName,w.FamilyNote,w.CreatedAtUtc}};
    }
    private static async Task<IResult> Source(Guid id,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        h.Response.Headers.CacheControl="private, no-store";var t=(await tenant.ResolveAsync())!;var r=await db.PlayerLevelReviews.AsNoTracking().SingleOrDefaultAsync(r=>r.Id==id);if(r==null||await PlayerGoalEndpoints.Enrollment(r.SportEnrollmentId,t,db,clock) is not {} e)return Results.NotFound();
        var eligible=r.TechnicalDecisionId!=null&&r.GroupTransferId!=null&&r.ToLevelId!=null&&r.Action=="Changed";
        var cert=await db.PlayerAchievementCertificates.AsNoTracking().SingleOrDefaultAsync(c=>c.SourceLevelReviewId==id);
        var academy=await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>a.ArabicName).SingleAsync();
        return Results.Ok(new{source=new{r.Id,r.Version,r.ToLevelName,r.CreatedAtUtc,player=e.Player,sport=e.Sport,academy},eligible,canIssue=eligible&&cert==null&&await CanManage(t,db,h,true),canWithdraw=cert!=null&&!await db.PlayerCertificateWithdrawals.AnyAsync(w=>w.CertificateId==cert.Id)&&await CanManage(t,db,h,false),certificate=cert==null?null:await CertificateView(cert,db)});
    }
    private static async Task<IResult> Issue(Guid id,CertificateIssueWrite b,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        if(b.RequestId==Guid.Empty)return Results.BadRequest(new{message="أعد فتح مصدر الإنجاز قبل الإصدار."});var t=(await tenant.ResolveAsync())!;if(!await CanManage(t,db,h,true))return Results.Forbid();
        var r=await db.PlayerLevelReviews.AsNoTracking().SingleOrDefaultAsync(r=>r.Id==id);if(r==null)return Results.NotFound();await using var tx=await db.Database.BeginTransactionAsync();
        if(!await PlayerGoalEndpoints.LockAuthority(t,db,h,r.SportEnrollmentId,b.RequestId,true))return Results.Forbid();var e=await PlayerGoalEndpoints.Enrollment(r.SportEnrollmentId,t,db,clock);if(e==null)return Results.NotFound();
        var hash=Hash(new{id,b});var prior=await db.PlayerAchievementCertificates.AsNoTracking().SingleOrDefaultAsync(c=>c.ActorUserId==db.RequestUserId&&c.RequestId==b.RequestId);if(prior!=null)return prior.RequestHash==hash?Results.Ok(new{prior.Id,replay=true}):Changed();
        if(r.Version!=b.SourceVersion||r.TechnicalDecisionId==null||r.GroupTransferId==null||r.ToLevelId==null||r.Action!="Changed"||await db.PlayerAchievementCertificates.AnyAsync(c=>c.SourceLevelReviewId==id))return Changed();
        var a=await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>new{a.ArabicName,a.TimeZone}).SingleAsync();var zone=TimeZoneInfo.FindSystemTimeZoneById(a.TimeZone);var now=clock.GetUtcNow();
        var c=new PlayerAchievementCertificate{Id=Guid.NewGuid(),AcademyId=t.AcademyId,SourceLevelReviewId=id,SportEnrollmentId=r.SportEnrollmentId,SportId=r.SportId,AcademyName=a.ArabicName,PlayerName=e.Player,SportName=e.Sport,LevelName=r.ToLevelName!,MilestoneAtUtc=r.CreatedAtUtc,MilestoneOn=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.CreatedAtUtc,zone).DateTime),IssuedOn=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,zone).DateTime),ActorUserId=db.RequestUserId,ActorName=await db.Users.Where(u=>u.Id==db.RequestUserId).Select(u=>u.DisplayName).SingleAsync(),RequestId=b.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};db.PlayerAchievementCertificates.Add(c);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created($"{Root}/{c.Id}",new{c.Id});
    }
    private static async Task<IResult> StaffRead(Guid id,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        h.Response.Headers.CacheControl="private, no-store";var t=(await tenant.ResolveAsync())!;var c=await db.PlayerAchievementCertificates.AsNoTracking().SingleOrDefaultAsync(c=>c.Id==id);if(c==null||await PlayerGoalEndpoints.Enrollment(c.SportEnrollmentId,t,db,clock)==null)return Results.NotFound();return Results.Ok(new{certificate=await CertificateView(c,db),canWithdraw=!await db.PlayerCertificateWithdrawals.AnyAsync(w=>w.CertificateId==id)&&await CanManage(t,db,h,false)});
    }
    private static async Task<IResult> FamilyList(Guid playerId,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        h.Response.Headers.CacheControl="private, no-store";if(!await Linked(playerId,db))return Results.NotFound();var t=(await tenant.ResolveAsync())!;
        var rows=await db.PlayerAchievementCertificates.AsNoTracking().Where(c=>db.SportEnrollments.Any(e=>e.Id==c.SportEnrollmentId&&e.PlayerId==playerId&&e.IsActive&&e.Sport.IsActive&&e.Branch.IsActive)).OrderByDescending(c=>c.CreatedAtUtc).Take(51).ToArrayAsync();
        var items=new List<object>();foreach(var c in rows.Take(50))items.Add(await CertificateView(c,db));return Results.Ok(new{items,hasMore=rows.Length>50});
    }
    private static async Task<IResult> FamilyRead(Guid playerId,Guid id,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        h.Response.Headers.CacheControl="private, no-store";if(!await Linked(playerId,db))return Results.NotFound();var t=(await tenant.ResolveAsync())!;var c=await db.PlayerAchievementCertificates.AsNoTracking().SingleOrDefaultAsync(c=>c.Id==id&&db.SportEnrollments.Any(e=>e.Id==c.SportEnrollmentId&&e.PlayerId==playerId));if(c==null||await PlayerGoalEndpoints.Enrollment(c.SportEnrollmentId,t,db,clock)==null)return Results.NotFound();return Results.Ok(new{certificate=await CertificateView(c,db),canWithdraw=false});
    }
    private static async Task<IResult> Withdraw(Guid id,CertificateWithdrawWrite b,CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        if(b.RequestId==Guid.Empty||b.FamilyNote?.Length>500)return Results.BadRequest(new{message="الملاحظة اختيارية حتى500حرف؛ أعد فتح الشهادة قبل السحب."});var t=(await tenant.ResolveAsync())!;if(!await CanManage(t,db,h,false))return Results.Forbid();var c=await db.PlayerAchievementCertificates.AsNoTracking().SingleOrDefaultAsync(c=>c.Id==id);if(c==null)return Results.NotFound();await using var tx=await db.Database.BeginTransactionAsync();if(!await PlayerGoalEndpoints.LockAuthority(t,db,h,c.SportEnrollmentId,b.RequestId,false))return Results.Forbid();if(await PlayerGoalEndpoints.Enrollment(c.SportEnrollmentId,t,db,clock)==null)return Results.NotFound();
        var hash=Hash(new{id,b});var old=await db.PlayerCertificateWithdrawals.AsNoTracking().SingleOrDefaultAsync(w=>w.ActorUserId==db.RequestUserId&&w.RequestId==b.RequestId);if(old!=null)return old.RequestHash==hash?Results.Ok(new{old.Id,replay=true}):Changed();
        if(c.Version!=b.Version||await db.PlayerCertificateWithdrawals.AnyAsync(w=>w.CertificateId==id))return Changed();var now=clock.GetUtcNow();var w=new PlayerCertificateWithdrawal{Id=Guid.NewGuid(),AcademyId=t.AcademyId,CertificateId=id,SportEnrollmentId=c.SportEnrollmentId,SportId=c.SportId,FamilyNote=string.IsNullOrWhiteSpace(b.FamilyNote)?null:b.FamilyNote.Trim(),ActorUserId=db.RequestUserId,ActorName=await db.Users.Where(u=>u.Id==db.RequestUserId).Select(u=>u.DisplayName).SingleAsync(),RequestId=b.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};db.PlayerCertificateWithdrawals.Add(w);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{w.Id});
    }
    public sealed class CertificateConflict:IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext c,EndpointFilterDelegate next){try{return await next(c);}catch(DbUpdateException e)when(e is DbUpdateConcurrencyException||e.InnerException is Npgsql.PostgresException{SqlState:"23505" or "23514"}){return Changed();}}
    }
}
