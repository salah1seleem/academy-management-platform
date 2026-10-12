using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public sealed partial class CashRegisterService
{
    public async Task AuthorizeRegistration()
    {
        var (t, _) = await Context(write: true); await CollectionPermissions(t, true);
    }

    public async Task<object> RegistrationOptions(Guid branchId, Guid planId)
    {
        var (t, _) = await Context(write: true); await CollectionPermissions(t, true); await Enabled(t.AcademyId);
        var plan = await db.SubscriptionPlans.TrainingOnly().ForBranch(branchId)
            .Where(p => p.Id == planId && p.IsActive && p.Sport.IsActive).Select(p => new { p.Currency }).SingleOrDefaultAsync();
        if (plan is null || !await db.Branches.AnyAsync(b => b.Id == branchId && b.IsActive))
            throw new CashRejected("الباقة أو الفرع غير متاحين للتحصيل.", 404);
        var sessions = await (from s in db.CashSessions.AsNoTracking()
                              join r in db.CashRegisters on s.RegisterId equals r.Id
                              join b in db.Branches on s.BranchId equals b.Id
                              where s.ClosedAtUtc == null && s.OpenedByMembershipId == t.MembershipId && s.BranchId == branchId &&
                                  r.IsActive && r.Currency == plan.Currency &&
                                  db.CashierAssignments.Any(a => a.RegisterId == r.Id && a.MembershipId == t.MembershipId && a.IsActive)
                              orderby r.Name
                              select new { s.Id, name = r.Name, branch = b.ArabicName }).ToArrayAsync();
        return new { sessions };
    }

    // Registration owns the transaction and takes the plan lock first. Keep the
    // shift lock until player, guardian link, service, receipt and posting commit.
    public async Task PrepareRegistration(Guid? sessionId, Guid branchId, Guid planId, decimal? expectedAmount)
    {
        var (t, _) = await Context(write: true); await CollectionPermissions(t, true);
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Registration transaction required.");
        if (sessionId is not Guid shift || shift == Guid.Empty) throw new CashRejected("اختر ورديتك المفتوحة قبل تسجيل القبض النقدي.", 422);
        if (!expectedAmount.HasValue) throw new CashRejected("راجع المبلغ المستحق قبل تسجيل القبض.", 422);
        Amount(expectedAmount.Value, zero: false);
        await Lock(t.AcademyId, $"session:{shift}");
        var (session, register) = await CollectingSession(shift, t);
        var currency = await db.SubscriptionPlans.TrainingOnly().ForBranch(branchId).Where(p => p.Id == planId && p.IsActive)
            .Select(p => p.Currency).SingleOrDefaultAsync();
        if (session.BranchId != branchId || currency != register.Currency)
            throw new CashRejected("اختر وردية من فرع التسجيل وبعملة الباقة.");
    }

    public async Task PostRegistration(Guid sessionId, Guid paymentId, string key, string payloadHash)
    {
        var (t, user) = await Context(write: true); var (session, register) = await CollectingSession(sessionId, t);
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Registration transaction required.");
        var receipt = await db.Receipts.SingleAsync(r => r.Collection.PaymentRequestId == paymentId);
        if (receipt.PaymentMethod != "Cash" || receipt.Currency != register.Currency ||
            !await db.SportEnrollments.AnyAsync(e => e.Id == receipt.SportEnrollmentId && e.BranchId == session.BranchId))
            throw new CashRejected("بيانات الإيصال لا تطابق وردية التسجيل.");
        var request = await Request(t.AcademyId, user, "registration", key); var at = time.GetUtcNow();
        db.CashReceiptPostings.Add(new CashReceiptPosting { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SessionId = sessionId,
            BranchId = session.BranchId, CollectionId = receipt.CollectionId, ReceiptId = receipt.Id, Amount = receipt.Amount,
            Currency = receipt.Currency, ActorUserId = user, RequestKey = request, RequestHash = payloadHash, CreatedAtUtc = at, UpdatedAtUtc = at });
        await db.SaveChangesAsync();
    }
}
