using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;

// Synthetic, repeatable Football Demo examples. Never used for a customer tenant.
internal static class GuardianFeedbackDemoSeed
{
    internal static async Task AddOnce(FoundationDbContext db, CancellationToken ct)
    {
        var academy = FootballDemoSeed.AcademyId;
        var first = FootballDemoSeed.Id("guardian-feedback/rating/0");
        if (await db.GuardianFeedbacks.AnyAsync(x => x.AcademyId == academy && x.Id == first, ct)) return;
        var time = DateTimeOffset.Parse("2026-10-08T06:00:00Z");
        var month = new DateOnly(2026, 10, 1);
        var entries = new (string key, int player, int guardian, string kind, int? score, string message)[]
        {
            ("rating/0", 0, 0, "Rating", 5, "تقييم اصطناعي: شرح واضح ومتابعة جيدة."),
            ("rating/1", 2, 1, "Rating", 4, "تقييم اصطناعي: التدريب منظم."),
            ("rating/2", 3, 2, "Rating", 4, "تقييم اصطناعي: تواصل جيد مع الأسرة."),
            ("complaint/0", 0, 0, "Complaint", null, "شكوى تجريبية: نحتاج إشعارًا أبكر عند تغيير الموعد."),
            ("suggestion/0", 2, 1, "Suggestion", null, "اقتراح تجريبي: مشاركة ملخص أهداف الأسبوع.")
        };
        foreach (var (key, player, guardian, kind, score, message) in entries)
            db.GuardianFeedbacks.Add(new GuardianFeedback { Id = FootballDemoSeed.Id("guardian-feedback/" + key), AcademyId = academy,
                PlayerId = FootballDemoSeed.Id($"player/{player}"), GuardianUserId = FootballDemoSeed.Id($"user/guardian{guardian + 1}"),
                BranchId = FootballDemoSeed.Id("branch/0"), TrainingGroupId = FootballDemoSeed.Id("group/0"), CoachMembershipId = FootballDemoSeed.Id("member/coach1"),
                Kind = kind, Score = score, Message = message, PeriodMonth = month, Status = "Open", IdempotencyKey = "demo-" + key,
                CreatedAtUtc = time, UpdatedAtUtc = time });
        await db.SaveChangesAsync(ct);
    }
}
