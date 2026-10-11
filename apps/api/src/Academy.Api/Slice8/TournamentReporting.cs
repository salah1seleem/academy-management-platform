using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;

public static partial class ReportingEndpoints
{
    private static IQueryable<TournamentPayment> TournamentQuery(Guid academy, string currency, DateTimeOffset start, DateTimeOffset end, Guid? sport, Guid? branch, string? method, string? search, FoundationDbContext db) => db.TournamentPayments.AsNoTracking().Where(x => x.AcademyId == academy && x.Status == "Confirmed" && x.Currency == currency && x.ConfirmedAtUtc >= start && x.ConfirmedAtUtc < end && (string.IsNullOrEmpty(method) || x.PaymentMethod == method) && db.SportEnrollments.Any(e => e.AcademyId == academy && e.Id == x.EnrollmentId && (!sport.HasValue || e.SportId == sport) && (!branch.HasValue || e.BranchId == branch) && (string.IsNullOrEmpty(search) || e.Player.ArabicName.Contains(search.Trim()))));
    private static IQueryable<FinancialRow> TournamentRows(IQueryable<TournamentPayment> q, FoundationDbContext db) => q.Join(db.SportEnrollments, p => new { p.AcademyId, Id = p.EnrollmentId }, e => new { e.AcademyId, e.Id }, (p, e) => new FinancialRow(p.Id, p.Id, p.ReceiptNumber!, e.Player.ArabicName, e.Sport.ArabicName, e.Branch.ArabicName, e.TrainingGroup.ArabicName, p.TournamentName, p.Amount, p.Currency, p.PaymentMethod!, "Manual", p.Id.ToString(), p.ConfirmedAtUtc!.Value, 0, "Tournament"));
}
