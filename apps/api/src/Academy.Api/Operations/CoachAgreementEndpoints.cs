using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class CoachAgreementEndpoints
{
    public static void MapCoachAgreements(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/manage/payroll/coaches/{coachId:guid}/agreements").RequireAuthorization(AcademyPermissions.PayrollRead);
        api.MapGet("/", List); api.MapGet("/active", Active);
        api.MapPost("/", Create).RequireAuthorization(AcademyPermissions.CoachAgreementManage).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/{id:guid}/approve", Approve).RequireAuthorization(AcademyPermissions.CoachAgreementManage).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/staff/payroll/agreements", Own).RequireAuthorization(AcademyPermissions.TenantAccess);
    }
    private static Task<Employee?> Employee(FoundationDbContext db, TenantMembership t, Guid coach)
        => db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.AcademyId == t.AcademyId && e.MembershipId == coach
            && db.AcademyMemberships.Any(m => m.Id == coach && m.AcademyId == t.AcademyId && m.Role == AcademyRole.Coach));
    private static async Task<IResult> List(Guid coachId, CurrentTenant current, FoundationDbContext db, TimeProvider clock, HttpResponse response)
    {
        var t = (await current.ResolveAsync())!; response.Headers.CacheControl = "no-store";
        if (!await db.AcademyMemberships.AnyAsync(m => m.Id == coachId && m.Role == AcademyRole.Coach)) return Results.NotFound();
        var employee = await Employee(db, t, coachId);
        var approvals = employee is null ? [] : await db.CoachCompensationApprovals.AsNoTracking().Where(p => p.EmployeeId == employee.Id).ToArrayAsync();
        return Results.Ok(new { configured = employee is not null, employeeId = employee?.Id, monthlyBase = employee is null?(decimal?)null:await SalaryHistory.Rate(db,employee,await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock)), currency = employee?.Currency, contractEnd = employee?.ContractEnd,
            latestApproved = approvals.GroupBy(p => new { p.BranchId, p.SportId }).Select(g => g.OrderByDescending(p => p.Revision).First()).Select(p => new { p.BranchId, p.SportId, p.AgreementId, version = p.Revision }),
            today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock), timeZone = await db.Academies.Where(a => a.Id == t.AcademyId).Select(a => a.TimeZone).SingleAsync(), items = await Views(db, t.AcademyId, coachId),
            branches = await db.Branches.Where(b => b.IsActive).OrderBy(b => b.ArabicName).Select(b => new { b.Id, name = b.ArabicName }).ToArrayAsync(),
            sports = await db.Sports.Where(s => s.IsActive).OrderBy(s => s.ArabicName).Select(s => new { s.Id, name = s.ArabicName }).ToArrayAsync() });
    }
    internal static async Task<CoachAgreementView[]> Views(FoundationDbContext db, Guid academy, Guid coach, bool own = false)
    {
        var query = from a in db.CoachCompensationAgreements.AsNoTracking()
            join approval in db.CoachCompensationApprovals.AsNoTracking() on a.Id equals approval.AgreementId into decisions
            from approval in decisions.DefaultIfEmpty()
            where a.AcademyId == academy && a.CoachMembershipId == coach && (!own || approval != null)
            orderby a.EffectiveFrom descending, a.CreatedAtUtc descending
            select new CoachAgreementView(a.Id, a.BranchId, a.SportId,
                db.Branches.Where(b => b.Id == a.BranchId).Select(b => b.ArabicName).First()!, db.Sports.Where(s => s.Id == a.SportId).Select(s => s.ArabicName).First()!,
                a.EffectiveFrom, a.EffectiveTo, a.VariableMethod, a.VariableValue, a.Eligibility, a.BaseCoverage, a.Currency,
                own ? null : (decimal?)a.BaseSalarySnapshot, own ? null : a.Note,
                approval == null ? null : (int?)approval.Revision, approval == null ? null : (DateTimeOffset?)approval.ApprovedAtUtc,
                approval == null ? null : db.AcademyMemberships.Where(m => m.Id == approval.ApprovedByMembershipId).Select(m => m.User.DisplayName).FirstOrDefault());
        return await query.Take(200).ToArrayAsync();
    }
    // Select the latest applicable approved version BEFORE checking its end.
    // An expired successor must not resurrect an older open-ended agreement.
    public static async Task<CoachCompensationAgreement?> Resolve(FoundationDbContext db, Guid academy, Guid coach, Guid branch, Guid sport, DateOnly serviceDate, DateTimeOffset commitment)
    {
        var row = await (from a in db.CoachCompensationAgreements.AsNoTracking()
            join p in db.CoachCompensationApprovals.AsNoTracking() on a.Id equals p.AgreementId
            where a.AcademyId == academy && a.CoachMembershipId == coach && a.BranchId == branch && a.SportId == sport && a.EffectiveFrom <= serviceDate && p.ApprovedAtUtc <= commitment
            orderby a.EffectiveFrom descending, p.Revision descending select a).FirstOrDefaultAsync();
        if(row is null||row.EffectiveTo.HasValue&&serviceDate>row.EffectiveTo)return null;
        var employee=await db.Employees.AsNoTracking().SingleOrDefaultAsync(e=>e.AcademyId==academy&&e.Id==row.EmployeeId);if(employee is null)return null;
        var contract=await ContractHistory.Bounds(db,employee,serviceDate);
        return serviceDate>=contract.ContractStart&&(!contract.ContractEnd.HasValue||serviceDate<=contract.ContractEnd)?row:null;
    }
    private static async Task<IResult> Active(Guid coachId, Guid branchId, Guid sportId, DateOnly on, CurrentTenant current, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await current.ResolveAsync())!;
        if (await Employee(db, t, coachId) is null) return Results.NotFound();
        var row = await Resolve(db, t.AcademyId, coachId, branchId, sportId, on, clock.GetUtcNow());
        return Results.Ok(new { configured = row is not null, agreementId = row?.Id });
    }
    private static string? Validate(CoachAgreementWrite b)
        => b.VariableMethod is not ("Percent" or "PerSession") || b.VariableValue is null or < 0 or > 100000000
            || decimal.Round(b.VariableValue.Value, 2) != b.VariableValue || b.VariableMethod == "Percent" && b.VariableValue > 100
            || b.Eligibility is not ("DeliveredCollected" or "Delivered") || b.BaseCoverage is not ("GroupOnly" or "IncludesPrivate")
            || b.BaseCoverage == "IncludesPrivate" && b.VariableValue != 0 || b.EffectiveFrom.Year is < 2000 or > 9998
            || b.EffectiveTo.HasValue && (b.EffectiveTo.Value.Year > 9998 || b.EffectiveTo < b.EffectiveFrom) || b.Note?.Length > 500
            ? "راجع تاريخ السريان والنسبة أو مبلغ الحصة؛ الصفر اختيار صريح، والعمل المشمول بالأساسي لا يجمع أجرًا متغيرًا إضافيًا." : null;
    private static async Task<bool> ValidScope(FoundationDbContext db, TenantMembership t, Employee e, Guid branch, Guid sport)
        => e.IsActive && await db.AcademyMemberships.AnyAsync(m => m.Id == e.MembershipId && m.Role == AcademyRole.Coach && m.IsActive && m.User.IsActive)
            && await PayrollSensitiveAccess.BranchAllowed(db, t, e.BranchId) && await PayrollSensitiveAccess.BranchAllowed(db, t, branch)
            && await db.Branches.AnyAsync(b => b.Id == branch && b.IsActive) && await db.Sports.AnyAsync(s => s.Id == sport && s.IsActive);
    private static async Task<bool> FitsContract(FoundationDbContext db,Employee e, DateOnly from, DateOnly? to)
    {
        var c=await ContractHistory.Bounds(db,e,from);
        return from>=c.ContractStart&&(!c.ContractEnd.HasValue||from<=c.ContractEnd&&to.HasValue&&to<=c.ContractEnd);
    }
    private static async Task<IResult> Create(Guid coachId, CoachAgreementWrite body, HttpContext context, CurrentTenant current, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await current.ResolveAsync())!; var error = Validate(body); if (error is not null) return Results.Problem(statusCode: 422, title: error);
        var key = context.Request.Headers["Idempotency-Key"].ToString(); if (key.Length is < 8 or > 100) return Results.Problem(statusCode: 422, title: "أعد محاولة الحفظ من النموذج.");
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await PayrollSensitiveAccess.LockAndAuthorize(context, current, db, AcademyPermissions.CoachAgreementManage)) return Results.Forbid();
        var employee = await Employee(db, t, coachId); if (employee is null) return Results.NotFound();
        await PayrollEndpoints.Lock(db, t.AcademyId, employee.Id, body.EffectiveFrom);
        employee = await Employee(db, t, coachId); if (employee is null || !await ValidScope(db, t, employee, body.BranchId, body.SportId)) return Results.Forbid();
        if (!await FitsContract(db,employee, body.EffectiveFrom, body.EffectiveTo)) return Results.Problem(statusCode: 422, title: "حدد فترة اتفاق داخل مدة عقد الموظف، مع نهاية للاتفاق إذا كان العقد محدد المدة.");
        if (t.Role != AcademyRole.AcademyOwner && await db.AcademyMemberships.AnyAsync(m => m.Id == coachId && m.UserId.ToString() == context.User.FindFirstValue(ClaimTypes.NameIdentifier))) return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"coach-agreement-key:{t.AcademyId}:{t.MembershipId}:{key}"},0))");
        var existing = await db.CoachCompensationAgreements.SingleOrDefaultAsync(a => a.CreatedByMembershipId == t.MembershipId && a.IdempotencyKey == key);
        if (existing is not null) return existing.CoachMembershipId == coachId && existing.BranchId == body.BranchId && existing.SportId == body.SportId && existing.EffectiveFrom == body.EffectiveFrom && existing.EffectiveTo == body.EffectiveTo && existing.VariableMethod == body.VariableMethod && existing.VariableValue == body.VariableValue && existing.Eligibility == body.Eligibility && existing.BaseCoverage == body.BaseCoverage && existing.Note == body.Note?.Trim()
            ? Results.Ok(new { existing.Id, replay = true }) : Results.Problem(statusCode: 409, title: "مفتاح الحفظ مرتبط بشروط مختلفة؛ راجع النموذج.");
        if (body.EffectiveFrom < await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock) || await PayrollEndpoints.Locked(db, t.AcademyId, employee.Id, body.EffectiveFrom)) return Results.Problem(statusCode: 409, title: "اختر تاريخًا من اليوم في شهر راتب مفتوح؛ لا تغيير رجعي لاتفاق مستخدم.");
        var row = new CoachCompensationAgreement { Id = Guid.NewGuid(), AcademyId = t.AcademyId, EmployeeId = employee.Id, CoachMembershipId = coachId, BranchId = body.BranchId, SportId = body.SportId, EffectiveFrom = body.EffectiveFrom, EffectiveTo = body.EffectiveTo, BaseSalarySnapshot = await SalaryHistory.Rate(db,employee,body.EffectiveFrom), Currency = employee.Currency, VariableMethod = body.VariableMethod, VariableValue = body.VariableValue!.Value, Eligibility = body.Eligibility, BaseCoverage = body.BaseCoverage, Note = body.Note?.Trim(), CreatedByMembershipId = t.MembershipId, IdempotencyKey = key, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
        db.CoachCompensationAgreements.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Created($"/api/v1/manage/payroll/coaches/{coachId}/agreements/{row.Id}", new { row.Id });
    }
    private static async Task<IResult> Approve(Guid coachId, Guid id, CoachAgreementApprovalWrite body, HttpContext context, CurrentTenant current, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await current.ResolveAsync())!; await using var tx = await db.Database.BeginTransactionAsync();
        if (!await PayrollSensitiveAccess.LockAndAuthorize(context, current, db, AcademyPermissions.CoachAgreementManage)) return Results.Forbid();
        var row = await db.CoachCompensationAgreements.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.CoachMembershipId == coachId); if (row is null) return Results.NotFound();
        await PayrollEndpoints.Lock(db, t.AcademyId, row.EmployeeId, row.EffectiveFrom);
        var employee = await Employee(db, t, coachId); if (employee is null || employee.Id != row.EmployeeId || !await ValidScope(db, t, employee, row.BranchId, row.SportId)) return Results.Forbid();
        if (!await FitsContract(db,employee, row.EffectiveFrom, row.EffectiveTo)) return Results.Problem(statusCode: 409, title: "تغيرت مدة عقد الموظف؛ راجع نسخة الاتفاق قبل اعتمادها.");
        if (t.Role != AcademyRole.AcademyOwner && await db.AcademyMemberships.AnyAsync(m => m.Id == coachId && m.UserId.ToString() == context.User.FindFirstValue(ClaimTypes.NameIdentifier))) return Results.Forbid();
        var replay = await db.CoachCompensationApprovals.SingleOrDefaultAsync(p => p.AgreementId == id); if (replay is not null) return Results.Ok(new { row.Id, version = replay.Revision, replay = true });
        var latest = await db.CoachCompensationApprovals.Where(p => p.EmployeeId == row.EmployeeId && p.BranchId == row.BranchId && p.SportId == row.SportId).OrderByDescending(p => p.Revision).FirstOrDefaultAsync();
        var latestFrom = latest is null ? (DateOnly?)null : await db.CoachCompensationAgreements.Where(a => a.Id == latest.AgreementId).Select(a => a.EffectiveFrom).SingleAsync();
        if (body.ExpectedLatestAgreementId != latest?.AgreementId || row.EffectiveFrom < latestFrom || row.BaseSalarySnapshot != await SalaryHistory.Rate(db,employee,row.EffectiveFrom) || row.EffectiveFrom < await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock) || await PayrollEndpoints.Locked(db, t.AcademyId, employee.Id, row.EffectiveFrom)) return Results.Problem(statusCode: 409, title: "تغير الاتفاق أو الأساسي أو فترة السريان؛ أعد المراجعة، ولا تعدل نسخة تاريخية.");
        var approval = new CoachCompensationApproval { Id = Guid.NewGuid(), AcademyId = t.AcademyId, AgreementId = id, EmployeeId = row.EmployeeId, BranchId = row.BranchId, SportId = row.SportId, Revision = (latest?.Revision ?? 0) + 1, ApprovedByMembershipId = t.MembershipId, ApprovedAtUtc = clock.GetUtcNow(), CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
        db.CoachCompensationApprovals.Add(approval); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { row.Id, version = approval.Revision });
    }
    private static async Task<IResult> Own(CurrentTenant current, FoundationDbContext db, AcademyConfigurationService configuration, HttpResponse response)
    {
        var t = (await current.ResolveAsync())!; if (t.Role != AcademyRole.Coach || !await configuration.Enabled(t.AcademyId, "payroll")) return Results.Forbid();
        response.Headers.CacheControl = "no-store";
        return Results.Ok(new { items = await Views(db, t.AcademyId, t.MembershipId, true) });
    }
}
public sealed record CoachAgreementWrite(Guid BranchId, Guid SportId, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string VariableMethod, decimal? VariableValue, string Eligibility, string BaseCoverage, string? Note = null);
public sealed record CoachAgreementApprovalWrite(Guid? ExpectedLatestAgreementId);
public sealed record CoachAgreementView(Guid Id, Guid BranchId, Guid SportId, string Branch, string Sport, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string VariableMethod, decimal VariableValue, string Eligibility, string BaseCoverage, string Currency, decimal? BaseSalarySnapshot, string? Note, int? Version, DateTimeOffset? ApprovedAtUtc, string? ApprovedBy);
