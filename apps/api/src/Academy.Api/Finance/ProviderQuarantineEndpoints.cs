using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public static class ProviderQuarantineEndpoints
{
    private static readonly TimeZoneInfo Cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    public static void MapProviderQuarantine(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/finance/provider-quarantine").RequireAuthorization(AcademyPermissions.ReportsFinancialRead);
        api.MapGet("/", async (DateOnly? from, DateOnly? to, int? page, int? pageSize, bool? monthToDate, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock) => {
            var t = (await tenant.ResolveAsync())!;
            if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
            var size = pageSize ?? 25;
            if (from > to || page is < 1 or > 100000 || size is not (10 or 25 or 50) ||
                monthToDate == true && (from.HasValue || to.HasValue))
                return Results.Json(new { title = "اختر فترة وصفحة صحيحتين." }, statusCode: 422);
            if (monthToDate == true)
            {
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Cairo).DateTime);
                from = new DateOnly(today.Year, today.Month, 1);
                to = today;
            }
            var query = db.ProviderEventQuarantines.AsNoTracking().Where(q => q.AcademyId == t.AcademyId);
            if (from is { } start) { var at = DayStart(start); query = query.Where(q => q.CreatedAtUtc >= at); }
            if (to is { } end) { if (end == DateOnly.MaxValue) return Results.Json(new { title = "تاريخ النهاية غير صالح." }, statusCode: 422); var at = DayStart(end.AddDays(1)); query = query.Where(q => q.CreatedAtUtc < at); }
            var count = await query.CountAsync();
            var lastPage = count == 0 ? 1 : (count - 1) / size + 1;
            var index = Math.Min(page ?? 1, lastPage);
            var items = await Project(query.OrderByDescending(q => q.CreatedAtUtc).ThenBy(q => q.Id).Skip((index - 1) * size).Take(size), db).ToListAsync();
            return Results.Ok(new { total = count, page = index, pageSize = size, from, to, items });
        });
        api.MapGet("/{id:guid}", async (Guid id, CurrentTenant tenant, FoundationDbContext db) => {
            var t = (await tenant.ResolveAsync())!;
            if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
            var row = await Project(db.ProviderEventQuarantines.AsNoTracking().Where(q => q.AcademyId == t.AcademyId && q.Id == id), db).SingleOrDefaultAsync();
            return row is null ? Results.NotFound() : Results.Ok(row);
        });
    }
    private static DateTimeOffset DayStart(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Cairo can advance at midnight. Start at its first valid local instant.
        while (Cairo.IsInvalidTime(local)) local = local.AddMinutes(1);
        return new(TimeZoneInfo.ConvertTimeToUtc(local, Cairo));
    }
    private static IQueryable<ProviderIssueRead> Project(IQueryable<ProviderEventQuarantine> query, FoundationDbContext db) => query.Select(q => new ProviderIssueRead(
        q.Id, q.PaymentRequestId, q.Provider, q.ProviderEventId, q.ReasonCode, q.ReceivedStatus, q.ReceivedReference, q.ExpectedReference,
        CashRegisterService.Money(q.ReceivedAmount), CashRegisterService.Money(q.ExpectedAmount), q.ReceivedCurrency, q.ExpectedCurrency, q.CreatedAtUtc,
        q.PaymentRequest.Status.ToString(), q.PaymentRequest.RenewalRequest.SportEnrollment.Player.ArabicName,
        q.PaymentRequest.RenewalRequest.SportEnrollment.Branch.ArabicName,
        db.Receipts.Where(r => r.Collection.PaymentRequestId == q.PaymentRequestId).Select(r => (Guid?)r.Id).SingleOrDefault()));
    public sealed record ProviderIssueRead(Guid Id, Guid PaymentRequestId, string Provider, string ProviderEventId, string ReasonCode, string ReceivedStatus,
        string ReceivedReference, string ExpectedReference, string ReceivedAmount, string ExpectedAmount, string ReceivedCurrency, string ExpectedCurrency,
        DateTimeOffset CreatedAtUtc, string PaymentStatus, string Player, string Branch, Guid? ReceiptId)
    {
        // Local wall time for the same Cairo date boundary used by the list.
        public DateTime ObservedAtCairo => TimeZoneInfo.ConvertTime(CreatedAtUtc, Cairo).DateTime;
    }
}
