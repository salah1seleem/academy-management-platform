using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice3;

public static class Slice3Endpoints
{
    public static void MapSlice3Endpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        var admin = api.MapGroup("/subscriptions").RequireAuthorization(AcademyPermissions.SubscriptionRead);
        admin.MapGet("/plans", Plans);
        admin.MapGet("/plans/{id:guid}", async (Guid id, CurrentTenant tenant, FoundationDbContext db) =>
        {
            var t = (await tenant.ResolveAsync())!;
            var item = await db.SubscriptionPlans.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == id).Select(x => new { x.Id, x.ArabicName, x.EnglishName, x.SportId, sportName = x.Sport.ArabicName, planType = x.PlanType.ToString(), x.Price, x.Currency, x.DurationDays, x.SessionCount, x.DisplayOrder, x.IsActive }).SingleOrDefaultAsync();
            return item is null ? Results.NotFound() : Results.Ok(item);
        });
        admin.MapPost("/plans", CreatePlan).RequireAuthorization(AcademyPermissions.SubscriptionPlanManage).AddEndpointFilter<CsrfFilter>();
        admin.MapPut("/plans/{id:guid}", UpdatePlan).RequireAuthorization(AcademyPermissions.SubscriptionPlanManage).AddEndpointFilter<CsrfFilter>();
        admin.MapPut("/plans/{id:guid}/status", async (Guid id, PlanStatusRequest request, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock) =>
        {
            var t = (await tenant.ResolveAsync())!; var plan = await db.SubscriptionPlans.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id);
            if (plan is null) return Results.NotFound(); plan.IsActive = request.IsActive; plan.UpdatedAtUtc = clock.UtcNow; await db.SaveChangesAsync(); return Results.NoContent();
        }).RequireAuthorization(AcademyPermissions.SubscriptionPlanManage).AddEndpointFilter<CsrfFilter>();

        admin.MapGet("/periods", async (string? state, int? days, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock) =>
        {
            var t = (await tenant.ResolveAsync())!; var today = clock.Today; var windowEnd = today.AddDays(Math.Clamp(days ?? 7, 1, 90));
            var q = db.SubscriptionPeriods.AsNoTracking().Where(x => x.AcademyId == t.AcademyId);
            q = state?.ToLowerInvariant() switch { "active" => q.Where(x => x.StartDate <= today && (x.EndDate == null || x.EndDate >= today) && x.Status != SubscriptionPeriodStatus.Cancelled), "expiring" => q.Where(x => x.EndDate >= today && x.EndDate <= windowEnd && x.Status != SubscriptionPeriodStatus.Cancelled), "expired" => q.Where(x => x.EndDate < today && x.Status != SubscriptionPeriodStatus.Cancelled), _ => q };
            return Results.Ok(await q.OrderByDescending(x => x.StartDate).Select(x => new { x.Id, player = x.SportEnrollment.Player.ArabicName, sport = x.SubscriptionPlan.Sport.ArabicName, plan = x.SubscriptionPlan.ArabicName, x.StartDate, x.EndDate, x.RemainingSessions, status = x.EndDate < today ? "Expired" : x.StartDate > today ? "Scheduled" : x.Status.ToString() }).ToListAsync());
        });
        admin.MapGet("/renewals", async (CurrentTenant tenant, FoundationDbContext db) => { var t = (await tenant.ResolveAsync())!; return Results.Ok(await db.RenewalRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId).OrderByDescending(x => x.RequestedAtUtc).Select(x => new { x.Id, player = x.SportEnrollment.Player.ArabicName, sport = x.SubscriptionPlan.Sport.ArabicName, plan = x.SubscriptionPlan.ArabicName, guardian = db.Users.Where(u => u.Id == x.RequestedByUserId).Select(u => u.DisplayName).FirstOrDefault(), amount = x.AmountExpected, x.Currency, status = x.Status.ToString(), x.RequestedAtUtc }).ToListAsync()); });
        admin.MapGet("/payments", async (CurrentTenant tenant, FoundationDbContext db) => { var t = (await tenant.ResolveAsync())!; return Results.Ok(await db.PaymentRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId).OrderByDescending(x => x.CreatedAtUtc).Select(x => new { x.Id, reference = x.ProviderReference, player = x.RenewalRequest.SportEnrollment.Player.ArabicName, plan = x.RenewalRequest.SubscriptionPlan.ArabicName, x.Provider, x.Amount, x.Currency, status = x.Status.ToString(), x.CreatedAtUtc, x.ConfirmedAtUtc }).ToListAsync()); }).RequireAuthorization(AcademyPermissions.PaymentRead);
        admin.MapGet("/collections", async (CurrentTenant tenant, FoundationDbContext db) => { var t = (await tenant.ResolveAsync())!; var rows = await db.Collections.AsNoTracking().Where(x => x.AcademyId == t.AcademyId).OrderByDescending(x => x.ConfirmedAtUtc).Select(x => new { x.Id, receiptId = db.Receipts.Where(r => r.AcademyId == t.AcademyId && r.CollectionId == x.Id).Select(r => r.Id).Single(), receiptNumber = db.Receipts.Where(r => r.AcademyId == t.AcademyId && r.CollectionId == x.Id).Select(r => r.ReceiptNumber).Single(), player = x.SportEnrollment.Player.ArabicName, sport = x.SportEnrollment.Sport.ArabicName, x.Amount, x.Currency, x.Provider, x.ConfirmedAtUtc }).ToListAsync(); return Results.Ok(new { total = rows.Sum(x => x.Amount), items = rows }); }).RequireAuthorization(AcademyPermissions.CollectionRead);
        admin.MapGet("/receipts/{receiptId:guid}", async (Guid receiptId, CurrentTenant tenant, FoundationDbContext db) => { var t = (await tenant.ResolveAsync())!; var receipt = await db.Receipts.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == receiptId).Select(x => new { x.Id, x.ReceiptNumber, x.PlayerNameSnapshot, x.SportNameSnapshot, x.PlanNameSnapshot, x.Amount, x.Currency, x.PaidAtUtc, x.PaymentMethod, x.ProviderReference }).SingleOrDefaultAsync(); return receipt is null ? Results.NotFound() : Results.Ok(receipt); }).RequireAuthorization(AcademyPermissions.CollectionRead);

        var guardian = api.MapGroup("/guardian/subscriptions").RequireAuthorization(AcademyPermissions.GuardianOwnRenewal);
        guardian.MapGet("/enrollments", GuardianEnrollments);
        guardian.MapGet("/enrollments/{enrollmentId:guid}/plans", GuardianPlans);
        guardian.MapPost("/renewals", CreateRenewal).AddEndpointFilter<CsrfFilter>();
        guardian.MapGet("/payments/{paymentId:guid}", GuardianPayment);
        guardian.MapGet("/receipts/{receiptId:guid}", GuardianReceipt);

        if (app.Environment.IsEnvironment("Demo") || app.Environment.IsEnvironment("Testing"))
        {
            guardian.MapPost("/payments/{paymentId:guid}/simulate", Simulate).AddEndpointFilter<CsrfFilter>();
            api.MapPost("/payments/internal-test/events", async (ProviderEventEnvelope value, PaymentProcessor processor) => { var result = await processor.ProcessAsync(value); return result.Accepted ? Results.Ok(result) : Results.BadRequest(result); });
        }
    }

    private static async Task<IResult> Plans(CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; return Results.Ok(await db.SubscriptionPlans.AsNoTracking().Where(x => x.AcademyId == t.AcademyId).OrderBy(x => x.DisplayOrder).ThenBy(x => x.ArabicName).Select(x => new { x.Id, x.ArabicName, x.EnglishName, sport = x.Sport.ArabicName, x.SportId, planType = x.PlanType.ToString(), x.Price, x.Currency, x.DurationDays, x.SessionCount, x.IsActive }).ToListAsync());
    }

    private static async Task<IResult> CreatePlan(PlanRequest request, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var error = await ValidatePlan(request, t.AcademyId, db); if (error is not null) return error;
        var plan = new SubscriptionPlan { Id = Guid.NewGuid(), AcademyId = t.AcademyId, ArabicName = request.ArabicName.Trim(), EnglishName = request.EnglishName?.Trim(), SportId = request.SportId, PlanType = request.PlanType, Price = request.Price, Currency = "EGP", DurationDays = request.DurationDays, SessionCount = request.SessionCount, DisplayOrder = request.DisplayOrder, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        db.SubscriptionPlans.Add(plan); await db.SaveChangesAsync(); return Results.Created($"/api/v1/subscriptions/plans/{plan.Id}", new { plan.Id });
    }
    private static async Task<IResult> UpdatePlan(Guid id, PlanRequest request, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var plan = await db.SubscriptionPlans.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (plan is null) return Results.NotFound(); var error = await ValidatePlan(request, t.AcademyId, db); if (error is not null) return error;
        plan.ArabicName = request.ArabicName.Trim(); plan.EnglishName = request.EnglishName?.Trim(); plan.SportId = request.SportId; plan.PlanType = request.PlanType; plan.Price = request.Price; plan.DurationDays = request.DurationDays; plan.SessionCount = request.SessionCount; plan.DisplayOrder = request.DisplayOrder; plan.UpdatedAtUtc = clock.UtcNow; await db.SaveChangesAsync(); return Results.NoContent();
    }
    private static async Task<IResult?> ValidatePlan(PlanRequest r, Guid academy, FoundationDbContext db)
    {
        var validType = r.PlanType switch { SubscriptionPlanType.Duration => r.DurationDays > 0 && r.SessionCount is null, SubscriptionPlanType.Sessions => r.DurationDays is null && r.SessionCount > 0, SubscriptionPlanType.Combined => r.DurationDays > 0 && r.SessionCount > 0, _ => false };
        if (string.IsNullOrWhiteSpace(r.ArabicName) || r.Price <= 0 || !validType) return Results.ValidationProblem(new Dictionary<string, string[]> { ["plan"] = ["بيانات الباقة أو حدود المدة/الحصص غير صالحة."] });
        if (!await db.Sports.AnyAsync(x => x.AcademyId == academy && x.Id == r.SportId && x.IsActive)) return Results.UnprocessableEntity(new { message = "الرياضة خارج نطاق الأكاديمية." }); return null;
    }

    private static async Task<IResult> GuardianEnrollments(CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var today = clock.Today;
        var ids = db.GuardianPlayerLinks.Where(l => l.AcademyId == t.AcademyId && l.Guardian.UserId == user && l.IsActive).Select(l => l.PlayerId);
        var items = await db.SportEnrollments.AsNoTracking().Where(e => e.AcademyId == t.AcademyId && ids.Contains(e.PlayerId) && e.IsActive).OrderBy(e => e.Player.ArabicName).ThenBy(e => e.Sport.ArabicName).Select(e => new { e.Id, playerId = e.PlayerId, player = e.Player.ArabicName, sport = e.Sport.ArabicName, sportId = e.SportId, period = db.SubscriptionPeriods.Where(p => p.AcademyId == t.AcademyId && p.SportEnrollmentId == e.Id && p.Status != SubscriptionPeriodStatus.Cancelled).OrderByDescending(p => p.EndDate ?? DateOnly.MaxValue).Select(p => new { plan = p.SubscriptionPlan.ArabicName, p.StartDate, p.EndDate, p.RemainingSessions, status = p.EndDate < today ? "Expired" : p.StartDate > today ? "Scheduled" : "Active" }).FirstOrDefault() }).ToListAsync();
        return Results.Ok(items);
    }
    private static async Task<IResult> GuardianPlans(Guid enrollmentId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var enrollment = await OwnEnrollment(db, t.AcademyId, user, enrollmentId); if (enrollment is null) return Results.NotFound();
        return Results.Ok(await db.SubscriptionPlans.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.SportId == enrollment.SportId && x.IsActive).OrderBy(x => x.DisplayOrder).Select(x => new { x.Id, x.ArabicName, planType = x.PlanType.ToString(), x.Price, x.Currency, x.DurationDays, x.SessionCount }).ToListAsync());
    }
    private static async Task<IResult> CreateRenewal(RenewalCreateRequest request, HttpContext http, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, IPaymentGateway gateway, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var key = http.Request.Headers["Idempotency-Key"].ToString(); if (string.IsNullOrWhiteSpace(key) || key.Length > 100) return Results.BadRequest(new { message = "Idempotency-Key مطلوب." });
        var existing = await db.RenewalRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.RequestedByUserId == user && x.IdempotencyKey == key).Select(x => new { renewalId = x.Id, paymentId = x.PaymentRequestId }).SingleOrDefaultAsync(); if (existing is not null) return Results.Ok(existing);
        var enrollment = await OwnEnrollment(db, t.AcademyId, user, request.SportEnrollmentId); if (enrollment is null) return Results.NotFound();
        var plan = await db.SubscriptionPlans.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == request.SubscriptionPlanId && x.SportId == enrollment.SportId && x.IsActive); if (plan is null) return Results.NotFound();
        await using var tx = await db.Database.BeginTransactionAsync();
        var renewal = new RenewalRequest { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SportEnrollmentId = enrollment.Id, SportId = enrollment.SportId, SubscriptionPlanId = plan.Id, RequestedByUserId = user, RequestedAtUtc = clock.UtcNow, AmountExpected = plan.Price, Currency = plan.Currency, Status = RenewalRequestStatus.PaymentInProgress, IdempotencyKey = key, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        var paymentId = Guid.NewGuid(); var session = gateway.CreateCheckout(t.AcademyId, paymentId); var payment = new PaymentRequest { Id = paymentId, AcademyId = t.AcademyId, RenewalRequestId = renewal.Id, Provider = session.Provider, ProviderEnvironment = "Demo", Amount = plan.Price, Currency = plan.Currency, Status = PaymentRequestStatus.Pending, ProviderReference = session.ProviderReference, CheckoutReference = session.CheckoutReference, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        renewal.PaymentRequestId = payment.Id; db.RenewalRequests.Add(renewal); db.PaymentRequests.Add(payment); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Created($"/api/v1/guardian/subscriptions/payments/{payment.Id}", new { renewalId = renewal.Id, paymentId = payment.Id });
    }
    private static async Task<IResult> GuardianPayment(Guid paymentId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var p = await db.PaymentRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == paymentId && x.RenewalRequest.RequestedByUserId == user && db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.Guardian.UserId == user && l.PlayerId == x.RenewalRequest.SportEnrollment.PlayerId && l.IsActive)).Select(x => new { x.Id, x.ProviderReference, x.Amount, x.Currency, status = x.Status.ToString(), player = x.RenewalRequest.SportEnrollment.Player.ArabicName, sport = x.RenewalRequest.SubscriptionPlan.Sport.ArabicName, plan = x.RenewalRequest.SubscriptionPlan.ArabicName, receiptId = db.Receipts.Where(r => r.AcademyId == t.AcademyId && r.Collection.PaymentRequestId == x.Id).Select(r => (Guid?)r.Id).FirstOrDefault() }).SingleOrDefaultAsync(); return p is null ? Results.NotFound() : Results.Ok(p);
    }
    private static async Task<IResult> Simulate(Guid paymentId, SimulationRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, IPaymentGateway gateway, PaymentProcessor processor)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var payment = await db.PaymentRequests.Include(x => x.RenewalRequest).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == paymentId && x.RenewalRequest.RequestedByUserId == user); if (payment is null) return Results.NotFound();
        if (!Enum.TryParse<PaymentEventOutcome>(request.Outcome, true, out var outcome)) return Results.BadRequest(); var providerEvent = gateway.CreateTestEvent(payment, outcome); var result = await processor.ProcessAsync(providerEvent); return result.Accepted ? Results.Ok(result) : Results.BadRequest(result);
    }
    private static async Task<IResult> GuardianReceipt(Guid receiptId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var r = await db.Receipts.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == receiptId && x.Collection.RenewalRequest.RequestedByUserId == user && db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.Guardian.UserId == user && l.PlayerId == x.PlayerId && l.IsActive)).Select(x => new { x.Id, x.ReceiptNumber, x.PlayerNameSnapshot, x.SportNameSnapshot, x.PlanNameSnapshot, x.Amount, x.Currency, x.PaidAtUtc, x.PaymentMethod, x.ProviderReference }).SingleOrDefaultAsync(); return r is null ? Results.NotFound() : Results.Ok(r);
    }
    private static async Task<Academy.Infrastructure.People.SportEnrollment?> OwnEnrollment(FoundationDbContext db, Guid academy, Guid user, Guid id) => await db.SportEnrollments.SingleOrDefaultAsync(e => e.AcademyId == academy && e.Id == id && e.IsActive && db.GuardianPlayerLinks.Any(l => l.AcademyId == academy && l.Guardian.UserId == user && l.PlayerId == e.PlayerId && l.IsActive));
    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public sealed record PlanRequest(string ArabicName, string? EnglishName, Guid SportId, SubscriptionPlanType PlanType, decimal Price, int? DurationDays, int? SessionCount, int DisplayOrder = 0);
public sealed record PlanStatusRequest(bool IsActive);
public sealed record RenewalCreateRequest(Guid SportEnrollmentId, Guid SubscriptionPlanId);
public sealed record SimulationRequest(string Outcome);
