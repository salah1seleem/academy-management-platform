using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Academy.Api.Auth;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public sealed record MediaErasureWrite(Guid RequestId,string? Note);
public sealed class PrivateMediaLifecycle(FoundationDbContext db,IConfiguration config,IWebHostEnvironment environment,TimeProvider clock)
{
    public bool WorkerEnabled => config.GetValue("Media:LifecycleWorkerEnabled",false);
    private DateTimeOffset Now => clock.GetUtcNow();
    public long StorageLimit => Math.Clamp(config.GetValue<long?>("Media:StorageLimitBytes")??1_073_741_824,1,107_374_182_400);
    public long DownloadLimit => Math.Clamp(config.GetValue<long?>("Media:DailyDownloadLimitBytes")??536_870_912,1,10_737_418_240);
    public static string Hash(Guid asset,string? note)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{asset,note}))));
    public static async Task<bool> FullOwner(FoundationDbContext db,TenantMembership t)=>t.Role==AcademyRole.AcademyOwner&&!t.AdditionalContext&&await db.AcademyMemberships.AsNoTracking().AnyAsync(m=>m.Id==t.MembershipId&&m.UserId==db.RequestUserId&&m.IsActive&&m.User.IsActive&&m.Academy.IsActive&&m.Role==AcademyRole.AcademyOwner&&m.PrimaryMembershipId==null&&m.AllBranches&&m.AllSports);
    public static Task StorageLock(FoundationDbContext db,Guid academy)=>db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"media-storage:{academy}"},0))");
    // Aggregate only, explicitly tenant-bound. A branch-filtered DbSet must never decide a tenant quota.
    public async Task<long> StoredBytes(Guid academy)=>await db.Database.SqlQuery<long>($"SELECT coalesce(sum(a.\"Length\"),0)::bigint AS \"Value\" FROM \"PrivateAssets\" a WHERE a.\"AcademyId\"={academy} AND a.\"Purpose\"='Media' AND NOT EXISTS(SELECT 1 FROM \"PrivateMediaPurges\" p WHERE p.\"AcademyId\"=a.\"AcademyId\" AND p.\"AssetId\"=a.\"Id\")").SingleAsync();
    public async Task<bool> Admit(PrivateAsset asset)
    {
        if(asset.Purpose!="Media")return true;
        var active=await db.AcademyConfigurationVersions.AsNoTracking().SingleOrDefaultAsync(v=>v.AcademyId==asset.AcademyId&&v.State==ConfigurationState.Active);
        if(active==null)return false;
        var settings=AcademyRuntimeSettings.Read(active.Payload);
        if(!settings.Features.GetValueOrDefault("media")||!await db.AcademyFeatureEntitlements.AnyAsync(e=>e.AcademyId==asset.AcademyId&&e.Feature=="media"&&e.IsActive&&e.Enabled))return false;
        if(settings.MediaRetention is {} policy)
        {
            if(policy.Days is <1 or >3650)return false;
            asset.RetainUntilUtc=clock.GetUtcNow().AddDays(policy.Days);asset.DeleteOnExpiry=policy.DeleteOnExpiry;
        }
        await StorageLock(db,asset.AcademyId);
        var used=await StoredBytes(asset.AcademyId);
        return asset.Length>0&&asset.Length<=StorageLimit&&used<=StorageLimit-asset.Length;
    }
    // Reserve the whole asset for every byte/range request, not a claim of measured network traffic.
    // Atomic upsert across all sessions/devices/branches; the authenticated entry redirect is free.
    public async Task<bool> ReserveDownload(Guid academy,Guid user,long length)
    {
        var now=clock.GetUtcNow();var day=DateOnly.FromDateTime(now.UtcDateTime);var id=Guid.NewGuid();var limit=DownloadLimit;
        var rows=await db.Database.SqlQuery<int>($"INSERT INTO \"PrivateMediaDownloadBudgets\" (\"Id\",\"AcademyId\",\"UserId\",\"UtcDay\",\"ReservedBytes\",\"IsActive\",\"CreatedAtUtc\",\"UpdatedAtUtc\") SELECT {id},{academy},{user},{day},{length},true,{now},{now} WHERE {length}>0 AND {length}<={limit} ON CONFLICT (\"AcademyId\",\"UserId\",\"UtcDay\") DO UPDATE SET \"ReservedBytes\"=\"PrivateMediaDownloadBudgets\".\"ReservedBytes\"+{length},\"UpdatedAtUtc\"={now} WHERE \"PrivateMediaDownloadBudgets\".\"ReservedBytes\"<={limit}-{length} RETURNING 1 AS \"Value\"").ToArrayAsync();
        return rows.Length==1;
    }
    public async Task<bool> Purge(Guid academy,Guid assetId)
    {
        await using var tx=await db.Database.BeginTransactionAsync();await GroupMediaPrivacy.Lock(db,academy);await StorageLock(db,academy);
        if(await db.PrivateMediaPurges.AnyAsync(p=>p.AcademyId==academy&&p.AssetId==assetId))return true;
        var erasure=await db.PrivateMediaErasures.AsNoTracking().SingleOrDefaultAsync(e=>e.AcademyId==academy&&e.AssetId==assetId);
        var asset=await db.PrivateAssets.AsNoTracking().SingleOrDefaultAsync(a=>a.AcademyId==academy&&a.Id==assetId&&a.Purpose=="Media");
        if(erasure==null||asset==null||!Regex.IsMatch(asset.StorageName,"^[a-fA-F0-9]{32}$"))return false;
        // Never remove a shared legacy filename, including a reference in another academy.
        if(await db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM \"PrivateAssets\" WHERE \"StorageName\"={asset.StorageName} AND \"Id\"<>{assetId}").SingleAsync()!=0)return false;
        var path=Path.Combine(AssetEndpoints.Root(config,environment),asset.StorageName);
        try
        {
            if(Directory.Exists(path))return false; // Only the exact asset file, never a recursive directory operation.
            File.Delete(path); // Missing after an interrupted prior attempt is safe to complete.
        }
        catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
        var now=clock.GetUtcNow();db.PrivateMediaPurges.Add(new(){Id=Guid.NewGuid(),AcademyId=academy,AssetId=assetId,ErasureId=erasure.Id,ReleasedBytes=asset.Length,CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();await tx.CommitAsync();return true;
    }
    public async Task RunBatch(CancellationToken token)
    {
        // Only this new, non-financial reservation aggregate; never assets/audiences/audit history.
        var cutoff=DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-32);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PrivateMediaDownloadBudgets\" WHERE \"UtcDay\"<{cutoff}",token);
        var now=clock.GetUtcNow();var due=await db.PrivateAssets.AsNoTracking().Where(a=>a.Purpose=="Media"&&a.DeleteOnExpiry&&a.RetainUntilUtc!=null&&a.RetainUntilUtc<=now&&!db.PrivateMediaErasures.Any(e=>e.AssetId==a.Id)).OrderBy(a=>a.RetainUntilUtc).Take(25).Select(a=>new{a.Id,a.AcademyId}).ToArrayAsync(token);
        foreach(var a in due)
        {
            token.ThrowIfCancellationRequested();await using var tx=await db.Database.BeginTransactionAsync(token);await GroupMediaPrivacy.Lock(db,a.AcademyId);
            if(!await db.PrivateMediaErasures.AnyAsync(e=>e.AcademyId==a.AcademyId&&e.AssetId==a.Id,token)&&await db.PrivateAssets.AnyAsync(p=>p.AcademyId==a.AcademyId&&p.Id==a.Id&&p.Purpose=="Media"&&p.DeleteOnExpiry&&p.RetainUntilUtc<=now,token))
                db.PrivateMediaErasures.Add(new(){Id=Guid.NewGuid(),AcademyId=a.AcademyId,AssetId=a.Id,Reason="Retention",RequestId=Guid.NewGuid(),RequestHash=Hash(a.Id,null),CreatedAtUtc=now,UpdatedAtUtc=now});
            await db.SaveChangesAsync(token);await tx.CommitAsync(token);
        }
        var queued=await db.PrivateMediaErasures.AsNoTracking().Where(e=>!db.PrivateMediaPurges.Any(p=>p.AssetId==e.AssetId&&p.AcademyId==e.AcademyId)).OrderBy(e=>e.CreatedAtUtc).Take(25).Select(e=>new{e.AcademyId,e.AssetId}).ToArrayAsync(token);
        foreach(var item in queued){token.ThrowIfCancellationRequested();await Purge(item.AcademyId,item.AssetId);}
    }
    public static void Map(WebApplication app)
    {
        var g=app.MapGroup("/api/v1/assets").RequireAuthorization(AcademyPermissions.TenantAccess);
        g.MapGet("/storage-policy",Policy);
        g.MapPost("/{id:guid}/delete",Delete).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Policy(CurrentTenant tenant,FoundationDbContext db,PrivateMediaLifecycle lifecycle,HttpContext h)
    {
        h.Response.Headers.CacheControl="private, no-store";var t=(await tenant.ResolveAsync())!;
        var active=await db.AcademyConfigurationVersions.AsNoTracking().SingleOrDefaultAsync(v=>v.AcademyId==t.AcademyId&&v.State==ConfigurationState.Active);
        var retention=active==null?null:AcademyRuntimeSettings.Read(active.Payload).MediaRetention;
        long? used=await FullOwner(db,t)?await lifecycle.StoredBytes(t.AcademyId):null;
        return Results.Ok(new{retention,storageLimitBytes=lifecycle.StorageLimit,storedBytes=used,downloadLimitBytes=lifecycle.DownloadLimit,downloadDay="UTC",workerEnabled=lifecycle.WorkerEnabled,notice="الاحتفاظ يطبق على الرفع الجديد فقط. انتهاء المدة يحجب الوصول؛ إزالة الملف الآلية تحتاج اختيار المالك وتشغيل عامل التنظيف. الحذف لا يسترجع تنزيلًا سابقًا أو يحذف النسخ الاحتياطية فورًا. حصة التخزين تشمل المسحوب والمنتهي والمنتظر حذفه حتى تأكيد إزالة الملف. طلب تنزيل أو جزء فيديو يحجز حجم الملف كاملًا من حدك اليومي بتوقيت UTC."});
    }
    public static async Task<bool> CanDelete(TenantMembership t,HttpContext h,FoundationDbContext db,IAuthorizationService auth)=>
        t.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin&&!t.AdditionalContext&&
        (await auth.AuthorizeAsync(h.User,h,AcademyPermissions.PlayerMediaRead)).Succeeded&&
        (await auth.AuthorizeAsync(h.User,h,AcademyPermissions.PlayerMediaManage)).Succeeded&&
        (t.Role==AcademyRole.AcademyOwner||await db.AcademyMemberships.AnyAsync(m=>m.Id==t.MembershipId&&!m.UseActionGrants)||await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.Module=="media"&&g.IsActive&&g.Read&&g.Delete));
    private static async Task<IResult> Delete(Guid id,MediaErasureWrite b,CurrentTenant tenant,FoundationDbContext db,PrivateMediaLifecycle lifecycle,IAuthorizationService auth,HttpContext h)
    {
        var t=(await tenant.ResolveAsync())!;h.Response.Headers.CacheControl="private, no-store";
        if(b.RequestId==Guid.Empty||b.Note?.Length>500)return Results.BadRequest(new{message="أعد فتح الحذف؛ الملاحظة اختيارية حتى500 حرف."});
        if(!await CanDelete(t,h,db,auth))return Results.Forbid();
        var note=string.IsNullOrWhiteSpace(b.Note)?null:b.Note.Trim();var hash=Hash(id,note);
        await using(var tx=await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"},0))");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AcademyMemberships\" WHERE \"Id\"={t.MembershipId} AND \"AcademyId\"={t.AcademyId} FOR UPDATE");
            var live=await db.AcademyMemberships.AsNoTracking().SingleOrDefaultAsync(m=>m.Id==t.MembershipId&&m.UserId==db.RequestUserId&&m.IsActive&&m.User.IsActive&&m.Academy.IsActive&&(!m.PrimaryMembershipId.HasValue||db.AcademyMemberships.Any(p=>p.Id==m.PrimaryMembershipId&&p.IsActive&&p.User.IsActive)));
            if(live==null||live.Role!=t.Role||!live.AllSports)return Results.Forbid();
            if(t.Role==AcademyRole.AcademyAdmin&&!live.AllBranches)
            {
                var branches=await db.MembershipBranchAccesses.Where(a=>a.MembershipId==t.MembershipId&&a.IsActive&&db.Branches.Any(v=>v.Id==a.BranchId&&v.IsActive)).Select(a=>a.BranchId).ToArrayAsync();if(!db.BranchScopeRestricted||db.RequestBranchIds.Any(v=>!branches.Contains(v)))return Results.Forbid();
            }
            if(!await CanDelete(t,h,db,auth))return Results.Forbid();await GroupMediaPrivacy.Lock(db,t.AcademyId);
            var asset=await db.PrivateAssets.SingleOrDefaultAsync(a=>a.Id==id&&a.Purpose=="Media");if(asset==null)return Results.NotFound();
            var prior=await db.PrivateMediaErasures.SingleOrDefaultAsync(e=>e.ActorUserId==db.RequestUserId&&e.RequestId==b.RequestId);
            if(prior!=null){if(prior.RequestHash!=hash)return Conflict();}
            else
            {
                if(await db.PrivateMediaErasures.AnyAsync(e=>e.AssetId==id))return Conflict();
                var now=lifecycle.Now;db.PrivateMediaErasures.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,AssetId=id,ActorUserId=db.RequestUserId,Reason="Manual",RequestId=b.RequestId,RequestHash=hash,Note=note,CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();
            }
            await tx.CommitAsync();
        }
        var purged=await lifecycle.Purge(t.AcademyId,id);return Results.Ok(new{erased=true,purged,message=purged?"تم حجب النسخة وإزالة ملفها؛ سجل النشر والحذف محفوظ.":"تم حجب النسخة؛ إزالة الملف ما زالت معلقة ولا تُحرر المساحة حتى التأكيد."});
    }
    private static IResult Conflict()=>Results.Conflict(new{message="تغير طلب الحذف أو حذفت النسخة بالفعل. أعد فتح المعرض؛ لا يوجد استرجاع تلقائي."});
}

public sealed class PrivateMediaLifecycleWorker(IServiceScopeFactory scopes,ILogger<PrivateMediaLifecycleWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try{await using var scope=scopes.CreateAsyncScope();await scope.ServiceProvider.GetRequiredService<PrivateMediaLifecycle>().RunBatch(stoppingToken);}
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){return;}
            catch(Exception){logger.LogWarning("تعذر إكمال دورة تنظيف الوسائط؛ ستُعاد المحاولة دون إعادة فتح النسخ المحجوبة.");}
        }while(await timer.WaitForNextTickAsync(stoppingToken));
    }
}
