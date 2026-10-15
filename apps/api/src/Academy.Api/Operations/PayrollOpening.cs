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

// A reviewed residual from BEFORE the cutover, not salary earned in a fabricated month.
// Uses the existing immutable statement and disbursement sources, never a second payroll.
public static class PayrollOpening
{
    public static void MapPayrollOpening(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1/manage/payroll/{id:guid}/opening").RequireAuthorization(AcademyPermissions.PayrollRead);
        api.MapGet("",Read);
        api.MapPost("",Approve).RequireAuthorization(AcademyPermissions.PayrollManage,AcademyPermissions.PayrollStatementApprove).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/corrections",PayrollPayableCorrections.Correct).RequireAuthorization(AcademyPermissions.PayrollManage,AcademyPermissions.PayrollStatementApprove).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/disbursements",Pay).RequireAuthorization(AcademyPermissions.PayrollManage,AcademyPermissions.PayrollPay).AddEndpointFilter<CsrfFilter>();
    }
    internal static Task<bool> BeforeBoundary(FoundationDbContext db,Guid id,DateOnly month)
        =>db.PayrollStatements.AnyAsync(s=>s.EmployeeId==id&&s.Kind=="Opening"&&s.OpeningAsOf>month);

    internal static async Task<(DateOnly Latest,string Snapshot,string Hash)> Facts(Employee e,DateOnly today,FoundationDbContext db)
    {
        var statements=await db.PayrollStatements.AsNoTracking().Where(s=>s.EmployeeId==e.Id).OrderBy(s=>s.Id).Select(s=>new{s.Id,s.Kind,s.Month,s.NetDue,s.SourceHash}).ToArrayAsync();
        var legacy=await db.PayrollSettlements.AsNoTracking().Where(s=>s.EmployeeId==e.Id).OrderBy(s=>s.Id).Select(s=>new{s.Id,s.Month,s.NetDue}).ToArrayAsync();
        var payments=await db.PayrollDisbursements.AsNoTracking().Where(p=>p.EmployeeId==e.Id).OrderBy(p=>p.Id).Select(p=>new{p.Id,p.StatementId,p.Amount}).ToArrayAsync();
        var advances=await db.PayrollAdvances.AsNoTracking().Where(p=>p.EmployeeId==e.Id).OrderBy(p=>p.Id).Select(p=>new{p.Id,p.Amount}).ToArrayAsync();
        var latest=statements.Select(s=>s.Month).Concat(legacy.Select(s=>s.Month)).Append(SalaryHistory.Month(today)).Min();
        var facts=new{schema=1,employee=new{e.Id,e.AcademyId,e.BranchId,e.MembershipId,e.Currency,e.Name,e.IsActive},latest,statements,legacy,payments,advances};
        return(latest,JsonSerializer.Serialize(facts),StatementComparisonRules.Hash(facts));
    }
    private static async Task<IResult> Read(Guid id,CurrentTenant current,FoundationDbContext db,TimeProvider clock,HttpResponse response)
    {
        response.Headers.CacheControl="no-store";var t=(await current.ResolveAsync())!;
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var e=await db.Employees.AsNoTracking().SingleOrDefaultAsync(e=>e.Id==id);if(e is null)return Results.NotFound();
        var facts=await Facts(e,await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock),db);
        var s=await db.PayrollStatements.AsNoTracking().SingleOrDefaultAsync(s=>s.EmployeeId==id&&s.Kind=="Opening");
        var payouts=s is null?[]:await db.PayrollDisbursements.AsNoTracking().Where(p=>p.StatementId==s.Id).OrderByDescending(p=>p.Sequence).ToArrayAsync();
        var payments=payouts.Select(p=>new{p.Id,p.Amount,p.Reference,p.CreatedAtUtc,kind=p.AdvanceId.HasValue?"AdvanceApplication":"DirectPayout"}).ToArray();
        var corrections=s is null?[]:await db.PayrollPayableCorrections.AsNoTracking().Where(c=>c.StatementId==s.Id).OrderBy(c=>c.Revision).ToArrayAsync();
        var amount=(s?.NetDue??0)+corrections.Sum(c=>c.Delta);
        var paid=payments.Sum(p=>p.Amount);var unallocated=await db.PayrollAdvances.Where(a=>a.EmployeeId==id).SumAsync(a=>a.Amount)-await db.PayrollDisbursements.Where(p=>p.EmployeeId==id&&p.AdvanceId.HasValue).SumAsync(p=>p.Amount);
        return Results.Ok(new{employeeName=e.Name,e.Currency,latestBoundary=facts.Latest,quoteHash=facts.Hash,
            correctionQuoteHash=s is null?null:PayrollPayableCorrections.Quote(s,corrections,payouts),
            canCorrect=s is not null&&t.Role==AcademyRole.AcademyOwner&&await PayrollSensitiveAccess.Allowed(db,t,AcademyPermissions.PayrollStatementApprove),
            corrections=corrections.Select(c=>new{c.Id,c.Revision,c.PreviousAmount,c.TargetAmount,c.Delta,c.PaidAtApproval,c.Reference,c.Note,c.CreatedAtUtc}),
            canApprove=s is null&&e.IsActive&&t.Role==AcademyRole.AcademyOwner&&await PayrollSensitiveAccess.Allowed(db,t,AcademyPermissions.PayrollStatementApprove),
            canPay=s is not null&&amount>paid&&await PayrollSensitiveAccess.Allowed(db,t,AcademyPermissions.PayrollPay)&&(t.Role==AcademyRole.AcademyOwner||!await PayrollSensitiveAccess.IsSelf(db,t,e.MembershipId)),
            opening=s is null?null:new{s.Id,asOf=s.OpeningAsOf,amount,originalAmount=s.NetDue,s.OpeningReference,s.Note,s.CreatedAtUtc,paid,remaining=amount-paid},payments,unallocatedAdvances=unallocated});
    }
    private static async Task<IResult> Approve(Guid id,PayrollOpeningApproval body,HttpContext http,CurrentTenant current,FoundationDbContext db,TimeProvider clock)
    {
        var key=http.Request.Headers["Idempotency-Key"].ToString();
        if(key.Length is <8 or >100||body.AsOf.Year is <2001 or >9998||body.AsOf.Day!=1||body.Amount<0||body.Amount>100000000||decimal.Round(body.Amount,2)!=body.Amount||string.IsNullOrWhiteSpace(body.Reference)||body.Reference.Trim().Length is <3 or >120||body.Note?.Length>500||body.QuoteHash?.Length!=64)
            return Results.Problem(statusCode:422,title:"راجع شهر بداية النظام والمبلغ ومرجع الافتتاح؛ الصفر يحتاج اعتمادًا صريحًا أيضًا.");
        var t=(await current.ResolveAsync())!;await using var tx=await db.Database.BeginTransactionAsync();
        if(t.Role!=AcademyRole.AcademyOwner||!await PayrollSensitiveAccess.LockAndAuthorize(http,current,db,AcademyPermissions.PayrollStatementApprove))return Results.Forbid();
        await PayrollEndpoints.Lock(db,t.AcademyId,id,body.AsOf);
        var e=await db.Employees.SingleOrDefaultAsync(e=>e.Id==id&&e.IsActive);if(e is null)return Results.NotFound();
        var hash=StatementComparisonRules.Hash(new{id,body,kind="Opening"});
        var replay=await db.PayrollStatements.SingleOrDefaultAsync(s=>s.ApprovedByMembershipId==t.MembershipId&&s.RequestKey==key);
        if(replay is not null)return replay.RequestHash==hash?Results.Ok(new{replay.Id,replay=true}):Results.Conflict();
        var facts=await Facts(e,await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock),db);
        if(await db.PayrollStatements.AnyAsync(s=>s.EmployeeId==id&&s.Kind=="Opening")||body.AsOf>facts.Latest||facts.Hash!=body.QuoteHash)
            return Results.Problem(statusCode:409,title:"تغيرت المصادر أو حد الافتتاح يتداخل مع شهر مسجل؛ حدّث المراجعة ولا تكرر الاستحقاق.");
        var s=New(e,t,Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!),body,facts.Snapshot,facts.Hash,key,hash,clock.GetUtcNow());
        db.PayrollStatements.Add(s);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created(http.Request.Path,new{s.Id,s.NetDue});
    }
    internal static PayrollStatement New(Employee e,TenantMembership t,Guid actor,PayrollOpeningApproval body,string factsSnapshot,string factsHash,string key,string hash,DateTimeOffset now)
    {
        var reference=body.Reference.Trim();
        return new PayrollStatement{Id=Guid.NewGuid(),AcademyId=t.AcademyId,EmployeeId=e.Id,BranchId=e.BranchId,EmployeeName=e.Name,Currency=e.Currency,Kind="Opening",OpeningAsOf=body.AsOf,Month=body.AsOf.AddMonths(-1),OpeningReference=reference,NetDue=body.Amount,
            SourceSnapshot=JsonSerializer.Serialize(new{kind="Opening",asOf=body.AsOf,amount=body.Amount,reference,knownSources=JsonSerializer.Deserialize<JsonElement>(factsSnapshot),scope="UnrecordedResidualBeforeBoundaryOnly"}),SourceHash=StatementComparisonRules.Hash(new{factsHash,body.AsOf,body.Amount,reference}),
            ApprovedByMembershipId=t.MembershipId,ActorUserId=actor,Note=body.Note?.Trim(),RequestKey=key,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};
    }
    private static async Task<IResult> Pay(Guid id,PayrollOpeningPayment body,HttpContext http,CurrentTenant current,FoundationDbContext db,TimeProvider clock)
    {
        var s=await db.PayrollStatements.AsNoTracking().SingleOrDefaultAsync(s=>s.EmployeeId==id&&s.Kind=="Opening");if(s is null)return Results.NotFound();
        return await PayrollStatements.Disburse(id,new(s.Month,body.Amount,body.ExpectedPaid,body.ExpectedRemaining,body.Reference),http,current,db,clock,true);
    }
}
public sealed record PayrollOpeningApproval(DateOnly AsOf,decimal Amount,string Reference,string QuoteHash,string? Note=null);
public sealed record PayrollOpeningPayment(decimal Amount,decimal ExpectedPaid,decimal ExpectedRemaining,string? Reference=null);
