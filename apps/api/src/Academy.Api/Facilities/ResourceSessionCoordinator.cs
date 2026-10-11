using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Persistence;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Facilities;

// Called inside the caller's transaction; no detached reservation or nested commit.
public sealed class ResourceSessionCoordinator(FoundationDbContext db, FacilityService facilities, TimeProvider time)
{
    public async Task LockCalendar(Guid academy, IEnumerable<Guid> groups, string action = "Add")
    {
        if (db.RequestMembershipId != Guid.Empty) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{academy}:{db.RequestMembershipId}"}, 0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + academy}, 0))");
        if (db.RequestScopeEnabled)
        {
            var member = await db.AcademyMemberships.AsNoTracking().SingleOrDefaultAsync(m => m.Id == db.RequestMembershipId && m.IsActive && m.User.IsActive && m.Academy.IsActive);
            if (member is null || member.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin)) throw new FacilityRejected("إدارة الحصص للإداري المصرح له.", 403);
            var config = await db.AcademyConfigurationVersions.Where(x => x.State == ConfigurationState.Active).Select(x => x.Payload).SingleOrDefaultAsync();
            if (config is null || !AcademyRuntimeSettings.Read(config).Features.GetValueOrDefault("training") || !await db.AcademyFeatureEntitlements.AnyAsync(x => x.Feature == "training" && x.Enabled && x.IsActive)) throw new FacilityRejected("التدريب غير متاح حاليًا لهذه الأكاديمية.", 403);
            if (member.Role == AcademyRole.AcademyAdmin)
            {
                var grant = await db.StaffActionGrants.AsNoTracking().SingleOrDefaultAsync(x => x.MembershipId == member.Id && x.Module == "academy" && x.IsActive);
                if (member.UseActionGrants ? grant?.Read != true || !(action == "Add" ? grant.Add : grant.Edit) : await db.StaffGrants.AnyAsync(x => x.MembershipId == member.Id && x.Permission == AcademyPermissions.StructureManage && x.IsActive && !x.Allowed)) throw new FacilityRejected("صلاحية إدارة الحصص غير متاحة حاليًا.", 403);
            }
        }
        var groupIds = groups.Distinct().OrderBy(x => x).ToArray();
        foreach (var group in groupIds)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"timetable:{academy}:{group}"}, 0))");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"group-capacity:{academy}:{group}"}, 0))");
        }
        // Calendar -> resource -> coach is shared by timetable/manual/generator writes.
        foreach (var resource in await db.GroupResourceSchedules.Where(x => groupIds.Contains(x.TrainingGroupId) && x.IsActive).Select(x => x.ResourceId).Distinct().OrderBy(x => x).ToArrayAsync()) await facilities.Lock(resource);
        foreach (var coach in await db.StaffGroupAssignments.Where(x => groupIds.Contains(x.TrainingGroupId) && x.IsActive).Select(x => x.AcademyMembershipId).Distinct().OrderBy(x => x).ToArrayAsync()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"facility-coach:{academy}:{coach}"}, 0))");
    }
    public async Task CheckCoachSlot(Guid academy, Guid group, DateOnly date, TimeOnly start, TimeOnly end, Guid? session = null, Guid[]? excludedSessions = null, Guid[]? excludedBookings = null)
    {
        excludedSessions ??= []; excludedBookings ??= [];
        var coaches = await db.StaffGroupAssignments.Where(x => x.TrainingGroupId == group && x.IsActive && x.AcademyMembership.IsActive && x.AcademyMembership.User.IsActive).Select(x => x.AcademyMembershipId).Distinct().OrderBy(x => x).ToArrayAsync();
        foreach (var coach in coaches)
        {
            var assignments = db.StaffGroupAssignments.IgnoreQueryFilters(["trusted-branches"]);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"facility-coach:{academy}:{coach}"}, 0))");
            if (await db.ResourceBookings.IgnoreQueryFilters(["trusted-branches"]).AnyAsync(x => x.AcademyId == academy && x.CoachMembershipId == coach && (!session.HasValue || x.TrainingSessionId != session) && !excludedBookings.Contains(x.Id) && x.State == ResourceBookingState.Reserved && x.Date == date && x.StartTime < end && start < x.EndTime)
                || await db.TrainingSessions.IgnoreQueryFilters(["trusted-branches"]).AnyAsync(s => s.AcademyId == academy && s.Id != session && !excludedSessions.Contains(s.Id) && s.SessionDate == date && s.Status != TrainingSessionStatus.Cancelled && s.StartTime < end && start < s.EndTime && assignments.Any(a => a.AcademyId == academy && a.TrainingGroupId == s.TrainingGroupId && a.AcademyMembershipId == coach && a.IsActive))) throw new FacilityRejected("أحد مدربي المجموعة لديه حصة أو حجز آخر في الموعد المختار.");
        }
    }
    public async Task Reserve(TrainingSession session, Guid actor)
    {
        await CheckCoachSlot(session.AcademyId, session.TrainingGroupId, session.SessionDate, session.StartTime, session.EndTime, session.Id);
        await CheckRosterSlot(session.AcademyId, session.TrainingGroupId, session.SessionDate, session.StartTime, session.EndTime, session.Id);
        var plan = await db.GroupResourceSchedules.AsNoTracking().SingleOrDefaultAsync(x => x.TrainingGroupId == session.TrainingGroupId && x.IsActive);
        if (plan is null) return;
        await facilities.Authority("Add");
        if (session.SessionDate < plan.ValidFrom || session.SessionDate > plan.ValidTo) throw new FacilityRejected("الحصة خارج فترة حجز المجموعة. عدّل جدول المورد أولًا.", 422);
        if (await db.ResourceBookings.AnyAsync(x => x.TrainingSessionId == session.Id && x.State == ResourceBookingState.Reserved)) return;
        await facilities.ReserveLocked(new(plan.ResourceId, session.SessionDate, session.StartTime, session.EndTime, plan.ReservedSeats, plan.Exclusive, "Training", session.TrainingGroupId, plan.CoachMembershipId, session.Id), "session-" + Guid.NewGuid().ToString("N"), session.AcademyId, actor);
    }
    public async Task<int> CheckRosterSlot(Guid academy, Guid group, DateOnly date, TimeOnly start, TimeOnly end, Guid? session = null, bool checkBookedSeats = true)
    {
        var roster = db.SportEnrollments.Where(e => e.AcademyId == academy && e.IsActive && FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, date) == group);
        var count = await roster.CountAsync();
        var capacity = await db.TrainingGroups.Where(g => g.Id == group).Select(g => g.Capacity).SingleAsync();
        if (capacity.HasValue && count > capacity.Value) throw new FacilityRejected("عدد أعضاء المجموعة المؤرخ يتجاوز سعتها في هذا اليوم.");
        var seats = await db.GroupResourceSchedules.Where(g => g.TrainingGroupId == group && g.IsActive).Select(g => (int?)g.ReservedSeats).SingleOrDefaultAsync();
        if (checkBookedSeats && seats.HasValue && count > seats.Value) throw new FacilityRejected("حجز المورد لا يكفي عدد الأعضاء في تاريخ الحصة.");
        var allEnrollments = db.SportEnrollments.IgnoreQueryFilters(["trusted-branches"]).Where(e => e.AcademyId == academy && e.IsActive);
        var allSessions = db.TrainingSessions.IgnoreQueryFilters(["trusted-branches"]).Where(s => s.AcademyId == academy && s.Id != session && s.TrainingGroupId != group && s.SessionDate == date && s.Status != TrainingSessionStatus.Cancelled && s.StartTime < end && start < s.EndTime);
        if (await roster.AnyAsync(e => allEnrollments.Any(other => other.PlayerId == e.PlayerId && other.Id != e.Id && allSessions.Any(s => FoundationDbContext.AssignedGroupAt(other.AcademyId, other.Id, date) == s.TrainingGroupId)))) throw new FacilityRejected("أحد اللاعبين لديه حصة أخرى متعارضة في هذا الموعد.");
        return count;
    }
    public async Task CancelBookings(TrainingSession session, Guid actor)
    {
        var rows = await db.ResourceBookings.Where(x => x.TrainingSessionId == session.Id && x.State == ResourceBookingState.Reserved).ToListAsync();
        if (rows.Count == 0) return;
        await facilities.Authority("Edit");
        if (session.SessionDate < await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(session.AcademyId, db, time)) throw new FacilityRejected("لا تعدل حجز حصة تاريخية من هذا الإجراء.");
        foreach (var id in rows.Select(x => x.ResourceId).Distinct().OrderBy(x => x)) await facilities.Lock(id);
        foreach (var row in rows) { row.State = ResourceBookingState.Cancelled; row.CancelledAtUtc = time.GetUtcNow(); row.CancelledByUserId = actor; row.UpdatedAtUtc = time.GetUtcNow(); }
    }
}
