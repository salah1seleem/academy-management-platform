using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Operations;
using Academy.Api.Slice3;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Facilities;

public sealed record ResourceScheduleWrite(Guid ResourceId, Guid CoachMembershipId, int Seats, bool Exclusive);
public sealed record ScheduleConflict(DateOnly Date, string Message);
public sealed record TimetablePreview(string Fingerprint, int ProposedSessions, int CancelledSessions, int PreservedSessions, int CancelledBookings, int ExpectedPlayers, string? Resource, IReadOnlyList<ScheduleConflict> Conflicts);

public sealed class ResourceTimetableService(FoundationDbContext db, CurrentTenant tenant, IHttpContextAccessor http, TimeProvider time, ISubscriptionClock demoClock, FacilityService facilities, ResourceSessionCoordinator coordinator)
{
    private async Task<(TenantMembership Tenant, Guid Actor, TrainingGroup Group)> Begin(Guid groupId, DateOnly from)
    {
        var t = await tenant.ResolveAsync() ?? throw new FacilityRejected("يلزم تسجيل الدخول.", 401);
        var actor = Guid.Parse(http.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"}, 0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + t.AcademyId}, 0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"timetable:{t.AcademyId}:{groupId}"}, 0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"group-capacity:{t.AcademyId}:{groupId}"}, 0))");
        var member = await db.AcademyMemberships.SingleOrDefaultAsync(x => x.Id == t.MembershipId && x.IsActive && x.User.IsActive && x.Academy.IsActive);
        if (member is null || member.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin)) throw new FacilityRejected("إدارة الجدول للإداري المصرح له فقط.", 403);
        var config = await db.AcademyConfigurationVersions.Where(x => x.State == ConfigurationState.Active).Select(x => x.Payload).SingleOrDefaultAsync();
        if (config is null || !AcademyRuntimeSettings.Read(config).Features.GetValueOrDefault("training") || !await db.AcademyFeatureEntitlements.AnyAsync(x => x.Feature == "training" && x.Enabled && x.IsActive)) throw new FacilityRejected("التدريب غير متاح حاليًا لهذه الأكاديمية.", 403);
        var group = await db.TrainingGroups.SingleOrDefaultAsync(x => x.Id == groupId && x.IsActive && x.Branch.IsActive && x.Sport.IsActive) ?? throw new FacilityRejected("المجموعة غير متاحة.", 404);
        if (member.Role == AcademyRole.AcademyAdmin)
        {
            if (!member.AllBranches && !await db.MembershipBranchAccesses.AnyAsync(x => x.MembershipId == member.Id && x.BranchId == group.BranchId && x.IsActive)) throw new FacilityRejected("الفرع خارج صلاحياتك الحالية.", 403);
            var editing = await db.RecurringSchedules.AnyAsync(x => x.TrainingGroupId == groupId && x.IsActive);
            var grant = await db.StaffActionGrants.SingleOrDefaultAsync(x => x.MembershipId == member.Id && x.Module == "academy" && x.IsActive);
            if (member.UseActionGrants ? grant?.Read != true || !(editing ? grant.Edit : grant.Add) : await db.StaffGrants.AnyAsync(x => x.MembershipId == member.Id && x.Permission == AcademyPermissions.StructureManage && x.IsActive && !x.Allowed)) throw new FacilityRejected("صلاحية تعديل الجدول غير متاحة.", 403);
        }
        foreach (var session in await db.TrainingSessions.Where(s => s.TrainingGroupId == groupId && s.SessionDate >= from).OrderBy(s => s.Id).Select(s => s.Id).ToArrayAsync())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"attendance:{t.AcademyId:N}:{session:N}:{Guid.Empty:N}"}, 0))");
        return (t, actor, group);
    }
    private sealed record Plan(ResourceScheduleWrite? Value, GroupResourceSchedule? Stored, bool ResourceAware);
    private async Task<Plan> ResolvePlan(Guid group, TimetableWrite b)
    {
        var stored = await db.GroupResourceSchedules.SingleOrDefaultAsync(x => x.TrainingGroupId == group);
        if (b.RemoveResource && b.Resource is not null) throw new FacilityRejected("اختر إزالة الحجز أو تحديد المورد، وليس الاثنين.", 422);
        var value = b.RemoveResource ? null : b.Resource ?? (stored?.IsActive == true ? new(stored.ResourceId, stored.CoachMembershipId, stored.ReservedSeats, stored.Exclusive) : null);
        var existingBookings = await db.ResourceBookings.AnyAsync(x => x.TrainingGroupId == group && x.Date >= b.From && x.TrainingSessionId.HasValue && x.State == ResourceBookingState.Reserved);
        var aware = value is not null || stored?.IsActive == true || existingBookings;
        if (aware) { await facilities.Authority("Add"); if (stored?.IsActive == true || existingBookings) await facilities.Authority("Edit"); }
        return new(value, stored, aware);
    }
    private static string Intent(Guid group, TimetableWrite b) => AcademyConfigurationService.Hash(JsonSerializer.Serialize(new { group, b.From, b.To, days = b.Days?.OrderBy(x => x).ToArray(), b.StartTime, b.EndTime, b.Resource, b.RemoveResource }));
    private async Task<(TimetablePreview Preview, List<TrainingSession> Existing, HashSet<Guid> Preserved, List<DateOnly> Dates, List<ResourceBooking> Cancelled)> Inspect(TrainingGroup group, TimetableWrite b, Plan plan)
    {
        var today = await AttendanceCalendar.TodayAsync(group.AcademyId, db, time);
        if (b.To < b.From || b.To.DayNumber - b.From.DayNumber > 366 || b.From < (plan.ResourceAware ? today : demoClock.Today) || plan.ResourceAware && b.To > today.AddDays(366) || b.Days is null || b.Days.Length is < 1 or > 7 || b.Days.Distinct().Count() != b.Days.Length || b.Days.Any(x => !Enum.IsDefined(x)) || b.StartTime >= b.EndTime) throw new FacilityRejected("اختر أيامًا وتوقيتًا صحيحًا وفترة مسموحة لا تتجاوز سنة.", 422);
        var sessions = await db.TrainingSessions.Where(x => x.TrainingGroupId == group.Id && x.SessionDate >= b.From).OrderBy(x => x.Id).ToListAsync();
        var recorded = await db.PlayerAttendances.Where(x => x.TrainingGroupId == group.Id && x.Status != AttendanceStatus.NotRecorded).Select(x => x.TrainingSessionId).Union(db.StaffAttendances.Where(x => x.TrainingGroupId == group.Id && x.Status != AttendanceStatus.NotRecorded).Select(x => x.TrainingSessionId)).ToListAsync();
        var preserved = sessions.Where(s => s.SessionDate < today || s.Status == TrainingSessionStatus.Held || recorded.Contains(s.Id)).Select(s => s.Id).ToHashSet();
        var removed = sessions.Where(s => !preserved.Contains(s.Id)).Select(s => s.Id).ToArray();
        var bookings = await db.ResourceBookings.Where(x => x.TrainingSessionId.HasValue && removed.Contains(x.TrainingSessionId.Value) && x.State == ResourceBookingState.Reserved).OrderBy(x => x.Id).ToListAsync();
        var dates = new List<DateOnly>();
        for (var day = b.From; day <= b.To; day = day.AddDays(1)) if (b.Days.Contains(day.DayOfWeek) && !sessions.Any(s => preserved.Contains(s.Id) && s.SessionDate == day && s.StartTime == b.StartTime)) dates.Add(day);
        if (dates.Count == 0 && !sessions.Any(s => preserved.Contains(s.Id) && s.SessionDate <= b.To && b.Days.Contains(s.SessionDate.DayOfWeek) && s.StartTime == b.StartTime)) throw new FacilityRejected("لا يوجد موعد يطابق الأيام المختارة داخل الفترة. راجع الأيام والتواريخ.", 422);
        var roster = await db.SportEnrollments.Where(x => FoundationDbContext.AssignedGroupAt(x.AcademyId, x.Id, b.From) == group.Id && x.IsActive).OrderBy(x => x.Id).Select(x => new { x.Id, x.Version, x.Status }).ToListAsync();
        var conflicts = new List<ScheduleConflict>(); FacilityResource? resource = null;
        var datedRoster = new List<object>();
        foreach (var day in dates)
            try { var count = await coordinator.CheckRosterSlot(group.AcademyId, group.Id, day, b.StartTime, b.EndTime, checkBookedSeats: false); datedRoster.Add(new { day, count }); if (plan.Value is { } reservation && count > reservation.Seats) conflicts.Add(new(day, "المقاعد المطلوبة لا تكفي أعضاء المجموعة في هذا التاريخ.")); }
            catch (FacilityRejected e) { conflicts.Add(new(day, e.Message)); }
        if (plan.Value is { } p)
        {
            resource = await db.FacilityResources.AsNoTracking().SingleOrDefaultAsync(x => x.Id == p.ResourceId) ?? throw new FacilityRejected("المورد غير متاح.", 404);
            if (resource.BranchId != group.BranchId || !await db.FacilitySpaces.AnyAsync(s => s.Id == resource.FacilitySpaceId && s.SportId == group.SportId)) throw new FacilityRejected("المورد لا يطابق فرع ورياضة المجموعة.", 422);
            foreach (var day in dates)
                try { await facilities.CheckSlot(new(p.ResourceId, day, b.StartTime, b.EndTime, p.Seats, p.Exclusive, "Training", group.Id, p.CoachMembershipId), resource, bookings.Select(x => x.Id).ToArray(), removed); }
                catch (FacilityRejected e) when (e.Status is 409 or 422) { conflicts.Add(new(day, e.Message)); }
        }
        foreach (var day in dates)
            try { await coordinator.CheckCoachSlot(group.AcademyId, group.Id, day, b.StartTime, b.EndTime, excludedSessions: removed, excludedBookings: bookings.Select(x => x.Id).ToArray()); }
            catch (FacilityRejected e) { if (!conflicts.Any(x => x.Date == day)) conflicts.Add(new(day, e.Message)); }
        var coach = plan.Value?.CoachMembershipId;
        var coachGroups = await db.StaffGroupAssignments.IgnoreQueryFilters(["trusted-branches"]).Where(x => x.AcademyId == group.AcademyId && (coach.HasValue ? x.AcademyMembershipId == coach : x.TrainingGroupId == group.Id)).OrderBy(x => x.Id).Select(x => new { x.Id, x.Version, x.IsActive, x.TrainingGroupId, x.AcademyMembershipId }).ToListAsync();
        var coachGroupIds = coachGroups.Select(x => x.TrainingGroupId).ToArray();
        var externalSessions = await db.TrainingSessions.IgnoreQueryFilters(["trusted-branches"]).Where(x => x.AcademyId == group.AcademyId && coachGroupIds.Contains(x.TrainingGroupId) && x.SessionDate >= b.From && x.SessionDate <= b.To).OrderBy(x => x.Id).Select(x => new { x.Id, x.Version }).ToListAsync();
        var externalBookings = await db.ResourceBookings.IgnoreQueryFilters(["trusted-branches"]).Where(x => x.AcademyId == group.AcademyId && x.Date >= b.From && (x.ResourceId == (plan.Value == null ? Guid.Empty : plan.Value.ResourceId) || coach.HasValue && x.CoachMembershipId == coach)).OrderBy(x => x.Id).Select(x => new { x.Id, x.Version }).ToListAsync();
        var resourceId = plan.Value?.ResourceId ?? Guid.Empty;
        var closureVersions = await db.ResourceClosures.Where(x => x.ResourceId == resourceId).OrderBy(x => x.Id).Select(x => new { x.Id, x.Version }).ToListAsync();
        var fingerprint = AcademyConfigurationService.Hash(JsonSerializer.Serialize(new { intent = Intent(group.Id, b), today, group.Version, plan = plan.Stored?.Version, resourceVersion = resource?.Version, roster, datedRoster, sessions = sessions.Select(x => new { x.Id, x.Version }), preserved = preserved.OrderBy(x => x), coachGroups, externalSessions, externalBookings, closureVersions }));
        return (new(fingerprint, dates.Count, sessions.Count(x => !preserved.Contains(x.Id) && x.Status != TrainingSessionStatus.Cancelled), preserved.Count, bookings.Count, roster.Count, resource?.Name, conflicts), sessions, preserved, dates, bookings);
    }
    public async Task<object> Preview(Guid groupId, TimetableWrite b)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var (_, _, group) = await Begin(groupId, b.From); var plan = await ResolvePlan(groupId, b); var result = await Inspect(group, b, plan); await tx.CommitAsync(); return result.Preview;
    }
    public async Task<object> Save(Guid groupId, TimetableWrite b, string key)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var (t, actor, group) = await Begin(groupId, b.From); var plan = await ResolvePlan(groupId, b); var hash = Intent(groupId, b);
        if (key.Length > 100 || plan.ResourceAware && key.Length < 8) throw new FacilityRejected("تعذر تحديد عملية الجدول؛ أعد المعاينة.", 422);
        var before = plan.Stored is null ? null : JsonSerializer.Serialize(new { plan.Stored.ResourceId, plan.Stored.CoachMembershipId, plan.Stored.ReservedSeats, plan.Stored.Exclusive, plan.Stored.ValidFrom, plan.Stored.ValidTo, plan.Stored.IsActive });
        if (key.Length > 0)
        {
            var prior = await db.TrainingScheduleRevisions.SingleOrDefaultAsync(x => x.ActorUserId == actor && x.RequestKey == key);
            if (prior is not null) { if (prior.RequestHash != hash || prior.TrainingGroupId != groupId) throw new FacilityRejected("معرف العملية مرتبط بجدول آخر."); return new { created = prior.CreatedSessions, preserved = prior.PreservedSessions, replayed = true }; }
        }
        var resourceIds = await db.ResourceBookings.Where(x => x.TrainingGroupId == groupId && x.Date >= b.From && x.State == ResourceBookingState.Reserved).Select(x => x.ResourceId).Distinct().ToListAsync();
        if (plan.Value is not null) resourceIds.Add(plan.Value.ResourceId);
        foreach (var id in resourceIds.Distinct().OrderBy(x => x)) await facilities.Lock(id);
        var coachIds = await db.StaffGroupAssignments.Where(x => x.TrainingGroupId == groupId && x.IsActive).Select(x => x.AcademyMembershipId).ToListAsync();
        if (plan.Value is { } selected) coachIds.Add(selected.CoachMembershipId);
        foreach (var coachId in coachIds.Distinct().OrderBy(x => x)) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"facility-coach:{t.AcademyId}:{coachId}"}, 0))");
        var inspection = await Inspect(group, b, plan); var preview = inspection.Preview;
        if (plan.ResourceAware && b.PreviewFingerprint != preview.Fingerprint) throw new FacilityRejected("تغير الجدول أو السعة أو الأعضاء بعد المعاينة. أعد المعاينة قبل الاعتماد.");
        if (preview.Conflicts.Count > 0) throw new FacilityRejected($"توجد تعارضات في {preview.Conflicts.Count} موعد. راجع المعاينة؛ لم يحفظ أي تغيير.");
        var now = time.GetUtcNow(); var schedules = await db.RecurringSchedules.Where(x => x.TrainingGroupId == groupId).ToListAsync();
        foreach (var row in schedules) { row.IsActive = false; row.UpdatedAtUtc = now; }
        foreach (var row in inspection.Existing.Where(x => !inspection.Preserved.Contains(x.Id))) { row.Status = TrainingSessionStatus.Cancelled; row.UpdatedAtUtc = now; }
        foreach (var row in inspection.Cancelled) { row.State = ResourceBookingState.Cancelled; row.CancelledAtUtc = now; row.CancelledByUserId = actor; row.UpdatedAtUtc = now; }
        var created = new List<TrainingSession>();
        foreach (var day in b.Days)
        {
            var schedule = schedules.SingleOrDefault(x => x.DayOfWeek == day && x.StartTime == b.StartTime);
            if (schedule is null) { schedule = new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, TrainingGroupId = groupId, DayOfWeek = day, StartTime = b.StartTime, CreatedAtUtc = now }; db.RecurringSchedules.Add(schedule); }
            schedule.EndTime = b.EndTime; schedule.ValidFrom = b.From; schedule.ValidTo = b.To; schedule.IsActive = true; schedule.UpdatedAtUtc = now;
            foreach (var date in inspection.Dates.Where(d => d.DayOfWeek == day))
            {
                var row = inspection.Existing.SingleOrDefault(x => x.SessionDate == date && x.StartTime == b.StartTime);
                if (row is null) { row = new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, TrainingGroupId = groupId, BranchId = group.BranchId, SportId = group.SportId, AgeCategoryId = group.AgeCategoryId, CreatedByUserId = actor, CreatedAtUtc = now }; db.TrainingSessions.Add(row); }
                row.SessionDate = date; row.StartTime = b.StartTime; row.EndTime = b.EndTime; row.Status = TrainingSessionStatus.Scheduled; row.Source = TrainingSessionSource.RecurringSchedule; row.RecurringScheduleId = schedule.Id; row.UpdatedAtUtc = now; created.Add(row);
            }
        }
        if (plan.Value is { } value)
        {
            var stored = plan.Stored;
            if (stored is null) { stored = new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, TrainingGroupId = groupId, BranchId = group.BranchId, CreatedAtUtc = now }; db.GroupResourceSchedules.Add(stored); }
            stored.ResourceId = value.ResourceId; stored.CoachMembershipId = value.CoachMembershipId; stored.ReservedSeats = value.Seats; stored.Exclusive = value.Exclusive; stored.ValidFrom = b.From; stored.ValidTo = b.To; stored.IsActive = true; stored.UpdatedAtUtc = now;
        }
        else if (plan.Stored is not null) { plan.Stored.IsActive = false; plan.Stored.UpdatedAtUtc = now; }
        await db.SaveChangesAsync();
        var revisionId = Guid.NewGuid();
        if (plan.Value is { } reservation)
            foreach (var s in created) await facilities.ReserveLocked(new(reservation.ResourceId, s.SessionDate, s.StartTime, s.EndTime, reservation.Seats, reservation.Exclusive, "Training", groupId, reservation.CoachMembershipId, s.Id), $"tt-{revisionId:N}-{s.Id:N}", t.AcademyId, actor);
        db.TrainingScheduleRevisions.Add(new() { Id = revisionId, AcademyId = t.AcademyId, BranchId = group.BranchId, TrainingGroupId = groupId, ActorUserId = actor, RequestKey = key.Length > 0 ? key : Guid.NewGuid().ToString("N"), RequestHash = hash, Snapshot = JsonSerializer.Serialize(new { request = b, before, preview, sessions = created.Select(x => x.Id), cancelledBookings = inspection.Cancelled.Select(x => x.Id) }), CreatedSessions = created.Count, PreservedSessions = preview.PreservedSessions, CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return new { created = created.Count, preserved = preview.PreservedSessions, replayed = false };
    }
}
