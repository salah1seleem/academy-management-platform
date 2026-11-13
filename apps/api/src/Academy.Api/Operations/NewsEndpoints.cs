using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Api.Configuration;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class NewsEndpoints
{
    public static void MapNews(this WebApplication app)
    {
        var staff = app.MapGroup("/api/v1/content/news").RequireAuthorization(AcademyPermissions.TenantAccess).WithMetadata(new AcademyFeatureMetadata("news"));
        staff.MapGet("/", List);
        staff.MapGet("/options", Options);
        staff.MapGet("/{id:guid}", Detail);
        staff.MapPost("/", Create).AddEndpointFilter<CsrfFilter>();
        staff.MapPost("/image", AssetEndpoints.UploadNews).AddEndpointFilter<CsrfFilter>();
        staff.MapPut("/{id:guid}", Edit).AddEndpointFilter<CsrfFilter>();
        staff.MapDelete("/{id:guid}", Archive).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/news", Feed).RequireAuthorization(AcademyPermissions.TenantAccess).WithMetadata(new AcademyFeatureMetadata("news"));
    }
    private static Guid User(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static object Read(AcademyNews n, DateTimeOffset now,TimeZoneInfo zone) => new {
        n.Id,n.Title,n.Body,n.Kind,n.VisibleDays,n.IsActive,n.PublishedAtUtc,n.StartsAtUtc,n.EndsAtUtc,n.TargetBranchIds,n.AudienceRoles,n.Version,
        state=NewsAudience.State(n,now),localStartsOn=n.StartsAtUtc.HasValue?(DateOnly?)DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(n.StartsAtUtc.Value,zone).DateTime):null,
        localEndsOn=n.EndsAtUtc.HasValue?(DateOnly?)DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(n.EndsAtUtc.Value.AddTicks(-1),zone).DateTime):null,imageUrl=n.ImageAssetId==null?null:"/api/v1/assets/"+n.ImageAssetId,n.ImageAssetId
    };
    private static async Task<IResult> List(CurrentTenant tenant,FoundationDbContext db,TimeProvider clock,int? page,bool? paged,string? kind)
    {
        var t=(await tenant.ResolveAsync())!;
        if(!await NewsAudience.ManageAllowed(db,t,"Read"))return Results.Forbid();
        if(page is <1 or >100000)return Invalid("رقم الصفحة غير صالح.");
        if(kind!=null&&!NewsAudience.Kinds.Contains(kind))return Invalid("نوع المحتوى غير صالح.");
        var zone=TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>a.TimeZone).SingleAsync());
        var q=NewsAudience.Managed(db,t.AcademyId).AsNoTracking();
        if(kind!=null)q=q.Where(n=>n.Kind==kind);
        var size=paged==true||page.HasValue?25:100;
        var items=await q.OrderByDescending(n=>n.CreatedAtUtc).ThenBy(n=>n.Id).Skip(((page??1)-1)*size).Take(size).ToArrayAsync();
        return paged==true?Results.Ok(new{items=items.Select(n=>Read(n,clock.GetUtcNow(),zone)),total=await q.CountAsync(),page=page??1,pageSize=size}):Results.Ok(items.Select(n=>Read(n,clock.GetUtcNow(),zone)));
    }
    private static async Task<IResult> Detail(Guid id,CurrentTenant tenant,FoundationDbContext db,TimeProvider clock)
    {
        var t=(await tenant.ResolveAsync())!;if(!await NewsAudience.ManageAllowed(db,t,"Read"))return Results.Forbid();
        var zone=TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>a.TimeZone).SingleAsync());
        var row=await NewsAudience.Managed(db,t.AcademyId).AsNoTracking().SingleOrDefaultAsync(n=>n.Id==id);
        return row==null?Results.NotFound():Results.Ok(Read(row,clock.GetUtcNow(),zone));
    }
    private static async Task<IResult> Options(CurrentTenant tenant,FoundationDbContext db,TimeProvider time)
    {
        var t=(await tenant.ResolveAsync())!;if(!await NewsAudience.ManageAllowed(db,t,"Read"))return Results.Forbid();
        return Results.Ok(new {branches=await db.Branches.Where(b=>b.AcademyId==t.AcademyId&&b.IsActive).OrderBy(b=>b.ArabicName).Select(b=>new{b.Id,name=b.ArabicName}).ToArrayAsync(),
            allBranchesAllowed=!db.BranchScopeRestricted,kinds=NewsAudience.Kinds,roles=NewsAudience.Roles,
            today=await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId,db,time),
            canCreate=await NewsAudience.ManageAllowed(db,t,"Add"),canEdit=await NewsAudience.ManageAllowed(db,t,"Edit"),canDelete=await NewsAudience.ManageAllowed(db,t,"Delete")});
    }
    private static async Task<IResult> Feed(CurrentTenant tenant, FoundationDbContext db, TimeProvider clock,
        ClaimsPrincipal actor, int? page, string? kind, Guid? item, HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        if (page is < 1 or > 100000) return Invalid("رقم الصفحة غير صالح.");
        if (kind is not (null or "News" or "Event")) return Invalid("نوع المحتوى غير صالح.");
        var t = (await tenant.ResolveAsync())!;
        var query = await NewsAudience.Feed(db, t, User(actor), clock.GetUtcNow());
        if (kind == "Event") query = query.Where(n => n.Kind == "Event");
        if (kind == "News") query = query.Where(n => n.Kind != "Event");
        if (item.HasValue) query = query.Where(n => n.Id == item);
        var size = page.HasValue ? 20 : 10;
        var pageNumber = item.HasValue ? 1 : page ?? 1;
        var items = await query.OrderByDescending(n => n.PublishedAtUtc).ThenBy(n => n.Id)
            .Skip((pageNumber - 1) * size).Take(size).Select(n => new
            {
                n.Id, n.Title, n.Body, n.Kind, n.PublishedAtUtc, n.StartsAtUtc, n.EndsAtUtc,
                imageUrl = n.ImageAssetId == null ? null : "/api/v1/assets/" + n.ImageAssetId
            }).ToArrayAsync();
        return page.HasValue
            ? Results.Ok(new { items, total = await query.CountAsync(), page = pageNumber, pageSize = size })
            : Results.Ok(items);
    }
    private static Task<IResult> Create(NewsWrite body,CurrentTenant t,FoundationDbContext db,TimeProvider clock) => Save(null,body,t,db,clock);
    private static Task<IResult> Edit(Guid id,NewsWrite body,CurrentTenant t,FoundationDbContext db,TimeProvider clock) => Save(id,body,t,db,clock);
    private static IResult Invalid(string message)=>Results.UnprocessableEntity(new{message});
    private static async Task<IResult> Save(Guid? id,NewsWrite body,CurrentTenant tenant,FoundationDbContext db,TimeProvider clock)
    {
        var t=(await tenant.ResolveAsync())!;
        if(!await NewsAudience.ManageAllowed(db,t,id.HasValue?"Edit":"Add"))return Results.Forbid();
        if(body.LocalStartsOn.HasValue||body.LocalEndsOn.HasValue){
            if(!body.LocalStartsOn.HasValue||!body.LocalEndsOn.HasValue||body.LocalStartsOn>body.LocalEndsOn||body.LocalStartsOn.Value.Year is <2000 or >9998||body.LocalEndsOn.Value.Year is <2000 or >9998)return Invalid("راجع تاريخ بداية الظهور ونهايته.");
            var localZone=await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>a.TimeZone).SingleAsync();
            var bounds=Academy.Api.Slice8.ReportingEndpoints.UtcRange(body.LocalStartsOn.Value,body.LocalEndsOn.Value,localZone);
            body=body with{StartsAtUtc=bounds.Start,EndsAtUtc=bounds.EndExclusive};
        }
        if(string.IsNullOrWhiteSpace(body.Title)||body.Title.Length>180||string.IsNullOrWhiteSpace(body.Body)||body.Body.Length>10000||body.VisibleDays is <1 or >90)
            return Invalid("أدخل عنوانًا وتفاصيل ومدة ظهور بين يوم و90 يومًا.");
        if(body.Kind!=null&&!NewsAudience.Kinds.Contains(body.Kind)||body.AudienceRoles!=null&&(body.AudienceRoles.Length==0||body.AudienceRoles.Length>3||body.AudienceRoles.Distinct().Count()!=body.AudienceRoles.Length||body.AudienceRoles.Any(r=>!NewsAudience.Roles.Contains(r))))
            return Invalid("اختر نوع المحتوى والجمهور من الخيارات المتاحة.");
        if(body.StartsAtUtc.HasValue!=body.EndsAtUtc.HasValue||body.StartsAtUtc>=body.EndsAtUtc||body.StartsAtUtc.HasValue&&(body.EndsAtUtc-body.StartsAtUtc)>TimeSpan.FromDays(90))
            return Invalid("اختر بداية ونهاية صحيحتين للظهور حتى90 يومًا.");
        if(body.TargetBranchIds!=null&&(body.TargetBranchIds.Length>100||body.TargetBranchIds.Distinct().Count()!=body.TargetBranchIds.Length||await db.Branches.CountAsync(b=>b.AcademyId==t.AcademyId&&b.IsActive&&body.TargetBranchIds.Contains(b.Id))!=body.TargetBranchIds.Length))
            return Invalid("اختر فروعًا فعالة من نطاقك في الأكاديمية.");
        if(body.ImageAssetId.HasValue&&!await db.PrivateAssets.AnyAsync(a=>a.AcademyId==t.AcademyId&&a.Id==body.ImageAssetId&&a.IsActive&&a.Purpose=="News"&&a.ContentType.StartsWith("image/")))return Results.NotFound();
        if(body.ImageAssetId.HasValue&&t.Role!=Academy.Infrastructure.Tenancy.AcademyRole.AcademyOwner&&
            !await db.PrivateAssets.AnyAsync(a=>a.Id==body.ImageAssetId&&a.UploadedByUserId==db.RequestUserId)&&
            !await NewsAudience.Managed(db,t.AcademyId).AnyAsync(n=>n.ImageAssetId==body.ImageAssetId))return Results.NotFound();
        var zone=TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>a.TimeZone).SingleAsync());
        await using var tx=await db.Database.BeginTransactionAsync();
        if(body.RequestId.HasValue)await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"academy-content:{t.AcademyId}:{body.RequestId}"},0))");
        var hash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(body)));
        if(!id.HasValue&&body.RequestId.HasValue){
            if(body.RequestId==Guid.Empty)return Invalid("رمز محاولة الحفظ غير صالح.");
            var previous=await db.AcademyNews.SingleOrDefaultAsync(n=>n.AcademyId==t.AcademyId&&n.CreationRequestId==body.RequestId);
            if(previous!=null){
                if(!await NewsAudience.Managed(db,t.AcademyId).AnyAsync(n=>n.Id==previous.Id))return Results.NotFound();
                return previous.CreationHash==hash?Results.Ok(Read(previous,clock.GetUtcNow(),zone)):Results.Conflict(new{message="رمز المحاولة مستخدم لمحتوى مختلف؛ راجع السجل."});
            }
        }
        if(id.HasValue)await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"academy-content-row:{t.AcademyId}:{id}"},0))");
        var row=id.HasValue?await NewsAudience.Managed(db,t.AcademyId).SingleOrDefaultAsync(n=>n.Id==id):null;
        if(id.HasValue&&row==null)return Results.NotFound();
        if(row!=null&&body.ExpectedVersion.HasValue&&row.Version!=body.ExpectedVersion)return Results.Conflict(new{message="تغير المحتوى؛ أعد تحميله قبل الحفظ."});
        var targets=body.TargetBranchIds??row?.TargetBranchIds??[];
        if(db.BranchScopeRestricted&&(targets.Length==0||targets.Any(b=>!db.RequestBranchIds.Contains(b))))return Results.Forbid();
        if(row==null){row=new AcademyNews{Id=Guid.NewGuid(),AcademyId=t.AcademyId,Title=body.Title.Trim(),Body=body.Body.Trim(),CreatedAtUtc=clock.GetUtcNow(),
            CreationRequestId=body.RequestId,CreationHash=body.RequestId.HasValue?hash:null};db.AcademyNews.Add(row);}
        row.Title=body.Title.Trim();row.Body=body.Body.Trim();row.ImageAssetId=body.ImageAssetId;row.VisibleDays=body.VisibleDays;row.IsActive=body.IsActive;
        row.Kind=body.Kind??row.Kind;row.TargetBranchIds=targets;row.AudienceRoles=body.AudienceRoles??row.AudienceRoles;
        if(body.StartsAtUtc.HasValue){row.StartsAtUtc=body.StartsAtUtc;row.EndsAtUtc=body.EndsAtUtc;}
        if(!body.Publish)row.PublishedAtUtc=null;else row.PublishedAtUtc??=clock.GetUtcNow();
        row.UpdatedAtUtc=clock.GetUtcNow();await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(Read(row,clock.GetUtcNow(),zone));
    }
    public sealed record NewsArchive(uint ExpectedVersion);
    private static async Task<IResult> Archive(Guid id,[Microsoft.AspNetCore.Mvc.FromBody] NewsArchive body,CurrentTenant tenant,FoundationDbContext db,TimeProvider clock)
    {
        var t=(await tenant.ResolveAsync())!;if(!await NewsAudience.ManageAllowed(db,t,"Delete"))return Results.Forbid();
        var zone=TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>a.TimeZone).SingleAsync());
        await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"academy-content-row:{t.AcademyId}:{id}"},0))");
        var row=await NewsAudience.Managed(db,t.AcademyId).SingleOrDefaultAsync(n=>n.Id==id);if(row==null)return Results.NotFound();
        if(row.IsActive&&row.Version!=body.ExpectedVersion)return Results.Conflict(new{message="تغير المحتوى؛ أعد تحميله قبل الحذف."});
        row.IsActive=false;row.UpdatedAtUtc=clock.GetUtcNow();await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(Read(row,clock.GetUtcNow(),zone));
    }
}
public sealed record NewsWrite(string Title,string Body,Guid? ImageAssetId,int VisibleDays,bool Publish,bool IsActive,
    string? Kind=null,Guid[]? TargetBranchIds=null,string[]? AudienceRoles=null,DateTimeOffset? StartsAtUtc=null,DateTimeOffset? EndsAtUtc=null,uint? ExpectedVersion=null,Guid? RequestId=null,
    DateOnly? LocalStartsOn=null,DateOnly? LocalEndsOn=null);
