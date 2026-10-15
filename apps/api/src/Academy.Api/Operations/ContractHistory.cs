using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;

public static class ContractHistory
{
    internal sealed record Terms(DateOnly ContractStart, DateOnly? ContractEnd, Guid? VersionId = null);
    internal static ContractVersion? At(IEnumerable<ContractVersion> rows, Guid employee, DateOnly date) => rows.Where(v => v.EmployeeId == employee && v.EffectiveFrom <= SalaryHistory.Month(date)).OrderByDescending(v => v.EffectiveFrom).ThenByDescending(v => v.Revision).FirstOrDefault();
    internal static Terms Bounds(Employee e, IEnumerable<ContractVersion> rows, DateOnly date) => At(rows, e.Id, date) is { } v ? new(v.ContractStart, v.ContractEnd, v.Id) : new(e.ContractStart, e.ContractEnd);
    internal static async Task<Terms> Bounds(FoundationDbContext db, Employee e, DateOnly date) => Bounds(e, await db.ContractVersions.AsNoTracking().Where(v => v.EmployeeId == e.Id && v.AcademyId == e.AcademyId && v.EffectiveFrom <= SalaryHistory.Month(date)).ToArrayAsync(), date);
    internal static ContractVersion New(Employee e, TenantMembership t, Guid actor, string kind, DateOnly from, DateOnly start, DateOnly? end, int revision, Guid? previous, string key, string hash, DateTimeOffset now, string? note = null) => new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, EmployeeId = e.Id, BranchId = e.BranchId, Kind = kind, EffectiveFrom = from, ContractStart = start, ContractEnd = end, Revision = revision, PreviousVersionId = previous, ApprovedByMembershipId = t.MembershipId, ActorUserId = actor, RequestKey = key, RequestHash = hash, Note = note, CreatedAtUtc = now, UpdatedAtUtc = now };
    public static void MapContractHistory(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/manage/employees/{id:guid}/contract-versions").RequireAuthorization(AcademyPermissions.PayrollRead);
        api.MapGet("", Read);
        api.MapPost("", Change).RequireAuthorization(AcademyPermissions.PayrollManage, AcademyPermissions.SalaryManage).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/staff/payroll/contract-versions", Own).RequireAuthorization(AcademyPermissions.TenantAccess);
    }
    private static async Task<object> View(Employee e, TenantMembership t, FoundationDbContext db, TimeProvider clock, bool own = false)
    {
        var today = await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock);
        var rows = await db.ContractVersions.AsNoTracking().Where(v => v.EmployeeId == e.Id).OrderByDescending(v => v.Revision).ToArrayAsync();
        var current = Bounds(e, rows, today);
        return new { configured = true, e.Id, e.Name, today, currentMonth = SalaryHistory.Month(today), current.ContractStart, current.ContractEnd, hasVerifiedHistory = current.VersionId.HasValue, revision = rows.FirstOrDefault()?.Revision ?? 0,
            canChange = !own && e.IsActive && (t.Role == AcademyRole.AcademyOwner || !await PayrollSensitiveAccess.IsSelf(db, t, e.MembershipId)) && await PayrollSensitiveAccess.Allowed(db, t, AcademyPermissions.SalaryManage),
            items = rows.Select(v => new { v.Id, v.Kind, v.Revision, v.EffectiveFrom, v.ContractStart, v.ContractEnd, v.CreatedAtUtc, note = own ? null : v.Note }) };
    }
    private static async Task<IResult> Read(Guid id, CurrentTenant current, FoundationDbContext db, TimeProvider clock, HttpResponse response)
    {
        var t = (await current.ResolveAsync())!; response.Headers.CacheControl = "no-store";
        var e = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id);
        return e is null ? Results.NotFound() : Results.Ok(await View(e, t, db, clock));
    }
    private static async Task<IResult> Own(CurrentTenant current, FoundationDbContext db, TimeProvider clock, Academy.Api.Configuration.AcademyConfigurationService config, HttpResponse response)
    {
        var t = (await current.ResolveAsync())!;
        if (t.Role is not (AcademyRole.Coach or AcademyRole.AcademyAdmin) || !await config.Enabled(t.AcademyId, "payroll")) return Results.Forbid();
        response.Headers.CacheControl = "no-store";
        var e = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.MembershipId == t.MembershipId && e.IsActive);
        return e is null ? Results.Ok(new { configured = false }) : Results.Ok(await View(e, t, db, clock, true));
    }
    private static async Task<IResult> Change(Guid id, ContractChange body, HttpContext http, CurrentTenant current, FoundationDbContext db, TimeProvider clock)
    {
        var key = http.Request.Headers["Idempotency-Key"].ToString();
        if (key.Length is < 8 or > 100 || body.EffectiveFrom.Day != 1 || body.EffectiveFrom.Year is < 2000 or > 9998 || body.ContractStart.Year < 2000 || body.ContractEnd < body.ContractStart || body.ExpectedRevision < 0 || body.Note?.Length > 500) return Results.Problem(statusCode: 422, title: "راجع شهر السريان وتواريخ العقد؛ الملاحظة اختيارية.");
        var t = (await current.ResolveAsync())!;
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await PayrollSensitiveAccess.LockAndAuthorize(http, current, db, AcademyPermissions.SalaryManage)) return Results.Forbid();
        await PayrollEndpoints.Lock(db, t.AcademyId, id, body.EffectiveFrom);
        var e = await db.Employees.SingleOrDefaultAsync(e => e.Id == id && e.IsActive); if (e is null) return Results.NotFound();
        if (!await PayrollSensitiveAccess.BranchAllowed(db, t, e.BranchId) || t.Role != AcademyRole.AcademyOwner && await PayrollSensitiveAccess.IsSelf(db, t, e.MembershipId)) return Results.Forbid();
        var hash = StatementComparisonRules.Hash(new { id, body });
        var replay = await db.ContractVersions.SingleOrDefaultAsync(v => v.ApprovedByMembershipId == t.MembershipId && v.RequestKey == key);
        if (replay is not null) return replay.RequestHash == hash ? Results.Ok(new { replay.Id, replay = true }) : Results.Problem(statusCode: 409, title: "معرف التأكيد مستخدم بشروط مختلفة.");
        var month = SalaryHistory.Month(await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock));
        var latest = await db.ContractVersions.Where(v => v.EmployeeId == id).OrderByDescending(v => v.Revision).FirstOrDefaultAsync();
        if (body.EffectiveFrom < month || body.EffectiveFrom > month.AddMonths(12) || latest?.EffectiveFrom > body.EffectiveFrom || body.ExpectedRevision != (latest?.Revision ?? 0) || body.ContractStart >= body.EffectiveFrom.AddMonths(1) || body.ContractEnd < body.EffectiveFrom)
            return Results.Problem(statusCode: 409, title: "تغير السجل أو الشهر خارج المسموح؛ اختر شهرًا حاليًا أو مستقبليًا يغطيه العقد وحدّث المراجعة.");
        if (await db.PayrollStatements.AnyAsync(s => s.EmployeeId == id && s.Month >= body.EffectiveFrom) || await db.PayrollSettlements.AnyAsync(s => s.EmployeeId == id && s.Month >= body.EffectiveFrom)) return Results.Problem(statusCode: 409, title: "التغيير يمس كشفًا معتمدًا أو مسددًا؛ اختر شهرًا لاحقًا ولا تعدّل الماضي.");
        if (await db.PrivateCompensationEntries.AnyAsync(s => s.EmployeeId == id && s.Kind == "Correction" && s.DueDate >= body.EffectiveFrom && (body.ContractStart >= s.DueDate.AddMonths(1) || body.ContractEnd < s.DueDate))) return Results.Problem(statusCode: 409, title: "يوجد تصحيح عمولة مجدول خارج مدة العقد المقترحة؛ راجع تسويته أولًا.");
        var actor = await db.AcademyMemberships.Where(m => m.Id == t.MembershipId).Select(m => m.UserId).SingleAsync(); var now = clock.GetUtcNow();
        if (latest is null) { latest = New(e, t, actor, "LegacyBaseline", month, e.ContractStart, e.ContractEnd, 1, null, "baseline-" + Guid.NewGuid().ToString("N"), hash, now, "مرجع العقد القائم يبدأ شهر المراجعة فقط؛ التاريخ السابق غير موثق."); db.ContractVersions.Add(latest); await db.SaveChangesAsync(); }
        var v = New(e, t, actor, "Change", body.EffectiveFrom, body.ContractStart, body.ContractEnd, latest.Revision + 1, latest.Id, key, hash, now, body.Note?.Trim());
        db.ContractVersions.Add(v); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Created(http.Request.Path, new { v.Id, v.Revision });
    }
}
public sealed record ContractChange(DateOnly EffectiveFrom, DateOnly ContractStart, DateOnly? ContractEnd, int ExpectedRevision, string? Note = null);
