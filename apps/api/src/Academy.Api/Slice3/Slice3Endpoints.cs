using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Operations;
using Academy.Api.Slice8;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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

        admin.MapGet("/periods", async (string? state, int? days, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock, ClaimsPrincipal actor, IAuthorizationService authorization, HttpContext http) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var t = (await tenant.ResolveAsync())!; var today = clock.Today; var windowEnd = today.AddDays(Math.Clamp(days ?? 7, 1, 90));
            var q = db.SubscriptionPeriods.AsNoTracking().Where(x => x.AcademyId == t.AcademyId);
            var running = q.Where(x => x.StartDate <= today && (x.EndDate == null || x.EndDate >= today) &&
                x.Status != SubscriptionPeriodStatus.Cancelled && x.Status != SubscriptionPeriodStatus.Frozen &&
                ((x.PlanTypeSnapshot ?? x.SubscriptionPlan.PlanType) == SubscriptionPlanType.Duration || x.RemainingSessions == null || x.RemainingSessions > 0));
            var filter = state?.ToLowerInvariant();
            q = filter switch
            {
                "active" => running.Concat(q.Where(x => x.Status == SubscriptionPeriodStatus.Frozen)),
                "expiring" => running.Where(x => x.EndDate != null && x.EndDate <= windowEnd),
                "expired" => q.Where(x => x.StartDate <= today && x.Status != SubscriptionPeriodStatus.Cancelled &&
                    x.Status != SubscriptionPeriodStatus.Frozen && (x.EndDate < today ||
                    ((x.PlanTypeSnapshot ?? x.SubscriptionPlan.PlanType) != SubscriptionPlanType.Duration && x.RemainingSessions <= 0))),
                _ => q
            };
            var canRenew = await SubscriptionAdjustmentEndpoints.AllowsPeriodAction(t, db, actor, authorization,
                AcademyPermissions.SubscriptionRenewManage, "/api/v1/subscriptions/admin-renewals", "POST");
            var ordered = filter == "expiring" ? q.OrderBy(x => x.EndDate) : q.OrderByDescending(x => x.StartDate);
            var rows = await ordered.ThenBy(x => x.Id).Select(x => new
            {
                x.Id, x.SportEnrollmentId, player = x.SportEnrollment.Player.ArabicName, playerCode = x.SportEnrollment.Player.PlayerCode,
                sport = x.SportEnrollment.Sport.ArabicName, plan = x.PlanNameSnapshot ?? x.SubscriptionPlan.ArabicName,
                planType = x.PlanTypeSnapshot ?? x.SubscriptionPlan.PlanType, x.StartDate, x.EndDate, x.FrozenFromDate, x.RemainingSessions, x.Status,
                canRenew = canRenew && x.SportEnrollment.IsActive && x.SportEnrollment.Player.IsActive
            }).ToListAsync();
            return Results.Ok(rows.Select(x => new
            {
                x.Id, x.SportEnrollmentId, x.player, x.playerCode, x.sport, x.plan, planType = x.planType.ToString(),
                x.StartDate, x.EndDate, x.FrozenFromDate, x.RemainingSessions, x.canRenew,
                status = SubscriptionPeriodState.Resolve(x.Status, x.StartDate, x.EndDate, x.planType, x.RemainingSessions, today).ToString()
            }));
        });
        admin.MapGet("/renewals", async (CurrentTenant tenant, FoundationDbContext db, HttpResponse response) =>
        {
            response.Headers.CacheControl = "private, no-store";
            var t = (await tenant.ResolveAsync())!;
            var rows = await db.RenewalRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId)
                .OrderByDescending(x => x.RequestedAtUtc).ThenBy(x => x.Id)
                .Select(x => new
                {
                    x.Id, player = x.SportEnrollment.Player.ArabicName, playerCode = x.SportEnrollment.Player.PlayerCode,
                    sport = x.SubscriptionPlan.Sport.ArabicName, plan = x.PlanNameSnapshot ?? x.SubscriptionPlan.ArabicName,
                    guardian = db.Users.Where(u => u.Id == x.RequestedByUserId).Select(u => u.DisplayName).FirstOrDefault(),
                    amount = x.FinalAmount, x.OriginalAmount, x.DiscountAmount, x.Currency, status = x.Status.ToString(), x.RequestedAtUtc
                }).ToListAsync();
            return Results.Ok(rows);
        });
        admin.MapGet("/payments", async (CurrentTenant tenant, FoundationDbContext db) => { var t = (await tenant.ResolveAsync())!; return Results.Ok(await db.PaymentRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId).OrderByDescending(x => x.CreatedAtUtc).Select(x => new { x.Id, reference = x.ProviderReference, player = x.RenewalRequest.SportEnrollment.Player.ArabicName, sport = x.RenewalRequest.SubscriptionPlan.Sport.ArabicName, plan = (x.RenewalRequest.PlanNameSnapshot ?? x.RenewalRequest.SubscriptionPlan.ArabicName), x.Provider, x.Amount, x.Currency, status = x.Status.ToString(), x.CreatedAtUtc, x.ConfirmedAtUtc }).ToListAsync()); }).RequireAuthorization(AcademyPermissions.PaymentRead);
        admin.MapGet("/collections", SubscriptionCollections).RequireAuthorization(AcademyPermissions.CollectionRead);
        admin.MapGet("/receipts/{receiptId:guid}", async (Guid receiptId, CurrentTenant tenant, FoundationDbContext db, HttpResponse response) => { response.Headers.CacheControl = "no-store"; var t=(await tenant.ResolveAsync())!; return await ReceiptResult(receiptId,db.Receipts.AsNoTracking().Where(x=>x.AcademyId==t.AcademyId),t,db,true); }).RequireAuthorization(AcademyPermissions.CollectionRead);
        admin.MapGet("/beneficiary-references", async (CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock) => { var t = (await tenant.ResolveAsync())!; return Results.Ok(await db.BeneficiaryRenewalReferences.AsNoTracking().Where(x => x.AcademyId == t.AcademyId).OrderBy(x => x.SportEnrollment.Player.ArabicName).Select(x => new { x.Id, player = x.SportEnrollment.Player.ArabicName, sport = x.SportEnrollment.Sport.ArabicName, x.CodeHint, x.ExpiresAtUtc, isActive = x.RevokedAtUtc == null && x.ExpiresAtUtc > clock.UtcNow }).ToListAsync()); }).RequireAuthorization(AcademyPermissions.SubscriptionPlanManage);
        admin.MapGet("/beneficiary-references/{enrollmentId:guid}", BeneficiaryReferenceStatus).RequireAuthorization(AcademyPermissions.SubscriptionPlanManage);
        admin.MapPost("/beneficiary-references/{enrollmentId:guid}/regenerate", RegenerateBeneficiaryReference).RequireAuthorization(AcademyPermissions.SubscriptionPlanManage).AddEndpointFilter<CsrfFilter>();
        admin.MapGet("/admin-renewals/enrollments", AdminRenewalEnrollments).RequireAuthorization(AcademyPermissions.SubscriptionRenewManage);
        admin.MapGet("/admin-renewals/enrollments/{enrollmentId:guid}", AdminRenewalEnrollment).RequireAuthorization(AcademyPermissions.SubscriptionRenewManage);
        admin.MapGet("/admin-renewals/enrollments/{enrollmentId:guid}/quote", AdminRenewalQuote)
            .RequireAuthorization(AcademyPermissions.SubscriptionRenewManage).RequireRateLimiting("renewal-search");
        admin.MapPost("/admin-renewals", CreateAdminRenewal).RequireAuthorization(AcademyPermissions.SubscriptionRenewManage).AddEndpointFilter<CsrfFilter>();
        admin.MapGet("/payments/{paymentId:guid}", AdminPayment).RequireAuthorization(AcademyPermissions.SubscriptionRenewManage);
        admin.MapPost("/payments/{paymentId:guid}/retry", RetryAdminPayment).RequireAuthorization(AcademyPermissions.SubscriptionRenewManage).AddEndpointFilter<CsrfFilter>();

        var guardian = api.MapGroup("/guardian/subscriptions").RequireAuthorization(AcademyPermissions.GuardianOwnRenewal);
        guardian.MapGet("/enrollments", GuardianEnrollments);
        guardian.MapGet("/enrollments/{enrollmentId:guid}/plans", GuardianPlans);
        guardian.MapGet("/enrollments/{enrollmentId:guid}/renewal-review", GuardianRenewalReview);
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

    private static async Task<IResult> SubscriptionCollections(CurrentTenant tenant, FoundationDbContext db, HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        var scope = (await tenant.ResolveAsync())!;
        var rows = await db.Collections.AsNoTracking()
            .Where(collection => collection.AcademyId == scope.AcademyId)
            .OrderByDescending(collection => collection.ConfirmedAtUtc)
            .ThenByDescending(collection => collection.Id)
            .Select(collection => new
            {
                collection.Id,
                receiptId = db.Receipts.Where(receipt => receipt.AcademyId == scope.AcademyId && receipt.CollectionId == collection.Id).Select(receipt => receipt.Id).Single(),
                receiptNumber = db.Receipts.Where(receipt => receipt.AcademyId == scope.AcademyId && receipt.CollectionId == collection.Id).Select(receipt => receipt.ReceiptNumber).Single(),
                player = collection.SportEnrollment.Player.ArabicName,
                sport = collection.SportEnrollment.Sport.ArabicName,
                collection.Amount,
                collection.Currency,
                collection.Provider,
                collection.PaymentMethod,
                collection.ConfirmedAtUtc,
            }).ToListAsync();
        var totals = rows.GroupBy(row => row.Currency).OrderBy(group => group.Key)
            .Select(group => new { currency = group.Key, amount = group.Sum(row => row.Amount) });
        return Results.Ok(new { total = rows.Sum(row => row.Amount), totals, items = rows });
    }

    private static async Task<IResult> Plans(CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        var outOfScope = PlanCatalog.PublicationsOutsideBranchScope(db, t.AcademyId);
        var branchRestricted = db.BranchScopeRestricted;
        return Results.Ok(await db.SubscriptionPlans.AsNoTracking().Where(x => x.AcademyId == t.AcademyId)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.ArabicName).Select(x => new
            {
                x.Id, x.ArabicName, x.EnglishName, sport = x.Sport.ArabicName, x.SportId,
                planType = x.PlanType.ToString(), x.Price, x.Currency, x.DurationDays, x.DurationMonths,
                serviceKind = x.ServiceKind.ToString(), trainingFormat = x.TrainingFormat.ToString(),
                x.SessionCount, x.IsActive, x.AllBranches, x.Revision,
                branchIds = x.BranchPublications.Where(p => p.IsActive && p.Branch.IsActive).Select(p => p.BranchId).ToArray(),
                canManagePublication = !branchRestricted || !x.AllBranches && !outOfScope.Contains(x.Id)
            }).ToListAsync());
    }

    private static Task<IResult> CreatePlan(PlanRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock) => PlanCatalog.Save(null, request, tenant, principal, db, clock);
    private static Task<IResult> UpdatePlan(Guid id, PlanRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock) => PlanCatalog.Save(id, request, tenant, principal, db, clock);

    private static async Task<IResult> GuardianEnrollments(CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!;
        var user = UserId(principal);
        var today = clock.Today;
        var expiring = today.AddDays(7);
        var ids = db.GuardianPlayerLinks.Where(l => l.AcademyId == t.AcademyId && l.Guardian.UserId == user &&
            l.Guardian.IsActive && l.Player.IsActive && l.IsActive).Select(l => l.PlayerId);
        var periods = SubscriptionPeriodState.OrderForDisplay(
            db.SubscriptionPeriods.AsNoTracking().Where(p => p.AcademyId == t.AcademyId && p.IsActive), today);
        var items = await db.SportEnrollments.AsNoTracking()
            .Where(e => e.AcademyId == t.AcademyId && ids.Contains(e.PlayerId) && e.IsActive && e.Status == EnrollmentStatus.Active)
            .OrderBy(e => e.Player.ArabicName).ThenBy(e => e.Sport.ArabicName)
            .Select(e => new
            {
                e.Id, e.PlayerId, player = e.Player.ArabicName, sport = e.Sport.ArabicName, e.SportId, e.BranchId, branch = e.Branch.ArabicName,
                period = periods.Where(p => p.SportEnrollmentId == e.Id)
                    .Select(p => new { p.Id, plan = p.PlanNameSnapshot ?? p.SubscriptionPlan.ArabicName, p.StartDate, p.EndDate, p.FrozenFromDate, p.RemainingSessions,
                        status = p.Status == SubscriptionPeriodStatus.Frozen ? "Frozen" : p.Status == SubscriptionPeriodStatus.Cancelled ? "Cancelled" :
                            p.StartDate > today ? "Scheduled" : p.EndDate < today ||
                            ((p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) != SubscriptionPlanType.Duration && p.RemainingSessions <= 0) ? "Expired" :
                            p.EndDate <= expiring ? "Expiring" : "Active" }).FirstOrDefault()
            }).ToListAsync();
        return Results.Ok(items);
    }
    private static async Task<IResult> GuardianPlans(Guid enrollmentId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var enrollment = await OwnEnrollment(db, t.AcademyId, user, enrollmentId); if (enrollment is null) return Results.NotFound();
        return Results.Ok(await GuardianPlanOptions(db, enrollment));
    }

    private sealed record GuardianPlanOption(Guid Id, string ArabicName, string PlanType, decimal Price, string Currency,
        int? DurationDays, int? DurationMonths, decimal? ReferenceMonthlyPrice, int? ChargedMonths, string ServiceKind,
        string TrainingFormat, int? SessionCount);
    private sealed record GuardianRenewalOpen(string Kind, Guid? PaymentId = null, Guid? FamilyRequestId = null);
    private sealed record ExternalBeneficiaryReview(string PlayerDisplayName, string Sport, string AcademyName,
        List<GuardianPlanOption> Plans,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Branch,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GuardianRenewalOpen? OpenRenewal);

    private static Task<List<GuardianPlanOption>> GuardianPlanOptions(FoundationDbContext db, SportEnrollment enrollment) =>
        GuardianPlanOptions(db, enrollment.AcademyId, enrollment.SportId, enrollment.BranchId, enrollment.RegistrationFormat);

    private static Task<List<GuardianPlanOption>> GuardianPlanOptions(FoundationDbContext db, Guid academyId,
        Guid sportId, Guid branchId, TrainingRegistrationFormat format) =>
        db.SubscriptionPlans.TrainingOnly().ForRegistrationFormat(format).AsNoTracking()
            .ForBranch(branchId).Where(p => p.AcademyId == academyId && p.SportId == sportId && p.IsActive)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Id)
            .Select(p => new GuardianPlanOption(p.Id, p.ArabicName, p.PlanType.ToString(), p.Price, p.Currency,
                p.DurationDays, p.DurationMonths, p.ReferenceMonthlyPrice, p.ChargedMonths, p.ServiceKind.ToString(),
                p.TrainingFormat.ToString(), p.SessionCount)).ToListAsync();

    private static async Task<IResult> GuardianRenewalReview(Guid enrollmentId, CurrentTenant tenant, ClaimsPrincipal principal,
        FoundationDbContext db, HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        var t = (await tenant.ResolveAsync())!;
        var user = UserId(principal);
        var enrollment = await OwnEnrollment(db, t.AcademyId, user, enrollmentId);
        if (enrollment is null) return Results.NotFound();
        var context = await db.SportEnrollments.AsNoTracking().Where(e => e.AcademyId == t.AcademyId && e.Id == enrollment.Id)
            .Select(e => new { e.Id, player = e.Player.ArabicName, sport = e.Sport.ArabicName, branch = e.Branch.ArabicName }).SingleAsync();
        var open = await ReadGuardianOpenRenewal(db, t.AcademyId, enrollment.Id, user);
        return Results.Ok(new { enrollment = context, plans = open is null ? await GuardianPlanOptions(db, enrollment) : [], openRenewal = open });
    }

    private static async Task<GuardianRenewalOpen?> ReadGuardianOpenRenewal(FoundationDbContext db,
        Guid academyId, Guid enrollmentId, Guid user)
    {
        var payment = await RenewalCreationService.OpenPayments(db, academyId, enrollmentId).AsNoTracking()
            .OrderByDescending(p => p.RenewalRequest.RequestedByUserId == user).ThenByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id)
            .Select(p => new GuardianRenewalOpen("Payment", p.RenewalRequest.RequestedByUserId == user ? p.Id : (Guid?)null, null))
            .FirstOrDefaultAsync();
        return payment ?? await db.FamilyRenewalLines.IgnoreQueryFilters()
            .Where(l => l.AcademyId == academyId && l.SportEnrollmentId == enrollmentId && l.Request.Status == FamilyRenewalStatus.Pending)
            .OrderByDescending(l => l.Request.RequestedByUserId == user).ThenByDescending(l => l.Request.CreatedAtUtc).ThenByDescending(l => l.RequestId)
            .Select(l => new GuardianRenewalOpen("Family", null, l.Request.RequestedByUserId == user ? l.RequestId : (Guid?)null))
            .FirstOrDefaultAsync();
    }
    private static async Task<IResult> CreateRenewal(RenewalCreateRequest request, HttpContext http, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, RenewalCreationService renewals)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var key = http.Request.Headers["Idempotency-Key"].ToString(); if (string.IsNullOrWhiteSpace(key) || key.Length > 100) return Results.BadRequest(new { message = "Idempotency-Key مطلوب." });
        var enrollment = await OwnEnrollment(db, t.AcademyId, user, request.SportEnrollmentId); if (enrollment is null) return Results.NotFound();
        var existing = await db.RenewalRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.RequestedByUserId == user && x.IdempotencyKey == key).SingleOrDefaultAsync();
        if (existing is not null) return existing.SportEnrollmentId != request.SportEnrollmentId || existing.SubscriptionPlanId != request.SubscriptionPlanId
            ? Results.Conflict(new { message = "مفتاح الطلب مستخدم لتسجيل أو باقة مختلفة." })
            : Results.Ok(new { renewalId = existing.Id, paymentId = existing.PaymentRequestId });
        var plan = await db.SubscriptionPlans.TrainingOnly().ForRegistrationFormat(enrollment.RegistrationFormat).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == request.SubscriptionPlanId && x.SportId == enrollment.SportId && x.IsActive); if (plan is null) return Results.NotFound();
        return RenewalResult(await renewals.CreateAsync(t.AcademyId, user, enrollment.Id, plan.Id, key, ct: http.RequestAborted), false);
    }
    private static async Task<IResult> GuardianPayment(Guid paymentId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db,
        IHostEnvironment environment, IOptions<InternalTestPaymentOptions> options)
    {
        var testGatewayAvailable = options.Value.Enabled && (environment.IsEnvironment("Demo") || environment.IsEnvironment("Testing"));
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var p = await db.PaymentRequests.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.Id == paymentId && x.RenewalRequest.RequestedByUserId == user).Select(x => new { x.Id, renewalId = x.RenewalRequestId, x.ProviderReference, x.Provider, simulationAvailable = testGatewayAvailable && x.Provider == InternalTestPaymentGateway.ProviderName && x.ProviderEnvironment == "Demo", x.Amount, x.Currency, proofStatus = db.PaymentProofs.Where(proof => proof.AcademyId == t.AcademyId && proof.PaymentRequestId == x.Id).Select(proof => proof.Status).SingleOrDefault(), status = x.Status.ToString(), player = x.RenewalRequest.SportEnrollment.Player.ArabicName, sport = x.RenewalRequest.SubscriptionPlan.Sport.ArabicName, plan = (x.RenewalRequest.PlanNameSnapshot ?? x.RenewalRequest.SubscriptionPlan.ArabicName), x.RenewalRequest.OriginalAmount, discountType = x.RenewalRequest.DiscountType == null ? null : x.RenewalRequest.DiscountType.ToString(), x.RenewalRequest.DiscountValue, x.RenewalRequest.DiscountAmount, x.RenewalRequest.FinalAmount,x.RenewalRequest.DiscountReason, currentPaymentId = x.RenewalRequest.PaymentRequestId, isCurrent = x.RenewalRequest.PaymentRequestId == x.Id, receiptId = db.Receipts.Where(r => r.AcademyId == t.AcademyId && r.Collection.PaymentRequestId == x.Id).Select(r => (Guid?)r.Id).FirstOrDefault() }).SingleOrDefaultAsync(); return p is null ? Results.NotFound() : Results.Ok(p);
    }
    private static async Task<IResult> Simulate(Guid paymentId, SimulationRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, IPaymentGateway gateway, PaymentProcessor processor)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var payment = await db.PaymentRequests.Include(x => x.RenewalRequest).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == paymentId && x.RenewalRequest.RequestedByUserId == user); if (payment is null) return Results.NotFound();
        if (!Enum.TryParse<PaymentEventOutcome>(request.Outcome, true, out var outcome)) return Results.BadRequest(); var providerEvent = gateway.CreateTestEvent(payment, outcome); var result = await processor.ProcessAsync(providerEvent); return result.Accepted ? Results.Ok(result) : Results.BadRequest(result);
    }
    private static async Task<IResult> GuardianReceipt(Guid receiptId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        var t = (await tenant.ResolveAsync())!;
        var user = UserId(principal);
        return await ReceiptResult(receiptId,GuardianReceiptQuery(db,t.AcademyId,user),t,db,false,user);
    }

    private static async Task<IResult> ReceiptResult(Guid id,IQueryable<Receipt> authorized,TenantMembership t,FoundationDbContext db,bool staff,Guid? guardianUser=null)
    {
        // Authorize/materialize the receipt BEFORE any independently scoped
        // attribution lookup. Never compose IgnoreQueryFilters into this query.
        var visible=authorized.Where(x=>x.Id==id);
        if(!await visible.AnyAsync())
        {
            // A scoped staff member may read a financial receipt collected at
            // their branch, not the child profile. Authorize the posting in an
            // independent, normally branch-filtered query before this exact-ID read.
            if(!staff||!await db.CashReceiptPostings.AnyAsync(p=>p.ReceiptId==id))return Results.NotFound();
            visible=db.Receipts.IgnoreQueryFilters(["trusted-branches"]).Where(x=>x.AcademyId==t.AcademyId&&x.Id==id);
        }
        var row=await visible.Select(x=>new { x.CollectionId, x.Collection.PaymentRequestId, historical = x.Collection.Provider == HistoricalReceiptCalendar.Provider,
            receipt = new {x.Id,academyName=t.AcademyName,x.ReceiptNumber,x.PlayerNameSnapshot,x.SportNameSnapshot,x.PlanNameSnapshot,x.OriginalAmount,
            currentPlayer=guardianUser.HasValue ? db.GuardianPlayerLinks.Where(l=>l.AcademyId==t.AcademyId&&l.PlayerId==x.PlayerId&&l.Guardian.UserId==guardianUser&&l.IsActive&&l.Guardian.IsActive).Select(l=>l.Player.ArabicName).FirstOrDefault() : null,
            discountType=x.DiscountType==null?null:x.DiscountType.ToString(),x.DiscountValue,x.DiscountAmount,x.FinalAmount,x.Amount,x.Currency,
            enrollmentBranch=x.Collection.SportEnrollment.Branch.ArabicName,x.PaidAtUtc,
            paymentMethod = x.Collection.Provider == HistoricalReceiptCalendar.Provider ? x.Collection.PaymentMethod : x.PaymentMethod,
            providerReference = x.Collection.Provider == HistoricalReceiptCalendar.Provider ? null : x.ProviderReference} }).SingleOrDefaultAsync();
        if(row is null)return Results.NotFound();
        var receipt = row.receipt;
        var collectingBranch=await Academy.Api.Finance.CashFinancialAttribution.ReceiptSources(db,t.AcademyId).Where(p=>p.ReceiptId==receipt.Id).Select(p=>(Guid?)p.BranchId).SingleOrDefaultAsync();
        var branchName=collectingBranch.HasValue?await Academy.Api.Finance.CashFinancialAttribution.BranchLabels(db,t.AcademyId).Where(b=>b.Id==collectingBranch.Value).Select(b=>b.ArabicName).SingleAsync():receipt.enrollmentBranch;
        var voidAt=await db.CashReceiptVoids.IgnoreQueryFilters(["trusted-branches"]).Where(v=>v.AcademyId==t.AcademyId&&v.ReceiptId==receipt.Id).Select(v=>(DateTimeOffset?)v.CreatedAtUtc).SingleOrDefaultAsync();
        var fields=JsonSerializer.SerializeToElement(receipt,JsonSerializerOptions.Web).EnumerateObject().ToDictionary(p=>p.Name,p=>(object?)p.Value);
        if (string.Equals(receipt.paymentMethod?.Trim(), "Cash", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(receipt.providerReference?.Trim(), $"manual-{row.PaymentRequestId:N}", StringComparison.OrdinalIgnoreCase))
            fields["providerReference"] = null;
        fields["collectionBranch"]=branchName;fields["isVoided"]=voidAt.HasValue;fields["voidedAtUtc"]=voidAt;
        var academyZone = TimeZoneInfo.FindSystemTimeZoneById(t.TimeZone);
        fields["voidedAtAcademy"] = voidAt is { } reversal ? TimeZoneInfo.ConvertTime(reversal, academyZone) : null;
        if (row.historical)
        {
            fields["historicalPaidOn"] = HistoricalReceiptCalendar.PaidOn(receipt.PaidAtUtc, academyZone);
            fields["historicalPeriod"] = await (
                from evidence in db.ImportedHistoricalPayments.AsNoTracking().IgnoreQueryFilters(["trusted-branches"])
                join period in db.SubscriptionPeriods.AsNoTracking().IgnoreQueryFilters(["trusted-branches"])
                    on new { evidence.AcademyId, Id = evidence.SubscriptionPeriodId } equals new { period.AcademyId, period.Id }
                where evidence.AcademyId == t.AcademyId && evidence.CollectionId == row.CollectionId
                select new { period.StartDate, period.EndDate }).SingleOrDefaultAsync();
        }
        else fields["paidAtAcademy"] = TimeZoneInfo.ConvertTime(receipt.PaidAtUtc, academyZone);
        if(staff)fields["canReviewVoid"]=t.Role==AcademyRole.AcademyOwner&&collectingBranch.HasValue&&!voidAt.HasValue;
        return Results.Ok(fields);
    }

    private static async Task<IResult> GuardianReceipts(CurrentTenant tenant, ClaimsPrincipal principal,
        FoundationDbContext db, HttpResponse response, Guid? playerId, int? page, bool? allIssued)
    {
        response.Headers.CacheControl = "no-store";
        if (page is < 1 or > 100000) return Results.UnprocessableEntity(new { message = "رقم الصفحة غير صالح." });
        var t = (await tenant.ResolveAsync())!;
        var user = UserId(principal);
        var linked = db.GuardianPlayerLinks.AsNoTracking()
            .Where(l => l.AcademyId == t.AcademyId && l.Guardian.UserId == user && l.Guardian.IsActive && l.IsActive);
        var children = await linked.Select(l => new { id = l.PlayerId, l.Player.ArabicName, l.Player.PlayerCode }).Distinct()
            .OrderBy(child => child.ArabicName).ThenBy(child => child.id).ToArrayAsync();
        if (playerId.HasValue && !children.Any(child => child.id == playerId.Value)) return Results.NotFound();
        var duplicateCodes = children.GroupBy(child => ReceiptPlayerNameKey(child.ArabicName))
            .Where(group => group.Count() > 1).SelectMany(group => group)
            .ToDictionary(child => child.id, child => child.PlayerCode);
        var receipts = GuardianReceiptQuery(db, t.AcademyId, user);
        if (playerId.HasValue) receipts = receipts.Where(x => x.PlayerId == playerId.Value);
        var combined = receipts.Select(x => new
        {
            x.Id, x.PlayerId, x.ReceiptNumber, player = x.PlayerNameSnapshot,
            currentPlayer = linked.Where(l => l.PlayerId == x.PlayerId).Select(l => l.Player.ArabicName).FirstOrDefault(),
            sport = x.SportNameSnapshot,
            plan = x.PlanNameSnapshot, x.OriginalAmount, x.DiscountAmount, x.FinalAmount, x.Amount,
            x.Currency, x.PaidAtUtc, source = "Subscription", linkedChild = linked.Any(l => l.PlayerId == x.PlayerId),
            historical = x.Collection.Provider == HistoricalReceiptCalendar.Provider
        });
        if (allIssued == true)
        {
            var tournamentReceipts = TournamentPayments.GuardianPayments(db, t.AcademyId, user)
                .Where(x => x.Status == "Confirmed" && x.ReceiptNumber != null && x.ConfirmedAtUtc != null);
            var tournamentRows =
                from payment in tournamentReceipts
                join enrollment in db.SportEnrollments.Where(e => e.AcademyId == t.AcademyId)
                    on payment.EnrollmentId equals enrollment.Id
                where !playerId.HasValue || enrollment.PlayerId == playerId.Value
                select new
                {
                    payment.Id, enrollment.PlayerId, ReceiptNumber = payment.ReceiptNumber!,
                    player = enrollment.Player.ArabicName, currentPlayer = (string?)enrollment.Player.ArabicName, sport = enrollment.Sport.ArabicName,
                    plan = payment.TournamentName, OriginalAmount = payment.Amount, DiscountAmount = 0m,
                    FinalAmount = payment.Amount, payment.Amount, payment.Currency,
                    PaidAtUtc = payment.ConfirmedAtUtc!.Value, source = "Tournament", linkedChild = true, historical = false
                };
            combined = combined.Concat(tournamentRows);
        }

        var size = page.HasValue ? 25 : 100;
        var items = await combined.OrderByDescending(x => x.PaidAtUtc).ThenByDescending(x => x.Id)
            .Skip(((page ?? 1) - 1) * size).Take(size).ToListAsync();
        var subscriptionIds = items.Where(item => item.source == "Subscription").Select(item => item.Id).ToArray();
        // Read void postings only for the already-authorized page. Collection
        // branch scope can differ from the child's enrollment branch.
        var voided = subscriptionIds.Length == 0 ? new HashSet<Guid>() :
            await db.CashReceiptVoids.AsNoTracking().IgnoreQueryFilters(["trusted-branches"])
                .Where(row => row.AcademyId == t.AcademyId && subscriptionIds.Contains(row.ReceiptId))
                .Select(row => row.ReceiptId).ToHashSetAsync();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(t.TimeZone);
        var visible = items.Select(item => new
        {
            item.Id, item.ReceiptNumber, item.player, item.currentPlayer, item.sport, item.plan, item.OriginalAmount,
            item.DiscountAmount, item.FinalAmount, item.Amount, item.Currency, item.PaidAtUtc,
            item.source, item.linkedChild, isVoided = item.source == "Subscription" && voided.Contains(item.Id),
            playerCode = item.linkedChild ? duplicateCodes.GetValueOrDefault(item.PlayerId) : null,
            historicalPaidOn = item.historical ? HistoricalReceiptCalendar.PaidOn(item.PaidAtUtc, zone) : (DateOnly?)null,
            paidAtAcademy = item.source == "Subscription" && !item.historical ? TimeZoneInfo.ConvertTime(item.PaidAtUtc, zone) : (DateTimeOffset?)null
        });
        var childOptions = children.Select(child => new { child.id, child.ArabicName, playerCode = duplicateCodes.GetValueOrDefault(child.id) });
        return Results.Ok(new { items = visible, children = childOptions, total = await combined.CountAsync(), page = page ?? 1, pageSize = size });
    }

    private static string ReceiptPlayerNameKey(string name) =>
        string.Join(' ', name.Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    // Paying for somebody else retains the payer's receipt only; it does not
    // create a guardian link. A currently linked guardian may also see the
    // child's staff-collected receipt. Never infer either right from a phone.
    private static IQueryable<Receipt> GuardianReceiptQuery(FoundationDbContext db,Guid academy,Guid user) => db.Receipts.AsNoTracking().Where(x=>x.AcademyId==academy &&
        (x.Collection.RenewalRequest.RequestedByUserId==user || db.GuardianPlayerLinks.Any(l=>l.AcademyId==academy&&l.PlayerId==x.PlayerId&&l.IsActive&&l.Guardian.IsActive&&l.Guardian.UserId==user)));

    private static IQueryable<SportEnrollment> SearchableExternalEnrollments(FoundationDbContext db, Guid academy, DateOnly groupDate) =>
        db.SportEnrollments.AsNoTracking().Where(x => x.AcademyId == academy && x.IsActive && x.Player.IsActive && x.Branch.IsActive && x.Sport.IsActive &&
            db.TrainingGroups.Any(g => g.Id == FoundationDbContext.AssignedGroupAt(x.AcademyId, x.Id, groupDate) && g.IsActive));

    private static IQueryable<SportEnrollment> UnambiguousExternalEnrollment(IQueryable<SportEnrollment> eligible, Guid target) =>
        eligible.Where(x => x.Id == target && !eligible.Any(other => other.Id != x.Id &&
            other.Player.ArabicName == x.Player.ArabicName && other.Sport.ArabicName == x.Sport.ArabicName && other.Branch.ArabicName == x.Branch.ArabicName));

    private static async Task<IResult> SearchExternalBeneficiary(ExternalSearchRequest request, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ScopedRenewalReferences scoped, TimeProvider time, HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        var t = (await tenant.ResolveAsync())!; var groupDate = await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, time); var payer = UserId(principal); var term = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(term) || term.Length is < 3 or > 100) return Results.UnprocessableEntity(new { message = "اكتب ثلاثة أحرف على الأقل من اسم اللاعب." });
        var matches = await SearchableExternalEnrollments(db, t.AcademyId, groupDate)
            .Where(x => x.Player.ArabicName.Contains(term))
            .GroupBy(x => new { name = x.Player.ArabicName, sport = x.Sport.ArabicName, branch = x.Branch.ArabicName })
            .Select(group => new { group.Key.name, group.Key.sport, group.Key.branch, count = group.Count(), enrollment = group.Min(x => x.Id.ToString()) })
            .OrderBy(x => x.name).ThenBy(x => x.sport).ThenBy(x => x.branch).Take(10).ToListAsync();
        return Results.Ok(matches.Select(x => new { x.name, x.sport, x.branch,
            reference = x.count == 1 ? scoped.Create(t.AcademyId, payer, Guid.Parse(x.enrollment!)) : null }));
    }
    private static async Task<IResult> ResolveExternalBeneficiary(ExternalReferenceRequest request, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock, ClaimsPrincipal principal, ScopedRenewalReferences scoped, TimeProvider time, HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        var t = (await tenant.ResolveAsync())!; var groupDate = await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
        var payer = UserId(principal);
        if (request.Reference?.StartsWith("SR1.", StringComparison.Ordinal) == true) {
            var target = scoped.Resolve(request.Reference, t.AcademyId, payer);
            var enrollment = target.HasValue ? await UnambiguousExternalEnrollment(SearchableExternalEnrollments(db, t.AcademyId, groupDate), target.Value)
                .Select(x => new { x.Id, playerDisplayName = x.Player.ArabicName, sport = x.Sport.ArabicName, branch = x.Branch.ArabicName, x.SportId, x.BranchId, x.RegistrationFormat }).SingleOrDefaultAsync() : null;
            if (enrollment is null) return Results.NotFound(new { message = "انتهى الاختيار أو تغير التسجيل. أعد البحث." });
            return await ExternalBeneficiary(db, t.AcademyId, t.AcademyName, payer, enrollment.Id,
                enrollment.playerDisplayName, enrollment.sport, enrollment.SportId, enrollment.BranchId, enrollment.RegistrationFormat, enrollment.branch);
        }
        if (string.IsNullOrWhiteSpace(request.Reference)) return Results.NotFound(new { message = "كود التجديد غير صالح أو منتهي." });
        var codeHash = BeneficiaryRenewalCodes.Hash(request.Reference);
        var eligibleCodes = BeneficiaryRenewalCodes.EligibleEnrollments(db, t.AcademyId);
        var beneficiary = await db.BeneficiaryRenewalReferences.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.CodeHash == codeHash && x.IsActive && x.RevokedAtUtc == null && x.ExpiresAtUtc > clock.UtcNow && eligibleCodes.Any(e => e.Id == x.SportEnrollmentId))
            .Select(x => new { x.SportEnrollmentId, playerDisplayName = x.SportEnrollment.Player.ArabicName, sport = x.SportEnrollment.Sport.ArabicName, x.SportEnrollment.SportId, x.SportEnrollment.BranchId, x.SportEnrollment.RegistrationFormat }).SingleOrDefaultAsync();
        if (beneficiary is null) return Results.NotFound(new { message = "كود التجديد غير صالح أو منتهي." });
        return await ExternalBeneficiary(db, t.AcademyId, t.AcademyName, payer, beneficiary.SportEnrollmentId,
            beneficiary.playerDisplayName, beneficiary.sport, beneficiary.SportId, beneficiary.BranchId, beneficiary.RegistrationFormat);
    }

    private static async Task<IResult> ExternalBeneficiary(FoundationDbContext db, Guid academyId, string academyName,
        Guid payer, Guid enrollmentId, string player, string sport, Guid sportId, Guid branchId,
        TrainingRegistrationFormat format, string? branch = null)
    {
        var open = await ReadGuardianOpenRenewal(db, academyId, enrollmentId, payer);
        if (open is { PaymentId: null, FamilyRequestId: null }) open = new("Unavailable");
        var plans = await GuardianPlanOptions(db, academyId, sportId, branchId, format);
        return Results.Ok(new ExternalBeneficiaryReview(player, sport, academyName, plans, branch, open));
    }

    private static async Task<IResult> CreateExternalRenewal(ExternalRenewalRequest request, HttpContext http, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, RenewalCreationService renewals, ISubscriptionClock clock, ScopedRenewalReferences scoped, TimeProvider time)
    {
        var t = (await tenant.ResolveAsync())!; var groupDate = await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, time); var user = UserId(principal); var key = IdempotencyKey(http); if (key is null) return Results.BadRequest(new { message = "Idempotency-Key مطلوب." });
        var existing = await ExistingRenewal(db, t.AcademyId, user, key); if (existing is not null) return Results.Ok(existing);
        if (string.IsNullOrWhiteSpace(request.Reference)) return Results.NotFound(new { message = "كود التجديد غير صالح أو منتهي." });
        if (request.Reference.StartsWith("SR1.", StringComparison.Ordinal)) {
            var target = scoped.Resolve(request.Reference, t.AcademyId, user);
            var eligible = target.HasValue ? await UnambiguousExternalEnrollment(SearchableExternalEnrollments(db, t.AcademyId, groupDate), target.Value)
                .Select(x => new { x.Id, x.SportId }).SingleOrDefaultAsync() : null;
            if (eligible is null || !await db.SubscriptionPlans.TrainingOnly().AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == request.SubscriptionPlanId && x.SportId == eligible.SportId && x.IsActive)) return Results.NotFound(new { message = "انتهى الاختيار أو تغيرت الباقة. أعد البحث." });
            return RenewalResult(await renewals.CreateAsync(t.AcademyId, user, eligible.Id, request.SubscriptionPlanId, key), false);
        }
        var codeHash = BeneficiaryRenewalCodes.Hash(request.Reference);
        var eligibleCodes = BeneficiaryRenewalCodes.EligibleEnrollments(db, t.AcademyId);
        var reference = await db.BeneficiaryRenewalReferences.Include(x => x.SportEnrollment).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.CodeHash == codeHash && x.IsActive && x.RevokedAtUtc == null && x.ExpiresAtUtc > clock.UtcNow && eligibleCodes.Any(e => e.Id == x.SportEnrollmentId));
        if (reference is null) return Results.NotFound(new { message = "كود التجديد غير صالح أو منتهي." });
        var plan = await db.SubscriptionPlans.TrainingOnly().ForRegistrationFormat(reference.SportEnrollment.RegistrationFormat).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == request.SubscriptionPlanId && x.SportId == reference.SportEnrollment.SportId && x.IsActive);
        if (plan is null) return Results.NotFound(new { message = "الباقة غير متاحة لهذا التسجيل." });
        return RenewalResult(await renewals.CreateAsync(t.AcademyId, user, reference.SportEnrollment.Id, plan.Id, key), false);
    }

    private static async Task<IResult> RetryPayment(Guid paymentId, HttpContext http, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, RenewalCreationService renewals)
    {
        var t = (await tenant.ResolveAsync())!; var user = UserId(principal); var key = IdempotencyKey(http); if (key is null) return Results.BadRequest(new { message = "Idempotency-Key مطلوب." });
        var old = await db.PaymentRequests.AsNoTracking().Include(x => x.RenewalRequest).ThenInclude(x => x.SportEnrollment).Include(x => x.RenewalRequest).ThenInclude(x => x.SubscriptionPlan)
            .SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == paymentId && x.RenewalRequest.RequestedByUserId == user);
        if (old is null) return Results.NotFound();
        if (old.RenewalRequest.PaymentRequestId != old.Id)
            return Results.Conflict(new { message = "استُبدل طلب الدفع. افتح الطلب الحالي." });
        if (old.Status is not (PaymentRequestStatus.Failed or PaymentRequestStatus.Cancelled or PaymentRequestStatus.Expired)) return Results.Conflict(new { message = "إعادة المحاولة متاحة للعمليات الفاشلة أو الملغاة أو المنتهية فقط." });
        var snapshot = new RenewalFinancialSnapshot(old.RenewalRequest.OriginalAmount, old.RenewalRequest.DiscountType,
            old.RenewalRequest.DiscountValue, old.RenewalRequest.DiscountAmount, old.RenewalRequest.FinalAmount,
            old.RenewalRequest.DiscountReason, old.RenewalRequest.DiscountAppliedByUserId, old.RenewalRequest.DiscountAppliedAtUtc, old.RenewalRequest.DiscountRuleIdSnapshot, old.RenewalRequest.DiscountRuleRevisionSnapshot);
        return RenewalResult(await renewals.CreateAsync(t.AcademyId, user, old.RenewalRequest.SportEnrollmentId,
            old.RenewalRequest.SubscriptionPlanId, key, snapshot, true, fixedTerms: PlanTerms.From(old.RenewalRequest, old.RenewalRequest.SubscriptionPlan)), false);
    }

    private static async Task<IResult> AdminRenewalEnrollments(string? search, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock, TimeProvider time)
    {
        var t = (await tenant.ResolveAsync())!; var groupDate = await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
        var today = clock.Today;
        var periods = SubscriptionPeriodState.OrderForDisplay(db.SubscriptionPeriods.AsNoTracking().Where(p => p.AcademyId == t.AcademyId), today);
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
                x.Player.PlayerCode,x.Player.PhotoReference,x.Player.DateOfBirth,x.Player.Gender,
                player = x.Player.ArabicName,
                sport = x.Sport.ArabicName,
                branch = x.Branch.ArabicName,
                group = db.TrainingGroups.Where(g => g.Id == FoundationDbContext.AssignedGroupAt(x.AcademyId, x.Id, groupDate)).Select(g => g.ArabicName).FirstOrDefault() ?? "في انتظار المجموعة",
                guardianPhone = db.GuardianPlayerLinks.Where(link => link.AcademyId == t.AcademyId && link.PlayerId == x.PlayerId && link.IsActive)
                    .Select(link => link.Guardian.ContactPhone).FirstOrDefault(),
                period = periods.Where(period => period.SportEnrollmentId == x.Id)
                    .Select(period => new
                    {
                        period.Id,
                        plan = (period.PlanNameSnapshot ?? period.SubscriptionPlan.ArabicName),
                        period.StartDate,
                        period.EndDate,period.RemainingSessions,sessionBased=(period.PlanTypeSnapshot??period.SubscriptionPlan.PlanType)!=SubscriptionPlanType.Duration,
                        status = period.Status == SubscriptionPeriodStatus.Frozen ? "Frozen" : period.Status == SubscriptionPeriodStatus.Cancelled ? "Cancelled" : period.EndDate < today || ((period.PlanTypeSnapshot ?? period.SubscriptionPlan.PlanType) != SubscriptionPlanType.Duration && period.RemainingSessions <= 0) ? "Expired" : period.StartDate > today ? "Scheduled" : "Active"
                    }).FirstOrDefault()
            }).ToListAsync();
        return Results.Ok(rows);
    }

    private static async Task<IResult> AdminRenewalEnrollment(Guid enrollmentId, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock, TimeProvider time)
    {
        var t = (await tenant.ResolveAsync())!; var groupDate = await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
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
                x.RegistrationFormat,
                x.Player.PlayerCode,
                player = x.Player.ArabicName,
                sport = x.Sport.ArabicName,
                branch = x.Branch.ArabicName,
                group = db.TrainingGroups.Where(g => g.Id == FoundationDbContext.AssignedGroupAt(x.AcademyId, x.Id, groupDate)).Select(g => g.ArabicName).FirstOrDefault() ?? "في انتظار المجموعة"
            }).SingleOrDefaultAsync();
        if (enrollment is null) return Results.NotFound();

        var today = clock.Today;
        var current = await SubscriptionPeriodState.OrderForDisplay(db.SubscriptionPeriods.AsNoTracking().Where(p => p.AcademyId == t.AcademyId), today)
            .Where(x => x.SportEnrollmentId == resolvedEnrollmentId)
            .Select(x => new { x.Id, plan = (x.PlanNameSnapshot ?? x.SubscriptionPlan.ArabicName), x.StartDate, x.EndDate,
                status = x.Status == SubscriptionPeriodStatus.Frozen ? "Frozen" : x.Status == SubscriptionPeriodStatus.Cancelled ? "Cancelled" :
                    x.EndDate < today || ((x.PlanTypeSnapshot ?? x.SubscriptionPlan.PlanType) != SubscriptionPlanType.Duration && x.RemainingSessions <= 0) ? "Expired" : x.StartDate > today ? "Scheduled" : "Active" })
            .FirstOrDefaultAsync();
        var previewStart = await RenewalCreationService.PreviewStartAsync(db, t.AcademyId, enrollment.Id, today);
        var plans = await db.SubscriptionPlans.TrainingOnly().ForRegistrationFormat(enrollment.RegistrationFormat).AsNoTracking().ForBranch(enrollment.BranchId)
            .Where(x => x.AcademyId == t.AcademyId && x.SportId == enrollment.SportId && x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.ArabicName)
            .Select(x => new { x.Id, x.ArabicName, planType = x.PlanType.ToString(), x.Price, x.Currency, x.DurationDays, x.DurationMonths, x.ChargedMonths, serviceKind = x.ServiceKind.ToString(), trainingFormat = x.TrainingFormat.ToString(), x.SessionCount })
            .ToListAsync();
        var openPaymentId = await RenewalCreationService.OpenPayments(db, t.AcademyId, enrollment.Id).AsNoTracking()
            .OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id).Select(p => (Guid?)p.Id).FirstOrDefaultAsync();
        var familyOpen = !openPaymentId.HasValue && await RenewalCreationService.HasOpenFamilyRenewal(db, t.AcademyId, enrollment.Id);
        // The payment detail uses the same renewal policy, plus payments.Read for delegated staff.
        var canReadPayment = t.Role != AcademyRole.AcademyAdmin || await db.AcademyMemberships.AsNoTracking().AnyAsync(m =>
            m.AcademyId == t.AcademyId && m.Id == t.MembershipId && (!m.UseActionGrants || db.StaffActionGrants.Any(g =>
                g.AcademyId == t.AcademyId && g.MembershipId == m.Id && g.Module == "payments" && g.IsActive && g.Read)));
        var openRenewal = openPaymentId.HasValue || familyOpen
            ? new { kind = familyOpen ? "Family" : "Single", paymentId = canReadPayment ? openPaymentId : null }
            : null;
        return Results.Ok(new { enrollment, current, previewStart, plans, openRenewal, canReadPayment });
    }

    private static async Task<IResult> AdminRenewalQuote(Guid enrollmentId, Guid planId, HttpContext http, CurrentTenant tenant,
        ClaimsPrincipal principal, RenewalCreationService renewals)
    {
        http.Response.Headers.CacheControl = "private, no-store";
        var t = (await tenant.ResolveAsync())!;
        var result = await renewals.ReadStaffQuoteAsync(t.AcademyId, UserId(principal), enrollmentId, planId, http.RequestAborted);
        if (result.HasOpenRenewal) return RenewalResult(new(RenewalCreationState.OpenRenewalExists), true);
        return result.Quote is null ? Results.NotFound() : Results.Ok(result.Quote);
    }

    private static async Task<IResult> CreateAdminRenewal(AdminRenewalCreateRequest request, HttpContext http, CurrentTenant tenant,
        ClaimsPrincipal principal, RenewalCreationService renewals)
    {
        var t = (await tenant.ResolveAsync())!;
        var key = IdempotencyKey(http);
        if (key is null) return Results.BadRequest(new { message = "Idempotency-Key مطلوب." });
        var result = await renewals.CreateAsync(t.AcademyId, UserId(principal), request.SportEnrollmentId,
            request.SubscriptionPlanId, key, expectedQuoteHash: request.ExpectedQuoteHash);
        return RenewalResult(result, true);
    }

    internal static async Task<bool> CanReadStaffRenewalPayment(ClaimsPrincipal actor, IAuthorizationService authorization) =>
        (await authorization.AuthorizeAsync(actor, AcademyPermissions.PaymentRead)).Succeeded &&
        (await authorization.AuthorizeAsync(actor, AcademyPermissions.SubscriptionRead)).Succeeded &&
        (await authorization.AuthorizeAsync(actor, AcademyPermissions.SubscriptionRenewManage)).Succeeded;

    private static async Task<IResult> AdminPayment(Guid paymentId, CurrentTenant tenant, FoundationDbContext db, TimeProvider time,
        ClaimsPrincipal actor, IAuthorizationService authorization, CashRegisterService cash, IHostEnvironment environment,
        IOptions<InternalTestPaymentOptions> testPaymentOptions, HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        if (!await CanReadStaffRenewalPayment(actor, authorization)) return Results.Forbid();
        var t = (await tenant.ResolveAsync())!; var groupDate = await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
        var payment = await db.PaymentRequests.AsNoTracking()
            .Where(x => x.AcademyId == t.AcademyId && x.Id == paymentId)
            .Select(x => new
            {
                x.Id,
                renewalId = x.RenewalRequestId,
                x.RenewalRequest.SportEnrollmentId,
                x.RenewalRequest.SubscriptionPlanId,
                hasProof = db.PaymentProofs.Any(p => p.AcademyId == t.AcademyId && p.PaymentRequestId == x.Id),
                staffRequest = db.AcademyMemberships.Any(m => m.AcademyId == t.AcademyId && m.UserId == x.RenewalRequest.RequestedByUserId && (m.Role == AcademyRole.AcademyOwner || m.Role == AcademyRole.AcademyAdmin)),
                branchId = x.RenewalRequest.SportEnrollment.BranchId,
                sportId = x.RenewalRequest.SportId,
                groupId = FoundationDbContext.AssignedGroupAt(x.AcademyId, x.RenewalRequest.SportEnrollmentId, groupDate),
                x.ProviderReference,
                x.Provider,
                x.ProviderEnvironment,
                x.RenewalRequest.VipRewardUseId,
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
                x.RenewalRequest.DiscountReason,
                x.RenewalRequest.FinalAmount,
                x.CreatedAtUtc,
                x.ConfirmedAtUtc,
                currentPaymentId = x.RenewalRequest.PaymentRequestId,
                isCurrent = x.RenewalRequest.PaymentRequestId == x.Id,
                periodId = db.SubscriptionPeriods.Where(p=>p.AcademyId==t.AcademyId&&p.CollectionId!=null&&db.Collections.Any(c=>c.Id==p.CollectionId&&c.PaymentRequestId==x.Id)).Select(p=>(Guid?)p.Id).SingleOrDefault(),
                receiptId = db.Receipts.Where(r => r.AcademyId == t.AcademyId && r.Collection.PaymentRequestId == x.Id).Select(r => (Guid?)r.Id).FirstOrDefault()
            }).SingleOrDefaultAsync();
        if (payment is null) return Results.NotFound();

        var pending = payment.isCurrent && payment.status == nameof(PaymentRequestStatus.Pending);
        Task<bool> CanWrite(string path, string permission = AcademyPermissions.SubscriptionRenewManage) =>
            SubscriptionAdjustmentEndpoints.AllowsPeriodAction(t, db, actor, authorization, permission, path, "POST");
        var canCollect = pending && payment.staffRequest && !payment.hasProof &&
            await CanWrite($"/api/v1/manage/payments/{paymentId}/collect");
        var canCollectCash = canCollect && payment.Amount > 0 && await cash.CanUsePendingCashCollection(t);
        var canReviewProof = pending && payment.hasProof && payment.Provider == "Manual" &&
            await CanWrite($"/api/v1/manage/payments/{paymentId}/review");
        var canRetry = payment.isCurrent && payment.status is ("Failed" or "Cancelled" or "Expired") &&
            await CanWrite($"/api/v1/subscriptions/payments/{paymentId}/retry");
        var canSimulate = pending && payment.Provider == InternalTestPaymentGateway.ProviderName && payment.ProviderEnvironment == "Demo" &&
            testPaymentOptions.Value.Enabled && (environment.IsEnvironment("Demo") || environment.IsEnvironment("Testing")) &&
            await CanWrite($"/api/v1/subscriptions/payments/{paymentId}/simulate");
        var canEditDiscount = pending && payment.VipRewardUseId is null && payment.renewalStatus is ("PendingPayment" or "PaymentInProgress") &&
            await CanWrite($"/api/v1/subscriptions/renewals/{payment.renewalId}/discount", AcademyPermissions.SubscriptionDiscountManage);
        var zone = await db.Academies.Where(a => a.Id == t.AcademyId).Select(a => a.TimeZone).SingleAsync();
        return Results.Ok(new
        {
            payment.Id, payment.renewalId, payment.SportEnrollmentId, payment.SubscriptionPlanId, payment.branchId, payment.sportId, payment.groupId,
            payment.ProviderReference, payment.Provider, payment.Amount, payment.Currency, payment.status, payment.renewalStatus,
            payment.player, payment.playerCode, payment.sport, payment.plan, payment.OriginalAmount, payment.discountType, payment.DiscountValue,
            payment.DiscountAmount, payment.DiscountReason, payment.FinalAmount, payment.CreatedAtUtc, payment.ConfirmedAtUtc,
            payment.currentPaymentId, payment.isCurrent, payment.periodId, payment.receiptId,
            reference = PaymentQueueItem.ShortReference(payment.Id),
            createdOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(payment.CreatedAtUtc, TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime),
            canCollect, canCollectCash, canReviewProof, canRetry, canSimulate, canEditDiscount
        });
    }

    private static async Task<IResult> RetryAdminPayment(Guid paymentId, HttpContext http, CurrentTenant tenant,
        ClaimsPrincipal principal, FoundationDbContext db, RenewalCreationService renewals)
    {
        var t = (await tenant.ResolveAsync())!;
        var key = IdempotencyKey(http);
        if (key is null) return Results.BadRequest(new { message = "Idempotency-Key مطلوب." });
        var old = await db.PaymentRequests.AsNoTracking().Include(x => x.RenewalRequest).ThenInclude(x => x.SubscriptionPlan)
            .SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == paymentId);
        if (old is null) return Results.NotFound();
        if (old.RenewalRequest.PaymentRequestId != old.Id)
            return Results.Conflict(new { message = "استُبدل طلب الدفع. افتح الطلب الحالي." });
        if (old.Status is not (PaymentRequestStatus.Failed or PaymentRequestStatus.Cancelled or PaymentRequestStatus.Expired))
            return Results.Conflict(new { message = "إعادة المحاولة متاحة للعمليات الفاشلة أو الملغاة أو المنتهية فقط." });
        var snapshot = new RenewalFinancialSnapshot(old.RenewalRequest.OriginalAmount, old.RenewalRequest.DiscountType,
            old.RenewalRequest.DiscountValue, old.RenewalRequest.DiscountAmount, old.RenewalRequest.FinalAmount,
            old.RenewalRequest.DiscountReason, old.RenewalRequest.DiscountAppliedByUserId, old.RenewalRequest.DiscountAppliedAtUtc, old.RenewalRequest.DiscountRuleIdSnapshot, old.RenewalRequest.DiscountRuleRevisionSnapshot);
        var result = await renewals.CreateAsync(t.AcademyId, UserId(principal), old.RenewalRequest.SportEnrollmentId,
            old.RenewalRequest.SubscriptionPlanId, key, snapshot, true, fixedTerms: PlanTerms.From(old.RenewalRequest, old.RenewalRequest.SubscriptionPlan));
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
        if (result.State == RenewalCreationState.QuoteChanged)
            return Results.Conflict(new { message = "تغيّر السعر أو الفترة المتوقعة. اختر الباقة مجددًا لمراجعة العرض المحدّث." });
        if (result.State == RenewalCreationState.OpenRenewalExists)
            return Results.Conflict(new { code = "renewal_already_open", message = admin
                ? "يوجد طلب تجديد ودفع جارٍ بالفعل لهذا التسجيل. افتح طلب الدفع الحالي قبل إنشاء طلب آخر."
                : "يوجد طلب تجديد مفتوح لهذا التسجيل. راجع الطلب الحالي أو تواصل مع الأكاديمية." });
        var body = new { renewalId = result.RenewalId, paymentId = result.PaymentId };
        if (result.State == RenewalCreationState.Replay) return Results.Ok(body);
        var location = admin
            ? $"/api/v1/subscriptions/payments/{result.PaymentId}"
            : $"/api/v1/guardian/subscriptions/payments/{result.PaymentId}";
        return Results.Created(location, body);
    }

    private static IQueryable<BeneficiaryRenewalReference> UnrevokedBeneficiaryReferences(FoundationDbContext db, Guid academyId, Guid enrollmentId) =>
        db.BeneficiaryRenewalReferences.Where(x => x.AcademyId == academyId && x.SportEnrollmentId == enrollmentId && x.RevokedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id);

    private static string BeneficiaryReferenceRevision(Guid? id) => id?.ToString("N") ?? "none";

    private static async Task<IResult> BeneficiaryReferenceStatus(Guid enrollmentId, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock, CancellationToken ct)
    {
        var t = (await tenant.ResolveAsync())!;
        if (!await BeneficiaryRenewalCodes.EligibleEnrollments(db, t.AcademyId).AnyAsync(e => e.Id == enrollmentId, ct)) return Results.NotFound();
        var latest = await UnrevokedBeneficiaryReferences(db, t.AcademyId, enrollmentId).AsNoTracking()
            .Select(x => new { x.Id, x.IsActive, x.ExpiresAtUtc }).ToListAsync(ct);
        var active = latest.FirstOrDefault(x => x.IsActive && x.ExpiresAtUtc > clock.UtcNow);
        return Results.Ok(new { revision = BeneficiaryReferenceRevision(latest.FirstOrDefault()?.Id), expiresAtUtc = active?.ExpiresAtUtc });
    }

    private static async Task<IResult> RegenerateBeneficiaryReference(Guid enrollmentId, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock, HttpContext http)
    {
        var t = (await tenant.ResolveAsync())!;
        var ct = http.RequestAborted;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var scope = $"beneficiary-renewal-code:{t.AcademyId:N}:{enrollmentId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({scope}, 0))", ct);
        if (!await BeneficiaryRenewalCodes.EligibleEnrollments(db, t.AcademyId).AnyAsync(e => e.Id == enrollmentId, ct)) return Results.NotFound();
        var current = await UnrevokedBeneficiaryReferences(db, t.AcademyId, enrollmentId).ToListAsync(ct);
        var expected = http.Request.Headers.IfMatch.ToString();
        if (expected.Length != 0 && expected != $"\"{BeneficiaryReferenceRevision(current.FirstOrDefault()?.Id)}\"")
            return Results.Json(new { message = "تغيّرت حالة كود التجديد. راجعها قبل إصدار كود جديد." }, statusCode: StatusCodes.Status412PreconditionFailed);

        var now = clock.UtcNow;
        foreach (var item in current) { item.RevokedAtUtc = now; item.UpdatedAtUtc = now; }
        var raw = BeneficiaryRenewalCodes.Generate();
        var entity = new BeneficiaryRenewalReference { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SportEnrollmentId = enrollmentId, CodeHash = BeneficiaryRenewalCodes.Hash(raw), CodeHint = BeneficiaryRenewalCodes.Hint(raw), ExpiresAtUtc = now.AddYears(1), GeneratedByUserId = UserId(principal), CreatedAtUtc = now, UpdatedAtUtc = now };
        db.BeneficiaryRenewalReferences.Add(entity);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(new { reference = raw, revision = BeneficiaryReferenceRevision(entity.Id), entity.CodeHint, entity.ExpiresAtUtc, message = "احفظ الكود الآن؛ لن يظهر كاملًا مرة أخرى." });
    }

    private static string? IdempotencyKey(HttpContext http) { var key = http.Request.Headers["Idempotency-Key"].ToString(); return string.IsNullOrWhiteSpace(key) || key.Length > 100 ? null : key; }
    private static async Task<object?> ExistingRenewal(FoundationDbContext db, Guid academy, Guid user, string key) => await db.RenewalRequests.AsNoTracking().Where(x => x.AcademyId == academy && x.RequestedByUserId == user && x.IdempotencyKey == key).Select(x => new { renewalId = x.Id, paymentId = x.PaymentRequestId }).SingleOrDefaultAsync();
    private static Task<SportEnrollment?> OwnEnrollment(FoundationDbContext db, Guid academy, Guid user, Guid id) =>
        db.SportEnrollments.SingleOrDefaultAsync(e => e.AcademyId == academy && e.Id == id && e.IsActive &&
            e.Status == EnrollmentStatus.Active && e.Player.IsActive && db.GuardianPlayerLinks.Any(l =>
                l.AcademyId == academy && l.Guardian.UserId == user && l.Guardian.IsActive && l.PlayerId == e.PlayerId && l.IsActive));
    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public sealed record PlanRequest(string ArabicName, string? EnglishName, Guid SportId, SubscriptionPlanType PlanType, [property: System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)] decimal Price, int? DurationDays, int? SessionCount, int DisplayOrder = 0, bool AllBranches = true, Guid[]? BranchIds = null, int? ExpectedRevision = null,
    AcademyServiceKind ServiceKind = AcademyServiceKind.Training, TrainingFormat TrainingFormat = TrainingFormat.Group, int? DurationMonths = null,
    [property: System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)] decimal? ReferenceMonthlyPrice = null, int? ChargedMonths = null);
public sealed record PlanStatusRequest(bool IsActive);
public sealed record RenewalCreateRequest(Guid SportEnrollmentId, Guid SubscriptionPlanId);
public sealed record AdminRenewalCreateRequest(Guid SportEnrollmentId, Guid SubscriptionPlanId, string? ExpectedQuoteHash = null);
public sealed record SimulationRequest(string Outcome);
public sealed record ExternalReferenceRequest(string Reference);
public sealed record ExternalSearchRequest(string? Name);
public sealed record ExternalRenewalRequest(string Reference, Guid SubscriptionPlanId);
