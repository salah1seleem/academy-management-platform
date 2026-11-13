using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static partial class ProductSales
{
    public static void MapProductSales(this WebApplication app)
    {
        app.MapProductVariants();
        app.MapPost("/api/v1/guardian/product-sales", CreateGuardian).RequireAuthorization(AcademyPermissions.GuardianOwnRenewal).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/guardian/product-sales", OwnSales).RequireAuthorization(AcademyPermissions.GuardianOwnRenewal);
        app.MapGet("/api/v1/guardian/product-sales/{id:guid}", OwnSale).RequireAuthorization(AcademyPermissions.GuardianOwnRenewal);
        app.MapPost("/api/v1/guardian/product-sales/{id:guid}/proof", Proof).RequireAuthorization(AcademyPermissions.GuardianOwnRenewal).AddEndpointFilter<CsrfFilter>();
        app.MapPost("/api/v1/manage/product-sales", CreateStaff).RequireAuthorization(AcademyPermissions.SportCatalogManage).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/manage/product-sales", ListSales).RequireAuthorization(AcademyPermissions.PaymentRead);
        app.MapGet("/api/v1/manage/product-sales/by-request", ReadStaffRequest).RequireAuthorization(AcademyPermissions.SportCatalogManage);
        app.MapGet("/api/v1/manage/product-sales/options", async (CashRegisterService cash) => {
            try { return Results.Ok(await cash.ProductOptions()); }
            catch (CashRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
        }).RequireAuthorization(AcademyPermissions.SportCatalogManage);
        app.MapGet("/api/v1/manage/product-sales/quote", async (Guid productId, Guid branchId, int quantity, Guid? playerId, Guid? variantId, CashRegisterService cash) => {
            try { return Results.Ok(await cash.ProductQuote(productId, branchId, quantity, playerId, variantId)); }
            catch (CashRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
        }).RequireAuthorization(AcademyPermissions.SportCatalogManage);
        app.MapPost("/api/v1/manage/product-sales/{id:guid}/review", Review).RequireAuthorization(AcademyPermissions.SportCatalogManage).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/manage/product-sales/{id:guid}", Details).RequireAuthorization(AcademyPermissions.PaymentRead);
    }
    private static Guid Actor(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static async Task<IResult> ReadStaffRequest(Guid productId, Guid branchId, string requestKey, HttpContext http, CurrentTenant tenant, FoundationDbContext db, CashRegisterService cash)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrWhiteSpace(requestKey) || requestKey.Length > 100) return Results.NotFound();
        try { await cash.AuthorizeProductSale(); }
        catch (CashRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
        var context = (await tenant.ResolveAsync())!;
        var actor = Actor(http.User);
        var saved = await db.ProductSales.AsNoTracking()
            .Where(s => s.AcademyId == context.AcademyId && s.RequestedByUserId == actor && s.ConfirmedByUserId == actor &&
                s.ProductId == productId && s.BranchId == branchId && s.IdempotencyKey == requestKey && s.Status == "Confirmed" && s.ProofAssetId == null &&
                (s.PaymentMethod == "Online" || s.PaymentMethod == "Cash" && db.CashProductPostings.Any(p => p.ProductSaleId == s.Id)))
            .Select(s => new { s.Id, s.Status, s.Amount, s.Currency, s.PlayerId }).SingleOrDefaultAsync(http.RequestAborted);
        if (saved is null) return Results.NotFound();
        if (saved.PlayerId.HasValue)
        {
            try { await cash.AuthorizeProductSale(hasPlayer: true); }
            catch (CashRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
        }
        return Results.Ok(new { saved.Id, saved.Status, amount = CashRegisterService.Money(saved.Amount), saved.Currency });
    }
    private static Task<IResult> CreateGuardian(SaleWrite body, HttpContext h, CurrentTenant t, FoundationDbContext db, ISubscriptionClock c, CashRegisterService cash) => Create(body, h, t, db, c, false, cash);
    private static Task<IResult> CreateStaff(SaleWrite body, HttpContext h, CurrentTenant t, FoundationDbContext db, ISubscriptionClock c, CashRegisterService cash) => Create(body, h, t, db, c, true, cash);
    private static async Task<IResult> Create(SaleWrite body, HttpContext h, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock, bool staff, CashRegisterService cash)
    {
        var t = (await tenant.ResolveAsync())!; var actor = Actor(h.User); var key = h.Request.Headers["Idempotency-Key"].ToString();
        if (body.Quantity is < 1 or > 100 || string.IsNullOrWhiteSpace(key) || key.Length > 100 || staff && body.PaymentMethod is not ("Cash" or "Online")) return Results.UnprocessableEntity(new { message = "اختر كمية وطريقة دفع صحيحتين." });
        if (staff) {
            try { await cash.AuthorizeProductSale(body.PlayerId.HasValue); }
            catch (CashRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
            if (!body.BranchId.HasValue || !body.ExpectedAmount.HasValue || body.ExpectedAmount <= 0 || body.ExpectedAmount > 999999999 || decimal.Round(body.ExpectedAmount.Value, 2) != body.ExpectedAmount.Value)
                return Results.Problem(statusCode: 422, title: "اختر الفرع وراجع مبلغ البيع قبل تأكيد التحصيل.");
        }
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"product-key:{t.AcademyId}:{actor}:{key}"}, 0))");
        var prior = await db.ProductSales.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.RequestedByUserId == actor && x.IdempotencyKey == key);
        if (prior is not null) {
            if (prior.ProductId != body.ProductId || prior.Quantity != body.Quantity || prior.PlayerId != body.PlayerId || prior.BranchId != body.BranchId || prior.VariantId != body.VariantId || prior.SportEnrollmentId != body.SportEnrollmentId || !staff && body.ExpectedAmount.HasValue && prior.Amount != body.ExpectedAmount || staff && (prior.PaymentMethod != body.PaymentMethod || prior.Amount != body.ExpectedAmount))
                return Results.Conflict(new { message = "لا يمكن استخدام مرجع الطلب نفسه لبيانات مختلفة." });
            if (staff && body.PaymentMethod == "Cash" && !await db.CashProductPostings.AnyAsync(p => p.ProductSaleId == prior.Id && p.SessionId == body.CashSessionId && p.RequestHash == CashRegisterService.ProductHash(body)))
                return Results.Conflict(new { message = "طلب النقدي غير مطابق لقيد ورديته؛ لا يُنشأ قبض تاريخي آليًا." });
            return staff ? Results.Ok(new { prior.Id, prior.Status, amount = CashRegisterService.Money(prior.Amount), prior.Currency, replay = true }) : Results.Ok(new { prior.Id, prior.Status, prior.Amount, prior.Currency, replay = true });
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"product-stock:{t.AcademyId}:{body.ProductId}"}, 0))");
        var product = await db.SportCatalogItems.Include(x => x.Sport).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == body.ProductId && x.IsActive && x.Sport.IsActive);
        if (product?.DisplayPrice is null || string.IsNullOrWhiteSpace(product.Currency)) return Results.NotFound();
        if (product.StockQuantity < body.Quantity) return Results.Conflict(new { message = "الكمية المطلوبة غير متاحة حاليًا." });
        if (body.PlayerId.HasValue && !await db.Players.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.PlayerId && x.IsActive)) return Results.NotFound();
        if (!staff && (body.PlayerId.HasValue && !await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == t.AcademyId && x.PlayerId == body.PlayerId && x.Guardian.UserId == actor && x.IsActive) || !await db.SportEnrollments.AnyAsync(x => x.AcademyId == t.AcademyId && x.SportId == product.SportId && x.IsActive && db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.PlayerId == x.PlayerId && l.Guardian.UserId == actor && l.IsActive)))) return Results.NotFound();
        var branch = body.BranchId.HasValue ? await db.Branches.Where(x => x.AcademyId == t.AcademyId && x.Id == body.BranchId && x.IsActive).Select(x => new { x.Id, x.ArabicName }).SingleOrDefaultAsync() : null;
        if (body.BranchId.HasValue && (branch is null || !staff && !await db.SportEnrollments.AnyAsync(x => x.AcademyId == t.AcademyId && x.BranchId == branch.Id && x.SportId == product.SportId && x.IsActive && db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.PlayerId == x.PlayerId && l.Guardian.UserId == actor && l.IsActive)))) return Results.NotFound();
        if (!staff && body.PlayerId.HasValue && !await db.SportEnrollments.AnyAsync(x => x.AcademyId == t.AcademyId && x.PlayerId == body.PlayerId && x.SportId == product.SportId && (!body.BranchId.HasValue || x.BranchId == body.BranchId) && x.IsActive)) return Results.NotFound();
        Academy.Infrastructure.Content.ProductStockVariant? variant=null;
        if(product.UsesVariants) {
            if(!body.VariantId.HasValue||!staff&&(!body.PlayerId.HasValue||!body.SportEnrollmentId.HasValue))return Results.Problem(statusCode:422,title:"اختر الطفل والمقاس ورقم القميص إن كان مرقمًا قبل الطلب.");
            variant=await db.ProductStockVariants.SingleOrDefaultAsync(v=>v.Id==body.VariantId&&v.ProductId==product.Id&&v.IsActive);
            if(variant is null)return Results.NotFound();
            if(variant.Quantity<body.Quantity)return Results.Problem(statusCode:409,title:"المقاس أو الرقم المختار غير متاح بالكمية المطلوبة. اختر المتاح بنفسك.");
        } else if(body.VariantId.HasValue)return Results.Problem(statusCode:422,title:"هذا المنتج بلا مقاسات مخزون؛ حدّث تفاصيله قبل الطلب.");
        if(body.SportEnrollmentId.HasValue && (!body.PlayerId.HasValue||!await db.SportEnrollments.AnyAsync(e=>e.Id==body.SportEnrollmentId&&e.PlayerId==body.PlayerId&&e.SportId==product.SportId&&e.BranchId==body.BranchId&&e.IsActive)))return Results.NotFound();
        var unit = decimal.Round(product.DisplayPrice.Value * (100 - (product.DiscountPercentage ?? 0)) / 100, 2, MidpointRounding.AwayFromZero);
        if (!staff && body.ExpectedAmount.HasValue && body.ExpectedAmount != unit * body.Quantity) return Results.Problem(statusCode:409,title:"تغير سعر المنتج؛ حدّث المراجعة قبل تأكيد الطلب.");
        if (staff && body.ExpectedAmount != unit * body.Quantity) return Results.Conflict(new { message = "تغير السعر أو الخصم؛ حدّث مراجعة المبلغ قبل التحصيل." });
        if (staff && body.PaymentMethod == "Cash") {
            try { await cash.PrepareProductSale(body.CashSessionId, branch!.Id, product.Currency); }
            catch (CashRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
        }
        var row = new ProductSale { Id = Guid.NewGuid(), AcademyId = t.AcademyId, ProductId = product.Id, RequestedByUserId = actor, PlayerId = body.PlayerId, VariantId = variant?.Id, SportEnrollmentId = body.SportEnrollmentId, SizeSnapshot = variant?.Size, ColorSnapshot = variant?.Color, ShirtNumberSnapshot = variant?.ShirtNumber, Quantity = body.Quantity, UnitPrice = unit, Amount = unit * body.Quantity, Currency = product.Currency, ProductName = product.ArabicName, SportId = product.SportId, SportName = product.Sport.ArabicName, BranchId = branch?.Id, BranchName = branch?.ArabicName, Status = staff ? "Confirmed" : "Pending", PaymentMethod = staff ? body.PaymentMethod : null, ConfirmedAtUtc = staff ? clock.UtcNow : null, ConfirmedByUserId = staff ? actor : null, IdempotencyKey = key, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        if (staff) { product.StockQuantity -= body.Quantity; product.StockRevision++; product.UpdatedAtUtc = clock.UtcNow; if(variant is not null){variant.Quantity-=body.Quantity;variant.UpdatedAtUtc=clock.UtcNow;} }
        db.ProductSales.Add(row); await db.SaveChangesAsync();
        if (staff && body.PaymentMethod == "Cash") {
            try { await cash.PostProductSale(body.CashSessionId!.Value, row.Id, CashRegisterService.ProductHash(body)); }
            catch (CashRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); }
        }
        await tx.CommitAsync(); return staff ? Results.Ok(new { row.Id, row.Status, amount = CashRegisterService.Money(row.Amount), row.Currency }) : Results.Ok(new { row.Id, row.Status, row.Amount, row.Currency });
    }
    private static async Task<IResult> Proof(Guid id, ProofWrite body, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var user = Actor(principal); await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"product-review:{t.AcademyId}:{id}"}, 0))");
        var sale = await db.ProductSales.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.RequestedByUserId == user);
        if (sale is null || !await db.PrivateAssets.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.AssetId && x.UploadedByUserId == user && x.Purpose == "Proof")) return Results.NotFound();
        if (sale.ProofAssetId == body.AssetId) return Results.Ok(new { message = "إثبات الدفع محفوظ لهذا الطلب." });
        if (sale.Status != "Pending") return Results.Conflict(); sale.ProofAssetId = body.AssetId; sale.UpdatedAtUtc = clock.UtcNow; await NoticeText.PaymentProof(db,t.AcademyId,id,"شراء منتج رياضي",clock.UtcNow,productOrder:true); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { message = "وصل إثبات الدفع. ستُراجع الإدارة الطلب وتؤكد توافر المنتج." });
    }
    private static async Task<IResult> Details(Guid id, CurrentTenant tenant, FoundationDbContext db, HttpContext http, IAuthorizationService authorization, CashRegisterService cash)
    {
        http.Response.Headers.CacheControl = "no-store";
        var t = (await tenant.ResolveAsync())!;
        var canReview = t.Role == AcademyRole.AcademyOwner ||
            (await authorization.AuthorizeAsync(http.User, http, AcademyPermissions.SportCatalogManage)).Succeeded &&
            (!await db.AcademyMemberships.AnyAsync(m => m.Id == t.MembershipId && m.UseActionGrants) ||
             await db.StaffActionGrants.AnyAsync(g => g.MembershipId == t.MembershipId && g.Module == "catalog" && g.IsActive && g.Read && g.Edit));
        var canAccept = false;
        if (canReview)
        {
            try { await cash.AuthorizeProductSale(review: true); canAccept = true; }
            catch (CashRejected) { /* Reading and rejecting do not imply authority to collect. */ }
        }
        var row = await db.ProductSales.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == id)
            .Select(x => new {
                x.Id, x.ProductId, x.BranchId, x.BranchName, x.ProductName,
                player = db.Players.Where(p => p.Id == x.PlayerId).Select(p => p.ArabicName).FirstOrDefault(),
                x.PlayerId, x.SportEnrollmentId, x.VariantId, x.SizeSnapshot, x.ColorSnapshot, x.ShirtNumberSnapshot,
                x.Quantity, x.UnitPrice, x.Amount, x.Currency, x.Status, x.ReviewReason, x.PaymentMethod, x.ConfirmedAtUtc, x.CreatedAtUtc,
                timeZone = db.Academies.Where(a => a.Id == t.AcademyId).Select(a => a.TimeZone).First(),
                cashSessionId = db.CashProductPostings.Where(p => p.ProductSaleId == x.Id).Select(p => (Guid?)p.SessionId).FirstOrDefault(),
                canReview = x.Status == "Pending" && canReview, canAccept = x.Status == "Pending" && canAccept, x.ProofAssetId,
                proofUrl = x.ProofAssetId == null ? null : "/api/v1/assets/" + x.ProofAssetId
            }).SingleOrDefaultAsync();
        if (row is null) return Results.NotFound();
        return Results.Ok(new
        {
            row.Id, row.ProductId, row.BranchId, row.BranchName, row.ProductName, row.player,
            row.PlayerId, row.SportEnrollmentId, row.VariantId, row.SizeSnapshot, row.ColorSnapshot, row.ShirtNumberSnapshot,
            row.Quantity, row.UnitPrice, row.Amount, row.Currency, row.Status, row.ReviewReason, row.PaymentMethod, row.ConfirmedAtUtc,
            row.timeZone, row.cashSessionId, row.canReview, row.canAccept, row.ProofAssetId, row.proofUrl,
            reference = PaymentQueueItem.ShortReference(row.Id),
            createdOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(row.CreatedAtUtc, TimeZoneInfo.FindSystemTimeZoneById(row.timeZone)).DateTime)
        });
    }
    private static async Task<IResult> Review(Guid id, ProductReviewWrite body, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock, CashRegisterService cash)
    {
        var t = (await tenant.ResolveAsync())!; var user = Actor(principal); if (body.Reason?.Length > 500) return Results.UnprocessableEntity();
        if (body.Accept) { try { await cash.AuthorizeProductSale(review: true); } catch (CashRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); } }
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"product-review:{t.AcademyId}:{id}"}, 0))");
        var sale = await db.ProductSales.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (sale is null) return Results.NotFound();
        var reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim();
        if (body.ExpectedProofAssetId.HasValue && sale.ProofAssetId != body.ExpectedProofAssetId)
            return Results.Conflict(new { message = "تغيّر إثبات الدفع. افتح الإثبات الحالي قبل اتخاذ القرار." });
        if (sale.Status != "Pending")
        {
            var savedReason = string.IsNullOrWhiteSpace(sale.ReviewReason) ? null : sale.ReviewReason.Trim();
            return sale.Status == (body.Accept ? "Confirmed" : "Rejected") && (!body.ExpectedProofAssetId.HasValue || savedReason == reason)
                ? Results.Ok(new { replay = true }) : Results.Conflict(new { message = "تمت مراجعة الطلب بالفعل. اقرأ القرار المحفوظ." });
        }
        if (!sale.ProofAssetId.HasValue) return Results.Conflict(new { message = "لم يصل إثبات دفع بعد." });
        if (body.Accept) {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"product-stock:{t.AcademyId}:{sale.ProductId}"}, 0))");
            var product = await db.SportCatalogItems.Include(x => x.Sport).SingleAsync(x => x.AcademyId == t.AcademyId && x.Id == sale.ProductId);
            if (!product.IsActive || !product.Sport.IsActive || product.StockQuantity < sale.Quantity) return Results.Conflict(new { message = "المنتج متوقف أو المخزون غير كافٍ. لا تؤكد الطلب؛ راجع التحويل مع ولي الأمر أولًا." });
            if(product.UsesVariants && !sale.VariantId.HasValue)return Results.Problem(statusCode:409,title:"تغير مخزون المنتج إلى مقاسات. راجع الطلب القديم مع ولي الأمر دون تبديل تلقائي.");
            if(sale.VariantId.HasValue){var variant=await db.ProductStockVariants.SingleOrDefaultAsync(v=>v.Id==sale.VariantId&&v.ProductId==product.Id&&v.IsActive);
                if(variant is null||variant.Quantity<sale.Quantity||variant.Size!=sale.SizeSnapshot||variant.Color!=(sale.ColorSnapshot??"")||variant.ShirtNumber!=sale.ShirtNumberSnapshot)return Results.Problem(statusCode:409,title:"المقاس أو رقم القميص الذي اختاره ولي الأمر غير متاح. لا تبدله تلقائيًا.");
                variant.Quantity-=sale.Quantity;variant.UpdatedAtUtc=clock.UtcNow;
            }
            product.StockQuantity -= sale.Quantity; product.StockRevision++; product.UpdatedAtUtc = clock.UtcNow; sale.PaymentMethod = "Online"; sale.ConfirmedAtUtc = clock.UtcNow; sale.ConfirmedByUserId = user;
        }
        sale.Status = body.Accept ? "Confirmed" : "Rejected"; sale.ReviewReason = reason; sale.UpdatedAtUtc = clock.UtcNow;
        db.AcademyNotices.Add(new AcademyNotice { EventKey="Payment", Id = Guid.NewGuid(), AcademyId = t.AcademyId, UserId = sale.RequestedByUserId, AudienceRole="Guardian",TargetKind="Product",TargetId=sale.Id, Message = (await NoticeText.Render(db,t.AcademyId,"Payment","تحديث طلب المنتج الرياضي."))+"\n"+(body.Accept ? "تم قبول دفع المنتج الرياضي. تواصل مع الأكاديمية للاستلام." : "لم يُقبل طلب المنتج. " + (reason ?? "تواصل مع الأكاديمية للتوضيح.")), CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { sale.Status });
    }
}
public sealed record ProductReviewWrite(bool Accept, string? Reason, Guid? ExpectedProofAssetId = null);
public sealed record SaleWrite(Guid ProductId, int Quantity, Guid? PlayerId, string? PaymentMethod, Guid? BranchId = null,
    [property: System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)] decimal? ExpectedAmount = null, Guid? CashSessionId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Guid? VariantId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Guid? SportEnrollmentId = null);
