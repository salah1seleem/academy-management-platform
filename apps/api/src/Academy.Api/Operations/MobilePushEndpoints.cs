using System.Security.Claims;
using System.Text.RegularExpressions;
using Academy.Api.Auth;
using Academy.Api.Mobile;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Operations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;
public sealed record PushDeviceWrite(string Provider,string Token);
public static class MobilePushEndpoints
{
    public static void MapMobilePush(this WebApplication app)
    {
        var g=app.MapGroup("/api/v1/mobile-push").RequireAuthorization(AcademyPermissions.TenantAccess);
        g.MapGet("/notices/{id:guid}",async(Guid id,CurrentTenant tenant,ClaimsPrincipal user,FoundationDbContext db,HttpResponse response,TimeProvider clock,Academy.Api.Configuration.AcademyConfigurationService config)=>{
            response.Headers.CacheControl="no-store";var t=(await tenant.ResolveAsync())!;var uid=Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var row=await NoticeEndpoints.OwnNotices(db,t.AcademyId,uid,t.Role).Where(n=>n.Id==id).Select(n=>new{n.Id,n.Message,n.MatchId,n.TargetKind,n.TargetId}).SingleOrDefaultAsync();
            if(row is null||row.TargetKind=="GroupAnnouncement"&&(!row.TargetId.HasValue||!await AudienceCommunicationEndpoints.GuardianCanRead(db,t.AcademyId,uid,row.TargetId.Value,clock,config)))return Results.NotFound();return Results.Ok(row);
        });
        g.MapGet("/status",(IConfiguration config,IHostEnvironment env,HttpResponse response)=>{response.Headers.CacheControl="no-store";return Results.Ok(new{inApp=true,remoteConfigured=!env.IsEnvironment("Demo")&&MobilePushProvider.Configured(config),demo=env.IsEnvironment("Demo")});});
        g.MapPut("/device",async(PushDeviceWrite body,ClaimsPrincipal user,FoundationDbContext db,IDataProtectionProvider protection,TimeProvider clock)=>{
            if(!Guid.TryParse(user.FindFirstValue("mobile_session_id"),out var session))return Results.Forbid();
            var uid=Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);var now=clock.GetUtcNow();
            if(body.Provider is not("APNs" or "FCM")||body.Token.Length is <32 or >2048||body.Provider=="APNs"&&!Regex.IsMatch(body.Token,"\\A[0-9a-fA-F]{32,256}\\z")||body.Provider=="FCM"&&!Regex.IsMatch(body.Token,"\\A[A-Za-z0-9_:.-]+\\z"))return Results.UnprocessableEntity(new{message="تعذر تسجيل جهاز الإشعارات. حاول مجددًا."});
            await using var tx=await db.Database.BeginTransactionAsync();
            var hash=MobileAuthenticationHandler.Hash(body.Token);await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"push-device:{hash}"},0))");
            if(!await db.MobileSessions.AnyAsync(s=>s.Id==session&&s.UserId==uid&&s.RevokedAtUtc==null&&s.ExpiresAtUtc>now))return Results.Forbid();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"push-session:{session}"},0))");
            await db.MobilePushDevices.Where(d=>d.MobileSessionId==session&&d.UserId==uid&&d.TokenHash!=hash).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.IsActive,false));
            var device=await db.MobilePushDevices.SingleOrDefaultAsync(d=>d.TokenHash==hash);
            if(device==null){device=new(){Id=Guid.NewGuid(),TokenHash=hash};db.MobilePushDevices.Add(device);}
            // Device token possession comes from the signed mobile session. Account
            // changes transfer this token and old queued jobs fail recipient checks.
            device.UserId=uid;device.MobileSessionId=session;device.Provider=body.Provider;device.IsActive=true;device.UpdatedAtUtc=now;device.ProtectedToken=protection.CreateProtector("iacademy.mobile-push.v1").Protect(body.Token);
            await db.SaveChangesAsync();await tx.CommitAsync();return Results.NoContent();
        }).AddEndpointFilter<CsrfFilter>();
        g.MapDelete("/device",async(ClaimsPrincipal user,FoundationDbContext db)=>{
            if(!Guid.TryParse(user.FindFirstValue("mobile_session_id"),out var sid))return Results.Forbid();var uid=Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await db.MobilePushDevices.Where(d=>d.UserId==uid&&d.MobileSessionId==sid).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.IsActive,false));return Results.NoContent();
        }).AddEndpointFilter<CsrfFilter>();
    }
    public static async Task Queue(FoundationDbContext db,AcademyNotice notice,DateTimeOffset now)
    {
        var devices=await db.MobilePushDevices.Where(d=>d.UserId==notice.UserId&&d.IsActive&&db.MobileSessions.Any(s=>s.Id==d.MobileSessionId&&s.UserId==d.UserId&&s.RevokedAtUtc==null&&s.ExpiresAtUtc>now)).Select(d=>d.Id).ToArrayAsync();
        foreach(var device in devices)db.MobilePushDeliveries.Add(new(){Id=Guid.NewGuid(),AcademyId=notice.AcademyId,NoticeId=notice.Id,DeviceId=device,NextAttemptAtUtc=now,CreatedAtUtc=now,UpdatedAtUtc=now});
    }
}
