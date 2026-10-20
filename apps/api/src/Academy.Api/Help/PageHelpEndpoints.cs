using System.Net;
using Academy.Api.Auth;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Help;
public static class PageHelpEndpoints
{
    public static readonly string[] Screens=["home","reports","matches","players","attendance","nutrition","evaluations"];
    public static readonly string[] Roles=["AcademyOwner","AcademyAdmin","Coach","Guardian"];
    public static void MapPageHelp(this WebApplication app)
    {
        app.MapGet("/api/v1/page-help/{screen}",async(string screen,CurrentTenant tenant,FoundationDbContext db,HttpResponse response)=>{
            var t=(await tenant.ResolveAsync())!;response.Headers.CacheControl="private, no-store";
            if(!Screens.Contains(screen))return Results.NotFound();
            var row=await db.PageHelpVideos.AsNoTracking().SingleOrDefaultAsync(v=>v.AcademyId==t.AcademyId&&v.Role==t.Role.ToString()&&v.Screen==screen&&v.IsActive);
            return Results.Ok(new{available=row!=null,screen,role=t.Role.ToString(),title=row?.Title,url=row?.Url});
        }).RequireAuthorization(AcademyPermissions.TenantAccess);
        var manage=app.MapGroup("/api/v1/manage/page-help").RequireAuthorization(AcademyPermissions.AcademySettingsManage);
        manage.MapGet("/",async(CurrentTenant tenant,FoundationDbContext db)=>{var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.AcademyOwner)return Results.Forbid();return Results.Ok(new{roles=Roles,screens=Screens,items=await db.PageHelpVideos.Where(v=>v.AcademyId==t.AcademyId).OrderBy(v=>v.Role).ThenBy(v=>v.Screen).Select(v=>new{v.Role,v.Screen,v.Title,v.Url,v.IsActive}).ToArrayAsync()});});
        manage.MapPut("/",Save).AddEndpointFilter<CsrfFilter>();
    }
    public sealed record Write(string Role,string Screen,string Title,string Url,bool IsActive=true);
    public static bool SafeUrl(string? value)=>value is{Length:>0 and <=2000}&&Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&string.IsNullOrEmpty(uri.UserInfo)&&string.IsNullOrEmpty(uri.Fragment)&&uri.Port==443&&!IPAddress.TryParse(uri.Host,out _)&&!uri.Host.Equals("localhost",StringComparison.OrdinalIgnoreCase)&&uri.Host.Contains('.')&&!uri.Host.EndsWith(".local",StringComparison.OrdinalIgnoreCase)&&!uri.Host.EndsWith(".localhost",StringComparison.OrdinalIgnoreCase);
    private static async Task<IResult> Save(Write body,CurrentTenant tenant,FoundationDbContext db,TimeProvider clock)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.AcademyOwner)return Results.Forbid();
        if(!Roles.Contains(body.Role)||!Screens.Contains(body.Screen)||string.IsNullOrWhiteSpace(body.Title)||body.Title.Length>120||!SafeUrl(body.Url))return Results.UnprocessableEntity(new{message="اختر الدور والصفحة وأدخل عنوانًا ورابط فيديو مباشر آمنًا بصيغةhttps."});
        await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"page-help:{t.AcademyId}:{body.Role}:{body.Screen}"},0))");
        var row=await db.PageHelpVideos.SingleOrDefaultAsync(v=>v.AcademyId==t.AcademyId&&v.Role==body.Role&&v.Screen==body.Screen);
        if(row==null){row=new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,Role=body.Role,Screen=body.Screen,Title=body.Title.Trim(),Url=body.Url,CreatedAtUtc=clock.GetUtcNow()};db.PageHelpVideos.Add(row);}
        row.Title=body.Title.Trim();row.Url=body.Url;row.IsActive=body.IsActive;row.UpdatedAtUtc=clock.GetUtcNow();await db.SaveChangesAsync();await tx.CommitAsync();return Results.NoContent();
    }
}
