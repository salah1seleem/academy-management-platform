using System.Security.Claims;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Operations;
using Academy.Api.Auth;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static partial class ProductSales
{
    private static IQueryable<ProductSale> GuardianSales(FoundationDbContext db, Guid academy, Guid user) =>
        db.ProductSales.AsNoTracking().Where(s => s.AcademyId == academy && s.RequestedByUserId == user);

    private static IQueryable<GuardianSaleRow> ProjectGuardianSales(IQueryable<ProductSale> sales, FoundationDbContext db, Guid academy) =>
        sales.Select(s => new GuardianSaleRow(s.Id, s.ProductId, s.BranchId, s.ProductName,
                db.Players.Where(p => p.AcademyId == academy && p.Id == s.PlayerId).Select(p => p.ArabicName).FirstOrDefault(),
                s.PlayerId, s.SportEnrollmentId, s.VariantId, s.SizeSnapshot, s.ColorSnapshot, s.ShirtNumberSnapshot,
                s.Quantity, s.Amount, s.Currency, s.Status, s.ReviewReason, s.ConfirmedAtUtc, s.ProofAssetId != null, s.CreatedAtUtc));

    private static async Task<IResult> OwnSales(int? page, int? pageSize, CurrentTenant tenant,
        ClaimsPrincipal principal, FoundationDbContext db, HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        var legacy = page is null && pageSize is null;
        var number = page ?? 1;
        var size = pageSize ?? (legacy ? 100 : 25);
        if (number is < 1 or > 100000 || size is < 1 or > 100)
            return Results.UnprocessableEntity(new { message = "اختر رقم صفحة وحجم صفحة صحيحين." });
        var context = (await tenant.ResolveAsync())!;
        var query = GuardianSales(db, context.AcademyId, Actor(principal))
            .OrderByDescending(s => s.CreatedAtUtc).ThenBy(s => s.Id)
            .Skip((number - 1) * size).Take(legacy ? size : size + 1);
        var sales = await ProjectGuardianSales(query, db, context.AcademyId).ToArrayAsync();
        var zone = await GuardianOrderZone(db, context.AcademyId);
        var items = sales.Take(size).Select(s => s.ToResponse(zone)).ToArray();
        return legacy ? Results.Ok(items) : Results.Ok(new
        {
            page = number, pageSize = size, hasMore = sales.Length > size, items
        });
    }

    private static async Task<IResult> OwnSale(Guid id, CurrentTenant tenant, ClaimsPrincipal principal,
        FoundationDbContext db, HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        var context = (await tenant.ResolveAsync())!;
        var query = GuardianSales(db, context.AcademyId, Actor(principal)).Where(s => s.Id == id);
        var row = await ProjectGuardianSales(query, db, context.AcademyId).SingleOrDefaultAsync();
        if (row is null) return Results.NotFound();
        return Results.Ok(row.ToResponse(await GuardianOrderZone(db, context.AcademyId)));
    }

    private static async Task<TimeZoneInfo> GuardianOrderZone(FoundationDbContext db, Guid academy) =>
        TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a => a.Id == academy).Select(a => a.TimeZone).SingleAsync());

    private sealed record GuardianSaleRow(Guid Id, Guid ProductId, Guid? BranchId, string ProductName, string? Player,
        Guid? PlayerId, Guid? SportEnrollmentId, Guid? VariantId, string? SizeSnapshot, string? ColorSnapshot, int? ShirtNumberSnapshot,
        int Quantity, decimal Amount, string Currency, string Status, string? ReviewReason, DateTimeOffset? ConfirmedAtUtc,
        bool HasProof, DateTimeOffset CreatedAtUtc)
    {
        public object ToResponse(TimeZoneInfo zone) => new
        {
            Id, ProductId, BranchId, ProductName, Player, PlayerId, SportEnrollmentId, VariantId, SizeSnapshot,
            ColorSnapshot, ShirtNumberSnapshot, Quantity, Amount, Currency, Status, ReviewReason, ConfirmedAtUtc, HasProof,
            reference = PaymentQueueItem.ShortReference(Id),
            createdOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(CreatedAtUtc, zone).DateTime)
        };
    }

    private static async Task<IResult> ListSales(string? status, string? search, int? page, int? pageSize,
        Guid? branchId, DateOnly? from, DateOnly? to, CurrentTenant tenant, FoundationDbContext db)
    {
        var number = page ?? 1;
        var size = pageSize ?? 25;
        if (number < 1 || number > 100000 || size is < 1 or > 100 || search?.Length > 100 ||
            status is not (null or "" or "Pending" or "Confirmed" or "Rejected") || from > to)
            return Results.UnprocessableEntity(new { message = "راجع حالة الطلب والبحث والفترة ورقم الصفحة." });
        var context = (await tenant.ResolveAsync())!;
        var query = db.ProductSales.AsNoTracking().Where(s => s.AcademyId == context.AcademyId);
        if (!string.IsNullOrEmpty(status)) query = query.Where(s => s.Status == status);
        if (branchId.HasValue) query = query.Where(s => s.BranchId == branchId);
        if (from.HasValue || to.HasValue)
        {
            var zone = await db.Academies.Where(a => a.Id == context.AcademyId).Select(a => a.TimeZone).SingleAsync();
            try
            {
                if (from.HasValue)
                {
                    var start = Slice8.ReportingEndpoints.UtcRange(from.Value, from.Value, zone).Start;
                    query = query.Where(s => s.CreatedAtUtc >= start);
                }
                if (to.HasValue)
                {
                    var end = Slice8.ReportingEndpoints.UtcRange(to.Value, to.Value, zone).EndExclusive;
                    query = query.Where(s => s.CreatedAtUtc < end);
                }
            }
            catch (ArgumentException) { return Results.UnprocessableEntity(new { message = "اختر فترة صحيحة للطلبات." }); }
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (Guid.TryParseExact(term, "D", out var id))
                query = query.Where(s => s.Id == id);
            else if (PaymentQueueItem.IsShortReference(term))
            {
                var prefix = term[..6].ToLowerInvariant();
                var suffix = term[6..].ToLowerInvariant();
                query = query.Where(s => s.Id.ToString().StartsWith(prefix) && s.Id.ToString().EndsWith(suffix));
            }
            else
                query = query.Where(s => s.ProductName.Contains(term) || db.Players.Any(p => p.Id == s.PlayerId && p.ArabicName.Contains(term)));
        }
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(s => s.CreatedAtUtc).ThenBy(s => s.Id)
            .Skip((number - 1) * size).Take(size).Select(s => new
            {
                s.Id, s.ProductName, s.PlayerId,
                player = db.Players.Where(p => p.Id == s.PlayerId).Select(p => p.ArabicName).FirstOrDefault(),
                s.SportName, s.BranchId, s.BranchName, s.SizeSnapshot, s.ShirtNumberSnapshot,
                s.Quantity, s.Amount, s.Currency, s.Status, hasProof = s.ProofAssetId != null, s.CreatedAtUtc
            }).ToArrayAsync();
        var academyZone = await GuardianOrderZone(db, context.AcademyId);
        return Results.Ok(new
        {
            page = number, pageSize = size, totalCount = total,
            items = items.Select(s => new
            {
                s.Id, s.ProductName, s.PlayerId, s.player, s.SportName, s.BranchId, s.BranchName, s.SizeSnapshot, s.ShirtNumberSnapshot,
                s.Quantity, s.Amount, s.Currency, s.Status, s.hasProof, s.CreatedAtUtc,
                reference = PaymentQueueItem.ShortReference(s.Id),
                createdOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.CreatedAtUtc, academyZone).DateTime)
            })
        });
    }
}
