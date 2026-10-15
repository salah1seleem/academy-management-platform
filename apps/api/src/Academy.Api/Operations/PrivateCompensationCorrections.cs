using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static partial class PrivateCompensationEndpoints
{
    private sealed record CorrectionPlan(Prepared Source, DateOnly Month, string QuoteHash, bool CanCorrect, string Message);

    private static async Task<CorrectionPlan?> PrepareCorrection(Guid coach, Guid delivery, DateOnly? requested, TenantMembership t, FoundationDbContext db, TimeProvider clock)
    {
        var p = await Prepare(coach, delivery, t, db, clock);
        if (p is null) return null;
        var currentMonth = Month(p.DueDate);
        var month = requested ?? currentMonth;
        // Suggest an available payroll month; the user still explicitly confirms it.
        if (!requested.HasValue)
            while (month < currentMonth.AddMonths(12) && await PayrollEndpoints.Finalized(db, t.AcademyId, p.Delivery.EmployeeId, month)) month = month.AddMonths(1);
        var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == p.Delivery.EmployeeId);
        var validDate = month.Day == 1 && month >= currentMonth && month <= currentMonth.AddMonths(12);
        var finalized = validDate && await PayrollEndpoints.Finalized(db, t.AcademyId, employee.Id, month);
        var terms = await ContractHistory.Bounds(db,employee,month);
        var contract = validDate && terms.ContractStart < month.AddMonths(1) && (!terms.ContractEnd.HasValue || terms.ContractEnd >= month);
        var can = p.Action == "ReviewPending" && p.ReviewId.HasValue && !p.Assessment.RequiresReview && p.Assessment.PendingDelta.HasValue && p.Assessment.PendingDelta != 0 && validDate && !finalized && contract;
        var message = !validDate ? "اختر أول يوم من شهر الراتب الحالي أو أحد الأشهر الاثني عشر التالية."
            : finalized ? "كشف الشهر المختار معتمد أو مسدد؛ اختر شهرًا غير معتمد، دون تعديل الكشف الأصلي."
            : !contract ? "العقد لا يغطي الشهر المختار؛ يلزم مسار تسوية نهاية الخدمة، ولا ننشئ راتبًا خارج مدة العمل."
            : !can ? "يلزم استثناء محفوظ وفرق عمولة محسوب غير صفري بلا تعارض مصادر؛ أعد مراجعة الاستحقاق."
            : "تصحيح صريح لعمولة الخاص فقط في الشهر المختار. يحفظ المراجعة والمصدر، ولا يغير الأساسي أو الكشف المعتمد أو يسجل دفعة أو يقاص دينًا خارجيًا.";
        var hash = StatementComparisonRules.Hash(new { t.AcademyId, t.MembershipId, sourceQuote = p.QuoteHash, month, can });
        return new(p, month, hash, can, message);
    }

    private static async Task<IResult> CorrectionPreview(Guid coachId, Guid deliveryId, DateOnly? month, CurrentTenant current, FoundationDbContext db, TimeProvider clock, HttpResponse response)
    {
        var t = (await current.ResolveAsync())!;
        if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        response.Headers.CacheControl = "no-store";
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var p = await PrepareCorrection(coachId, deliveryId, month, t, db, clock);
        return p is null ? Results.NotFound() : Results.Ok(new { p.Month, p.QuoteHash, p.CanCorrect, p.Message, reviewEntryId = p.Source.ReviewId, currency = p.Source.Assessment.Currency, difference = p.Source.Assessment.PendingDelta });
    }

    private static async Task<IResult> Correct(Guid coachId, Guid deliveryId, PrivateCorrectionWrite body, HttpContext http, CurrentTenant current, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await current.ResolveAsync())!;
        var key = http.Request.Headers["Idempotency-Key"].ToString();
        if (key.Length is < 8 or > 100 || body.QuoteHash?.Length != 64 || body.Note?.Length > 500)
            return Results.Problem(statusCode: 422, title: "راجع تصحيح العمولة والشهر قبل التأكيد؛ الملاحظة اختيارية حتى 500 حرف.");
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await PrivateServiceEndpoints.Authority(http, current, db)) return Results.Forbid();
        var d = await db.PrivateServiceDeliveries.AsNoTracking().SingleOrDefaultAsync(d => d.Id == deliveryId && d.CoachMembershipId == coachId);
        if (d is null) return Results.NotFound();
        await PayrollEndpoints.Lock(db, t.AcademyId, d.EmployeeId, body.Month);
        var requestHash = StatementComparisonRules.Hash(new { coachId, deliveryId, kind = "Correction", body.Month, body.QuoteHash, body.Note });
        var prior = await db.PrivateCompensationEntries.SingleOrDefaultAsync(e => e.ActorMembershipId == t.MembershipId && e.RequestKey == key);
        if (prior is not null) return prior.RequestHash == requestHash ? Results.Ok(new { prior.Id, replay = true }) : Results.Problem(statusCode: 409, title: "معرف التأكيد مستخدم بمراجعة مختلفة.");
        // Same lock order as original posting and receipt/attendance mutation consumers.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"private-package:{t.AcademyId}:{d.PackageId}"},0))");
        var document = await db.PrivateServicePackages.Where(p => p.Id == d.PackageId).Select(p => p.DocumentId).SingleAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"receivable-document:{t.AcademyId}:{document}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"attendance:{t.AcademyId:N}:{d.TrainingSessionId:N}:{Guid.Empty:N}"},0))");
        var plan = await PrepareCorrection(coachId, deliveryId, body.Month, t, db, clock);
        if (plan is null) return Results.NotFound();
        if (!plan.CanCorrect || plan.QuoteHash != body.QuoteHash)
            return Results.Problem(statusCode: 409, title: "تغيرت المراجعة أو المصادر أو شهر الراتب؛ أعد المعاينة قبل التأكيد.");
        var p = plan.Source;
        var delta = p.Assessment.PendingDelta!.Value;
        var now = clock.GetUtcNow();
        var id = Guid.NewGuid();
        var actor = Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var adjustment = new PayrollAdjustment { Id = Guid.NewGuid(), AcademyId = t.AcademyId, EmployeeId = d.EmployeeId, Kind = delta > 0 ? "Addition" : "Deduction", Amount = Math.Abs(delta), Currency = p.Assessment.Currency, DueDate = plan.Month, Reason = "تصحيح عمولة خاص بعد مراجعة المصدر", PerformedByUserId = actor, IdempotencyKey = "private-" + id.ToString("N"), CreatedAtUtc = now, UpdatedAtUtc = now };
        // Preserve original fact keys for verification; the correction is a separate source revision.
        var snapshot = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(p.Snapshot)!;
        snapshot["correction"] = JsonSerializer.SerializeToElement(new { reviewEntryId = p.ReviewId, month = plan.Month, note = body.Note?.Trim(), approvedAtUtc = now });
        var entry = new PrivateCompensationEntry { Id = id, AcademyId = t.AcademyId, DeliveryId = deliveryId, EmployeeId = d.EmployeeId, BranchId = d.BranchId, SportId = d.SportId, Revision = p.Revision + 1, Kind = "Correction", PostedBefore = p.Assessment.PostedAmount, TargetAmount = p.Assessment.Earned, Delta = delta, PayrollAdjustmentId = adjustment.Id, ReviewEntryId = p.ReviewId, DueDate = plan.Month, Currency = p.Assessment.Currency, CutoffAtUtc = now, SourceSnapshot = JsonSerializer.Serialize(snapshot), FactsHash = p.FactsHash, QuoteHash = plan.QuoteHash, RequestHash = requestHash, RequestKey = key, ActorMembershipId = t.MembershipId, ActorUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.PayrollAdjustments.Add(adjustment);
        db.PrivateCompensationEntries.Add(entry);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Created(http.Request.Path, new { entry.Id, entry.Kind, entry.Delta, entry.DueDate, entry.ReviewEntryId, entry.PayrollAdjustmentId });
    }
}

public sealed record PrivateCorrectionWrite(DateOnly Month, string QuoteHash, string? Note = null);
