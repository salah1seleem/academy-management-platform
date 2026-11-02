using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static partial class PrivateCompensationEndpoints
{
    private sealed record FinalPlan(Prepared Source, string Hash, bool Allowed, DateOnly? ContractEnd, Guid? ContractVersionId);
    private static async Task<FinalPlan?> PrepareFinal(Guid coach, Guid delivery, TenantMembership t, FoundationDbContext db, TimeProvider clock)
    {
        var p = await Prepare(coach,delivery,t,db,clock); if(p is null)return null;
        var employee = await db.Employees.AsNoTracking().SingleAsync(e=>e.Id==p.Delivery.EmployeeId);
        var contract = await ContractHistory.Bounds(db,employee,p.DueDate);
        // A stopped account alone is NOT evidence of ended employment. No next-month salary is fabricated.
        var allowed = contract.ContractEnd < Month(p.DueDate) && p.Action=="ReviewPending" && p.ReviewId.HasValue
            && !p.Assessment.RequiresReview && p.Assessment.PendingDelta>0;
        var hash=StatementComparisonRules.Hash(new{p.QuoteHash,kind="PrivateFinal",allowed,contract.ContractEnd,contract.VersionId});
        return new(p,hash,allowed,contract.ContractEnd,contract.VersionId);
    }
    private static async Task<IResult> FinalPreview(Guid coachId,Guid deliveryId,CurrentTenant current,FoundationDbContext db,TimeProvider clock,HttpResponse response)
    {
        var t=(await current.ResolveAsync())!; if(t.Role!=AcademyRole.AcademyOwner)return Results.Forbid(); response.Headers.CacheControl="no-store";
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var p=await PrepareFinal(coachId,deliveryId,t,db,clock);if(p is null)return Results.NotFound();
        return Results.Ok(new{quoteHash=p.Hash,canFinalize=p.Allowed,contractEnd=p.ContractEnd,difference=p.Source.Assessment.PendingDelta,currency=p.Source.Assessment.Currency,
            message=p.Allowed?"استحقاق عمولة موجب بعد انتهاء العقد، مستقل عن راتب الشهر. لا يغير كشفًا سابقًا أو يسجل صرفًا أو يقاص دينًا خارجيًا."
                :"يلزم عقد انتهى قبل الشهر الحالي واستثناء محفوظ بفرق موجب محسوب. الفرق السالب أو تعارض المصدر يحتاج تسوية مراجعة أخرى، ولا ينشئ راتبًا خارج العقد."});
    }
    private static async Task<IResult> FinalizePrivate(Guid coachId,Guid deliveryId,PrivateFinalWrite body,HttpContext http,CurrentTenant current,FoundationDbContext db,TimeProvider clock)
    {
        var key=http.Request.Headers["Idempotency-Key"].ToString();
        if(key.Length is <8 or >100||body.QuoteHash?.Length!=64||body.Note?.Length>500)return Results.Problem(statusCode:422,title:"راجع استحقاق نهاية العقد قبل التأكيد.");
        var t=(await current.ResolveAsync())!;await using var tx=await db.Database.BeginTransactionAsync();
        if(!await PrivateServiceEndpoints.Authority(http,current,db)||!await PayrollSensitiveAccess.Allowed(db,t,AcademyPermissions.PayrollStatementApprove))return Results.Forbid();
        var d=await db.PrivateServiceDeliveries.AsNoTracking().SingleOrDefaultAsync(d=>d.Id==deliveryId&&d.CoachMembershipId==coachId);if(d is null)return Results.NotFound();
        await PayrollEndpoints.Lock(db,t.AcademyId,d.EmployeeId,d.ServiceDate);
        var requestHash=StatementComparisonRules.Hash(new{coachId,deliveryId,kind="PrivateFinal",body});
        var replay=await db.PrivateCompensationEntries.SingleOrDefaultAsync(e=>e.ActorMembershipId==t.MembershipId&&e.RequestKey==key);
        if(replay is not null)return replay.RequestHash==requestHash?Results.Ok(new{replay.Id,replay.FinalStatementId,replay=true}):Results.Conflict();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"private-package:{t.AcademyId}:{d.PackageId}"},0))");
        var document=await db.PrivateServicePackages.Where(p=>p.Id==d.PackageId).Select(p=>p.DocumentId).SingleAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"receivable-document:{t.AcademyId}:{document}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"attendance:{t.AcademyId:N}:{d.TrainingSessionId:N}:{Guid.Empty:N}"},0))");
        var plan=await PrepareFinal(coachId,deliveryId,t,db,clock);
        if(plan is null)return Results.NotFound();
        if(!plan.Allowed||plan.Hash!=body.QuoteHash)return Results.Problem(statusCode:409,title:"تغير العقد أو المراجعة أو فرق العمولة؛ أعد المعاينة قبل الاعتماد.");
        var p=plan.Source;var employee=await db.Employees.SingleAsync(e=>e.Id==d.EmployeeId);var now=clock.GetUtcNow();
        var actor=Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);var amount=p.Assessment.PendingDelta!.Value;
        var statement=new PayrollStatement{Id=Guid.NewGuid(),AcademyId=t.AcademyId,EmployeeId=employee.Id,BranchId=employee.BranchId,EmployeeName=employee.Name,Currency=employee.Currency,Kind="PrivateFinal",PrivateReviewId=p.ReviewId,Month=Month(p.DueDate),NetDue=amount,PrivateCompensation=amount,
            SourceSnapshot=JsonSerializer.Serialize(new{kind="PrivateFinal",reviewEntryId=p.ReviewId,deliveryId,p.FactsHash,contractEnd=plan.ContractEnd,contractVersionId=plan.ContractVersionId}),SourceHash=plan.Hash,ApprovedByMembershipId=t.MembershipId,ActorUserId=actor,Note=body.Note?.Trim(),RequestKey=key,RequestHash=requestHash,CreatedAtUtc=now,UpdatedAtUtc=now};
        var entry=new PrivateCompensationEntry{Id=Guid.NewGuid(),AcademyId=t.AcademyId,DeliveryId=deliveryId,EmployeeId=d.EmployeeId,BranchId=d.BranchId,SportId=d.SportId,Revision=p.Revision+1,Kind="PrivateFinal",PostedBefore=p.Assessment.PostedAmount,TargetAmount=p.Assessment.Earned,Delta=amount,FinalStatementId=statement.Id,ReviewEntryId=p.ReviewId,DueDate=p.DueDate,Currency=p.Assessment.Currency,CutoffAtUtc=now,SourceSnapshot=p.Snapshot,FactsHash=p.FactsHash,QuoteHash=plan.Hash,RequestHash=requestHash,RequestKey=key,ActorMembershipId=t.MembershipId,ActorUserId=actor,CreatedAtUtc=now,UpdatedAtUtc=now};
        db.PayrollStatements.Add(statement);db.PrivateCompensationEntries.Add(entry);
        await db.SaveChangesAsync();await tx.CommitAsync();
        return Results.Created(http.Request.Path,new{entry.Id,entry.FinalStatementId,entry.Delta});
    }
}
public sealed record PrivateFinalWrite(string QuoteHash,string? Note=null);
