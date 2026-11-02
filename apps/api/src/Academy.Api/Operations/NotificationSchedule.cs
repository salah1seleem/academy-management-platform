using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

// Bounded local database conditions. There is no external messaging integration.
public sealed class NotificationSchedule(IServiceScopeFactory scopes, TimeProvider clock, ILogger<NotificationSchedule> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Guid? after=null;
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));
        while(!stoppingToken.IsCancellationRequested){
            try{
                await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
                var academies=await db.Academies.AsNoTracking().Where(a=>a.IsActive&&(!after.HasValue||a.Id.CompareTo(after.Value)>0)).OrderBy(a=>a.Id).Take(10).Select(a=>a.Id).ToArrayAsync(stoppingToken);
                foreach(var academy in academies){
                    await using var unit=scopes.CreateAsyncScope();await ProcessAcademy(unit.ServiceProvider.GetRequiredService<FoundationDbContext>(),academy,clock.GetUtcNow(),stoppingToken);
                }
                after=academies.Length==10?academies[^1]:null;
            }catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception){logger.LogWarning("Notification condition batch unavailable; retrying the next bounded tick");}
            try{if(!await timer.WaitForNextTickAsync(stoppingToken))break;}catch(OperationCanceledException){break;}
        }
    }
    public static async Task<int> ProcessAcademy(FoundationDbContext db,Guid academy,DateTimeOffset now,CancellationToken token=default, bool expiringOnly=false, Guid? branchId=null, Guid? sportId=null)
    {
        if(db.RequestScopeEnabled&&db.RequestAcademyId!=academy)throw new InvalidOperationException("Different academy scope");
        await using var tx=await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"notice-schedule:{academy}"},0))",token);
        var payload=await db.AcademyConfigurationVersions.AsNoTracking().Where(v=>v.AcademyId==academy&&v.State==ConfigurationState.Active).Select(v=>v.Payload).SingleOrDefaultAsync(token);
        var zone=await db.Academies.Where(a=>a.Id==academy&&a.IsActive).Select(a=>a.TimeZone).SingleOrDefaultAsync(token);
        if(payload is null||zone is null||AcademyRuntimeSettings.Read(payload).Features.GetValueOrDefault("training")!=true||!await db.AcademyFeatureEntitlements.AnyAsync(e=>e.AcademyId==academy&&e.Feature=="training"&&e.IsActive&&e.Enabled,token))return 0;
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);
        var policies=await db.NoticeTemplates.AsNoTracking().Where(t=>t.AcademyId==academy&&(t.Key=="Expiring"||t.Key=="Expired")).ToDictionaryAsync(t=>t.Key,token);
        var count=0;
        foreach(var key in expiringOnly ? new[]{"Expiring"} : new[]{"Expiring","Expired"}){
            if(policies.TryGetValue(key,out var policy)&&!policy.IsActive)continue;
            var through=today.AddDays(expiringOnly ? 7 : policy?.TimingDays??7);
            var candidates=db.SubscriptionPeriods.AsNoTracking().Where(p=>p.AcademyId==academy&&(!branchId.HasValue||p.SportEnrollment.BranchId==branchId)&&(!sportId.HasValue||p.SportEnrollment.SportId==sportId)&&p.IsActive&&p.SportEnrollment.IsActive&&p.SportEnrollment.Player.IsActive&&(p.ServiceKindSnapshot??p.SubscriptionPlan.ServiceKind)==AcademyServiceKind.Training
                &&db.GuardianPlayerLinks.Any(l=>l.AcademyId==academy&&l.PlayerId==p.SportEnrollment.PlayerId&&l.IsActive&&l.Guardian.IsActive&&l.Guardian.User.IsActive)
                &&p.StartDate<=today&&p.Status!=SubscriptionPeriodStatus.Cancelled&&p.Status!=SubscriptionPeriodStatus.Frozen
                &&!db.SubscriptionPeriods.Any(n=>n.AcademyId==academy&&n.SportEnrollmentId==p.SportEnrollmentId&&n.IsActive&&n.Status!=SubscriptionPeriodStatus.Cancelled&&(n.StartDate>p.StartDate||n.StartDate==p.StartDate&&(n.CreatedAtUtc>p.CreatedAtUtc||n.CreatedAtUtc==p.CreatedAtUtc&&n.Id.CompareTo(p.Id)>0)))
                &&!db.AcademyNotices.Any(n=>n.AcademyId==academy&&n.EventKey==key&&n.TargetId==p.Id));
            candidates=key=="Expiring"
                ?candidates.Where(p=>p.EndDate>=today&&p.EndDate<=through&&((p.PlanTypeSnapshot??p.SubscriptionPlan.PlanType)==SubscriptionPlanType.Duration||p.RemainingSessions>0))
                :candidates.Where(p=>(p.EndDate<today||(p.PlanTypeSnapshot??p.SubscriptionPlan.PlanType)!=SubscriptionPlanType.Duration&&p.RemainingSessions<=0)
                    &&!db.SubscriptionPeriods.Any(n=>n.AcademyId==academy&&n.SportEnrollmentId==p.SportEnrollmentId&&n.Id!=p.Id&&n.IsActive&&n.Status!=SubscriptionPeriodStatus.Cancelled&&n.Status!=SubscriptionPeriodStatus.Frozen&&n.StartDate<=today&&(n.EndDate==null||n.EndDate>=today)&&((n.PlanTypeSnapshot??n.SubscriptionPlan.PlanType)==SubscriptionPlanType.Duration||n.RemainingSessions>0)));
            var periods=await candidates.OrderBy(p=>p.Id).Take(100).Select(p=>new{p.Id,p.SportEnrollmentId,p.EndDate}).ToArrayAsync(token);
            foreach(var period in periods){
                await GuardianLifecycleNotice.Add(db,academy,period.SportEnrollmentId,key,period.Id,"SubscriptionPeriod",period.Id,now,date:period.EndDate,token:token);count++;
            }
        }
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);return count;
    }
}
