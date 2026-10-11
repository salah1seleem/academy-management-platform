using System.Text.Json;
using Academy.Api.Configuration;
using Academy.Api.Facilities;
using Academy.Api.Slice4;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public sealed record GroupCreationWrite(string ArabicName, Guid BranchId, Guid SportId, Guid AgeCategoryId, Guid LevelId, int Capacity, TrainingRegistrationFormat RegistrationFormat, bool CapacityReviewed, Guid CoachMembershipId, TimetableWrite Timetable, Guid[] EnrollmentIds, string? Fingerprint = null);
public sealed record GroupCreationPreview(string Fingerprint, string Name, int Sessions, int Players, string? Resource, string[] Conflicts);

// A single local database transaction reuses timetable and placement invariants.
// Preview rolls the whole transaction back, including provisional group/session IDs.
public sealed class GroupCreationService(FoundationDbContext db, GroupPlacementService placement, ResourceTimetableService timetable, TimeProvider time)
{
    private static string Intent(GroupCreationWrite b) => AcademyConfigurationService.Hash(JsonSerializer.Serialize(b with { ArabicName = b.ArabicName?.Trim() ?? "", Fingerprint = null, Timetable = b.Timetable with { PreviewFingerprint = null }, EnrollmentIds = b.EnrollmentIds.Order().ToArray() }));
    private async Task Validate(GroupCreationWrite b)
    {
        if (string.IsNullOrWhiteSpace(b.ArabicName) || b.ArabicName.Trim().Length > 160 || b.Capacity is < 1 or > 10000 || !Enum.IsDefined(b.RegistrationFormat) || b.EnrollmentIds is null || b.EnrollmentIds.Length > 100 || b.EnrollmentIds.Distinct().Count() != b.EnrollmentIds.Length || b.Timetable is null || b.Timetable.RemoveResource) throw new FacilityRejected("راجع اسم المجموعة والسعة ونمط التسجيل والجدول واللاعبين المختارين.", 422);
        var t = await placement.Authority("academy", "Add", b.BranchId); await placement.Authority("players", "Edit", b.BranchId);
        if (!await db.Branches.AnyAsync(x => x.Id == b.BranchId && x.IsActive) || !await db.Sports.AnyAsync(x => x.Id == b.SportId && x.IsActive) || !await db.AgeCategories.AnyAsync(x => x.Id == b.AgeCategoryId && x.IsActive) || !await db.TrainingLevels.AnyAsync(x => x.Id == b.LevelId && x.SportId == b.SportId && x.IsActive)) throw new FacilityRejected("الفرع أو الرياضة أو العمر أو المستوى غير متاح.", 404);
        if (!await db.AcademyMemberships.AnyAsync(x => x.Id == b.CoachMembershipId && x.Role == AcademyRole.Coach && x.IsActive && x.User.IsActive && (x.AllBranches || db.MembershipBranchAccesses.Any(a => a.MembershipId == x.Id && a.BranchId == b.BranchId && a.IsActive)))) throw new FacilityRejected("اختر مدربًا فعالًا مخولًا لهذا الفرع.", 422);
        if (b.Timetable.Resource is { } resource && (resource.CoachMembershipId != b.CoachMembershipId || resource.Seats < b.Capacity)) throw new FacilityRejected("مدرب حجز المورد يجب أن يطابق المدرب المختار، ومقاعده تغطي سعة المجموعة.", 422);
        if (b.RegistrationFormat != TrainingRegistrationFormat.Group && (!b.CapacityReviewed || b.Timetable.Resource is null)) throw new FacilityRejected("الخاص وشبه الخاص يحتاجان موردًا ومراجعة سعة صريحة.", 422);
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, time);
        if (b.Timetable.From < today) throw new FacilityRejected("جدول مجموعة جديدة يبدأ من اليوم أو المستقبل.", 422);
    }
    private async Task<string> Stamp(GroupCreationWrite b)
    {
        // Only opaque revision metadata is returned. Ignore branch scope solely for
        // academy-wide coach/player collision detection; never ignore tenant scope.
        var sessions = await db.TrainingSessions.IgnoreQueryFilters(["trusted-branches"]).Where(s => s.SessionDate >= b.Timetable.From && s.SessionDate <= b.Timetable.To).OrderBy(s => s.Id).Select(s => new { s.Id, s.Version }).ToArrayAsync();
        var bookings = await db.ResourceBookings.IgnoreQueryFilters(["trusted-branches"]).Where(s => s.Date >= b.Timetable.From && s.Date <= b.Timetable.To).OrderBy(s => s.Id).Select(s => new { s.Id, s.Version }).ToArrayAsync();
        var resourceId = b.Timetable.Resource?.ResourceId ?? Guid.Empty;
        var resource = await db.FacilityResources.AsNoTracking().Where(r => r.Id == resourceId).Select(r => new { r.Id, r.Version }).SingleOrDefaultAsync();
        var closures = await db.ResourceClosures.Where(c => c.ResourceId == resourceId).OrderBy(c => c.Id).Select(c => new { c.Id, c.Version }).ToArrayAsync();
        var assignments = await db.StaffGroupAssignments.IgnoreQueryFilters(["trusted-branches"]).Where(a => a.AcademyMembershipId == b.CoachMembershipId).OrderBy(a => a.Id).Select(a => new { a.Id, a.Version }).ToArrayAsync();
        var coach = await db.AcademyMemberships.Where(m => m.Id == b.CoachMembershipId).Select(m => new { m.UpdatedAtUtc, user = m.User.UpdatedAtUtc }).SingleAsync();
        var branch = await db.Branches.Where(x => x.Id == b.BranchId).Select(x => x.Version).SingleAsync(); var sport = await db.Sports.Where(x => x.Id == b.SportId).Select(x => x.Version).SingleAsync(); var age = await db.AgeCategories.Where(x => x.Id == b.AgeCategoryId).Select(x => x.Version).SingleAsync(); var level = await db.TrainingLevels.Where(x => x.Id == b.LevelId).Select(x => x.Version).SingleAsync();
        var enrollments = await db.SportEnrollments.Where(e => b.EnrollmentIds.Contains(e.Id)).OrderBy(e => e.Id).Select(e => new { e.Id, e.Version, player = e.Player.Version }).ToArrayAsync();
        var periods = await db.SubscriptionPeriods.Where(p => b.EnrollmentIds.Contains(p.SportEnrollmentId)).OrderBy(p => p.Id).Select(p => new { p.Id, p.Version }).ToArrayAsync();
        return AcademyConfigurationService.Hash(JsonSerializer.Serialize(new { intent = Intent(b), sessions, bookings, resource, closures, assignments, coach, branch, sport, age, level, enrollments, periods }));
    }
    public async Task<object> Run(GroupCreationWrite b, bool commit, string? requestKey = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); await Validate(b);
        var academy = db.RequestAcademyId; var actor = db.RequestUserId; var hash = Intent(b); var key = "group-wizard:" + requestKey;
        if (commit)
        {
            if (string.IsNullOrWhiteSpace(requestKey) || requestKey.Length is < 8 or > 70) throw new FacilityRejected("معرف عملية الإنشاء مطلوب.", 422);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"group-wizard:{academy}:{actor}:{key}"}, 0))");
            var prior = await db.GroupPlacementRevisions.SingleOrDefaultAsync(r => r.ActorUserId == actor && r.RequestKey == key);
            if (prior is not null) { if (prior.RequestHash != hash) throw new FacilityRejected("معرف العملية استُخدم لإنشاء آخر."); return new { id = prior.TrainingGroupId, replayed = true }; }
        }
        var fingerprint = await Stamp(b);
        if (commit && fingerprint != b.Fingerprint) throw new FacilityRejected("تغير الجدول أو الأهلية منذ المعاينة؛ أعد المراجعة قبل الإنشاء.");
        var now = time.GetUtcNow(); var group = new TrainingGroup { Id = Guid.NewGuid(), AcademyId = academy, ArabicName = b.ArabicName.Trim(), BranchId = b.BranchId, SportId = b.SportId, AgeCategoryId = b.AgeCategoryId, Capacity = b.Capacity, TrainingLevelId = b.LevelId, RegistrationFormat = b.RegistrationFormat, FormatReviewedAtUtc = b.CapacityReviewed ? now : null, FormatReviewedByUserId = b.CapacityReviewed ? actor : null, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.TrainingGroups.Add(group); db.StaffGroupAssignments.Add(new() { Id = Guid.NewGuid(), AcademyId = academy, TrainingGroupId = group.Id, AcademyMembershipId = b.CoachMembershipId, CreatedAtUtc = now, UpdatedAtUtc = now }); await db.SaveChangesAsync();
        var schedule = await timetable.PreviewInTransaction(group.Id, b.Timetable);
        var conflicts = schedule.Conflicts.Select(c => c.Message).Distinct().ToArray();
        if (conflicts.Length == 0)
        {
            await timetable.SaveInTransaction(group.Id, b.Timetable with { PreviewFingerprint = schedule.Fingerprint }, "wizard-schedule-" + group.Id.ToString("N"));
            if (b.EnrollmentIds.Length > 0)
            {
                var review = await placement.PreviewInTransaction(group.Id, new(b.EnrollmentIds)); conflicts = review.Conflicts.Concat(review.Players.SelectMany(p => p.Reasons)).Distinct().ToArray();
                if (conflicts.Length == 0) await placement.SaveInTransaction(group.Id, new(b.EnrollmentIds, review.Fingerprint), "wizard-members-" + group.Id.ToString("N"));
            }
        }
        var preview = new GroupCreationPreview(fingerprint, group.ArabicName, schedule.ProposedSessions, b.EnrollmentIds.Length, schedule.Resource, conflicts);
        if (!commit) { await tx.RollbackAsync(); return preview; }
        if (conflicts.Length > 0) throw new FacilityRejected(string.Join(" ", conflicts));
        db.GroupPlacementRevisions.Add(new() { Id = Guid.NewGuid(), AcademyId = academy, BranchId = group.BranchId, TrainingGroupId = group.Id, ActorUserId = actor, RequestKey = key, RequestHash = hash, Snapshot = JsonSerializer.Serialize(new { request = b, review = preview, financialEffect = false, attendanceCreated = false }), EffectiveDate = b.Timetable.From, Placed = b.EnrollmentIds.Length, CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return new { group.Id, sessions = schedule.ProposedSessions, players = b.EnrollmentIds.Length, replayed = false };
    }
    public async Task<object> Candidates(Guid branchId, Guid sportId, string? search, int offset)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); await placement.Authority("academy", "Read", branchId); await placement.Authority("players", "Read", branchId);
        if (offset < 0 || offset > 100000 || search?.Length > 100) throw new FacilityRejected("البحث خارج الحد المسموح.", 422);
        var q = db.SportEnrollments.Where(e => e.BranchId == branchId && e.SportId == sportId && e.IsActive && e.Player.IsActive && e.TrainingGroupId == null);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(e => e.Player.ArabicName.Contains(search.Trim()) || e.Player.PlayerCode.Contains(search.Trim()));
        return new { total = await q.CountAsync(), offset, players = await q.OrderBy(e => e.Player.ArabicName).ThenBy(e => e.Id).Skip(offset).Take(100).Select(e => new { e.Id, name = e.Player.ArabicName, code = e.Player.PlayerCode, e.TrainingLevelId, registrationFormat = e.RegistrationFormat.ToString() }).ToArrayAsync() };
    }
}
