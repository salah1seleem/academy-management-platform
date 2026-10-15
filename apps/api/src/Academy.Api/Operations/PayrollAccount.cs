using System.Globalization;
using System.Text;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Slice8;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

// A projection of approved sources, not a new accrual, payout or opening entry.
public static class PayrollAccount
{
    public static void MapPayrollAccount(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/manage/payroll/{id:guid}/account").RequireAuthorization(AcademyPermissions.PayrollRead);
        api.MapGet("", (Guid id, DateOnly? asOf, DateTimeOffset? cutoffUtc, CurrentTenant current, FoundationDbContext db, TimeProvider clock, HttpResponse response)
            => Read(id, asOf, cutoffUtc, false, current, db, clock, response));
        api.MapGet("/export", (Guid id, DateOnly? asOf, DateTimeOffset? cutoffUtc, CurrentTenant current, FoundationDbContext db, TimeProvider clock, HttpResponse response)
            => Read(id, asOf, cutoffUtc, true, current, db, clock, response));
        app.MapGet("/api/v1/staff/payroll/account", Own).RequireAuthorization(AcademyPermissions.TenantAccess);
    }

    private static async Task<IResult> Read(Guid id, DateOnly? asOf, DateTimeOffset? cutoffUtc, bool export, CurrentTenant current, FoundationDbContext db, TimeProvider clock, HttpResponse response)
    {
        var t = (await current.ResolveAsync())!;
        return await View(id, asOf, cutoffUtc, export, t, db, clock, response);
    }

    private static async Task<IResult> Own(DateOnly? asOf, DateTimeOffset? cutoffUtc, CurrentTenant current, FoundationDbContext db, AcademyConfigurationService config, TimeProvider clock, HttpResponse response)
    {
        var t = (await current.ResolveAsync())!;
        if (t.Role is not (AcademyRole.Coach or AcademyRole.AcademyAdmin) || !await config.Enabled(t.AcademyId, "payroll")) return Results.Forbid();
        response.Headers.CacheControl = "no-store";
        var id = await db.Employees.Where(e => e.AcademyId == t.AcademyId && e.MembershipId == t.MembershipId && e.IsActive).Select(e => (Guid?)e.Id).SingleOrDefaultAsync();
        return id is null ? Results.Ok(new { configured = false }) : await View(id.Value, asOf, cutoffUtc, false, t, db, clock, response);
    }

    private static async Task<IResult> View(Guid id, DateOnly? date, DateTimeOffset? requestedCutoff, bool export, TenantMembership t, FoundationDbContext db, TimeProvider clock, HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var e = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id && e.AcademyId == t.AcademyId);
        if (e is null) return Results.NotFound();
        var zoneId = await db.Academies.Where(a => a.Id == t.AcademyId).Select(a => a.TimeZone).SingleAsync();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var asOf = date ?? today;
        if (asOf.Year < 2000 || asOf > today) return Results.Problem(statusCode: 422, title: "اختر تاريخًا حتى اليوم الفعلي للأكاديمية.");
        // Today ends at the actual read instant, never at a future midnight.
        var latest = asOf == today ? now : DayBoundary(asOf.AddDays(1), zone);
        var cutoff = requestedCutoff?.ToUniversalTime() ?? latest;
        if (cutoff > latest || cutoff < DayBoundary(asOf, zone)) return Results.Problem(statusCode: 422, title: "توقيت الكشف خارج التاريخ المختار أو في المستقبل.");
        var month = SalaryHistory.Month(asOf);
        var statements = await db.PayrollStatements.AsNoTracking().Where(s => s.AcademyId == t.AcademyId && s.EmployeeId == id && s.Month <= month && s.CreatedAtUtc < cutoff && (s.Kind=="MonthlySalary"||s.OpeningAsOf<=asOf)).OrderByDescending(s => s.Month).ToArrayAsync();
        if (statements.Any(s => s.Currency != e.Currency)) return Results.Problem(statusCode: 409, title: "تحتاج عملات الكشف إلى مراجعة؛ لم نجمع عملتين.");
        var ids = statements.Select(s => s.Id).ToArray();
        var payments = await db.PayrollDisbursements.AsNoTracking().Where(p => p.AcademyId == t.AcademyId && ids.Contains(p.StatementId) && p.CreatedAtUtc < cutoff).OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Sequence).ToArrayAsync();
        PayrollAccountPayment[] PaymentItems(Guid source)=>payments.Where(p=>p.StatementId==source).Select(p=>new PayrollAccountPayment(p.Id,p.Amount,p.CreatedAtUtc,p.AdvanceId.HasValue?"AdvanceApplication":"DirectPayout")).ToArray();
        var openingSource=statements.SingleOrDefault(s=>s.Kind=="Opening");
        var opening=openingSource is null?null:new PayrollAccountOpening(openingSource.Id,openingSource.OpeningAsOf!.Value,openingSource.NetDue,payments.Where(p=>p.StatementId==openingSource.Id).Sum(p=>p.Amount),openingSource.CreatedAtUtc,PaymentItems(openingSource.Id));
        var items = statements.Where(s=>s.Kind=="MonthlySalary").Select(s =>
        {
            var paid = payments.Where(p => p.StatementId == s.Id).Sum(p => p.Amount);
            return new PayrollAccountItem(s.Id, s.Month, s.CreatedAtUtc, s.NetDue, paid, s.NetDue - paid, s.BaseDue, s.Deductions, s.Additions,
                payments.Where(p => p.StatementId == s.Id).Select(p => new PayrollAccountPayment(p.Id, p.Amount, p.CreatedAtUtc,p.AdvanceId.HasValue?"AdvanceApplication":"DirectPayout")).ToArray());
        }).ToArray();
        var prior = items.Where(s => s.Month < month).ToArray();
        var current = items.Where(s => s.Month == month).ToArray();
        var legacyCount = await db.PayrollSettlements.CountAsync(s => s.AcademyId == t.AcademyId && s.EmployeeId == id && s.Month <= month && s.CreatedAtUtc < cutoff);
        var advances=await db.PayrollAdvances.AsNoTracking().Where(a=>a.AcademyId==t.AcademyId&&a.EmployeeId==id&&a.CreatedAtUtc<cutoff).OrderByDescending(a=>a.CreatedAtUtc).ToArrayAsync();
        if(advances.Any(a=>a.Currency!=e.Currency))return Results.Problem(statusCode:409,title:"عملات السلف تحتاج مراجعة؛ لم نجمع عملتين.");
        var advanceItems=advances.Select(a=>new PayrollAccountAdvance(a.Id,a.Amount,payments.Where(p=>p.AdvanceId==a.Id).Sum(p=>p.Amount),a.Amount-payments.Where(p=>p.AdvanceId==a.Id).Sum(p=>p.Amount),a.CreatedAtUtc)).ToArray();
        var result = new PayrollAccountView(true, e.Name, e.Currency, asOf, cutoff, zoneId, items.Sum(s => s.NetDue)+(opening?.Amount??0), items.Sum(s => s.Paid)+(opening?.Paid??0), items.Sum(s => s.Remaining)+(opening?.Remaining??0),
            prior.Sum(s => s.Remaining)+(opening?.Remaining??0), current.Sum(s => s.Remaining), current.Length > 0, legacyCount, items) {Opening=opening,Advances=advanceItems,AdvanceApplied=payments.Where(p=>p.AdvanceId.HasValue).Sum(p=>p.Amount),DirectPaid=payments.Where(p=>!p.AdvanceId.HasValue).Sum(p=>p.Amount)};
        return export ? Export(result) : Results.Ok(result);
    }

    // Cairo has a skipped midnight at DST start and a repeated hour at DST end.
    private static DateTimeOffset DayBoundary(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private static IResult Export(PayrollAccountView v)
    {
        static string N(decimal n) => n.ToString("0.00", CultureInfo.InvariantCulture);
        var b = new StringBuilder("\uFEFF");
        static string Cell(string value) => ReportingEndpoints.CsvCell(value.TrimStart() is { Length: > 0 } v && "=+-@".Contains(v[0]) || value.StartsWith('\t') || value.StartsWith('\r') ? "'" + value : value);
        void Row(params string[] cells) => b.AppendLine(string.Join(",", cells.Select(Cell)));
        Row("كشف المصادر المعتمدة فقط", v.EmployeeName, v.Currency, v.AsOf.ToString("yyyy-MM-dd"), v.CutoffUtc.ToString("O"), v.TimeZone);
        Row(v.OpeningBalanceVerified?"افتتاح موثق قبل حد البداية فقط؛ ليس راتب شهر أو دليل اكتمال كل السلف التاريخية":"الافتتاح والسلف التاريخية غير المستوردة غير موثقة؛ المسدد القديم غير معاد ترحيله");
        if(v.Opening is {} o)Row("الافتتاح قبل",o.AsOf.ToString("yyyy-MM-dd"),N(o.Amount),N(o.Paid),N(o.Remaining));
        Row("شهر الخدمة", "الاستحقاق المعتمد", "المسدد حتى التاريخ", "المتبقي", "وقت الاعتماد");
        foreach (var s in v.Items) Row(s.Month.ToString("yyyy-MM-dd"), N(s.NetDue), N(s.Paid), N(s.Remaining), s.ApprovedAtUtc.ToString("O"));
        Row("الإجمالي المعروف", N(v.TotalDue), N(v.TotalPaid), N(v.Remaining));
        Row("متبقي الأشهر السابقة", N(v.PriorRemaining));
        Row("متبقي الشهر المختار", N(v.CurrentRemaining));
        Row("من السداد: صرف مباشر",N(v.DirectPaid));
        Row("من السداد: تخصيص سلفة وليس صرفًا جديدًا",N(v.AdvanceApplied));
        Row("السلف المصروفة المسجلة",N(v.RecordedAdvanceTotal));
        Row("السلف غير المخصصة — لا تخصم تلقائيًا",N(v.UnallocatedAdvances));
        Row("خروج المال المسجل: صرف مباشر + سلف، دون تكرار التخصيص",N(v.CashOutRecorded));
        Row("الدفعات — تفصيل للإجمالي أعلاه وليس استحقاقًا إضافيًا");
        Row("شهر الخدمة", "المبلغ", "وقت التسجيل","نوع التسوية");
        foreach (var s in v.Items) foreach (var p in s.Payments) Row(s.Month.ToString("yyyy-MM-dd"), N(p.Amount), p.CreatedAtUtc.ToString("O"),p.Kind=="AdvanceApplication"?"تخصيص سلفة سابقة":"صرف مباشر");
        if(v.Opening is {} opening)foreach(var p in opening.Payments)Row("تسوية افتتاح",N(p.Amount),p.CreatedAtUtc.ToString("O"),p.Kind=="AdvanceApplication"?"تخصيص سلفة سابقة — ليس صرفًا جديدًا":"صرف مباشر");
        Row("السلف — خروج مال سبق التخصيص وليس استحقاق راتب");
        Row("المبلغ","المخصص","غير المخصص","وقت التسجيل");
        foreach(var a in v.Advances)Row(N(a.Amount),N(a.Applied),N(a.Remaining),a.CreatedAtUtc.ToString("O"));
        return Results.File(Encoding.UTF8.GetBytes(b.ToString()), "text/csv; charset=utf-8", $"payroll-account-{v.AsOf:yyyy-MM-dd}.csv");
    }
}

public sealed record PayrollAccountPayment(Guid Id, decimal Amount, DateTimeOffset CreatedAtUtc,string Kind);
public sealed record PayrollAccountOpening(Guid Id,DateOnly AsOf,decimal Amount,decimal Paid,DateTimeOffset ApprovedAtUtc,PayrollAccountPayment[] Payments){public decimal Remaining=>Amount-Paid;}
public sealed record PayrollAccountAdvance(Guid Id,decimal Amount,decimal Applied,decimal Remaining,DateTimeOffset CreatedAtUtc);
public sealed record PayrollAccountItem(Guid Id, DateOnly Month, DateTimeOffset ApprovedAtUtc, decimal NetDue, decimal Paid, decimal Remaining, decimal BaseDue, decimal Deductions, decimal Additions, PayrollAccountPayment[] Payments);
public sealed record PayrollAccountView(bool Configured, string EmployeeName, string Currency, DateOnly AsOf, DateTimeOffset CutoffUtc, string TimeZone, decimal TotalDue, decimal TotalPaid, decimal Remaining, decimal PriorRemaining, decimal CurrentRemaining, bool CurrentMonthApproved, int LegacyPaidMonthsExcluded, PayrollAccountItem[] Items)
{
    public string BalanceScope => "ApprovedSourcesAsOfOnly";
    public bool OpeningBalanceVerified => Opening is not null;
    public PayrollAccountOpening? Opening {get;init;}
    public bool AdvancesVerified => false;
    public string AdvanceScope=>"RecordedSourcesOnly";
    public PayrollAccountAdvance[] Advances {get;init;}=[];
    public decimal AdvanceApplied {get;init;}
    public decimal DirectPaid {get;init;}
    public decimal RecordedAdvanceTotal=>Advances.Sum(a=>a.Amount);
    public decimal UnallocatedAdvances=>Advances.Sum(a=>a.Remaining);
    public decimal CashOutRecorded=>DirectPaid+RecordedAdvanceTotal;
}
