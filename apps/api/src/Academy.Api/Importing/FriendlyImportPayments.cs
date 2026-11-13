using Academy.Api.Slice3;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Importing;

public sealed partial class FriendlyImportHistory
{
    private async Task PublishPayments(FoundationDbContext db, AcademyImportJob job, Dictionary<string, ImportedEnrollment> mapped,
        Dictionary<Guid, SubscriptionPeriod> stored, Dictionary<string, Guid> periodIds, CancellationToken token)
    {
        if (PaymentsCount == 0) return;
        if (!job.OpeningBalancesApproved) throw new ImportRejected("authority");
        var ids = input.Rows["تحصيلات سابقة اختيارية"].Select(r => Id("collection", r["Code"])).ToArray();
        var existing = (await db.Collections.Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToArrayAsync(token)).ToHashSet();
        var sportIds = packages.Values.Select(p => p.SportId).Distinct().ToArray();
        var sports = await db.Sports.Where(s => sportIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.ArabicName, token);
        var evidence = new List<ImportedHistoricalPayment>();
        var postings = new List<(RenewalRequest Renewal, PaymentCollection Collection, Receipt Receipt, SubscriptionPeriod Period)>();
        foreach (var r in input.Rows["تحصيلات سابقة اختيارية"])
        {
            var collectionId = Id("collection", r["Code"]); if (existing.Contains(collectionId)) continue;
            var p = periods[r["Period"]]; var m = mapped[p.Enrollment]; var period = stored[periodIds[r["Period"]]];
            if (period.CollectionId.HasValue) throw new ImportRejected("conflict");
            var at = At(input.Date(r, "Date")!.Value); var amount = ImportPreview.Amount(r["Amount"])!.Value;
            var renewalId = Id("renewal", r["Code"]); var paymentId = Id("payment", r["Code"]);
            var reference = "HIST-" + Id("evidence", r["Evidence"]).ToString("N");
            var renewal = new RenewalRequest { Id = renewalId, AcademyId = academy, SportEnrollmentId = m.SportEnrollmentId, SportId = p.Plan.SportId,
                SubscriptionPlanId = p.Plan.Id, RequestedByUserId = job.ActorUserId, RequestedAtUtc = at, OriginalAmount = amount, DiscountAmount = 0,
                FinalAmount = amount, AmountExpected = amount, Currency = p.Currency, Status = RenewalRequestStatus.Paid, PaymentRequestId = null,
                IdempotencyKey = "history:" + collectionId.ToString("N"), PlanNameSnapshot = p.Plan.ArabicName, PlanTypeSnapshot = p.Plan.PlanType,
                ServiceKindSnapshot = p.Plan.ServiceKind, TrainingFormatSnapshot = p.Plan.TrainingFormat, PlanRevisionSnapshot = p.Plan.Revision,
                DurationDaysSnapshot = period.DurationDaysSnapshot, SessionCountSnapshot = p.Entitled, CreatedAtUtc = job.CreatedAtUtc, UpdatedAtUtc = job.CreatedAtUtc };
            var payment = new PaymentRequest { Id = paymentId, AcademyId = academy, RenewalRequestId = renewalId, Provider = HistoricalReceiptCalendar.Provider,
                ProviderEnvironment = "RecordedEvidence", ProviderReference = reference, CheckoutReference = reference, Amount = amount, Currency = p.Currency,
                Status = PaymentRequestStatus.Confirmed, ConfirmedAtUtc = at, CreatedAtUtc = job.CreatedAtUtc, UpdatedAtUtc = job.CreatedAtUtc };
            db.RenewalRequests.Add(renewal); db.PaymentRequests.Add(payment);
            // Store the mutual request pointer after its FK target exists, within the same transaction.

            var (collection, receipt) = SubscriptionCollectionPosting.Create(payment, renewal, m.PlayerId, input.Players[m.LegacyPlayerId]["Name"], sports[p.Plan.SportId], p.Plan.ArabicName,
                r["Method"] == "نقدي" ? "Cash" : "Online", r["Method"] + " — سجل تاريخي معتمد", at, job.CreatedAtUtc,
                "historical-import:" + job.ActorUserId, collectionId, Id("receipt", r["Code"]), "HIST-" + collectionId.ToString("N")[..16].ToUpperInvariant());
            postings.Add((renewal, collection, receipt, period));
            evidence.Add(new() { Id = Id("payment-evidence", r["Code"]), AcademyId = academy, JobId = job.Id,
                BranchId = m.BranchId, SubscriptionPeriodId = period.Id, CollectionId = collection.Id, SourceSystem = source,
                ExternalCode = r["Code"], Evidence = r["Evidence"], ContentHash = ImportCommitService.Hash(r), ApprovedByUserId = job.ActorUserId,
                CreatedAtUtc = job.CreatedAtUtc, UpdatedAtUtc = job.CreatedAtUtc });
        }
        await db.SaveChangesAsync(token);
        foreach (var posting in postings) { posting.Renewal.PaymentRequestId = posting.Collection.PaymentRequestId; db.Collections.Add(posting.Collection); db.Receipts.Add(posting.Receipt); }
        db.ImportedHistoricalPayments.AddRange(evidence);
        await db.SaveChangesAsync(token);
    }
}
