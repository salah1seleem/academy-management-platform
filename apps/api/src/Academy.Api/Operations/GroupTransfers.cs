using System.Text.Json;
using Academy.Api.Configuration;
using Academy.Api.Facilities;
using Academy.Api.Slice4;
using Academy.Infrastructure.People;
using Microsoft.EntityFrameworkCore;
using Academy.Infrastructure.Persistence;

namespace Academy.Api.Operations;
public sealed record GroupTransferWrite(Guid GroupId, DateOnly EffectiveDate, string? Fingerprint = null);
public sealed record GroupTransferPreview(string Fingerprint, DateOnly EffectiveDate, Guid SourceGroupId, string SourceGroup, Guid TargetGroupId, string TargetGroup,
    PlacementPreview Eligibility, object[] RemovedSessions, object[] AddedSessions, string[] Conflicts,
    string Effect = "نقل المواعيد فقط؛ لا تحصيل أو خصم رصيد أو حضور جديد. أجور الحصص لا تتغير دون تنفيذ واعتماد العمل.");

public sealed partial class GroupPlacementService
{
    private async Task LockTransferPlayer(Guid academy, Guid id)
    {
        var e = await db.SportEnrollments.FromSqlInterpolated($"SELECT *, xmin FROM \"SportEnrollments\" WHERE \"AcademyId\"={academy} AND \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync() ?? throw new FacilityRejected("التسجيل غير متاح.", 404);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Players\" WHERE \"AcademyId\"={academy} AND \"Id\"={e.PlayerId} FOR UPDATE");
        _ = await db.SubscriptionPeriods.FromSqlInterpolated($"SELECT *, xmin FROM \"SubscriptionPeriods\" WHERE \"AcademyId\"={academy} AND \"SportEnrollmentId\"={id} ORDER BY \"Id\" FOR UPDATE").AsNoTracking().ToArrayAsync();
    }
    private async Task<GroupTransferPreview> InspectTransfer(Guid id, GroupTransferWrite b, bool write)
    {
        var t = await Authority("players", write ? "Edit" : "Read"); await Authority("academy", write ? "Edit" : "Read");
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
        if (b.EffectiveDate < today || b.EffectiveDate > today.AddYears(1)) throw new FacilityRejected("اختر تاريخ اليوم أو تاريخًا خلال سنة للنقل؛ لا نغير الماضي.", 422);
        var source = await db.SportEnrollments.Where(e => e.Id == id && e.IsActive).Select(e => new { e.BranchId, group = FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, b.EffectiveDate) }).SingleOrDefaultAsync() ?? throw new FacilityRejected("التسجيل غير متاح.", 404);
        await Authority("players", write ? "Edit" : "Read", source.BranchId);
        if (source.group is not Guid sourceId) throw new FacilityRejected("التسجيل ينتظر التسكين؛ استخدم إضافة أعضاء للمجموعة.");
        if (sourceId == b.GroupId) throw new FacilityRejected("اختر مجموعة مختلفة للنقل.", 422);
        if (write)
        {
            await coordinator.LockCalendar(t.AcademyId, [sourceId, b.GroupId], "Edit");
            await LockTransferPlayer(t.AcademyId, id);
            var refreshedSource = await db.SportEnrollments.Where(e => e.Id == id).Select(e => FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, b.EffectiveDate)).SingleAsync();
            if (refreshedSource != sourceId) throw new FacilityRejected("تغيرت المجموعة بعد المعاينة؛ راجع النقل من جديد.");
        }
        var (_, target) = await Group(b.GroupId, write);
        var eligibility = await Inspect(target, [id], effectiveDate: b.EffectiveDate, transfer: true);
        var conflicts = new List<string>(eligibility.Conflicts.Concat(eligibility.Players.SelectMany(p => p.Reasons)));
        var changes = await db.EnrollmentGroupTransfers.AsNoTracking().Where(x => x.SportEnrollmentId == id).OrderBy(x => x.EffectiveDate).Select(x => new { x.Id, x.EffectiveDate, x.TargetGroupId }).ToArrayAsync();
        if (changes.Any(x => x.EffectiveDate >= b.EffectiveDate)) conflicts.Add("يوجد نقل مجدول في هذا التاريخ أو بعده؛ لا نستبدل قرارًا مؤرخًا بصمت.");
        if (await db.PlayerAttendances.AnyAsync(a => a.SportEnrollmentId == id && a.TrainingSession.SessionDate >= b.EffectiveDate)) conflicts.Add("يوجد سجل حضور في الفترة؛ اختر تاريخًا بعد آخر حضور لحفظ التاريخ.");
        var slots = await db.TrainingSessions.AsNoTracking().Where(s => (s.TrainingGroupId == sourceId || s.TrainingGroupId == b.GroupId) && s.SessionDate >= b.EffectiveDate && s.Status == Academy.Infrastructure.Attendance.TrainingSessionStatus.Scheduled)
            .OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime).Select(s => new { s.Id, s.TrainingGroupId, s.SessionDate, s.StartTime, s.EndTime, s.Version }).Take(1001).ToArrayAsync();
        if (slots.Length > 1000) conflicts.Add("عدد المواعيد يتجاوز حد المعاينة؛ قلل نطاق الجدول أولًا.");
        var sourceName = await db.TrainingGroups.Where(g => g.Id == sourceId).Select(g => g.ArabicName).SingleAsync();
        var fingerprint = AcademyConfigurationService.Hash(JsonSerializer.Serialize(new { id, b.GroupId, b.EffectiveDate, sourceId, eligibility.Fingerprint, changes, slots, conflicts }));
        return new(fingerprint, b.EffectiveDate, sourceId, sourceName, b.GroupId, target.ArabicName, eligibility, slots.Where(x => x.TrainingGroupId == sourceId).Cast<object>().ToArray(), slots.Where(x => x.TrainingGroupId == b.GroupId).Cast<object>().ToArray(), conflicts.Distinct().ToArray());
    }
    public async Task<object> PreviewTransfer(Guid id, GroupTransferWrite b)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); return await InspectTransfer(id, b, false);
    }
    public async Task<object> Transfer(Guid id, GroupTransferWrite b, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new FacilityRejected("معرف العملية مطلوب.", 422);
        await using var tx = await db.Database.BeginTransactionAsync(); var t = await Authority("players", "Edit"); await Authority("academy", "Edit");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"transfer-request:{t.AcademyId}:{db.RequestUserId}:{key}"}, 0))");
        var hash = AcademyConfigurationService.Hash(JsonSerializer.Serialize(new { id, b.GroupId, b.EffectiveDate }));
        var prior = await db.EnrollmentGroupTransfers.SingleOrDefaultAsync(x => x.ActorUserId == db.RequestUserId && x.RequestKey == key);
        if (prior is not null) { if (prior.RequestHash != hash) throw new FacilityRejected("معرف العملية مستخدم لطلب آخر."); return new { prior.Id, prior.EffectiveDate, replayed = true }; }
        var preview = await InspectTransfer(id, b, true);
        if (b.Fingerprint != preview.Fingerprint) throw new FacilityRejected("تغيرت البيانات بعد المعاينة؛ راجع النقل من جديد.");
        if (preview.Conflicts.Length > 0) throw new FacilityRejected(string.Join(" ", preview.Conflicts));
        var e = await db.SportEnrollments.SingleAsync(x => x.Id == id);
        var row = new EnrollmentGroupTransfer { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SportEnrollmentId = id, BranchId = e.BranchId, SportId = e.SportId, SourceGroupId = preview.SourceGroupId, TargetGroupId = b.GroupId, EffectiveDate = b.EffectiveDate, ActorUserId = db.RequestUserId, RequestKey = key, RequestHash = hash, Snapshot = JsonSerializer.Serialize(preview), CreatedAtUtc = time.GetUtcNow(), UpdatedAtUtc = time.GetUtcNow() };
        db.EnrollmentGroupTransfers.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return new { row.Id, row.EffectiveDate, replayed = false };
    }
}
