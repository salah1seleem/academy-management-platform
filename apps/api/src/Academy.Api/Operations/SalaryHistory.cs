using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class SalaryHistory
{
    internal static DateOnly Month(DateOnly date)=>new(date.Year,date.Month,1);
    internal static SalaryVersion? At(IEnumerable<SalaryVersion> versions,Guid employee,DateOnly date)=>versions.Where(v=>v.EmployeeId==employee&&v.EffectiveFrom<=Month(date)).OrderByDescending(v=>v.EffectiveFrom).ThenByDescending(v=>v.Revision).FirstOrDefault();
    internal static async Task<decimal> Rate(FoundationDbContext db,Employee employee,DateOnly date)=>await db.SalaryVersions.Where(v=>v.EmployeeId==employee.Id&&v.AcademyId==employee.AcademyId&&v.EffectiveFrom<=Month(date)).OrderByDescending(v=>v.EffectiveFrom).ThenByDescending(v=>v.Revision).Select(v=>(decimal?)v.MonthlySalary).FirstOrDefaultAsync()??employee.MonthlySalary;
    internal static SalaryVersion New(Employee e, TenantMembership t, Guid actor,string kind,DateOnly from,decimal amount,int revision,Guid? previous,string key,string hash,DateTimeOffset now,string? note=null)=>new() { Id=Guid.NewGuid(),AcademyId=t.AcademyId,EmployeeId=e.Id,BranchId=e.BranchId,Revision=revision,PreviousVersionId=previous,Kind=kind,EffectiveFrom=from,MonthlySalary=amount,Currency=e.Currency,ApprovedByMembershipId=t.MembershipId,ActorUserId=actor,RequestKey=key,RequestHash=hash,Note=note,CreatedAtUtc=now,UpdatedAtUtc=now };

    public static void MapSalaryHistory(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1/manage/employees/{id:guid}/salary-versions").RequireAuthorization(AcademyPermissions.PayrollRead);
        api.MapGet("/",Read);
        api.MapPost("/",Change).RequireAuthorization(AcademyPermissions.PayrollManage,AcademyPermissions.SalaryManage).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/staff/payroll/salary-versions",Own).RequireAuthorization(AcademyPermissions.TenantAccess);
    }
    private static async Task<object> View(Employee e,TenantMembership t,FoundationDbContext db,TimeProvider time,bool own=false)
    {
        var today=await AttendanceCalendar.TodayAsync(t.AcademyId,db,time); var rows=await db.SalaryVersions.AsNoTracking().Where(v=>v.AcademyId==t.AcademyId&&v.EmployeeId==e.Id).OrderByDescending(v=>v.Revision).ToArrayAsync(); var current=At(rows,e.Id,today);
        var actorIds=rows.Select(v=>v.ApprovedByMembershipId).Distinct().ToArray();
        var actors=own?new Dictionary<Guid,string>():await db.AcademyMemberships.AsNoTracking().Where(m=>m.AcademyId==t.AcademyId&&actorIds.Contains(m.Id)).Select(m=>new {m.Id,name=m.User.DisplayName}).ToDictionaryAsync(m=>m.Id,m=>m.name);
        var canChange=!own&&e.IsActive&&(t.Role==AcademyRole.AcademyOwner||!await PayrollSensitiveAccess.IsSelf(db,t,e.MembershipId))&&await PayrollSensitiveAccess.Allowed(db,t,AcademyPermissions.SalaryManage);
        return new { e.Id,e.Name,e.Currency,today,currentMonth=Month(today),currentSalary=current?.MonthlySalary??e.MonthlySalary,hasVerifiedHistory=current is not null,revision=rows.FirstOrDefault()?.Revision??0,canChange,
            currentMonthPaid=await PayrollEndpoints.Locked(db,t.AcademyId,e.Id,today),items=rows.Select(v=>new {v.Id,v.Kind,v.Revision,v.EffectiveFrom,v.MonthlySalary,v.Currency,note=own?null:v.Note,approvedBy=own?null:actors.GetValueOrDefault(v.ApprovedByMembershipId),v.CreatedAtUtc}) };
    }
    private static async Task<IResult> Read(Guid id,CurrentTenant current,FoundationDbContext db,TimeProvider time,HttpResponse response)
    {
        var t=(await current.ResolveAsync())!; response.Headers.CacheControl="no-store"; var e=await db.Employees.AsNoTracking().SingleOrDefaultAsync(e=>e.Id==id); return e is null?Results.NotFound():Results.Ok(await View(e,t,db,time));
    }
    private static async Task<IResult> Own(CurrentTenant current,FoundationDbContext db,TimeProvider time,Academy.Api.Configuration.AcademyConfigurationService config,HttpResponse response)
    {
        var t=(await current.ResolveAsync())!; if(t.Role is not (AcademyRole.Coach or AcademyRole.AcademyAdmin)||!await config.Enabled(t.AcademyId,"payroll"))return Results.Forbid();
        response.Headers.CacheControl="no-store"; var e=await db.Employees.AsNoTracking().SingleOrDefaultAsync(e=>e.MembershipId==t.MembershipId&&e.IsActive);return e is null?Results.Ok(new {configured=false}):Results.Ok(await View(e,t,db,time,true));
    }
    private static async Task<IResult> Change(Guid id,SalaryChange body,HttpContext http,CurrentTenant current,FoundationDbContext db,TimeProvider time)
    {
        var t=(await current.ResolveAsync())!;var key=http.Request.Headers["Idempotency-Key"].ToString();
        if(key.Length is <8 or >100||body.MonthlySalary<0||body.MonthlySalary>100000000||decimal.Round(body.MonthlySalary,2)!=body.MonthlySalary||body.EffectiveFrom.Day!=1||body.EffectiveFrom.Year is <2000 or >9998||body.Note?.Length>500||body.ExpectedRevision<0)return Results.Problem(statusCode:422,title:"راجع المبلغ وبداية شهر السريان قبل الاعتماد.");
        await using var tx=await db.Database.BeginTransactionAsync();if(!await PayrollSensitiveAccess.LockAndAuthorize(http,current,db,AcademyPermissions.SalaryManage))return Results.Forbid();
        await PayrollEndpoints.Lock(db,t.AcademyId,id,body.EffectiveFrom);var e=await db.Employees.SingleOrDefaultAsync(e=>e.Id==id&&e.IsActive);if(e is null)return Results.NotFound();
        if(!await PayrollSensitiveAccess.BranchAllowed(db,t,e.BranchId)||t.Role!=AcademyRole.AcademyOwner&&await PayrollSensitiveAccess.IsSelf(db,t,e.MembershipId))return Results.Forbid();
        var hash=StatementComparisonRules.Hash(new {id,body});var prior=await db.SalaryVersions.SingleOrDefaultAsync(v=>v.ApprovedByMembershipId==t.MembershipId&&v.RequestKey==key);if(prior is not null)return prior.RequestHash==hash?Results.Ok(new {prior.Id,replay=true}):Results.Problem(statusCode:409,title:"معرف الاعتماد مستخدم بتفاصيل مختلفة.");
        var month=Month(await AttendanceCalendar.TodayAsync(t.AcademyId,db,time));var latest=await db.SalaryVersions.Where(v=>v.EmployeeId==id).OrderByDescending(v=>v.Revision).FirstOrDefaultAsync();
        if(body.EffectiveFrom<month||latest?.EffectiveFrom>body.EffectiveFrom||body.ExpectedRevision!=(latest?.Revision??0)||body.ExpectedCurrentSalary!=await Rate(db,e,month))return Results.Problem(statusCode:409,title:"تغير الراتب أو السجل، أو اخترت شهرًا أقدم من المسموح. حدّث المراجعة.");
        if(await db.PayrollSettlements.AnyAsync(p=>p.EmployeeId==id&&p.Month>=body.EffectiveFrom))return Results.Problem(statusCode:409,title:"يوجد راتب مسدد يتأثر بهذا التاريخ؛ لا يمكن إعادة كتابة الماضي.");
        var actor=await db.AcademyMemberships.Where(m=>m.Id==t.MembershipId).Select(m=>m.UserId).SingleAsync();
        var now=time.GetUtcNow();if(latest is null){latest=New(e,t,actor,"LegacyBaseline",month,e.MonthlySalary,1,null,"baseline-"+Guid.NewGuid().ToString("N"),hash,now,"مرجع الراتب القائم من شهر المراجعة فقط؛ التاريخ الأسبق غير موثق.");db.SalaryVersions.Add(latest);await db.SaveChangesAsync();}
        var v=New(e,t,actor,"Change",body.EffectiveFrom,body.MonthlySalary,latest.Revision+1,latest.Id,key,hash,now,body.Note?.Trim());db.SalaryVersions.Add(v);await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created(http.Request.Path,new {v.Id,v.Revision});
    }
}
public sealed record SalaryChange(DateOnly EffectiveFrom,decimal MonthlySalary,int ExpectedRevision,decimal ExpectedCurrentSalary,string? Note=null);
