using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Facilities;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public sealed record PlacementWrite(Guid[] EnrollmentIds, string? Fingerprint = null);
public sealed record PlacementCandidate(Guid Id, Guid PlayerId, string Player, string PlayerCode, Guid? LevelId, string? Level, string[] Reasons);
public sealed record PlacementPreview(string Fingerprint, DateOnly EffectiveDate, int CurrentPlayers, int? Capacity, int Selected, string[] Conflicts, PlacementCandidate[] Players);
public sealed record PlacementLevelWrite(Guid? LevelId);
public sealed record TrainingLevelWrite(Guid SportId, string ArabicName, int Rank);

public sealed partial class GroupPlacementService(FoundationDbContext db, CurrentTenant tenant, ResourceSessionCoordinator coordinator, TimeProvider time)
{
    internal async Task<TenantMembership> Authority(string module, string action, Guid? branch = null)
    {
        var t = await tenant.ResolveAsync() ?? throw new FacilityRejected("يلزم تسجيل الدخول.", 401);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"}, 0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + t.AcademyId}, 0))");
        var member = await db.AcademyMemberships.AsNoTracking().SingleOrDefaultAsync(x => x.Id == t.MembershipId && x.IsActive && x.User.IsActive && x.Academy.IsActive);
        var config = await db.AcademyConfigurationVersions.AsNoTracking().Where(x => x.State == ConfigurationState.Active).Select(x => x.Payload).SingleOrDefaultAsync();
        if (member is null || member.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin) || config is null || !AcademyRuntimeSettings.Read(config).Features.GetValueOrDefault("training") || !await db.AcademyFeatureEntitlements.AnyAsync(x => x.Feature == "training" && x.IsActive && x.Enabled)) throw new FacilityRejected("التسكين غير متاح بصلاحياتك وإعدادات الأكاديمية الحالية.", 403);
        if (member.Role == AcademyRole.AcademyAdmin)
        {
            var grant = await db.StaffActionGrants.AsNoTracking().SingleOrDefaultAsync(x => x.MembershipId == t.MembershipId && x.Module == module && x.IsActive);
            var permission = module == "academy" ? AcademyPermissions.StructureManage : AcademyPermissions.PeopleManage;
            if (member.UseActionGrants ? grant?.Read != true || action == "Add" && !grant.Add || action == "Edit" && !grant.Edit : await db.StaffGrants.AnyAsync(x => x.MembershipId == member.Id && x.Permission == permission && x.IsActive && !x.Allowed)) throw new FacilityRejected("صلاحية هذا الإجراء غير متاحة حاليًا.", 403);
            if (branch.HasValue && !member.AllBranches && !await db.MembershipBranchAccesses.AnyAsync(x => x.MembershipId == member.Id && x.BranchId == branch && x.IsActive)) throw new FacilityRejected("الفرع غير متاح.", 404);
        }
        return t;
    }
    private async Task<(TenantMembership T, TrainingGroup G)> Group(Guid id, bool write)
    {
        var t = await Authority("academy", write ? "Edit" : "Read");
        await Authority("players", write ? "Edit" : "Read");
        if (write) await coordinator.LockCalendar(t.AcademyId, [id], "Edit");
        var g = write ? await db.TrainingGroups.FromSqlInterpolated($"SELECT *, xmin FROM \"TrainingGroups\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\"={id} FOR UPDATE").Include(x => x.AgeCategory).SingleOrDefaultAsync()
            : await db.TrainingGroups.AsNoTracking().Include(x => x.AgeCategory).SingleOrDefaultAsync(x => x.Id == id);
        if (g is null || !g.IsActive || !g.AgeCategory.IsActive || !await db.Branches.AnyAsync(x => x.Id == g.BranchId && x.IsActive) || !await db.Sports.AnyAsync(x => x.Id == g.SportId && x.IsActive)) throw new FacilityRejected("المجموعة غير متاحة.", 404);
        await Authority("academy", write ? "Edit" : "Read", g.BranchId);
        return (t, g);
    }
    private static Guid[] Ids(PlacementWrite b)
    {
        if (b.EnrollmentIds is null || b.EnrollmentIds.Length is < 1 or > 100 || b.EnrollmentIds.Any(x => x == Guid.Empty) || b.EnrollmentIds.Distinct().Count() != b.EnrollmentIds.Length) throw new FacilityRejected("اختر من لاعب واحد إلى 100 تسجيل مختلف.", 422);
        return b.EnrollmentIds.OrderBy(x => x).ToArray();
    }
    private async Task<PlacementPreview> Inspect(TrainingGroup g, Guid[] ids, bool assessBatch = true)
    {
        var today = await AttendanceCalendar.TodayAsync(g.AcademyId, db, time);
        var rows = await db.SportEnrollments.AsNoTracking().Include(x => x.Player).Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync();
        if (rows.Count != ids.Length) throw new FacilityRejected("أحد التسجيلات غير متاح في نطاقك.", 404);
        var roster = await db.SportEnrollments.AsNoTracking().Where(x => x.TrainingGroupId == g.Id && x.IsActive).OrderBy(x => x.Id).Select(x => new { x.Id, x.Version }).ToArrayAsync();
        var level = g.TrainingLevelId.HasValue ? await db.TrainingLevels.AsNoTracking().SingleOrDefaultAsync(x => x.Id == g.TrainingLevelId && x.IsActive && x.SportId == g.SportId) : null;
        var levels = await db.TrainingLevels.AsNoTracking().Where(x => x.SportId == g.SportId && x.IsActive).ToDictionaryAsync(x => x.Id);
        var coaches = await db.StaffGroupAssignments.AsNoTracking().Where(x => x.TrainingGroupId == g.Id && x.IsActive && x.AcademyMembership.IsActive && x.AcademyMembership.User.IsActive).OrderBy(x => x.Id).Select(x => new { x.Id, x.AcademyMembershipId, x.Version, memberUpdated = x.AcademyMembership.UpdatedAtUtc }).ToArrayAsync();
        var sessions = await db.TrainingSessions.AsNoTracking().Where(x => x.TrainingGroupId == g.Id && x.SessionDate >= today && x.Status == TrainingSessionStatus.Scheduled).OrderBy(x => x.Id).ToArrayAsync();
        var binding = await db.GroupResourceSchedules.AsNoTracking().SingleOrDefaultAsync(x => x.TrainingGroupId == g.Id && x.IsActive);
        var bookings = await db.ResourceBookings.AsNoTracking().Where(x => x.TrainingGroupId == g.Id && x.Date >= today && x.State == Academy.Infrastructure.Facilities.ResourceBookingState.Reserved).OrderBy(x => x.Id).ToArrayAsync();
        var periods = await db.SubscriptionPeriods.AsNoTracking().Include(x => x.SubscriptionPlan).Where(x => ids.Contains(x.SportEnrollmentId) && x.IsActive && x.Status != SubscriptionPeriodStatus.Cancelled && x.Status != SubscriptionPeriodStatus.Frozen && x.StartDate <= today && (x.EndDate == null || x.EndDate >= today)).OrderBy(x => x.Id).ToArrayAsync();
        var conflicts = new List<string>();
        if (level is null) conflicts.Add("حدد مستوى تدريب معتمدًا للمجموعة قبل التسكين.");
        if (g.Capacity is null or <= 0) conflicts.Add("حدد سعة المجموعة قبل التسكين.");
        else if (assessBatch && roster.Length + rows.Count > g.Capacity) conflicts.Add("عدد اللاعبين المختار يتجاوز المقاعد المتاحة؛ لم يُحفظ أي لاعب.");
        if (coaches.Length == 0) conflicts.Add("أسند مدربًا فعالًا للمجموعة أولًا.");
        if (sessions.Length == 0) conflicts.Add("أنشئ حصص المجموعة القادمة قبل التسكين.");
        if (sessions.Length > 1000) conflicts.Add("جدول المجموعة أكبر من الحد المسموح لهذه المعاينة.");
        if (g.RegistrationFormat != TrainingRegistrationFormat.Group && (g.FormatReviewedByUserId is null || g.FormatReviewedAtUtc is null || binding is null)) conflicts.Add("التدريب الخاص وشبه الخاص يحتاج اعتماد السعة وحجز مورد ومدرب قبل التسكين.");
        foreach (var session in sessions.Take(1000))
        {
            try { await coordinator.CheckCoachSlot(g.AcademyId, g.Id, session.SessionDate, session.StartTime, session.EndTime, session.Id); }
            catch (FacilityRejected error) { conflicts.Add(error.Message); break; }
        }
        uint? resourceVersion = null;
        if (binding is not null)
        {
            var requiredSeats = roster.Length + (assessBatch ? rows.Count : 0);
            var payload = await db.AcademyConfigurationVersions.AsNoTracking().Where(x => x.State == ConfigurationState.Active).Select(x => x.Payload).SingleOrDefaultAsync();
            if (payload is null || !AcademyRuntimeSettings.Read(payload).Features.GetValueOrDefault("facilities") || !await db.AcademyFeatureEntitlements.AnyAsync(x => x.Feature == "facilities" && x.IsActive && x.Enabled)) conflicts.Add("ميزة المرافق المرتبطة بهذه المجموعة غير متاحة.");
            if (requiredSeats > binding.ReservedSeats) conflicts.Add("المقاعد المحجوزة في الحارة لا تكفي؛ راجع حجز المجموعة.");
            if (sessions.Any(s => !bookings.Any(b => b.TrainingSessionId == s.Id && b.ResourceId == binding.ResourceId && b.ReservedSeats >= requiredSeats))) conflicts.Add("حجز الحارة غير مكتمل لكل الحصص القادمة.");
            var resource = await db.FacilityResources.AsNoTracking().SingleOrDefaultAsync(x => x.Id == binding.ResourceId && x.IsActive && x.ApprovedAtUtc != null);
            resourceVersion = resource?.Version;
            if (resource is null || !resource.ServiceKinds.Contains("Training")) conflicts.Add("مورد التدريب غير معتمد أو غير فعال.");
            else if (sessions.Any(s => !resource.OperatingDays.Contains((int)s.SessionDate.DayOfWeek) || s.StartTime < resource.OpensAt || s.EndTime > resource.ClosesAt || resource.Capacity < requiredSeats)) conflicts.Add("المواعيد أو السعة لا تطابق سياسة المورد المعتمدة.");
            if (await db.ResourceClosures.AnyAsync(c => c.ResourceId == binding.ResourceId && c.IsActive && db.TrainingSessions.Any(s => s.TrainingGroupId == g.Id && s.Status == TrainingSessionStatus.Scheduled && s.SessionDate >= today && s.SessionDate >= c.FromDate && s.SessionDate <= c.ToDate && s.StartTime < c.EndTime && c.StartTime < s.EndTime))) conflicts.Add("إغلاق مورد يتعارض مع حصص المجموعة.");
        }
        var players = new List<PlacementCandidate>();
        foreach (var e in rows)
        {
            var reasons = new List<string>();
            if (!e.IsActive || !e.Player.IsActive || e.Status != EnrollmentStatus.Active) reasons.Add("التسجيل أو اللاعب غير فعال.");
            if (!assessBatch && (g.Capacity is null || roster.Length >= g.Capacity || binding is not null && roster.Length >= binding.ReservedSeats)) reasons.Add("لا يوجد مقعد متاح حاليًا.");
            if (e.TrainingGroupId.HasValue) reasons.Add("التسجيل مسكن بالفعل؛ استخدم نقل المجموعة بدل الإضافة.");
            if (e.SportId != g.SportId || e.BranchId != g.BranchId) reasons.Add("الرياضة أو الفرع لا يطابق المجموعة.");
            if (e.RegistrationFormat != g.RegistrationFormat) reasons.Add("نوع تسجيل اللاعب لا يطابق نمط تدريب المجموعة.");
            if (g.AgeCategory.MinimumBirthYear > e.Player.DateOfBirth.Year || g.AgeCategory.MaximumBirthYear < e.Player.DateOfBirth.Year || e.Player.DateOfBirth > today) reasons.Add("الفئة العمرية لا تناسب اللاعب.");
            if (level is null || e.TrainingLevelId != level.Id || e.LevelReviewedByUserId is null || e.LevelReviewedAtUtc is null) reasons.Add("راجع مستوى اللاعب؛ المستوى المعتمد يجب أن يطابق المجموعة.");
            if (!periods.Any(p => p.SportEnrollmentId == e.Id && (p.ServiceKindSnapshot ?? p.SubscriptionPlan.ServiceKind) == AcademyServiceKind.Training && Academy.Api.Slice3.PlanCatalog.FormatMatches(g.RegistrationFormat, p.TrainingFormatSnapshot ?? p.SubscriptionPlan.TrainingFormat) && ((p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) == SubscriptionPlanType.Duration || p.RemainingSessions is > 0))) reasons.Add("لا يوجد استحقاق تدريب سارٍ مطابق للنوع؛ يبقى اللاعب في الانتظار دون حضور مدفوع.");
            var otherGroups = await db.SportEnrollments.IgnoreQueryFilters(["trusted-branches"]).Where(x => x.AcademyId == g.AcademyId && x.PlayerId == e.PlayerId && x.Id != e.Id && x.IsActive && x.TrainingGroupId != null).Select(x => x.TrainingGroupId!.Value).ToArrayAsync();
            if (otherGroups.Length > 0 && sessions.Length > 0)
            {
                var otherSessions = await db.TrainingSessions.IgnoreQueryFilters(["trusted-branches"]).AsNoTracking().Where(x => x.AcademyId == g.AcademyId && otherGroups.Contains(x.TrainingGroupId) && x.SessionDate >= today && x.Status == TrainingSessionStatus.Scheduled).ToArrayAsync();
                if (sessions.Any(s => otherSessions.Any(o => o.SessionDate == s.SessionDate && o.StartTime < s.EndTime && s.StartTime < o.EndTime))) reasons.Add("موعد اللاعب يتعارض مع حصة أخرى؛ لا يمكن تجاوز التعارض.");
            }
            players.Add(new(e.Id, e.PlayerId, e.Player.ArabicName, e.Player.PlayerCode, e.TrainingLevelId, e.TrainingLevelId.HasValue ? levels.GetValueOrDefault(e.TrainingLevelId.Value)?.ArabicName : null, reasons.ToArray()));
        }
        var fingerprint = AcademyConfigurationService.Hash(JsonSerializer.Serialize(new { today, group = new { g.Id, g.Version, g.TrainingLevelId, g.Capacity, ageVersion = g.AgeCategory.Version }, levelVersion = level?.Version, roster, coaches, sessions = sessions.Select(x => new { x.Id, x.Version }), bindingVersion = binding?.Version, resourceVersion, bookings = bookings.Select(x => new { x.Id, x.Version }), periods = periods.Select(x => new { x.Id, x.Version }), enrollments = rows.Select(x => new { x.Id, x.Version, playerVersion = x.Player.Version }), conflicts, players }));
        return new(fingerprint, today, roster.Length, g.Capacity, rows.Count, conflicts.ToArray(), players.ToArray());
    }
    public async Task<object> Candidates(Guid groupId, string? search, int offset, Guid? enrollmentId = null)
    {
        if (offset < 0 || offset > 100000 || search?.Length > 100) throw new FacilityRejected("فلاتر غير صالحة.", 422);
        await using var tx = await db.Database.BeginTransactionAsync(); var (_, g) = await Group(groupId, false);
        var q = db.SportEnrollments.AsNoTracking().Where(e => e.IsActive && e.Player.IsActive && e.TrainingGroupId == null && e.SportId == g.SportId && e.BranchId == g.BranchId && e.RegistrationFormat == g.RegistrationFormat);
        if (enrollmentId.HasValue) q = q.Where(e => e.Id == enrollmentId);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(e => e.Player.ArabicName.Contains(search.Trim()) || e.Player.PlayerCode.Contains(search.Trim()));
        var ids = await q.OrderBy(x => x.Player.ArabicName).ThenBy(x => x.Id).Skip(offset).Take(100).Select(x => x.Id).ToArrayAsync();
        var preview = await Inspect(g, ids, false);
        return new { total = await q.CountAsync(), offset, pageSize = 100, levelId = g.TrainingLevelId, preview.EffectiveDate, preview.CurrentPlayers, preview.Capacity, preview.Conflicts, players = preview.Players };
    }
    public async Task<object> Preview(Guid groupId, PlacementWrite body)
    {
        var ids = Ids(body); await using var tx = await db.Database.BeginTransactionAsync(); var (_, g) = await Group(groupId, false); return await Inspect(g, ids);
    }
    public async Task<object> Save(Guid groupId, PlacementWrite body, string key)
    {
        var ids = Ids(body); if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new FacilityRejected("معرف العملية مطلوب.", 422);
        await using var tx = await db.Database.BeginTransactionAsync(); var (t, g) = await Group(groupId, true);
        var hash = AcademyConfigurationService.Hash(JsonSerializer.Serialize(new { groupId, ids })); var actor = db.RequestUserId;
        var prior = await db.GroupPlacementRevisions.SingleOrDefaultAsync(x => x.ActorUserId == actor && x.RequestKey == key);
        if (prior is not null) { if (prior.RequestHash != hash) throw new FacilityRejected("معرف العملية مستخدم لاختيار مختلف."); return new { prior.Id, prior.Placed, prior.EffectiveDate, replayed = true }; }
        var rows = await db.SportEnrollments.FromSqlInterpolated($"SELECT *, xmin FROM \"SportEnrollments\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\"=ANY({ids}) ORDER BY \"Id\" FOR UPDATE").ToListAsync();
        if (rows.Count != ids.Length) throw new FacilityRejected("أحد التسجيلات غير متاح.", 404);
        foreach (var player in rows.Select(x => x.PlayerId).Distinct().OrderBy(x => x)) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Players\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\"={player} FOR UPDATE");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"StaffGroupAssignments\" WHERE \"AcademyId\"={t.AcademyId} AND \"TrainingGroupId\"={g.Id} ORDER BY \"Id\" FOR SHARE");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AcademyMemberships\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\" IN (SELECT \"AcademyMembershipId\" FROM \"StaffGroupAssignments\" WHERE \"AcademyId\"={t.AcademyId} AND \"TrainingGroupId\"={g.Id}) ORDER BY \"Id\" FOR SHARE");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Users\" WHERE \"Id\" IN (SELECT \"UserId\" FROM \"AcademyMemberships\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\" IN (SELECT \"AcademyMembershipId\" FROM \"StaffGroupAssignments\" WHERE \"AcademyId\"={t.AcademyId} AND \"TrainingGroupId\"={g.Id})) ORDER BY \"Id\" FOR SHARE");
        // Freeze/cancel/session-consumption cannot race the eligibility check and commit.
        _ = await db.SubscriptionPeriods.FromSqlInterpolated($"SELECT *, xmin FROM \"SubscriptionPeriods\" WHERE \"AcademyId\"={t.AcademyId} AND \"SportEnrollmentId\"=ANY({ids}) ORDER BY \"Id\" FOR UPDATE").AsNoTracking().ToArrayAsync();
        var preview = await Inspect(g, ids);
        if (preview.Fingerprint != body.Fingerprint) throw new FacilityRejected("تغيرت البيانات بعد المعاينة؛ راجع التسكين من جديد.");
        var reasons = preview.Conflicts.Concat(preview.Players.SelectMany(x => x.Reasons)).Distinct().ToArray();
        if (reasons.Length != 0) throw new FacilityRejected(string.Join(" ", reasons));
        foreach (var row in rows) { row.TrainingGroupId = groupId; row.GroupAssignedFrom = preview.EffectiveDate; row.UpdatedAtUtc = time.GetUtcNow(); }
        var revision = new GroupPlacementRevision { Id = Guid.NewGuid(), AcademyId = t.AcademyId, BranchId = g.BranchId, TrainingGroupId = g.Id, ActorUserId = actor, EffectiveDate = preview.EffectiveDate, RequestKey = key, RequestHash = hash, Snapshot = JsonSerializer.Serialize(new { before = preview, assignedEnrollmentIds = ids, financialEffect = false, attendanceCreated = false }), Placed = rows.Count, CreatedAtUtc = time.GetUtcNow(), UpdatedAtUtc = time.GetUtcNow() };
        db.GroupPlacementRevisions.Add(revision); await db.SaveChangesAsync(); await tx.CommitAsync(); return new { revision.Id, revision.Placed, revision.EffectiveDate, replayed = false };
    }
}
