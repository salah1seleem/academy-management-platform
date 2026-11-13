using System.Globalization;
using Academy.Api.Auth;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using R = Academy.Api.Importing.FriendlyImportInput.Row;

namespace Academy.Api.Importing;

public sealed record ImportHistorySummary(int Periods, int Sessions, int Attendance, int Evaluations, int Published, int Drafts, int Scores);

public sealed partial class FriendlyImportHistory
{
    private FriendlyImportInput input = null!;
    private ConnectedImportPlan plan = null!;
    private Guid academy;
    private string source = "";
    private readonly Dictionary<string, Guid> guardians = [];
    private readonly Dictionary<string, Guid> coaches = [];
    private readonly Dictionary<string, Guid> groups = [];
    private readonly Dictionary<string, SubscriptionPlan> packages = [];
    private readonly Dictionary<string, EvaluationCriterion> criteria = [];
    private readonly Dictionary<string, Period> periods = [];
    private readonly Dictionary<string, Session> sessions = [];
    private readonly Dictionary<string, Assessment> assessments = [];
    private DateOnly cutoff;
    private sealed record Period(R Row, string Enrollment, SubscriptionPlan Plan, DateOnly Start, DateOnly? End, int? Entitled, int? Remaining, decimal Net, string Currency, bool Current);
    private sealed record Session(R Row, TrainingGroup Group, DateOnly Date, TimeOnly Start, TimeOnly End, TrainingSessionStatus State);
    private sealed record Assessment(R Row, R Enrollment, TrainingGroup Group, Guid Coach, DateOnly Date, EvaluationStatus State);
    public ImportHistorySummary Summary => new(periods.Count, sessions.Count, input.Rows["حضور اختياري"].Count, assessments.Count, assessments.Values.Count(a => a.State == EvaluationStatus.Published), assessments.Values.Count(a => a.State == EvaluationStatus.Draft), input.Rows["درجات اختيارية"].Count);
    public int PaymentsCount => input.Rows["تحصيلات سابقة اختيارية"].Count;
    public decimal PaymentsTotal => input.Rows["تحصيلات سابقة اختيارية"].Sum(r => ImportPreview.Amount(r["Amount"]) ?? 0);
    public ISet<string> ReviewedFamilies => guardians.Keys.Select(k => "H:" + k).ToHashSet();
    private Guid Id(string kind, string code) => ConnectedImportPlan.Id(academy, source, "history-" + kind, code);
    private bool Ref(R row, string field, string sheet) => input.Need(row, input.ByCode(sheet).ContainsKey(row[field]), field, "الكود غير موجود في ورقة " + sheet + ". استخدم الكود، وليس الاسم أو معرف قاعدة البيانات.");
    public object Fingerprint(string enrollment, bool legacyWorksheetRows)
    {
        var e = input.Enrollments[enrollment]; var player = input.Players[e["Player"]];
        var links = input.Rows["علاقات أولياء الأمور"].Where(r => r["Player"] == player["Code"]).OrderBy(r => r["Code"]).ToArray();
        var ps = periods.Where(p => p.Value.Enrollment == enrollment).Select(p => p.Key).ToHashSet();
        var evaluations = assessments.Where(a => a.Value.Row["Enrollment"] == enrollment).Select(a => a.Key).ToHashSet();
        R? Snapshot(R? row) => row is null || legacyWorksheetRows ? row : row with { Number = 0 };
        return new {
            player = Snapshot(player),
            links = links.Select(Snapshot),
            guardians = links.Select(l => Snapshot(input.Guardians.GetValueOrDefault(l["Guardian"]))),
            periods = ps.Order().Select(k => Snapshot(periods[k].Row)),
            attendance = input.Rows["حضور اختياري"].Where(r => r["Enrollment"] == enrollment).OrderBy(r => r["Code"]).Select(Snapshot),
            evaluations = evaluations.Order().Select(k => Snapshot(assessments[k].Row)),
            scores = input.Rows["درجات اختيارية"].Where(r => evaluations.Contains(r["Evaluation"])).OrderBy(r => r["Code"]).Select(Snapshot),
            payments = input.Rows["تحصيلات سابقة اختيارية"].Where(r => ps.Contains(r["Period"])).OrderBy(r => r["Code"]).Select(Snapshot)
        };
    }
    public static async Task<FriendlyImportHistory> Build(FriendlyImportInput input, ConnectedImportPlan plan, FoundationDbContext db, Guid academy, string source, TimeProvider time, Dictionary<string, Dictionary<string, Guid>> maps)
    {
        var h = new FriendlyImportHistory { input = input, plan = plan, academy = academy, source = source };
        h.zone = TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a => a.Id == academy).Select(a => a.TimeZone).SingleAsync());
        h.cutoff = ImportPreview.Date(ConnectedImportSchema.Meta(input.Original, "تاريخ القطع")) ?? DateOnly.MinValue;
        if (ConnectedImportSchema.Meta(input.Original, "نسخة القالب") != FriendlyImportSchema.Version) input.Errors.Add(new("إعدادات الملف", 2, "نسخة القالب", "نزّل نسخة القالب الحالية."));
        foreach (var (code, id) in maps.GetValueOrDefault("group", [])) if (plan.Groups.ContainsKey(id)) h.groups[code] = id;
        foreach (var (code, id) in maps.GetValueOrDefault("product", [])) if (plan.Plans.TryGetValue(id, out var p)) h.packages[code] = p;
        var memberships = await db.AcademyMemberships.AsNoTracking().Include(m => m.User).Where(m => m.AcademyId == academy && m.IsActive && m.User.IsActive).ToListAsync();
        foreach (var (code, id) in maps.GetValueOrDefault("coach", []))
        {
            var m = plan.Creates.OfType<AcademyMembership>().FirstOrDefault(m => m.Id == id) ?? memberships.FirstOrDefault(m => m.Id == id);
            if (m != null) h.coaches[code] = m.UserId;
        }
        await h.Guardians(db, memberships);
        foreach (var r in input.Rows["اللاعبون"])
        {
            var registered = input.Date(r, "Registered"); input.Need(r, registered <= h.cutoff, "Registered", "تاريخ التسجيل بعد تاريخ القطع.");
        }
        void AddPeriod(R r, bool current)
        {
            var enrollment = current ? r["Code"] : r["Enrollment"];
            if (!input.Enrollments.TryGetValue(enrollment, out var e)) { input.Error(r, "Enrollment", "كود التسجيل غير معروف."); return; }
            if (!input.Players.ContainsKey(e["Player"])) { input.Error(r, current ? "Player" : "Enrollment", "اللاعب المرتبط بالتسجيل غير موجود أو له كود مكرر."); return; }
            if (!h.packages.TryGetValue(r["Plan"], out var p)) { input.Error(r, "Plan", "كود الباقة غير صالح أو لم يجتز الفحص."); return; }
            if (!h.groups.TryGetValue(e["Group"], out var gid)) return; var group = plan.Groups[gid];
            input.Need(r, p.SportId == group.SportId && (int)p.TrainingFormat == (int)group.RegistrationFormat, "Plan", "الباقة لا تطابق رياضة ونمط المجموعة.");
            var start = input.Date(r, "Start"); var end = input.Date(r, "End", false); var entitled = input.Count(r, "Entitled", required: false); var remaining = input.Count(r, "Remaining", required: false); var net = input.Amount(r, "Net");
            input.Need(r, start <= h.cutoff && start >= input.Date(input.Players[e["Player"]], "Registered"), "Start", "البداية خارج تاريخ تسجيل اللاعب وتاريخ القطع.");
            input.Need(r, end == null || end >= start, "End", "نهاية الفترة أسبق من بدايتها.");
            input.Need(r, p.PlanType == SubscriptionPlanType.Sessions || end.HasValue, "End", "المدة والمزدوجة تحتاجان نهاية صريحة.");
            input.Need(r, p.PlanType == SubscriptionPlanType.Duration ? entitled == null && remaining == null : entitled > 0 && remaining >= 0 && remaining <= entitled, "Remaining", "راجع نوع الباقة وإجمالي الحصص والمتبقي. لا حصص لباقة المدة وحدها.");
            input.Need(r, current || (end.HasValue && end <= h.cutoff && end < input.Date(e, "Start") || p.PlanType == SubscriptionPlanType.Sessions && remaining == 0 && start < input.Date(e, "Start")), "End", "الفترات السابقة يجب أن تنتهي قبل الفترة الحالية؛ الحصص السابقة مستنفدة.");
            input.Need(r, r["Currency"] == plan.References.Currency, "Currency", "العملة لا تطابق الأكاديمية.");
            var code = current ? r["Period"] : r["Code"];
            if (start.HasValue && net.HasValue) input.Need(r, h.periods.TryAdd(code, new(r, enrollment, p, start.Value, end, entitled, remaining, net.Value, r["Currency"], current)), current ? "Period" : "Code", "كود الفترة مستخدم بالفعل.");
        }
        foreach (var r in input.Rows["التسجيلات"]) AddPeriod(r, true);
        foreach (var r in input.Rows["فترات سابقة اختيارية"]) AddPeriod(r, false);
        foreach (var group in h.periods.Values.GroupBy(p => p.Enrollment))
        {
            var ordered = group.OrderBy(p => p.Start).ToArray();
            for (var i = 1; i < ordered.Length; i++) input.Need(ordered[i].Row, (ordered[i - 1].End ?? ordered[i - 1].Start) < ordered[i].Start, "Start", "فترات الاشتراك متداخلة لنفس التسجيل.");
        }
        foreach (var r in input.Rows["حصص فعلية اختيارية"])
        {
            if (!h.groups.TryGetValue(r["Group"], out var gid)) { input.Error(r, "Group", "كود مجموعة غير صالح."); continue; }
            var date = input.Date(r, "Date");
            var ok = TimeOnly.TryParseExact(r["Start"], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) && TimeOnly.TryParseExact(r["End"], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) && end > start;
            input.Need(r, ok, "End", "أدخل وقتين صحيحين HH:mm؛ النهاية بعد البداية.");
            var state = r["State"] switch { "منعقدة" => TrainingSessionStatus.Held, "مجدولة" => TrainingSessionStatus.Scheduled, "ملغاة" => TrainingSessionStatus.Cancelled, _ => (TrainingSessionStatus)0 };
            input.Need(r, state != 0 && (state != TrainingSessionStatus.Held || date <= h.cutoff), "State", "اختر حالة صحيحة؛ الحصة المنعقدة لا تقع بعد تاريخ القطع.");
            if (date.HasValue && ok) h.sessions[r["Code"]] = new(r, plan.Groups[gid], date.Value, start, TimeOnly.ParseExact(r["End"], "HH:mm"), state);
        }
        foreach (var group in h.sessions.Values.GroupBy(s => (s.Group.Id, s.Date, s.Start)).Where(g => g.Count() > 1)) foreach (var s in group) input.Error(s.Row, "Code", "الحصة نفسها مكررة بأكواد مختلفة.");
        var sports = await db.Sports.AsNoTracking().Where(s => s.AcademyId == academy).ToListAsync(); sports.AddRange(plan.Creates.OfType<Sport>());
        foreach (var r in input.Rows["معايير تقييم اختيارية"])
        {
            if (!maps.GetValueOrDefault("sport", []).TryGetValue(r["Sport"], out var sport)) { input.Error(r, "Sport", "كود الرياضة غير معروف."); continue; }
            var weight = input.Amount(r, "Weight"); input.Need(r, weight is > 0 and <= 100, "Weight", "الوزن أكبر من صفر وحتى 100.");
            FootballAxis? axis = Enum.TryParse<FootballAxis>(r["Axis"], out var parsed) && Enum.IsDefined(parsed) ? parsed : null;
            input.Need(r, r["Axis"] == "" || axis.HasValue && sports.Single(s => s.Id == sport).Kind == SportKind.Football, "Axis", "محور الكرة يكون Passing أو Dribbling أو Speed أو Defending أو Physical أو Shooting لرياضة كرة القدم فقط.");
            var criterion = new EvaluationCriterion { Id = h.Id("criterion", r["Code"]), AcademyId = academy, SportId = sport, ArabicName = r["Name"], Weight = weight ?? 1, FootballAxis = axis, DisplayOrder = h.criteria.Count + 1 };
            var candidates = await db.EvaluationCriteria.AsNoTracking().Where(c => c.AcademyId == academy && (c.Id == criterion.Id || c.SportId == sport && c.ArabicName == criterion.ArabicName)).ToArrayAsync();
            var old = candidates.FirstOrDefault(c => c.Id == criterion.Id) ?? candidates.FirstOrDefault();
            if (old == null) plan.Creates.Add(criterion);
            else { input.Need(r, old.IsActive && old.SportId == sport && old.ArabicName == criterion.ArabicName && old.Weight == criterion.Weight && old.FootballAxis == axis, "Code", "كود المعيار مستخدم ببيانات مختلفة."); criterion.Id = old.Id; }
            h.criteria[r["Code"]] = criterion;
        }
        foreach (var group in h.criteria.Values.GroupBy(c => (c.SportId, c.ArabicName)).Where(g => g.Count() > 1)) input.Errors.Add(new("معايير تقييم اختيارية", 2, "اسم المعيار", "اسم معيار مكرر لنفس الرياضة."));
        foreach (var r in input.Rows["تقييمات اختيارية"])
        {
            if (!h.coaches.TryGetValue(r["Coach"], out var coach)) { input.Error(r, "Coach", "كود المدرب غير صالح."); continue; }
            if (!input.Enrollments.TryGetValue(r["Enrollment"], out var e) || !h.groups.TryGetValue(r["Group"], out var gid)) { input.Error(r, "Enrollment", "تسجيل أو مجموعة غير معروفة."); continue; }
            if (!input.Players.ContainsKey(e["Player"])) { input.Error(r, "Enrollment", "اللاعب المرتبط بالتسجيل غير موجود أو له كود مكرر."); continue; }
            var group = plan.Groups[gid]; var date = input.Date(r, "Date");
            input.Need(r, e["Group"] == r["Group"], "Group", "التقييم خارج مجموعة التسجيل.");
            var membership = maps.GetValueOrDefault("coach", []).GetValueOrDefault(r["Coach"]);
            input.Need(r, plan.Assignments.Any(a => a.TrainingGroupId == gid && a.AcademyMembershipId == membership), "Coach", "المدرب غير مسند للمجموعة.");
            input.Need(r, date <= h.cutoff && date >= input.Date(input.Players[e["Player"]], "Registered"), "Date", "تاريخ التقييم خارج تاريخ التسجيل وتاريخ القطع.");
            input.Need(r, date?.ToString("yyyy-MM") == r["Month"], "Month", "فترة التقرير yyyy-MM يجب أن تطابق شهر تاريخ التقييم.");
            var state = r["State"] == "منشور" ? EvaluationStatus.Published : r["State"] == "مسودة" ? EvaluationStatus.Draft : 0;
            input.Need(r, state != 0, "State", "اختر مسودة أو منشور.");
            input.Need(r, r["Session"] == "" || h.sessions.TryGetValue(r["Session"], out var session) && session.Group.Id == gid && session.Date == date && session.State == TrainingSessionStatus.Held, "Session", "الحصة المرتبطة يجب أن تكون منعقدة لنفس المجموعة والتاريخ.");
            if (date.HasValue) h.assessments[r["Code"]] = new(r, e, group, coach, date.Value, state);
        }
        var scoreKeys = new HashSet<(string, string)>();
        foreach (var r in input.Rows["درجات اختيارية"])
        {
            if (!h.assessments.TryGetValue(r["Evaluation"], out var a) || !h.criteria.TryGetValue(r["Criterion"], out var c)) { input.Error(r, "Evaluation", "التقييم أو المعيار غير معروف أو لم يجتز الفحص."); continue; }
            input.Need(r, c.SportId == a.Group.SportId, "Criterion", "المعيار من رياضة مختلفة عن التقييم.");
            input.Count(r, "Score", 100, false); input.Need(r, scoreKeys.Add((r["Evaluation"], r["Criterion"])), "Criterion", "درجة المعيار مكررة داخل التقييم.");
        }
        foreach (var a in h.assessments.Values) input.Need(a.Row, input.Rows["درجات اختيارية"].Any(r => r["Evaluation"] == a.Row["Code"] && r["Score"] != "") || a.State == EvaluationStatus.Draft, "State", "التقييم المنشور يحتاج درجة صالحة واحدة على الأقل.");
        h.ValidateAttendance(); h.ValidatePayments();
        return h;
    }
}
