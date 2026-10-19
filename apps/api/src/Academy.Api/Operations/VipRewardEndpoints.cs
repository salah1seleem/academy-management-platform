using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Finance;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;

public sealed record VipRewardWrite(Guid RequestId,Guid[] GuardianIds,decimal Percentage,string Message,DateOnly ExpiresOn,string? QuoteHash=null);
public static partial class VipRewards
{
    public static void MapVipRewards(this WebApplication app)
    {
        var owner=app.MapGroup("/api/v1/manage/vip-rewards").RequireAuthorization(AcademyPermissions.OwnerDashboardRead,AcademyPermissions.SubscriptionDiscountManage);
        owner.MapPost("/preview",Preview).AddEndpointFilter<CsrfFilter>();owner.MapPost("/",Issue).AddEndpointFilter<CsrfFilter>();
        owner.MapGet("/",ListOwner);owner.MapPost("/{id:guid}/cancel",Cancel).AddEndpointFilter<CsrfFilter>();
        owner.MapGet("/options",async(CurrentTenant tenant,FoundationDbContext db,ISubscriptionClock clock,HttpContext http)=>{try{var t=await Owner(tenant,db,http);var currency=await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>a.DefaultCurrency).SingleAsync();var items=await MemberSubscriptionValues.Guardians(db,t.AcademyId,clock.Today,currency).Where(g=>g.UnpricedSubscriptions==0&&g.MonthlySubscriptionValue>0).OrderByDescending(g=>g.MonthlySubscriptionValue).ThenBy(g=>g.DisplayName).ThenBy(g=>g.Id).Take(10).Select(g=>new RewardRecipient(g.Id,g.DisplayName,g.MonthlySubscriptionValue)).ToArrayAsync();return Results.Ok(new{items,currency,today=clock.Today,conditions=Conditions});}catch(UnauthorizedAccessException){return Results.Forbid();}});
        app.MapGet("/api/v1/guardian/vip-rewards",ListGuardian).RequireAuthorization(AcademyPermissions.GuardianChildrenRead);
    }
    private static string InputHash(VipRewardWrite b)=>StatementComparisonRules.Hash(new{ids=b.GuardianIds.Order().ToArray(),b.Percentage,message=b.Message.Trim(),b.ExpiresOn});
    private static async Task<TenantMembership> Owner(CurrentTenant tenant,FoundationDbContext db,HttpContext http,bool write=false)
    {
        var t=await tenant.ResolveAsync()??throw new VipRewardConflict("سجل الدخول أولًا.");http.Response.Headers.CacheControl="private, no-store";
        if(t.Role!=AcademyRole.AcademyOwner)throw new UnauthorizedAccessException();
        if(write){await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"},0))");await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"vip-issue:{t.AcademyId}"},0))");}
        if(!await db.AcademyMemberships.AnyAsync(m=>m.Id==t.MembershipId&&m.Role==AcademyRole.AcademyOwner&&m.IsActive&&m.User.IsActive&&m.Academy.IsActive))throw new UnauthorizedAccessException();return t;
    }
    private static bool Valid(VipRewardWrite b,DateOnly day)=>b.RequestId!=Guid.Empty&&b.GuardianIds is {Length:>0 and <=10}&&b.GuardianIds.All(i=>i!=Guid.Empty)&&b.GuardianIds.Distinct().Count()==b.GuardianIds.Length&&b.Percentage>0&&b.Percentage<100&&Math.Round(b.Percentage,2)==b.Percentage&&!string.IsNullOrWhiteSpace(b.Message)&&b.Message.Length<=500&&b.ExpiresOn>=day&&b.ExpiresOn.DayNumber-day.DayNumber<=366;
    private sealed record RewardRecipient(Guid Id,string Name,decimal MonthlyValue);
    private static async Task<(RewardRecipient[] Recipients,string Currency,string Hash)> Prepare(VipRewardWrite b,Guid academy,FoundationDbContext db,ISubscriptionClock clock)
    {
        if(!Valid(b,clock.Today))throw new VipRewardConflict("اختر من1إلى10أولياء، ونسبة أكبر من0وأقل من100، ورسالة حتى500حرف وصلاحية حتى سنة من تاريخ الاشتراك المعتمد.");
        var currency=await db.Academies.Where(a=>a.Id==academy).Select(a=>a.DefaultCurrency).SingleAsync();
        var top=await MemberSubscriptionValues.Guardians(db,academy,clock.Today,currency).Where(g=>g.UnpricedSubscriptions==0&&g.MonthlySubscriptionValue>0).OrderByDescending(g=>g.MonthlySubscriptionValue).ThenBy(g=>g.DisplayName).ThenBy(g=>g.Id).Take(10).Select(g=>new RewardRecipient(g.Id,g.DisplayName,g.MonthlySubscriptionValue)).ToArrayAsync();
        var recipients=top.Where(g=>b.GuardianIds.Contains(g.Id)).OrderBy(g=>g.Id).ToArray();
        if(recipients.Length!=b.GuardianIds.Length)throw new VipRewardConflict("تغيرت قائمة الأعضاء المميزين؛ حدّث القائمة واختر المستحقين اليوم.");
        var blocked=BlockingUses(db,academy);
        if(await db.VipRewards.AnyAsync(r=>r.AcademyId==academy&&b.GuardianIds.Contains(r.GuardianId)&&r.CancelledAtUtc==null&&!db.VipRewardUses.Any(u=>u.RewardId==r.Id&&u.UsedAtUtc!=null)&&(r.ExpiresOn>=clock.Today||blocked.Any(u=>u.RewardId==r.Id))))throw new VipRewardConflict("لأحد المختارين مكافأة غير مستخدمة أو محجوزة بالفعل. راجع سجل المكافآت.");
        var hash=StatementComparisonRules.Hash(new{input=InputHash(b),day=clock.Today,currency,recipients});return(recipients,currency,hash);
    }
    private static async Task<IResult> Preview(VipRewardWrite b,CurrentTenant tenant,FoundationDbContext db,ISubscriptionClock clock,HttpContext http,AcademyConfigurationService config)
    {
        try{var t=await Owner(tenant,db,http);if(!await config.Enabled(t.AcademyId,"training"))return Results.Forbid();var p=await Prepare(b,t.AcademyId,db,clock);return Results.Ok(new{quoteHash=p.Hash,recipients=p.Recipients,percentage=b.Percentage,message=b.Message.Trim(),b.ExpiresOn,currency=p.Currency,asOf=clock.Today,conditions=Conditions,delivery="إشعار داخلي محفوظ فقط، دون رسالة أو دفع خارجي."});}catch(UnauthorizedAccessException){return Results.Forbid();}
    }
    private static async Task<IResult> Issue(VipRewardWrite b,CurrentTenant tenant,FoundationDbContext db,ISubscriptionClock clock,HttpContext http,AcademyConfigurationService config)
    {
        try{await using var tx=await db.Database.BeginTransactionAsync();var t=await Owner(tenant,db,http,true);if(!Valid(b,clock.Today))throw new VipRewardConflict("راجع بيانات المكافأة وصلاحيتها.");
            var hash=InputHash(b);var prior=await db.VipRewards.Where(r=>r.IssuedByMembershipId==t.MembershipId&&r.RequestId==b.RequestId).ToArrayAsync();if(prior.Length>0)return prior.Length==b.GuardianIds.Length&&prior.All(r=>r.RequestHash==hash)?Results.Ok(new{ids=prior.Select(r=>r.Id),replay=true}):Results.Conflict(new{message="معرف المحاولة مستخدم لرسالة أو نسبة أو مستلمين مختلفين."});
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:"+t.AcademyId},0))");config.DiscardRequestCache(t.AcademyId);if(!await config.Enabled(t.AcademyId,"training"))return Results.Forbid();var p=await Prepare(b,t.AcademyId,db,clock);if(b.QuoteHash!=p.Hash)throw new VipRewardConflict("تغيرت المعاينة. راجع النسبة والمستلمين قبل الإصدار.");
            var ids=new List<Guid>();foreach(var recipient in p.Recipients){var id=Guid.NewGuid();var rule=new DiscountRule{Id=Guid.NewGuid(),AcademyId=t.AcademyId,ArabicName="مكافأة الأعضاء المميزين "+id.ToString("N")[..12],Percentage=b.Percentage,Type=RenewalDiscountType.Percentage,Currency=p.Currency,StartsOn=clock.Today,EndsOn=b.ExpiresOn,Revision=1,Reason=$"مكافأة الأعضاء المميزين بنسبة{b.Percentage}% — باقة واحدة دون جمع عروض",CreatedAtUtc=clock.UtcNow,UpdatedAtUtc=clock.UtcNow};
                var user=await db.GuardianProfiles.Where(g=>g.Id==recipient.Id).Select(g=>g.UserId).SingleAsync();var notice=new AcademyNotice{Id=Guid.NewGuid(),AcademyId=t.AcademyId,UserId=user,Message=b.Message.Trim()+"\n"+$"مكافأة بنسبة{b.Percentage}% حتى{b.ExpiresOn:yyyy-MM-dd}. "+Conditions,CreatedAtUtc=clock.UtcNow,UpdatedAtUtc=clock.UtcNow};
                db.DiscountRules.Add(rule);db.DiscountRuleVersions.Add(DiscountRuleVersion.Create(rule,Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!),clock.UtcNow));db.AcademyNotices.Add(notice);db.VipRewards.Add(new(){Id=id,AcademyId=t.AcademyId,GuardianId=recipient.Id,DiscountRuleId=rule.Id,NoticeId=notice.Id,IssuedByMembershipId=t.MembershipId,RequestId=b.RequestId,RequestHash=hash,Message=b.Message.Trim(),IssuedOn=clock.Today,ExpiresOn=b.ExpiresOn,Percentage=b.Percentage,Currency=p.Currency,CreatedAtUtc=clock.UtcNow,UpdatedAtUtc=clock.UtcNow});ids.Add(id);}
            await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created("/api/v1/manage/vip-rewards",new{ids,delivery="StoredInApp"});
        }catch(UnauthorizedAccessException){return Results.Forbid();}
    }
    private static async Task<IResult> ListOwner(CurrentTenant tenant,FoundationDbContext db,ISubscriptionClock clock,HttpContext http,int? page)
    {try{var t=await Owner(tenant,db,http);return await Read(db,t.AcademyId,null,clock,page??1);}catch(UnauthorizedAccessException){return Results.Forbid();}}
    private static async Task<IResult> ListGuardian(CurrentTenant tenant,FoundationDbContext db,ISubscriptionClock clock,HttpContext http,int? page)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.Guardian)return Results.Forbid();http.Response.Headers.CacheControl="private, no-store";var user=Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);var guardian=await db.GuardianProfiles.Where(g=>g.AcademyId==t.AcademyId&&g.UserId==user&&g.IsActive).Select(g=>(Guid?)g.Id).SingleOrDefaultAsync();return guardian.HasValue?await Read(db,t.AcademyId,guardian,clock,page??1):Results.NotFound();
    }
    private static async Task<IResult> Read(FoundationDbContext db,Guid academy,Guid? guardian,ISubscriptionClock clock,int page)
    {
        if(page is <1 or >100000)return Results.UnprocessableEntity(new{message="رقم الصفحة غير صالح."});var q=db.VipRewards.AsNoTracking().Where(r=>r.AcademyId==academy&&(!guardian.HasValue||r.GuardianId==guardian));var rows=await q.OrderByDescending(r=>r.CreatedAtUtc).ThenBy(r=>r.Id).Skip((page-1)*25).Take(25).ToArrayAsync();var ids=rows.Select(r=>r.Id).ToArray();var uses=await db.VipRewardUses.AsNoTracking().Where(u=>ids.Contains(u.RewardId)).OrderByDescending(u=>u.CreatedAtUtc).ToArrayAsync();var notices=await db.AcademyNotices.Where(n=>rows.Select(r=>r.NoticeId).Contains(n.Id)).ToDictionaryAsync(n=>n.Id,n=>n.IsRead);
        var names=await db.GuardianProfiles.Where(g=>rows.Select(r=>r.GuardianId).Contains(g.Id)).ToDictionaryAsync(g=>g.Id,g=>g.DisplayName);var blocked=(await BlockingUses(db,academy).Where(u=>ids.Contains(u.RewardId)).Select(u=>u.Id).ToArrayAsync()).ToHashSet();
        return Results.Ok(new{count=await q.CountAsync(),page,pageSize=25,asOf=clock.Today,conditions=Conditions,items=rows.Select(r=>new{r.Id,r.GuardianId,guardian=names.GetValueOrDefault(r.GuardianId),r.Percentage,r.Currency,r.Message,r.IssuedOn,r.ExpiresOn,r.CancelledAtUtc,state=r.CancelledAtUtc!=null?"Cancelled":uses.Any(u=>u.RewardId==r.Id&&u.UsedAtUtc!=null)?"Used":uses.Any(u=>u.RewardId==r.Id&&blocked.Contains(u.Id))?"Reserved":r.ExpiresOn<clock.Today?"Expired":"Available",delivery="StoredInApp",read=notices.GetValueOrDefault(r.NoticeId),history=uses.Where(u=>u.RewardId==r.Id).Select(u=>new{sourceKind=guardian.HasValue?null:u.SourceKind,sourceId=guardian.HasValue?(Guid?)null:u.SourceId,u.CreatedAtUtc,u.UsedAtUtc,u.ReleasedAtUtc})})});
    }
    private static async Task<IResult> Cancel(Guid id,CurrentTenant tenant,FoundationDbContext db,ISubscriptionClock clock,HttpContext http)
    {
        try{await using var tx=await db.Database.BeginTransactionAsync();var t=await Owner(tenant,db,http,true);await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"vip-reward:{t.AcademyId}:{id}"},0))");var r=await db.VipRewards.SingleOrDefaultAsync(r=>r.Id==id);if(r is null)return Results.NotFound();if(r.CancelledAtUtc!=null)return Results.Ok(new{r.Id,replay=true});if(await BlockingUses(db,t.AcademyId).AnyAsync(u=>u.RewardId==id))throw new VipRewardConflict("المكافأة مستخدمة أو محجوزة؛ احسم الطلب غير المدفوع أولًا.");
            foreach(var u in await db.VipRewardUses.Where(u=>u.RewardId==id&&u.ReleasedAtUtc==null).ToArrayAsync()){u.ReleasedAtUtc=clock.UtcNow;u.UpdatedAtUtc=clock.UtcNow;}
            await db.SaveChangesAsync();r.CancelledAtUtc=clock.UtcNow;r.CancelledByMembershipId=t.MembershipId;r.UpdatedAtUtc=clock.UtcNow;await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{r.Id});}catch(UnauthorizedAccessException){return Results.Forbid();}
    }
}
