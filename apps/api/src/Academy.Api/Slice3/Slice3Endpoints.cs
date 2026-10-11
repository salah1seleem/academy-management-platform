using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice3;

public static class Slice3Endpoints
{
    public static void MapSlice3Endpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        var admin = api.MapGroup("/subscriptions").RequireAuthorization(AcademyPermissions.SubscriptionRead);
        admin.MapGet("/plans", Plans);
        admin.MapGet("/plans/{id:guid}", PlanCatalog.Detail);
        admin.MapPost("/plans", CreatePlan).RequireAuthorization(AcademyPermissions.SubscriptionPlanManage).AddEndpointFilter<CsrfFilter>();
        admin.MapPut("/plans/{id:guid}", UpdatePlan).RequireAuthorization(AcademyPermissions.SubscriptionPlanManage).AddEndpointFilter<CsrfFilter>();
        admin.MapPost("/plans/{id:guid}/delete", (Guid id, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock) => PlanCatalog.SetStatus(id, false, tenant, principal, db, clock)).RequireAuthorization(AcademyPermissions.SubscriptionPlanManage).AddEndpointFilter<CsrfFilter>();
        admin.MapPut("/plans/{id:guid}/status", (Guid id, PlanStatusRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock) => PlanCatalog.SetStatus(id, request.IsActive, tenant, principal, db, clock)).RequireAuthorization(AcademyPermissions.SubscriptionPlanManage).AddEndpointFilter<CsrfFilter>();

        admin.MapGet("/periods", async (string? state, int? days, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock) =>
        {
            var t = (await tenant.ResolveAsync())!; var today = clock.Today; var windowEnd = today.AddDays(Math.Clamp(days ?? 7, 1, 90));
            var q = db.SubscriptionPeriods.AsNoTracking().Where(x => x.AcademyId == t.AcademyId);
            q = state?.ToLowerInvariant() switch { "active" => q.Where(x => x.Status == SubscriptionPeriodStatus.Frozen || (x.StartDate <= today && (x.EndDate == null || x.EndDate >= today) && x.Status != SubscriptionPeriodStatus.Cancelled && ((x.PlanTypeSnapshot ?? x.SubscriptionPlan.PlanType) == SubscriptionPlanType.Duration || x.RemainingSessions > 0))), "expiring" => q.Where(x => x.EndDate >= today && x.EndDate <= windowEnd && x.Status != SubscriptionPeriodStatus.Cancelled), "expired" => q.Where(x => (x.EndDate < today || ((x.PlanTypeSnapshot ?? x.SubscriptionPlan.PlanType) != SubscriptionPlanType.Duration && x.RemainingSessions <= 0)) && x.Status != SubscriptionPeriodStatus.Cancelled && x.Status != SubscriptionPeriodStatus.Frozen), _ => q };
            return Results.Ok(await q.OrderByDescending(x => x.StartDate).Select(x => new { x.Id, sportEnrollmentId = x.SportEnrollmentId, player = x.SportEnrollment.Player.ArabicName, sport = x.SubscriptionPlan.Sport.ArabicName, plan = (x.PlanNameSnapshot ?? x.SubscriptionPlan.ArabicName), planType = (x.PlanTypeSnapshot ?? x.SubscriptionPlan.PlanType).ToString(), x.StartDate, x.EndDate, x.FrozenFromDate, x.RemainingSessions, status = x.Status == SubscriptionPeriodStatus.Frozen ? "Frozen" : x.Status == SubscriptionPeriodStatus.Cancelled ? "Cancelled" : x.EndDate < today || ((x.PlanTypeSnapshot ?? x.SubscriptionPlan.PlanType) != SubscriptionPlanType.Duration && x.RemainingSessions <= 0) ? "Expired" : x.StartDate > today ? "Scheduled" : "Active" }).ToListAsync());
        });
        admin.MapGet("/renewals", async (CurrentTenant tenant, FoundationDbContext db) => { var t = (await tenant.ResolveAsync())!; return Results.Ok(await db.RenewalRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId).OrderByDescending(x => x.RequestedAtUtc).Select(x => new { x.Id, player = x.SportEnrollment.Player.ArabicName, sport = x.SubscriptionPlan.Sport.ArabicName, plan = (x.PlanNameSnapshot ?? x.SubscriptionPlan.ArabicName), guardian = db.Users.Where(u => u.Id == x.RequestedByUserId).Select(u => u.DisplayName).FirstOrDefault(), amount = x.FinalAmount, x.OriginalAmount, x.DiscountAmount, x.Currency, status = x.Status.ToString(), x.RequestedAtUtc }).ToListAsync()); });
        admin.MapGet("/payments", async (CurrentTenant tenant, FoundationDbContext db) => { var t = (await tenant.ResolveAsync())!; return Results.Ok(await db.PaymentRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId).OrderByDescending(x => x.CreatedAtUtc).Select(x => new { x.Id, reference = x.ProviderReference, player = x.RenewalRequest.SportEnrollment.Player.ArabicName, sport = x.RenewalRequest.SubscriptionPlan.Sport.ArabicName, plan = (x.RenewalRequest.PlanNameSnapshot ?? x.RenewalRequest.SubscriptionPlan.ArabicName), x.Provider, x.Amount, x.Currency, status = x.Status.ToString(), x.CreatedAtUtc, x.ConfirmedAtUtc }).ToListAsync()); }).RequireAuthorization(AcademyPermissions.PaymentRead);
        admin.MapGet("/collections", async (CurrentTenant tenant, FoundationDbContext db) => { var t = (await tenant.ResolveAsync())!; var rows = await db.Collections.AsNoTracking().Where(x => x.AcademyId == t.AcademyId).OrderByDescending(x => x.ConfirmedAtUtc).Select(x => new { x.Id, receiptId = db.Receipts.Where(r => r.AcademyId == t.AcademyId && r.CollectionId == x.Id).Select(r => r.Id).Single(), receiptNumber = db.Receipts.Where(r => r.AcademyId == t.AcademyId && r.CollectionId == x.Id).Select(r => r.ReceiptNumber).Single(), player = x.SportEnrollment.Player.ArabicName, sport = x.SportEnrollment.Sport.ArabicName, x.Amount, x.Currency, x.Provider, x.ConfirmedAtUtc }).ToListAsync(); return Results.Ok(new { total = rows.Sum(x => x.Amount), items = rows }); }).RequireAuthorization(AcademyPermissions.CollectionRead);
        admin.MapGet("/receipts/{receiptId:guid}", async (Guid receiptId, CurrentTenant tenant, FoundationDbContext db) => { var t = (await tenant.ResolveAsync())!; var receipt = await db.Receipts.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == receiptId).Select(x => new { x.Id, academyName = t.AcademyName, x.ReceiptNumber, x.PlayerNameSnapshot, x.SportNameSnapshot, x.PlanNameSnapshot, x.OriginalAmount, discountType = x.DiscountType == null ? null : x.DiscountType.ToString(), x.DiscountValue, x.DiscountAmount, x.FinalAmount, x.Amount, x.Currency, x.PaidAtUtc, x.PaymentMethod, x.ProviderReference }).SingleOrDefaultAsync(); return receipt is null ? Results.NotFound() : Results.Ok(receipt); }).RequireAuthorization(AcademyPermissions.CollectionRead);
        admin.MapGet("/beneficiary-references", async (CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock) => { var t = (await tenant.ResolveAsync())!; return Results.Ok(await db.BeneficiaryRenewalReferences.AsNoTracking().Where(x => x.AcademyId == t.AcademyId).OrderBy(x => x.SportEnrollment.Player.ArabicName).Select(x => new { x.Id, player = x.SportEnrollment.Player.ArabicName, sport = x.SportEnrollment.Sport.ArabicName, x.CodeHint, x.ExpiresAtUtc, isActive = x.RevokedAtUtc == null && x.ExpiresAtUtc > clock.UtcNow }).ToListAsync()); }).RequireAuthorization(AcademyPermissions.SubscriptionPlanManage);
        admin.MapPost("/beneficiary-references/{enrollmentId:guid}/regenerate", RegenerateBeneficiaryReference).RequireAuthorization(AcademyPermissions.SubscriptionPlanManage).AddEndpointFilter<CsrfFilter>();
        admin.MapGet("/admin-renewals/enrollments", AdminRenewalEnrollments).RequireAuthorization(AcademyPermissions.SubscriptionRenewManage);
        admin.MapGet("/admin-renewals/enrollments/{enrollmentId:guid}", AdminRenewalEnrollment).RequireAuthorization(AcademyPermissions.SubscriptionRenewManage);
        admin.MapPost("/admin-renewals", CreateAdminRenewal).RequireAuthorization(AcademyPermissions.SubscriptionRenewManage).AddEndpointFilter<CsrfFilter>();
        admin.MapGet("/payments/{paymentId:guid}", AdminPayment).RequireAuthorization(AcademyPermissions.SubscriptionRenewManage);
        admin.MapPost("/payments/{paymentId:guid}/retry", RetryAdminPayment).RequireAuthorization(AcademyPermissions.SubscriptionRenewManage).AddEndpointFilter<CsrfFilter>();

        var guardian = api.MapGroup("/guardian/subscriptions").RequireAuthorization(AcademyPermissions.GuardianOwnRenewal);
        guardian.MapGet("/enrollments", GuardianEnrollments);
        guardian.MapGet("/enrollments/{enrollmentId:guid}/plans", GuardianPlans);
        guardian.MapPost("/renewals", CreateRenewal).AddEndpointFilter<CsrfFilter>();
        guardian.MapPost("/external/resolve", ResolveExternalBeneficiary).AddEndpointFilter<CsrfFilter>();
        guardian.MapPost("/external/search", SearchExternalBeneficiary).RequireRateLimiting("renewal-search").AddEndpointFilter<CsrfFilter>();
        guardian.MapPost("/external/renewals", CreateExternalRenewal).AddEndpointFilter<CsrfFilter>();
        guardian.MapGet("/payments/{paymentId:guid}", GuardianPayment);
        guardian.MapPost("/payments/{paymentId:guid}/retry", RetryPayment).AddEndpointFilter<CsrfFilter>();
        guardian.MapGet("/receipts", GuardianReceipts);
        guardian.MapGet("/receipts/{receiptId:guid}", GuardianReceipt);

        if (app.Environment.IsEnvironment("Demo") || app.Environment.IsEnvironment("Testing"))
        {
            guardian.MapPost("/payments/{paymentId:guid}/simulate", Simulate).AddEndpointFilter<CsrfFilter>();
            admin.MapPost("/payments/{paymentId:guid}/simulate", SimulateAdmin).RequireAuthorization(AcademyPermissions.SubscriptionRenewManage).AddEndpointFilter<CsrfFilter>();
            api.MapPost("/payments/internal-test/events", async (ProviderEventEnvelope value, PaymentProcessor processor) => { var result = await processor.ProcessAsync(value); return result.Accepted ? Results.Ok(result) : Results.BadRequest(result); });
        }
    }

    private static async Task<IResult> Plans(CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; return Results.Ok(await db.SubscriptionPlans.AsNoTracking().Where(x => x.AcademyId == t.AcademyId).OrderBy(x => x.DisplayOrder).ThenBy(x => x.ArabicName).Select(x => new { x.Id, x.ArabicName, x.EnglishName, sport = x.Sport.ArabicName, x.SportId, planType = x.PlanType.ToString(), x.Price, x.Currency, x.DurationDays, x.DurationMonths, serviceKind = x.ServiceKind.ToString(), trainingFormat = x.TrainingFormat.ToString(), x.SessionCount, x.IsActive, x.AllBranches, x.Revision, branchIds = x.BranchPublications.Where(p => p.IsActive && p.Branch.IsActive).Select(p => p.BranchId).ToArray() }).ToListAsync());
    }

    private static Task<IResult> CreatePlan(PlanRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock) => PlanCatalog.Save(null, request, tenant, principal, db, clock);
    private static Task<IResult> UpdatePlan(Guid id, PlanRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock) => PlanCatalog.Save(id, request, tenant, principal, db, clock);

    private static async Task<IResult> GuardianEnrollments(CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var today = clock.Today;
        var ids = db.GuardianPlayerLinks.Where(l => l.AcademyId == t.AcademyId && l.Guardian.UserId == user && l.IsActive).Select(l => l.PlayerId);
        var items = await db.SportEnrollments.AsNoTracking().Where(e => e.AcademyId == t.AcademyId && ids.Contains(e.PlayerId) && e.IsActive).OrderBy(e => e.Player.ArabicName).ThenBy(e => e.Sport.ArabicName).Select(e => new { e.Id, playerId = e.PlayerId, player = e.Player.ArabicName, sport = e.Sport.ArabicName, sportId = e.SportId, branchId = e.BranchId, branch = e.Branch.ArabicName, period = db.SubscriptionPeriods.Where(p => p.AcademyId == t.AcademyId && p.SportEnrollmentId == e.Id).OrderByDescending(p => p.UpdatedAtUtc).Select(p => new { p.Id, plan = (p.PlanNameSnapshot ?? p.SubscriptionPlan.ArabicName), p.StartDate, p.EndDate, p.FrozenFromDate, p.RemainingSessions, status = p.Status == SubscriptionPeriodStatus.Frozen ? "Frozen" : p.Status == SubscriptionPeriodStatus.Cancelled ? "Cancelled" : p.EndDate < today || ((p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) != SubscriptionPlanType.Duration && p.RemainingSessions <= 0) ? "Expired" : p.StartDate > today ? "Scheduled" : "Active" }).FirstOrDefault() }).ToListAsync();
        return Results.Ok(items);
    }
    private static async Task<IResult> GuardianPlans(Guid enrollmentId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var enrollment = await OwnEnrollment(db, t.AcademyId, user, enrollmentId); if (enrollment is null) return Results.NotFound();
        return Results.Ok(await db.SubscriptionPlans.TrainingOnly().AsNoTracking().ForBranch(enrollment.BranchId).Where(x => x.AcademyId == t.AcademyId && x.SportId == enrollment.SportId && x.IsActive).OrderBy(x => x.DisplayOrder).Select(x => new { x.Id, x.ArabicName, planType = x.PlanType.ToString(), x.Price, x.Currency, x.DurationDays, x.DurationMonths, serviceKind = x.ServiceKind.ToString(), trainingFormat = x.TrainingFormat.ToString(), x.SessionCount }).ToListAsync());
    }
    private static async Task<IResult> CreateRenewal(RenewalCreateRequest request, HttpContext http, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, RenewalCreationService renewals)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var key = http.Request.Headers["Idempotency-Key"].ToString(); if (string.IsNullOrWhiteSpace(key) || key.Length > 100) return Results.BadRequest(new { message = "Idempotency-Key مطلوب." });
        var existing = await db.RenewalRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.RequestedByUserId == user && x.IdempotencyKey == key).SingleOrDefaultAsync();
        if (existing is not null) return existing.SportEnrollmentId != request.SportEnrollmentId || existing.SubscriptionPlanId != request.SubscriptionPlanId
            ? Results.Conflict(new { message = "مفتاح الطلب مستخدم لتسجيل أو باقة مختلفة." })
            : Results.Ok(new { renewalId = existing.Id, paymentId = existing.PaymentRequestId });
        var enrollment = await OwnEnrollment(db, t.AcademyId, user, request.SportEnrollmentId); if (enrollment is null) return Results.NotFound();
        var plan = await db.SubscriptionPlans.TrainingOnly().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == request.SubscriptionPlanId && x.SportId == enrollment.SportId && x.IsActive); if (plan is null) return Results.NotFound();
        return RenewalResult(await renewals.CreateAsync(t.AcademyId, user, enrollment.Id, plan.Id, key, false), false);
    }
    private static async Task<IResult> GuardianPayment(Guid paymentId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var p = await db.PaymentRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == paymentId && x.RenewalRequest.RequestedByUserId == user).Select(x => new { x.Id, renewalId = x.RenewalRequestId, x.ProviderReference, x.Provider, x.Amount, x.Currency, status = x.Status.ToString(), player = x.RenewalRequest.SportEnrollment.Player.ArabicName, sport = x.RenewalRequest.SubscriptionPlan.Sport.ArabicName, plan = (x.RenewalRequest.PlanNameSnapshot ?? x.RenewalRequest.SubscriptionPlan.ArabicName), x.RenewalRequest.OriginalAmount, discountType = x.RenewalRequest.DiscountType == null ? null : x.RenewalRequest.DiscountType.ToString(), x.RenewalRequest.DiscountValue, x.RenewalRequest.DiscountAmount, x.RenewalRequest.FinalAmount, currentPaymentId = x.RenewalRequest.PaymentRequestId, isCurrent = x.RenewalRequest.PaymentRequestId == x.Id, receiptId = db.Receipts.Where(r => r.AcademyId == t.AcademyId && r.Collection.PaymentRequestId == x.Id).Select(r => (Guid?)r.Id).FirstOrDefault() }).SingleOrDefaultAsync(); return p is null ? Results.NotFound() : Results.Ok(p);
    }
    private static async Task<IResult> Simulate(Guid paymentId, SimulationRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, IPaymentGateway gateway, PaymentProcessor processor)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var payment = await db.PaymentRequests.Include(x => x.RenewalRequest).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == paymentId && x.RenewalRequest.RequestedByUserId == user); if (payment is null) return Results.NotFound();
        if (!Enum.TryParse<PaymentEventOutcome>(request.Outcome, true, out var outcome)) return Results.BadRequest(); var providerEvent = gateway.CreateTestEvent(payment, outcome); var result = await processor.ProcessAsync(providerEvent); return result.Accepted ? Results.Ok(result) : Results.BadRequest(result);
    }
    private static async Task<IResult> GuardianReceipt(Guid receiptId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var r = await db.Receipts.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == receiptId && x.Collection.RenewalRequest.RequestedByUserId == user).Select(x => new { x.Id, academyName = t.AcademyName, x.ReceiptNumber, x.PlayerNameSnapshot, x.SportNameSnapshot, x.PlanNameSnapshot, x.OriginalAmount, discountType = x.DiscountType == null ? null : x.DiscountType.ToString(), x.DiscountValue, x.DiscountAmount, x.FinalAmount, x.Amount, x.Currency, x.PaidAtUtc, x.PaymentMethod, x.ProviderReference }).SingleOrDefaultAsync(); return r is null ? Results.NotFound() : Results.Ok(r);
    }

    private static async Task<IResult> GuardianReceipts(CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        var user = UserId(principal);
        var items = await db.Receipts.AsNoTracking()
            .Where(x => x.AcademyId == t.AcademyId && x.Collection.RenewalRequest.RequestedByUserId == user)
            .OrderByDescending(x => x.PaidAtUtc)
            .Take(100)
            .Select(x => new { x.Id, x.ReceiptNumber, player = x.PlayerNameSnapshot, sport = x.SportNameSnapshot, plan = x.PlanNameSnapshot, x.OriginalAmount, x.DiscountAmount, x.FinalAmount, x.Amount, x.Currency, x.PaidAtUtc })
            .ToListAsync();
        return Results.Ok(new { items });
    }

    private static async Task<IResult> SearchExternalBeneficiary(ExternalSearchRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ScopedRenewalReferences scoped)
    {
        var t = (await tenant.ResolveAsync())!; var payer = UserId(principal); var term = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(term) || term.Length is < 3 or > 100) return Results.UnprocessableEntity(new { message = "اكتب ثلاثة أحرف على الأقل من اسم اللاعب." });
        var matches = await db.SportEnrollments.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.IsActive && x.Player.IsActive && x.TrainingGroup.IsActive && x.Branch.IsActive && x.Sport.IsActive && x.Player.ArabicName.Contains(term)).OrderBy(x => x.Player.ArabicName).ThenBy(x => x.Sport.ArabicName).Take(10).Select(x => new { x.Id, name = x.Player.ArabicName, sport = x.Sport.ArabicName, branch = x.Branch.ArabicName }).ToListAsync();
        return Results.Ok(matches.Select(x => new { x.name, x.sport, x.branch, reference = scoped.Create(t.AcademyId, payer, x.Id) }));
    }
    private static async Task<IResult> ResolveExternalBeneficiary(ExternalReferenceRequest request, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock, ClaimsPrincipal principal, ScopedRenewalReferences scoped)
    {
        var t = (await tenant.ResolveAsync())!;
        if (request.Reference?.StartsWith("SR1.", StringComparison.Ordinal) == true) {
            var target = scoped.Resolve(request.Reference, t.AcademyId, UserId(principal));
            var enrollment = target.HasValue ? await db.SportEnrollments.Where(x => x.AcademyId == t.AcademyId && x.Id == target && x.IsActive && x.Player.IsActive && x.TrainingGroup.IsActive && x.Branch.IsActive && x.Sport.IsActive).Select(x => new { playerDisplayName = x.Player.ArabicName, sport = x.Sport.ArabicName, x.SportId, x.BranchId }).SingleOrDefaultAsync() : null;
            if (enrollment is null) return Results.NotFound(new { message = "انتهى الاختيار أو تغير التسجيل. أعد البحث." });
            var available = await db.SubscriptionPlans.TrainingOnly().ForBranch(enrollment.BranchId).Where(x => x.AcademyId == t.AcademyId && x.SportId == enrollment.SportId && x.IsActive).OrderBy(x => x.DisplayOrder).Select(x => new { x.Id, x.ArabicName, planType = x.PlanType.ToString(), x.Price, x.Currency, x.DurationDays, x.DurationMonths, serviceKind = x.ServiceKind.ToString(), trainingFormat = x.TrainingFormat.ToString(), x.SessionCount }).ToListAsync();
            return Results.Ok(new { enrollment.playerDisplayName, enrollment.sport, academyName = t.AcademyName, plans = available });
        }
        if (string.IsNullOrWhiteSpace(request.Reference)) return Results.NotFound(new { message = "كود التجديد غير صالح أو منتهي." });
        var codeHash = BeneficiaryRenewalCodes.Hash(request.Reference);
        var beneficiary = await db.BeneficiaryRenewalReferences.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.CodeHash == codeHash && x.IsActive && x.RevokedAtUtc == null && x.ExpiresAtUtc > clock.UtcNow && x.SportEnrollment.IsActive)
            .Select(x => new { playerDisplayName = x.SportEnrollment.Player.ArabicName, sport = x.SportEnrollment.Sport.ArabicName, academyName = t.AcademyName, sportId = x.SportEnrollment.SportId, branchId = x.SportEnrollment.BranchId }).SingleOrDefaultAsync();
        if (beneficiary is null) return Results.NotFound(new { message = "كود التجديد غير صالح أو منتهي." });
        var plans = await db.SubscriptionPlans.TrainingOnly().AsNoTracking().ForBranch(beneficiary.branchId).Where(x => x.AcademyId == t.AcademyId && x.SportId == beneficiary.sportId && x.IsActive).OrderBy(x => x.DisplayOrder).Select(x => new { x.Id, x.ArabicName, planType = x.PlanType.ToString(), x.Price, x.Currency, x.DurationDays, x.DurationMonths, serviceKind = x.ServiceKind.ToString(), trainingFormat = x.TrainingFormat.ToString(), x.SessionCount }).ToListAsync();
        return Results.Ok(new { beneficiary.playerDisplayName, beneficiary.sport, beneficiary.academyName, plans });
    }

    private static async Task<IResult> CreateExternalRenewal(ExternalRenewalRequest request, HttpContext http, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, RenewalCreationService renewals, ISubscriptionClock clock, ScopedRenewalReferences scoped)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var key = IdempotencyKey(http); if (key is null) return Results.BadRequest(new { message = "Idempotency-Key مطلوب." });
        var existing = await ExistingRenewal(db, t.AcademyId, user, key); if (existing is not null) return Results.Ok(existing);
        if (string.IsNullOrWhiteSpace(request.Reference)) return Results.NotFound(new { message = "كود التجديد غير صالح أو منتهي." });
        if (request.Reference.StartsWith("SR1.", StringComparison.Ordinal)) {
            var target = scoped.Resolve(request.Reference, t.AcademyId, user);
            var eligible = target.HasValue ? await db.SportEnrollments.Where(x => x.AcademyId == t.AcademyId && x.Id == target && x.IsActive && x.Player.IsActive && x.TrainingGroup.IsActive && x.Branch.IsActive && x.Sport.IsActive).Select(x => new { x.Id, x.SportId }).SingleOrDefaultAsync() : null;
            if (eligible is null || !await db.SubscriptionPlans.TrainingOnly().AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == request.SubscriptionPlanId && x.SportId == eligible.SportId && x.IsActive)) return Results.NotFound(new { message = "انتهى الاختيار أو تغيرت الباقة. أعد البحث." });
            return RenewalResult(await renewals.CreateAsync(t.AcademyId, user, eligible.Id, request.SubscriptionPlanId, key, false), false);
        }
        var codeHash = BeneficiaryRenewalCodes.Hash(request.Reference);
        var reference = await db.BeneficiaryRenewalReferences.Include(x => x.SportEnrollment).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.CodeHash == codeHash && x.IsActive && x.RevokedAtUtc == null && x.ExpiresAtUtc > clock.UtcNow && x.SportEnrollment.IsActive);
        if (reference is null) return Results.NotFound(new { message = "كود التجديد غير صالح أو منتهي." });
        var plan = await db.SubscriptionPlans.TrainingOnly().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == request.SubscriptionPlanId && x.SportId == reference.SportEnrollment.SportId && x.IsActive);
        if (plan is null) return Results.NotFound(new { message = "الباقة غير متاحة لهذا التسجيل." });
        return RenewalResult(await renewals.CreateAsync(t.AcademyId, user, reference.SportEnrollment.Id, plan.Id, key, false), false);
    }

    private static async Task<IResult> RetryPayment(Guid paymentId, HttpContext http, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, RenewalCreationService renewals)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var key = IdempotencyKey(http); if (key is null) return Results.BadRequest(new { message = "Idempotency-Key مطلوب." });
        var existing = await ExistingRenewal(db, t.AcademyId, user, key); if (existing is not null) return Results.Ok(existing);
        var old = await db.PaymentRequests.AsNoTracking().Include(x => x.RenewalRequest).ThenInclude(x => x.SportEnrollment).Include(x => x.RenewalRequest).ThenInclude(x => x.SubscriptionPlan)
            .SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == paymentId && x.RenewalRequest.RequestedByUserId == user);
        if (old is null) return Results.NotFound();
        if (old.Status is not (PaymentRequestStatus.Failed or PaymentRequestStatus.Cancelled or PaymentRequestStatus.Expired)) return Results.Conflict(new { message = "إعادة المحاولة متاحة للعمليات الفاشلة أو الملغاة أو المنتهية فقط." });
        var snapshot = new RenewalFinancialSnapshot(old.RenewalRequest.OriginalAmount, old.RenewalRequest.DiscountType,
            old.RenewalRequest.DiscountValue, old.RenewalRequest.DiscountAmount, old.RenewalRequest.FinalAmount,
            old.RenewalRequest.DiscountReason, old.RenewalRequest.DiscountAppliedByUserId, old.RenewalRequest.DiscountAppliedAtUtc);
        return RenewalResult(await renewals.CreateAsync(t.AcademyId, user, old.RenewalRequest.SportEnrollmentId,
            old.RenewalRequest.SubscriptionPlanId, key, false, snapshot, true), false);
    }

    private static async Task<IResult> AdminRenewalEnrollments(string? search, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!;
        var today = clock.Today;
        var query = db.SportEnrollments.AsNoTracking()
            .Where(x => x.AcademyId == t.AcademyId && x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                EF.Functions.ILike(x.Player.ArabicName, $"%{term}%") ||
                EF.Functions.ILike(x.Player.PlayerCode, $"%{term}%") ||
                db.GuardianPlayerLinks.Any(link =>
                    link.AcademyId == t.AcademyId && link.PlayerId == x.PlayerId && link.IsActive &&
                    link.Guardian.ContactPhone != null && EF.Functions.ILike(link.Guardian.ContactPhone, $"%{term}%")));
        }

        var rows = await query.OrderBy(x => x.Player.ArabicName).ThenBy(x => x.Sport.ArabicName).Take(50)
            .Select(x => new
            {
                x.Id,
                x.PlayerId,
                x.Player.PlayerCode,
                player = x.Player.ArabicName,
                sport = x.Sport.ArabicName,
                branch = x.Branch.ArabicName,
                group = x.TrainingGroup.ArabicName,
                guardianPhone = db.GuardianPlayerLinks.Where(link => link.AcademyId == t.AcademyId && link.PlayerId == x.PlayerId && link.IsActive)
                    .Select(link => link.Guardian.ContactPhone).FirstOrDefault(),
                period = db.SubscriptionPeriods.Where(period => period.AcademyId == t.AcademyId && period.SportEnrollmentId == x.Id)
                    .OrderByDescending(period => period.UpdatedAtUtc)
                    .Select(period => new
                    {
                        period.Id,
                        plan = (period.PlanNameSnapshot ?? period.SubscriptionPlan.ArabicName),
                        period.StartDate,
                        period.EndDate,
                        status = period.Status == SubscriptionPeriodStatus.Frozen ? "Frozen" : period.Status == SubscriptionPeriodStatus.Cancelled ? "Cancelled" : period.EndDate < today || ((period.PlanTypeSnapshot ?? period.SubscriptionPlan.PlanType) != SubscriptionPlanType.Duration && period.RemainingSessions <= 0) ? "Expired" : period.StartDate > today ? "Scheduled" : "Active"
                    }).FirstOrDefault()
            }).ToListAsync();
        return Results.Ok(rows);
    }

    private static async Task<IResult> AdminRenewalEnrollment(Guid enrollmentId, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!;
        var resolvedEnrollmentId = await db.SportEnrollments.AsNoTracking()
            .Where(x => x.AcademyId == t.AcademyId && x.Id == enrollmentId)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync()
            ?? await db.SubscriptionPeriods.AsNoTracking()
                .Where(x => x.AcademyId == t.AcademyId && x.Id == enrollmentId)
                .Select(x => (Guid?)x.SportEnrollmentId).SingleOrDefaultAsync();
        if (resolvedEnrollmentId is null) return Results.NotFound();
        var enrollment = await db.SportEnrollments.AsNoTracking()
            .Where(x => x.AcademyId == t.AcademyId && x.Id == resolvedEnrollmentId && x.IsActive)
            .Select(x => new
            {
                x.Id,
                x.PlayerId,
                x.SportId,
                x.BranchId,
                x.Player.PlayerCode,
                player = x.Player.ArabicName,
                sport = x.Sport.ArabicName,
                branch = x.Branch.ArabicName,
                group = x.TrainingGroup.ArabicName
            }).SingleOrDefaultAsync();
        if (enrollment is null) return Results.NotFound();

        var current = await db.SubscriptionPeriods.AsNoTracking()
            .Where(x => x.AcademyId == t.AcademyId && x.SportEnrollmentId == resolvedEnrollmentId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new { x.Id, plan = (x.PlanNameSnapshot ?? x.SubscriptionPlan.ArabicName), x.StartDate, x.EndDate, status = x.Status.ToString() })
            .FirstOrDefaultAsync();
        var latestEligibleEnd = await db.SubscriptionPeriods.AsNoTracking()
            .Where(x => x.AcademyId == t.AcademyId && x.SportEnrollmentId == resolvedEnrollmentId && x.Status != SubscriptionPeriodStatus.Cancelled && x.EndDate != null)
            .MaxAsync(x => x.EndDate);
        var previewStart = latestEligibleEnd.HasValue && latestEligibleEnd.Value >= clock.Today
            ? latestEligibleEnd.Value.AddDays(1)
            : clock.Today;
        var plans = await db.SubscriptionPlans.TrainingOnly().AsNoTracking().ForBranch(enrollment.BranchId)
            .Where(x => x.AcademyId == t.AcademyId && x.SportId == enrollment.SportId && x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.ArabicName)
            .Select(x => new { x.Id, x.ArabicName, planType = x.PlanType.ToString(), x.Price, x.Currency, x.DurationDays, x.DurationMonths, serviceKind = x.ServiceKind.ToString(), trainingFormat = x.TrainingFormat.ToString(), x.SessionCount })
            .ToListAsync();
        return Results.Ok(new { enrollment, current, previewStart, plans });
    }

    private static async Task<IResult> CreateAdminRenewal(RenewalCreateRequest request, HttpContext http, CurrentTenant tenant,
        ClaimsPrincipal principal, RenewalCreationService renewals)
    {
        var t = (await tenant.ResolveAsync())!;
        var key = IdempotencyKey(http);
        if (key is null) return Results.BadRequest(new { message = "Idempotency-Key مطلوب." });
        var result = await renewals.CreateAsync(t.AcademyId, UserId(principal), request.SportEnrollmentId,
            request.SubscriptionPlanId, key, true);
        return RenewalResult(result, true);
    }

    private static async Task<IResult> AdminPayment(Guid paymentId, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        var payment = await db.PaymentRequests.AsNoTracking()
            .Where(x => x.AcademyId == t.AcademyId && x.Id == paymentId)
            .Select(x => new
            {
                x.Id,
                renewalId = x.RenewalRequestId,
                canCollect = db.AcademyMemberships.Any(m => m.AcademyId == t.AcademyId && m.UserId == x.RenewalRequest.RequestedByUserId && (m.Role == AcademyRole.AcademyOwner || m.Role == AcademyRole.AcademyAdmin)) && !db.PaymentProofs.Any(p => p.AcademyId == t.AcademyId && p.PaymentRequestId == x.Id),
                branchId = x.RenewalRequest.SportEnrollment.BranchId,
                sportId = x.RenewalRequest.SportId,
                groupId = x.RenewalRequest.SportEnrollment.TrainingGroupId,
                x.ProviderReference,
                x.Provider,
                x.Amount,
                x.Currency,
                status = x.Status.ToString(),
                renewalStatus = x.RenewalRequest.Status.ToString(),
                player = x.RenewalRequest.SportEnrollment.Player.ArabicName,
                playerCode = x.RenewalRequest.SportEnrollment.Player.PlayerCode,
                sport = x.RenewalRequest.SubscriptionPlan.Sport.ArabicName,
                plan = (x.RenewalRequest.PlanNameSnapshot ?? x.RenewalRequest.SubscriptionPlan.ArabicName),
                x.RenewalRequest.OriginalAmount,
                discountType = x.RenewalRequest.DiscountType == null ? null : x.RenewalRequest.DiscountType.ToString(),
                x.RenewalRequest.DiscountValue,
                x.RenewalRequest.DiscountAmount,
                x.RenewalRequest.FinalAmount,
                x.CreatedAtUtc,
                x.ConfirmedAtUtc,
                currentPaymentId = x.RenewalRequest.PaymentRequestId,
                isCurrent = x.RenewalRequest.PaymentRequestId == x.Id,
                receiptId = db.Receipts.Where(r => r.AcademyId == t.AcademyId && r.Collection.PaymentRequestId == x.Id).Select(r => (Guid?)r.Id).FirstOrDefault()
            }).SingleOrDefaultAsync();
        return payment is null ? Results.NotFound() : Results.Ok(payment);
    }

    private static async Task<IResult> RetryAdminPayment(Guid paymentId, HttpContext http, CurrentTenant tenant,
        ClaimsPrincipal principal, FoundationDbContext db, RenewalCreationService renewals)
    {
        var t = (await tenant.ResolveAsync())!;
        var key = IdempotencyKey(http);
        if (key is null) return Results.BadRequest(new { message = "Idempotency-Key مطلوب." });
        var old = await db.PaymentRequests.AsNoTracking().Include(x => x.RenewalRequest)
            .SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == paymentId);
        if (old is null) return Results.NotFound();
        if (old.Status is not (PaymentRequestStatus.Failed or PaymentRequestStatus.Cancelled or PaymentRequestStatus.Expired))
            return Results.Conflict(new { message = "إعادة المحاولة متاحة للعمليات الفاشلة أو الملغاة أو المنتهية فقط." });
        var snapshot = new RenewalFinancialSnapshot(old.RenewalRequest.OriginalAmount, old.RenewalRequest.DiscountType,
            old.RenewalRequest.DiscountValue, old.RenewalRequest.DiscountAmount, old.RenewalRequest.FinalAmount,
            old.RenewalRequest.DiscountReason, old.RenewalRequest.DiscountAppliedByUserId, old.RenewalRequest.DiscountAppliedAtUtc);
        var result = await renewals.CreateAsync(t.AcademyId, UserId(principal), old.RenewalRequest.SportEnrollmentId,
            old.RenewalRequest.SubscriptionPlanId, key, true, snapshot, true);
        return RenewalResult(result, true);
    }

    private static async Task<IResult> SimulateAdmin(Guid paymentId, SimulationRequest request, CurrentTenant tenant,
        FoundationDbContext db, IPaymentGateway gateway, PaymentProcessor processor)
    {
        var t = (await tenant.ResolveAsync())!;
        var payment = await db.PaymentRequests.Include(x => x.RenewalRequest)
            .SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == paymentId);
        if (payment is null) return Results.NotFound();
        if (!Enum.TryParse<PaymentEventOutcome>(request.Outcome, true, out var outcome))
            return Results.BadRequest(new { message = "نتيجة المحاكاة غير صالحة." });
        var result = await processor.ProcessAsync(gateway.CreateTestEvent(payment, outcome));
        return result.Accepted ? Results.Ok(result) : Results.BadRequest(result);
    }

    private static IResult RenewalResult(RenewalCreationResult result, bool admin)
    {
        if (result.State == RenewalCreationState.PayloadConflict) return Results.Conflict(new { message = "مفتاح الطلب مستخدم لتسجيل أو باقة مختلفة." });
        if (result.State == RenewalCreationState.NotFound) return Results.NotFound();
        if (result.State == RenewalCreationState.OpenRenewalExists)
            return Results.Conflict(new { message = "يوجد طلب تجديد ودفع جارٍ بالفعل لهذا التسجيل. افتح طلب الدفع الحالي قبل إنشاء طلب آخر." });
        var body = new { renewalId = result.RenewalId, paymentId = result.PaymentId };
        if (result.State == RenewalCreationState.Replay) return Results.Ok(body);
        var location = admin
            ? $"/api/v1/subscriptions/payments/{result.PaymentId}"
            : $"/api/v1/guardian/subscriptions/payments/{result.PaymentId}";
        return Results.Created(location, body);
    }

    private static async Task<IResult> RegenerateBeneficiaryReference(Guid enrollmentId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var enrollment = await db.SportEnrollments.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == enrollmentId && x.IsActive); if (enrollment is null) return Results.NotFound();
        var active = await db.BeneficiaryRenewalReferences.Where(x => x.AcademyId == t.AcademyId && x.SportEnrollmentId == enrollmentId && x.RevokedAtUtc == null).ToListAsync(); foreach (var item in active) { item.RevokedAtUtc = clock.UtcNow; item.UpdatedAtUtc = clock.UtcNow; }
        var raw = BeneficiaryRenewalCodes.Generate(); var entity = new BeneficiaryRenewalReference { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SportEnrollmentId = enrollmentId, CodeHash = BeneficiaryRenewalCodes.Hash(raw), CodeHint = BeneficiaryRenewalCodes.Hint(raw), ExpiresAtUtc = clock.UtcNow.AddYears(1), GeneratedByUserId = UserId(principal), CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
        db.BeneficiaryRenewalReferences.Add(entity); await db.SaveChangesAsync(); return Results.Ok(new { reference = raw, entity.CodeHint, entity.ExpiresAtUtc, message = "احفظ الكود الآن؛ لن يظهر كاملًا مرة أخرى." });
    }

    private static string? IdempotencyKey(HttpContext http) { var key = http.Request.Headers["Idempotency-Key"].ToString(); return string.IsNullOrWhiteSpace(key) || key.Length > 100 ? null : key; }
    private static async Task<object?> ExistingRenewal(FoundationDbContext db, Guid academy, Guid user, string key) => await db.RenewalRequests.AsNoTracking().Where(x => x.AcademyId == academy && x.RequestedByUserId == user && x.IdempotencyKey == key).Select(x => new { renewalId = x.Id, paymentId = x.PaymentRequestId }).SingleOrDefaultAsync();
    private static async Task<Academy.Infrastructure.People.SportEnrollment?> OwnEnrollment(FoundationDbContext db, Guid academy, Guid user, Guid id) => await db.SportEnrollments.SingleOrDefaultAsync(e => e.AcademyId == academy && e.Id == id && e.IsActive && db.GuardianPlayerLinks.Any(l => l.AcademyId == academy && l.Guardian.UserId == user && l.PlayerId == e.PlayerId && l.IsActive));
    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public sealed record PlanRequest(string ArabicName, string? EnglishName, Guid SportId, SubscriptionPlanType PlanType, [property: System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)] decimal Price, int? DurationDays, int? SessionCount, int DisplayOrder = 0, bool AllBranches = true, Guid[]? BranchIds = null, int? ExpectedRevision = null,
    AcademyServiceKind ServiceKind = AcademyServiceKind.Training, TrainingFormat TrainingFormat = TrainingFormat.Group, int? DurationMonths = null,
    [property: System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)] decimal? ReferenceMonthlyPrice = null, int? ChargedMonths = null);
public sealed record PlanStatusRequest(bool IsActive);
public sealed record RenewalCreateRequest(Guid SportEnrollmentId, Guid SubscriptionPlanId);
public sealed record SimulationRequest(string Outcome);
public sealed record ExternalReferenceRequest(string Reference);
public sealed record ExternalSearchRequest(string? Name);
public sealed record ExternalRenewalRequest(string Reference, Guid SubscriptionPlanId);
