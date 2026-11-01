using System.Data;
using Academy.Api.Slice3;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
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
    bool Replay,
    int? SessionsDelta = null,
    int? OldRemainingSessions = null,
    int? NewRemainingSessions = null);

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
    public async Task<bool> CanGrantSessionsAsync(Guid academyId, Guid actorId, Guid membershipId, CancellationToken ct = default)
    {
        var member = await db.AcademyMemberships.AsNoTracking()
            .Where(m => m.AcademyId == academyId && m.Id == membershipId && m.UserId == actorId && m.IsActive && m.User.IsActive)
            .Select(m => new { m.Role, m.UseActionGrants }).SingleOrDefaultAsync(ct);
        if (member?.Role == AcademyRole.AcademyOwner) return true;
        if (member?.Role != AcademyRole.AcademyAdmin) return false;
        if (!await db.StaffGrants.AnyAsync(g => g.AcademyId == academyId && g.MembershipId == membershipId && g.IsActive && g.Permission == "workflow.subscriptions.sessions" && g.Allowed, ct)) return false;
        if (await db.StaffGrants.AnyAsync(g => g.AcademyId == academyId && g.MembershipId == membershipId && g.IsActive && g.Permission == AcademyPermissions.SubscriptionAdjustmentManage && !g.Allowed, ct)) return false;
        return !member.UseActionGrants || await db.StaffActionGrants.AnyAsync(g => g.AcademyId == academyId && g.MembershipId == membershipId && g.IsActive && g.Module == "subscriptions" && g.Read && g.Edit, ct);
    }

    public async Task<SubscriptionAdjustmentResult> GrantSessionsAsync(Guid academyId, Guid actorId, Guid membershipId, Guid periodId, int sessions, DateOnly endDate, string? reason, uint expectedVersion, string key, CancellationToken ct = default)
    {
        if (!await CanGrantSessionsAsync(academyId, actorId, membershipId, ct)) throw new AdjustmentForbiddenException();
        return await ExecuteAsync(academyId, actorId, periodId, SubscriptionAdjustmentType.SessionsAdded, expectedVersion, key, reason, async period =>
        {
            if (!await CanGrantSessionsAsync(academyId, actorId, membershipId, ct)) throw new AdjustmentForbiddenException();
            if (sessions is < 1 or > 10000) throw new AdjustmentValidationException("أدخل عدد حصص صحيحًا من1 إلى10000.");
            if (string.IsNullOrWhiteSpace(reason)) throw new AdjustmentValidationException("سبب إضافة الحصص مطلوب.");
            if (period.Status == SubscriptionPeriodStatus.Cancelled) throw new AdjustmentConflictException("لا يمكن إضافة حصص إلى اشتراك ملغي.");
            if ((period.ServiceKindSnapshot ?? period.SubscriptionPlan.ServiceKind) != AcademyServiceKind.Training ||
                (period.PlanTypeSnapshot ?? period.SubscriptionPlan.PlanType) == SubscriptionPlanType.Duration ||
                period.InitialSessions is null || period.RemainingSessions is not int before)
                throw new AdjustmentValidationException("هذه الفترة لا تحتوي رصيد حصص قابلًا للزيادة. لا تتحول باقة المدة تلقائيًا إلى باقة حصص.");
            if (endDate == default || endDate < clock.Today || endDate < period.StartDate || period.EndDate is DateOnly old && endDate < old)
                throw new AdjustmentValidationException("اختر نهاية لا تسبق اليوم أو بداية الفترة أو نهايتها الحالية.");
            if (!await db.SportEnrollments.AnyAsync(e => e.AcademyId == academyId && e.Id == period.SportEnrollmentId && e.IsActive && e.Player.IsActive, ct)) throw new AdjustmentNotFoundException();
            if (before > int.MaxValue - sessions) throw new AdjustmentValidationException("رصيد الحصص يتجاوز الحد المسموح.");
            // Existing periods can already overlap (for example a purchased session pack beside a dated plan).
            // Reject only overlap introduced by extending this period's previous finite end.
            if (period.EndDate is DateOnly previousEnd && endDate > previousEnd &&
                await db.SubscriptionPeriods.AnyAsync(p => p.AcademyId == academyId && p.SportEnrollmentId == period.SportEnrollmentId && p.Id != period.Id && p.IsActive &&
                p.Status != SubscriptionPeriodStatus.Cancelled && (p.RemainingSessions == null || p.RemainingSessions > 0) && p.StartDate <= endDate && (p.EndDate == null || p.EndDate > previousEnd), ct))
                throw new AdjustmentConflictException("النهاية المختارة تتداخل مع فترة اشتراك أخرى لها خدمة متبقية. راجع الفترات قبل الإضافة.");
            var oldEnd = period.EndDate;
            period.RemainingSessions = before + sessions;
            period.EndDate = endDate;
            if (period.Status != SubscriptionPeriodStatus.Frozen)
                period.Status = SubscriptionPeriodState.Resolve(period.Status, period.StartDate, endDate, period.PlanTypeSnapshot ?? period.SubscriptionPlan.PlanType, period.RemainingSessions, clock.Today);
            return new AdjustmentDraft(clock.Today, null, oldEnd, endDate, null, sessions, before, before + sessions);
        }, ct, replay => replay.SessionsDelta == sessions && replay.NewEndDate == endDate && replay.Reason == NormalizeReason(reason));
    }

    public Task<SubscriptionAdjustmentResult> FreezeAsync(Guid academyId, Guid actorId, Guid periodId, DateOnly effectiveDate, string? reason, uint expectedVersion, string key, CancellationToken ct = default)
        => ExecuteAsync(academyId, actorId, periodId, SubscriptionAdjustmentType.FreezeStarted, expectedVersion, key, reason, async period =>
        {
            if ((period.PlanTypeSnapshot ?? period.SubscriptionPlan.PlanType) == SubscriptionPlanType.Sessions || period.EndDate is null)
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

    public Task<SubscriptionAdjustmentResult> ResumeAsync(Guid academyId, Guid actorId, Guid periodId, DateOnly resumeDate, string? reason, uint expectedVersion, string key, CancellationToken ct = default)
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
            period.Status = SubscriptionPeriodState.Resolve(SubscriptionPeriodStatus.Active, period.StartDate, newEnd, (period.PlanTypeSnapshot ?? period.SubscriptionPlan.PlanType), period.RemainingSessions, clock.Today);
            return new AdjustmentDraft(resumeDate, frozenDays, oldEnd, newEnd, freezeStart.Id);
        }, ct);

    public Task<SubscriptionAdjustmentResult> AdjustDaysAsync(Guid academyId, Guid actorId, Guid periodId, int days, bool add, string? reason, uint expectedVersion, string key, CancellationToken ct = default)
        => ExecuteAsync(academyId, actorId, periodId, add ? SubscriptionAdjustmentType.DaysAdded : SubscriptionAdjustmentType.DaysDeducted, expectedVersion, key, reason, period =>
        {
            if (days is < 1 or > 365) throw new AdjustmentValidationException("عدد الأيام يجب أن يكون من 1 إلى 365.");
            if (period.Status == SubscriptionPeriodStatus.Cancelled) throw new AdjustmentConflictException("لا يمكن تعديل مدة اشتراك ملغي.");
            if ((period.PlanTypeSnapshot ?? period.SubscriptionPlan.PlanType) == SubscriptionPlanType.Sessions || period.EndDate is null)
                throw new AdjustmentValidationException("هذه الباقة تعتمد على عدد الحصص ولا تحتوي مدة زمنية قابلة للتعديل.");
            var oldEnd = period.EndDate.Value;
            var delta = add ? days : -days;
            var newEnd = oldEnd.AddDays(delta);
            if (newEnd < period.StartDate) throw new AdjustmentValidationException("لا يمكن أن يصبح تاريخ النهاية قبل تاريخ البداية.");
            period.EndDate = newEnd;
            if (period.Status != SubscriptionPeriodStatus.Frozen)
                period.Status = SubscriptionPeriodState.Resolve(period.Status, period.StartDate, newEnd, (period.PlanTypeSnapshot ?? period.SubscriptionPlan.PlanType), period.RemainingSessions, clock.Today);
            return Task.FromResult(new AdjustmentDraft(clock.Today, delta, oldEnd, newEnd, null));
        }, ct);

    public Task<SubscriptionAdjustmentResult> CancelAsync(Guid academyId, Guid actorId, Guid periodId, DateOnly effectiveDate, string? reason, uint expectedVersion, string key, CancellationToken ct = default)
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
        string? reason,
        Func<SubscriptionPeriod, Task<AdjustmentDraft>> mutate,
        CancellationToken ct,
        Func<SubscriptionAdjustment, bool>? matchesReplay = null)
    {
        var normalizedReason = NormalizeReason(reason);
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new AdjustmentValidationException("معرف العملية مطلوب ولا يتجاوز 100 حرف.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        if (type == SubscriptionAdjustmentType.SessionsAdded)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"subscription-session-grant:{academyId:N}:{actorId:N}:{key}"}, 0))", ct);
            var enrollment = await db.SubscriptionPeriods.Where(p => p.AcademyId == academyId && p.Id == periodId).Select(p => (Guid?)p.SportEnrollmentId).SingleOrDefaultAsync(ct) ?? throw new AdjustmentNotFoundException();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"renewal-enrollment:{academyId:N}:{enrollment:N}"}, 0))", ct);
            // Same enrollment -> player order as attendance; payment activation also locks this enrollment.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SportEnrollments\" WHERE \"AcademyId\"={academyId} AND \"Id\"={enrollment} FOR UPDATE", ct);
            var player = await db.SportEnrollments.Where(e => e.AcademyId == academyId && e.Id == enrollment).Select(e => e.PlayerId).SingleAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Players\" WHERE \"AcademyId\"={academyId} AND \"Id\"={player} FOR UPDATE", ct);
        }
        var replay = await db.SubscriptionAdjustments.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == academyId && x.PerformedByUserId == actorId && x.AdjustmentType == type && x.IdempotencyKey == key, ct);
        if (replay is not null)
        {
            if (replay.SubscriptionPeriodId != periodId || matchesReplay is not null && !matchesReplay(replay))
                throw new AdjustmentConflictException("مرجع التعديل مستخدم لعملية مختلفة. راجع العملية المحفوظة.");
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
            SessionsDelta = draft.SessionsDelta, OldRemainingSessions = draft.OldRemainingSessions, NewRemainingSessions = draft.NewRemainingSessions,
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        period.UpdatedAtUtc = clock.UtcNow;
        db.SubscriptionAdjustments.Add(adjustment);
        try
        {
            if(type is SubscriptionAdjustmentType.DaysAdded or SubscriptionAdjustmentType.DaysDeducted)
                await Academy.Api.Operations.GuardianLifecycleNotice.Add(db,academyId,period.SportEnrollmentId,type==SubscriptionAdjustmentType.DaysAdded?"DaysAdded":"DaysDeducted",adjustment.Id,"SubscriptionPeriod",period.Id,clock.UtcNow,count:Math.Abs(draft.DaysDelta??0),date:draft.EffectiveDate,token:ct);
            if (type == SubscriptionAdjustmentType.SessionsAdded)
                await Academy.Api.Operations.GuardianLifecycleNotice.Add(db, academyId, period.SportEnrollmentId, "SessionsAdded", adjustment.Id, "SubscriptionPeriod", period.Id, clock.UtcNow, count:draft.SessionsDelta, date:draft.NewEndDate, token:ct);
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
        => SubscriptionPeriodState.Resolve(period.Status, period.StartDate, period.EndDate, (period.PlanTypeSnapshot ?? period.SubscriptionPlan.PlanType), period.RemainingSessions, clock.Today);

    private static string NormalizeReason(string? reason)
    {
        var value = reason?.Trim() ?? string.Empty;
        if (value.Length > 500) throw new AdjustmentValidationException("سبب التعديل لا يتجاوز 500 حرف.");
        return value;
    }

    private static SubscriptionAdjustmentResult Result(SubscriptionPeriod period, SubscriptionAdjustment adjustment, bool replay)
        => new(period.Id, period.Status.ToString(), period.StartDate, period.EndDate, period.FrozenFromDate, period.RemainingSessions, period.Version,
            adjustment.Id, adjustment.AdjustmentType.ToString(), adjustment.EffectiveDate, adjustment.DaysDelta, replay, adjustment.SessionsDelta, adjustment.OldRemainingSessions, adjustment.NewRemainingSessions);

    private sealed record AdjustmentDraft(DateOnly EffectiveDate, int? DaysDelta, DateOnly? OldEndDate, DateOnly? NewEndDate, Guid? RelatedAdjustmentId, int? SessionsDelta = null, int? OldRemainingSessions = null, int? NewRemainingSessions = null);
}

public sealed class AdjustmentValidationException(string message) : Exception(message);
public sealed class AdjustmentConflictException(string message) : Exception(message);
public sealed class AdjustmentNotFoundException : Exception;
public sealed class AdjustmentForbiddenException : Exception;
