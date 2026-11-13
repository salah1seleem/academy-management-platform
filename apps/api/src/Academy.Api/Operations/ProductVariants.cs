using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Infrastructure.Content;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class ProductVariants
{
    public sealed record VariantWrite(string Size, int? ShirtNumber, int Quantity, string? Color = null);
    public sealed record InventoryWrite(int ExpectedRevision, bool NumberedUniform, bool ReconcileExistingStock, VariantWrite[] Variants);
    public sealed record ShirtWrite(int? ShirtNumber);
    public static void MapProductVariants(this WebApplication app)
    {
        app.MapGet("/api/v1/content/catalog/{id:guid}/inventory", Read).RequireAuthorization(AcademyPermissions.SportCatalogManage);
        app.MapPut("/api/v1/content/catalog/{id:guid}/inventory", Save).RequireAuthorization(AcademyPermissions.SportCatalogManage).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/guardian/catalog/{id:guid}/variants", Guardian).RequireAuthorization(AcademyPermissions.GuardianOwnRenewal);
        app.MapPut("/api/v1/people/enrollments/{id:guid}/shirt-number", Shirt).RequireAuthorization(AcademyPermissions.PeopleManage).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<bool> Allowed(FoundationDbContext db, TenantMembership t, string module, bool edit) => t.Role == AcademyRole.AcademyOwner ||
        t.Role == AcademyRole.AcademyAdmin && await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.Module==module&&g.IsActive&&g.Read&&(!edit||g.Edit));
    private static object View(SportCatalogItem p, IEnumerable<ProductStockVariant> variants) => new {p.Id,p.UsesVariants,p.NumberedUniform,p.StockRevision,p.StockQuantity,variants=variants.OrderBy(v=>v.Size).ThenBy(v=>v.ShirtNumber).Select(v=>new{v.Id,v.Size,v.Color,v.ShirtNumber,v.Quantity})};
    private static async Task<IResult> Read(Guid id, CurrentTenant tenant, FoundationDbContext db)
    {
        var t=(await tenant.ResolveAsync())!;if(!await Allowed(db,t,"catalog",false))return Results.Forbid();
        var p=await db.SportCatalogItems.AsNoTracking().Include(p=>p.Variants.Where(v=>v.IsActive)).SingleOrDefaultAsync(p=>p.Id==id);
        return p is null?Results.NotFound():Results.Ok(View(p,p.Variants));
    }
    private static async Task<IResult> Save(Guid id, InventoryWrite body, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t=(await tenant.ResolveAsync())!;if(!await Allowed(db,t,"catalog",true))return Results.Forbid();
        if(body.Variants is null || body.Variants.Length is <1 or >500 || body.Variants.Any(v=>string.IsNullOrWhiteSpace(v.Size)||v.Size.Trim().Length>30||(v.Color?.Trim().Length??0)>40||v.Quantity is <0 or >1000000||v.ShirtNumber is <0 or >999||body.NumberedUniform!=v.ShirtNumber.HasValue))
            return Results.Problem(statusCode:422,title:"حدد المقاس والكمية الصحيحة، ورقم كل قميص عند اختيار زي مرقم.");
        var requested=body.Variants.Select(v=>v with{Size=v.Size.Trim().ToUpperInvariant(),Color=v.Color?.Trim()??""}).ToArray();
        if(requested.Select(v=>(v.Color,v.Size,v.ShirtNumber)).Distinct().Count()!=requested.Length||requested.Sum(v=>(long)v.Quantity)>1000000)
            return Results.Problem(statusCode:422,title:"لا تكرر اللون والمقاس والرقم، وإجمالي المخزون ضمن مليون قطعة.");
        await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"product-stock:{t.AcademyId}:{id}"}, 0))");
        var p=await db.SportCatalogItems.Include(p=>p.Variants).SingleOrDefaultAsync(p=>p.Id==id);if(p is null)return Results.NotFound();
        if(p.StockRevision!=body.ExpectedRevision)return Results.Problem(statusCode:409,title:"تغير المخزون أثناء المراجعة. حدّث المقاسات والأرقام قبل الحفظ.");
        if(!p.UsesVariants && (!body.ReconcileExistingStock||p.StockQuantity!=requested.Sum(v=>v.Quantity)))
            return Results.Problem(statusCode:409,title:"وزع إجمالي المخزون الحالي على المقاسات والأرقام واعتمد مراجعته أولًا.");
        if(p.UsesVariants&&p.NumberedUniform!=body.NumberedUniform)return Results.Problem(statusCode:409,title:"نوع ترقيم المنتج ثابت بعد تفعيل المقاسات. أنشئ منتجًا مستقلًا للنوع الآخر.");
        var now=clock.GetUtcNow();
        foreach(var v in p.Variants){if(!requested.Any(r=>r.Color==v.Color&&r.Size==v.Size&&r.ShirtNumber==v.ShirtNumber)){if(v.Quantity>0)return Results.Problem(statusCode:409,title:"لا تُخفِ مقاسًا له رصيد. صفّر رصيده بعد المراجعة ثم احفظ.");v.IsActive=false;v.UpdatedAtUtc=now;}}
        foreach(var item in requested){var row=p.Variants.SingleOrDefault(v=>v.Color==item.Color&&v.Size==item.Size&&v.ShirtNumber==item.ShirtNumber);
            if(row is null){row=new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,ProductId=id,Size=item.Size,Color=item.Color!,ShirtNumber=item.ShirtNumber,CreatedAtUtc=now};p.Variants.Add(row);db.ProductStockVariants.Add(row);}
            row.Quantity=item.Quantity;row.IsActive=true;row.UpdatedAtUtc=now;
        }
        p.UsesVariants=true;p.NumberedUniform=body.NumberedUniform;p.StockQuantity=requested.Sum(v=>v.Quantity);p.StockRevision++;p.UpdatedAtUtc=now;
        await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(View(p,p.Variants.Where(v=>v.IsActive)));
    }
    private static async Task<IResult> Guardian(Guid id, Guid? enrollmentId, CurrentTenant tenant, ClaimsPrincipal actor, FoundationDbContext db)
    {
        var t=(await tenant.ResolveAsync())!;var user=Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var p=await db.SportCatalogItems.AsNoTracking().Include(p=>p.Variants.Where(v=>v.IsActive)).SingleOrDefaultAsync(p=>p.Id==id&&p.IsActive&&p.Sport.IsActive);if(p is null)return Results.NotFound();
        var eligible=db.SportEnrollments.AsNoTracking().Where(e=>e.IsActive&&e.Player.IsActive&&e.SportId==p.SportId&&db.GuardianPlayerLinks.Any(l=>l.PlayerId==e.PlayerId&&l.IsActive&&l.Guardian.UserId==user));
        if(!await eligible.AnyAsync()||enrollmentId.HasValue&&!await eligible.AnyAsync(e=>e.Id==enrollmentId))return Results.NotFound();
        var assigned=enrollmentId.HasValue?await eligible.Where(e=>e.Id==enrollmentId).Select(e=>e.ShirtNumber).SingleAsync():null;
        return Results.Ok(new{p.UsesVariants,p.NumberedUniform,p.StockRevision,p.StockQuantity,unitPrice=p.DisplayPrice is decimal price?decimal.Round(price*(100-(p.DiscountPercentage??0))/100,2,MidpointRounding.AwayFromZero):(decimal?)null,p.Currency,assignedShirtNumber=assigned,variants=p.Variants.Where(v=>v.Quantity>0).OrderBy(v=>v.Size).ThenBy(v=>v.ShirtNumber).Select(v=>new{v.Id,v.Size,v.Color,v.ShirtNumber,v.Quantity})});
    }
    private static async Task<IResult> Shirt(Guid id, ShirtWrite body, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t=(await tenant.ResolveAsync())!;if(!await Allowed(db,t,"players",true))return Results.Forbid();
        if(body.ShirtNumber is <0 or >999)return Results.Problem(statusCode:422,title:"رقم القميص من0إلى999، أو اتركه فارغًا.");
        var e=await db.SportEnrollments.SingleOrDefaultAsync(e=>e.Id==id&&e.IsActive);if(e is null)return Results.NotFound();
        e.ShirtNumber=body.ShirtNumber;e.UpdatedAtUtc=clock.GetUtcNow();await db.SaveChangesAsync();return Results.Ok(new{e.Id,e.ShirtNumber});
    }
}
