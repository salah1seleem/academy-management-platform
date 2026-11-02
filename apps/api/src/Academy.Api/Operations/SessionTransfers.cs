using System.Text.Json;
using Academy.Api.Configuration;
using Academy.Api.Facilities;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public sealed record SessionTransferPair(Guid SourceSessionId, Guid TargetSessionId);
public sealed record SessionTransferWrite(SessionTransferPair[] Pairs, string? Fingerprint = null);
public sealed record SessionTransferSlot(Guid Id, Guid GroupId, string Group, DateOnly Date, TimeOnly Start, TimeOnly End);
public sealed record SessionTransferImpact(SessionTransferSlot Source, SessionTransferSlot Target, int ExpectedPlayers, int? Capacity);
public sealed record SessionTransferPreview(string Fingerprint, SessionTransferImpact[] Changes, string[] Conflicts,
    string Effect = "استبدال الحصص المختارة فقط. لا تغيير للمجموعة الأساسية أو التحصيل أو الرصيد أو الحضور السابق. أجور العمل لا تتغير بمجرد النقل.");

public sealed partial class GroupPlacementService
{
    private static SessionTransferPair[] Pairs(SessionTransferWrite b)
    {
        if (b.Pairs is null || b.Pairs.Length is < 1 or > 100 || b.Pairs.Any(p => p.SourceSessionId == Guid.Empty || p.TargetSessionId == Guid.Empty || p.SourceSessionId == p.TargetSessionId) || b.Pairs.Select(p => p.SourceSessionId).Distinct().Count() != b.Pairs.Length || b.Pairs.Select(p => p.TargetSessionId).Distinct().Count() != b.Pairs.Length || b.Pairs.Any(p => b.Pairs.Any(o => o.TargetSessionId == p.SourceSessionId))) throw new FacilityRejected("اختر من حصة واحدة إلى100 حصة مختلفة، ولكل حصة بديل مستقل دون تكرار.", 422);
        return b.Pairs.OrderBy(p => p.SourceSessionId).ToArray();
    }
    private async Task<SessionTransferPreview> InspectSessions(Guid enrollment, SessionTransferWrite b, bool write)
    {
        var pairs = Pairs(b); var t = await Authority("players", write ? "Edit" : "Read"); await Authority("academy", write ? "Edit" : "Read");
        var e = await db.SportEnrollments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == enrollment && x.IsActive && x.Player.IsActive) ?? throw new FacilityRejected("التسجيل غير متاح.", 404);
        await Authority("players", write ? "Edit" : "Read", e.BranchId);
        var ids = pairs.SelectMany(p => new[] { p.SourceSessionId, p.TargetSessionId }).Order().ToArray();
        var slots = await db.TrainingSessions.AsNoTracking().Include(s => s.TrainingGroup).Where(s => ids.Contains(s.Id)).OrderBy(s => s.Id).ToArrayAsync();
        if (slots.Length != ids.Length || slots.Any(s => s.BranchId != e.BranchId || s.SportId != e.SportId)) throw new FacilityRejected("إحدى الحصص غير متاحة لنفس فرع ورياضة التسجيل.", 404);
        if (write)
        {
            await coordinator.LockCalendar(t.AcademyId, slots.Select(s => s.TrainingGroupId), "Edit");
            // Same order as timetable writes; attendance locks precede enrollment/player locks.
            foreach (var session in ids) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"attendance:{t.AcademyId:N}:{session:N}:{Guid.Empty:N}"}, 0))");
            await LockTransferPlayer(t.AcademyId, enrollment);
            // A timetable may have changed while we waited for its lock.
            slots = await db.TrainingSessions.AsNoTracking().Include(s => s.TrainingGroup).Where(s => ids.Contains(s.Id)).OrderBy(s => s.Id).ToArrayAsync();
            if (slots.Length != ids.Length || slots.Any(s => s.BranchId != e.BranchId || s.SportId != e.SportId)) throw new FacilityRejected("تغيرت الحصص؛ أعد المعاينة.");
        }
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, time); var conflicts = new List<string>();
        if (slots.Any(s => s.SessionDate < today || s.SessionDate > today.AddYears(1))) conflicts.Add("النقل للحصص من اليوم وحتى سنة فقط؛ لا نغير الماضي.");
        if (slots.Any(s => s.Status != TrainingSessionStatus.Scheduled || !s.TrainingGroup.IsActive)) conflicts.Add("الحصة الملغاة أو المنفذة أو المجموعة الموقوفة غير متاحة للنقل.");
        if (await db.PlayerAttendances.AnyAsync(a => a.SportEnrollmentId == enrollment && ids.Contains(a.TrainingSessionId))) conflicts.Add("إحدى الحصص لها سجل حضور؛ يبقى التاريخ محفوظًا.");
        if (await db.EnrollmentSessionMoves.AnyAsync(m => m.SportEnrollmentId == enrollment && (ids.Contains(m.SourceSessionId) || ids.Contains(m.TargetSessionId)))) conflicts.Add("إحدى الحصص لها نقل معتمد؛ لا نعيد نقلها أو نستبدل القرار بصمت.");
        if (await db.CompensationBookings.AnyAsync(c => c.SportEnrollmentId == enrollment && ids.Contains(c.TrainingSessionId) && c.State == Academy.Infrastructure.People.CompensationBookingState.Reserved)) conflicts.Add("الحصة لها تعويض محجوز؛ راجعه قبل النقل.");
        var impacts = new List<SessionTransferImpact>(); var eligibility = new List<object>();
        var allEnrollments = db.SportEnrollments.IgnoreQueryFilters(["trusted-branches", "trusted-sports"]);
        foreach (var p in pairs)
        {
            var source = slots.Single(s => s.Id == p.SourceSessionId); var target = slots.Single(s => s.Id == p.TargetSessionId);
            if (!await db.SportEnrollments.Where(x => x.Id == enrollment).Select(x => FoundationDbContext.IsSessionAssigned(x.AcademyId, x.Id, source.Id)).SingleAsync()) conflicts.Add("الحصة الأصلية ليست ضمن مواعيد اللاعب الحالية.");
            if (await db.SportEnrollments.Where(x => x.Id == enrollment).Select(x => FoundationDbContext.IsSessionAssigned(x.AcademyId, x.Id, target.Id)).SingleAsync()) conflicts.Add("اللاعب مسجل بالفعل في الحصة البديلة.");
            var (_, group) = await Group(target.TrainingGroupId, false);
            var review = await Inspect(group, [enrollment], effectiveDate: target.SessionDate, transfer: true, occurrence: target.Id);
            eligibility.Add(review.Fingerprint); conflicts.AddRange(review.Conflicts); conflicts.AddRange(review.Players.SelectMany(x => x.Reasons));
            if (await db.TrainingSessions.IgnoreQueryFilters(["trusted-branches", "trusted-sports"]).AnyAsync(s => s.AcademyId == t.AcademyId && !ids.Contains(s.Id) && s.SessionDate == target.SessionDate && s.Status != TrainingSessionStatus.Cancelled && s.StartTime < target.EndTime && target.StartTime < s.EndTime && allEnrollments.Any(x => x.AcademyId == t.AcademyId && x.PlayerId == e.PlayerId && x.IsActive && FoundationDbContext.IsSessionAssigned(x.AcademyId, x.Id, s.Id)))) conflicts.Add("الحصة البديلة تتعارض مع موعد آخر للاعب.");
            impacts.Add(new(Slot(source), Slot(target), review.CurrentPlayers + 1, group.Capacity));
        }
        var targetIds = pairs.Select(p => p.TargetSessionId).ToArray(); var targets = slots.Where(s => targetIds.Contains(s.Id)).ToArray();
        if (targets.Any(s => targets.Any(o => o.Id != s.Id && o.SessionDate == s.SessionDate && o.StartTime < s.EndTime && s.StartTime < o.EndTime))) conflicts.Add("الحصص البديلة المختارة تتعارض مع بعضها.");
        var fingerprint = AcademyConfigurationService.Hash(JsonSerializer.Serialize(new { enrollment, pairs, today, e.Version, slots = slots.Select(s => new { s.Id, s.Version }), eligibility, conflicts }));
        return new(fingerprint, impacts.ToArray(), conflicts.Distinct().ToArray());
    }
    private static SessionTransferSlot Slot(TrainingSession s) => new(s.Id, s.TrainingGroupId, s.TrainingGroup.ArabicName, s.SessionDate, s.StartTime, s.EndTime);
    public async Task<object> PreviewSessions(Guid enrollment, SessionTransferWrite b)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); return await InspectSessions(enrollment, b, false);
    }
    public async Task<object> MoveSessions(Guid enrollment, SessionTransferWrite b, string key)
    {
        var pairs = Pairs(b); if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new FacilityRejected("معرف العملية مطلوب.", 422);
        await using var tx = await db.Database.BeginTransactionAsync(); var t = await Authority("players", "Edit"); await Authority("academy", "Edit");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"occurrence-request:{t.AcademyId}:{db.RequestUserId}:{key}"}, 0))");
        var hash = AcademyConfigurationService.Hash(JsonSerializer.Serialize(new { enrollment, pairs }));
        var prior = await db.EnrollmentSessionMoves.FirstOrDefaultAsync(m => m.ActorUserId == db.RequestUserId && m.RequestKey == key);
        if (prior is not null) { if (prior.RequestHash != hash) throw new FacilityRejected("معرف العملية مستخدم لاختيار آخر."); return new { id = prior.BatchId, moved = await db.EnrollmentSessionMoves.CountAsync(m => m.BatchId == prior.BatchId), replayed = true }; }
        var p = await InspectSessions(enrollment, b, true);
        if (p.Fingerprint != b.Fingerprint) throw new FacilityRejected("تغيرت البيانات بعد المعاينة؛ راجع الحصص من جديد.");
        if (p.Conflicts.Length != 0) throw new FacilityRejected(string.Join(" ", p.Conflicts));
        var e = await db.SportEnrollments.SingleAsync(x => x.Id == enrollment); var batch = Guid.NewGuid(); var snapshot = JsonSerializer.Serialize(p);
        foreach (var change in p.Changes) db.EnrollmentSessionMoves.Add(new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SportEnrollmentId = enrollment, BranchId = e.BranchId, SportId = e.SportId, SourceSessionId = change.Source.Id, SourceGroupId = change.Source.GroupId, TargetSessionId = change.Target.Id, TargetGroupId = change.Target.GroupId, BatchId = batch, ActorUserId = db.RequestUserId, RequestKey = key, RequestHash = hash, Snapshot = snapshot, CreatedAtUtc = time.GetUtcNow(), UpdatedAtUtc = time.GetUtcNow() });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return new { id = batch, moved = p.Changes.Length, replayed = false };
    }
    public async Task<object> SessionOptions(Guid enrollment, Guid? targetGroup)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var t = await Authority("players", "Read"); await Authority("academy", "Read");
        var e = await db.SportEnrollments.SingleOrDefaultAsync(x => x.Id == enrollment && x.IsActive) ?? throw new FacilityRejected("التسجيل غير متاح.", 404); await Authority("players", "Read", e.BranchId);
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
        var slots = db.TrainingSessions.Where(s => s.BranchId == e.BranchId && s.SportId == e.SportId && s.SessionDate >= today && s.SessionDate <= today.AddYears(1) && s.Status == TrainingSessionStatus.Scheduled && s.TrainingGroup.IsActive && s.TrainingGroup.RegistrationFormat == e.RegistrationFormat && !db.EnrollmentSessionMoves.Any(m => m.SportEnrollmentId == enrollment && (m.SourceSessionId == s.Id || m.TargetSessionId == s.Id)));
        var original = await slots.Where(s => FoundationDbContext.IsSessionAssigned(t.AcademyId, enrollment, s.Id)).OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime).Take(100).Select(s => new SessionTransferSlot(s.Id, s.TrainingGroupId, s.TrainingGroup.ArabicName, s.SessionDate, s.StartTime, s.EndTime)).ToArrayAsync();
        var alternative = targetGroup.HasValue ? await slots.Where(s => s.TrainingGroupId == targetGroup && !FoundationDbContext.IsSessionAssigned(t.AcademyId, enrollment, s.Id)).OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime).Take(100).Select(s => new SessionTransferSlot(s.Id, s.TrainingGroupId, s.TrainingGroup.ArabicName, s.SessionDate, s.StartTime, s.EndTime)).ToArrayAsync() : [];
        return new { original, alternative, limit = 100 };
    }
}
