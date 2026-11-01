using Academy.Api.Auth;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class GroupScheduleNotices
{
    private static string Time(TimeOnly value) => $"{(value.Hour % 12 == 0 ? 12 : value.Hour % 12)}:{value.Minute:00} {(value.Hour < 12 ? "صباحًا" : "مساءً")}";
    public static string Description(TimetableWrite b) => string.Join("، ", b.Days.OrderBy(d => ((int)d + 1) % 7).Select(d => new[] { "الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت" }[(int)d])) + $" · من {Time(b.StartTime)} إلى {Time(b.EndTime)}";

    public static async Task Add(FoundationDbContext db, TenantMembership tenant, TrainingGroup group,
        TimetableWrite body, Guid revision, DateTimeOffset now)
    {
        var academy = tenant.AcademyId;
        var players = await db.SportEnrollments.Where(e => e.AcademyId == academy && e.IsActive && e.Player.IsActive
            && FoundationDbContext.AssignedGroupAt(e.AcademyId, e.Id, body.From) == group.Id).Select(e => e.PlayerId).Distinct().ToArrayAsync();
        var parents = await db.GuardianPlayerLinks.Where(l => l.AcademyId == academy && l.IsActive && players.Contains(l.PlayerId)
            && l.Guardian.IsActive && l.Guardian.User.IsActive && db.AcademyMemberships.Any(m => m.AcademyId == academy && m.UserId == l.Guardian.UserId && m.Role == AcademyRole.Guardian && m.IsActive))
            .Select(l => l.Guardian.UserId).Distinct().ToArrayAsync();
        var coaches = await db.StaffGroupAssignments.Where(a => a.AcademyId == academy && a.TrainingGroupId == group.Id && a.IsActive
            && a.AcademyMembership.IsActive && a.AcademyMembership.User.IsActive && a.AcademyMembership.Role == AcademyRole.Coach)
            .Select(a => a.AcademyMembership.UserId).Distinct().ToArrayAsync();
        if (parents.Length == 0 && coaches.Length == 0) return;
        var message = "تم تحديث مواعيد مجموعة " + group.ArabicName + ": " + Description(body) + $". يبدأ الجدول في {body.From:yyyy/MM/dd}.";
        db.AudienceAnnouncements.Add(new AudienceAnnouncement { Id = revision, AcademyId = academy, TrainingGroupId = group.Id,
            ActorMembershipId = tenant.MembershipId, RequestId = revision, Message = message, PlayerCount = players.Length,
            RecipientCount = parents.Length, CreatedAtUtc = now, UpdatedAtUtc = now });
        foreach (var recipient in parents.Select(id => (Id: id, Role: "Guardian")).Concat(coaches.Select(id => (Id: id, Role: "Coach"))))
        {
            var notice = new AcademyNotice { Id = Guid.NewGuid(), AcademyId = academy, UserId = recipient.Id, AudienceRole = recipient.Role,
                EventKey = "GroupAnnouncement", TargetKind = "GroupAnnouncement", TargetId = revision, Message = message,
                CreatedAtUtc = now, UpdatedAtUtc = now };
            db.AcademyNotices.Add(notice); await MobilePushEndpoints.Queue(db, notice, now);
        }
    }
}
