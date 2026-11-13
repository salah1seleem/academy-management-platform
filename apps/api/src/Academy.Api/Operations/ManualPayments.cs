using System.Data;
using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Api.Slice8;
using Academy.Api.Finance;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class ManualPayments
{
    public static void MapManualPayments(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        api.MapGet("/payment-settings", Settings).RequireAuthorization(AcademyPermissions.TenantAccess);
        api.MapPut("/payment-settings", SaveSettings).RequireAuthorization(AcademyPermissions.AcademySettingsManage).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/guardian/payments/{id:guid}/proof", SubmitProof).RequireAuthorization(AcademyPermissions.GuardianOwnRenewal).AddEndpointFilter<CsrfFilter>();
        api.MapGet("/manage/payments/{id:guid}/proof", GetProof).RequireAuthorization(AcademyPermissions.PaymentRead);
        api.MapPost("/manage/payments/{id:guid}/review", Review).RequireAuthorization(AcademyPermissions.SubscriptionRenewManage).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/manage/payments/{id:guid}/collect", CollectStaff).RequireAuthorization(AcademyPermissions.SubscriptionRenewManage).AddEndpointFilter<CsrfFilter>();
        api.MapGet("/manage/payments/{id:guid}/cash-options", async (Guid id, CashRegisterService cash) => {
            try { return Results.Ok(await cash.PendingPaymentOptions(id)); }
            catch (CashRejected e) { return Results.Problem(statusCode: e.Status, title: e.Message); }
        }).RequireAuthorization(AcademyPermissions.SubscriptionRenewManage);
        api.MapGet("/manage/payment-requests", Queue).RequireAuthorization(AcademyPermissions.PaymentRead);
        api.MapPost("/guardian/enrollment-requests/{id:guid}/proof", EnrollmentProof).RequireAuthorization(AcademyPermissions.GuardianOwnRenewal).AddEndpointFilter<CsrfFilter>();
        api.MapGet("/guardian/enrollment-requests/{id:guid}/payment", EnrollmentPayment).RequireAuthorization(AcademyPermissions.GuardianOwnRenewal);
        api.MapGet("/manage/enrollment-requests/{id:guid}/payment", EnrollmentPaymentStaff).RequireAuthorization(AcademyPermissions.PaymentRead);
        api.MapGet("/notices", async (CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db) => { var t = (await tenant.ResolveAsync())!; var user = Actor(principal); return Results.Ok(await NoticeEndpoints.OwnNotices(db,t.AcademyId,user,t.Role).OrderByDescending(x => x.CreatedAtUtc).Take(50).Select(x => new { x.Id, x.Message, x.IsRead, x.CreatedAtUtc }).ToListAsync()); }).RequireAuthorization(AcademyPermissions.TenantAccess);
        api.MapGet("/people/players/{id:guid}/refunds", Refunds).RequireAuthorization(AcademyPermissions.SubscriptionAdjustmentManage);
        api.MapPost("/people/players/{id:guid}/refunds", Refund).RequireAuthorization(AcademyPermissions.SubscriptionAdjustmentManage).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/people/players/{id:guid}/refunds/quote", async (Guid id, RefundWrite body, CashRegisterService cash) => {
            try { return Results.Ok(await cash.RefundQuote(id,body)); }
            catch(CashRejected error) { return Results.Problem(statusCode:error.Status,title:error.Message); }
        }).RequireAuthorization(AcademyPermissions.SubscriptionAdjustmentManage).AddEndpointFilter<CsrfFilter>();
    }
    private static Guid Actor(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static async Task<IResult> CollectStaff(Guid id, CollectWrite body, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, CollectionService service, TimeProvider time, CashRegisterService cash)
    {
        if (body.PaymentMethod is not ("Cash" or "Online")) return Results.UnprocessableEntity();
        if (body.PaymentMethod == "Cash")
        {
            try { return Results.Ok(await cash.CollectPendingPayment(id, body.CashSessionId, body.ExpectedAmount, service)); }
            catch (CashRejected e) { return Results.Problem(statusCode: e.Status, title: e.Message); }
        }
        var t = (await tenant.ResolveAsync())!; var actor = Actor(principal);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"collection:{t.AcademyId}:{id}"}, 0))");
        var p = await db.PaymentRequests.Include(x => x.RenewalRequest).ThenInclude(x => x.SubscriptionPlan).Include(x => x.RenewalRequest).ThenInclude(x => x.SportEnrollment).ThenInclude(x => x.TrainingGroup).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id);
        if (p is null) return Results.NotFound();
        if(body.ExpectedAmount is not null) {
            if(!decimal.TryParse(body.ExpectedAmount,System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out var expected)||expected<0||decimal.Round(expected,2)!=expected)
                return Results.UnprocessableEntity(new{message="راجع مبلغ الطلب الصحيح بحد أقصى منزلتين عشريتين."});
            if(expected!=p.Amount)return Results.Conflict(new{message="تغير المبلغ. حدّث الطلب وراجع الإجمالي قبل التأكيد."});
        }
        if (!await db.AcademyMemberships.AnyAsync(x => x.AcademyId == t.AcademyId && x.UserId == p.RenewalRequest.RequestedByUserId && (x.Role == AcademyRole.AcademyOwner || x.Role == AcademyRole.AcademyAdmin))) return Results.Forbid();
        if (await db.PaymentProofs.AnyAsync(x => x.AcademyId == t.AcademyId && x.PaymentRequestId == id)) return Results.Conflict(new { message = "استخدم مراجعة إثبات الدفع لهذا الطلب." });
        if (p.Status == PaymentRequestStatus.Confirmed) { var existing = await db.Receipts.Where(x => x.AcademyId == t.AcademyId && x.Collection.PaymentRequestId == id).Select(x => new { x.Id, x.PaymentMethod }).SingleAsync(); return existing.PaymentMethod != body.PaymentMethod ? Results.Conflict(new { message = "طلب الدفع محصل بطريقة أخرى." }) : Results.Ok(new { receiptId = existing.Id, replay = true }); }
        var day = await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
        if (p.Status != PaymentRequestStatus.Pending || p.RenewalRequest.PaymentRequestId != id || !await db.TrainingGroups.AnyAsync(g => g.Id == FoundationDbContext.AssignedGroupAt(t.AcademyId, p.RenewalRequest.SportEnrollmentId, day) && g.IsActive)) return Results.Conflict();
        if (!PlanCatalog.FormatMatches(p.RenewalRequest.SportEnrollment.RegistrationFormat, PlanTerms.From(p.RenewalRequest, p.RenewalRequest.SubscriptionPlan).Format)) return Results.Conflict(new { message = "الباقة المحفوظة لا تطابق نوع التسجيل." });
        p.Provider = "Manual"; p.ProviderEnvironment = "Manual";
        var receipt = await service.ConfirmAsync(t.AcademyId, actor, id, body.PaymentMethod); await tx.CommitAsync(); return Results.Ok(new { receiptId = receipt });
    }
    private sealed class QueueRow
    {
        public Guid Id { get; init; }
        public required string Type { get; init; }
        public required string Player { get; init; }
        public required string Sport { get; init; }
        public required string Branch { get; init; }
        public required string Plan { get; init; }
        public decimal? Amount { get; init; }
        public string? Currency { get; init; }
        public required string Status { get; init; }
        public bool HasProof { get; init; }
        public DateTimeOffset CreatedAtUtc { get; init; }
    }

    private static async Task<IResult> Queue(DateOnly? from, DateOnly? to, bool? allDates, Guid? branchId, string? type, string? status, string? reference,
        int? page, int? pageSize, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock, ClaimsPrincipal actor,
        IAuthorizationService authorization, HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        var t = (await tenant.ResolveAsync())!;
        var academy = t.AcademyId;
        var currentPage = page ?? 1;
        var size = pageSize ?? 100;
        var term = reference?.Trim() ?? "";
        var fullReference = Guid.TryParseExact(term, "D", out var referenceId);
        if (currentPage is < 1 or > 100_000 || size is not (10 or 25 or 50 or 100) ||
            type is not (null or "" or "Renewal" or "Enrollment" or "Product" or "Tournament") ||
            status is not (null or "" or "Pending" or "Approved" or "Rejected" or "Cancelled"))
            return Results.UnprocessableEntity(new { message = "اختر نوع طلب وحالة وصفحة صحيحة." });
        if (term.Length > 0 && !fullReference && !PaymentQueueItem.IsShortReference(term))
            return Results.UnprocessableEntity(new { message = "أدخل رقم الطلب كاملًا كما يظهر في تفاصيله." });
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        if (branchId.HasValue && !await db.Branches.AnyAsync(b => b.AcademyId == academy && b.Id == branchId))
            return Results.NotFound();
        var zone = await db.Academies.Where(x => x.Id == academy).Select(x => x.TimeZone).SingleAsync();
        var academyZone = TimeZoneInfo.FindSystemTimeZoneById(zone);
        var today = clock.Today;
        var first = from ?? new DateOnly(today.Year, today.Month, 1);
        var last = to ?? today;
        var all = allDates == true && from is null && to is null;
        if (!all && (first.Year < 2000 || last.Year > 2100 || first > last || last.DayNumber - first.DayNumber > 366))
            return Results.UnprocessableEntity(new { message = "اختر فترة صحيحة لا تتجاوز 366 يومًا." });
        var bounds = all ? (Start: DateTimeOffset.MinValue, EndExclusive: DateTimeOffset.MaxValue) : ReportingEndpoints.UtcRange(first, last, zone);

        var renewals = db.PaymentRequests.Where(x => x.AcademyId == academy && x.Id == x.RenewalRequest.PaymentRequestId &&
            x.CreatedAtUtc >= bounds.Start && x.CreatedAtUtc < bounds.EndExclusive &&
            (!branchId.HasValue || x.RenewalRequest.SportEnrollment.BranchId == branchId) &&
            !db.NewEnrollmentRequests.Any(n => n.AcademyId == academy && n.PaymentRequestId == x.Id) &&
            !db.FamilyRenewalLines.Any(l => l.AcademyId == academy && l.ResultPaymentId == x.Id))
            .Select(x => new QueueRow
            {
                Id = x.Id, Type = "Renewal", CreatedAtUtc = x.CreatedAtUtc,
                Player = x.RenewalRequest.SportEnrollment.Player.ArabicName,
                Sport = x.RenewalRequest.SubscriptionPlan.Sport.ArabicName,
                Branch = x.RenewalRequest.SportEnrollment.Branch.ArabicName,
                Plan = x.RenewalRequest.PlanNameSnapshot ?? x.RenewalRequest.SubscriptionPlan.ArabicName,
                Amount = x.Amount, Currency = x.Currency,
                Status = x.Status == PaymentRequestStatus.Confirmed ? "Approved" :
                    x.Status == PaymentRequestStatus.Pending || x.Status == PaymentRequestStatus.Created ? "Pending" :
                    x.Status == PaymentRequestStatus.Cancelled && !db.PaymentProofs.Any(p => p.AcademyId == academy && p.PaymentRequestId == x.Id && p.Status == "Rejected")
                        ? "Cancelled" : "Rejected",
                HasProof = db.PaymentProofs.Any(p => p.AcademyId == academy && p.PaymentRequestId == x.Id)
            });
        var families = db.FamilyRenewalRequests.Where(x => x.AcademyId == academy &&
            x.CreatedAtUtc >= bounds.Start && x.CreatedAtUtc < bounds.EndExclusive &&
            (!branchId.HasValue || x.Lines.Any(l => l.BranchId == branchId)))
            .Select(x => new QueueRow
            {
                Id = x.Id, Type = "FamilyRenewal", CreatedAtUtc = x.CreatedAtUtc, Player = x.Guardian.DisplayName,
                Sport = "تجديد الأسرة", Branch = "فروع التسجيلات", Plan = "تجديد " + x.LineCount + " تسجيل",
                Amount = x.TotalAmount, Currency = x.Currency, HasProof = x.ProofAssetId != null,
                Status = x.Status == FamilyRenewalStatus.Approved ? "Approved" :
                    x.Status == FamilyRenewalStatus.Pending ? "Pending" :
                    x.Status == FamilyRenewalStatus.Cancelled ? "Cancelled" : "Rejected"
            });
        var enrollments = db.NewEnrollmentRequests.Where(x => x.AcademyId == academy &&
            x.CreatedAtUtc >= bounds.Start && x.CreatedAtUtc < bounds.EndExclusive && (!branchId.HasValue || x.PreferredBranchId == branchId))
            .Select(x => new QueueRow
            {
                Id = x.Id, Type = "Enrollment", CreatedAtUtc = x.CreatedAtUtc,
                Player = x.ExistingPlayer == null ? x.NewChildArabicName! : x.ExistingPlayer.ArabicName,
                Sport = x.Sport.ArabicName, Branch = x.PreferredBranch.ArabicName,
                Plan = db.SubscriptionPlans.Where(p => p.AcademyId == academy && p.Id == x.SubscriptionPlanId)
                    .Select(p => p.ArabicName).FirstOrDefault() ?? "لم تُختر باقة",
                Amount = x.FinalAmount, Currency = x.Currency, HasProof = x.ProofAssetId != null,
                Status = x.Status == NewEnrollmentRequestStatus.Approved ? "Approved" :
                    x.Status == NewEnrollmentRequestStatus.Rejected ? "Rejected" :
                    x.Status == NewEnrollmentRequestStatus.Cancelled ? "Cancelled" : "Pending"
            });
        var sales = db.ProductSales.Where(x => x.AcademyId == academy && x.CreatedAtUtc >= bounds.Start &&
            x.CreatedAtUtc < bounds.EndExclusive && (!branchId.HasValue || x.BranchId == branchId))
            .Select(x => new QueueRow
            {
                Id = x.Id, Type = "Product", CreatedAtUtc = x.CreatedAtUtc,
                Player = x.PlayerId == null
                    ? db.Users.Where(u => u.Id == x.RequestedByUserId).Select(u => u.DisplayName).FirstOrDefault() ?? "عميل"
                    : db.Players.Where(p => p.AcademyId == academy && p.Id == x.PlayerId).Select(p => p.ArabicName).FirstOrDefault()!,
                Sport = x.SportName, Branch = x.BranchName ?? "—", Plan = x.ProductName,
                Amount = x.Amount, Currency = x.Currency, HasProof = x.ProofAssetId != null,
                Status = x.Status == "Confirmed" ? "Approved" : x.Status
            });
        var tournaments = db.TournamentPayments.Where(x => x.AcademyId == academy &&
            x.CreatedAtUtc >= bounds.Start && x.CreatedAtUtc < bounds.EndExclusive)
            .Join(db.SportEnrollments.Where(e => !branchId.HasValue || e.BranchId == branchId),
                p => new { p.AcademyId, Id = p.EnrollmentId }, e => new { e.AcademyId, e.Id },
                (p, e) => new QueueRow
                {
                    Id = p.Id, Type = "Tournament", CreatedAtUtc = p.CreatedAtUtc,
                    Player = e.Player.ArabicName, Sport = e.Sport.ArabicName, Branch = e.Branch.ArabicName, Plan = p.TournamentName,
                    Amount = p.Amount, Currency = p.Currency, HasProof = p.ProofAssetId != null,
                    Status = p.Status == "Confirmed" ? "Approved" : p.Status
                });
        var combined = renewals.Concat(families).Concat(enrollments).Concat(sales).Concat(tournaments);
        var pending = await combined.Where(x => x.Status == "Pending").GroupBy(x => x.Type)
            .Select(group => new { Type = group.Key, Count = group.Count() }).ToDictionaryAsync(group => group.Type, group => group.Count);
        var nr = pending.GetValueOrDefault("Renewal") + pending.GetValueOrDefault("FamilyRenewal");
        var nn = pending.GetValueOrDefault("Enrollment");
        var ns = pending.GetValueOrDefault("Product");
        var nt = pending.GetValueOrDefault("Tournament");
        var filtered = type switch
        {
            "Renewal" => renewals.Concat(families),
            "Enrollment" => enrollments,
            "Product" => sales,
            "Tournament" => tournaments,
            _ => combined
        };
        if (!string.IsNullOrEmpty(status)) filtered = filtered.Where(x => x.Status == status);
        if (fullReference) filtered = filtered.Where(x => x.Id == referenceId);
        else if (term.Length > 0)
        {
            var prefix = term[..6].ToLowerInvariant();
            var suffix = term[6..].ToLowerInvariant();
            filtered = filtered.Where(x => x.Id.ToString().StartsWith(prefix) && x.Id.ToString().EndsWith(suffix));
        }
        var total = await filtered.CountAsync();
        var pageRows = await filtered.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).ThenBy(x => x.Type)
            .Skip((currentPage - 1) * size).Take(size).ToArrayAsync();
        var displayed = pageRows.Select(x => new PaymentQueueItem(x.Id, x.Type, x.Player, x.Sport, x.Branch, x.Plan,
            x.Amount, x.Currency, x.Status, x.HasProof, x.CreatedAtUtc)).ToArray();
        var repeatedReferences = displayed.GroupBy(x => x.Reference).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        var renewal = displayed.FirstOrDefault(x => x.Type == "Renewal");
        var enrollment = displayed.FirstOrDefault(x => x.Type == "Enrollment");
        var family = displayed.FirstOrDefault(x => x.Type == "FamilyRenewal");
        var product = displayed.FirstOrDefault(x => x.Type == "Product");
        var tournament = displayed.FirstOrDefault(x => x.Type == "Tournament");
        var canOpenRenewal = renewal is not null && await Slice3Endpoints.CanReadStaffRenewalPayment(actor, authorization) &&
            await SubscriptionAdjustmentEndpoints.AllowsPeriodAction(t, db, actor, authorization,
                AcademyPermissions.PaymentRead, $"/api/v1/subscriptions/payments/{renewal.Id}", "GET");
        var canReviewRenewalProof = renewal is not null && canOpenRenewal &&
            await SubscriptionAdjustmentEndpoints.AllowsPeriodAction(t, db, actor, authorization,
                AcademyPermissions.SubscriptionRenewManage, $"/api/v1/manage/payments/{renewal.Id}/review", "POST");
        var canOpenEnrollment = enrollment is not null &&
            await SubscriptionAdjustmentEndpoints.AllowsPeriodAction(t, db, actor, authorization,
                AcademyPermissions.EnrollmentRequestRead, $"/api/v1/enrollment-requests/{enrollment.Id}", "GET");
        var canReviewEnrollment = enrollment is not null && canOpenEnrollment &&
            await SubscriptionAdjustmentEndpoints.AllowsPeriodAction(t, db, actor, authorization,
                AcademyPermissions.EnrollmentRequestManage, $"/api/v1/enrollment-requests/{enrollment.Id}/review", "POST");
        var canReviewFamily = family is not null &&
            await FamilyRequestService.CanReview(t, db, response.HttpContext, authorization, family.Id);
        var canOpenProduct = product is not null && await SubscriptionAdjustmentEndpoints.AllowsPeriodAction(t, db, actor, authorization,
            AcademyPermissions.PaymentRead, $"/api/v1/manage/product-sales/{product.Id}", "GET");
        var canOpenTournament = tournament is not null && await SubscriptionAdjustmentEndpoints.AllowsPeriodAction(t, db, actor, authorization,
            AcademyPermissions.PaymentRead, $"/api/v1/manage/tournament-payments/{tournament.Id}", "GET");
        return Results.Ok(new
        {
            from = first, to = last, pending = nr + nn + ns + nt, newEnrollments = nn, renewals = nr, products = ns, tournaments = nt,
            canReviewRenewalProof, total, page = currentPage, pageSize = size,
            items = displayed.Select(x => x with
            {
                CreatedOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.CreatedAtUtc, academyZone).DateTime),
                Reference = repeatedReferences.Contains(x.Reference) ? x.Id.ToString("D").ToUpperInvariant() : x.Reference,
                CanOpen = x.Type switch
                {
                    "Renewal" => canOpenRenewal,
                    "Enrollment" => canOpenEnrollment,
                    "Product" => canOpenProduct,
                    "Tournament" => canOpenTournament,
                    _ => true
                },
                CanReview = x.Status == "Pending" ? x.Type switch
                {
                    "Renewal" => canReviewRenewalProof,
                    "Enrollment" => canReviewEnrollment,
                    "FamilyRenewal" => canReviewFamily,
                    _ => null
                } : null
            })
        });
    }
    private static async Task<IResult> EnrollmentProof(Guid id, ProofWrite body, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var user = Actor(principal); await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"new-enrollment-review:{t.AcademyId}:{id}"}, 0))");
        var row = await db.NewEnrollmentRequests.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.RequestedByGuardianUserId == user);
        if (row is null || !await db.PrivateAssets.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.AssetId && x.UploadedByUserId == user && x.Purpose == "Proof")) return Results.NotFound();
        if (row.ProofAssetId == body.AssetId) return Results.Ok(new { status = row.Status.ToString(), replay = true });
        if (!row.SubscriptionPlanId.HasValue || row.Status is not (NewEnrollmentRequestStatus.Pending or NewEnrollmentRequestStatus.UnderReview))
            return Results.Conflict(new { message = "هذا الطلب لا ينتظر إثبات دفع جديدًا. حدّث حالة الطلب." });
        row.ProofAssetId = body.AssetId; row.UpdatedAtUtc = clock.UtcNow;
        await NoticeText.PaymentProof(db,t.AcademyId,id,"اشتراك جديد",clock.UtcNow);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { status = row.Status.ToString(), message = "وصل إثبات الدفع للإدارة. يتم تفعيل الاشتراك بعد قبوله وإسناد المجموعة." });
    }
    private static Task<IResult> EnrollmentPayment(Guid id, CurrentTenant t, ClaimsPrincipal p, FoundationDbContext db, HttpResponse response) =>
        EnrollmentPaymentCore(id, t, db, response, Actor(p), true);
    private static async Task<IResult> EnrollmentPaymentStaff(Guid id, CurrentTenant tenant, FoundationDbContext db, HttpContext http,
        IAuthorizationService authorization)
    {
        var t = (await tenant.ResolveAsync())!;
        var canReadReceipt = await SubscriptionAdjustmentEndpoints.AllowsPeriodAction(t, db, http.User, authorization,
            AcademyPermissions.CollectionRead, $"/api/v1/subscriptions/receipts/{id}", "GET");
        return await EnrollmentPaymentCore(id, tenant, db, http.Response, null, canReadReceipt);
    }
    private static async Task<IResult> EnrollmentPaymentCore(Guid id, CurrentTenant tenant, FoundationDbContext db, HttpResponse response, Guid? owner, bool canReadReceipt)
    {
        response.Headers.CacheControl = "private, no-store";
        var t = (await tenant.ResolveAsync())!;
        var row = await db.NewEnrollmentRequests.AsNoTracking()
            .Where(x => x.AcademyId == t.AcademyId && x.Id == id && (!owner.HasValue || x.RequestedByGuardianUserId == owner))
            .Select(x => new {
                x.Id, x.CreatedAtUtc, child = x.ExistingPlayer != null ? x.ExistingPlayer.ArabicName : x.NewChildArabicName,
                guardian = x.RequestedByGuardianUser.DisplayName, sport = x.Sport.ArabicName, branch = x.PreferredBranch.ArabicName,
                x.SubscriptionPlanId, x.OriginalAmount, x.DiscountAmount, x.DiscountValue, x.DiscountReason,
                discountType = x.DiscountType == null ? null : x.DiscountType.ToString(), amount = x.FinalAmount, x.Currency,
                status = x.Status.ToString(), x.GuardianVisibleReason, x.CreatedSportEnrollmentId, x.PaymentRequestId,
                x.ProofAssetId, proofUrl = x.ProofAssetId == null ? null : "/api/v1/assets/" + x.ProofAssetId,
                receiptId = canReadReceipt && x.PaymentRequestId.HasValue
                    ? db.Receipts.Where(r => r.AcademyId == t.AcademyId && r.Collection.PaymentRequestId == x.PaymentRequestId)
                        .Select(r => (Guid?)r.Id).FirstOrDefault()
                    : null
            }).SingleOrDefaultAsync();
        if (row is null) return Results.NotFound();
        var zone = await db.Academies.Where(a => a.Id == t.AcademyId).Select(a => a.TimeZone).SingleAsync();
        return Results.Ok(new
        {
            row.Id, row.child, row.guardian, row.sport, row.branch, row.SubscriptionPlanId,
            row.OriginalAmount, row.DiscountAmount, row.DiscountValue, row.DiscountReason, row.discountType,
            row.amount, row.Currency, row.status, row.GuardianVisibleReason, row.CreatedSportEnrollmentId, row.PaymentRequestId,
            row.ProofAssetId, row.proofUrl, row.receiptId,
            reference = PaymentQueueItem.ShortReference(row.Id),
            createdOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(row.CreatedAtUtc, TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime)
        });
    }
    private static async Task<IResult> Settings(CurrentTenant tenant, FoundationDbContext db) { var t = (await tenant.ResolveAsync())!; var row = await db.AcademyTransferSettings.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId); return Results.Ok(new { row?.InstaPayAddress, row?.WalletNumber, row?.RecipientName, isActive = row?.IsActive == true, configured = row?.IsActive == true && (!string.IsNullOrWhiteSpace(row.InstaPayAddress) || !string.IsNullOrWhiteSpace(row.WalletNumber)) }); }
    private static async Task<IResult> SaveSettings(TransferWrite body, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!;
        if (body.InstaPayAddress?.Length > 120 || body.WalletNumber?.Length > 30 || body.RecipientName?.Length > 120) return Results.UnprocessableEntity();
        var row = await db.AcademyTransferSettings.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId);
        if (row is null) { row = new AcademyTransferSettings { Id = Guid.NewGuid(), AcademyId = t.AcademyId, CreatedAtUtc = clock.UtcNow }; db.AcademyTransferSettings.Add(row); }
        row.InstaPayAddress = body.InstaPayAddress?.Trim(); row.WalletNumber = body.WalletNumber?.Trim(); row.RecipientName = body.RecipientName?.Trim(); row.IsActive = body.IsActive; row.UpdatedAtUtc = clock.UtcNow; await db.SaveChangesAsync(); return Results.NoContent();
    }
    private static async Task<IResult> SubmitProof(Guid id, ProofWrite body, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var user = Actor(principal); await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"collection:{t.AcademyId}:{id}"}, 0))");
        var payment = await db.PaymentRequests.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.RenewalRequest.RequestedByUserId == user);
        if (payment is null || !await db.PrivateAssets.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.AssetId && x.UploadedByUserId == user && x.Purpose == "Proof")) return Results.NotFound();
        if (!await db.RenewalRequests.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == payment.RenewalRequestId && x.PaymentRequestId == id))
            return Results.Conflict(new { message = "افتح طلب الدفع الحالي لإرسال إثبات التحويل." });
        var proof = await db.PaymentProofs.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.PaymentRequestId == id);
        if (proof?.AssetId == body.AssetId)
            return Results.Ok(new { proof.Id, proof.Status, replay = true });
        if (payment.Status != PaymentRequestStatus.Pending) return Results.Conflict(new { message = "طلب الدفع لم يعد ينتظر إثباتًا." });
        if (proof is null) { proof = new PaymentProof { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PaymentRequestId = id, AssetId = body.AssetId, SubmittedByUserId = user, Status = "Pending", CreatedAtUtc = clock.UtcNow }; db.PaymentProofs.Add(proof); }
        else if (proof.Status != "Pending") return Results.Conflict();
        proof.AssetId = body.AssetId; proof.UpdatedAtUtc = clock.UtcNow;
        payment.Provider = "Manual"; payment.ProviderEnvironment = "Manual"; payment.UpdatedAtUtc = clock.UtcNow;
        await NoticeText.PaymentProof(db,t.AcademyId,id,"تجديد الاشتراك",clock.UtcNow);
        await db.SaveChangesAsync(); await transaction.CommitAsync(); return Results.Ok(new { proof.Id, proof.Status, message = "وصل إثبات الدفع إلى الإدارة. يبدأ الاشتراك بعد الموافقة." });
    }
    private static async Task<IResult> GetProof(Guid id, CurrentTenant tenant, FoundationDbContext db, HttpResponse response)
    {
        response.Headers.CacheControl = "private, no-store";
        var t = (await tenant.ResolveAsync())!;
        var row = await db.PaymentProofs.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.PaymentRequestId == id)
            .Select(x => new { x.Id, x.AssetId, x.Status, x.ReviewReason, x.ReviewedAtUtc, url = "/api/v1/assets/" + x.AssetId }).SingleOrDefaultAsync();
        return row is null ? Results.NotFound() : Results.Ok(row);
    }
    private static async Task<IResult> Review(Guid id, ReviewWrite body, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, CollectionService collections, ISubscriptionClock clock, GroupPlacementService placement, TimeProvider time)
    {
        var t = (await tenant.ResolveAsync())!; var user = Actor(principal);
        if (body.Reason?.Length > 500) return Results.UnprocessableEntity(new { message = "اكتب ملاحظة من 500 حرف أو أقل." });
        var reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"collection:{t.AcademyId}:{id}"}, 0))");
        var payment = await db.PaymentRequests.Include(x => x.RenewalRequest).ThenInclude(x => x.SubscriptionPlan).Include(x => x.RenewalRequest).ThenInclude(x => x.SportEnrollment).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id);
        var proof = await db.PaymentProofs.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.PaymentRequestId == id);
        if (payment is null || proof is null) return Results.NotFound();
        if (body.ExpectedProofAssetId != proof.AssetId)
            return Results.Conflict(new { message = "تغيّر إثبات التحويل. افتح الإثبات الحالي وراجعه قبل تأكيد القرار.", code = "renewal_proof_changed" });
        if (proof.Status != "Pending")
            return proof.Status == (body.Accept ? "Accepted" : "Rejected") && (proof.ReviewReason ?? "") == (reason ?? "")
                ? Results.Ok(new { proof.Status, replay = true })
                : Results.Conflict(new { message = "سُجّل قرار مختلف لهذا الإثبات. راجع حالة الطلب الحالية." });
        if (payment.Status != PaymentRequestStatus.Pending || payment.RenewalRequest.PaymentRequestId != id || payment.Provider != "Manual") return Results.Conflict(new { message = "طلب الدفع تغير. حدّث الصفحة." });
        Guid? receipt = null;
        if (body.Accept)
        {
            if (body.GroupId is not Guid groupId) return Results.UnprocessableEntity(new { message = "اختر مجموعة اللاعب قبل القبول." });
            var enrollment = payment.RenewalRequest.SportEnrollment;
            var day = await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
            var assignedGroup = await db.SportEnrollments.Where(e => e.Id == enrollment.Id).Select(e => FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, day)).SingleAsync();
            if (assignedGroup.HasValue && assignedGroup != groupId) return Results.Conflict(new { message = "استخدم النقل المؤرخ أولًا؛ قبول الدفع لا يغير مجموعة اللاعب." });
            var group = await db.TrainingGroups.Include(x => x.AgeCategory).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == groupId && x.IsActive && x.Branch.IsActive && x.Sport.IsActive && x.AgeCategory.IsActive);
            if (group is null || group.SportId != enrollment.SportId || group.BranchId != enrollment.BranchId || group.RegistrationFormat != enrollment.RegistrationFormat || !PlanCatalog.FormatMatches(enrollment.RegistrationFormat, PlanTerms.From(payment.RenewalRequest, payment.RenewalRequest.SubscriptionPlan).Format)) return Results.UnprocessableEntity(new { message = "المجموعة أو الباقة غير متوافقة مع نوع التسجيل." });
            var birthYear = await db.Players.Where(x => x.AcademyId == t.AcademyId && x.Id == enrollment.PlayerId).Select(x => x.DateOfBirth.Year).SingleAsync();
            if (group.AgeCategory.MinimumBirthYear > birthYear || group.AgeCategory.MaximumBirthYear < birthYear) return Results.UnprocessableEntity(new { message = "عمر اللاعب غير مناسب للمجموعة." });
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"group-capacity:{t.AcademyId}:{group.Id}"}, 0))");
            if (!assignedGroup.HasValue && !await DatedGroupRoster.CanAdd(db, group.Id, day, group.Capacity)) return Results.Conflict(new { message = "المجموعة مكتملة العدد في الفترة الحالية أو في نقل مجدول." });
            var specialAdmission = group.RegistrationFormat != TrainingRegistrationFormat.Group && enrollment.TrainingGroupId is null;
            if (specialAdmission) { try { group = await placement.PrepareSpecialAdmission(group.Id); } catch (Academy.Api.Facilities.FacilityRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); } }
            else if (!enrollment.TrainingGroupId.HasValue) enrollment.TrainingGroupId = groupId;
            enrollment.UpdatedAtUtc = clock.UtcNow;
            receipt = await collections.ConfirmAsync(t.AcademyId, user, id, "Online");
            if (specialAdmission) { try { await placement.FinishSpecialAdmission(group, enrollment); } catch (Academy.Api.Facilities.FacilityRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); } }
        }
        else { payment.Status = PaymentRequestStatus.Cancelled; payment.RenewalRequest.Status = RenewalRequestStatus.Cancelled; }
        proof.Status = body.Accept ? "Accepted" : "Rejected"; proof.ReviewReason = reason; proof.ReviewedByUserId = user; proof.ReviewedAtUtc = clock.UtcNow; proof.UpdatedAtUtc = clock.UtcNow;
        db.AcademyNotices.Add(new AcademyNotice { EventKey="Payment", Id = Guid.NewGuid(), AcademyId = t.AcademyId, UserId = payment.RenewalRequest.RequestedByUserId, AudienceRole="Guardian",TargetKind="Payment",TargetId=id,
            Message = await NoticeText.Render(db,t.AcademyId,"Payment","تحديث طلب الدفع.")+"\n"+(body.Accept ? "تم قبول إثبات الدفع وتفعيل الاشتراك بنجاح. يمكنك مراجعة الإيصال ومواعيد التدريب." : $"لم يتم قبول طلب الدفع. {reason ?? "تواصل مع إدارة الأكاديمية للتوضيح."}"), CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
        await db.SaveChangesAsync(); await transaction.CommitAsync(); return Results.Ok(new { receiptId = receipt, proof.Status });
    }
    private static async Task<IResult> Refunds(Guid id, HttpContext context, CurrentTenant tenant, FoundationDbContext db, CashRegisterService cash)
    {
        try { await cash.AuthorizeRefund(readOnly:true); }
        catch(CashRejected error) { return Results.Problem(statusCode:error.Status,title:error.Message); }
        var t = (await tenant.ResolveAsync())!; if (!await db.Players.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == id)) return Results.NotFound();
        var canRecord = true;
        try { await cash.AuthorizeRefund(); }
        catch (CashRejected error) when (error.Status == 403) { canRecord = false; }
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        var hasKey = key.Length is > 0 and <= 100;
        var user = Actor(context.User);
        var refunds = await db.CollectionRefunds.Where(x => x.AcademyId == t.AcademyId && x.PlayerId == id)
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => new {
                x.Id, x.CollectionId, x.Amount, x.Currency, x.PayoutMethod, x.Reason, x.CreatedAtUtc,
                cashSessionId = db.CashRefundPostings.Where(p => p.RefundId == x.Id).Select(p => (Guid?)p.SessionId).SingleOrDefault(),
                matchesAttempt = hasKey && x.PerformedByUserId == user && x.IdempotencyKey == key
            }).ToListAsync();
        // Scope the visible collections first. Aggregate only their own tenant-bound
        // financial usage, including historical branches, without disclosing those rows.
        var records = await db.Collections.Where(x => x.AcademyId == t.AcademyId && x.SportEnrollment.PlayerId == id).OrderByDescending(x => x.ConfirmedAtUtc).Select(x => new {
            x.Id, x.Amount, x.Currency, x.ConfirmedAtUtc, x.PaymentMethod,
            receiptNumber = db.Receipts.Where(r => r.AcademyId == t.AcademyId && r.CollectionId == x.Id).Select(r => r.ReceiptNumber).SingleOrDefault(),
            refunded = db.CollectionRefunds.IgnoreQueryFilters().Where(r => r.AcademyId == t.AcademyId && r.CollectionId == x.Id).Sum(r => (decimal?)r.Amount) ?? 0,
            allocated = db.ReceivableMovements.IgnoreQueryFilters().Where(m => m.AcademyId == t.AcademyId && m.CollectionId == x.Id && m.Kind == ReceivableMovementKind.PaymentAllocation).Sum(m => (decimal?)m.Amount) ?? 0,
        }).ToListAsync();
        var collections = records.Select(c => new { c.Id, c.Amount, c.Currency, c.refunded, c.allocated, c.PaymentMethod, c.receiptNumber, available = CollectionFunds.Available(c.Amount,c.refunded,c.allocated).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture), c.ConfirmedAtUtc });
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { collections, refunds, canRecord });
    }
    private static async Task<IResult> Refund(Guid id, RefundWrite body, HttpContext context, CurrentTenant tenant, FoundationDbContext db, CashRegisterService cash, TimeProvider time)
    {
        try { return await RecordRefund(id,body,context,tenant,db,cash,time); }
        catch(CashRejected error) { return Results.Problem(statusCode:error.Status,title:error.Message); }
    }
    private static async Task<IResult> RecordRefund(Guid id, RefundWrite body, HttpContext context, CurrentTenant tenant, FoundationDbContext db, CashRegisterService cash, TimeProvider time)
    {
        var t = (await tenant.ResolveAsync())!; var user = Actor(context.User); var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || body.Amount <= 0 || body.Amount > 999999999m || decimal.Round(body.Amount, 2) != body.Amount || body.Reason?.Length > 500) return Results.UnprocessableEntity(new { message = "أدخل مبلغ استرداد صحيحًا." });
        await cash.AuthorizeRefund();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"refund-key:{t.AcademyId}:{user}:{key}"}, 0))");
        var replay = await db.CollectionRefunds.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.PerformedByUserId == user && x.IdempotencyKey == key);
        if (replay is not null) {
            var posting=await db.CashRefundPostings.SingleOrDefaultAsync(p=>p.RefundId==replay.Id);
            return replay.CollectionId == body.CollectionId && replay.PlayerId == id && replay.Amount == body.Amount && (string.IsNullOrWhiteSpace(replay.Reason) ? null : replay.Reason.Trim()) == (string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim()) &&
                (posting is null ? body.CashSessionId is null && body.QuoteHash is null : posting.SessionId==body.CashSessionId && posting.RequestHash==CashRegisterService.RefundHash(body))
                ? Results.Ok(new { replay.Id, replay = true }) : Results.Conflict(new { message="معرف الاسترداد مستخدم بتفاصيل أخرى." });
        }
        await CollectionFunds.Lock(db,t.AcademyId,body.CollectionId);
        var collection = await db.Collections.Include(x=>x.SportEnrollment).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == body.CollectionId && x.SportEnrollment.PlayerId == id); if (collection is null) return Results.NotFound();
        var used = await CollectionFunds.Used(db,t.AcademyId,collection.Id);
        if (body.Amount > CollectionFunds.Available(collection.Amount,used.Refunded,used.Allocated)) return Results.Conflict(new { message = "الاسترداد يتجاوز المبلغ غير المسترد وغير المخصص لمطالبة. راجع الحركات المالية أولًا؛ لن يُعاد فتح دين تلقائيًا." });
        var cashPayout=collection.PaymentMethod=="Cash" || body.CashSessionId.HasValue;
        if(cashPayout) await cash.PrepareRefund(id,body,collection,CollectionFunds.Available(collection.Amount,used.Refunded,used.Allocated));
        else if(body.QuoteHash is not null) return Results.UnprocessableEntity(new {message="اختر وردية مراجعة الصرف النقدي."});
        var at=time.GetUtcNow();
        var row = new CollectionRefund { Id = Guid.NewGuid(), AcademyId = t.AcademyId, CollectionId = collection.Id, PlayerId = id, Amount = body.Amount, Currency = collection.Currency, PayoutMethod=cashPayout?"Cash":"Online", Reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim(), PerformedByUserId = user, IdempotencyKey = key, CreatedAtUtc = at, UpdatedAtUtc = at }; db.CollectionRefunds.Add(row); await db.SaveChangesAsync();
        if(cashPayout) await cash.PostRefund(body,row);
        await transaction.CommitAsync(); return Results.Ok(new { row.Id });
    }
}
public sealed record TransferWrite(string? InstaPayAddress, string? WalletNumber, string? RecipientName, bool IsActive);
public sealed record CollectWrite(string PaymentMethod, Guid? CashSessionId = null, string? ExpectedAmount = null);
public sealed record ProofWrite(Guid AssetId);
public sealed record ReviewWrite(bool Accept, Guid? GroupId, string? Reason, Guid? ExpectedProofAssetId);
public sealed record RefundWrite(Guid CollectionId, [property: System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)] decimal Amount, string? Reason, Guid? CashSessionId = null, string? QuoteHash = null);
public sealed record PaymentQueueItem(Guid Id, string Type, string Player, string Sport, string Branch, string Plan, decimal? Amount, string? Currency, string Status, bool HasProof, DateTimeOffset CreatedAtUtc)
{
    public string Reference { get; init; } = ShortReference(Id);
    public bool CanOpen { get; init; } = true;
    public bool? CanReview { get; init; }
    public DateOnly? CreatedOn { get; init; }

    internal static string ShortReference(Guid id)
    {
        var compact = id.ToString("N");
        return (compact[..6] + compact[^6..]).ToUpperInvariant();
    }
    internal static bool IsShortReference(string value) => value.Length == 12 && value.All(Uri.IsHexDigit);
}
