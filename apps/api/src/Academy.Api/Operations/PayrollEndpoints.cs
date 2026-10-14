using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class PayrollEndpoints
{
    private static Guid Actor(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static bool Money(decimal amount) => amount > 0 && amount <= 100_000_000 && decimal.Round(amount, 2) == amount;
    private static string? Key(HttpContext context) { var value = context.Request.Headers["Idempotency-Key"].ToString(); return value.Length is >= 8 and <= 100 ? value : null; }
    private static DateOnly Month(DateOnly date) => new(date.Year, date.Month, 1);
    internal static Task<bool> Locked(FoundationDbContext db, Guid academy, Guid employee, DateOnly date) => db.PayrollSettlements.AnyAsync(x => x.AcademyId == academy && x.EmployeeId == employee && x.Month == Month(date));
    internal static Task Lock(FoundationDbContext db, Guid academy, Guid employee, DateOnly date) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"payroll:{academy}:{employee}"}, 0))");

    public static void MapPayroll(this WebApplication app)
    {
        var employees = app.MapGroup("/api/v1/manage/employees").RequireAuthorization(AcademyPermissions.PayrollRead);
        employees.MapGet("/", Employees); employees.MapGet("/{id:guid}", EmployeeDetail);
        employees.MapPost("/", CreateEmployee).RequireAuthorization(AcademyPermissions.PayrollManage).AddEndpointFilter<CsrfFilter>(); employees.MapPut("/{id:guid}", EditEmployee).RequireAuthorization(AcademyPermissions.PayrollManage).AddEndpointFilter<CsrfFilter>();
        var payroll = app.MapGroup("/api/v1/manage/payroll").RequireAuthorization(AcademyPermissions.PayrollRead);
        payroll.MapGet("/", Report); payroll.MapGet("/adjustments", Adjustments); payroll.MapGet("/requests", Requests);
        payroll.MapPost("/adjustments", AddAdjustment).RequireAuthorization(AcademyPermissions.PayrollManage).AddEndpointFilter<CsrfFilter>();
        payroll.MapPost("/requests/{id:guid}/review", Review).RequireAuthorization(AcademyPermissions.PayrollManage, AcademyPermissions.PayrollAdditionReview).AddEndpointFilter<CsrfFilter>();
        payroll.MapPost("/{id:guid}/pay", Pay).RequireAuthorization(AcademyPermissions.PayrollManage, AcademyPermissions.PayrollPay).AddEndpointFilter<CsrfFilter>();
        payroll.MapGet("/reasons", Reasons); payroll.MapPost("/reasons", CreateReason).RequireAuthorization(AcademyPermissions.PayrollManage).AddEndpointFilter<CsrfFilter>();
        var self = app.MapGroup("/api/v1/staff/payroll").RequireAuthorization(AcademyPermissions.TenantAccess);
        self.MapGet("/", Own); self.MapPost("/requests", RequestAddition).AddEndpointFilter<CsrfFilter>();
    }

    private static async Task<IResult> Employees(CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        return Results.Ok(new {
            items = await db.Employees.Where(x => x.AcademyId == t.AcademyId).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.Phone, x.JobTitle, x.BranchId, x.MonthlySalary, x.Currency, x.ContractStart, x.ContractEnd, x.MembershipId, x.IsActive, photoUrl = x.PhotoAssetId == null ? null : "/api/v1/assets/" + x.PhotoAssetId }).ToListAsync(),
            branches = await db.Branches.Where(x => x.AcademyId == t.AcademyId && x.IsActive).Select(x => new { x.Id, name = x.ArabicName }).ToListAsync(),
            accounts = await db.AcademyMemberships.Where(x => x.AcademyId == t.AcademyId && x.PrimaryMembershipId == null && (x.Role == AcademyRole.Coach || x.Role == AcademyRole.AcademyAdmin) && x.IsActive && !db.Employees.Any(e => e.AcademyId == t.AcademyId && e.MembershipId == x.Id)).Select(x => new { x.Id, name = x.User.DisplayName, phone = x.User.PhoneNumber }).ToListAsync()
        });
    }
    private static async Task<IResult> EmployeeDetail(Guid id, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; var x = await db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id);
        return x is null ? Results.NotFound() : Results.Ok(x);
    }
    private static async Task<string?> ValidateEmployee(EmployeeWrite body, Guid academy, FoundationDbContext db, Guid? id = null)
    {
        var phone = EgyptPhoneNormalizer.Normalize(body.Phone);
        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 160 || string.IsNullOrWhiteSpace(body.JobTitle) || body.JobTitle.Length > 100 || phone is null || body.MonthlySalary < 0 || body.MonthlySalary > 100_000_000 || decimal.Round(body.MonthlySalary, 2) != body.MonthlySalary || body.ContractStart == default || body.ContractEnd < body.ContractStart) return "أدخل بيانات موظف وراتب وتواريخ عقد صحيحة.";
        if (!await db.Branches.AnyAsync(x => x.AcademyId == academy && x.Id == body.BranchId && x.IsActive)) return "اختر فرعًا فعالًا من الأكاديمية.";
        if (body.MembershipId.HasValue && !await db.AcademyMemberships.AnyAsync(x => x.AcademyId == academy && x.Id == body.MembershipId && x.PrimaryMembershipId == null && x.IsActive && (x.Role == AcademyRole.Coach || x.Role == AcademyRole.AcademyAdmin))) return "اختر حساب موظف صالحًا داخل الأكاديمية، وليس سياق صلاحيات إضافيًا.";
        if (await db.Employees.AnyAsync(x => x.AcademyId == academy && x.Id != id && (x.Phone == phone || body.MembershipId.HasValue && x.MembershipId == body.MembershipId))) return "الهاتف أو الحساب مرتبط بموظف آخر.";
        if (body.PhotoAssetId.HasValue && !await db.PrivateAssets.AnyAsync(x => x.AcademyId == academy && x.Id == body.PhotoAssetId && x.Purpose == "StaffPhoto" && x.IsActive)) return "اختر صورة موظف مرفوعة في هذه الأكاديمية.";
        return null;
    }
    private static void Apply(Employee x, EmployeeWrite body)
    { x.Name = body.Name.Trim(); x.Phone = EgyptPhoneNormalizer.Normalize(body.Phone)!; x.JobTitle = body.JobTitle.Trim(); x.BranchId = body.BranchId; x.MonthlySalary = body.MonthlySalary; x.ContractStart = body.ContractStart; x.ContractEnd = body.ContractEnd; x.MembershipId = body.MembershipId; x.PhotoAssetId = body.PhotoAssetId; x.IsActive = body.IsActive; }
    private static async Task<IResult> CreateEmployee(EmployeeWrite body, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"employee-create:{t.AcademyId}"}, 0))");
        var error = await ValidateEmployee(body, t.AcademyId, db); if (error is not null) return Results.UnprocessableEntity(new { message = error });
        var x = new Employee { Id = Guid.NewGuid(), AcademyId = t.AcademyId, Name = "", Phone = "", JobTitle = "", Currency = await db.Academies.Where(x => x.Id == t.AcademyId).Select(x => x.DefaultCurrency).SingleAsync(), CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
        Apply(x, body); db.Employees.Add(x); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Created($"/api/v1/manage/employees/{x.Id}", new { x.Id });
    }
    private static async Task<IResult> EditEmployee(Guid id, EmployeeWrite body, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"employee-create:{t.AcademyId}"}, 0))");
        await Lock(db, t.AcademyId, id, body.ContractStart);
        var x = await db.Employees.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (x is null) return Results.NotFound();
        var error = await ValidateEmployee(body, t.AcademyId, db, id); if (error is not null) return Results.UnprocessableEntity(new { message = error });
        if (body.MembershipId != x.MembershipId && (await db.PayrollSettlements.AnyAsync(s => s.AcademyId == t.AcademyId && s.EmployeeId == id) || await db.CoachCompensationAgreements.AnyAsync(a => a.EmployeeId == id))) return Results.Conflict(new { message = "لا يمكن تغيير ربط الحساب بعد سداد راتب أو إنشاء اتفاق مدرب. أنشئ ربطًا جديدًا موثقًا." });
        Apply(x, body); x.UpdatedAtUtc = clock.GetUtcNow(); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
    }

    internal static async Task<List<PayrollRow>> Rows(Guid academy, DateOnly month, Guid? branchId, FoundationDbContext db)
    {
        var first = Month(month); var last = first.AddMonths(1).AddDays(-1);
        var employees = await db.Employees.Where(x => x.AcademyId == academy && (!branchId.HasValue || x.BranchId == branchId) && (x.IsActive || db.PayrollSettlements.Any(p => p.AcademyId == academy && p.EmployeeId == x.Id && p.Month == first))).OrderBy(x => x.Name).ToListAsync();
        var adjustments = await db.PayrollAdjustments.Where(x => x.AcademyId == academy && x.DueDate >= first && x.DueDate <= last).ToListAsync();
        var daily = await db.StaffDailyAttendances.Where(x => x.AcademyId == academy && x.WorkDate >= first && x.WorkDate <= last).ToListAsync();
        var paid = await db.PayrollSettlements.Where(x => x.AcademyId == academy && x.Month == first).ToListAsync();
        return employees.Select(e => {
            var snapshot = paid.SingleOrDefault(p => p.EmployeeId == e.Id); var basic = PayrollCalculator.BaseDue(e.MonthlySalary, e.ContractStart, e.ContractEnd, first);
            var deduct = adjustments.Where(x => x.EmployeeId == e.Id && x.Kind == "Deduction").Sum(x => x.Amount) + daily.Where(x => x.MembershipId == e.MembershipId).Sum(x => x.ApprovedDeduction);
            var add = adjustments.Where(x => x.EmployeeId == e.Id && x.Kind == "Addition").Sum(x => x.Amount);
            return new PayrollRow(e.Id, e.Name, e.JobTitle, e.BranchId, snapshot?.MonthlySalary ?? e.MonthlySalary, snapshot?.ContractDays ?? basic.Days, snapshot?.BaseDue ?? basic.Due, snapshot?.Deductions ?? deduct, snapshot?.Additions ?? add, snapshot?.NetDue ?? basic.Due - deduct + add, e.Currency, snapshot is not null, snapshot?.PaidAtUtc);
        }).ToList();
    }
    private static async Task<IResult> Report(DateOnly? month, Guid? branchId, string? search, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var first = Month(month ?? await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock));
        if (first.Year is < 2000 or > 9998) return Results.UnprocessableEntity();
        var rows = await Rows(t.AcademyId, first, branchId, db); if (!string.IsNullOrWhiteSpace(search)) rows = rows.Where(x => x.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return Results.Ok(new { month = first, currency = await db.Academies.Where(x => x.Id == t.AcademyId).Select(x => x.DefaultCurrency).SingleAsync(), totalBase = rows.Sum(x => x.BaseDue), totalDeductions = rows.Sum(x => x.Deductions), totalAdditions = rows.Sum(x => x.Additions), totalNet = rows.Sum(x => x.NetDue), unpaid = rows.Where(x => !x.IsPaid).Sum(x => x.NetDue), items = rows });
    }
    private static async Task<IResult> Adjustments(Guid? employeeId, string? kind, CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!;
        if (!string.IsNullOrEmpty(kind) && kind is not ("Deduction" or "Addition")) return Results.UnprocessableEntity();
        var rows = await db.PayrollAdjustments.Where(x => x.AcademyId == t.AcademyId && (!employeeId.HasValue || x.EmployeeId == employeeId) && (string.IsNullOrEmpty(kind) || x.Kind == kind)).OrderByDescending(x => x.DueDate).Take(1000).Select(x => new AdjustmentRow(x.Id, x.EmployeeId, db.Employees.Where(e => e.AcademyId == t.AcademyId && e.Id == x.EmployeeId).Select(e => e.Name).First()!, x.Kind, x.Amount, x.Currency, x.DueDate, x.Reason, "Manual")).ToListAsync();
        if (string.IsNullOrEmpty(kind) || kind == "Deduction")
        {
            var daily = await (from day in db.StaffDailyAttendances
                join employee in db.Employees on new { day.AcademyId, MembershipId = (Guid?)day.MembershipId } equals new { employee.AcademyId, employee.MembershipId }
                where day.AcademyId == t.AcademyId && day.ApprovedDeduction > 0 && (!employeeId.HasValue || employee.Id == employeeId)
                orderby day.WorkDate descending
                select new AdjustmentRow(day.Id, employee.Id, employee.Name, "Deduction", day.ApprovedDeduction, employee.Currency, day.WorkDate, day.DeductionReason, "Attendance")).Take(1000).ToListAsync();
            rows.AddRange(daily);
        }
        return Results.Ok(rows.OrderByDescending(x => x.DueDate).Take(1000));
    }
    private static async Task<IResult> AddAdjustment(AdjustmentWrite body, HttpContext context, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var key = Key(context); var actor = Actor(context.User);
        if (key is null || !Money(body.Amount) || body.Kind != "Deduction" || string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length > 500 || body.DueDate == default) return Results.UnprocessableEntity(new { message = "أدخل مبلغ الخصم وتاريخ الاستحقاق وسبب الخصم." });
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"payroll-adjustment:{t.AcademyId}:{actor}:{key}"}, 0))");
        await Lock(db, t.AcademyId, body.EmployeeId, body.DueDate);
        var previous = await db.PayrollAdjustments.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.PerformedByUserId == actor && x.IdempotencyKey == key);
        if (previous is not null) return previous.EmployeeId == body.EmployeeId && previous.Amount == body.Amount && previous.DueDate == body.DueDate && previous.Reason == body.Reason.Trim() ? Results.Ok(new { previous.Id, replay = true }) : Results.Conflict(new { message = "مفتاح العملية مستخدم لبيانات أخرى." });
        var employee = await db.Employees.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == body.EmployeeId && x.IsActive); if (employee is null) return Results.NotFound();
        if (await Locked(db, t.AcademyId, employee.Id, body.DueDate)) return Results.Conflict(new { message = "راتب هذا الشهر سُدد؛ لا يمكن تغيير قيوده." });
        var row = new PayrollAdjustment { Id = Guid.NewGuid(), AcademyId = t.AcademyId, EmployeeId = employee.Id, Kind = "Deduction", Amount = body.Amount, Currency = employee.Currency, DueDate = body.DueDate, Reason = body.Reason.Trim(), PerformedByUserId = actor, IdempotencyKey = key, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
        db.PayrollAdjustments.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Created("/api/v1/manage/payroll/adjustments", new { row.Id });
    }
    private static async Task<IResult> Reasons(CurrentTenant tenant, FoundationDbContext db) { var t = (await tenant.ResolveAsync())!; return Results.Ok(await db.PayrollReasons.Where(x => x.AcademyId == t.AcademyId && x.IsActive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync()); }
    private static async Task<IResult> CreateReason(ReasonWrite body, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    { var t = (await tenant.ResolveAsync())!; if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 100) return Results.UnprocessableEntity(); await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"payroll-reasons:{t.AcademyId}"}, 0))"); if (await db.PayrollReasons.AnyAsync(x => x.AcademyId == t.AcademyId && x.Name == body.Name.Trim())) return Results.Conflict(new { message = "السبب موجود بالفعل." }); var row = new PayrollReason { Id = Guid.NewGuid(), AcademyId = t.AcademyId, Name = body.Name.Trim(), CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() }; db.PayrollReasons.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Created("/api/v1/manage/payroll/reasons", new { row.Id }); }
    private static async Task<IResult> Own(CurrentTenant tenant, FoundationDbContext db)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role is not (AcademyRole.Coach or AcademyRole.AcademyAdmin)) return Results.Forbid();
        var e = await db.Employees.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.MembershipId == t.MembershipId && x.IsActive); if (e is null) return Results.Ok(new { configured = false });
        return Results.Ok(new { configured = true, employee = new { e.Id, e.Name }, reasons = await db.PayrollReasons.Where(x => x.AcademyId == t.AcademyId && x.IsActive).Select(x => new { x.Id, x.Name }).ToListAsync(), requests = await db.PayrollAdditionRequests.Where(x => x.AcademyId == t.AcademyId && x.EmployeeId == e.Id).OrderByDescending(x => x.CreatedAtUtc).Take(100).Select(x => new { x.Id, Amount = x.Status == "Approved" ? (decimal?)x.Amount : null, x.Currency, x.ReasonName, x.Status, x.DueDate, x.ReviewNote }).ToListAsync() });
    }
    private static async Task<IResult> RequestAddition(AdditionWrite body, HttpContext context, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; if (t.Role is not (AcademyRole.Coach or AcademyRole.AcademyAdmin)) return Results.Forbid(); var key = Key(context); var actor = Actor(context.User);
        if (key is null || body.Amount.HasValue) return Results.UnprocessableEntity(new { message = "اختر سبب الطلب فقط؛ المسؤول يحدد المبلغ عند الاعتماد." });
        await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"payroll-request:{t.AcademyId}:{actor}:{key}"}, 0))");
        var e = await db.Employees.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.MembershipId == t.MembershipId && x.IsActive); var reason = await db.PayrollReasons.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == body.ReasonId && x.IsActive); if (e is null || reason is null) return Results.NotFound();
        var other = body.OtherReason?.Trim(); var isOther = reason.Name == "سبب آخر";
        if (isOther && (string.IsNullOrWhiteSpace(other) || other.Length > 80) || !isOther && !string.IsNullOrWhiteSpace(other)) return Results.UnprocessableEntity(new { message = "اكتب تفاصيل السبب الآخر فقط عند اختياره." });
        var reasonName = isOther ? $"سبب آخر: {other}" : reason.Name;
        if (reasonName.Length > 100) return Results.UnprocessableEntity(new { message = "تفاصيل السبب طويلة جدًا." });
        var old = await db.PayrollAdditionRequests.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.RequestedByUserId == actor && x.IdempotencyKey == key); if (old is not null) return old.ReasonId == body.ReasonId && old.ReasonName == reasonName ? Results.Ok(new { old.Id, replay = true }) : Results.Conflict();
        var row = new PayrollAdditionRequest { Id = Guid.NewGuid(), AcademyId = t.AcademyId, EmployeeId = e.Id, ReasonId = reason.Id, ReasonName = reasonName, Amount = 0, Currency = e.Currency, Status = "Pending", RequestedByUserId = actor, IdempotencyKey = key, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() }; db.PayrollAdditionRequests.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Created("/api/v1/staff/payroll/", new { row.Id });
    }
    private static async Task<IResult> Requests(CurrentTenant tenant, FoundationDbContext db)
    { var t = (await tenant.ResolveAsync())!; return Results.Ok(await db.PayrollAdditionRequests.Where(x => x.AcademyId == t.AcademyId).OrderByDescending(x => x.CreatedAtUtc).Take(1000).Select(x => new { x.Id, x.EmployeeId, name = db.Employees.Where(e => e.AcademyId == t.AcademyId && e.Id == x.EmployeeId).Select(e => e.Name).FirstOrDefault(), Amount = x.Status == "Approved" ? (decimal?)x.Amount : null, x.Currency, x.ReasonName, x.Status, x.DueDate, x.ReviewNote }).ToListAsync()); }
    private static async Task<IResult> Review(Guid id, AdditionReview body, HttpContext context, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var actor = Actor(context.User); if (body.Note?.Length > 500 || body.Accept && (body.DueDate is null || body.DueDate.Value.Year is < 2000 or > 9998 || body.Amount is null || !Money(body.Amount.Value)) || !body.Accept && body.Amount.HasValue) return Results.UnprocessableEntity(new { message = "حدد المبلغ الصحيح وتاريخ الاستحقاق عند قبول الطلب فقط." });
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await PayrollSensitiveAccess.LockAndAuthorize(context, tenant, db, AcademyPermissions.PayrollAdditionReview)) return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"payroll-review:{t.AcademyId}:{id}"}, 0))");
        var row = await db.PayrollAdditionRequests.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id); if (row is null) return Results.NotFound();
        await Lock(db, t.AcademyId, row.EmployeeId, body.DueDate ?? new DateOnly(2000, 1, 1));
        var branch = await db.Employees.Where(e => e.Id == row.EmployeeId).Select(e => (Guid?)e.BranchId).SingleOrDefaultAsync();
        if (branch is null || !await PayrollSensitiveAccess.BranchAllowed(db, t, branch.Value)) return Results.Forbid();
        if (row.RequestedByUserId == actor) return Results.Forbid(); // A delegated approver cannot approve their own claim.
        var state = body.Accept ? "Approved" : "Rejected";
        if (row.Status != "Pending") return row.Status == state && row.DueDate == (body.Accept ? body.DueDate : null) && row.Amount == (body.Accept ? body.Amount : row.Amount) && row.ReviewNote == body.Note?.Trim() ? Results.Ok(new { row.Id, replay = true }) : Results.Conflict(new { message = "الطلب تمت مراجعته بالفعل." });
        if (body.Accept)
        {
            await Lock(db, t.AcademyId, row.EmployeeId, body.DueDate!.Value); if (await Locked(db, t.AcademyId, row.EmployeeId, body.DueDate.Value)) return Results.Conflict(new { message = "راتب شهر الاستحقاق سُدد؛ اختر شهرًا مفتوحًا." });
            row.Amount = body.Amount!.Value;
            db.PayrollAdjustments.Add(new PayrollAdjustment { Id = Guid.NewGuid(), AcademyId = t.AcademyId, EmployeeId = row.EmployeeId, Kind = "Addition", DueDate = body.DueDate.Value, Amount = row.Amount, Currency = row.Currency, Reason = row.ReasonName, AdditionRequestId = row.Id, PerformedByUserId = actor, IdempotencyKey = "request-" + row.Id, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() });
        }
        row.Status = state; row.DueDate = body.Accept ? body.DueDate : null; row.ReviewNote = body.Note?.Trim(); row.ReviewedByUserId = actor; row.ReviewedAtUtc = clock.GetUtcNow(); row.UpdatedAtUtc = clock.GetUtcNow();
        var userId = row.RequestedByUserId; db.AcademyNotices.Add(new AcademyNotice { Id = Guid.NewGuid(), AcademyId = t.AcademyId, UserId = userId, Message = body.Accept ? "تم اعتماد طلب المبلغ الإضافي." : "تم رفض طلب المبلغ الإضافي." + (string.IsNullOrWhiteSpace(body.Note) ? "" : " " + body.Note.Trim()), CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { row.Id, row.Status });
    }
    private static async Task<IResult> Pay(Guid id, SalaryPay body, HttpContext context, CurrentTenant tenant, FoundationDbContext db, TimeProvider clock)
    {
        var t = (await tenant.ResolveAsync())!; var key = Key(context); var month = Month(body.Month);
        if (key is null || body.Month.Year is < 2000 or > 9998 || month > Month(await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock))
            || body.ExpectedNetDue is null or < 0 || decimal.Round(body.ExpectedNetDue.Value, 2) != body.ExpectedNetDue)
            return Results.UnprocessableEntity(new { message = "راجع الشهر والمبلغ المعروض قبل تأكيد السداد." });
        var actor = Actor(context.User);
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await PayrollSensitiveAccess.LockAndAuthorize(context, tenant, db, AcademyPermissions.PayrollPay)) return Results.Forbid();
        var branch = await db.Employees.Where(e => e.Id == id).Select(e => (Guid?)e.BranchId).SingleOrDefaultAsync();
        if (branch is null) return Results.NotFound();
        if (!await PayrollSensitiveAccess.BranchAllowed(db, t, branch.Value)) return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"payroll-pay:{t.AcademyId}:{actor}:{key}"}, 0))");
        var reusedKey = await db.PayrollSettlements.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.PaidByUserId == actor && x.IdempotencyKey == key);
        if (reusedKey is not null && (reusedKey.EmployeeId != id || reusedKey.Month != month || reusedKey.NetDue != body.ExpectedNetDue)) return Results.Conflict(new { message = "مفتاح السداد مستخدم لموظف أو شهر أو مبلغ آخر." });
        await Lock(db, t.AcademyId, id, month);
        var existing = await db.PayrollSettlements.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.EmployeeId == id && x.Month == month);
        if (existing is not null) return existing.NetDue == body.ExpectedNetDue ? Results.Ok(new { existing.Id, replay = true }) : Results.Conflict(new { message = "هذا الراتب سُدد بمبلغ مختلف. حدّث القائمة لمراجعة السداد." });
        var row = (await Rows(t.AcademyId, month, null, db)).SingleOrDefault(x => x.EmployeeId == id); if (row is null) return Results.NotFound(); if (row.NetDue < 0 || row.ContractDays == 0) return Results.Conflict(new { message = "راجع مبلغ الراتب وأيام العقد قبل السداد." });
        if (row.NetDue != body.ExpectedNetDue) return Results.Conflict(new { message = "تغيرت المستحقات منذ عرضها. راجع الصافي المحدّث ثم أكد السداد." });
        var snapshot = new PayrollSettlement { Id = Guid.NewGuid(), AcademyId = t.AcademyId, EmployeeId = id, Month = month, MonthlySalary = row.MonthlySalary, ContractDays = row.ContractDays, BaseDue = row.BaseDue, Deductions = row.Deductions, Additions = row.Additions, NetDue = row.NetDue, Currency = row.Currency, PaidAtUtc = clock.GetUtcNow(), PaidByUserId = Actor(context.User), IdempotencyKey = key, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
        db.PayrollSettlements.Add(snapshot); await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Created("/api/v1/manage/payroll", new { snapshot.Id });
    }
}
public sealed record EmployeeWrite(string Name, string Phone, string JobTitle, Guid BranchId, decimal MonthlySalary, DateOnly ContractStart, DateOnly? ContractEnd, Guid? MembershipId, Guid? PhotoAssetId, bool IsActive = true);
public sealed record AdjustmentWrite(Guid EmployeeId, string Kind, decimal Amount, DateOnly DueDate, string? Reason);
public sealed record AdditionWrite(Guid ReasonId, string? OtherReason = null, decimal? Amount = null);
public sealed record AdditionReview(bool Accept, DateOnly? DueDate, string? Note, decimal? Amount = null);
public sealed record SalaryPay(DateOnly Month, decimal? ExpectedNetDue = null);
public sealed record ReasonWrite(string Name);
public sealed record PayrollRow(Guid EmployeeId, string Name, string JobTitle, Guid BranchId, decimal MonthlySalary, int ContractDays, decimal BaseDue, decimal Deductions, decimal Additions, decimal NetDue, string Currency, bool IsPaid, DateTimeOffset? PaidAtUtc);
public sealed record AdjustmentRow(Guid Id, Guid EmployeeId, string Name, string Kind, decimal Amount, string Currency, DateOnly DueDate, string? Reason, string Source);
