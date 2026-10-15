using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;

public static class PayrollAdvances
{
    public static void MapPayrollAdvances(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1/manage/payroll/{id:guid}/advances").RequireAuthorization(AcademyPermissions.PayrollRead);
        api.MapGet("",Read);
        api.MapPost("",Record).RequireAuthorization(AcademyPermissions.PayrollManage,AcademyPermissions.PayrollPay,AcademyPermissions.PayrollAdvanceManage).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/{advanceId:guid}/applications",Apply).RequireAuthorization(AcademyPermissions.PayrollManage,AcademyPermissions.PayrollPay,AcademyPermissions.PayrollAdvanceManage).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Read(Guid id,CurrentTenant current,FoundationDbContext db,HttpResponse response)
    {
        response.Headers.CacheControl="no-store";var t=(await current.ResolveAsync())!;
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var e=await db.Employees.AsNoTracking().SingleOrDefaultAsync(e=>e.Id==id);if(e is null)return Results.NotFound();
        var advances=await db.PayrollAdvances.AsNoTracking().Where(a=>a.EmployeeId==id).OrderByDescending(a=>a.CreatedAtUtc).ToArrayAsync();
        var payments=await db.PayrollDisbursements.AsNoTracking().Where(p=>p.EmployeeId==id).ToArrayAsync();
        var statements=await db.PayrollStatements.AsNoTracking().Where(s=>s.EmployeeId==id).OrderByDescending(s=>s.Month).ToArrayAsync();
        var canManage=await PayrollSensitiveAccess.Allowed(db,t,AcademyPermissions.PayrollAdvanceManage)&&await PayrollSensitiveAccess.Allowed(db,t,AcademyPermissions.PayrollPay)&&(t.Role==AcademyRole.AcademyOwner||!await PayrollSensitiveAccess.IsSelf(db,t,e.MembershipId));
        return Results.Ok(new {employeeName=e.Name,e.Currency,canManage,canRecord=canManage&&e.IsActive,
            advances=advances.Select(a=>new{a.Id,a.Amount,a.Reference,a.CreatedAtUtc,applied=payments.Where(p=>p.AdvanceId==a.Id).Sum(p=>p.Amount),remaining=a.Amount-payments.Where(p=>p.AdvanceId==a.Id).Sum(p=>p.Amount)}),
            statements=statements.Select(s=>new{s.Month,s.Kind,s.OpeningAsOf,s.NetDue,remaining=s.NetDue-payments.Where(p=>p.StatementId==s.Id).Sum(p=>p.Amount)}),
            applications=payments.Where(p=>p.AdvanceId.HasValue).Select(p=>new{p.Id,p.AdvanceId,p.Amount,p.CreatedAtUtc,month=statements.Single(s=>s.Id==p.StatementId).Month,kind=statements.Single(s=>s.Id==p.StatementId).Kind}),
            balanceScope="RecordedAdvancesOnly",openingBalanceVerified=await db.PayrollStatements.AnyAsync(s=>s.EmployeeId==id&&s.Kind=="Opening")});
    }
    private static bool Amount(decimal v)=>v>0&&v<=100000000&&decimal.Round(v,2)==v;
    private static string Key(HttpContext c)=>c.Request.Headers["Idempotency-Key"].ToString();
    private static async Task<bool> Authority(HttpContext http,CurrentTenant current,FoundationDbContext db)
        =>await PayrollSensitiveAccess.LockAndAuthorize(http,current,db,AcademyPermissions.PayrollAdvanceManage)&&await PayrollSensitiveAccess.LockAndAuthorize(http,current,db,AcademyPermissions.PayrollPay);
    private static async Task<IResult> Record(Guid id,PayrollAdvanceWrite body,HttpContext http,CurrentTenant current,FoundationDbContext db,TimeProvider clock)
    {
        var key=Key(http);if(key.Length is <8 or >100||!Amount(body.Amount)||body.Reference?.Length>120)return Results.Problem(statusCode:422,title:"أدخل مبلغ السلفة الصحيح.");
        var t=(await current.ResolveAsync())!;await using var tx=await db.Database.BeginTransactionAsync();if(!await Authority(http,current,db))return Results.Forbid();await PayrollEndpoints.Lock(db,t.AcademyId,id,default);
        var e=await db.Employees.SingleOrDefaultAsync(e=>e.Id==id&&e.IsActive);if(e is null)return Results.NotFound();if(!await PayrollSensitiveAccess.BranchAllowed(db,t,e.BranchId)||t.Role!=AcademyRole.AcademyOwner&&await PayrollSensitiveAccess.IsSelf(db,t,e.MembershipId))return Results.Forbid();
        var hash=StatementComparisonRules.Hash(new{id,body});var replay=await db.PayrollAdvances.SingleOrDefaultAsync(a=>a.ActorMembershipId==t.MembershipId&&a.RequestKey==key);if(replay is not null)return replay.RequestHash==hash?Results.Ok(new{replay.Id,replay=true}):Results.Conflict();
        var now=clock.GetUtcNow();var a=new PayrollAdvance {Id=Guid.NewGuid(),AcademyId=t.AcademyId,EmployeeId=id,BranchId=e.BranchId,Currency=e.Currency,Amount=body.Amount,Reference=body.Reference?.Trim(),ActorMembershipId=t.MembershipId,ActorUserId=Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!),RequestKey=key,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};
        db.PayrollAdvances.Add(a);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created(http.Request.Path,new{a.Id,a.Amount});
    }
    private static async Task<IResult> Apply(Guid id,Guid advanceId,PayrollAdvanceApply body,HttpContext http,CurrentTenant current,FoundationDbContext db,TimeProvider clock)
    {
        var key=Key(http);if(key.Length is <8 or >100||!Amount(body.Amount)||body.Month.Day!=1||body.Month.Year is <2000 or >9998)return Results.Problem(statusCode:422,title:"اختر كشفًا معتمدًا ومبلغ تخصيص صحيحًا.");
        var t=(await current.ResolveAsync())!;await using var tx=await db.Database.BeginTransactionAsync();if(!await Authority(http,current,db))return Results.Forbid();await PayrollEndpoints.Lock(db,t.AcademyId,id,body.Month);
        var e=await db.Employees.SingleOrDefaultAsync(e=>e.Id==id);if(e is null)return Results.NotFound();if(!await PayrollSensitiveAccess.BranchAllowed(db,t,e.BranchId)||t.Role!=AcademyRole.AcademyOwner&&await PayrollSensitiveAccess.IsSelf(db,t,e.MembershipId))return Results.Forbid();
        // Preserve hashes of existing monthly commands when extending the contract.
        var hash=body.Opening?StatementComparisonRules.Hash(new{id,advanceId,body}):StatementComparisonRules.Hash(new{id,advanceId,body=new{body.Month,body.Amount,body.ExpectedAdvanceRemaining,body.ExpectedStatementRemaining}});var replay=await db.PayrollDisbursements.SingleOrDefaultAsync(p=>p.ActorMembershipId==t.MembershipId&&p.RequestKey==key);if(replay is not null)return replay.RequestHash==hash?Results.Ok(new{replay.Id,replay=true}):Results.Conflict();
        var kind=body.Opening?"Opening":"MonthlySalary";
        var a=await db.PayrollAdvances.SingleOrDefaultAsync(a=>a.Id==advanceId&&a.EmployeeId==id);var s=await db.PayrollStatements.SingleOrDefaultAsync(s=>s.EmployeeId==id&&s.Month==body.Month&&s.Kind==kind);if(a is null||s is null)return Results.NotFound();
        if(a.BranchId!=s.BranchId||a.Currency!=s.Currency||await PayrollEndpoints.Locked(db,t.AcademyId,id,body.Month))return Results.Conflict();
        var applied=await db.PayrollDisbursements.Where(p=>p.AdvanceId==a.Id).SumAsync(p=>p.Amount);var payouts=await db.PayrollDisbursements.Where(p=>p.StatementId==s.Id).ToArrayAsync();var paid=payouts.Sum(p=>p.Amount);var available=a.Amount-applied;var remaining=s.NetDue-paid;
        if(available!=body.ExpectedAdvanceRemaining||remaining!=body.ExpectedStatementRemaining||body.Amount>available||body.Amount>remaining)return Results.Problem(statusCode:409,title:"تغير رصيد السلفة أو الكشف؛ حدّث المراجعة قبل التخصيص.");
        var now=clock.GetUtcNow();var p=new PayrollDisbursement {Id=Guid.NewGuid(),AcademyId=t.AcademyId,StatementId=s.Id,EmployeeId=id,BranchId=s.BranchId,Currency=s.Currency,Sequence=payouts.Length+1,Amount=body.Amount,PaidBefore=paid,RemainingAfter=remaining-body.Amount,AdvanceId=a.Id,AdvanceAppliedBefore=applied,AdvanceRemainingAfter=available-body.Amount,ActorMembershipId=t.MembershipId,ActorUserId=Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!),RequestKey=key,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};
        db.PayrollDisbursements.Add(p);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created(http.Request.Path,new{p.Id,p.RemainingAfter,p.AdvanceRemainingAfter});
    }
}
public sealed record PayrollAdvanceWrite(decimal Amount,string? Reference=null);
public sealed record PayrollAdvanceApply(DateOnly Month,decimal Amount,decimal ExpectedAdvanceRemaining,decimal ExpectedStatementRemaining,bool Opening=false);
