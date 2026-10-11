using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class ProductSales
{
    public static void MapProductSales(this WebApplication app)
    {
        app.MapPost("/api/v1/guardian/product-sales", CreateGuardian).RequireAuthorization(AcademyPermissions.GuardianOwnRenewal).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/guardian/product-sales", OwnSales).RequireAuthorization(AcademyPermissions.GuardianOwnRenewal);
        app.MapPost("/api/v1/guardian/product-sales/{id:guid}/proof", Proof).RequireAuthorization(AcademyPermissions.GuardianOwnRenewal).AddEndpointFilter<CsrfFilter>();
        app.MapPost("/api/v1/manage/product-sales", CreateStaff).RequireAuthorization(AcademyPermissions.SportCatalogManage).AddEndpointFilter<CsrfFilter>();
        app.MapPost("/api/v1/manage/product-sales/{id:guid}/review", Review).RequireAuthorization(AcademyPermissions.SportCatalogManage).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/manage/product-sales/{id:guid}", Details).RequireAuthorization(AcademyPermissions.PaymentRead);
    }
    private static Guid Actor(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static Task<IResult> CreateGuardian(SaleWrite body, HttpContext h, CurrentTenant t, FoundationDbContext db, ISubscriptionClock c) => Create(body, h, t, db, c, false);
    private static Task<IResult> CreateStaff(SaleWrite body, HttpContext h, CurrentTenant t, FoundationDbContext db, ISubscriptionClock c) => Create(body, h, t, db, c, true);
    private static async Task<IResult> Create(SaleWrite body, HttpContext h, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock, bool staff)
    {
        var t = (await tenant.ResolveAsync())!; var actor = Actor(h.User); var key = h.Request.Headers["Idempotency-Key"].ToString();
        if (body.Quantity is < 1 or > 100 || string.IsNullOrWhiteSpace(key) || key.Length > 100 || staff && body.PaymentMethod is not ("Cash" or "Online")) return Results.UnprocessableEntity(new { message = "اختر كمية وطريقة دفع صحيحتين." });
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"product-key:{t.AcademyId}:{actor}:{key}"}, 0))");
        var prior = await db.ProductSales.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.RequestedByUserId == actor && x.IdempotencyKey == key);
        if (prior is not null) return prior.ProductId == body.ProductId && prior.Quantity == body.Quantity && prior.PlayerId == body.PlayerId && prior.BranchId == body.BranchId && (!staff || prior.PaymentMethod == body.PaymentMethod) ? Results.Ok(new { prior.Id, prior.Status, prior.Amount, prior.Currency, replay = true }) : Results.Conflict(new { message = "لا يمكن استخدام مرجع الطلب نفسه لبيانات مختلفة." });
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"product-stock:{t.AcademyId}:{body.ProductId}"}, 0))");
        var product = await db.SportCatalogItems.Include(x => x.Sport).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == body.ProductId && x.IsActive && x.Sport.IsActive);
        if (product?.DisplayPrice is null || string.IsNullOrWhiteSpace(product.Currency)) return Results.NotFound();
        if (product.StockQuantity < body.Quantity) return Results.Conflict(new { message = "الكمية المطلوبة غير متاحة حاليًا." });
        if (body.PlayerId.HasValue && !await db.Players.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.PlayerId && x.IsActive)) return Results.NotFound();
        if (!staff && (body.PlayerId.HasValue && !await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == t.AcademyId && x.PlayerId == body.PlayerId && x.Guardian.UserId == actor && x.IsActive) || !await db.SportEnrollments.AnyAsync(x => x.AcademyId == t.AcademyId && x.SportId == product.SportId && x.IsActive && db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.PlayerId == x.PlayerId && l.Guardian.UserId == actor && l.IsActive)))) return Results.NotFound();
        var branch = body.BranchId.HasValue ? await db.Branches.Where(x => x.AcademyId == t.AcademyId && x.Id == body.BranchId && x.IsActive).Select(x => new { x.Id, x.ArabicName }).SingleOrDefaultAsync() : null;
        if (body.BranchId.HasValue && (branch is null || !staff && !await db.SportEnrollments.AnyAsync(x => x.AcademyId == t.AcademyId && x.BranchId == branch.Id && x.SportId == product.SportId && x.IsActive && db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.PlayerId == x.PlayerId && l.Guardian.UserId == actor && l.IsActive)))) return Results.NotFound();
        if (!staff && body.PlayerId.HasValue && !await db.SportEnrollments.AnyAsync(x => x.AcademyId == t.AcademyId && x.PlayerId == body.PlayerId && x.SportId == product.SportId && (!body.BranchId.HasValue || x.BranchId == body.BranchId) && x.IsActive)) return Results.NotFound();
        var unit = decimal.Round(product.DisplayPrice.Value * (100 - (product.DiscountPercentage ?? 0)) / 100, 2, MidpointRounding.AwayFromZero);
        var row = new ProductSale { Id = Guid.NewGuid(), AcademyId = t.AcademyId, ProductId = product.Id, RequestedByUserId = actor, PlayerId = body.PlayerId, Quantity = body.Quantity, UnitPrice = unit, Amount = unit * body.Quantity, Currency = product.Currency, ProductName = product.ArabicName, SportId = product.SportId, SportName = product.Sport.ArabicName, BranchId = branch?.Id, BranchName = branch?.ArabicName, Status = staff ? "Confirmed" : "Pending", PaymentMethod = staff ? body.PaymentMethod : null, ConfirmedAtUtc = staff ? clock.UtcNow : null, ConfirmedByUserId = staff ? actor : null, IdempotencyKey = key, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        if (staff) { product.StockQuantity -= body.Quantity; product.UpdatedAtUtc = clock.UtcNow; }
        db.ProductSales.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { row.Id, row.Status, row.Amount, row.Currency });
    }
    private static async Task<IResult> OwnSales(CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    { var t = (await tenant.ResolveAsync())!; var user = Actor(principal); return Results.Ok(await db.ProductSales.Where(x => x.AcademyId == t.AcademyId && x.RequestedByUserId == user).OrderByDescending(x => x.CreatedAtUtc).Select(x => new { x.Id, x.ProductName, x.Quantity, x.Amount, x.Currency, x.Status, x.ReviewReason, x.ConfirmedAtUtc }).Take(100).ToListAsync()); }
    private static async Task<IResult> Proof(Guid id, ProofWrite body, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var user = Actor(principal); await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"product-review:{t.AcademyId}:{id}"}, 0))");
        var sale = await db.ProductSales.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.RequestedByUserId == user);
        if (sale is null || !await db.PrivateAssets.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.AssetId && x.UploadedByUserId == user && x.Purpose == "Proof")) return Results.NotFound();
        if (sale.Status != "Pending") return Results.Conflict(); sale.ProofAssetId = body.AssetId; sale.UpdatedAtUtc = clock.UtcNow; await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { message = "وصل إثبات الدفع. ستُراجع الإدارة الطلب وتؤكد توافر المنتج." });
    }
    private static async Task<IResult> Details(Guid id, CurrentTenant tenant, FoundationDbContext db)
    { var t = (await tenant.ResolveAsync())!; var row = await db.ProductSales.Where(x => x.AcademyId == t.AcademyId && x.Id == id).Select(x => new { x.Id, x.ProductName, x.Quantity, x.UnitPrice, x.Amount, x.Currency, x.Status, x.ReviewReason, proofUrl = x.ProofAssetId == null ? null : "/api/v1/assets/" + x.ProofAssetId }).SingleOrDefaultAsync(); return row is null ? Results.NotFound() : Results.Ok(row); }
    private static async Task<IResult> Review(Guid id, ReviewWrite body, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var user = Actor(principal); if (body.Reason?.Length > 500) return Results.UnprocessableEntity(); await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"product-review:{t.AcademyId}:{id}"}, 0))");
        var sale = await db.ProductSales.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (sale is null) return Results.NotFound();
        if (sale.Status != "Pending") return sale.Status == (body.Accept ? "Confirmed" : "Rejected") ? Results.Ok(new { replay = true }) : Results.Conflict();
        if (!sale.ProofAssetId.HasValue) return Results.Conflict(new { message = "لم يصل إثبات دفع بعد." });
        if (body.Accept) {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"product-stock:{t.AcademyId}:{sale.ProductId}"}, 0))");
            var product = await db.SportCatalogItems.Include(x => x.Sport).SingleAsync(x => x.AcademyId == t.AcademyId && x.Id == sale.ProductId);
            if (!product.IsActive || !product.Sport.IsActive || product.StockQuantity < sale.Quantity) return Results.Conflict(new { message = "المنتج متوقف أو المخزون غير كافٍ. لا تؤكد الطلب؛ راجع التحويل مع ولي الأمر أولًا." });
            product.StockQuantity -= sale.Quantity; product.UpdatedAtUtc = clock.UtcNow; sale.PaymentMethod = "Online"; sale.ConfirmedAtUtc = clock.UtcNow; sale.ConfirmedByUserId = user;
        }
        sale.Status = body.Accept ? "Confirmed" : "Rejected"; sale.ReviewReason = body.Reason?.Trim(); sale.UpdatedAtUtc = clock.UtcNow;
        db.AcademyNotices.Add(new AcademyNotice { Id = Guid.NewGuid(), AcademyId = t.AcademyId, UserId = sale.RequestedByUserId, Message = body.Accept ? "تم قبول دفع المنتج الرياضي. تواصل مع الأكاديمية للاستلام." : "لم يُقبل طلب المنتج. " + (body.Reason?.Trim() ?? "تواصل مع الأكاديمية للتوضيح."), CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { sale.Status });
    }
}
public sealed record SaleWrite(Guid ProductId, int Quantity, Guid? PlayerId, string? PaymentMethod, Guid? BranchId = null);
