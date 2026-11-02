using System.Security.Claims;
using System.Text;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Api.Slice5;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;
public static partial class AssetEndpoints
{
    public static bool MemoryIndexFits(int count)=>count is >=0 and <=1000;
    // The very same gallery query authorizes the audience first. No saved/public album or alternate ACL.
    private static async Task<IResult> Memories(Guid? playerId,Guid? groupId,string? month,int? page,string? format,CurrentTenant tenant,ClaimsPrincipal principal,FoundationDbContext db,IAuthorizationService authorization,TimeProvider time,HttpContext context)
    {
        context.Response.Headers.CacheControl="private, no-store";
        context.Response.Headers["X-Content-Type-Options"]="nosniff";
        var t=(await tenant.ResolveAsync())!;var today=await AttendanceCalendar.TodayAsync(t.AcademyId,db,time);var first=PlayerMonthlyEvidence.Month(month,today);
        if(first==null||page is <0 or >1000||format is not(null or "csv" or "text"))return Results.BadRequest(new{message="اختر شهرًا حتى الشهر الحالي وصفحة صحيحة للذكريات."});
        if(!playerId.HasValue&&!groupId.HasValue)return Results.BadRequest(new{message="افتح ذكريات لاعب أو مجموعة محددة، لا معرضًا عامًا."});
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        return await ReadGallery(playerId,groupId,false,tenant,principal,db,authorization,time,context,first,page??0,format);
    }
    private static async Task<IResult> ReadMemories(IQueryable<PrivateAsset> query,Guid? player,Guid? group,DateOnly first,int page,string? format,TenantMembership tenant,FoundationDbContext db,TimeProvider clock)
    {
        var today=await AttendanceCalendar.TodayAsync(tenant.AcademyId,db,clock);var last=first.AddMonths(1).AddDays(-1);if(last>today)last=today;var now=clock.GetUtcNow();
        if(player.HasValue&&(!await db.Players.AnyAsync(p=>p.Id==player&&p.IsActive)||!await db.SportEnrollments.AnyAsync(e=>e.PlayerId==player&&e.IsActive&&e.Player.IsActive&&e.Branch.IsActive&&e.Sport.IsActive)))return Results.NotFound();
        if(group.HasValue&&!await db.TrainingGroups.AnyAsync(g=>g.Id==group&&g.IsActive&&g.Branch.IsActive&&g.Sport.IsActive))return Results.NotFound();
        // Optional targets are intersections, never an alternative to the role's authorized query.
        if(player.HasValue)query=query.Where(a=>a.PlayerId==player||db.PrivateAssetAudiences.Any(p=>p.AssetId==a.Id&&p.PlayerId==player&&p.IsActive));
        if(group.HasValue)query=query.Where(a=>a.TrainingGroupId==group);
        var shareable=GroupMediaPrivacy.Shareable(db);
        // Even staff memories never resurrect withdrawn/expired/erased or unreviewed legacy originals.
        query=query.Where(a=>a.MediaPolicyVersion==1&&a.MediaEventOn>=first&&a.MediaEventOn<=last&&(a.RetainUntilUtc==null||a.RetainUntilUtc>now)&&!db.PrivateMediaErasures.Any(e=>e.AssetId==a.Id)&&!db.PrivateMediaWithdrawals.Any(w=>w.AssetId==a.Id)&&(a.TrainingGroupId==null||shareable.Any(s=>s.Id==a.Id)));
        var total=await query.CountAsync();var images=await query.CountAsync(a=>a.ContentType.StartsWith("image/"));var videos=await query.CountAsync(a=>a.ContentType.StartsWith("video/"));
        const int size=25,exportLimit=1000;
        if(format!=null&&!MemoryIndexFits(total))return Results.Conflict(new{message="يتجاوز هذا الشهر حد فهرس التصدير الآمن. لم نصدر فهرسًا ناقصًا؛ استخدم صفحات المعرض."});
        var items=await query.OrderByDescending(a=>a.MediaEventOn).ThenByDescending(a=>a.CreatedAtUtc).ThenBy(a=>a.Id).Skip(format==null?page*size:0).Take(format==null?size:exportLimit)
            .Select(a=>new{a.Id,a.Caption,a.ContentType,a.MediaEventOn,a.TrainingSessionId,a.RetainUntilUtc,url="/api/v1/assets/"+a.Id}).ToArrayAsync();
        if(format!=null)
        {
            // Metadata only; no bearer URLs, tickets, child roster, embedded bytes or permission bypass.
            var text=PlayerMonthlyEvidence.Csv(new[]{new[]{"تاريخ الواقعة","الوصف","النوع","مرجع المصدر","مرتبط بحصة","موعد الاحتفاظ المسجل"}}.Concat(items.Select(a=>new[]{a.MediaEventOn?.ToString("yyyy-MM-dd")??"",a.Caption??"",a.ContentType.StartsWith("video/")?"فيديو":"صورة",a.Id.ToString(),a.TrainingSessionId.HasValue?"نعم":"لا",a.RetainUntilUtc?.ToString("O")??"لم تحدد مدة"})));
            return format=="text"?Results.Ok(new{text}):Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray(),"text/csv; charset=utf-8",$"private-memories-index-{first:yyyy-MM}.csv");
        }
        return Results.Ok(new{month=first.ToString("yyyy-MM"),from=first,to=last,asOfDate=today,page,pageSize=size,total,images,videos,hasMore=total>(page+1)*size,items,
            notice="ذكريات شهرية من المعرض الخاص القائم، حسب تاريخ الواقعة لا تاريخ الرفع. العرض الحالي يعيد فحص الجمهور والموافقات؛ السحب والانتهاء والحذف يحجبونها، ولا تعود النسخة تلقائيًا. لا ملفات قديمة غير مراجعة أو صور طبية أو إثباتات دفع. فهرس CSV يصف الشهر دون صور أو فيديوهات أو روابط مشاركة، بحد1000 نسخة ثم رفض صريح دون فهرس ناقص؛ التصفح يظل متاحًا. ليس إذن نشر خارجي أو نسخة احتياطية، ولا يشترط تجديد الاشتراك."});
    }
}
