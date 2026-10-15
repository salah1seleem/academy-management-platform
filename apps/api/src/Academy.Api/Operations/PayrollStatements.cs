using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Finance;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;

public static class PayrollStatements
{
    public static void MapPayrollStatements(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1/manage/payroll/{id:guid}/statement").RequireAuthorization(AcademyPermissions.PayrollRead);
        api.MapGet("",Read);
        api.MapPost("/approve",Approve).RequireAuthorization(AcademyPermissions.PayrollManage,AcademyPermissions.PayrollStatementApprove).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/disbursements",Disburse).RequireAuthorization(AcademyPermissions.PayrollManage,AcademyPermissions.PayrollPay).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/staff/payroll/statements",Own).RequireAuthorization(AcademyPermissions.TenantAccess);
    }
    private static string Key(HttpContext http)=>http.Request.Headers["Idempotency-Key"].ToString();
    private static Guid Actor(HttpContext http)=>Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static bool ValidMonth(DateOnly month)=>month.Day==1&&month.Year is >=2000 and <=9998;
    internal static async Task<(PayrollRow Row,string Snapshot,string Hash)?> Prepare(Guid id,DateOnly month,TenantMembership t,FoundationDbContext db)
    {
        var employee=await db.Employees.AsNoTracking().SingleOrDefaultAsync(e=>e.Id==id&&e.AcademyId==t.AcademyId);if(employee is null)return null;
        var row=(await PayrollEndpoints.Rows(t.AcademyId,month,null,db)).SingleOrDefault(r=>r.EmployeeId==id);if(row is null)return null;
        var end=month.AddMonths(1);
        var salary=await db.SalaryVersions.AsNoTracking().Where(v=>v.AcademyId==t.AcademyId&&v.EmployeeId==id&&v.EffectiveFrom<=month).OrderByDescending(v=>v.EffectiveFrom).ThenByDescending(v=>v.Revision).Select(v=>new {v.Id,v.Revision,v.EffectiveFrom,v.MonthlySalary}).FirstOrDefaultAsync();
        var adjustments=await db.PayrollAdjustments.AsNoTracking().Where(a=>a.AcademyId==t.AcademyId&&a.EmployeeId==id&&a.DueDate>=month&&a.DueDate<end).OrderBy(a=>a.Id).Select(a=>new {a.Id,a.Kind,a.Amount,a.DueDate,a.AdditionRequestId,a.Reason}).ToArrayAsync();
        var attendance=await db.StaffDailyAttendances.AsNoTracking().Where(a=>a.AcademyId==t.AcademyId&&a.MembershipId==employee.MembershipId&&a.WorkDate>=month&&a.WorkDate<end&&a.ApprovedDeduction>0).OrderBy(a=>a.Id).Select(a=>new {a.Id,a.WorkDate,a.ApprovedDeduction,a.DeductionReason}).ToArrayAsync();
        var sources=await db.PrivateCompensationEntries.AsNoTracking().Where(a=>a.AcademyId==t.AcademyId&&a.EmployeeId==id&&a.DueDate>=month&&a.DueDate<end&&a.PayrollAdjustmentId.HasValue).OrderBy(a=>a.Id).Select(a=>new {a.Id,a.DeliveryId,a.PayrollAdjustmentId,a.Delta,a.DueDate}).ToArrayAsync();
        var facts=new {schema=1,t.AcademyId,month,employee=new {employee.Id,employee.Name,employee.BranchId,employee.MembershipId,employee.ContractStart,employee.ContractEnd,employee.Currency},salary,row,adjustments,attendance,privateSources=sources};
        return(row,JsonSerializer.Serialize(facts),StatementComparisonRules.Hash(facts));
    }
    internal static PayrollStatement New(Employee e,DateOnly month,TenantMembership t,Guid actor,(PayrollRow Row,string Snapshot,string Hash) p,string key,string hash,DateTimeOffset now,string? note)=>new() {Id=Guid.NewGuid(),AcademyId=t.AcademyId,EmployeeId=e.Id,BranchId=e.BranchId,EmployeeName=e.Name,Month=month,Currency=e.Currency,MonthlySalary=p.Row.MonthlySalary,ContractDays=p.Row.ContractDays,BaseDue=p.Row.BaseDue,Deductions=p.Row.Deductions,Additions=p.Row.Additions,PrivateCompensation=p.Row.PrivateCompensation,NetDue=p.Row.NetDue,SourceSnapshot=p.Snapshot,SourceHash=p.Hash,ApprovedByMembershipId=t.MembershipId,ActorUserId=actor,Note=note,RequestKey=key,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};
    private static async Task<IResult> Read(Guid id,DateOnly month,CurrentTenant current,FoundationDbContext db,TimeProvider time,HttpResponse response)
    {
        response.Headers.CacheControl="no-store";if(!ValidMonth(month))return Results.UnprocessableEntity();var t=(await current.ResolveAsync())!;
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var prepared=await Prepare(id,month,t,db);if(prepared is null)return Results.NotFound();
        var statement=await db.PayrollStatements.AsNoTracking().SingleOrDefaultAsync(s=>s.EmployeeId==id&&s.Month==month);
        var payments=statement is null?[]:await db.PayrollDisbursements.AsNoTracking().Where(p=>p.StatementId==statement.Id).OrderByDescending(p=>p.Sequence).Select(p=>new {p.Id,p.Sequence,p.Amount,p.RemainingAfter,p.Reference,p.CreatedAtUtc}).ToArrayAsync();
        var employeeMember=await db.Employees.Where(e=>e.Id==id).Select(e=>e.MembershipId).SingleAsync();var today=await AttendanceCalendar.TodayAsync(t.AcademyId,db,time);
        var canApprove=statement is null&&!prepared.Value.Row.IsPaid&&prepared.Value.Row.NetDue>0&&prepared.Value.Row.ContractDays>0&&month<=SalaryHistory.Month(today)&&await PayrollSensitiveAccess.Allowed(db,t,AcademyPermissions.PayrollStatementApprove)&&(t.Role==AcademyRole.AcademyOwner||!await PayrollSensitiveAccess.IsSelf(db,t,employeeMember));
        return Results.Ok(new {month,row=prepared.Value.Row,statement=statement is null?null:new {statement.Id,statement.CreatedAtUtc,statement.Note,statement.SourceHash},quoteHash=statement?.SourceHash??prepared.Value.Hash,
            sources=JsonSerializer.Deserialize<JsonElement>(statement?.SourceSnapshot??prepared.Value.Snapshot),canApprove,canPay=statement is not null&&prepared.Value.Row.RemainingAmount>0&&await PayrollSensitiveAccess.Allowed(db,t,AcademyPermissions.PayrollPay),payments,
            balanceScope="SelectedMonthOnly",openingBalanceVerified=false});
    }
    private static async Task<IResult> Approve(Guid id,StatementApproval body,HttpContext http,CurrentTenant current,FoundationDbContext db,TimeProvider time)
    {
        var key=Key(http);if(key.Length is <8 or >100||!ValidMonth(body.Month)||body.QuoteHash?.Length!=64||body.Note?.Length>500)return Results.Problem(statusCode:422,title:"راجع كشف الشهر قبل الاعتماد.");
        var t=(await current.ResolveAsync())!;await using var tx=await db.Database.BeginTransactionAsync();if(!await PayrollSensitiveAccess.LockAndAuthorize(http,current,db,AcademyPermissions.PayrollStatementApprove))return Results.Forbid();
        await PayrollEndpoints.Lock(db,t.AcademyId,id,body.Month);var e=await db.Employees.SingleOrDefaultAsync(e=>e.Id==id&&e.IsActive);if(e is null)return Results.NotFound();
        if(!await PayrollSensitiveAccess.BranchAllowed(db,t,e.BranchId)||t.Role!=AcademyRole.AcademyOwner&&await PayrollSensitiveAccess.IsSelf(db,t,e.MembershipId))return Results.Forbid();
        var hash=StatementComparisonRules.Hash(new {id,body});var replay=await db.PayrollStatements.SingleOrDefaultAsync(s=>s.ApprovedByMembershipId==t.MembershipId&&s.RequestKey==key);if(replay is not null)return replay.RequestHash==hash?Results.Ok(new {replay.Id,replay=true}):Results.Conflict();
        if(await db.PayrollStatements.AnyAsync(s=>s.EmployeeId==id&&s.Month==body.Month)||await PayrollEndpoints.Locked(db,t.AcademyId,id,body.Month))return Results.Problem(statusCode:409,title:"الكشف معتمد أو الراتب مسدد بالفعل؛ لا تعِد اعتماد المصدر.");
        var p=await Prepare(id,body.Month,t,db);var month=SalaryHistory.Month(await AttendanceCalendar.TodayAsync(t.AcademyId,db,time));
        if(p is null||p.Value.Hash!=body.QuoteHash||p.Value.Row.NetDue<=0||p.Value.Row.ContractDays==0||body.Month>month)return Results.Problem(statusCode:409,title:"تغيرت المصادر أو الشهر غير مستحق. حدّث المراجعة قبل الاعتماد.");
        var s=New(e,body.Month,t,Actor(http),p.Value,key,hash,time.GetUtcNow(),body.Note?.Trim());
        db.PayrollStatements.Add(s);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created(http.Request.Path,new {s.Id,s.NetDue});
    }
    private static async Task<IResult> Disburse(Guid id,StatementDisbursement body,HttpContext http,CurrentTenant current,FoundationDbContext db,TimeProvider time)
    {
        var key=Key(http);if(key.Length is <8 or >100||!ValidMonth(body.Month)||body.Amount<=0||body.Amount>100000000||decimal.Round(body.Amount,2)!=body.Amount||body.Reference?.Length>120)return Results.Problem(statusCode:422,title:"أدخل مبلغ صرف صحيحًا وراجع المتبقي.");
        var t=(await current.ResolveAsync())!;await using var tx=await db.Database.BeginTransactionAsync();if(!await PayrollSensitiveAccess.LockAndAuthorize(http,current,db,AcademyPermissions.PayrollPay))return Results.Forbid();
        await PayrollEndpoints.Lock(db,t.AcademyId,id,body.Month);var s=await db.PayrollStatements.SingleOrDefaultAsync(s=>s.EmployeeId==id&&s.Month==body.Month);if(s is null)return Results.NotFound();if(!await PayrollSensitiveAccess.BranchAllowed(db,t,s.BranchId))return Results.Forbid();
        var hash=StatementComparisonRules.Hash(new {id,body});var replay=await db.PayrollDisbursements.SingleOrDefaultAsync(p=>p.ActorMembershipId==t.MembershipId&&p.RequestKey==key);if(replay is not null)return replay.RequestHash==hash?Results.Ok(new {replay.Id,replay=true}):Results.Conflict();
        var payments=await db.PayrollDisbursements.Where(p=>p.StatementId==s.Id).ToArrayAsync();var paid=payments.Sum(p=>p.Amount);var remaining=s.NetDue-paid;
        if(paid!=body.ExpectedPaid||remaining!=body.ExpectedRemaining||body.Amount>remaining||await PayrollEndpoints.Locked(db,t.AcademyId,id,body.Month))return Results.Problem(statusCode:409,title:"تغير المدفوع أو المبلغ أكبر من المتبقي. حدّث الكشف قبل تسجيل الصرف.");
        var now=time.GetUtcNow();var p=new PayrollDisbursement {Id=Guid.NewGuid(),AcademyId=t.AcademyId,StatementId=s.Id,EmployeeId=id,BranchId=s.BranchId,Currency=s.Currency,Sequence=payments.Length+1,Amount=body.Amount,PaidBefore=paid,RemainingAfter=remaining-body.Amount,ActorMembershipId=t.MembershipId,ActorUserId=Actor(http),Reference=body.Reference?.Trim(),RequestKey=key,RequestHash=hash,CreatedAtUtc=now,UpdatedAtUtc=now};
        db.PayrollDisbursements.Add(p);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created(http.Request.Path,new {p.Id,p.Amount,p.RemainingAfter});
    }
    private static async Task<IResult> Own(CurrentTenant current,FoundationDbContext db,AcademyConfigurationService config,HttpResponse response)
    {
        var t=(await current.ResolveAsync())!;if(t.Role is not (AcademyRole.Coach or AcademyRole.AcademyAdmin)||!await config.Enabled(t.AcademyId,"payroll"))return Results.Forbid();response.Headers.CacheControl="no-store";
        var e=await db.Employees.AsNoTracking().SingleOrDefaultAsync(e=>e.MembershipId==t.MembershipId&&e.IsActive);if(e is null)return Results.Ok(new {configured=false});
        var statements=await db.PayrollStatements.AsNoTracking().Where(s=>s.EmployeeId==e.Id).OrderByDescending(s=>s.Month).Take(24).ToArrayAsync();var ids=statements.Select(s=>s.Id).ToArray();var paid=await db.PayrollDisbursements.AsNoTracking().Where(p=>ids.Contains(p.StatementId)).OrderByDescending(p=>p.Sequence).ToArrayAsync();
        return Results.Ok(new {configured=true,items=statements.Select(s=>new {s.Id,s.Month,s.Currency,s.BaseDue,s.Deductions,s.Additions,s.PrivateCompensation,s.NetDue,paid=paid.Where(p=>p.StatementId==s.Id).Sum(p=>p.Amount),remaining=s.NetDue-paid.Where(p=>p.StatementId==s.Id).Sum(p=>p.Amount),payments=paid.Where(p=>p.StatementId==s.Id).Select(p=>new {p.Amount,p.CreatedAtUtc})}),balanceScope="ApprovedMonthsOnly",openingBalanceVerified=false});
    }
}
public sealed record StatementApproval(DateOnly Month,string QuoteHash,string? Note=null);
public sealed record StatementDisbursement(DateOnly Month,decimal Amount,decimal ExpectedPaid,decimal ExpectedRemaining,string? Reference=null);
