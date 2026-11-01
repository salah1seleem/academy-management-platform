using Academy.Infrastructure.Persistence;
using Academy.Api.Auth;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static partial class ProductSales
{
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
        return Results.Ok(new { page = number, pageSize = size, totalCount = total, items });
    }
}
