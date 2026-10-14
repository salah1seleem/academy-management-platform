using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;

// A scoped projection of decisions already persisted by their source workflows.
// This is not a substitute for the still-open cross-module append-only audit.
public static partial class ReportingEndpoints
{
    private static async Task<IResult> FinancialDecisions(DateOnly? from, DateOnly? to, bool? allDates, Guid? branchId, int? page,
        CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var current = (await tenant.ResolveAsync())!;
        if (current.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        if (page is < 1 or > 100) return Validation("page", "رقم الصفحة غير صالح.");
        var range = await FinancialRange(current.AcademyId, from, to, db, clock, allDates == true);
        if (range.Error is not null) return range.Error;
        if (branchId.HasValue && !await db.Branches.AnyAsync(x => x.AcademyId == current.AcademyId && x.Id == branchId.Value)) return Results.NotFound();

        var expenses = db.OperatingExpenses.AsNoTracking().Where(x => x.AcademyId == current.AcademyId && (!branchId.HasValue || x.BranchId == branchId));
        var createdExpenses = expenses.Where(x => x.CreatedAtUtc >= range.StartUtc && x.CreatedAtUtc < range.EndUtcExclusive);
        var approvedExpenses = expenses.Where(x => x.ApprovedAtUtc >= range.StartUtc && x.ApprovedAtUtc < range.EndUtcExclusive && x.ApprovedByUserId != null);
        var postedExpenses = expenses.Where(x => x.PostedAtUtc >= range.StartUtc && x.PostedAtUtc < range.EndUtcExclusive && x.PostedByUserId != null);
        var reviewed = from proof in db.PaymentProofs.AsNoTracking()
                       join payment in db.PaymentRequests.AsNoTracking() on new { proof.AcademyId, Id = proof.PaymentRequestId } equals new { payment.AcademyId, payment.Id }
                       where proof.AcademyId == current.AcademyId && proof.ReviewedByUserId != null && proof.ReviewedAtUtc >= range.StartUtc && proof.ReviewedAtUtc < range.EndUtcExclusive
                           && (proof.Status == "Accepted" || proof.Status == "Rejected")
                           && (!branchId.HasValue || payment.RenewalRequest.SportEnrollment.BranchId == branchId)
                       select new { proof, payment };
        var count = await createdExpenses.CountAsync() + await approvedExpenses.CountAsync() + await postedExpenses.CountAsync() + await reviewed.CountAsync();
        var limit = (page ?? 1) * 50;
        // EF cannot translate a set operation after record construction. Each source is bounded,
        // then merged in memory so later pages still have a correct global order.
        var rows = (await createdExpenses.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(limit)
                .Select(x => new FinancialDecisionRow(x.Id, x.BranchId, "Expense", "Created", x.CreatedByUserId, x.CreatedAtUtc, x.Amount, x.Currency)).ToArrayAsync())
            .Concat(await approvedExpenses.OrderByDescending(x => x.ApprovedAtUtc).ThenBy(x => x.Id).Take(limit)
                .Select(x => new FinancialDecisionRow(x.Id, x.BranchId, "Expense", "Approved", x.ApprovedByUserId!.Value, x.ApprovedAtUtc!.Value, x.Amount, x.Currency)).ToArrayAsync())
            .Concat(await postedExpenses.OrderByDescending(x => x.PostedAtUtc).ThenBy(x => x.Id).Take(limit)
                .Select(x => new FinancialDecisionRow(x.Id, x.BranchId, "Expense", "Posted", x.PostedByUserId!.Value, x.PostedAtUtc!.Value, x.Amount, x.Currency)).ToArrayAsync())
            .Concat(await reviewed.OrderByDescending(x => x.proof.ReviewedAtUtc).ThenBy(x => x.payment.Id).ThenBy(x => x.proof.Status).Take(limit)
                .Select(x => new FinancialDecisionRow(x.payment.Id, x.payment.RenewalRequest.SportEnrollment.BranchId,
                    "RenewalProof", x.proof.Status, x.proof.ReviewedByUserId!.Value, x.proof.ReviewedAtUtc!.Value, x.payment.Amount, x.payment.Currency)).ToArrayAsync())
            .OrderByDescending(x => x.OccurredAtUtc).ThenBy(x => x.SourceId).ThenBy(x => x.State)
            .Skip(((page ?? 1) - 1) * 50).Take(50).ToArray();
        var actorIds = rows.Select(x => x.ActorUserId).Distinct().ToArray();
        var branchIds = rows.Select(x => x.BranchId).Distinct().ToArray();
        var actors = await db.Users.Where(x => actorIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName);
        var branches = await db.Branches.Where(x => x.AcademyId == current.AcademyId && branchIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.ArabicName);
        return Results.Ok(new { count, page = page ?? 1, from = range.From, to = range.To,
            items = rows.Select(x => new { x.SourceId, x.BranchId, branchName = branches.GetValueOrDefault(x.BranchId), x.Kind, x.State,
                x.ActorUserId, actorName = actors.GetValueOrDefault(x.ActorUserId), x.OccurredAtUtc, x.Amount, x.Currency }).ToArray() });
    }
}

internal sealed record FinancialDecisionRow(Guid SourceId, Guid BranchId, string Kind, string State,
    Guid ActorUserId, DateTimeOffset OccurredAtUtc, decimal Amount, string Currency);
