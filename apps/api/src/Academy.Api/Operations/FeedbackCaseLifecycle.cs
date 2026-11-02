using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Academy.Api.Auth;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static partial class GuardianFeedbackEndpoints
{
    internal static readonly string[] CaseStatuses = ["Open", "Reviewed", "InProgress", "AwaitingInfo", "Resolved", "Closed", "Reopened"];
    public static bool CanTransition(string from, string to) => (from, to) switch
    {
        ("Open" or "Reviewed" or "Reopened", "InProgress") => true,
        ("InProgress", "AwaitingInfo") => true,
        ("AwaitingInfo", "InProgress") => true,
        ("Resolved", "Closed") => true,
        ("Resolved" or "Closed", "Reopened") => true,
        _ => false
    };

    private static async Task<bool> LockOwner(FoundationDbContext db, TenantMembership t)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"},0))");
        return await db.AcademyMemberships.AnyAsync(m => m.AcademyId == t.AcademyId && m.Id == t.MembershipId && m.IsActive && m.User.IsActive && m.Role == AcademyRole.AcademyOwner
            && (!m.PrimaryMembershipId.HasValue || db.AcademyMemberships.Any(p => p.Id == m.PrimaryMembershipId && p.IsActive && p.User.IsActive)));
    }

    private static FeedbackChange RecordChange(FoundationDbContext db, GuardianFeedback f, TenantMembership t, DateTimeOffset now,
        string action, string before, DateOnly? beforeDue, string? note = null, Guid? requestId = null, string? hash = null)
    {
        var c = new FeedbackChange { Id = Guid.NewGuid(), AcademyId = t.AcademyId, FeedbackId = f.Id, ActorMembershipId = t.MembershipId,
            Action = action, FromStatus = before, ToStatus = f.Status, BeforeDueOn = beforeDue, AfterDueOn = f.DueOn,
            // Snapshot an existing reply on reopen as well: legacy replies have no
            // invented historical event, and must survive a later second resolution.
            InternalNote = note, PublicResponse = action == "Resolve" || action == "Transition" && f.Status == "Reopened" ? f.OwnerResponse : null,
            RequestId = requestId, RequestHash = hash, CreatedAtUtc = now, UpdatedAtUtc = now };
        return c;
    }

    private static async Task<IResult> Transition(Guid id, FeedbackCaseWrite body, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        h.Response.Headers.CacheControl = "private, no-store";
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        var note = body.InternalNote?.Trim();
        if (body.RequestId == Guid.Empty || body.Version == 0 || note?.Length > 500 || !CaseStatuses.Contains(body.Status))
            return Results.UnprocessableEntity(new { message = "راجع حالة الملاحظة؛ الملاحظة الداخلية اختيارية حتى 500 حرف." });
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await LockOwner(db, t)) return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"feedback-case-request:{t.AcademyId}:{t.MembershipId}:{body.RequestId}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"guardian-feedback-resolve:{t.AcademyId}:{id}"},0))");
        var item = await db.GuardianFeedbacks.SingleOrDefaultAsync(f => f.AcademyId == t.AcademyId && f.Id == id && f.IsActive);
        if (item == null) return Results.NotFound();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id, body.Status, body.Version, note }))));
        var prior = await db.FeedbackChanges.SingleOrDefaultAsync(c => c.AcademyId == t.AcademyId && c.ActorMembershipId == t.MembershipId && c.RequestId == body.RequestId);
        if (prior != null) return prior.FeedbackId == id && prior.RequestHash == hash ? Results.Ok(new { id, replay = true }) : Results.Conflict(new { message = "مفتاح المحاولة مستخدم لتحديث مختلف." });
        if (item.Kind == "Rating") return Results.UnprocessableEntity(new { message = "مراحل التحقيق للشكاوى والاقتراحات فقط، وليست لتغيير تقييم المدرب." });
        if (item.Version != body.Version) return Results.Conflict(new { message = "تغيرت الملاحظة؛ حدّث تفاصيلها قبل الحفظ." });
        if (!CanTransition(item.Status, body.Status)) return Results.Conflict(new { message = "هذا الانتقال غير متاح. الحل يتطلب ردًا للأسرة، والإغلاق بعد الحل فقط." });
        var now = clock.GetUtcNow(); var before = item.Status; item.Status = body.Status; item.UpdatedAtUtc = now;
        if (body.Status == "Reopened") { item.TriagedAtUtc = null; item.TriagedByMembershipId = null; }
        var change = RecordChange(db, item, t, now, "Transition", before, item.DueOn, string.IsNullOrEmpty(note) ? null : note, body.RequestId, hash);
        try
        {
            await db.SaveChangesAsync(); db.FeedbackChanges.Add(change); await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { message = "تغيرت الملاحظة؛ حدّث تفاصيلها قبل الحفظ." }); }
        return Results.Ok(new { id });
    }
}
public sealed record FeedbackCaseWrite(string Status, uint Version, Guid RequestId, string? InternalNote = null);
