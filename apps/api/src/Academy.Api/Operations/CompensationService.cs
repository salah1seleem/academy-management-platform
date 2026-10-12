using System.Text.Json;
using Academy.Api.Configuration;
using Academy.Api.Facilities;
using Academy.Api.Slice3;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Academy.Infrastructure.Tenancy;

namespace Academy.Api.Operations;
public sealed record CompensationGrantWrite(Guid SourceSessionId, CompensationCause Cause, CompensationUnit Unit, DateOnly ValidFrom, DateOnly ValidTo, string? Fingerprint = null);
public sealed record CompensationGrantPreview(string Fingerprint, int Units, string[] Conflicts);
public sealed record CompensationBookWrite(Guid TrainingSessionId, string? Fingerprint = null);
public sealed record CompensationBookPreview(string Fingerprint, SessionTransferSlot Target, int Units, int Available, int ExpectedPlayers, int? Capacity, string[] Conflicts);
public sealed record CompensationCancelWrite(uint Version);

public sealed partial class GroupPlacementService
{
    private async Task LockCompensation(Guid academy, Guid enrollment, TrainingSession[] slots)
    {
        await coordinator.LockCalendar(academy, slots.Select(s => s.TrainingGroupId), "Edit");
        foreach (var id in slots.Select(s => s.Id).Distinct().Order()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"attendance:{academy:N}:{id:N}:{Guid.Empty:N}"}, 0))");
        await LockTransferPlayer(academy, enrollment);
    }
    private static void Key(string key) { if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new FacilityRejected("معرف العملية مطلوب.", 422); }
    private async Task<CompensationGrantPreview> InspectCompensation(Guid enrollment, CompensationGrantWrite b, bool write)
    {
        var t = await Authority("players", write ? "Edit" : "Read"); await Authority("academy", write ? "Edit" : "Read");
        var e = await db.SportEnrollments.AsNoTracking().SingleOrDefaultAsync(e => e.Id == enrollment && e.IsActive && e.Player.IsActive) ?? throw new FacilityRejected("التسجيل غير متاح.", 404); await Authority("players", write ? "Edit" : "Read", e.BranchId);
        var s = await db.TrainingSessions.AsNoTracking().Include(s => s.TrainingGroup).SingleOrDefaultAsync(s => s.Id == b.SourceSessionId && s.BranchId == e.BranchId && s.SportId == e.SportId) ?? throw new FacilityRejected("الحصة الأصلية غير متاحة.", 404);
        if (write) { await LockCompensation(t.AcademyId, enrollment, [s]); s = await db.TrainingSessions.AsNoTracking().Include(s => s.TrainingGroup).SingleAsync(s => s.Id == b.SourceSessionId); }
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
        if (!Enum.IsDefined(b.Cause) || !Enum.IsDefined(b.Unit) || !CompensationLimits.Validity(today, b.ValidFrom, b.ValidTo)) throw new FacilityRejected("اختر سببًا ووحدة وصلاحية من اليوم ولمدة لا تتجاوز90 يومًا.", 422);
        var conflicts = new List<string>();
        if (s.SessionDate > today) conflicts.Add("اعتماد التعويض بعد واقعة اليوم أو الماضي فقط.");
        if (!await db.SportEnrollments.Where(e => e.Id == enrollment).Select(e => FoundationDbContext.IsSessionAssigned(e.AcademyId, e.Id, s.Id)).SingleAsync()) conflicts.Add("الحصة ليست ضمن مواعيد هذا التسجيل.");
        var attendance = await db.PlayerAttendances.AsNoTracking().SingleOrDefaultAsync(a => a.SportEnrollmentId == enrollment && a.TrainingSessionId == s.Id);
        if (attendance is not null && AttendanceRules.Attended(attendance.Status) || attendance?.ConsumedSubscriptionPeriodId is not null) conflicts.Add("الحصة لها حضور أو استهلاك قائم؛ صحح السجل المعني أولًا.");
        var staff = await db.StaffAttendances.AsNoTracking().Where(a => a.TrainingSessionId == s.Id).OrderBy(a => a.Id).Select(a => new { a.Id, a.AcademyMembershipId, a.Status, a.Version }).ToArrayAsync();
        var actualCoaches = b.Cause == CompensationCause.CoachAbsent ? await db.AcademyMemberships.Where(m=>m.Role==AcademyRole.Coach&&FoundationDbContext.IsSessionStaffAssigned(s.AcademyId,m.Id,s.Id)).Select(m=>m.Id).ToArrayAsync() : [];
        if (b.Cause == CompensationCause.CancelledSession && s.Status != TrainingSessionStatus.Cancelled) conflicts.Add("سبب الإلغاء يحتاج حصة ملغاة فعليًا، وليس غياب طفل.");
        if (b.Cause == CompensationCause.CoachAbsent && ((!staff.Any(a => a.Status == AttendanceStatus.Absent) || staff.Any(a => AttendanceRules.Attended(a.Status))) || s.Status != TrainingSessionStatus.Cancelled && (actualCoaches.Length==0 || actualCoaches.Any(m=>!staff.Any(a=>a.AcademyMembershipId==m&&a.Status==AttendanceStatus.Absent))))) conflicts.Add("راجع غياب المدربين الفعليين؛ لا تمنح تعويضًا من غياب الأصلي مع بديل حاضر أو غير مسجل.");
        if (b.Cause == CompensationCause.ApprovedAbsence && attendance?.Status is not (AttendanceStatus.Absent or AttendanceStatus.Excused)) conflicts.Add("التعويض عن غياب معتمد يحتاج واقعة غياب أو اعتذار مسجلة للطفل.");
        var periods = await db.SubscriptionPeriods.AsNoTracking().Include(p => p.SubscriptionPlan).Where(p => p.SportEnrollmentId == enrollment && p.IsActive && p.Status != SubscriptionPeriodStatus.Cancelled && p.Status != SubscriptionPeriodStatus.Frozen && p.StartDate <= s.SessionDate && (p.EndDate == null || p.EndDate >= s.SessionDate)).OrderBy(p => p.StartDate).ThenBy(p => p.Id).ToArrayAsync();
        var period = periods.FirstOrDefault(p => (p.ServiceKindSnapshot ?? p.SubscriptionPlan.ServiceKind) == AcademyServiceKind.Training && PlanCatalog.FormatMatches(e.RegistrationFormat, p.TrainingFormatSnapshot ?? p.SubscriptionPlan.TrainingFormat));
        if (period is null) conflicts.Add("لا توجد فترة تدريب مؤهلة للحصة الأصلية.");
        if (await db.CompensationGrants.AnyAsync(g => g.SportEnrollmentId == enrollment && g.SourceSessionId == s.Id)) conflicts.Add("صدر تعويض لهذه الواقعة بالفعل؛ لا نكرر الاستحقاق.");
        var units = CompensationLimits.Units(b.Unit, s.StartTime, s.EndTime);
        if (units <= 0) conflicts.Add("مدة الحصة الأصلية غير صالحة.");
        var fingerprint = AcademyConfigurationService.Hash(JsonSerializer.Serialize(new { enrollment, b.SourceSessionId, b.Cause, b.Unit, b.ValidFrom, b.ValidTo, units, today, e.Version, session = s.Version, attendance = attendance?.Version, staff, period = period?.Id, periodVersion = period?.Version, conflicts }));
        return new(fingerprint, units, conflicts.ToArray());
    }
    public async Task<object> PreviewCompensation(Guid enrollment, CompensationGrantWrite b) { await using var tx = await db.Database.BeginTransactionAsync(); return await InspectCompensation(enrollment, b, false); }
    public async Task<object> GrantCompensation(Guid enrollment, CompensationGrantWrite b, string key)
    {
        Key(key); await using var tx = await db.Database.BeginTransactionAsync(); var t = await Authority("players", "Edit"); await Authority("academy", "Edit");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"compensation-grant:{t.AcademyId}:{db.RequestUserId}:{key}"}, 0))");
        var hash = AcademyConfigurationService.Hash(JsonSerializer.Serialize(new { enrollment, b.SourceSessionId, b.Cause, b.Unit, b.ValidFrom, b.ValidTo }));
        var prior = await db.CompensationGrants.SingleOrDefaultAsync(g => g.ActorUserId == db.RequestUserId && g.RequestKey == key);
        if (prior is not null) { if (prior.RequestHash != hash) throw new FacilityRejected("معرف العملية مستخدم لطلب آخر."); return new { prior.Id, replayed = true }; }
        var p = await InspectCompensation(enrollment, b, true); if (p.Fingerprint != b.Fingerprint) throw new FacilityRejected("تغيرت الواقعة؛ أعد المعاينة."); if (p.Conflicts.Length != 0) throw new FacilityRejected(string.Join(" ", p.Conflicts));
        var e = await db.SportEnrollments.SingleAsync(e => e.Id == enrollment); var s = await db.TrainingSessions.SingleAsync(s => s.Id == b.SourceSessionId);
        var periods = await db.SubscriptionPeriods.Include(p => p.SubscriptionPlan).Where(p => p.SportEnrollmentId == enrollment && p.IsActive && p.Status != SubscriptionPeriodStatus.Cancelled && p.Status != SubscriptionPeriodStatus.Frozen && p.StartDate <= s.SessionDate && (p.EndDate == null || p.EndDate >= s.SessionDate)).OrderBy(p => p.StartDate).ThenBy(p => p.Id).ToArrayAsync();
        var period = periods.First(p => (p.ServiceKindSnapshot ?? p.SubscriptionPlan.ServiceKind) == AcademyServiceKind.Training && PlanCatalog.FormatMatches(e.RegistrationFormat, p.TrainingFormatSnapshot ?? p.SubscriptionPlan.TrainingFormat));
        var row = new CompensationGrant { Id = Guid.NewGuid(), AcademyId = t.AcademyId, BranchId = e.BranchId, SportId = e.SportId, SportEnrollmentId = e.Id, SourceSessionId = s.Id, SourceGroupId = s.TrainingGroupId, SourcePeriodId = period.Id, Cause = b.Cause, Unit = b.Unit, Units = p.Units, ValidFrom = b.ValidFrom, ValidTo = b.ValidTo, ActorUserId = db.RequestUserId, RequestKey = key, RequestHash = hash, Snapshot = JsonSerializer.Serialize(p), CreatedAtUtc = time.GetUtcNow(), UpdatedAtUtc = time.GetUtcNow() };
        db.CompensationGrants.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return new { row.Id, replayed = false };
    }
    private async Task<CompensationBookPreview> InspectCompensationBooking(Guid grantId, CompensationBookWrite b, bool write)
    {
        var t = await Authority("players", write ? "Edit" : "Read"); await Authority("academy", write ? "Edit" : "Read");
        var grant = await db.CompensationGrants.AsNoTracking().SingleOrDefaultAsync(g => g.Id == grantId) ?? throw new FacilityRejected("التعويض غير متاح.", 404); await Authority("players", write ? "Edit" : "Read", grant.BranchId);
        var target = await db.TrainingSessions.AsNoTracking().Include(s => s.TrainingGroup).SingleOrDefaultAsync(s => s.Id == b.TrainingSessionId && s.BranchId == grant.BranchId && s.SportId == grant.SportId) ?? throw new FacilityRejected("الحصة البديلة غير متاحة.", 404);
        if (write)
        {
            var source = await db.TrainingSessions.AsNoTracking().SingleAsync(s => s.Id == grant.SourceSessionId); await LockCompensation(t.AcademyId, grant.SportEnrollmentId, [source, target]);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"CompensationGrants\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\"={grantId} FOR UPDATE");
            target = await db.TrainingSessions.AsNoTracking().Include(s => s.TrainingGroup).SingleAsync(s => s.Id == b.TrainingSessionId);
        }
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, time); var conflicts = new List<string>();
        if (target.Id == grant.SourceSessionId || target.SessionDate < today || target.SessionDate < grant.ValidFrom || target.SessionDate > grant.ValidTo || target.Status != TrainingSessionStatus.Scheduled) conflicts.Add("اختر حصة قادمة مختلفة داخل صلاحية التعويض.");
        if (await db.PlayerAttendances.AnyAsync(a => a.SportEnrollmentId == grant.SportEnrollmentId && a.TrainingSessionId == target.Id)) conflicts.Add("الحصة لها سجل حضور؛ لا نستبدل أساسه بتعويض بعد التسجيل.");
        if (await db.EnrollmentSessionMoves.AnyAsync(m => m.SportEnrollmentId == grant.SportEnrollmentId && (m.SourceSessionId == target.Id || m.TargetSessionId == target.Id))) conflicts.Add("الحصة مرتبطة بنقل مختار؛ لا نجمع قرارين عليها.");
        var bookings = await db.CompensationBookings.AsNoTracking().Where(b => b.GrantId == grantId).OrderBy(b => b.Id).ToArrayAsync();
        var available = grant.Units - bookings.Where(b => b.State == CompensationBookingState.Reserved).Sum(b => b.Units);
        var units = CompensationLimits.Units(grant.Unit, target.StartTime, target.EndTime);
        if (!CompensationLimits.CanAllocate(grant.Units, grant.Units - available, units)) conflicts.Add("رصيد التعويض المتاح لا يغطي مدة الحصة المختارة.");
        if (await db.CompensationBookings.AnyAsync(b => b.SportEnrollmentId == grant.SportEnrollmentId && b.TrainingSessionId == target.Id && b.State == CompensationBookingState.Reserved)) conflicts.Add("يوجد تعويض محجوز للاعب في هذه الحصة.");
        var (_, group) = await Group(target.TrainingGroupId, false); var review = await Inspect(group, [grant.SportEnrollmentId], effectiveDate: target.SessionDate, transfer: true, occurrence: target.Id, compensation: true);
        conflicts.AddRange(review.Conflicts); conflicts.AddRange(review.Players.SelectMany(p => p.Reasons));
        var player = await db.SportEnrollments.Where(e => e.Id == grant.SportEnrollmentId).Select(e => e.PlayerId).SingleAsync(); var enrollments = db.SportEnrollments.IgnoreQueryFilters(["trusted-branches"]);
        if (await db.TrainingSessions.IgnoreQueryFilters(["trusted-branches"]).AnyAsync(s => s.AcademyId == t.AcademyId && s.Id != target.Id && s.Status != TrainingSessionStatus.Cancelled && s.SessionDate == target.SessionDate && s.StartTime < target.EndTime && target.StartTime < s.EndTime && enrollments.Any(e => e.AcademyId == t.AcademyId && e.PlayerId == player && e.IsActive && FoundationDbContext.IsSessionAssigned(e.AcademyId, e.Id, s.Id)))) conflicts.Add("الحصة تتعارض مع موعد آخر للاعب.");
        var fingerprint = AcademyConfigurationService.Hash(JsonSerializer.Serialize(new { grantId, b.TrainingSessionId, today, target.Version, available, units, bookings = bookings.Select(b => new { b.Id, b.Version }), review.Fingerprint, conflicts }));
        return new(fingerprint, Slot(target), units, available, review.CurrentPlayers + 1, group.Capacity, conflicts.Distinct().ToArray());
    }
    public async Task<object> PreviewCompensationBooking(Guid grant, CompensationBookWrite b) { await using var tx = await db.Database.BeginTransactionAsync(); return await InspectCompensationBooking(grant, b, false); }
    public async Task<object> BookCompensation(Guid grantId, CompensationBookWrite b, string key)
    {
        Key(key); await using var tx = await db.Database.BeginTransactionAsync(); var t = await Authority("players", "Edit"); await Authority("academy", "Edit");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"compensation-book:{t.AcademyId}:{db.RequestUserId}:{key}"}, 0))");
        var hash = AcademyConfigurationService.Hash(JsonSerializer.Serialize(new { grantId, b.TrainingSessionId })); var prior = await db.CompensationBookings.SingleOrDefaultAsync(b => b.ActorUserId == db.RequestUserId && b.RequestKey == key);
        if (prior is not null) { if (prior.RequestHash != hash) throw new FacilityRejected("معرف العملية مستخدم لطلب آخر."); return new { prior.Id, replayed = true }; }
        var p = await InspectCompensationBooking(grantId, b, true); if (p.Fingerprint != b.Fingerprint) throw new FacilityRejected("تغيرت الحصة أو الرصيد؛ أعد المعاينة."); if (p.Conflicts.Length > 0) throw new FacilityRejected(string.Join(" ", p.Conflicts));
        var g = await db.CompensationGrants.SingleAsync(g => g.Id == grantId); var row = new CompensationBooking { Id = Guid.NewGuid(), AcademyId = t.AcademyId, GrantId = grantId, SportEnrollmentId = g.SportEnrollmentId, SportId = g.SportId, BranchId = g.BranchId, TrainingGroupId = p.Target.GroupId, TrainingSessionId = p.Target.Id, Units = p.Units, ActorUserId = db.RequestUserId, RequestKey = key, RequestHash = hash, Snapshot = JsonSerializer.Serialize(p), CreatedAtUtc = time.GetUtcNow(), UpdatedAtUtc = time.GetUtcNow() };
        db.CompensationBookings.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return new { row.Id, replayed = false };
    }
    public async Task<object> CancelCompensationBooking(Guid id, CompensationCancelWrite b)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var t = await Authority("players", "Edit"); await Authority("academy", "Edit");
        var row = await db.CompensationBookings.SingleOrDefaultAsync(b => b.Id == id) ?? throw new FacilityRejected("الحجز غير متاح.", 404); await Authority("players", "Edit", row.BranchId);
        var slot = await db.TrainingSessions.AsNoTracking().SingleAsync(s => s.Id == row.TrainingSessionId); await LockCompensation(t.AcademyId, row.SportEnrollmentId, [slot]); await db.Entry(row).ReloadAsync();
        if (row.State == CompensationBookingState.Cancelled) return new { row.Id, replayed = true };
        if (row.Version != b.Version) throw new FacilityRejected("تغير الحجز؛ أعد تحميل الملف.");
        if (await db.CompensationMovements.Where(m => m.BookingId == id).SumAsync(m => (int?)m.Units) is > 0 || await db.PlayerAttendances.AnyAsync(a => a.TrainingSessionId == row.TrainingSessionId && a.SportEnrollmentId == row.SportEnrollmentId && (a.Status == AttendanceStatus.Present || a.Status == AttendanceStatus.Late))) throw new FacilityRejected("الحصة لها حضور قائم؛ صحح الحضور قبل تحرير الحجز.");
        row.State = CompensationBookingState.Cancelled; row.CancelledByUserId = db.RequestUserId; row.CancelledAtUtc = time.GetUtcNow(); row.UpdatedAtUtc = time.GetUtcNow(); await db.SaveChangesAsync(); await tx.CommitAsync(); return new { row.Id, replayed = false };
    }
    public async Task<object> CompensationOptions(Guid enrollment)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); await Authority("players", "Read"); await Authority("academy", "Read");
        var e = await db.SportEnrollments.SingleOrDefaultAsync(e => e.Id == enrollment && e.IsActive) ?? throw new FacilityRejected("التسجيل غير متاح.", 404); var today = await AttendanceCalendar.TodayAsync(e.AcademyId, db, time);
        var sessions = db.TrainingSessions.Where(s => s.BranchId == e.BranchId && s.SportId == e.SportId && s.TrainingGroup.RegistrationFormat == e.RegistrationFormat);
        var sources = await sessions.Where(s => s.SessionDate <= today && s.SessionDate >= today.AddYears(-1) && FoundationDbContext.IsSessionAssigned(e.AcademyId, enrollment, s.Id) && !db.CompensationGrants.Any(g => g.SportEnrollmentId == enrollment && g.SourceSessionId == s.Id)).OrderByDescending(s => s.SessionDate).Take(100).Select(s => new { s.Id, date = s.SessionDate, group = s.TrainingGroup.ArabicName, status = s.Status.ToString() }).ToArrayAsync();
        var targets = await sessions.Where(s => s.SessionDate >= today && s.SessionDate <= today.AddDays(180) && s.Status == TrainingSessionStatus.Scheduled && s.TrainingGroup.IsActive).OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime).Take(100).Select(s => new SessionTransferSlot(s.Id, s.TrainingGroupId, s.TrainingGroup.ArabicName, s.SessionDate, s.StartTime, s.EndTime)).ToArrayAsync();
        return new { today, sources, targets, maximumValidityDays = 90, maximumSources = 100 };
    }
}
