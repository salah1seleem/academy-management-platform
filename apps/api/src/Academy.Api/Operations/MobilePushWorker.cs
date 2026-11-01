using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;
public sealed class MobilePushWorker(IServiceScopeFactory scopes,IHostEnvironment environment,IConfiguration config,ILogger<MobilePushWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(15));
        while(!token.IsCancellationRequested){
            try{if(environment.IsProduction()&&MobilePushProvider.Configured(config)){await using var scope=scopes.CreateAsyncScope();await ProcessOne(scope.ServiceProvider,token);}}
            catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
            catch(Exception){logger.LogWarning("Mobile push batch unavailable; inbox remains authoritative");}
            try{if(!await timer.WaitForNextTickAsync(token))break;}catch(OperationCanceledException){break;}
        }
    }
    public static async Task ProcessOne(IServiceProvider services,CancellationToken token,Guid? deliveryId=null)
    {
        var db=services.GetRequiredService<FoundationDbContext>();var now=services.GetRequiredService<TimeProvider>().GetUtcNow();
        await using var tx=await db.Database.BeginTransactionAsync(token);
        // One short bounded HTTP attempt owns the DB row. A second process cannot
        // send it concurrently; an ambiguous network retry remains best effort.
        var rows=await db.MobilePushDeliveries.FromSqlInterpolated($"SELECT *, xmin FROM \"MobilePushDeliverys\" WHERE \"State\"='Pending' AND \"IsActive\" AND \"NextAttemptAtUtc\"<={now} AND ({deliveryId}::uuid IS NULL OR \"Id\"={deliveryId}::uuid) ORDER BY \"NextAttemptAtUtc\" LIMIT 1 FOR UPDATE SKIP LOCKED").ToListAsync(token);
        if(rows.Count==0)return;var delivery=rows[0];var notice=await db.AcademyNotices.SingleAsync(n=>n.Id==delivery.NoticeId&&n.AcademyId==delivery.AcademyId,token);
        var device=await db.MobilePushDevices.SingleAsync(d=>d.Id==delivery.DeviceId,token);
        var valid=notice.IsActive&&notice.EventKey is ("MatchGoals" or "GroupAnnouncement")&&device.IsActive&&device.UserId==notice.UserId
            &&await db.MobileSessions.AnyAsync(s=>s.Id==device.MobileSessionId&&s.UserId==notice.UserId&&s.RevokedAtUtc==null&&s.ExpiresAtUtc>now&&s.User.IsActive&&s.User.SecurityStamp==s.SecurityStamp,token)
            &&await db.Academies.AnyAsync(a=>a.Id==notice.AcademyId&&a.IsActive,token)
            &&await db.AcademyMemberships.AnyAsync(m=>m.AcademyId==notice.AcademyId&&m.UserId==notice.UserId&&m.IsActive&&m.Role==AcademyRole.Guardian,token)
;
        if(valid&&notice.EventKey=="MatchGoals")valid=notice.SubjectPlayerId.HasValue
            &&await db.AcademyFeatureEntitlements.AnyAsync(e=>e.AcademyId==notice.AcademyId&&e.Feature=="matches"&&e.Enabled&&e.IsActive,token)
            &&await db.GuardianPlayerLinks.AnyAsync(l=>l.AcademyId==notice.AcademyId&&l.PlayerId==notice.SubjectPlayerId&&l.Guardian.UserId==notice.UserId&&l.IsActive&&l.Guardian.IsActive&&l.Player.IsActive,token)
            &&await db.MatchParticipants.AnyAsync(p=>p.AcademyId==notice.AcademyId&&p.MatchId==notice.MatchId&&p.PlayerId==notice.SubjectPlayerId&&p.IsActive,token)
            &&await db.MatchGoalSlots.AnyAsync(g=>g.MatchId==notice.MatchId&&g.AcademyId==notice.AcademyId&&g.PlayerId==notice.SubjectPlayerId&&g.Kind=="Player"&&g.IsActive,token)
            &&await db.AcademyMatches.AnyAsync(m=>m.Id==notice.MatchId&&m.AcademyId==notice.AcademyId&&m.ResultFinalized&&m.IsActive,token)
            &&!await db.NoticeTemplates.AnyAsync(p=>p.AcademyId==notice.AcademyId&&p.Key=="Match"&&!p.IsActive,token);
        if(valid&&notice.EventKey=="GroupAnnouncement")valid=notice.TargetId.HasValue&&await AudienceCommunicationEndpoints.GuardianCanRead(db,notice.AcademyId,notice.UserId,notice.TargetId.Value,services.GetRequiredService<TimeProvider>(),services.GetRequiredService<Academy.Api.Configuration.AcademyConfigurationService>());
        if(!valid)delivery.State="Suppressed";
        else {
            var payload=await db.AcademyConfigurationVersions.Where(v=>v.AcademyId==notice.AcademyId&&v.State==ConfigurationState.Active).Select(v=>v.Payload).SingleOrDefaultAsync(token);
            if(notice.EventKey=="MatchGoals"&&(payload==null||!AcademyRuntimeSettings.Read(payload).Features.GetValueOrDefault("matches")))delivery.State="Suppressed";
            else {
                var pushToken=services.GetRequiredService<IDataProtectionProvider>().CreateProtector("iacademy.mobile-push.v1").Unprotect(device.ProtectedToken);
                string state;
                try { state=await services.GetRequiredService<MobilePushProvider>().Send(device.Provider,pushToken,delivery.Id,notice.Id,notice.AcademyId,token); }
                catch(HttpRequestException){state="Retry";}
                catch(OperationCanceledException)when(!token.IsCancellationRequested){state="Retry";}
                if(state=="Unconfigured")return;
                delivery.Attempts++;if(state=="Accepted"){delivery.State="Accepted";delivery.AcceptedAtUtc=now;}else if(state=="Rejected"){delivery.State="Rejected";device.IsActive=false;}else if(delivery.Attempts>=5)delivery.State="Failed";
                delivery.NextAttemptAtUtc=now.AddMinutes(Math.Pow(2,delivery.Attempts));
            }
        }
        delivery.UpdatedAtUtc=now;await db.SaveChangesAsync(token);await tx.CommitAsync(token);
    }
}
