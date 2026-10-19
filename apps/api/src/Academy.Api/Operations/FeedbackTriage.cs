using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public static partial class GuardianFeedbackEndpoints
{
    internal static readonly string[] FeedbackSubjects = ["Coach", "Schedule", "Administration", "Facility", "Water", "Payment", "Other"];
    internal static readonly string[] FeedbackUrgencies = ["Routine", "Urgent", "Safety"];
    private static async Task<IResult> Triage(Guid id, FeedbackTriageWrite b, CurrentTenant tenant, FoundationDbContext db, HttpContext h, TimeProvider clock)
    {
        h.Response.Headers.CacheControl = "private, no-store";
        var t = (await tenant.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        var note = b.Note?.Trim();
        if (b.RequestId == Guid.Empty || b.Version == 0 || !FeedbackSubjects.Contains(b.Subject) || !FeedbackUrgencies.Contains(b.Urgency) || note?.Length > 500)
            return Results.UnprocessableEntity(new { message = "راجع الموضوع ودرجة الاستعجال. ملاحظة الفرز اختيارية حتى 500 حرف." });
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await LockOwner(db, t)) return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"feedback-case-request:{t.AcademyId}:{t.MembershipId}:{b.RequestId}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"guardian-feedback-resolve:{t.AcademyId}:{id}"},0))");
        var f = await db.GuardianFeedbacks.SingleOrDefaultAsync(f => f.AcademyId == t.AcademyId && f.Id == id && f.IsActive); if (f == null) return Results.NotFound();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id, b.Subject, b.Urgency, b.Version, note }))));
        var previous = await db.FeedbackChanges.SingleOrDefaultAsync(c => c.ActorMembershipId == t.MembershipId && c.RequestId == b.RequestId);
        if (previous != null) return previous.FeedbackId == id && previous.Action == "Triage" && previous.RequestHash == hash ? Results.Ok(new { id, replay = true }) : Results.Conflict(new { message = "مفتاح المحاولة مستخدم لبيانات أخرى." });
        if (f.Kind == "Rating" || f.Status is "Resolved" or "Closed") return Results.Conflict(new { message = "الفرز للشكاوى والاقتراحات المفتوحة فقط." });
        if (f.Version != b.Version) return Results.Conflict(new { message = "تغيرت الملاحظة؛ حدّثها قبل تأكيد الفرز." });
        var beforeSubject = f.Subject; var beforeUrgency = f.Urgency; var now = clock.GetUtcNow();
        f.Subject = b.Subject; f.Urgency = b.Urgency; f.TriagedByMembershipId = t.MembershipId; f.TriagedAtUtc = now; f.UpdatedAtUtc = now;
        var change = RecordChange(db, f, t, now, "Triage", f.Status, f.DueOn, string.IsNullOrEmpty(note) ? null : note, b.RequestId, hash);
        change.BeforeSubject = beforeSubject; change.AfterSubject = f.Subject; change.BeforeUrgency = beforeUrgency; change.AfterUrgency = f.Urgency;
        try { await db.SaveChangesAsync(); db.FeedbackChanges.Add(change); await db.SaveChangesAsync(); await tx.CommitAsync(); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { message = "تغيرت الملاحظة؛ حدّثها قبل تأكيد الفرز." }); }
        return Results.Ok(new { id });
    }
}
public sealed record FeedbackTriageWrite(string Subject, string Urgency, uint Version, Guid RequestId, string? Note = null);
