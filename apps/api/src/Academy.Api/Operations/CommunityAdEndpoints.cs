using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Slice4;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public static class CommunityAdEndpoints
{
    public static void MapCommunityAds(this WebApplication app)
    {
        var owner=app.MapGroup("/api/v1/manage/community-campaigns").RequireAuthorization(AcademyPermissions.TenantAccess);
        owner.MapGet("/",List);owner.MapGet("/options",Options);owner.MapPost("/",Save).AddEndpointFilter<CsrfFilter>();owner.MapPost("/{id:guid}/review",Review).AddEndpointFilter<CsrfFilter>();
        var guardian=app.MapGroup("/api/v1/guardian/community-ads").RequireAuthorization(AcademyPermissions.GuardianChildrenRead);
        guardian.MapGet("/preference",Preference);guardian.MapPut("/preference",SetPreference).AddEndpointFilter<CsrfFilter>();guardian.MapGet("/feed",Feed);guardian.MapGet("/{id:guid}",Details);guardian.MapPost("/{id:guid}/events",Event).AddEndpointFilter<CsrfFilter>();
    }
    private static string Hash(object body)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body))));
    private static IResult Invalid(string message)=>Results.UnprocessableEntity(new{message});
    private static void Private(HttpContext h)=>h.Response.Headers.CacheControl="private, no-store";
    private static async Task<bool> Authority(FoundationDbContext db,CurrentTenant tenant,HttpContext h,bool write=false)
    {
        Private(h);var t=(await tenant.ResolveAsync())!;
        if(write)await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"},0))");
        return await CommunityAds.FullOwner(db,t);
    }
    private static async Task<IResult> Options(FoundationDbContext db,CurrentTenant tenant,HttpContext h,AcademyConfigurationService config,TimeProvider clock)
    {
        if(!await Authority(db,tenant,h))return Results.Forbid();var a=(await tenant.ResolveAsync())!.AcademyId;
        return Results.Ok(new{licensed=await config.Enabled(a,"community_ads"),tenantOptIn=(await config.Active(a))?.TenantMarketingOptIn==true,branches=await db.Branches.Where(b=>b.IsActive).OrderBy(b=>b.ArabicName).Select(b=>new{b.Id,name=b.ArabicName}).ToArrayAsync(),now=clock.GetUtcNow(),placement="GuardianHome",destination="InternalDetails",scope="FullAcademyOwner",measurement="ظهور نصف البطاقة لمدة ثانية متصلة. مرة واحدة للحساب في يوم الأكاديمية، وليست الأسرة أو مجرد طلب API. مجاميع أقل من خمسة حسابات تُحجب."});
    }
    private static async Task<IResult> List(FoundationDbContext db,CurrentTenant tenant,HttpContext h,TimeProvider clock)
    {
        if(!await Authority(db,tenant,h))return Results.Forbid();var a=(await tenant.ResolveAsync())!.AcademyId;
        var campaigns=await db.CommunityCampaigns.AsNoTracking().OrderByDescending(c=>c.UpdatedAtUtc).Take(100).ToArrayAsync();var ids=campaigns.Select(c=>c.Id).ToArray();
        var revisions=await db.CommunityCampaignRevisions.AsNoTracking().Where(r=>ids.Contains(r.CampaignId)).ToArrayAsync();var revisionIds=revisions.Select(r=>r.Id).ToArray();
        var targets=await db.CommunityCampaignTargets.Where(t=>revisionIds.Contains(t.RevisionId)).Select(t=>new{t.Id,t.RevisionId,t.BranchId,t.Decision,name=db.Branches.Where(b=>b.Id==t.BranchId).Select(b=>b.ArabicName).FirstOrDefault()}).ToArrayAsync();
        var events=await db.CommunityAdEvents.Where(e=>e.AcademyId==a&&revisionIds.Contains(e.RevisionId)).GroupBy(e=>new{e.RevisionId,e.Kind}).Select(g=>new{g.Key.RevisionId,g.Key.Kind,count=g.Count(),accounts=g.Select(e=>e.UserId).Distinct().Count()}).ToArrayAsync();
        var reviews=await db.CommunityCampaignReviews.Where(v=>revisionIds.Contains(v.RevisionId)).OrderByDescending(v=>v.CreatedAtUtc).Select(v=>new{v.Id,v.RevisionId,v.BranchId,v.Decision,v.Note,v.CreatedAtUtc,actorName=db.AcademyMemberships.Where(m=>m.Id==v.ActorMembershipId).Select(m=>m.User.DisplayName).FirstOrDefault()}).ToArrayAsync();
        return Results.Ok(new{limit=100,items=campaigns.Select(c=>new{c.Id,c.Version,c.InternalName,c.Advertiser,c.CurrentRevision,versions=revisions.Where(r=>r.CampaignId==c.Id).OrderByDescending(r=>r.Revision).Select(r=>new{r.Id,r.Version,r.Revision,r.Title,r.Body,r.ButtonText,r.ImageAlt,r.ImageAssetId,imageUrl=r.ImageAssetId.HasValue?$"/api/v1/assets/{r.ImageAssetId}":null,r.StartsAtUtc,r.EndsAtUtc,r.Priority,state=CommunityAds.State(r,clock.GetUtcNow()),storedState=r.State,targets=targets.Where(t=>t.RevisionId==r.Id).ToArray(),history=reviews.Where(v=>v.RevisionId==r.Id).ToArray(),metrics=events.Where(e=>e.RevisionId==r.Id).Select(e=>new{e.Kind,suppressed=e.accounts<5,count=e.accounts>=5?(int?)e.count:null,accounts=e.accounts>=5?(int?)e.accounts:null}).ToArray()}).ToArray()}).ToArray()});
    }
    private static async Task<IResult> Save(CommunityCampaignWrite body,FoundationDbContext db,CurrentTenant tenant,HttpContext h,AcademyConfigurationService config,TimeProvider clock)
    {
        await using var tx=await db.Database.BeginTransactionAsync();if(!await Authority(db,tenant,h,true))return Results.Forbid();var t=(await tenant.ResolveAsync())!;
        if(!await config.Enabled(t.AcademyId,"community_ads"))return Results.Forbid();
        if(body.RequestId==Guid.Empty||string.IsNullOrWhiteSpace(body.InternalName)||body.InternalName.Length>120||string.IsNullOrWhiteSpace(body.Advertiser)||body.Advertiser.Length>120||string.IsNullOrWhiteSpace(body.Title)||body.Title.Length>100||string.IsNullOrWhiteSpace(body.Body)||body.Body.Length>1000||string.IsNullOrWhiteSpace(body.ButtonText)||body.ButtonText.Length>40||body.ImageAlt?.Length>200||body.ImageAssetId.HasValue&&string.IsNullOrWhiteSpace(body.ImageAlt)||body.StartsAtUtc>=body.EndsAtUtc||body.Priority is <0 or >10||body.BranchIds is null||body.BranchIds.Length is <1 or >100||body.BranchIds.Distinct().Count()!=body.BranchIds.Length||body.DestinationUrl is not null)return Invalid("راجع بيانات المحتوى والفترة والفروع. الوجهة الحالية صفحة تفاصيل داخل التطبيق فقط، ولا يوجد دفع أو رابط خارجي في هذه الشريحة.");
        var hash=Hash(body);await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"community-save:{t.AcademyId}:{t.MembershipId}:{body.RequestId}"},0))");
        var prior=await db.CommunityCampaignRevisions.SingleOrDefaultAsync(r=>r.CreatedByMembershipId==t.MembershipId&&r.RequestId==body.RequestId);if(prior!=null)return prior.RequestHash==hash?Results.Ok(new{id=prior.CampaignId,revisionId=prior.Id,replay=true}):Results.Conflict(new{message="استُخدم مفتاح المحاولة لمحتوى آخر."});
        if(await db.Branches.CountAsync(b=>b.IsActive&&body.BranchIds.Contains(b.Id))!=body.BranchIds.Length)return Invalid("اختر فروعًا فعالة من الأكاديمية الحالية فقط.");
        if(body.ImageAssetId.HasValue&&!await db.PrivateAssets.AnyAsync(a=>a.Id==body.ImageAssetId&&a.IsActive&&a.Purpose=="Campaign"&&a.ContentType.StartsWith("image/")))return Invalid("ارفع تصميم إعلان خاصًا بهذه الأكاديمية، وليس صورة طفل أو إيصالًا.");
        CommunityCampaign c;var now=clock.GetUtcNow();
        if(body.CampaignId.HasValue){await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"community:{t.AcademyId}:{body.CampaignId}"},0))");var existing=await db.CommunityCampaigns.SingleOrDefaultAsync(c=>c.Id==body.CampaignId);if(existing==null)return Results.NotFound();if(existing.Version!=body.Version)return Results.Conflict(new{message="تغيرت الحملة؛ أعد تحميل أحدث نسخة."});c=existing;}
        else{if(body.Version!=0)return Invalid("نسخة الحملة الجديدة غير صحيحة.");c=new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,InternalName=body.InternalName.Trim(),Advertiser=body.Advertiser.Trim(),CreatedAtUtc=now};db.CommunityCampaigns.Add(c);}
        c.InternalName=body.InternalName.Trim();c.Advertiser=body.Advertiser.Trim();c.CurrentRevision++;c.UpdatedAtUtc=now;
        var r=new CommunityCampaignRevision{Id=Guid.NewGuid(),AcademyId=t.AcademyId,CampaignId=c.Id,Advertiser=body.Advertiser.Trim(),Revision=c.CurrentRevision,Title=body.Title.Trim(),Body=body.Body.Trim(),ButtonText=body.ButtonText.Trim(),ImageAssetId=body.ImageAssetId,ImageAlt=body.ImageAlt?.Trim()??"",StartsAtUtc=body.StartsAtUtc.ToUniversalTime(),EndsAtUtc=body.EndsAtUtc.ToUniversalTime(),Priority=body.Priority,State="Draft",CreatedByMembershipId=t.MembershipId,RequestId=body.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};db.CommunityCampaignRevisions.Add(r);
        foreach(var b in body.BranchIds)db.CommunityCampaignTargets.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,RevisionId=r.Id,BranchId=b,Decision="Pending",CreatedAtUtc=now,UpdatedAtUtc=now});
        // Selecting the new draft immediately supersedes the old creative's approval.
        db.CommunityCampaignReviews.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,RevisionId=r.Id,ActorMembershipId=t.MembershipId,Decision="Draft",RequestId=body.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now});
        await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created($"/api/v1/manage/community-campaigns/{c.Id}",new{id=c.Id,revisionId=r.Id});
    }
    private static async Task<IResult> Review(Guid id,CommunityCampaignReviewWrite body,FoundationDbContext db,CurrentTenant tenant,HttpContext h,AcademyConfigurationService config,TimeProvider clock)
    {
        await using var tx=await db.Database.BeginTransactionAsync();if(!await Authority(db,tenant,h,true))return Results.Forbid();var t=(await tenant.ResolveAsync())!;
        if(body.RequestId==Guid.Empty||body.Note?.Length>500||body.Action is not("Submit" or "Approve" or "Reject" or "Pause" or "Resume" or "Revoke"))return Invalid("اختر إجراء مراجعة صحيحًا وملاحظة لا تتجاوز 500 حرف.");
        var hash=Hash(new{id,body});await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"community-review:{t.AcademyId}:{t.MembershipId}:{body.RequestId}"},0))");
        var previous=await db.CommunityCampaignReviews.SingleOrDefaultAsync(v=>v.ActorMembershipId==t.MembershipId&&v.RequestId==body.RequestId);if(previous!=null)return previous.RevisionId==id&&previous.RequestHash==hash?Results.Ok(new{id,replay=true}):Results.Conflict(new{message="استُخدم مفتاح المحاولة لإجراء آخر."});
        var r=await db.CommunityCampaignRevisions.SingleOrDefaultAsync(r=>r.Id==id);if(r==null)return Results.NotFound();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"community:{t.AcademyId}:{r.CampaignId}"},0))");await db.Entry(r).ReloadAsync();
        if(r.Version!=body.Version||!await db.CommunityCampaigns.AnyAsync(c=>c.Id==r.CampaignId&&c.CurrentRevision==r.Revision))return Results.Conflict(new{message="تغيرت النسخة أو أصبحت تاريخية؛ أعد تحميل الحملة."});
        if(body.Action is not("Pause" or "Revoke")&&!await config.Enabled(t.AcademyId,"community_ads"))return Results.Forbid();
        if(body.Action is "Approve" or "Resume"&&!await CommunityAds.Enabled(config,t.AcademyId))return Invalid("اعتماد العرض يتطلب استحقاق الميزة وموافقة الأكاديمية من الإعدادات.");
        var targets=await db.CommunityCampaignTargets.Where(x=>x.RevisionId==id).ToArrayAsync();var target=targets.SingleOrDefault(x=>x.BranchId==body.BranchId);
        if(body.Action is "Approve" or "Reject" or "Revoke"){
            if(target==null)return Results.NotFound();if(r.State is not("PendingApproval" or "Approved" or "Paused"))return Results.Conflict(new{message="قدم المسودة للمراجعة أولًا."});
            if(!await db.Branches.AnyAsync(b=>b.Id==target.BranchId&&b.IsActive)&&body.Action=="Approve")return Invalid("الفرع متوقف؛ لا يمكن اعتماد عرضه.");
            target.Decision=body.Action=="Approve"?"Approved":body.Action=="Reject"?"Rejected":"Revoked";target.UpdatedAtUtc=clock.GetUtcNow();
            if(r.State!="Paused")r.State=targets.Any(t=>t.Decision=="Approved")?"Approved":targets.All(t=>t.Decision is "Rejected" or "Revoked")?"Rejected":"PendingApproval";
        }else{
            if(body.BranchId.HasValue)return Invalid("هذا الإجراء يخص النسخة لا فرعًا منفردًا.");
            if(body.Action=="Submit"&&r.State=="Draft")r.State="PendingApproval";
            else if(body.Action=="Pause"&&r.State=="Approved")r.State="Paused";
            else if(body.Action=="Resume"&&r.State=="Paused"&&targets.Any(x=>x.Decision=="Approved"))r.State="Approved";
            else return Results.Conflict(new{message="الإجراء غير متاح لحالة النسخة الحالية."});
        }
        var now=clock.GetUtcNow();r.UpdatedAtUtc=now;db.CommunityCampaignReviews.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,RevisionId=id,BranchId=body.BranchId,ActorMembershipId=t.MembershipId,Decision=body.Action,Note=body.Note?.Trim(),RequestId=body.RequestId,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now});
        try{await db.SaveChangesAsync();await tx.CommitAsync();}catch(DbUpdateConcurrencyException){return Results.Conflict(new{message="تغيرت النسخة؛ أعد تحميل الحملة."});}return Results.Ok(new{id});
    }
    private static async Task<IResult> Preference(CurrentTenant tenant,FoundationDbContext db,HttpContext h,AcademyConfigurationService config)
    {
        Private(h);var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.Guardian)return Results.Forbid();var user=CommunityAds.Actor(h.User);var p=await db.GuardianSponsorPreferences.SingleOrDefaultAsync(p=>p.UserId==user);
        return Results.Ok(new{available=await CommunityAds.Enabled(config,t.AcademyId),adultDeclared=p?.AdultDeclaredAtUtc!=null,inAppOptIn=p?.InAppOptIn==true,consentVersion=1,notice="محتوى برعاية داخل التطبيق فقط لحساب بالغ. يمكنك الإيقاف في أي وقت. لا يوافق هذا الخيار على SMS أو WhatsApp أو مشاركة بيانات أطفالك."});
    }
    private static async Task<IResult> SetPreference(CommunityAdPreferenceWrite body,CurrentTenant tenant,FoundationDbContext db,HttpContext h,AcademyConfigurationService config,TimeProvider clock)
    {
        Private(h);var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.Guardian)return Results.Forbid();if(body.InAppOptIn&&(!body.AdultDeclared||!await CommunityAds.Enabled(config,t.AcademyId)))return Invalid("الموافقة اختيارية لحساب بالغ فقط، وبعد إتاحة المحتوى من الأكاديمية.");
        var user=CommunityAds.Actor(h.User);await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"community-preference:{t.AcademyId}:{user}"},0))");var p=await db.GuardianSponsorPreferences.SingleOrDefaultAsync(p=>p.UserId==user);var now=clock.GetUtcNow();
        if(p==null){p=new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,UserId=user,CreatedAtUtc=now};db.GuardianSponsorPreferences.Add(p);}p.InAppOptIn=body.InAppOptIn;p.AdultDeclaredAtUtc=body.AdultDeclared?p.AdultDeclaredAtUtc??now:null;p.UpdatedAtUtc=now;
        await db.SaveChangesAsync();await tx.CommitAsync();return Results.NoContent();
    }
    private static async Task<IResult> Feed(CurrentTenant tenant,FoundationDbContext db,HttpContext h,AcademyConfigurationService config,TimeProvider clock,ISubscriptionClock subscriptions)
    {
        Private(h);var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.Guardian)return Results.Forbid();var user=CommunityAds.Actor(h.User);var today=await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock);
        if(!await CommunityAds.Enabled(config,t.AcademyId)||!await db.GuardianSponsorPreferences.AnyAsync(p=>p.UserId==user&&p.InAppOptIn&&p.AdultDeclaredAtUtc!=null)||await db.CommunityAdEvents.AnyAsync(e=>e.UserId==user&&e.ViewDate==today&&e.Kind=="Impression"))return Results.Ok(new{item=(object?)null});
        var branches=CommunityAds.EligibleBranches(db,t.AcademyId,user,subscriptions.Today);
        var r=await CommunityAds.Live(db,t.AcademyId,clock.GetUtcNow()).Where(r=>db.CommunityCampaignTargets.Any(t=>t.RevisionId==r.Id&&t.Decision=="Approved"&&branches.Contains(t.BranchId))&&!db.CommunityAdEvents.Any(e=>e.RevisionId==r.Id&&e.UserId==user&&e.ViewDate==today&&e.Kind=="Hide")).OrderByDescending(r=>r.Priority).ThenByDescending(r=>r.CreatedAtUtc).ThenBy(r=>r.Id).FirstOrDefaultAsync();
        if(r==null)return Results.Ok(new{item=(object?)null});var target=await CommunityAds.AuthorizedTarget(db,config,t,user,r.Id,clock,subscriptions.Today);return Results.Ok(new{item=target==null?null:await CommunityAds.Card(db,r,target.BranchId,subscriptions.Today)});
    }
    private static async Task<IResult> Details(Guid id,CurrentTenant tenant,FoundationDbContext db,HttpContext h,AcademyConfigurationService config,TimeProvider clock,ISubscriptionClock subscriptions)
    {
        Private(h);var t=(await tenant.ResolveAsync())!;var target=await CommunityAds.AuthorizedTarget(db,config,t,CommunityAds.Actor(h.User),id,clock,subscriptions.Today);if(target==null)return Results.NotFound();var r=await db.CommunityCampaignRevisions.AsNoTracking().SingleAsync(r=>r.Id==id);return Results.Ok(await CommunityAds.Card(db,r,target.BranchId,subscriptions.Today));
    }
    private static async Task<IResult> Event(Guid id,CommunityAdEventWrite body,CurrentTenant tenant,FoundationDbContext db,HttpContext h,AcademyConfigurationService config,TimeProvider clock,ISubscriptionClock subscriptions)
    {
        Private(h);var t=(await tenant.ResolveAsync())!;if(body.Kind is not("Impression" or "Click" or "Hide"))return Invalid("نوع تفاعل غير صحيح.");var user=CommunityAds.Actor(h.User);var today=await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock);
        await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"community-event:{t.AcademyId}:{user}:{today}"},0))");
        var target=await CommunityAds.AuthorizedTarget(db,config,t,user,id,clock,subscriptions.Today);if(target==null)return Results.NotFound();
        if(await db.CommunityAdEvents.AnyAsync(e=>e.RevisionId==id&&e.UserId==user&&e.ViewDate==today&&e.Kind==body.Kind))return Results.NoContent();
        if(body.Kind=="Impression"&&await db.CommunityAdEvents.AnyAsync(e=>e.UserId==user&&e.ViewDate==today&&e.Kind=="Impression"))return Results.Conflict(new{message="وصل الحساب إلى حد المحتوى برعاية اليوم."});
        var now=clock.GetUtcNow();db.CommunityAdEvents.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,RevisionId=id,BranchId=target.BranchId,UserId=user,ViewDate=today,Kind=body.Kind,CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();await tx.CommitAsync();return Results.NoContent();
    }
}
public sealed record CommunityCampaignWrite(string InternalName,string Advertiser,string Title,string Body,string ButtonText,string? ImageAlt,DateTimeOffset StartsAtUtc,DateTimeOffset EndsAtUtc,int Priority,Guid[] BranchIds,Guid RequestId,Guid? CampaignId=null,uint Version=0,Guid? ImageAssetId=null,string? DestinationUrl=null);
public sealed record CommunityCampaignReviewWrite(string Action,uint Version,Guid RequestId,Guid? BranchId=null,string? Note=null);
public sealed record CommunityAdPreferenceWrite(bool InAppOptIn,bool AdultDeclared);
public sealed record CommunityAdEventWrite(string Kind);
