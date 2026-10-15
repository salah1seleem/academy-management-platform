using System.Security.Claims;
using System.Text.Json.Serialization;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;

public static class PayrollPayableCorrections
{
    internal static async Task<decimal> Due(FoundationDbContext db,PayrollStatement s)
        =>s.NetDue+(s.Kind=="Opening"?await db.PayrollPayableCorrections.Where(c=>c.StatementId==s.Id).SumAsync(c=>c.Delta):0);

    internal static string Quote(PayrollStatement s,PayrollPayableCorrection[] corrections,PayrollDisbursement[] payments)
        =>StatementComparisonRules.Hash(new{source=s.Id,s.SourceHash,s.NetDue,corrections=corrections.OrderBy(c=>c.Revision).Select(c=>new{c.Id,c.Revision,c.Delta,c.TargetAmount}),payments=payments.OrderBy(p=>p.Sequence).Select(p=>new{p.Id,p.Amount,p.AdvanceId})});

    internal static async Task<IResult> Correct(Guid id,PayrollOpeningCorrection body,HttpContext http,CurrentTenant current,FoundationDbContext db,TimeProvider clock)
    {
        var key=http.Request.Headers["Idempotency-Key"].ToString();
        if(key.Length is <8 or >100||body.TargetAmount<0||body.TargetAmount>100000000||decimal.Round(body.TargetAmount,2)!=body.TargetAmount||string.IsNullOrWhiteSpace(body.Reference)||body.Reference.Trim().Length is <3 or >120||body.Note?.Length>500||body.QuoteHash?.Length!=64)
            return Results.Problem(statusCode:422,title:"أدخل إجمالي الافتتاح الصحيح ومرجع مستند المراجعة.");
        var t=(await current.ResolveAsync())!;await using var tx=await db.Database.BeginTransactionAsync();
        if(t.Role!=AcademyRole.AcademyOwner||!await PayrollSensitiveAccess.LockAndAuthorize(http,current,db,AcademyPermissions.PayrollStatementApprove))return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"opening-correction:{t.AcademyId}:{t.MembershipId}:{key}"},0))");
        await PayrollEndpoints.Lock(db,t.AcademyId,id,default);
        var s=await db.PayrollStatements.SingleOrDefaultAsync(s=>s.EmployeeId==id&&s.Kind=="Opening");if(s is null)return Results.NotFound();
        if(!await PayrollSensitiveAccess.BranchAllowed(db,t,s.BranchId))return Results.Forbid();
        var hash=StatementComparisonRules.Hash(new{id,body,kind="OpeningCorrection"});
        var replay=await db.PayrollPayableCorrections.SingleOrDefaultAsync(c=>c.ActorMembershipId==t.MembershipId&&c.RequestKey==key);
        if(replay is not null)return replay.RequestHash==hash?Results.Ok(new{replay.Id,replay=true}):Results.Conflict();
        var corrections=await db.PayrollPayableCorrections.Where(c=>c.StatementId==s.Id).ToArrayAsync();var payments=await db.PayrollDisbursements.Where(p=>p.StatementId==s.Id).ToArrayAsync();
        var previous=s.NetDue+corrections.Sum(c=>c.Delta);var paid=payments.Sum(p=>p.Amount);
        if(body.QuoteHash!=Quote(s,corrections,payments))return Results.Problem(statusCode:409,title:"تغير الرصيد أو السداد؛ حدّث المراجعة قبل التصحيح.");
        if(body.TargetAmount==previous||body.TargetAmount<paid)return Results.Problem(statusCode:422,title:"التصحيح يجب أن يغيّر الإجمالي وألا يقل عن المسدد؛ لا يمكن إلغاء دفعة أو إنشاء دين تلقائيًا.");
        var now=clock.GetUtcNow();var c=new PayrollPayableCorrection{Id=Guid.NewGuid(),AcademyId=t.AcademyId,StatementId=s.Id,EmployeeId=id,BranchId=s.BranchId,Currency=s.Currency,Revision=corrections.Length+1,PreviousAmount=previous,TargetAmount=body.TargetAmount,Delta=body.TargetAmount-previous,PaidAtApproval=paid,ActorMembershipId=t.MembershipId,ActorUserId=Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!),Reference=body.Reference.Trim(),Note=body.Note?.Trim(),QuoteHash=body.QuoteHash!,RequestKey=key,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};
        db.PayrollPayableCorrections.Add(c);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created(http.Request.Path,new{c.Id,c.Delta,c.TargetAmount,remaining=c.TargetAmount-paid});
    }
}
public sealed record PayrollOpeningCorrection([property:JsonRequired] decimal TargetAmount,string Reference,string QuoteHash,string? Note=null);
