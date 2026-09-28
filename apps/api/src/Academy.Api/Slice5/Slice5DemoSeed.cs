using System.Security.Cryptography;
using System.Text;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice5;

public static class Slice5DemoSeed
{
    public static readonly Guid CurrentFootballEvaluationId = Guid.Parse("72000000-0000-0000-0000-000000000001");
    public static readonly Guid PriorFootballEvaluationId = Guid.Parse("72000000-0000-0000-0000-000000000002");
    public static readonly Guid DraftFootballEvaluationId = Guid.Parse("72000000-0000-0000-0000-000000000003");
    public static readonly Guid SwimmingEvaluationId = Guid.Parse("72000000-0000-0000-0000-000000000004");
    public static readonly Guid AcademyBEvaluationId = Guid.Parse("72000000-0000-0000-0000-000000000005");

    private static readonly (string Name, FootballAxis Axis, int Score)[] FootballCriteria =
    [
        ("دقة التمرير", FootballAxis.Passing, 84), ("الرؤية", FootballAxis.Passing, 82), ("التمرير تحت الضغط", FootballAxis.Passing, 80),
        ("التحكم بالكرة", FootballAxis.Dribbling, 79), ("المراوغة", FootballAxis.Dribbling, 76), ("اللمسة الأولى", FootballAxis.Dribbling, 77),
        ("السرعة", FootballAxis.Speed, 90), ("التسارع", FootballAxis.Speed, 88), ("رد الفعل", FootballAxis.Speed, 86),
        ("الافتكاك", FootballAxis.Defending, 64), ("التمركز الدفاعي", FootballAxis.Defending, 67), ("المواجهات", FootballAxis.Defending, 65),
        ("القوة البدنية", FootballAxis.Physical, 72), ("التحمل", FootballAxis.Physical, 75), ("التوازن", FootballAxis.Physical, 72),
        ("دقة التسديد", FootballAxis.Shooting, 82), ("قوة التسديد", FootballAxis.Shooting, 80), ("إنهاء الهجمة", FootballAxis.Shooting, 78)
    ];
    private static readonly string[] SwimmingCriteria = ["تقنية التنفس", "وضع الجسم", "حركة الذراعين", "ضربات الرجلين", "التحمل", "الانطلاقة", "الدوران"];

    public static async Task SeedAsync(FoundationDbContext db, Guid ownerUserId, Guid coachUserId, Guid futureOwnerUserId, DateTimeOffset now, CancellationToken ct)
    {
        var player = await db.Players.SingleAsync(x => x.Id == Slice2DemoSeed.OmarPlayerId, ct);
        var profileChanged = false;
        if (player.FootballPosition is null) { player.FootballPosition = "جناح أيمن"; player.HeightCm = 142m; player.WeightKg = 38m; player.PreferredFoot = PreferredFoot.Right; profileChanged = true; }
        if (player.PhotoReference is null) { player.PhotoReference = "/icon.svg"; profileChanged = true; }
        if (profileChanged) await db.SaveChangesAsync(ct);

        var football = new List<EvaluationCriterion>();
        for (var i = 0; i < FootballCriteria.Length; i++) football.Add(await Criterion(db, DemoSeed.NogoomAcademyId, Slice2DemoSeed.FootballId, FootballCriteria[i].Name, i + 1, FootballCriteria[i].Axis, now, ct));
        var swimming = new List<EvaluationCriterion>();
        for (var i = 0; i < SwimmingCriteria.Length; i++) swimming.Add(await Criterion(db, DemoSeed.NogoomAcademyId, Slice2DemoSeed.SwimmingId, SwimmingCriteria[i], i + 1, null, now, ct));

        var footballEnrollment = await db.SportEnrollments.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.PlayerId == Slice2DemoSeed.OmarPlayerId && x.SportId == Slice2DemoSeed.FootballId, ct);
        var swimmingEnrollment = await db.SportEnrollments.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.PlayerId == Slice2DemoSeed.OmarPlayerId && x.SportId == Slice2DemoSeed.SwimmingId, ct);
        await Evaluation(db, PriorFootballEvaluationId, footballEnrollment, coachUserId, new DateOnly(2026, 8, 28), "أغسطس 2026", EvaluationStatus.Published, "بداية جيدة مع حاجة لمزيد من الثبات تحت الضغط.", football, FootballCriteria.Select(x => (int?)(x.Score - 6)).ToArray(), now.AddMonths(-1), ct);
        await Evaluation(db, CurrentFootballEvaluationId, footballEnrollment, coachUserId, new DateOnly(2026, 9, 28), "سبتمبر 2026", EvaluationStatus.Published, "تطور ملحوظ في السرعة والتمرير. بيانات عرض خيالية.", football, FootballCriteria.Select(x => (int?)x.Score).ToArray(), now, ct);
        await Evaluation(db, DraftFootballEvaluationId, footballEnrollment, coachUserId, new DateOnly(2026, 9, 29), "متابعة تدريبية", EvaluationStatus.Draft, "مسودة لا تظهر لولي الأمر.", football, football.Select((_, i) => i < 2 ? (int?)70 + i : null).ToArray(), now.AddHours(1), ct);
        await Evaluation(db, SwimmingEvaluationId, swimmingEnrollment, ownerUserId, new DateOnly(2026, 9, 26), "سبتمبر 2026", EvaluationStatus.Published, "أداء متوازن في أساسيات السباحة.", swimming, [78, 81, 76, 80, 74, 72, 75], now.AddDays(-2), ct);

        var bEnrollment = await db.SportEnrollments.SingleAsync(x => x.AcademyId == DemoSeed.FutureAcademyId && x.PlayerId == Slice2DemoSeed.AcademyBPlayerId, ct);
        var bCriterion = await Criterion(db, DemoSeed.FutureAcademyId, bEnrollment.SportId, "دقة التمرير", 1, FootballAxis.Passing, now, ct);
        await Evaluation(db, AcademyBEvaluationId, bEnrollment, futureOwnerUserId, new DateOnly(2026, 9, 28), "سبتمبر 2026", EvaluationStatus.Published, "بيانات أكاديمية ثانية لاختبار العزل.", [bCriterion], [75], now, ct);
    }

    private static async Task<EvaluationCriterion> Criterion(FoundationDbContext db, Guid academyId, Guid sportId, string name, int order, FootballAxis? axis, DateTimeOffset now, CancellationToken ct)
    {
        var id = Deterministic($"criterion:{academyId}:{sportId}:{name}"); var existing = await db.EvaluationCriteria.SingleOrDefaultAsync(x => x.Id == id, ct); if (existing is not null) return existing;
        var item = new EvaluationCriterion { Id = id, AcademyId = academyId, SportId = sportId, ArabicName = name, DisplayOrder = order, IsActive = true, Weight = 1m, FootballAxis = axis, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.EvaluationCriteria.Add(item); await db.SaveChangesAsync(ct); return item;
    }

    private static async Task Evaluation(FoundationDbContext db, Guid id, SportEnrollment enrollment, Guid evaluator, DateOnly date, string period, EvaluationStatus status, string notes, IReadOnlyList<EvaluationCriterion> criteria, IReadOnlyList<int?> values, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.PlayerEvaluations.AnyAsync(x => x.Id == id, ct)) return;
        var evaluation = new PlayerEvaluation { Id = id, AcademyId = enrollment.AcademyId, SportEnrollmentId = enrollment.Id, SportId = enrollment.SportId, TrainingGroupId = enrollment.TrainingGroupId, EvaluatedByUserId = evaluator, EvaluationDate = date, ReportingPeriod = period, Status = status, GeneralNotes = notes, PublishedAtUtc = status == EvaluationStatus.Published ? now : null, PublishedByUserId = status == EvaluationStatus.Published ? evaluator : null, CreatedAtUtc = now, UpdatedAtUtc = now };
        for (var i = 0; i < criteria.Count; i++) evaluation.Scores.Add(new EvaluationScore { Id = Deterministic($"score:{id}:{criteria[i].Id}"), AcademyId = enrollment.AcademyId, PlayerEvaluationId = id, EvaluationCriterionId = criteria[i].Id, SportId = enrollment.SportId, Score = values[i], Notes = values[i].HasValue ? "ملاحظة تدريبية تجريبية" : null, CriterionNameSnapshot = criteria[i].ArabicName, WeightSnapshot = criteria[i].Weight, FootballAxisSnapshot = criteria[i].FootballAxis, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.PlayerEvaluations.Add(evaluation); await db.SaveChangesAsync(ct);
    }

    private static Guid Deterministic(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value))[..16]);
}
