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
    public static void MapPrivateCompensation(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/manage/payroll/coaches/{coachId:guid}/private-services/{deliveryId:guid}").RequireAuthorization(AcademyPermissions.PayrollRead);
        api.MapGet("/posting-preview", Preview);
        api.MapGet("/entries", History);
        api.MapPost("/entries", Post).AddEndpointFilter<CsrfFilter>();
        api.MapGet("/correction-preview", CorrectionPreview);
        api.MapPost("/corrections", Correct).AddEndpointFilter<CsrfFilter>();
    }
    private static DateOnly Month(DateOnly date) => new(date.Year,date.Month,1);
    private sealed record Prepared(PrivateServiceDelivery Delivery, PrivateAssessment Assessment, DateOnly DueDate, string FactsHash, string Snapshot, string QuoteHash, string Action, string Message, int Revision, Guid? ReviewId);
    private static async Task<Prepared?> Prepare(Guid coach, Guid delivery, TenantMembership t, FoundationDbContext db, TimeProvider time)
    {
        var d = await db.PrivateServiceDeliveries.AsNoTracking().SingleOrDefaultAsync(d => d.Id == delivery && d.CoachMembershipId == coach);
        if (d is null || !await db.Employees.AnyAsync(e => e.Id == d.EmployeeId && e.IsActive)) return null;
        var a = (await PrivateServiceEndpoints.Assessments(db,coach,delivery)).Single();
        var entries = await db.PrivateCompensationEntries.AsNoTracking().Where(e => e.DeliveryId == delivery).OrderBy(e => e.Revision).ToArrayAsync();
        var movements = await db.ReceivableMovements.AsNoTracking().Where(m => m.DocumentId == a.DocumentId).OrderBy(m => m.Id).Select(m => new { m.Id,m.Kind,m.Amount,m.Currency,m.CollectionId,m.CreatedAtUtc }).ToArrayAsync();
        var credits = await db.ReceivableCreditApplications.AsNoTracking().Where(c => c.SourceDocumentId == a.DocumentId || c.TargetDocumentId == a.DocumentId).OrderBy(c => c.Id).Select(c => new { c.Id,c.SourceDocumentId,c.TargetDocumentId,c.Amount,c.Currency,c.CreatedAtUtc }).ToArrayAsync();
        var facts = new { schema = 1, t.AcademyId, delivery = a.Id, a.PackageId,a.DocumentId,a.TrainingSessionId,a.ServiceDate,a.Ordinal,a.SessionCount,a.AgreementId,a.VariableMethod,a.VariableValue,a.ServiceNet,a.Eligible,a.Earned,a.Contingent,a.RequiresReview,a.Currency, movements, credits };
        var snapshot = JsonSerializer.Serialize(facts); var factsHash = StatementComparisonRules.Hash(facts);
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId,db,time); var revision = entries.LastOrDefault()?.Revision ?? 0;
        var review = entries.LastOrDefault(e => e.Kind == "Review" && e.FactsHash == factsHash && e.PostedBefore == a.PostedAmount && !entries.Any(r => r.ReviewEntryId == e.Id));
        var paid = await PayrollEndpoints.Finalized(db,t.AcademyId,d.EmployeeId,today);
        var delta = a.PendingDelta;
        var canReverse = delta < 0 && review is not null && !paid && entries.Where(e => e.PayrollAdjustmentId.HasValue).All(e => Month(e.DueDate) == Month(today));
        var action = a.RequiresReview || delta < 0 ? review is null ? "Review" : canReverse ? "Reversal" : "ReviewPending" : delta > 0 ? paid ? "Blocked" : "Accrual" : "None";
        var message = action switch {
            "Accrual" => "ترحيل فرق العمولة إلى كشف الشهر الحالي فقط؛ لا سداد أو قبض أو مصروف مكرر.",
            "Review" => "حفظ استثناء للمراجعة دون تغيير الرواتب؛ انخفاض الاستحقاق أو تعارض التنفيذ لا يخصم تلقائيًا.",
            "Reversal" => "عكس صريح لجزء من عمولة هذا الشهر غير المسددة فقط؛ تبقى الحركات الأصلية، ولا تمس الأساسي أو شهرًا مسددًا.",
            "ReviewPending" => "الاستثناء محفوظ بلا خصم. إذا كان الفرق محسوبًا، يمكن للمالك مراجعة تصحيح عمولة مستقل في شهر راتب غير معتمد؛ لا إعادة كتابة كشف سابق أو مقاصة دين خارجي.",
            "Blocked" => "كشف الشهر الحالي معتمد أو مسدد؛ لا نعيد كتابته أو ننقل الفرق لشهر مستقبلي صامتًا.",
            _ => "لا فرق جديد لترحيله؛ المصدر السابق محفوظ دون تكرار." };
        var hash = StatementComparisonRules.Hash(new { t.AcademyId,t.MembershipId,delivery,factsHash,revision,a.PostedAmount,dueDate = today,action, reviewId = review?.Id });
        return new(d,a,today,factsHash,snapshot,hash,action,message,revision,review?.Id);
    }
    private static object View(Prepared p, DateTimeOffset asOf) => new { quoteHash = p.QuoteHash, action = p.Action, message = p.Message, dueDate = p.DueDate, asOf, currency = p.Assessment.Currency, currentPosted = p.Assessment.PostedAmount, targetAmount = p.Assessment.Earned, difference = p.Assessment.PendingDelta, p.Assessment.RequiresReview, revision = p.Revision };
    private static async Task<IResult> Preview(Guid coachId, Guid deliveryId, CurrentTenant current, FoundationDbContext db, TimeProvider time, HttpResponse response)
    {
        var t = (await current.ResolveAsync())!; if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid(); response.Headers.CacheControl = "no-store";
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var p = await Prepare(coachId,deliveryId,t,db,time); return p is null ? Results.NotFound() : Results.Ok(View(p,time.GetUtcNow()));
    }
    private static async Task<IResult> History(Guid coachId, Guid deliveryId, CurrentTenant current, FoundationDbContext db, HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        if (!await db.PrivateServiceDeliveries.AnyAsync(d => d.Id == deliveryId && d.CoachMembershipId == coachId)) return Results.NotFound();
        return Results.Ok(await db.PrivateCompensationEntries.AsNoTracking().Where(e => e.DeliveryId == deliveryId).OrderByDescending(e => e.Revision).Select(e => new { e.Id,e.Revision,e.Kind,e.PostedBefore,e.TargetAmount,e.Delta,e.Currency,e.DueDate,e.CutoffAtUtc,e.PayrollAdjustmentId,e.ReviewEntryId, actor = db.AcademyMemberships.Where(m => m.Id == e.ActorMembershipId).Select(m => m.User.DisplayName).First() }).ToArrayAsync());
    }
    private static async Task<IResult> Post(Guid coachId, Guid deliveryId, PrivatePostingWrite body, HttpContext http, CurrentTenant current, FoundationDbContext db, TimeProvider time)
    {
        var t = (await current.ResolveAsync())!; var key = http.Request.Headers["Idempotency-Key"].ToString();
        if (key.Length is < 8 or > 100 || body.QuoteHash?.Length != 64 || body.Action is not ("Accrual" or "Review" or "Reversal")) return Results.Problem(statusCode:422,title:"راجع فرق العمولة من الشاشة قبل التأكيد.");
        await using var tx = await db.Database.BeginTransactionAsync(); if (!await PrivateServiceEndpoints.Authority(http,current,db)) return Results.Forbid();
        var d = await db.PrivateServiceDeliveries.AsNoTracking().SingleOrDefaultAsync(d => d.Id == deliveryId && d.CoachMembershipId == coachId); if (d is null) return Results.NotFound();
        await PayrollEndpoints.Lock(db,t.AcademyId,d.EmployeeId,d.ServiceDate);
        var requestHash = StatementComparisonRules.Hash(new { coachId,deliveryId,body.Action,body.QuoteHash });
        var prior = await db.PrivateCompensationEntries.SingleOrDefaultAsync(e => e.ActorMembershipId == t.MembershipId && e.RequestKey == key);
        if (prior is not null) return prior.RequestHash == requestHash ? Results.Ok(new { prior.Id,replay = true }) : Results.Problem(statusCode:409,title:"معرف التأكيد مستخدم بمراجعة مختلفة.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"private-package:{t.AcademyId}:{d.PackageId}"},0))");
        var document = await db.PrivateServicePackages.Where(p => p.Id == d.PackageId).Select(p => p.DocumentId).SingleAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"receivable-document:{t.AcademyId}:{document}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"attendance:{t.AcademyId:N}:{d.TrainingSessionId:N}:{Guid.Empty:N}"},0))");
        var p = await Prepare(coachId,deliveryId,t,db,time); if (p is null) return Results.NotFound();
        if (p.QuoteHash != body.QuoteHash || p.Action != body.Action) return Results.Problem(statusCode:409,title:"تغيرت المصادر أو كشف الراتب؛ أعد مراجعة الفرق قبل التأكيد.");
        var delta = body.Action == "Review" ? 0 : p.Assessment.PendingDelta!.Value;
        var now = time.GetUtcNow(); var entryId = Guid.NewGuid(); var actor = Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        PayrollAdjustment? adjustment = null;
        if (body.Action != "Review")
        {
            adjustment = new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, EmployeeId = d.EmployeeId, Kind = delta > 0 ? "Addition" : "Deduction", DueDate = p.DueDate, Amount = Math.Abs(delta), Currency = p.Assessment.Currency, Reason = delta > 0 ? "استحقاق خاص — حصة معتمدة وتحصيل مخصص" : "عكس عمولة خاص غير مسددة بعد مراجعة المصدر", PerformedByUserId = actor, IdempotencyKey = "private-" + entryId.ToString("N"), CreatedAtUtc = now, UpdatedAtUtc = now };
            db.PayrollAdjustments.Add(adjustment);
        }
        var entry = new PrivateCompensationEntry { Id = entryId, AcademyId = t.AcademyId, DeliveryId = deliveryId, EmployeeId = d.EmployeeId, BranchId = d.BranchId, SportId = d.SportId, Revision = p.Revision + 1, Kind = body.Action, PostedBefore = p.Assessment.PostedAmount, TargetAmount = p.Assessment.Earned, Delta = delta, PayrollAdjustmentId = adjustment?.Id, ReviewEntryId = body.Action == "Reversal" ? p.ReviewId : null, DueDate = p.DueDate, Currency = p.Assessment.Currency, CutoffAtUtc = now, SourceSnapshot = p.Snapshot, FactsHash = p.FactsHash, QuoteHash = p.QuoteHash, RequestHash = requestHash, RequestKey = key, ActorMembershipId = t.MembershipId, ActorUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.PrivateCompensationEntries.Add(entry); await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Created(http.Request.Path,new { entry.Id,entry.Kind,entry.Delta,entry.PayrollAdjustmentId,postedBalance = entry.PostedBefore + entry.Delta });
    }
}
public sealed record PrivatePostingWrite(string Action,string QuoteHash);
