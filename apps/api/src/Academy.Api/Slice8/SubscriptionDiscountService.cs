using System.Data;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;

public sealed record RenewalDiscountResult(Guid RenewalRequestId, decimal OriginalAmount, string? DiscountType,
    decimal? DiscountValue, decimal DiscountAmount, decimal FinalAmount, Guid? PaymentRequestId,
    uint Version, Guid AdjustmentId, bool Replay);

public sealed class SubscriptionDiscountService(FoundationDbContext db, IPaymentGateway gateway, ISubscriptionClock clock)
{
    public Task<RenewalDiscountResult> ApplyAsync(Guid academyId, Guid actorId, Guid renewalId,
        RenewalDiscountType type, decimal value, string reason, uint expectedVersion, string key, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(type)) throw new DiscountValidationException("نوع الخصم غير صحيح.");
        if (value <= 0) throw new DiscountValidationException("قيمة الخصم يجب أن تكون أكبر من صفر.");
        return ExecuteAsync(academyId, actorId, renewalId, type, value, reason, expectedVersion, key, ct);
    }

    public Task<RenewalDiscountResult> RemoveAsync(Guid academyId, Guid actorId, Guid renewalId,
        string reason, uint expectedVersion, string key, CancellationToken ct = default)
        => ExecuteAsync(academyId, actorId, renewalId, null, null, reason, expectedVersion, key, ct);

    private async Task<RenewalDiscountResult> ExecuteAsync(Guid academyId, Guid actorId, Guid renewalId,
        RenewalDiscountType? type, decimal? value, string reason, uint expectedVersion, string key, CancellationToken ct)
    {
        var normalizedReason = NormalizeReason(reason);
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
            throw new DiscountValidationException("Idempotency-Key مطلوب ولا يتجاوز 100 حرف.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var replay = await db.RenewalDiscountAdjustments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.AcademyId == academyId && x.PerformedByUserId == actorId && x.IdempotencyKey == key, ct);
        if (replay is not null)
        {
            var replayRenewal = await db.RenewalRequests.AsNoTracking().SingleOrDefaultAsync(
                x => x.AcademyId == academyId && x.Id == replay.RenewalRequestId, ct) ?? throw new DiscountNotFoundException();
            await transaction.CommitAsync(ct);
            return Result(replayRenewal, replay, true);
        }

        var renewal = await db.RenewalRequests.SingleOrDefaultAsync(x => x.AcademyId == academyId && x.Id == renewalId, ct)
            ?? throw new DiscountNotFoundException();
        if (renewal.Version != expectedVersion)
            throw new DiscountConflictException("تغير طلب التجديد منذ فتح الصفحة. أعد التحميل قبل حفظ الخصم.");
        if (renewal.Status is not (RenewalRequestStatus.PendingPayment or RenewalRequestStatus.PaymentInProgress))
            throw new DiscountConflictException("لا يمكن تعديل الخصم بعد تأكيد أو انتهاء محاولة الدفع.");

        var oldPayment = await db.PaymentRequests.SingleOrDefaultAsync(
            x => x.AcademyId == academyId && x.Id == renewal.PaymentRequestId && x.RenewalRequestId == renewal.Id, ct)
            ?? throw new DiscountConflictException("طلب الدفع الحالي غير موجود.");
        if (oldPayment.Status is not (PaymentRequestStatus.Created or PaymentRequestStatus.Pending))
            throw new DiscountConflictException("لا يمكن تعديل الخصم بعد تأكيد أو انتهاء محاولة الدفع.");

        var original = renewal.OriginalAmount;
        decimal discount;
        if (type is null)
        {
            if (renewal.DiscountType is null) throw new DiscountConflictException("لا يوجد خصم حالي لإزالته.");
            discount = 0m;
        }
        else if (type == RenewalDiscountType.Percentage)
        {
            if (value is null or <= 0 or > 100) throw new DiscountValidationException("النسبة يجب أن تكون أكبر من صفر وحتى 100%.");
            discount = decimal.Round(original * value.Value / 100m, 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            if (value is null or <= 0 || value > original) throw new DiscountValidationException("المبلغ الثابت يجب أن يكون أكبر من صفر ولا يتجاوز السعر الأصلي.");
            discount = decimal.Round(value.Value, 2, MidpointRounding.AwayFromZero);
        }
        if (type is not null && discount <= 0)
            throw new DiscountValidationException("قيمة الخصم بعد التقريب يجب أن تكون أكبر من صفر.");
        if (discount < 0 || discount > original) throw new DiscountValidationException("قيمة الخصم غير صالحة.");
        var final = original - discount;

        var adjustment = new RenewalDiscountAdjustment
        {
            Id = Guid.NewGuid(), AcademyId = academyId, RenewalRequestId = renewal.Id,
            PreviousDiscountType = renewal.DiscountType, PreviousDiscountValue = renewal.DiscountValue,
            NewDiscountType = type, NewDiscountValue = value,
            PreviousDiscountAmount = renewal.DiscountAmount, NewDiscountAmount = discount,
            PreviousFinalAmount = renewal.FinalAmount, NewFinalAmount = final,
            Reason = normalizedReason, PerformedByUserId = actorId, IdempotencyKey = key,
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };

        oldPayment.Status = PaymentRequestStatus.Cancelled;
        oldPayment.UpdatedAtUtc = clock.UtcNow;
        var paymentId = Guid.NewGuid();
        var checkout = gateway.CreateCheckout(academyId, paymentId);
        db.PaymentRequests.Add(new PaymentRequest
        {
            Id = paymentId, AcademyId = academyId, RenewalRequestId = renewal.Id,
            Provider = checkout.Provider, ProviderEnvironment = oldPayment.ProviderEnvironment,
            Amount = final, Currency = renewal.Currency, Status = PaymentRequestStatus.Pending,
            ProviderReference = checkout.ProviderReference, CheckoutReference = checkout.CheckoutReference,
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        });
        renewal.DiscountType = type;
        renewal.DiscountValue = value;
        renewal.DiscountAmount = discount;
        renewal.FinalAmount = final;
        renewal.AmountExpected = final;
        renewal.DiscountReason = normalizedReason;
        renewal.DiscountAppliedByUserId = actorId;
        renewal.DiscountAppliedAtUtc = clock.UtcNow;
        renewal.PaymentRequestId = paymentId;
        renewal.Status = RenewalRequestStatus.PaymentInProgress;
        renewal.UpdatedAtUtc = clock.UtcNow;
        db.RenewalDiscountAdjustments.Add(adjustment);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result(renewal, adjustment, false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DiscountConflictException("تغير طلب التجديد أثناء الحفظ. أعد تحميل الصفحة وحاول مرة أخرى.");
        }
        catch (DbUpdateException)
        {
            throw new DiscountConflictException("تعذر تنفيذ الأمر بأمان بسبب تعارض أو إعادة إرسال. أعد تحميل الصفحة.");
        }
    }

    private static string NormalizeReason(string reason)
    {
        var value = reason?.Trim() ?? string.Empty;
        if (value.Length == 0) throw new DiscountValidationException("سبب الخصم أو إزالته مطلوب.");
        if (value.Length > 500) throw new DiscountValidationException("السبب لا يتجاوز 500 حرف.");
        return value;
    }

    private static RenewalDiscountResult Result(RenewalRequest renewal, RenewalDiscountAdjustment adjustment, bool replay)
        => new(renewal.Id, renewal.OriginalAmount, renewal.DiscountType?.ToString(), renewal.DiscountValue,
            renewal.DiscountAmount, renewal.FinalAmount, renewal.PaymentRequestId, renewal.Version, adjustment.Id, replay);
}

public sealed class DiscountValidationException(string message) : Exception(message);
public sealed class DiscountConflictException(string message) : Exception(message);
public sealed class DiscountNotFoundException : Exception;
