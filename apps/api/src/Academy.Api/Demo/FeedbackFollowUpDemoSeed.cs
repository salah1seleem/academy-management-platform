using Academy.Api.Auth;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;
public static class FeedbackFollowUpDemoSeed
{
    public static async Task SeedRequests(FoundationDbContext db, IHostEnvironment env, DemoOptions options)
    {
        var a = FootballDemoSeed.AcademyId;
        if (!env.IsEnvironment("Demo") || !options.SeedEnabled || options.SeedProfile != "Football" || !await db.Academies.AnyAsync(x => x.Id == a && x.Slug == FootballDemoSeed.Slug && x.EnglishName == FootballDemoSeed.Marker)) throw new InvalidOperationException("طلبات الأسر الاصطناعية تتطلب Football Demo معلوم الهوية.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"football-feedback-request-demo"},0))");
        var id = FootballDemoSeed.Id("feedback-request-demo/0");
        if (await db.GuardianFeedbacks.AnyAsync(f => f.Id == id)) return;
        var child = FootballDemoSeed.Id("player/10");
        var guardian = await db.GuardianPlayerLinks.Where(l => l.AcademyId == a && l.PlayerId == child && l.IsActive).Select(l => l.Guardian.UserId).SingleAsync();
        var at = DateTimeOffset.Parse("2026-10-08T06:00:00Z");
        db.GuardianFeedbacks.Add(new() { Id = id, AcademyId = a, PlayerId = child, GuardianUserId = guardian,
            BranchId = FootballDemoSeed.Id("branch/1"), TrainingGroupId = FootballDemoSeed.Id("group/2"), Kind = "Request",
            Message = "طلب اصطناعي للإدارة: أحتاج توضيح مواعيد التدريب قبل ترتيب مواصلات الطفل. لا تغيير تلقائي للجدول.",
            Subject = "Schedule", ReportedSubject = "Schedule", Urgency = "Routine", ReportedUrgency = "Routine",
            Status = "Open", PeriodMonth = new(2026,10,1), IdempotencyKey = "feedback-request-demo/0", CreatedAtUtc = at, UpdatedAtUtc = at });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public static async Task SeedIncident(FoundationDbContext db, IHostEnvironment env, DemoOptions options)
    {
        var a=FootballDemoSeed.AcademyId; var child=FootballDemoSeed.Id("player/0"); var id=FootballDemoSeed.Id("feedback-incident-demo/0");
        if(!env.IsEnvironment("Demo") || !options.SeedEnabled || options.SeedProfile!="Football" || !await db.Academies.AnyAsync(x=>x.Id==a && x.Slug==FootballDemoSeed.Slug && x.EnglishName==FootballDemoSeed.Marker)) throw new InvalidOperationException("سياق الحصة الاصطناعي يتطلب Football Demo معلوم الهوية.");
        await using var tx=await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"football-feedback-incident-demo"},0))");
        if(await db.GuardianFeedbacks.AnyAsync(f=>f.Id==id)) return;
        var day=new DateOnly(2026,9,28); var session=(await Academy.Api.Operations.GuardianFeedbackEndpoints.IncidentSessions(db,a,child,day)).FirstOrDefault() ?? throw new InvalidOperationException("لا توجد حصة ديمو مؤرخة صالحة؛ لن نختلق إسنادًا تاريخيًا.");
        var guardian=await db.GuardianPlayerLinks.Where(l=>l.AcademyId==a && l.PlayerId==child && l.IsActive).Select(l=>l.Guardian.UserId).SingleAsync();
        var at=DateTimeOffset.Parse("2026-10-08T06:00:00Z");
        db.GuardianFeedbacks.Add(new(){Id=id,AcademyId=a,PlayerId=child,GuardianUserId=guardian,BranchId=session.BranchId,TrainingGroupId=session.GroupId,Kind="Complaint",Message="ملاحظة اصطناعية مرتبطة بحصة الديمو، دون اتهام مدرب أو إثبات حضور.",Status="Open",Subject="Schedule",ReportedSubject="Schedule",Urgency="Routine",ReportedUrgency="Routine",IncidentOn=day,IncidentTrainingSessionId=session.Id,IncidentSessionEvidence=Academy.Api.Operations.GuardianFeedbackEndpoints.IncidentSnapshot(session,null),PeriodMonth=new(2026,10,1),IdempotencyKey="feedback-incident-demo/0",CreatedAtUtc=at,UpdatedAtUtc=at});
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public static async Task SeedTriage(FoundationDbContext db, IHostEnvironment env, DemoOptions options)
    {
        var a = FootballDemoSeed.AcademyId;
        if (!env.IsEnvironment("Demo") || !options.SeedEnabled || options.SeedProfile != "Football" || !await db.Academies.AnyAsync(x => x.Id == a && x.Slug == FootballDemoSeed.Slug && x.EnglishName == FootballDemoSeed.Marker)) throw new InvalidOperationException("فرز البلاغات الاصطناعية يتطلب Football Demo معلوم الهوية.");
        await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"football-feedback-triage-demo"},0))");
        var guardian = await db.GuardianPlayerLinks.Where(l => l.AcademyId == a && l.PlayerId == FootballDemoSeed.Id("player/10") && l.IsActive).Select(l => l.Guardian.UserId).SingleAsync();
        var at = DateTimeOffset.Parse("2026-10-08T06:00:00Z");
        for (var i = 0; i < 2; i++) {
            var id = FootballDemoSeed.Id($"feedback-triage-demo/{i}"); if (await db.GuardianFeedbacks.AnyAsync(f => f.Id == id)) continue;
            var subject = i == 0 ? "Water" : "Schedule"; var urgency = i == 0 ? "Safety" : "Urgent";
            db.GuardianFeedbacks.Add(new() { Id = id, AcademyId = a, PlayerId = FootballDemoSeed.Id("player/10"), GuardianUserId = guardian, BranchId = FootballDemoSeed.Id("branch/1"), TrainingGroupId = FootballDemoSeed.Id("group/2"), Kind = "Complaint", Message = i == 0 ? "بلاغ سلامة اصطناعي عن المرفق للمراجعة البشرية؛ ليس واقعة حقيقية أو إثبات ضرر." : "بلاغ اصطناعي عاجل لمراجعة موعد التدريب.", Status = "Open", Subject = subject, ReportedSubject = subject, Urgency = urgency, ReportedUrgency = urgency, IncidentOn = new(2026,10,7), PeriodMonth = new(2026,10,1), IdempotencyKey = $"feedback-triage-demo/{i}", CreatedAtUtc = at, UpdatedAtUtc = at });
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public static async Task SeedInvestigation(FoundationDbContext db, IHostEnvironment env, DemoOptions options)
    {
        var a = FootballDemoSeed.AcademyId;
        if (!env.IsEnvironment("Demo") || !options.SeedEnabled || options.SeedProfile != "Football" || !await db.Academies.AnyAsync(x => x.Id == a && x.Slug == FootballDemoSeed.Slug && x.EnglishName == FootballDemoSeed.Marker)) throw new InvalidOperationException("تكليف التحقيق الاصطناعي يتطلب Football Demo معلوم الهوية.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"football-feedback-investigation-demo"},0))");
        var id = FootballDemoSeed.Id("feedback-investigation-demo/0");
        if (await db.GuardianFeedbacks.AnyAsync(f => f.Id == id)) return; // Never replace a manual cancellation or result.
        var member = AssignedFollowUpDemoSeed.MembershipId;
        if (!await Academy.Api.Operations.OwnerFollowUpEndpoints.DelegateAllowed(db, a, member, true)) throw new InvalidOperationException("أعد إعداد مسؤول العرض صراحة؛ لن يعيد seed منح صلاحيات سحبت يدويًا.");
        var guardian = await db.GuardianPlayerLinks.Where(l => l.AcademyId == a && l.PlayerId == FootballDemoSeed.Id("player/10") && l.IsActive).Select(l => l.Guardian.UserId).SingleAsync();
        var at = DateTimeOffset.Parse("2026-10-08T06:00:00Z");
        var f = new GuardianFeedback { Id = id, AcademyId = a, PlayerId = FootballDemoSeed.Id("player/10"), GuardianUserId = guardian,
            BranchId = FootballDemoSeed.Id("branch/1"), TrainingGroupId = FootballDemoSeed.Id("group/2"), CoachMembershipId = FootballDemoSeed.Id("member/coach3"),
            Kind = "Complaint", Message = "شكوى اصطناعية خاصة بالأسرة عن توزيع وقت التدريب؛ لا تُنسخ في تكليف الإداري.", Status = "Open", PeriodMonth = new(2026,10,1), IdempotencyKey = "feedback-investigation-demo/0", CreatedAtUtc = at, UpdatedAtUtc = at,
            InvestigationId = FootballDemoSeed.Id("feedback-investigation-demo/assignment"), InvestigatorMembershipId = member, InvestigationBrief = "راجع توزيع وقت التدريب واقترح تحسينًا للإدارة دون بيانات الأسرة." };
        db.GuardianFeedbacks.Add(f); await db.SaveChangesAsync();
        db.FeedbackChanges.Add(new() { Id = FootballDemoSeed.Id("feedback-investigation-demo/audit"), AcademyId = a, FeedbackId = id, ActorMembershipId = FootballDemoSeed.Id("member/owner"),
            Action = "Assign", FromStatus = f.Status, ToStatus = f.Status, InvestigationId = f.InvestigationId, InvestigatorMembershipId = member, InternalNote = f.InvestigationBrief,
            RequestId = FootballDemoSeed.Id("feedback-investigation-demo/request"), RequestHash = new string('2',64), CreatedAtUtc = at, UpdatedAtUtc = at });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public static async Task SeedCases(FoundationDbContext db, IHostEnvironment env, DemoOptions options)
    {
        var a = FootballDemoSeed.AcademyId;
        if (!env.IsEnvironment("Demo") || !options.SeedEnabled || options.SeedProfile != "Football" || !await db.Academies.AnyAsync(x => x.Id == a && x.Slug == FootballDemoSeed.Slug && x.EnglishName == FootballDemoSeed.Marker)) throw new InvalidOperationException("دورة الشكاوى الاصطناعية تتطلب Football Demo معلوم الهوية.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"football-feedback-case-demo"},0))");
        var at = DateTimeOffset.Parse("2026-10-08T06:00:00Z");
        for (var i = 0; i < 2; i++)
        {
            var id = FootballDemoSeed.Id($"feedback-case-demo/{i}"); if (await db.GuardianFeedbacks.AnyAsync(f => f.Id == id)) continue;
            var f = new GuardianFeedback { Id = id, AcademyId = a, PlayerId = FootballDemoSeed.Id("player/0"), GuardianUserId = FootballDemoSeed.Id("user/guardian1"),
                BranchId = FootballDemoSeed.Id("branch/0"), TrainingGroupId = FootballDemoSeed.Id("group/0"), CoachMembershipId = FootballDemoSeed.Id("member/coach1"),
                Kind = i == 0 ? "Complaint" : "Suggestion", Message = i == 0 ? "شكوى اصطناعية: نراجع تنظيم إشعار الحصة مع الإدارة." : "اقتراح اصطناعي: ننتظر تفاصيل الوقت الأنسب من الأسرة.",
                Status = "Open", DueOn = new(2026,10,10), DueSetByUserId = FootballDemoSeed.Id("user/owner"), DueSetAtUtc = at, PeriodMonth = new(2026,10,1),
                IdempotencyKey = $"feedback-case-demo/{i}", CreatedAtUtc = at, UpdatedAtUtc = at };
            db.GuardianFeedbacks.Add(f); await db.SaveChangesAsync();
            f.Status = "InProgress"; await db.SaveChangesAsync();
            db.FeedbackChanges.Add(new() { Id = FootballDemoSeed.Id($"feedback-case-demo/{i}/start"), AcademyId = a, FeedbackId = id, ActorMembershipId = FootballDemoSeed.Id("member/owner"),
                RequestId = FootballDemoSeed.Id($"feedback-case-demo/{i}/request-start"), RequestHash = new string('0',64), Action = "Transition", FromStatus = "Open", ToStatus = "InProgress", AfterDueOn = f.DueOn, BeforeDueOn = f.DueOn,
                InternalNote = "مثال تجريبي خاص: بدأ المالك المراجعة دون مشاركة بيانات الأسرة.", CreatedAtUtc = at, UpdatedAtUtc = at });
            await db.SaveChangesAsync();
            if (i == 1)
            {
                f.Status = "AwaitingInfo"; f.UpdatedAtUtc = at.AddMinutes(1); await db.SaveChangesAsync();
                db.FeedbackChanges.Add(new() { Id = FootballDemoSeed.Id("feedback-case-demo/1/wait"), AcademyId = a, FeedbackId = id, ActorMembershipId = FootballDemoSeed.Id("member/owner"),
                    RequestId = FootballDemoSeed.Id("feedback-case-demo/1/request-wait"), RequestHash = new string('1',64), Action = "Transition", FromStatus = "InProgress", ToStatus = "AwaitingInfo", AfterDueOn = f.DueOn, BeforeDueOn = f.DueOn,
                    InternalNote = "انتظار معلومات في السيناريو الاصطناعي؛ لا تواصل خارجي.", CreatedAtUtc = at.AddMinutes(1), UpdatedAtUtc = at.AddMinutes(1) });
                await db.SaveChangesAsync();
            }
        }
        await tx.CommitAsync();
    }
    public static async Task Seed(FoundationDbContext db, IHostEnvironment env, DemoOptions options)
    {
        var a=FootballDemoSeed.AcademyId;
        if(!env.IsEnvironment("Demo")||!options.SeedEnabled||options.SeedProfile!="Football"||!await db.Academies.AnyAsync(x=>x.Id==a&&x.Slug==FootballDemoSeed.Slug&&x.EnglishName==FootballDemoSeed.Marker))throw new InvalidOperationException("ملاحظات المتابعة الاصطناعية تتطلب Football Demo معلوم الهوية.");
        await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"football-feedback-followup-demo"},0))");
        var player=FootballDemoSeed.Id("player/10");
        var guardian=await db.GuardianPlayerLinks.Where(l=>l.AcademyId==a&&l.PlayerId==player&&l.IsActive).Select(l=>l.Guardian.UserId).SingleAsync();
        var at=DateTimeOffset.Parse("2026-10-08T06:00:00Z");
        for(var i=0;i<2;i++) {
            var id=FootballDemoSeed.Id($"feedback-followup-demo/{i}");if(await db.GuardianFeedbacks.AnyAsync(f=>f.Id==id))continue;
            db.GuardianFeedbacks.Add(new(){Id=id,AcademyId=a,PlayerId=player,GuardianUserId=guardian,TrainingGroupId=FootballDemoSeed.Id("group/2"),BranchId=FootballDemoSeed.Id("branch/1"),CoachMembershipId=FootballDemoSeed.Id("member/coach3"),Kind=i==0?"Complaint":"Suggestion",Message=i==0?"شكوى اصطناعية لمتابعة إشعار تغيير الحصة.":"اقتراح اصطناعي لمشاركة أهداف الأسبوع.",PeriodMonth=new(2026,10,1),Status="Open",DueOn=new(2026,10,10),DueSetByUserId=FootballDemoSeed.Id("user/owner"),DueSetAtUtc=at,IdempotencyKey=$"feedback-followup-demo/{i}",CreatedAtUtc=at,UpdatedAtUtc=at});
        }
        await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
