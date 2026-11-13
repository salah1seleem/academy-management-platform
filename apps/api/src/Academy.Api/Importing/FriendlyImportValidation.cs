using Academy.Api.Auth;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Importing;

public sealed partial class FriendlyImportHistory
{
    private async Task Guardians(FoundationDbContext db, List<AcademyMembership> memberships)
    {
        var mappings = await db.ImportedHouseholds.AsNoTracking().Where(h => h.SourceSystem == source).ToDictionaryAsync(h => h.HouseholdKey);
        var phones = input.Guardians.Values.Select(r => r["Phone"]).ToArray();
        var users = await db.Users.AsNoTracking().Where(u => phones.Contains(u.PhoneNumber) || phones.Contains(u.UserName)).ToListAsync();
        var profiles = await db.GuardianProfiles.AsNoTracking().Include(g => g.User).Where(g => g.AcademyId == academy && phones.Contains(g.User.PhoneNumber)).ToListAsync();
        foreach (var r in input.Guardians.Values)
        {
            if (EgyptPhoneNormalizer.Normalize(r["Phone"]) is not { } phone) continue;
            // Invalid V3 player rows do not produce guardian links. The split
            // workbooks are validated again with their own complete links.
            if (input.PlayerCellErrors.Count == 0)
                input.Need(r, input.Rows["علاقات أولياء الأمور"].Any(l => l["Guardian"] == r["Code"]), "Code", "ولي الأمر بلا علاقة بطفل. أضف العلاقة أو احذف الصف غير المستخدم.");
            var key = "H:" + r["Code"]; var hash = ImportCommitService.Hash(new { GuardianName = r["Name"], phone });
            if (mappings.TryGetValue(key, out var old))
            {
                input.Need(r, old.IdentityHash == hash && profiles.Any(g => g.Id == old.GuardianId && g.IsActive && g.User.IsActive), "Code", "كود ولي الأمر مستخدم بهوية مختلفة أو متوقفة.");
                guardians[r["Code"]] = old.GuardianId; continue;
            }
            var staged = plan.Creates.OfType<ApplicationUser>().SingleOrDefault(u => u.PhoneNumber == phone);
            var existing = users.SingleOrDefault(u => u.PhoneNumber == phone || u.UserName == phone);
            var user = existing ?? staged;
            if (user != null)
            {
                if (!input.Need(r, (r["Associate"] == "نعم" || plan.PlatformAccountReview) && user.IsActive && user.DisplayName == r["Name"], "Associate", "الهاتف مرتبط بحساب. راجع أن صاحب الحساب هو الشخص نفسه بالاسم والهاتف، ثم اختر نعم واعتمد علاقة الطفل. لا تُعدّل هوية قائمة ولا تربط شخصًا باسم مختلف.")) continue;
            }
            else
            {
                user = new ApplicationUser { Id = Id("guardian-user", r["Code"]), UserName = phone, NormalizedUserName = phone.ToUpperInvariant(), PhoneNumber = phone, PhoneNumberConfirmed = false, DisplayName = r["Name"], SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N"), LockoutEnabled = true };
                plan.Creates.Add(user);
            }
            if(existing is not null && plan.PlatformAccountReview)plan.AccountLinks.Add(new("ولي أمر",r["Name"],phone));
            var profile = profiles.SingleOrDefault(g => g.UserId == user.Id);
            if (profile != null && !input.Need(r, profile.IsActive && profile.DisplayName == r["Name"], "Name", "ملف ولي الأمر القائم مختلف أو متوقف.")) continue;
            if (profile == null)
            {
                profile = new GuardianProfile { Id = Id("guardian", r["Code"]), AcademyId = academy, UserId = user.Id, DisplayName = r["Name"], ContactPhone = phone };
                plan.Creates.Add(profile);
            }
            if (!memberships.Any(m => m.UserId == user.Id && m.Role == AcademyRole.Guardian) && !plan.Creates.OfType<AcademyMembership>().Any(m => m.UserId == user.Id && m.Role == AcademyRole.Guardian))
                plan.Creates.Add(new AcademyMembership { Id = Id("guardian-membership", r["Code"]), AcademyId = academy, UserId = user.Id, Role = AcademyRole.Guardian });
            guardians[r["Code"]] = profile.Id;
            plan.Creates.Add(new ImportedHousehold { Id = Id("family", r["Code"]), AcademyId = academy, SourceSystem = source, HouseholdKey = key, IdentityHash = hash, GuardianId = profile.Id, JobId = Guid.Empty });
        }
        plan.Effects.Add(new("أولياء الأمور", plan.Creates.OfType<GuardianProfile>().Count(), guardians.Count - plan.Creates.OfType<GuardianProfile>().Count()));
    }
    private void ValidateAttendance()
    {
        var unique = new HashSet<(string, string)>(); var charged = new Dictionary<string, int>();
        foreach (var r in input.Rows["حضور اختياري"])
        {
            if (!sessions.TryGetValue(r["Session"], out var s) || !input.Enrollments.TryGetValue(r["Enrollment"], out var e)) { input.Error(r, "Session", "الحصة أو التسجيل غير معروف أو لم يجتز الفحص."); continue; }
            if (!input.Players.ContainsKey(e["Player"])) { input.Error(r, "Enrollment", "اللاعب المرتبط بالتسجيل غير موجود أو له كود مكرر."); continue; }
            input.Need(r, groups.GetValueOrDefault(e["Group"]) == s.Group.Id && s.State == TrainingSessionStatus.Held && s.Date <= cutoff && s.Date >= input.Date(input.Players[e["Player"]], "Registered"), "Session", "الحضور يجب أن يخص حصة منعقدة لمجموعة اللاعب بعد تسجيله وقبل تاريخ القطع.");
            input.Need(r, unique.Add((r["Session"], r["Enrollment"])), "Enrollment", "سجل الحضور نفسه مكرر بأكواد مختلفة.");
            var state = Attendance(r["State"]); input.Need(r, state != 0, "State", "اختر حاضر أو متأخر أو غائب أو بعذر.");
            if (r["Period"] == "")
            {
                var sessionPlan = periods.Values.Any(p => p.Enrollment == r["Enrollment"] && p.Plan.PlanType != SubscriptionPlanType.Duration && Covers(p, s.Date));
                input.Need(r, !AttendanceRules.Attended(state) || !sessionPlan, "Period", "الحاضر أو المتأخر في باقة حصص يحتاج كود الفترة المخصوم منها."); continue;
            }
            if (!periods.TryGetValue(r["Period"], out var p)) { input.Error(r, "Period", "كود الفترة غير معروف."); continue; }
            input.Need(r, p.Enrollment == r["Enrollment"] && p.Plan.PlanType != SubscriptionPlanType.Duration && Covers(p, s.Date) && AttendanceRules.Attended(state), "Period", "الفترة لا تخص التسجيل والتاريخ، أو أن الحالة لا تستهلك حصة. باقة المدة لا تحتاج كود خصم.");
            charged[r["Period"]] = charged.GetValueOrDefault(r["Period"]) + 1;
        }
        foreach (var (code, count) in charged) input.Need(periods[code].Row, count <= periods[code].Entitled - periods[code].Remaining, "Remaining", "الحضور المستهلك أكبر من الفرق بين إجمالي الحصص والمتبقي. راجع الرصيد؛ لا نخصمه مرة ثانية عند الاستيراد.");
    }
    private bool Covers(Period p, DateOnly date)
    {
        var next = periods.Values.Where(o => o.Enrollment == p.Enrollment && o.Start > p.Start).Select(o => (DateOnly?)o.Start).Min();
        return date >= p.Start && (!p.End.HasValue || date <= p.End.Value) && (!next.HasValue || date < next.Value);
    }
    private void ValidatePayments()
    {
        var paid = new HashSet<string>(); var evidence = new HashSet<string>();
        foreach (var r in input.Rows["تحصيلات سابقة اختيارية"])
        {
            if (!periods.TryGetValue(r["Period"], out var p)) { input.Error(r, "Period", "كود الفترة غير معروف أو لم يجتز الفحص."); continue; }
            var amount = input.Amount(r, "Amount"); var date = input.Date(r, "Date");
            input.Need(r, amount > 0 && amount == p.Net, "Amount", "هذه النسخة تدعم قبض الفترة كاملًا مرة واحدة. المبلغ يجب أن يساوي صافي الفترة؛ لا دفعات جزئية أو استرداد داخل ملف الاستيراد.");
            input.Need(r, date <= cutoff && date >= input.Date(input.Players[input.Enrollments[p.Enrollment]["Player"]], "Registered"), "Date", "تاريخ القبض خارج تاريخ التسجيل وتاريخ القطع.");
            input.Need(r, r["Currency"] == p.Currency && r["Method"] is "نقدي" or "إلكتروني", "Method", "راجع عملة الفترة واختر نقدي أو إلكتروني.");
            input.Need(r, paid.Add(r["Period"]), "Period", "قبض الفترة مكرر. لكل فترة تحصيل واحد في هذه النسخة.");
            input.Need(r, evidence.Add(r["Evidence"]), "Evidence", "مرجع السند مكرر؛ راجع المصدر ولا تسجل نفس القبض مرتين.");
        }
    }
    private static AttendanceStatus Attendance(string value) => value switch { "حاضر" => AttendanceStatus.Present, "متأخر" => AttendanceStatus.Late, "غائب" => AttendanceStatus.Absent, "بعذر" => AttendanceStatus.Excused, _ => 0 };
}
