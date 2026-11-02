using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static partial class PayrollEndpoints
{
    // Approval is not a disbursement. Record only after the owner confirms actual payment.
    private static async Task<IResult> RecordRequestedAdvance(Guid id, AdvanceRequestPayment body,
        HttpContext http, CurrentTenant current, FoundationDbContext db, TimeProvider clock)
    {
        if (Key(http) is null || !Money(body.ExpectedAmount) || string.IsNullOrWhiteSpace(body.Reference)
            || body.Reference.Trim().Length is < 3 or > 120)
            return Results.UnprocessableEntity(new { message = "راجع المبلغ وأدخل مرجع صرف السلفة." });
        var t = (await current.ResolveAsync())!;
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await PayrollSensitiveAccess.LockAndAuthorize(http, current, db, AcademyPermissions.PayrollAdvanceManage)
            || !await PayrollSensitiveAccess.LockAndAuthorize(http, current, db, AcademyPermissions.PayrollPay)) return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"payroll-review:{t.AcademyId}:{id}"}, 0))");
        var row = await db.PayrollAdditionRequests.SingleOrDefaultAsync(r => r.AcademyId == t.AcademyId && r.Id == id && r.Kind == "Advance");
        if (row is null) return Results.NotFound();
        await Lock(db, t.AcademyId, row.EmployeeId, default);
        var e = await db.Employees.SingleOrDefaultAsync(e => e.AcademyId == t.AcademyId && e.Id == row.EmployeeId && e.IsActive);
        if (e is null) return Results.NotFound();
        var actor = Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!await PayrollSensitiveAccess.BranchAllowed(db, t, e.BranchId) || row.RequestedByUserId == actor) return Results.Forbid();
        var reference = body.Reference.Trim();
        var hash = StatementComparisonRules.Hash(new { id, body.ExpectedAmount, reference });
        if (row.Status == "Paid")
        {
            var paid = await db.PayrollAdvances.SingleOrDefaultAsync(a => a.AcademyId == t.AcademyId && a.Id == id);
            return paid?.RequestHash == hash ? Results.Ok(new { row.Id, replay = true }) : Results.Conflict();
        }
        if (row.Status != "Approved" || row.Amount != body.ExpectedAmount || row.Currency != e.Currency)
            return Results.Conflict(new { message = "تغير الطلب؛ حدّث المراجعة قبل تسجيل الصرف." });
        var now = clock.GetUtcNow();
        db.PayrollAdvances.Add(new PayrollAdvance {
            Id = id, AcademyId = t.AcademyId, EmployeeId = e.Id, BranchId = e.BranchId,
            Amount = row.Amount, Currency = row.Currency, Reference = reference,
            ActorMembershipId = t.MembershipId, ActorUserId = actor,
            RequestKey = "advance-request-" + id, RequestHash = hash, CreatedAtUtc = now, UpdatedAtUtc = now
        });
        row.Status = "Paid"; row.UpdatedAtUtc = now;
        db.AcademyNotices.Add(new AcademyNotice { Id = Guid.NewGuid(), AcademyId = t.AcademyId,
            UserId = row.RequestedByUserId, Message = "تم تسجيل صرف السلفة المعتمدة.", CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Ok(new { row.Id, row.Status });
    }
}

public sealed record AdvanceRequestPayment(decimal ExpectedAmount, string Reference);
