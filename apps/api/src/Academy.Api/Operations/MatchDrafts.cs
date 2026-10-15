using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;

public static class MatchDrafts
{
    internal sealed record RosterItem(Guid EnrollmentId,Guid PlayerId,string Name);
    internal static string Hash(object v)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(v))));
    internal static Task<List<RosterItem>> Roster(FoundationDbContext db,MatchTeam team)=>db.MatchTeamPlayers.Where(p=>p.TeamId==team.Id&&p.IsActive)
        .Join(db.SportEnrollments.Where(e=>e.IsActive&&e.Player.IsActive&&e.SportId==team.SportId),p=>p.EnrollmentId,e=>e.Id,(p,e)=>e).OrderBy(e=>e.Id).Select(e=>new RosterItem(e.Id,e.PlayerId,e.Player.ArabicName)).ToListAsync();
    internal static string RosterHash(MatchTeam team,List<RosterItem> roster)=>Hash(new{team.Id,team.SportId,team.BranchId,team.CoachMembershipId,team.IsActive,roster});
    internal static Task TeamLock(FoundationDbContext db,Guid academy,Guid team)=>db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"match-team:{academy}:{team}"},0))");
    internal static async Task<IResult> RosterPreview(Guid id,CurrentTenant current,FoundationDbContext db,HttpResponse response)
    {
        response.Headers.CacheControl="no-store";var t=(await current.ResolveAsync())!;var team=await db.MatchTeams.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.AcademyId==t.AcademyId&&x.IsActive);if(team is null)return Results.NotFound();var roster=await Roster(db,team);return Results.Ok(new{teamId=team.Id,teamName=team.Name,items=roster,quoteHash=RosterHash(team,roster)});
    }
    internal static async Task<IResult> Preview(Guid id,CurrentTenant current,FoundationDbContext db,HttpResponse response)
    {
        response.Headers.CacheControl="no-store";var t=(await current.ResolveAsync())!;await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var s=await db.AcademyMatches.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.AcademyId==t.AcademyId);if(s is null)return Results.NotFound();var team=await db.MatchTeams.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==s.TeamId&&x.IsActive);if(team is null||!await db.AcademyTournaments.AnyAsync(x=>x.Id==s.TournamentId&&x.IsActive))return Results.Problem(statusCode:409,title:"الفريق أو البطولة غير فعال؛ راجع الإعداد قبل النسخ.");
        var roster=await Roster(db,team);return Results.Ok(new{s.Id,s.Opponent,s.Venue,s.Notes,teamName=team.Name,roster,quoteHash=Quote(s,team,roster)});
    }
    private static string Quote(AcademyMatch source,MatchTeam team,List<RosterItem> roster)=>Hash(new{source.Id,source.UpdatedAtUtc,source.TeamId,source.TournamentId,source.Opponent,source.Venue,source.Notes,rosterHash=RosterHash(team,roster)});
    internal static async Task<IResult> Copy(Guid id,MatchCopyWrite body,HttpContext http,CurrentTenant current,FoundationDbContext db,ISubscriptionClock clock)
    {
        var key=http.Request.Headers["Idempotency-Key"].ToString();if(key.Length is <8 or >100||body.QuoteHash?.Length!=64||body.MatchDate.Year is <2000 or >2100)return Results.Problem(statusCode:422,title:"اختر موعد النسخة وراجع قائمة الفريق قبل الحفظ.");
        var t=(await current.ResolveAsync())!;await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"},0))");
        if(!await db.AcademyMemberships.AnyAsync(m=>m.Id==t.MembershipId&&m.Role==t.Role&&m.IsActive&&m.User.IsActive)||!(await http.RequestServices.GetRequiredService<IAuthorizationService>().AuthorizeAsync(http.User,http,AcademyPermissions.StructureManage)).Succeeded)return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"match-copy:{t.AcademyId}:{t.MembershipId}:{key}"},0))");
        var source=await db.AcademyMatches.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.AcademyId==t.AcademyId);if(source is null)return Results.NotFound();
        var requestHash=Hash(new{id,body});var replay=await db.AcademyMatches.AsNoTracking().SingleOrDefaultAsync(x=>x.CopiedByMembershipId==t.MembershipId&&x.CopyRequestKey==key);if(replay is not null)return replay.CopyRequestHash==requestHash?Results.Ok(new{replay.Id,replay=true}):Results.Conflict();
        var lockedTeam=source.TeamId;await TeamLock(db,t.AcademyId,lockedTeam);await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"match:{t.AcademyId}:{id}"},0))");
        source=await db.AcademyMatches.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id);if(source is null)return Results.NotFound();if(source.TeamId!=lockedTeam)return Results.Conflict();var team=await db.MatchTeams.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==source.TeamId&&x.IsActive);
        if(team is null||!await db.AcademyTournaments.AnyAsync(x=>x.Id==source.TournamentId&&x.IsActive))return Results.Conflict();var roster=await Roster(db,team);
        if(t.Role!=AcademyRole.AcademyOwner&&!await db.AcademyMemberships.AnyAsync(m=>m.Id==t.MembershipId&&m.IsActive&&(m.AllBranches||db.MembershipBranchAccesses.Any(b=>b.MembershipId==m.Id&&b.IsActive&&b.BranchId==team.BranchId))&&(m.AllSports||db.MembershipSportAccesses.Any(s=>s.MembershipId==m.Id&&s.IsActive&&s.SportId==team.SportId))))return Results.Forbid();
        if(Quote(source,team,roster)!=body.QuoteHash)return Results.Problem(statusCode:409,title:"تغيرت المباراة أو قائمة الفريق؛ حدّث المعاينة قبل النسخ.");
        var row=new AcademyMatch{Id=Guid.NewGuid(),AcademyId=t.AcademyId,TournamentId=source.TournamentId,TeamId=source.TeamId,Opponent=source.Opponent,Venue=source.Venue,Notes=source.Notes,MatchDate=body.MatchDate,StartTime=body.StartTime,IsActive=true,CopiedFromMatchId=source.Id,CopiedByMembershipId=t.MembershipId,CopyRequestKey=key,CopyRequestHash=requestHash,CreatedAtUtc=clock.UtcNow,UpdatedAtUtc=clock.UtcNow};
        db.AcademyMatches.Add(row);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created($"/api/v1/manage/matches/{row.Id}",new{row.Id});
    }
}
public sealed record MatchCopyWrite([property:JsonRequired] DateOnly MatchDate,[property:JsonRequired] TimeOnly StartTime,string QuoteHash);
