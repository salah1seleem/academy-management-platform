using System.Globalization;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice3;
using Academy.Api.Slice4;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public sealed partial class CashRegisterService
{
    private async Task<PaymentRequest> StaffPayment(Guid id, TenantMembership t)
    {
        var payment = await db.PaymentRequests.Include(p => p.RenewalRequest).ThenInclude(r => r.SubscriptionPlan)
            .Include(p => p.RenewalRequest).ThenInclude(r => r.SportEnrollment).ThenInclude(e => e.Player)
            .SingleOrDefaultAsync(p => p.AcademyId == t.AcademyId && p.Id == id)
            ?? throw new CashRejected("طلب الدفع غير متاح.", 404);
        if (!await db.AcademyMemberships.AnyAsync(m => m.AcademyId == t.AcademyId && m.UserId == payment.RenewalRequest.RequestedByUserId &&
            (m.Role == AcademyRole.AcademyOwner || m.Role == AcademyRole.AcademyAdmin)))
            throw new CashRejected("هذا المسار لطلب تجديد أنشأته الإدارة فقط.", 403);
        if (await db.PaymentProofs.AnyAsync(p => p.AcademyId == t.AcademyId && p.PaymentRequestId == id) ||
            await db.NewEnrollmentRequests.AnyAsync(n => n.AcademyId == t.AcademyId && n.PaymentRequestId == id))
            throw new CashRejected("استخدم مراجعة طلب التسجيل أو إثبات الدفع الخاص به.");
        if (payment.Provider != "Manual" && !(payment.Provider == InternalTestPaymentGateway.ProviderName && payment.ProviderEnvironment == "Demo"))
            throw new CashRejected("طلب المزود الإلكتروني يحتاج مطابقة مستقلة؛ لا يمكن تحويله إلى قبض نقدي.");
        if (payment.Status != PaymentRequestStatus.Pending || payment.RenewalRequest.PaymentRequestId != id ||
            payment.Amount != payment.RenewalRequest.FinalAmount)
            throw new CashRejected("تغير طلب الدفع أو لم يعد ينتظر التحصيل. حدّث الصفحة.");
        Amount(payment.Amount, zero: false);
        var enrollment = payment.RenewalRequest.SportEnrollment;
        var day = await AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
        if (!enrollment.IsActive || enrollment.Status != EnrollmentStatus.Active || !enrollment.Player.IsActive ||
            !await db.Branches.AnyAsync(b => b.Id == enrollment.BranchId && b.IsActive) ||
            !await db.Sports.AnyAsync(s => s.Id == enrollment.SportId && s.IsActive) ||
            !await db.TrainingGroups.AnyAsync(g => g.Id == FoundationDbContext.AssignedGroupAt(t.AcademyId, enrollment.Id, day) &&
                g.IsActive && g.BranchId == enrollment.BranchId && g.SportId == enrollment.SportId) ||
            !PlanCatalog.FormatMatches(enrollment.RegistrationFormat, PlanTerms.From(payment.RenewalRequest, payment.RenewalRequest.SubscriptionPlan).Format))
            throw new CashRejected("تسجيل اللاعب أو مجموعته أو شروط الباقة غير متاحة للتحصيل.");
        return payment;
    }

    public async Task<object> PendingPaymentOptions(Guid id)
    {
        var (t, _) = await Context(write: true); await CollectionPermissions(t, true); await Enabled(t.AcademyId);
        var payment = await StaffPayment(id, t); var branch = payment.RenewalRequest.SportEnrollment.BranchId;
        var sessions = await (from s in db.CashSessions.AsNoTracking()
                              join r in db.CashRegisters on s.RegisterId equals r.Id
                              join b in db.Branches on s.BranchId equals b.Id
                              where s.ClosedAtUtc == null && s.OpenedByMembershipId == t.MembershipId && s.BranchId == branch &&
                                  r.IsActive && r.Currency == payment.Currency &&
                                  db.CashierAssignments.Any(a => a.RegisterId == r.Id && a.MembershipId == t.MembershipId && a.IsActive)
                              orderby r.Name
                              select new { s.Id, name = r.Name, branch = b.ArabicName }).ToArrayAsync();
        return new { amount = Money(payment.Amount), payment.Currency, sessions };
    }

    public async Task<object> CollectPendingPayment(Guid id, Guid? sessionId, string? expectedAmount, CollectionService collections)
    {
        var (t, user) = await Context(write: true); await CollectionPermissions(t, true);
        if (sessionId is not Guid shift || shift == Guid.Empty) throw new CashRejected("اختر ورديتك المفتوحة قبل التحصيل النقدي.", 422);
        if (!decimal.TryParse(expectedAmount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var expected))
            throw new CashRejected("راجع مبلغ الطلب قبل التحصيل.", 422);
        Amount(expected, zero: false);
        await using var tx = await db.Database.BeginTransactionAsync();
        var request = await Request(t.AcademyId, user, "pending-payment", id.ToString("N"));
        var payload = Academy.Api.Configuration.AcademyConfigurationService.Hash(JsonSerializer.Serialize(new { id, shift, amount = Money(expected), user }));
        var plan = await db.PaymentRequests.Where(p => p.AcademyId == t.AcademyId && p.Id == id).Select(p => (Guid?)p.RenewalRequest.SubscriptionPlanId).SingleOrDefaultAsync()
            ?? throw new CashRejected("طلب الدفع غير متاح.", 404);
        // Same order as direct shift collection: plan -> shift -> payment/enrollment.
        await PlanCatalog.LockAsync(db, t.AcademyId, plan); await Lock(t.AcademyId, $"session:{shift}");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"collection:{t.AcademyId}:{id}"}, 0))");
        var prior = await db.CashReceiptPostings.SingleOrDefaultAsync(p => p.AcademyId == t.AcademyId &&
            db.Collections.Any(c => c.AcademyId == t.AcademyId && c.Id == p.CollectionId && c.PaymentRequestId == id));
        if (prior is not null)
        {
            if (prior.ActorUserId != user) throw new CashRejected("طلب الدفع محصل بالفعل بواسطة أمين آخر.", 403);
            Same(prior.RequestHash, payload);
            return new { receiptId = prior.ReceiptId, postingId = prior.Id, replay = true };
        }
        var payment = await StaffPayment(id, t); var (session, register) = await CollectingSession(shift, t);
        if (payment.RenewalRequest.SportEnrollment.BranchId != session.BranchId || payment.Currency != register.Currency)
            throw new CashRejected("اختر وردية من فرع التسجيل وبعملته.");
        if (payment.Amount != expected) throw new CashRejected("تغير المبلغ. حدّث الطلب وراجع الإجمالي قبل التأكيد.");
        // Preserve the issued request's financial/plan snapshots; never reprice it.
        payment.Provider = "Manual"; payment.ProviderEnvironment = "Manual";
        var receiptId = await collections.ConfirmAsync(t.AcademyId, user, id, "Cash");
        var receipt = await db.Receipts.SingleAsync(r => r.Id == receiptId);
        var at = time.GetUtcNow();
        var posting = new CashReceiptPosting { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SessionId = shift, BranchId = session.BranchId,
            CollectionId = receipt.CollectionId, ReceiptId = receipt.Id, Amount = receipt.Amount, Currency = receipt.Currency, ActorUserId = user,
            RequestKey = request, RequestHash = payload, CreatedAtUtc = at, UpdatedAtUtc = at };
        db.CashReceiptPostings.Add(posting); await db.SaveChangesAsync(); await tx.CommitAsync();
        return new { receiptId, postingId = posting.Id, amount = Money(posting.Amount), posting.Currency };
    }
}
