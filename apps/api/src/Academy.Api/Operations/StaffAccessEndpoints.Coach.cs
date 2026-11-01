using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;
public static partial class StaffAccessEndpoints
{
    internal static readonly string[] CoachActionKeys=["workflow.attendance.players","workflow.evaluations.manage","workflow.media.manage","workflow.attendance.self"];
    private static async Task<IResult> GetCoachAccess(Guid id,CurrentTenant tenant,FoundationDbContext db,HttpResponse response)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.AcademyOwner)return Results.Forbid();
        var member=await db.AcademyMemberships.AsNoTracking().SingleOrDefaultAsync(m=>m.AcademyId==t.AcademyId&&m.Id==id&&m.Role==AcademyRole.Coach);if(member is null)return Results.NotFound();
        response.Headers.CacheControl="no-store";
        var grants=await db.StaffGrants.Where(g=>g.MembershipId==id&&g.IsActive&&CoachActionKeys.Contains(g.Permission)).ToDictionaryAsync(g=>g.Permission,g=>g.Allowed);
        return Results.Ok(new{revision=member.ContextRevision,actions=CoachActionKeys.Select(key=>new{key,label=StaffWorkflowAccess.Actions.Single(a=>a.Key==key).Label,allowed=grants.GetValueOrDefault(key,true)})});
    }
    private static async Task<IResult> SaveCoachAccess(Guid id,CoachAccessWrite body,CurrentTenant tenant,FoundationDbContext db,ClaimsPrincipal actor,TimeProvider clock)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.AcademyOwner)return Results.Forbid();
        if(body.Actions is null||body.Actions.Distinct().Count()!=body.Actions.Length||body.Actions.Any(k=>!CoachActionKeys.Contains(k)))return Results.UnprocessableEntity(new{message="اختر صلاحيات المدرب المتاحة."});
        await using var tx=await db.Database.BeginTransactionAsync();
        if(!await StaffRoleTemplateEndpoints.LockOwner(db,t,actor))return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{id}"},0))");
        var member=await db.AcademyMemberships.SingleOrDefaultAsync(m=>m.AcademyId==t.AcademyId&&m.Id==id&&m.Role==AcademyRole.Coach);if(member is null)return Results.NotFound();
        if(member.ContextRevision!=body.Revision)return Results.Conflict(new{message="تغيرت صلاحيات المدرب. حدّث الصفحة وراجع اختياراتك."});
        var rows=await db.StaffGrants.Where(g=>g.MembershipId==id&&CoachActionKeys.Contains(g.Permission)).ToListAsync();
        var before=JsonSerializer.Serialize(rows.Select(g=>new{g.Permission,g.Allowed,g.IsActive}));var now=clock.GetUtcNow();
        foreach(var key in CoachActionKeys){var row=rows.SingleOrDefault(g=>g.Permission==key);if(row is null){row=new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,MembershipId=id,Permission=key,CreatedAtUtc=now};db.StaffGrants.Add(row);}row.Allowed=body.Actions.Contains(key);row.IsActive=true;row.UpdatedAtUtc=now;}
        member.ContextRevision++;member.UpdatedAtUtc=now;
        db.StaffAccessAudits.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,MembershipId=id,ActorUserId=Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!),Before=before,After=JsonSerializer.Serialize(body),CreatedAtUtc=now,UpdatedAtUtc=now});
        await db.SaveChangesAsync();await tx.CommitAsync();return Results.NoContent();
    }
}
public sealed record CoachAccessWrite(int Revision,string[] Actions);
