using System.Globalization;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Slice3;
using Academy.Infrastructure.Finance;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;

public sealed record OperatingStatementLine(string Key,string Label,decimal Amount,string SourceUrl);
public sealed record OperatingStatementData(DateOnly From,DateOnly To,string Currency,string Basis,
    OperatingStatementLine[] Income,OperatingStatementLine[] Expenses,decimal TotalIncome,decimal TotalExpenses,decimal Surplus,
    decimal Refunds,decimal Discounts,decimal Receivables,decimal ApprovedPayrollPayable,decimal UnpaidOperatingExpenses,
    decimal AdvanceCashOut,decimal AdvanceApplied,decimal? EarnedRevenue,string[] Warnings);

public static partial class ReportingEndpoints
{
    private static async Task<IResult> OperatingStatement(DateOnly? from,DateOnly? to,bool? allDates,Guid? branchId,Guid? sportId,string? format,
        CurrentTenant tenant,FoundationDbContext db,ISubscriptionClock clock,HttpResponse response)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.AcademyOwner)return Results.Forbid();
        response.Headers.CacheControl="private, no-store";
        var range=await FinancialRange(t.AcademyId,from,to,db,clock,allDates==true);if(range.Error!=null)return range.Error;
        if(format is not (null or "csv"))return Validation("format","صيغة التقرير غير متاحة.");
        if(branchId.HasValue&&!await db.Branches.AnyAsync(b=>b.Id==branchId&&b.AcademyId==t.AcademyId))return Results.NotFound();
        if(sportId.HasValue&&!await db.Sports.AnyAsync(s=>s.Id==sportId&&s.AcademyId==t.AcademyId))return Results.NotFound();
        var d=await BuildOperatingStatement(t.AcademyId,range,branchId,sportId,db,clock);
        if(format!="csv")return Results.Ok(d);
        var rows=new List<string[]>{new[]{"قائمة تشغيلية بالأساس النقدي","من",d.From.ToString("yyyy-MM-dd"),"إلى",d.To.ToString("yyyy-MM-dd"),d.Currency},new[]{"القسم","البند","المبلغ"}};
        rows.AddRange(d.Income.Select(x=>new[]{"مقبوضات",x.Label,x.Amount.ToString("0.00",CultureInfo.InvariantCulture)}));
        rows.AddRange(d.Expenses.Select(x=>new[]{"مدفوعات تشغيلية",x.Label,x.Amount.ToString("0.00",CultureInfo.InvariantCulture)}));
        rows.Add(["الإجمالي","الفائض / العجز النقدي",d.Surplus.ToString("0.00",CultureInfo.InvariantCulture)]);
        rows.AddRange(d.Warnings.Select(w=>new[]{"ملاحظة",w,""}));return Csv(rows,"operating-statement.csv");
    }

    // Reuses receipt attribution and refund queries used by the collections report.
    // This cash-basis projection is NOT earned revenue or a new accounting ledger.
    private static async Task<OperatingStatementData> BuildOperatingStatement(Guid academy,FinancialRangeResult r,Guid? branch,Guid? sport,FoundationDbContext db,ISubscriptionClock clock)
    {
        var query=FinancialQuery(academy,r.Currency,r.StartUtc,r.EndUtcExclusive,sport,branch,null,null,null,db);
        var privateQuery=query.Where(c=>c.RenewalRequest.TrainingFormatSnapshot==TrainingFormat.Private||c.RenewalRequest.TrainingFormatSnapshot==TrainingFormat.SemiPrivate);
        var privateIncome=await privateQuery.SumAsync(c=>(decimal?)c.Amount)??0;
        var subscriptions=(await query.SumAsync(c=>(decimal?)c.Amount)??0)-privateIncome;
        var products=await ProductFinancialQuery(academy,r.Currency,r.StartUtc,r.EndUtcExclusive,sport,branch,null,null,db).SumAsync(c=>(decimal?)c.Amount)??0;
        var tournaments=await TournamentQuery(academy,r.Currency,r.StartUtc,r.EndUtcExclusive,sport,branch,null,null,db).SumAsync(c=>(decimal?)c.Amount)??0;
        var visits=await DayPassFinancialQuery(academy,r.Currency,r.StartUtc,r.EndUtcExclusive,sport,branch,null,null,null,db).SumAsync(c=>(decimal?)c.Amount)??0;
        var refunds=await RefundFinancialQuery(academy,r.Currency,r.StartUtc,r.EndUtcExclusive,sport,branch,null,null,null,db).SumAsync(c=>(decimal?)c.Amount)??0;
        var discounts=await db.Receipts.Where(x=>query.Any(c=>c.Id==x.CollectionId)).SumAsync(x=>(decimal?)x.DiscountAmount)??0;
        var income=new List<OperatingStatementLine>{new("subscription","اشتراكات محصلة",subscriptions,"/dashboard/reports/financial?collectionType=Subscription"),new("private","برايفت محصل",privateIncome,"/dashboard/reports/financial?collectionType=Subscription"),new("product","مبيعات منتجات محصلة",products,"/dashboard/reports/financial?collectionType=Product"),new("tournament","رسوم بطولات محصلة",tournaments,"/dashboard/reports/financial?collectionType=Tournament"),new("refund","استردادات وعكس إيصالات",-refunds,"/dashboard/reports/financial")};
        if(visits!=0)income.Add(new("visits","زيارات وخدمات مرافق محصلة",visits,"/dashboard/reports/financial?collectionType=DayPass"));
        var warnings=new List<string>{"أساس نقدي: المقبوض ليس إيرادًا مكتسبًا بالضرورة. لا يحدد هذا التقرير التزامات الاشتراكات المؤجلة أو ربح الاستحقاق.","العقود الخارجية والتأجير غير مكتملة في المصدر الحالي؛ عدم عرضها لا يعني أن قيمتها صفر.","المصروفات غير المسجلة لا تُفترض صفرًا. لا توجد تكلفة مخزون موثقة للمنتجات ضمن هذه القائمة."};
        var expenses=new List<OperatingStatementLine>();
        var operating=db.OperatingExpenses.Where(e=>e.AcademyId==academy&&e.Currency==r.Currency&&e.Status==OperatingExpenseStatus.Posted&&(!branch.HasValue||e.BranchId==branch));
        if(sport.HasValue)operating=operating.Where(e=>e.SportId==sport||e.TrainingGroupId.HasValue&&db.TrainingGroups.Any(g=>g.Id==e.TrainingGroupId&&g.SportId==sport));
        var categoryRows=await operating.Where(e=>e.PaidOn>=r.From&&e.PaidOn<=r.To).GroupBy(e=>e.Category).Select(g=>new{key=g.Key,amount=g.Sum(e=>e.Amount)}).ToArrayAsync();
        foreach(var c in categoryRows)expenses.Add(new(c.key,ExpenseLabel(c.key),c.amount,"/dashboard/finance/operating-expenses"));
        decimal payrollCash=0,privateCash=0,advanceCash=0,advanceApplied=0,payable=0;
        if(!sport.HasValue)
        {
            var payments=await (from p in db.PayrollDisbursements.AsNoTracking() join s in db.PayrollStatements.AsNoTracking() on p.StatementId equals s.Id
                where p.AcademyId==academy&&p.Currency==r.Currency&&p.CreatedAtUtc>=r.StartUtc&&p.CreatedAtUtc<r.EndUtcExclusive&&(!branch.HasValue||p.BranchId==branch)
                select new{p.Amount,p.AdvanceId,s.NetDue,s.PrivateCompensation,s.Kind}).ToArrayAsync();
            payrollCash=payments.Where(p=>p.AdvanceId==null).Sum(p=>p.Amount);
            // Explicit proportional disclosure only for mixed payroll cash settlements.
            privateCash=payments.Where(p=>p.AdvanceId==null&&p.NetDue>0).Sum(p=>Math.Round(p.Amount*Math.Clamp(p.PrivateCompensation,0,p.NetDue)/p.NetDue,2));
            advanceApplied=payments.Where(p=>p.AdvanceId!=null).Sum(p=>p.Amount);
            var legacy=await (from p in db.PayrollSettlements join e in db.Employees on p.EmployeeId equals e.Id where p.AcademyId==academy&&p.Currency==r.Currency&&p.PaidAtUtc>=r.StartUtc&&p.PaidAtUtc<r.EndUtcExclusive&&(!branch.HasValue||e.BranchId==branch) select (decimal?)p.NetDue).SumAsync()??0;
            payrollCash+=legacy;
            advanceCash=await db.PayrollAdvances.Where(a=>a.AcademyId==academy&&a.Currency==r.Currency&&a.CreatedAtUtc>=r.StartUtc&&a.CreatedAtUtc<r.EndUtcExclusive&&(!branch.HasValue||a.BranchId==branch)).SumAsync(a=>(decimal?)a.Amount)??0;
            payable=await db.PayrollStatements.Where(s=>s.AcademyId==academy&&s.Currency==r.Currency&&s.CreatedAtUtc<r.EndUtcExclusive&&(!branch.HasValue||s.BranchId==branch)).SumAsync(s=>(decimal?)(s.NetDue-db.PayrollDisbursements.Where(p=>p.StatementId==s.Id&&p.CreatedAtUtc<r.EndUtcExclusive).Sum(p=>p.Amount)))??0;
            expenses.Add(new("salary","رواتب وإضافات مسددة — دون عمولة الخاص المفصلة",payrollCash-privateCash,"/dashboard/payroll"));
            expenses.Add(new("commission","عمولات خاص ضمن الصرف المسجل",privateCash,"/dashboard/academy/coaches"));
            warnings.Add("تقسيم الصرف الجزئي بين العمولة وبقية الراتب نسبي من بنود الكشف المعتمد، لا تكلفة إضافية. صرف السلف منفصل، وتخصيصها ليس خروج مال جديدًا.");
        }
        else warnings.Add("فلتر الرياضة يستبعد الرواتب المشتركة ومصروفات الفرع أو المرفق غير المنسوبة لرياضة؛ النتيجة جزئية وليست ربحية الرياضة الكاملة.");
        var balances=ReceivableEndpoints.Rows(db,academy,r.EndUtcExclusive==DateTimeOffset.MaxValue?clock.UtcNow:r.EndUtcExclusive.AddTicks(-1),r.To,branch,null,null);
        if(sport.HasValue)balances=balances.Where(b=>db.SportEnrollments.Any(e=>e.Id==b.EnrollmentId&&e.SportId==sport));
        var receivables=await balances.Where(b=>b.Currency==r.Currency&&b.Outstanding>0).SumAsync(b=>(decimal?)b.Outstanding)??0;
        var unpaid=await operating.Where(e=>e.PeriodFrom<=r.To&&e.PaidOn==null).SumAsync(e=>(decimal?)e.Amount)??0;
        var known=categoryRows.Select(c=>c.key).ToHashSet();foreach(var key in new[]{"Rent","Electricity","Water","Maintenance","CollectionFees"})if(!known.Contains(key))warnings.Add($"لم يسجل بند {ExpenseLabel(key)} مدفوعًا ومرحّلًا في النطاق؛ راجع اكتمال التكلفة.");
        var incomeTotal=income.Sum(x=>x.Amount);var expenseTotal=expenses.Sum(x=>x.Amount);
        return new(r.From,r.To,r.Currency,"Cash",income.ToArray(),expenses.ToArray(),incomeTotal,expenseTotal,incomeTotal-expenseTotal,refunds,discounts,receivables,payable,unpaid,advanceCash,advanceApplied,null,warnings.ToArray());
    }
    private static string ExpenseLabel(string key)=>key switch{"Rent"=>"إيجار المنشأة","Electricity"=>"الكهرباء","Water"=>"المياه","Heating"=>"التدفئة","Maintenance"=>"الصيانة والتشغيل","CollectionFees"=>"رسوم التحصيل الإلكتروني","Supplies"=>"المستلزمات","Administration"=>"المصروفات الإدارية","Facility"=>"تشغيل المرافق",_=>"مصروفات أخرى"};
}
