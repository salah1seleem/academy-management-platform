using System.Data;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;

public sealed record SubscriptionAdjustmentResult(
    Guid PeriodId,
    string Status,
    DateOnly StartDate,
    DateOnly? EndDate,
    DateOnly? FrozenFromDate,
    int? RemainingSessions,
    uint Version,
    Guid AdjustmentId,
    string AdjustmentType,
    DateOnly EffectiveDate,
    int? DaysDelta,
    bool Replay);

public static class SubscriptionPeriodState
{
    public static SubscriptionPeriodStatus Resolve(SubscriptionPeriodStatus stored, DateOnly start, DateOnly? end, SubscriptionPlanType planType, int? remainingSessions, DateOnly today)
    {
        if (stored is SubscriptionPeriodStatus.Cancelled or SubscriptionPeriodStatus.Frozen) return stored;
        if (start > today) return SubscriptionPeriodStatus.Scheduled;
        if (end is DateOnly endDate && endDate < today) return SubscriptionPeriodStatus.Expired;
        if (planType is SubscriptionPlanType.Sessions or SubscriptionPlanType.Combined && remainingSessions is <= 0) return SubscriptionPeriodStatus.Expired;
        return SubscriptionPeriodStatus.Active;
    }
}

public sealed class SubscriptionAdjustmentService(FoundationDbContext db, ISubscriptionClock clock)
{
    public Task<SubscriptionAdjustmentResult> FreezeAsync(Guid academyId, Guid actorId, Guid periodId, DateOnly effectiveDate, string reason, uint expectedVersion, string key, CancellationToken ct = default)
        => ExecuteAsync(academyId, actorId, periodId, SubscriptionAdjustmentType.FreezeStarted, expectedVersion, key, reason, async period =>
        {
            if (period.SubscriptionPlan.PlanType == SubscriptionPlanType.Sessions || period.EndDate is null)
                throw new AdjustmentValidationException("تجميد الباقة بالحصة فقط غير مدعوم في هذا الإصدار.");
            var status = Current(period);
            if (status == SubscriptionPeriodStatus.Frozen) throw new AdjustmentConflictException("الاشتراك مجمد بالفعل.");
            if (status == SubscriptionPeriodStatus.Cancelled) throw new AdjustmentConflictException("لا يمكن تجميد اشتراك ملغي.");
            if (status != SubscriptionPeriodStatus.Active) throw new AdjustmentValidationException("يمكن تجميد فترة اشتراك نشطة فقط.");
            if (effectiveDate < period.StartDate || effectiveDate > period.EndDate || effectiveDate > clock.Today)
                throw new AdjustmentValidationException("تاريخ التجميد يجب أن يقع داخل الفترة النشطة وحتى تاريخ اليوم.");
            period.Status = SubscriptionPeriodStatus.Frozen;
            period.FrozenFromDate = effectiveDate;
            return await Task.FromResult(new AdjustmentDraft(effectiveDate, null, period.EndDate, null, null));
        }, ct);

    public Task<SubscriptionAdjustmentResult> ResumeAsync(Guid academyId, Guid actorId, Guid periodId, DateOnly resumeDate, string reason, uint expectedVersion, string key, CancellationToken ct = default)
        => ExecuteAsync(academyId, actorId, periodId, SubscriptionAdjustmentType.FreezeEnded, expectedVersion, key, reason, async period =>
        {
            if (period.Status != SubscriptionPeriodStatus.Frozen || period.FrozenFromDate is not DateOnly frozenFrom)
                throw new AdjustmentConflictException("الاشتراك ليس مجمدًا حاليًا.");
            if (resumeDate <= frozenFrom || resumeDate > clock.Today)
                throw new AdjustmentValidationException("تاريخ الاستئناف يجب أن يكون بعد بداية التجميد وحتى تاريخ اليوم.");
            if (period.EndDate is not DateOnly oldEnd) throw new AdjustmentValidationException("هذه الباقة لا تحتوي مدة زمنية قابلة للاستئناف.");
            var freezeStart = await db.SubscriptionAdjustments.Where(x => x.AcademyId == academyId && x.SubscriptionPeriodId == period.Id && x.AdjustmentType == SubscriptionAdjustmentType.FreezeStarted && !db.SubscriptionAdjustments.Any(y => y.AcademyId == academyId && y.RelatedAdjustmentId == x.Id))
                .OrderByDescending(x => x.PerformedAtUtc).FirstOrDefaultAsync(ct)
                ?? throw new AdjustmentConflictException("تعذر العثور على بداية التجميد النشطة.");
            var frozenDays = resumeDate.DayNumber - frozenFrom.DayNumber;
            var newEnd = oldEnd.AddDays(frozenDays);
            period.EndDate = newEnd;
            period.FrozenFromDate = null;
            period.Status = SubscriptionPeriodState.Resolve(SubscriptionPeriodStatus.Active, period.StartDate, newEnd, period.SubscriptionPlan.PlanType, period.RemainingSessions, clock.Today);
            return new AdjustmentDraft(resumeDate, frozenDays, oldEnd, newEnd, freezeStart.Id);
        }, ct);

    public Task<SubscriptionAdjustmentResult> AdjustDaysAsync(Guid academyId, Guid actorId, Guid periodId, int days, bool add, string reason, uint expectedVersion, string key, CancellationToken ct = default)
        => ExecuteAsync(academyId, actorId, periodId, add ? SubscriptionAdjustmentType.DaysAdded : SubscriptionAdjustmentType.DaysDeducted, expectedVersion, key, reason, period =>
        {
            if (days is < 1 or > 365) throw new AdjustmentValidationException("عدد الأيام يجب أن يكون من 1 إلى 365.");
            if (period.Status == SubscriptionPeriodStatus.Cancelled) throw new AdjustmentConflictException("لا يمكن تعديل مدة اشتراك ملغي.");
            if (period.SubscriptionPlan.PlanType == SubscriptionPlanType.Sessions || period.EndDate is null)
                throw new AdjustmentValidationException("هذه الباقة تعتمد على عدد الحصص ولا تحتوي مدة زمنية قابلة للتعديل.");
            var oldEnd = period.EndDate.Value;
            var delta = add ? days : -days;
            var newEnd = oldEnd.AddDays(delta);
            if (newEnd < period.StartDate) throw new AdjustmentValidationException("لا يمكن أن يصبح تاريخ النهاية قبل تاريخ البداية.");
            period.EndDate = newEnd;
            if (period.Status != SubscriptionPeriodStatus.Frozen)
                period.Status = SubscriptionPeriodState.Resolve(period.Status, period.StartDate, newEnd, period.SubscriptionPlan.PlanType, period.RemainingSessions, clock.Today);
            return Task.FromResult(new AdjustmentDraft(clock.Today, delta, oldEnd, newEnd, null));
        }, ct);

    public Task<SubscriptionAdjustmentResult> CancelAsync(Guid academyId, Guid actorId, Guid periodId, DateOnly effectiveDate, string reason, uint expectedVersion, string key, CancellationToken ct = default)
        => ExecuteAsync(academyId, actorId, periodId, SubscriptionAdjustmentType.Cancelled, expectedVersion, key, reason, period =>
        {
            if (effectiveDate == default) throw new AdjustmentValidationException("تاريخ سريان الإلغاء مطلوب.");
            if (period.Status == SubscriptionPeriodStatus.Cancelled) throw new AdjustmentConflictException("تم إلغاء هذه الفترة بالفعل.");
            period.Status = SubscriptionPeriodStatus.Cancelled;
            period.FrozenFromDate = null;
            return Task.FromResult(new AdjustmentDraft(effectiveDate, null, period.EndDate, null, null));
        }, ct);

    private async Task<SubscriptionAdjustmentResult> ExecuteAsync(
        Guid academyId,
        Guid actorId,
        Guid periodId,
        SubscriptionAdjustmentType type,
        uint expectedVersion,
        string key,
        string reason,
        Func<SubscriptionPeriod, Task<AdjustmentDraft>> mutate,
        CancellationToken ct)
    {
        var normalizedReason = NormalizeReason(reason);
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new AdjustmentValidationException("Idempotency-Key مطلوب ولا يتجاوز 100 حرف.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var replay = await db.SubscriptionAdjustments.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == academyId && x.PerformedByUserId == actorId && x.AdjustmentType == type && x.IdempotencyKey == key, ct);
        if (replay is not null)
        {
            var replayPeriod = await db.SubscriptionPeriods.AsNoTracking().Include(x => x.SubscriptionPlan).SingleOrDefaultAsync(x => x.AcademyId == academyId && x.Id == replay.SubscriptionPeriodId, ct) ?? throw new AdjustmentNotFoundException();
            await transaction.CommitAsync(ct);
            return Result(replayPeriod, replay, true);
        }

        var period = await db.SubscriptionPeriods.Include(x => x.SubscriptionPlan).SingleOrDefaultAsync(x => x.AcademyId == academyId && x.Id == periodId, ct) ?? throw new AdjustmentNotFoundException();
        if (period.Version != expectedVersion) throw new AdjustmentConflictException("تغير الاشتراك منذ فتح الصفحة. أعد التحميل قبل تنفيذ الإجراء.");
        var draft = await mutate(period);
        var adjustment = new SubscriptionAdjustment
        {
            Id = Guid.NewGuid(), AcademyId = academyId, SubscriptionPeriodId = period.Id, AdjustmentType = type,
            EffectiveDate = draft.EffectiveDate, DaysDelta = draft.DaysDelta, OldEndDate = draft.OldEndDate,
            NewEndDate = draft.NewEndDate, Reason = normalizedReason, PerformedByUserId = actorId,
            RelatedAdjustmentId = draft.RelatedAdjustmentId, IdempotencyKey = key, PerformedAtUtc = clock.UtcNow,
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        period.UpdatedAtUtc = clock.UtcNow;
        db.SubscriptionAdjustments.Add(adjustment);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result(period, adjustment, false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AdjustmentConflictException("تغير الاشتراك أثناء الحفظ. أعد تحميل الصفحة وحاول مرة أخرى.");
        }
        catch (DbUpdateException)
        {
            throw new AdjustmentConflictException("تعذر تنفيذ الأمر بأمان بسبب تعارض أو إعادة إرسال. أعد تحميل الصفحة.");
        }
    }

    private SubscriptionPeriodStatus Current(SubscriptionPeriod period)
        => SubscriptionPeriodState.Resolve(period.Status, period.StartDate, period.EndDate, period.SubscriptionPlan.PlanType, period.RemainingSessions, clock.Today);

    private static string NormalizeReason(string reason)
    {
        var value = reason?.Trim() ?? string.Empty;
        if (value.Length == 0) throw new AdjustmentValidationException("سبب التعديل مطلوب.");
        if (value.Length > 500) throw new AdjustmentValidationException("سبب التعديل لا يتجاوز 500 حرف.");
        return value;
    }

    private static SubscriptionAdjustmentResult Result(SubscriptionPeriod period, SubscriptionAdjustment adjustment, bool replay)
        => new(period.Id, period.Status.ToString(), period.StartDate, period.EndDate, period.FrozenFromDate, period.RemainingSessions, period.Version,
            adjustment.Id, adjustment.AdjustmentType.ToString(), adjustment.EffectiveDate, adjustment.DaysDelta, replay);

    private sealed record AdjustmentDraft(DateOnly EffectiveDate, int? DaysDelta, DateOnly? OldEndDate, DateOnly? NewEndDate, Guid? RelatedAdjustmentId);
}

public sealed class AdjustmentValidationException(string message) : Exception(message);
public sealed class AdjustmentConflictException(string message) : Exception(message);
public sealed class AdjustmentNotFoundException : Exception;
