using Academy.Api.Facilities;
using Academy.Api.Slice8;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Importing;

public sealed partial class FriendlyImportHistory
{
    public async Task Publish(FoundationDbContext db, AcademyImportJob job, DateOnly publishedOn, ResourceSessionCoordinator resources, CancellationToken token)
    {
        var now = job.CreatedAtUtc;
        var mapped = await db.ImportedEnrollments.Where(m => m.SourceSystem == source && job.LegacyEnrollmentIds.Contains(m.LegacyEnrollmentId)).ToDictionaryAsync(m => m.LegacyEnrollmentId, token);
        var playerIds = mapped.Values.Select(m => m.PlayerId).Distinct().ToArray();
        var players = await db.Players.Where(p => playerIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, token);
        var enrollmentIds = mapped.Values.Select(m => m.SportEnrollmentId).ToArray();
        var enrollments = await db.SportEnrollments.Where(e => enrollmentIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, token);
        foreach (var (code, m) in mapped)
        {
            var registered = input.Date(input.Players[m.LegacyPlayerId], "Registered")!.Value;
            // Preserve technical CreatedAtUtc. The customer's original registration date is separate.
            players[m.PlayerId].RegisteredOn ??= registered;
            var photo = input.Extras.GetValueOrDefault(code).Photo;
            if (!string.IsNullOrEmpty(photo)) { var reference = photo == "ذكر" ? "/avatars/child-male.svg" : photo == "أنثى" ? "/avatars/child-female.svg" : "/avatars/child-neutral.svg"; if(players[m.PlayerId].PhotoReference is null) players[m.PlayerId].PhotoReference = reference; else if(players[m.PlayerId].PhotoReference != reference) throw new ImportRejected("identity"); }
            if (players[m.PlayerId].RegisteredOn != registered) throw new ImportRejected("identity");
            enrollments[m.SportEnrollmentId].GroupAssignedFrom ??= registered;
        }
        var linked = await db.GuardianPlayerLinks.Where(l => playerIds.Contains(l.PlayerId)).ToListAsync(token);
        foreach (var r in input.Rows["علاقات أولياء الأمور"])
        {
            var player = mapped.Values.First(m => m.LegacyPlayerId == r["Player"]).PlayerId;
            var guardian = guardians[r["Guardian"]];
            var old = linked.SingleOrDefault(l => l.PlayerId == player && l.GuardianId == guardian);
            if (old != null)
            {
                if (!old.IsActive || old.CreatedByUserId != job.ActorUserId && old.RelationshipType != r["Relation"]) throw new ImportRejected("identity");
                if (old.RelationshipType == "ولي أمر — اعتماد استيراد") old.RelationshipType = r["Relation"];
                else if (old.RelationshipType != r["Relation"]) throw new ImportRejected("identity");
            }
            else db.GuardianPlayerLinks.Add(new() { Id = Id("guardian-link", r["Code"]), AcademyId = academy, PlayerId = player, GuardianId = guardian, RelationshipType = r["Relation"], CreatedByUserId = job.ActorUserId, CreatedAtUtc = now, UpdatedAtUtc = now });
        }
        var storedPeriods = await db.SubscriptionPeriods.Where(p => enrollmentIds.Contains(p.SportEnrollmentId)).ToDictionaryAsync(p => p.Id, token);
        var periodIds = new Dictionary<string, Guid>();
        foreach (var (code, p) in periods)
        {
            var m = mapped[p.Enrollment]; var id = p.Current ? m.SubscriptionPeriodId : Id("period", code); periodIds[code] = id;
            if (storedPeriods.ContainsKey(id)) continue;
            var period = new SubscriptionPeriod { Id = id, AcademyId = academy, SportEnrollmentId = m.SportEnrollmentId, SportId = p.Plan.SportId, SubscriptionPlanId = p.Plan.Id, Origin = SubscriptionOrigin.ImportedHistorical, ImportJobId = job.Id, StartDate = p.Start, EndDate = p.End, InitialSessions = p.Entitled, RemainingSessions = p.Remaining, PriceSnapshot = p.Net, CurrencySnapshot = p.Currency, CreatedByUserId = job.ActorUserId, CreatedAtUtc = now, UpdatedAtUtc = now, PlanTypeSnapshot = p.Plan.PlanType, PlanNameSnapshot = p.Plan.ArabicName, ServiceKindSnapshot = p.Plan.ServiceKind, TrainingFormatSnapshot = p.Plan.TrainingFormat, PlanRevisionSnapshot = p.Plan.Revision, DurationDaysSnapshot = p.End.HasValue ? p.End.Value.DayNumber - p.Start.DayNumber + 1 : null, SessionCountSnapshot = p.Entitled };
            period.Status = SubscriptionPeriodState.Resolve(SubscriptionPeriodStatus.Active, p.Start, p.End, p.Plan.PlanType, p.Remaining, publishedOn);
            db.SubscriptionPeriods.Add(period); storedPeriods[id] = period;
        }
        await db.SaveChangesAsync(token);
        var groupIds = sessions.Values.Select(s => s.Group.Id).Distinct().ToArray();
        var storedSessions = await db.TrainingSessions.Where(s => groupIds.Contains(s.TrainingGroupId)).ToListAsync(token);
        var sessionIds = new Dictionary<string, Guid>();
        await resources.LockCalendar(academy, groupIds, platformImportJob: job.PlatformInitiated ? job.Id : null);
        foreach (var (code, s) in sessions)
        {
            var stamp = "استيراد تاريخي " + Id("session", code).ToString("N");
            var old = storedSessions.SingleOrDefault(x => x.TrainingGroupId == s.Group.Id && x.SessionDate == s.Date && x.StartTime == s.Start);
            if (old != null)
            {
                if (old.EndTime != s.End || !old.IsActive || old.Notes != stamp && (!plan.Schedules.Any(p => p.Id == old.RecurringScheduleId) || old.Status != TrainingSessionStatus.Scheduled || old.CreatedByUserId != job.ActorUserId)) throw new ImportRejected("conflict");
                if (old.Notes == stamp && old.Status != s.State) throw new ImportRejected("conflict");
                old.Status = s.State; old.Notes = stamp; sessionIds[code] = old.Id; continue;
            }
            var session = new TrainingSession { Id = Id("session", code), AcademyId = academy, TrainingGroupId = s.Group.Id, BranchId = s.Group.BranchId, SportId = s.Group.SportId, AgeCategoryId = s.Group.AgeCategoryId, SessionDate = s.Date, StartTime = s.Start, EndTime = s.End, Status = s.State, Source = TrainingSessionSource.Manual, Notes = stamp, CreatedByUserId = job.ActorUserId, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.TrainingSessions.Add(session); sessionIds[code] = session.Id; storedSessions.Add(session);
            await db.SaveChangesAsync(token);
            if (session.Status != TrainingSessionStatus.Cancelled) await resources.Reserve(session, job.ActorUserId, job.PlatformInitiated ? job.Id : null);
        }
        await db.SaveChangesAsync(token);
        var existingAttendance = (await db.PlayerAttendances.Where(a => enrollmentIds.Contains(a.SportEnrollmentId)).Select(a => a.Id).ToArrayAsync(token)).ToHashSet();
        var consumed = new Dictionary<string, int>();
        foreach (var r in input.Rows["حضور اختياري"].OrderBy(r => sessions[r["Session"]].Date).ThenBy(r => sessions[r["Session"]].Start).ThenBy(r => r["Code"], StringComparer.Ordinal))
        {
            var id = Id("attendance", r["Code"]); if (existingAttendance.Contains(id)) continue;
            var s = sessions[r["Session"]]; var m = mapped[r["Enrollment"]]; var pid = r["Period"] == "" ? (Guid?)null : periodIds[r["Period"]];
            var a = new PlayerAttendance { Id = id, AcademyId = academy, TrainingSessionId = sessionIds[r["Session"]], TrainingGroupId = s.Group.Id, SportEnrollmentId = m.SportEnrollmentId, Status = Attendance(r["State"]), RecordedByUserId = job.ActorUserId, ConsumedSubscriptionPeriodId = pid, Notes = "حضور تاريخي معتمد — الرصيد المستورد يشمل الاستهلاك", CreatedAtUtc = now, UpdatedAtUtc = now };
            db.PlayerAttendances.Add(a);
            if (pid.HasValue)
            {
                var p = periods[r["Period"]]; var used = consumed.GetValueOrDefault(r["Period"]); consumed[r["Period"]] = used + 1;
                db.SubscriptionSessionMovements.Add(new() { Id = Id("consume", r["Code"]), AcademyId = academy, SubscriptionPeriodId = pid.Value, PlayerAttendanceId = id, MovementType = SessionMovementType.AttendanceConsume, Quantity = -1, BalanceBefore = p.Entitled!.Value - used, BalanceAfter = p.Entitled.Value - used - 1, OccurredAtUtc = At(s.Date), PerformedByUserId = job.ActorUserId, Reason = "إثبات استهلاك تاريخي؛ لا خصم ثانٍ من الرصيد المستورد", CreatedAtUtc = now, UpdatedAtUtc = now });
            }
        }
        var evaluationIds = assessments.Keys.Select(c => Id("evaluation", c)).ToArray();
        var existingEvaluations = (await db.PlayerEvaluations.Where(e => evaluationIds.Contains(e.Id)).Select(e => e.Id).ToArrayAsync(token)).ToHashSet();
        var scoreRows = input.Rows["درجات اختيارية"].GroupBy(r => r["Evaluation"]).ToDictionary(g => g.Key, g => g.ToArray());
        foreach (var (code, a) in assessments)
        {
            var id = Id("evaluation", code); if (existingEvaluations.Contains(id)) continue;
            var e = new PlayerEvaluation { Id = id, AcademyId = academy, SportEnrollmentId = mapped[a.Row["Enrollment"]].SportEnrollmentId, SportId = a.Group.SportId, TrainingGroupId = a.Group.Id, EvaluatedByUserId = a.Coach, EvaluationDate = a.Date, ReportingPeriod = a.Row["Month"], Status = a.State, GeneralNotes = a.Row["Notes"] == "" ? null : a.Row["Notes"], TrainingSessionId = a.Row["Session"] == "" ? null : sessionIds[a.Row["Session"]], PublishedAtUtc = a.State == EvaluationStatus.Published ? At(a.Date) : null, PublishedByUserId = a.State == EvaluationStatus.Published ? a.Coach : null, CreatedAtUtc = now, UpdatedAtUtc = now };
            var scored = scoreRows.GetValueOrDefault(code, []).ToDictionary(r => r["Criterion"]);
            foreach (var (criterionCode, c) in criteria.Where(c => c.Value.SportId == a.Group.SportId))
            {
                var row = scored.GetValueOrDefault(criterionCode);
                e.Scores.Add(new() { Id = Id("score", code + ":" + criterionCode), AcademyId = academy, PlayerEvaluationId = id, EvaluationCriterionId = c.Id, SportId = c.SportId, Score = row == null || row["Score"] == "" ? null : int.Parse(row["Score"]), CriterionNameSnapshot = c.ArabicName, WeightSnapshot = c.Weight, FootballAxisSnapshot = c.FootballAxis, CreatedAtUtc = now, UpdatedAtUtc = now });
            }
            db.PlayerEvaluations.Add(e);
        }
        await db.SaveChangesAsync(token);
        await PublishPayments(db, job, mapped, storedPeriods, periodIds, token);
    }
    private TimeZoneInfo zone = TimeZoneInfo.Utc;
    private DateTimeOffset At(DateOnly date) => new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(12, 0)), zone), TimeSpan.Zero);
}
