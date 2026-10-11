using Academy.Api.Facilities;
using Academy.Api.Slice4;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public sealed record PlacementPreferences(int[]? Days = null, TimeOnly? StartTime = null, Guid? PreferredCoachId = null);
public sealed record PlacementChoice(Guid GroupId, string Group, int Rank, int CurrentPlayers, int Capacity, DateOnly NextDate, TimeOnly StartTime, string[] Reasons);
public sealed record PlacementSuggestions(DateOnly AsOfDate, Guid EnrollmentId, int WaitingDays, PlacementChoice[] Choices, string? NoAlternativeMessage);
public sealed partial class GroupPlacementService
{
    private async Task<SportEnrollment> Waiting(Guid id)
    {
        await Authority("players", "Read"); await Authority("academy", "Read");
        var e = await db.SportEnrollments.AsNoTracking().Include(x => x.Player).SingleOrDefaultAsync(x => x.Id == id && x.IsActive && x.Player.IsActive) ?? throw new FacilityRejected("التسجيل غير متاح.", 404); await Authority("players", "Read", e.BranchId);
        if (e.TrainingGroupId.HasValue) throw new FacilityRejected("التسجيل مسكن بالفعل؛ استخدم النقل المراجع.");
        return e;
    }
    public async Task<object> PreferenceOptions(Guid id)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var e = await Waiting(id);
        return new { coaches = await db.StaffGroupAssignments.AsNoTracking().Where(a => a.IsActive && a.TrainingGroup.BranchId == e.BranchId && a.TrainingGroup.SportId == e.SportId && a.TrainingGroup.IsActive && a.AcademyMembership.IsActive && a.AcademyMembership.User.IsActive).Select(a => new { id = a.AcademyMembershipId, name = a.AcademyMembership.User.DisplayName }).Distinct().OrderBy(x => x.name).ToArrayAsync() };
    }
    public async Task<object> Recommend(Guid id, PlacementPreferences preferences)
    {
        var days = preferences.Days ?? [];
        if (days.Length > 7 || days.Any(d => d is < 0 or > 6) || days.Distinct().Count() != days.Length) throw new FacilityRejected("أيام التدريب المفضلة غير صالحة.", 422);
        await using var tx = await db.Database.BeginTransactionAsync(); var e = await Waiting(id); var today = await AttendanceCalendar.TodayAsync(e.AcademyId, db, time);
        var waitingDays = Math.Clamp(today.DayNumber - DateOnly.FromDateTime(e.CreatedAtUtc.UtcDateTime).DayNumber, 0, 36500);
        var groups = await db.TrainingGroups.AsNoTracking().Include(g => g.AgeCategory).Where(g => g.BranchId == e.BranchId && g.SportId == e.SportId && g.IsActive && g.Branch.IsActive && g.Sport.IsActive && g.AgeCategory.IsActive && g.Capacity > 0 && g.TrainingLevelId != null && g.TrainingLevelId == e.TrainingLevelId && (g.AgeCategory.MinimumBirthYear == null || g.AgeCategory.MinimumBirthYear <= e.Player.DateOfBirth.Year) && (g.AgeCategory.MaximumBirthYear == null || g.AgeCategory.MaximumBirthYear >= e.Player.DateOfBirth.Year)).OrderBy(g => g.Id).Take(501).ToArrayAsync();
        if (groups.Length > 500) throw new FacilityRejected("عدد المجموعات كبير؛ راجع التسكين من قائمة المجموعات لهذا الفرع.", 422);
        var groupIds = groups.Select(g => g.Id).ToArray();
        var coaches = await db.StaffGroupAssignments.AsNoTracking().Where(a => a.IsActive && groupIds.Contains(a.TrainingGroupId) && a.AcademyMembership.IsActive && a.AcademyMembership.User.IsActive).Select(a => new { a.TrainingGroupId, a.AcademyMembershipId }).ToArrayAsync();
        if (preferences.PreferredCoachId.HasValue && !await db.StaffGroupAssignments.AnyAsync(a => a.IsActive && a.TrainingGroup.IsActive && a.TrainingGroup.BranchId == e.BranchId && a.TrainingGroup.SportId == e.SportId && a.AcademyMembershipId == preferences.PreferredCoachId && a.AcademyMembership.IsActive && a.AcademyMembership.User.IsActive)) throw new FacilityRejected("المدرب المفضل غير متاح لهذا التسجيل.", 422);
        var slots = await db.TrainingSessions.AsNoTracking().Where(s => groupIds.Contains(s.TrainingGroupId) && s.Status == TrainingSessionStatus.Scheduled && s.SessionDate >= today).Select(s => new { s.TrainingGroupId, s.SessionDate, s.StartTime }).ToArrayAsync();
        var counts = await db.SportEnrollments.AsNoTracking().Where(s => s.IsActive && s.TrainingGroupId != null && groupIds.Contains(s.TrainingGroupId.Value)).GroupBy(s => s.TrainingGroupId!.Value).Select(s => new { id = s.Key, count = s.Count() }).ToDictionaryAsync(s => s.id, s => s.count);
        var ordered = groups.Where(g => coaches.Any(c => c.TrainingGroupId == g.Id) && slots.Any(s => s.TrainingGroupId == g.Id) && counts.GetValueOrDefault(g.Id) < g.Capacity).Select(g => {
            var dates = slots.Where(s => s.TrainingGroupId == g.Id).ToArray(); var metric = PlacementPreferenceRank.Calculate(days, preferences.StartTime, preferences.PreferredCoachId.HasValue && !coaches.Any(c => c.TrainingGroupId == g.Id && c.AcademyMembershipId == preferences.PreferredCoachId), dates.Select(s => ((int)s.SessionDate.DayOfWeek, s.StartTime)).ToArray(), counts.GetValueOrDefault(g.Id), g.Capacity!.Value);
            return new { group = g, metric, next = dates.OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime).First() };
        }).OrderBy(x => x.metric.DayDistance).ThenBy(x => x.metric.TimeDistance).ThenBy(x => x.metric.CoachPenalty).ThenByDescending(x => x.metric.Occupancy).ThenBy(x => x.group.Id);
        var choices = new List<PlacementChoice>();
        // Sorting the visible pool first is equivalent to ranking after removing rejected candidates.
        // No candidate is returned until the shared hard-constraint engine passes it in full.
        foreach (var candidate in ordered)
        {
            var p = await Inspect(candidate.group, [id]); if (p.Conflicts.Length != 0 || p.Players.Any(x => x.Reasons.Length != 0)) continue;
            var reasons = new List<string> { "مطابقة للفرع والرياضة والعمر والمستوى المراجع", "استحقاق تدريب سارٍ ومدرب وجدول بلا تعارض", $"مقعد متاح: {p.CurrentPlayers + 1} من {p.Capacity} بعد التسكين" };
            if (days.Length > 0) reasons.Add(candidate.metric.DayDistance == 0 ? "تشمل الأيام المفضلة" : "أقرب بديل للأيام المفضلة مع الالتزام بالشروط");
            if (preferences.StartTime.HasValue) reasons.Add($"فرق أقرب وقت عن التفضيل: {candidate.metric.TimeDistance} دقيقة");
            if (preferences.PreferredCoachId.HasValue) reasons.Add(candidate.metric.CoachPenalty == 0 ? "تحافظ على المدرب المفضل" : "مدرب بديل مؤهل؛ التفضيل لا يتجاوز قيدًا إلزاميًا");
            choices.Add(new(candidate.group.Id, candidate.group.ArabicName, choices.Count + 1, p.CurrentPlayers, p.Capacity!.Value, candidate.next.SessionDate, candidate.next.StartTime, reasons.ToArray())); if (choices.Count == 3) break;
        }
        return new PlacementSuggestions(today, id, waitingDays, choices.ToArray(), choices.Count == 0 ? "لا توجد مجموعة تستوفي الشروط الحالية. يبقى التسجيل في الانتظار؛ راجع المستوى أو الاستحقاق أو أنشئ مجموعة مناسبة دون تجاوز السعة." : null);
    }
}
public sealed record PlacementPreferenceRank(int DayDistance, int TimeDistance, int CoachPenalty, decimal Occupancy)
{
    public static PlacementPreferenceRank Calculate(int[] days, TimeOnly? preferredTime, bool differentCoach, (int Day, TimeOnly Start)[] slots, int roster, int capacity)
    {
        var distance = days.Sum(d => slots.Length == 0 ? 7 : slots.Min(s => Math.Min(Math.Abs(d - s.Day), 7 - Math.Abs(d - s.Day))));
        var preferredSlots = slots.Where(s => days.Length == 0 || days.Contains(s.Day)).ToArray(); if (preferredSlots.Length == 0) preferredSlots = slots;
        var timeDistance = preferredTime.HasValue && preferredSlots.Length > 0 ? preferredSlots.Min(s => (int)Math.Abs(s.Start.ToTimeSpan().TotalMinutes - preferredTime.Value.ToTimeSpan().TotalMinutes)) : 0;
        return new(distance, timeDistance, differentCoach ? 1 : 0, capacity > 0 ? (decimal)roster / capacity : 0);
    }
}
