using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Platform;
public static class PlatformStaffAccess
{
    public static void MapPlatformStaffAccess(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1/platform/academies/{academy:guid}/users").RequireAuthorization(PlatformIdentity.Policy);
        api.MapGet("/{id:guid}/access",async(Guid academy,Guid id,FoundationDbContext db,ClaimsPrincipal actor,HttpResponse response)=>{
            if(!await db.Academies.AnyAsync(a=>a.Id==academy))return Results.NotFound();
            db.BindRequestScope(academy,Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!),Guid.Empty,null);
            return await StaffAccessEndpoints.ReadForAcademy(id,academy,db,response);
        });
        api.MapPut("/{id:guid}/access",async(Guid academy,Guid id,StaffAccessWrite body,FoundationDbContext db,ClaimsPrincipal actor,TimeProvider clock)=>{
            if(!await db.Academies.AnyAsync(a=>a.Id==academy))return Results.NotFound();
            var user=Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            db.BindRequestScope(academy,user,Guid.Empty,null);
            async Task<bool> LiveAuthority(){
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"platform-grants:"+user},0))");
                var stamp=actor.FindFirstValue("platform_stamp");
                return await db.PlatformAdministrators.AnyAsync(p=>p.UserId==user&&p.IsActive&&db.Users.Any(u=>u.Id==user&&u.IsActive&&u.SecurityStamp==stamp));
            }
            return await StaffAccessEndpoints.SaveForAcademy(id,body,academy,db,actor,clock,LiveAuthority,true);
        }).AddEndpointFilter<CsrfFilter>();
    }
}
