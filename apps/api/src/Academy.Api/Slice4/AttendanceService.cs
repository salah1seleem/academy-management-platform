using System.Data;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice4;

public sealed record PlayerAttendanceChange(Guid SportEnrollmentId, AttendanceStatus Status);
public sealed record StaffAttendanceChange(Guid AcademyMembershipId, AttendanceStatus Status);
public sealed record AttendanceSaveResult(Guid SubjectId, string Status, bool SessionConsumed, int? RemainingSessions, string? Warning);

public sealed class AttendanceService(FoundationDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<AttendanceSaveResult>> SavePlayersAsync(Guid academyId, Guid sessionId, Guid actorId, IReadOnlyList<PlayerAttendanceChange> changes, CancellationToken ct = default)
    {
        if (changes.Count == 0 || changes.Select(x => x.SportEnrollmentId).Distinct().Count() != changes.Count) throw new AttendanceValidationException("قائمة الحضور فارغة أو تحتوي لاعبًا مكررًا.");
        if (changes.Any(x => !Enum.IsDefined(x.Status))) throw new AttendanceValidationException("حالة حضور اللاعب غير صحيحة.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await LockAsync(academyId, sessionId, Guid.Empty, ct);
        var session = await db.TrainingSessions.SingleOrDefaultAsync(x => x.AcademyId == academyId && x.Id == sessionId, ct) ?? throw new AttendanceNotFoundException();
        if (session.Status == TrainingSessionStatus.Cancelled) throw new AttendanceConflictException("لا يمكن تسجيل الحضور لحصة ملغاة.");
        var ids = changes.Select(x => x.SportEnrollmentId).ToArray();
        var valid = await db.SportEnrollments.CountAsync(x => x.AcademyId == academyId && ids.Contains(x.Id) && x.TrainingGroupId == session.TrainingGroupId && x.IsActive, ct);
        if (valid != ids.Length) throw new AttendanceNotFoundException();
        var results = new List<AttendanceSaveResult>();
        foreach (var change in changes.OrderBy(x => x.SportEnrollmentId))
        {
            var attendance = await db.PlayerAttendances.SingleOrDefaultAsync(x => x.AcademyId == academyId && x.TrainingSessionId == sessionId && x.SportEnrollmentId == change.SportEnrollmentId, ct);
            var previous = attendance?.Status ?? AttendanceStatus.NotRecorded;
            if (attendance is null)
            {
                attendance = new PlayerAttendance { Id = Guid.NewGuid(), AcademyId = academyId, TrainingSessionId = sessionId, TrainingGroupId = session.TrainingGroupId, SportEnrollmentId = change.SportEnrollmentId, Status = AttendanceStatus.NotRecorded, RecordedByUserId = actorId, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
                db.PlayerAttendances.Add(attendance);
            }
            int? remaining = null; string? warning = null; var consumed = attendance.ConsumedSubscriptionPeriodId is not null;
            if (previous == AttendanceStatus.Present && change.Status != AttendanceStatus.Present && attendance.ConsumedSubscriptionPeriodId is Guid consumedPeriod)
            {
                remaining = await RestoreAsync(attendance, consumedPeriod, actorId, ct); consumed = false;
            }
            if (previous != AttendanceStatus.Present && change.Status == AttendanceStatus.Present)
            {
                var effect = await ConsumeAsync(attendance, session.SessionDate, actorId, ct); consumed = effect.consumed; remaining = effect.remaining; warning = effect.warning;
            }
            attendance.Status = change.Status; attendance.RecordedByUserId = actorId; attendance.UpdatedAtUtc = clock.GetUtcNow();
            results.Add(new AttendanceSaveResult(change.SportEnrollmentId, change.Status.ToString(), consumed, remaining, warning));
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return results;
    }

    public async Task<IReadOnlyList<AttendanceSaveResult>> SaveStaffAsync(Guid academyId, Guid sessionId, Guid actorId, IReadOnlyList<StaffAttendanceChange> changes, CancellationToken ct = default)
    {
        if (changes.Count == 0 || changes.Select(x => x.AcademyMembershipId).Distinct().Count() != changes.Count) throw new AttendanceValidationException("قائمة حضور الجهاز الفني غير صالحة.");
        if (changes.Any(x => !Enum.IsDefined(x.Status))) throw new AttendanceValidationException("حالة حضور الموظف غير صحيحة.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await LockAsync(academyId, sessionId, Guid.Empty, ct);
        var session = await db.TrainingSessions.SingleOrDefaultAsync(x => x.AcademyId == academyId && x.Id == sessionId, ct) ?? throw new AttendanceNotFoundException();
        if (session.Status == TrainingSessionStatus.Cancelled) throw new AttendanceConflictException("لا يمكن تسجيل الحضور لحصة ملغاة.");
        var ids = changes.Select(x => x.AcademyMembershipId).ToArray();
        var assigned = await db.StaffGroupAssignments.CountAsync(x => x.AcademyId == academyId && x.TrainingGroupId == session.TrainingGroupId && ids.Contains(x.AcademyMembershipId) && x.IsActive, ct);
        if (assigned != ids.Length) throw new AttendanceNotFoundException();
        var output = new List<AttendanceSaveResult>();
        foreach (var change in changes.OrderBy(x => x.AcademyMembershipId))
        {
            var attendance = await db.StaffAttendances.SingleOrDefaultAsync(x => x.AcademyId == academyId && x.TrainingSessionId == sessionId && x.AcademyMembershipId == change.AcademyMembershipId, ct);
            if (attendance is null)
            {
                attendance = new StaffAttendance { Id = Guid.NewGuid(), AcademyId = academyId, TrainingSessionId = sessionId, TrainingGroupId = session.TrainingGroupId, AcademyMembershipId = change.AcademyMembershipId, CreatedAtUtc = clock.GetUtcNow() };
                db.StaffAttendances.Add(attendance);
            }
            attendance.Status = change.Status; attendance.RecordedByUserId = actorId; attendance.UpdatedAtUtc = clock.GetUtcNow();
            output.Add(new AttendanceSaveResult(change.AcademyMembershipId, change.Status.ToString(), false, null, null));
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return output;
    }

    private async Task<(bool consumed, int? remaining, string? warning)> ConsumeAsync(PlayerAttendance attendance, DateOnly sessionDate, Guid actorId, CancellationToken ct)
    {
        var periods = await db.SubscriptionPeriods.Include(x => x.SubscriptionPlan)
            .Where(x => x.AcademyId == attendance.AcademyId && x.SportEnrollmentId == attendance.SportEnrollmentId && x.Status != SubscriptionPeriodStatus.Cancelled && x.Status != SubscriptionPeriodStatus.Frozen && x.StartDate <= sessionDate && (x.EndDate == null || x.EndDate >= sessionDate))
            .OrderBy(x => x.StartDate).ThenBy(x => x.CreatedAtUtc).ToListAsync(ct);
        var period = periods.FirstOrDefault(x => x.SubscriptionPlan.PlanType == SubscriptionPlanType.Duration || x.RemainingSessions is > 0)
            ?? periods.FirstOrDefault();
        if (period is null) return (false, null, "تم تسجيل الحضور دون خصم: لا توجد فترة اشتراك مؤهلة.");
        if (period.SubscriptionPlan.PlanType == SubscriptionPlanType.Duration) return (false, null, null);
        if (period.RemainingSessions is not > 0) return (false, period.RemainingSessions ?? 0, "تم تسجيل الحضور دون خصم: رصيد الحصص غير كافٍ.");
        var before = period.RemainingSessions.Value; period.RemainingSessions = before - 1; period.UpdatedAtUtc = clock.GetUtcNow();
        attendance.ConsumedSubscriptionPeriodId = period.Id;
        db.SubscriptionSessionMovements.Add(new SubscriptionSessionMovement { Id = Guid.NewGuid(), AcademyId = attendance.AcademyId, SubscriptionPeriodId = period.Id, PlayerAttendanceId = attendance.Id, MovementType = SessionMovementType.AttendanceConsume, Quantity = -1, BalanceBefore = before, BalanceAfter = before - 1, OccurredAtUtc = clock.GetUtcNow(), PerformedByUserId = actorId, Reason = "حضور لاعب مؤهل", CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() });
        return (true, before - 1, null);
    }

    private async Task<int> RestoreAsync(PlayerAttendance attendance, Guid periodId, Guid actorId, CancellationToken ct)
    {
        var period = await db.SubscriptionPeriods.SingleAsync(x => x.AcademyId == attendance.AcademyId && x.Id == periodId, ct);
        var consume = await db.SubscriptionSessionMovements.Where(x => x.AcademyId == attendance.AcademyId && x.PlayerAttendanceId == attendance.Id && x.SubscriptionPeriodId == periodId && x.MovementType == SessionMovementType.AttendanceConsume && !db.SubscriptionSessionMovements.Any(r => r.AcademyId == attendance.AcademyId && r.ReversesMovementId == x.Id))
            .OrderByDescending(x => x.OccurredAtUtc).FirstOrDefaultAsync(ct) ?? throw new AttendanceConflictException("تعذر التحقق من حركة الخصم الأصلية.");
        var before = period.RemainingSessions ?? 0;
        if (period.InitialSessions is int initial && before >= initial) throw new AttendanceConflictException("لا يمكن استعادة رصيد يتجاوز الرصيد الأصلي.");
        period.RemainingSessions = before + 1; period.UpdatedAtUtc = clock.GetUtcNow(); attendance.ConsumedSubscriptionPeriodId = null;
        db.SubscriptionSessionMovements.Add(new SubscriptionSessionMovement { Id = Guid.NewGuid(), AcademyId = attendance.AcademyId, SubscriptionPeriodId = period.Id, PlayerAttendanceId = attendance.Id, ReversesMovementId = consume.Id, MovementType = SessionMovementType.AttendanceRestore, Quantity = 1, BalanceBefore = before, BalanceAfter = before + 1, OccurredAtUtc = clock.GetUtcNow(), PerformedByUserId = actorId, Reason = "تصحيح حالة الحضور", CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() });
        return before + 1;
    }

    private Task LockAsync(Guid academyId, Guid sessionId, Guid subjectId, CancellationToken ct)
    {
        var key = $"attendance:{academyId:N}:{sessionId:N}:{subjectId:N}";
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
    }
}

public sealed class AttendanceValidationException(string message) : Exception(message);
public sealed class AttendanceConflictException(string message) : Exception(message);
public sealed class AttendanceNotFoundException : Exception;
