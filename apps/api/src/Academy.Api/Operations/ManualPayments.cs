using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
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
        api.MapGet("/manage/payment-requests", Queue).RequireAuthorization(AcademyPermissions.PaymentRead);
        api.MapPost("/guardian/enrollment-requests/{id:guid}/proof", EnrollmentProof).RequireAuthorization(AcademyPermissions.GuardianOwnRenewal).AddEndpointFilter<CsrfFilter>();
        api.MapGet("/guardian/enrollment-requests/{id:guid}/payment", EnrollmentPayment).RequireAuthorization(AcademyPermissions.GuardianOwnRenewal);
        api.MapGet("/manage/enrollment-requests/{id:guid}/payment", EnrollmentPaymentStaff).RequireAuthorization(AcademyPermissions.PaymentRead);
        api.MapGet("/notices", async (CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db) => { var t = (await tenant.ResolveAsync())!; var user = Actor(principal); return Results.Ok(await db.AcademyNotices.Where(x => x.AcademyId == t.AcademyId && x.UserId == user).OrderByDescending(x => x.CreatedAtUtc).Take(50).Select(x => new { x.Id, x.Message, x.IsRead, x.CreatedAtUtc }).ToListAsync()); }).RequireAuthorization(AcademyPermissions.TenantAccess);
        api.MapGet("/people/players/{id:guid}/refunds", Refunds).RequireAuthorization(AcademyPermissions.SubscriptionAdjustmentManage);
        api.MapPost("/people/players/{id:guid}/refunds", Refund).RequireAuthorization(AcademyPermissions.SubscriptionAdjustmentManage).AddEndpointFilter<CsrfFilter>();
    }
    private static Guid Actor(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static async Task<IResult> CollectStaff(Guid id, CollectWrite body, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, CollectionService service)
    {
        if (body.PaymentMethod is not ("Cash" or "Online")) return Results.UnprocessableEntity();
        var t = (await tenant.ResolveAsync())!; var actor = Actor(principal);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"collection:{t.AcademyId}:{id}"}, 0))");
        var p = await db.PaymentRequests.Include(x => x.RenewalRequest).ThenInclude(x => x.SubscriptionPlan).Include(x => x.RenewalRequest).ThenInclude(x => x.SportEnrollment).ThenInclude(x => x.TrainingGroup).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id);
        if (p is null) return Results.NotFound();
        if (!await db.AcademyMemberships.AnyAsync(x => x.AcademyId == t.AcademyId && x.UserId == p.RenewalRequest.RequestedByUserId && (x.Role == AcademyRole.AcademyOwner || x.Role == AcademyRole.AcademyAdmin))) return Results.Forbid();
        if (await db.PaymentProofs.AnyAsync(x => x.AcademyId == t.AcademyId && x.PaymentRequestId == id)) return Results.Conflict(new { message = "استخدم مراجعة إثبات الدفع لهذا الطلب." });
        if (p.Status == PaymentRequestStatus.Confirmed) { var existing = await db.Receipts.Where(x => x.AcademyId == t.AcademyId && x.Collection.PaymentRequestId == id).Select(x => x.Id).SingleAsync(); return Results.Ok(new { receiptId = existing, replay = true }); }
        if (p.Status != PaymentRequestStatus.Pending || p.RenewalRequest.PaymentRequestId != id || p.RenewalRequest.SportEnrollment.TrainingGroup?.IsActive != true) return Results.Conflict();
        if (!PlanCatalog.FormatMatches(p.RenewalRequest.SportEnrollment.RegistrationFormat, PlanTerms.From(p.RenewalRequest, p.RenewalRequest.SubscriptionPlan).Format)) return Results.Conflict(new { message = "الباقة المحفوظة لا تطابق نوع التسجيل." });
        p.Provider = "Manual"; p.ProviderEnvironment = "Manual";
        var receipt = await service.ConfirmAsync(t.AcademyId, actor, id, body.PaymentMethod); await tx.CommitAsync(); return Results.Ok(new { receiptId = receipt });
    }
    private static async Task<IResult> Queue(DateOnly? from, DateOnly? to, bool? allDates, Guid? branchId, string? type, string? status, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var academy = t.AcademyId;
        var first = from ?? new DateOnly(clock.Today.Year, clock.Today.Month, 1); var last = to ?? clock.Today;
        var all = allDates == true && from is null && to is null;
        if ((!all && (first > last || last.DayNumber - first.DayNumber > 366)) || type is not (null or "" or "Renewal" or "Enrollment" or "Product" or "Tournament") || status is not (null or "" or "Pending" or "Approved" or "Rejected")) return Results.UnprocessableEntity(new { message = "اختر فترة ونوع طلب وحالة صحيحة." });
        var zone = await db.Academies.Where(x => x.Id == academy).Select(x => x.TimeZone).SingleAsync();
        var bounds = all ? (Start: DateTimeOffset.MinValue, EndExclusive: DateTimeOffset.MaxValue) : Academy.Api.Slice8.ReportingEndpoints.UtcRange(first, last, zone);
        var rq = db.PaymentRequests.Where(x => x.AcademyId == academy && x.Id == x.RenewalRequest.PaymentRequestId && x.CreatedAtUtc >= bounds.Start && x.CreatedAtUtc < bounds.EndExclusive && (!branchId.HasValue || x.RenewalRequest.SportEnrollment.BranchId == branchId) && !db.NewEnrollmentRequests.Any(n => n.AcademyId == academy && n.PaymentRequestId == x.Id));
        var nq = db.NewEnrollmentRequests.Where(x => x.AcademyId == academy && x.CreatedAtUtc >= bounds.Start && x.CreatedAtUtc < bounds.EndExclusive && (!branchId.HasValue || x.PreferredBranchId == branchId));
        var sq = db.ProductSales.Where(x => x.AcademyId == academy && x.CreatedAtUtc >= bounds.Start && x.CreatedAtUtc < bounds.EndExclusive && (!branchId.HasValue || x.BranchId == branchId));
        var nr = await rq.CountAsync(x => x.Status == PaymentRequestStatus.Pending || x.Status == PaymentRequestStatus.Created);
        var nn = await nq.CountAsync(x => x.Status == NewEnrollmentRequestStatus.Pending || x.Status == NewEnrollmentRequestStatus.UnderReview);
        var ns = await sq.CountAsync(x => x.Status == "Pending");
        var tq = db.TournamentPayments.Where(x => x.AcademyId == academy && x.CreatedAtUtc >= bounds.Start && x.CreatedAtUtc < bounds.EndExclusive && (!branchId.HasValue || db.SportEnrollments.Any(e => e.AcademyId == academy && e.Id == x.EnrollmentId && e.BranchId == branchId)));
        var nt = await tq.CountAsync(x => x.Status == "Pending");
        if (status == "Pending") tq = tq.Where(x => x.Status == "Pending");
        if (status == "Approved") tq = tq.Where(x => x.Status == "Confirmed");
        if (status == "Rejected") tq = tq.Where(x => x.Status == "Rejected");
        if (status == "Pending") { rq = rq.Where(x => x.Status == PaymentRequestStatus.Pending || x.Status == PaymentRequestStatus.Created); nq = nq.Where(x => x.Status == NewEnrollmentRequestStatus.Pending || x.Status == NewEnrollmentRequestStatus.UnderReview); sq = sq.Where(x => x.Status == "Pending"); }
        else if (status == "Approved") { rq = rq.Where(x => x.Status == PaymentRequestStatus.Confirmed); nq = nq.Where(x => x.Status == NewEnrollmentRequestStatus.Approved); sq = sq.Where(x => x.Status == "Confirmed"); }
        else if (status == "Rejected") { rq = rq.Where(x => x.Status != PaymentRequestStatus.Confirmed && x.Status != PaymentRequestStatus.Pending && x.Status != PaymentRequestStatus.Created); nq = nq.Where(x => x.Status == NewEnrollmentRequestStatus.Rejected); sq = sq.Where(x => x.Status == "Rejected"); }
        var renewals = rq.OrderByDescending(x => x.CreatedAtUtc).Take(100).Select(x => new PaymentQueueItem(x.Id, "Renewal", x.RenewalRequest.SportEnrollment.Player.ArabicName, x.RenewalRequest.SubscriptionPlan.Sport.ArabicName, x.RenewalRequest.SportEnrollment.Branch.ArabicName, (x.RenewalRequest.PlanNameSnapshot ?? x.RenewalRequest.SubscriptionPlan.ArabicName), x.Amount, x.Currency, x.Status == PaymentRequestStatus.Confirmed ? "Approved" : x.Status == PaymentRequestStatus.Pending || x.Status == PaymentRequestStatus.Created ? "Pending" : "Rejected", db.PaymentProofs.Any(p => p.AcademyId == academy && p.PaymentRequestId == x.Id), x.CreatedAtUtc));
        var enrollments = nq.OrderByDescending(x => x.CreatedAtUtc).Take(100).Select(x => new PaymentQueueItem(x.Id, "Enrollment", x.ExistingPlayer == null ? x.NewChildArabicName! : x.ExistingPlayer.ArabicName, x.Sport.ArabicName, x.PreferredBranch.ArabicName, db.SubscriptionPlans.Where(p => p.AcademyId == academy && p.Id == x.SubscriptionPlanId).Select(p => p.ArabicName).FirstOrDefault() ?? "لم تُختر باقة", x.FinalAmount ?? 0, x.Currency ?? "EGP", x.Status == NewEnrollmentRequestStatus.Approved ? "Approved" : x.Status == NewEnrollmentRequestStatus.Rejected ? "Rejected" : "Pending", x.ProofAssetId != null, x.CreatedAtUtc));
        var sales = sq.OrderByDescending(x => x.CreatedAtUtc).Take(100).Select(x => new PaymentQueueItem(x.Id, "Product", x.PlayerId == null ? db.Users.Where(u => u.Id == x.RequestedByUserId).Select(u => u.DisplayName).FirstOrDefault() ?? "عميل" : db.Players.Where(p => p.AcademyId == academy && p.Id == x.PlayerId).Select(p => p.ArabicName).FirstOrDefault()!, x.SportName, x.BranchName ?? "—", x.ProductName, x.Amount, x.Currency, x.Status == "Confirmed" ? "Approved" : x.Status == "Rejected" ? "Rejected" : "Pending", x.ProofAssetId != null, x.CreatedAtUtc));
        var items = new List<PaymentQueueItem>();
        if (string.IsNullOrEmpty(type) || type == "Renewal") items.AddRange(await renewals.ToListAsync());
        if (string.IsNullOrEmpty(type) || type == "Enrollment") items.AddRange(await enrollments.ToListAsync());
        if (string.IsNullOrEmpty(type) || type == "Product") items.AddRange(await sales.ToListAsync());
        if (string.IsNullOrEmpty(type) || type == "Tournament") items.AddRange(await tq.OrderByDescending(x => x.CreatedAtUtc).Take(100).Join(db.SportEnrollments, p => new { p.AcademyId, Id = p.EnrollmentId }, e => new { e.AcademyId, e.Id }, (p, e) => new PaymentQueueItem(p.Id, "Tournament", e.Player.ArabicName, e.Sport.ArabicName, e.Branch.ArabicName, p.TournamentName, p.Amount, p.Currency, p.Status == "Confirmed" ? "Approved" : p.Status, p.ProofAssetId != null, p.CreatedAtUtc)).ToListAsync());
        return Results.Ok(new { from = first, to = last, pending = nr + nn + ns + nt, newEnrollments = nn, renewals = nr, products = ns, tournaments = nt, items = items.OrderByDescending(x => x.CreatedAtUtc).Take(100) });
    }
    private static async Task<IResult> EnrollmentProof(Guid id, ProofWrite body, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var user = Actor(principal); await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"new-enrollment-review:{t.AcademyId}:{id}"}, 0))");
        var row = await db.NewEnrollmentRequests.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id && x.RequestedByGuardianUserId == user);
        if (row is null || !await db.PrivateAssets.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.AssetId && x.UploadedByUserId == user && x.Purpose == "Proof")) return Results.NotFound();
        if (!row.SubscriptionPlanId.HasValue || row.Status is not (NewEnrollmentRequestStatus.Pending or NewEnrollmentRequestStatus.UnderReview)) return Results.Conflict();
        row.ProofAssetId = body.AssetId; row.UpdatedAtUtc = clock.UtcNow; await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { message = "وصل إثبات الدفع للإدارة. يتم تفعيل الاشتراك بعد قبوله وإسناد المجموعة." });
    }
    private static Task<IResult> EnrollmentPayment(Guid id, CurrentTenant t, ClaimsPrincipal p, FoundationDbContext db) => EnrollmentPaymentCore(id, t, db, Actor(p));
    private static Task<IResult> EnrollmentPaymentStaff(Guid id, CurrentTenant t, FoundationDbContext db) => EnrollmentPaymentCore(id, t, db, null);
    private static async Task<IResult> EnrollmentPaymentCore(Guid id, CurrentTenant tenant, FoundationDbContext db, Guid? owner)
    { var t = (await tenant.ResolveAsync())!; var row = await db.NewEnrollmentRequests.Where(x => x.AcademyId == t.AcademyId && x.Id == id && (!owner.HasValue || x.RequestedByGuardianUserId == owner)).Select(x => new { x.Id, x.SubscriptionPlanId, x.OriginalAmount, amount = x.FinalAmount, x.Currency, status = x.Status.ToString(), x.GuardianVisibleReason, x.CreatedSportEnrollmentId, x.PaymentRequestId, proofUrl = x.ProofAssetId == null ? null : "/api/v1/assets/" + x.ProofAssetId }).SingleOrDefaultAsync(); return row is null ? Results.NotFound() : Results.Ok(row); }
    private static async Task<IResult> Settings(CurrentTenant tenant, FoundationDbContext db) { var t = (await tenant.ResolveAsync())!; var row = await db.AcademyTransferSettings.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId); return Results.Ok(new { row?.InstaPayAddress, row?.WalletNumber, row?.RecipientName, configured = row?.IsActive == true && (!string.IsNullOrWhiteSpace(row.InstaPayAddress) || !string.IsNullOrWhiteSpace(row.WalletNumber)) }); }
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
        if (payment.Status != PaymentRequestStatus.Pending) return Results.Conflict(new { message = "طلب الدفع لم يعد ينتظر إثباتًا." });
        var proof = await db.PaymentProofs.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.PaymentRequestId == id);
        if (proof is null) { proof = new PaymentProof { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PaymentRequestId = id, AssetId = body.AssetId, SubmittedByUserId = user, Status = "Pending", CreatedAtUtc = clock.UtcNow }; db.PaymentProofs.Add(proof); }
        else if (proof.Status != "Pending") return Results.Conflict();
        proof.AssetId = body.AssetId; proof.UpdatedAtUtc = clock.UtcNow;
        payment.Provider = "Manual"; payment.ProviderEnvironment = "Manual"; payment.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(); await transaction.CommitAsync(); return Results.Ok(new { proof.Id, message = "وصل إثبات الدفع إلى الإدارة. يبدأ الاشتراك بعد الموافقة." });
    }
    private static async Task<IResult> GetProof(Guid id, CurrentTenant tenant, FoundationDbContext db)
    { var t = (await tenant.ResolveAsync())!; var row = await db.PaymentProofs.AsNoTracking().Where(x => x.AcademyId == t.AcademyId && x.PaymentRequestId == id).Select(x => new { x.Id, x.Status, x.ReviewReason, x.ReviewedAtUtc, url = "/api/v1/assets/" + x.AssetId }).SingleOrDefaultAsync(); return row is null ? Results.NotFound() : Results.Ok(row); }
    private static async Task<IResult> Review(Guid id, ReviewWrite body, CurrentTenant tenant, ClaimsPrincipal principal, FoundationDbContext db, CollectionService collections, ISubscriptionClock clock, GroupPlacementService placement)
    {
        var t = (await tenant.ResolveAsync())!; var user = Actor(principal);
        if (body.Reason?.Length > 500) return Results.UnprocessableEntity();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"collection:{t.AcademyId}:{id}"}, 0))");
        var payment = await db.PaymentRequests.Include(x => x.RenewalRequest).ThenInclude(x => x.SubscriptionPlan).Include(x => x.RenewalRequest).ThenInclude(x => x.SportEnrollment).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id);
        var proof = await db.PaymentProofs.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.PaymentRequestId == id);
        if (payment is null || proof is null) return Results.NotFound();
        if (proof.Status != "Pending") return proof.Status == (body.Accept ? "Accepted" : "Rejected") ? Results.Ok(new { replay = true }) : Results.Conflict();
        if (payment.Status != PaymentRequestStatus.Pending || payment.RenewalRequest.PaymentRequestId != id || payment.Provider != "Manual") return Results.Conflict(new { message = "طلب الدفع تغير. حدّث الصفحة." });
        Guid? receipt = null;
        if (body.Accept)
        {
            if (body.GroupId is not Guid groupId) return Results.UnprocessableEntity(new { message = "اختر مجموعة اللاعب قبل القبول." });
            var enrollment = payment.RenewalRequest.SportEnrollment;
            var group = await db.TrainingGroups.Include(x => x.AgeCategory).SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == groupId && x.IsActive && x.Branch.IsActive && x.Sport.IsActive && x.AgeCategory.IsActive);
            if (group is null || group.SportId != enrollment.SportId || group.BranchId != enrollment.BranchId || group.RegistrationFormat != enrollment.RegistrationFormat || !PlanCatalog.FormatMatches(enrollment.RegistrationFormat, PlanTerms.From(payment.RenewalRequest, payment.RenewalRequest.SubscriptionPlan).Format)) return Results.UnprocessableEntity(new { message = "المجموعة أو الباقة غير متوافقة مع نوع التسجيل." });
            var birthYear = await db.Players.Where(x => x.AcademyId == t.AcademyId && x.Id == enrollment.PlayerId).Select(x => x.DateOfBirth.Year).SingleAsync();
            if (group.AgeCategory.MinimumBirthYear > birthYear || group.AgeCategory.MaximumBirthYear < birthYear) return Results.UnprocessableEntity(new { message = "عمر اللاعب غير مناسب للمجموعة." });
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"group-capacity:{t.AcademyId}:{group.Id}"}, 0))");
            if (group.Id != enrollment.TrainingGroupId && group.Capacity.HasValue && await db.SportEnrollments.CountAsync(x => x.AcademyId == t.AcademyId && x.TrainingGroupId == group.Id && x.IsActive) >= group.Capacity.Value) return Results.Conflict(new { message = "المجموعة مكتملة العدد." });
            var specialAdmission = group.RegistrationFormat != TrainingRegistrationFormat.Group && enrollment.TrainingGroupId is null;
            if (group.RegistrationFormat != TrainingRegistrationFormat.Group && enrollment.TrainingGroupId.HasValue && enrollment.TrainingGroupId != groupId) return Results.Conflict(new { message = "جدد في المجموعة الحالية؛ نقل التدريب الخاص يحتاج معاينة نقل مستقلة." });
            if (specialAdmission) { try { group = await placement.PrepareSpecialAdmission(group.Id); } catch (Academy.Api.Facilities.FacilityRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); } }
            else enrollment.TrainingGroupId = groupId;
            enrollment.UpdatedAtUtc = clock.UtcNow;
            receipt = await collections.ConfirmAsync(t.AcademyId, user, id, "Online");
            if (specialAdmission) { try { await placement.FinishSpecialAdmission(group, enrollment); } catch (Academy.Api.Facilities.FacilityRejected error) { return Results.Problem(statusCode: error.Status, title: error.Message); } }
        }
        else { payment.Status = PaymentRequestStatus.Cancelled; payment.RenewalRequest.Status = RenewalRequestStatus.Cancelled; }
        proof.Status = body.Accept ? "Accepted" : "Rejected"; proof.ReviewReason = body.Reason?.Trim(); proof.ReviewedByUserId = user; proof.ReviewedAtUtc = clock.UtcNow; proof.UpdatedAtUtc = clock.UtcNow;
        db.AcademyNotices.Add(new AcademyNotice { Id = Guid.NewGuid(), AcademyId = t.AcademyId, UserId = payment.RenewalRequest.RequestedByUserId, Message = body.Accept ? "تم قبول إثبات الدفع وتفعيل الاشتراك بنجاح. يمكنك مراجعة الإيصال ومواعيد التدريب." : $"لم يتم قبول طلب الدفع. {body.Reason?.Trim() ?? "تواصل مع إدارة الأكاديمية للتوضيح."}", CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
        await db.SaveChangesAsync(); await transaction.CommitAsync(); return Results.Ok(new { receiptId = receipt, proof.Status });
    }
    private static async Task<IResult> Refunds(Guid id, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; if (!await db.Players.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == id)) return Results.NotFound();
        var collections = await db.Collections.Where(x => x.AcademyId == t.AcademyId && x.SportEnrollment.PlayerId == id).OrderByDescending(x => x.ConfirmedAtUtc).Select(x => new { x.Id, x.Amount, x.Currency, refunded = db.CollectionRefunds.Where(r => r.AcademyId == t.AcademyId && r.CollectionId == x.Id).Sum(r => (decimal?)r.Amount) ?? 0, x.ConfirmedAtUtc }).ToListAsync();
        return Results.Ok(new { collections, refunds = await db.CollectionRefunds.Where(x => x.AcademyId == t.AcademyId && x.PlayerId == id).OrderByDescending(x => x.CreatedAtUtc).Select(x => new { x.Id, x.Amount, x.Currency, x.Reason, x.CreatedAtUtc }).ToListAsync() });
    }
    private static async Task<IResult> Refund(Guid id, RefundWrite body, HttpContext context, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var user = Actor(context.User); var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || body.Amount <= 0 || decimal.Round(body.Amount, 2) != body.Amount || body.Reason?.Length > 500) return Results.UnprocessableEntity(new { message = "أدخل مبلغ استرداد صحيحًا." });
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"refund-key:{t.AcademyId}:{user}:{key}"}, 0))");
        var replay = await db.CollectionRefunds.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.PerformedByUserId == user && x.IdempotencyKey == key);
        if (replay is not null) return replay.CollectionId == body.CollectionId && replay.PlayerId == id && replay.Amount == body.Amount ? Results.Ok(new { replay.Id, replay = true }) : Results.Conflict();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"refund-collection:{t.AcademyId}:{body.CollectionId}"}, 0))");
        var collection = await db.Collections.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == body.CollectionId && x.SportEnrollment.PlayerId == id); if (collection is null) return Results.NotFound();
        var refunded = await db.CollectionRefunds.Where(x => x.AcademyId == t.AcademyId && x.CollectionId == body.CollectionId).SumAsync(x => (decimal?)x.Amount) ?? 0;
        if (refunded + body.Amount > collection.Amount) return Results.Conflict(new { message = "الاسترداد أكبر من الرصيد المتبقي للتحصيل." });
        var row = new CollectionRefund { Id = Guid.NewGuid(), AcademyId = t.AcademyId, CollectionId = collection.Id, PlayerId = id, Amount = body.Amount, Currency = collection.Currency, Reason = body.Reason?.Trim(), PerformedByUserId = user, IdempotencyKey = key, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow }; db.CollectionRefunds.Add(row); await db.SaveChangesAsync(); await transaction.CommitAsync(); return Results.Ok(new { row.Id });
    }
}
public sealed record TransferWrite(string? InstaPayAddress, string? WalletNumber, string? RecipientName, bool IsActive);
public sealed record CollectWrite(string PaymentMethod);
public sealed record ProofWrite(Guid AssetId);
public sealed record ReviewWrite(bool Accept, Guid? GroupId, string? Reason);
public sealed record RefundWrite(Guid CollectionId, decimal Amount, string? Reason);
public sealed record PaymentQueueItem(Guid Id, string Type, string Player, string Sport, string Branch, string Plan, decimal Amount, string Currency, string Status, bool HasProof, DateTimeOffset CreatedAtUtc);
