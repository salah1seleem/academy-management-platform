using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Demo;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2StaffScopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task V3_positive_correction_after_closed_salary_requires_review_and_posts_only_delta_without_rewriting_or_paying(bool alreadyAccrued)
    {
        var f=await PrivateFixture(isolatedBranch:true);using var owner=f.Owner;var path=(await PrivateBindUnits(owner,f.Coach,f.Document,f.Sessions[0])).Single();var month=new DateOnly(f.Today.Year,f.Today.Month,1);
        if(alreadyAccrued){await PrivatePayment(owner,f.Document,f.Enrollment,f.Plan,f.Actor,1000);(await PrivatePost(owner,path,await PrivatePreview(owner,path))).EnsureSuccessStatusCode();}
        var originalDue=alreadyAccrued?3050:3000;var paid=alreadyAccrued?1000:3000;
        (await StatementApprove(owner,f.Employee,month,await StatementView(owner,f.Employee,month))).EnsureSuccessStatusCode();(await StatementPay(owner,f.Employee,month,paid,0,originalDue)).EnsureSuccessStatusCode();
        var original=await StatementView(owner,f.Employee,month);
        await PrivatePayment(owner,f.Document,f.Enrollment,f.Plan,f.Actor,1000);
        var review=await PrivatePreview(owner,path);Assert.Equal("Review",review.GetProperty("action").GetString());Assert.Equal(50,review.GetProperty("difference").GetDecimal());Assert.False((await CorrectionQuote(owner,path)).GetProperty("canCorrect").GetBoolean());
        (await PrivatePost(owner,path,review)).EnsureSuccessStatusCode();Assert.Equal("ReviewPending",(await PrivatePreview(owner,path)).GetProperty("action").GetString());
        var quote=await CorrectionQuote(owner,path);Assert.True(quote.GetProperty("canCorrect").GetBoolean());Assert.Equal(month.AddMonths(1).ToString("yyyy-MM-dd"),quote.GetProperty("month").GetString());Assert.Equal(50,quote.GetProperty("difference").GetDecimal());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var collections=await db.Collections.CountAsync();var payments=await db.PayrollDisbursements.CountAsync();var expenses=await db.OperatingExpenses.CountAsync();
        Assert.Equal(alreadyAccrued?1:0,await db.PayrollAdjustments.CountAsync(a=>a.EmployeeId==f.Employee));Assert.Equal(0,(await db.PrivateCompensationEntries.SingleAsync(e=>e.EmployeeId==f.Employee&&e.Kind=="Review")).Delta);
        var key=Guid.NewGuid().ToString();var results=await Task.WhenAll(CorrectionPost(owner,path,quote,key),CorrectionPost(owner,path,quote,key));Assert.Single(results,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(results,r=>r.StatusCode==HttpStatusCode.OK);Assert.Equal(HttpStatusCode.Conflict,(await CorrectionPost(owner,path,quote)).StatusCode);
        var next=await PrivatePayroll(owner,f.Employee,month.AddMonths(1));Assert.Equal(3000,next.GetProperty("baseDue").GetDecimal());Assert.Equal(50,next.GetProperty("additions").GetDecimal());Assert.Equal(0,next.GetProperty("deductions").GetDecimal());Assert.Equal(3050,next.GetProperty("netDue").GetDecimal());
        var current=await StatementView(owner,f.Employee,month);Assert.Equal(original.GetProperty("statement").GetProperty("sourceHash").GetString(),current.GetProperty("statement").GetProperty("sourceHash").GetString());Assert.Equal(originalDue,current.GetProperty("row").GetProperty("netDue").GetDecimal());Assert.Equal(paid,current.GetProperty("row").GetProperty("paidAmount").GetDecimal());
        var entry=await db.PrivateCompensationEntries.AsNoTracking().SingleAsync(e=>e.EmployeeId==f.Employee&&e.Kind=="Correction");Assert.Equal(50,entry.Delta);Assert.NotNull(entry.ReviewEntryId);var adjustment=await db.PayrollAdjustments.SingleAsync(a=>a.Id==entry.PayrollAdjustmentId);Assert.Equal("Addition",adjustment.Kind);Assert.Equal(50,adjustment.Amount);Assert.Null(adjustment.AdditionRequestId);
        Assert.Equal(collections,await db.Collections.CountAsync());Assert.Equal(payments,await db.PayrollDisbursements.CountAsync());Assert.Equal(expenses,await db.OperatingExpenses.CountAsync());Assert.Equal("None",(await PrivatePreview(owner,path)).GetProperty("action").GetString());
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PayrollAdjustments\" SET \"Kind\"='Deduction' WHERE \"Id\"={adjustment.Id}"))).SqlState);
        using var coach=factory.CreateClient();await MobileCoachTestSession.SignInAsync(coach,factory.Services,f.Email);var own=(await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/payroll/private-services")).GetProperty("items")[0];Assert.Equal(alreadyAccrued?100:50,own.GetProperty("postedAmount").GetDecimal());Assert.Equal(50,own.GetProperty("corrections")[0].GetProperty("delta").GetDecimal());Assert.DoesNotContain("ملاحظة مراجعة مالية",own.GetRawText());
        var summary=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-summary?allDates=true&branchId={f.Branch}");Assert.Equal(originalDue,summary.GetProperty("payroll").GetProperty("netDue").GetDecimal());Assert.False((await StatementView(owner,f.Employee,month.AddMonths(1))).GetProperty("canApprove").GetBoolean());
    }
    [Fact]
    public async Task V3_positive_correction_preserves_source_checks_period_contract_and_owner_authority()
    {
        var f=await PrivateFixture();using var owner=f.Owner;var path=(await PrivateBindUnits(owner,f.Coach,f.Document,f.Sessions[0])).Single();var month=new DateOnly(f.Today.Year,f.Today.Month,1);
        (await StatementApprove(owner,f.Employee,month,await StatementView(owner,f.Employee,month))).EnsureSuccessStatusCode();await PrivatePayment(owner,f.Document,f.Enrollment,f.Plan,f.Actor,1000);(await PrivatePost(owner,path,await PrivatePreview(owner,path))).EnsureSuccessStatusCode();var quote=await CorrectionQuote(owner,path);
        foreach(var invalid in new[]{month,month.AddMonths(-1),month.AddMonths(13),month.AddDays(1)}){var q=await CorrectionQuote(owner,path,invalid);Assert.False(q.GetProperty("canCorrect").GetBoolean());Assert.Equal(HttpStatusCode.Conflict,(await CorrectionPost(owner,path,q)).StatusCode);}
        var a=await CreateAdmin();using var admin=a.Admin;using var grants=a.Owner;(await Write(grants,$"/api/v1/manage/staff/{a.Member}/access",Access(f.Branch,true,true) with{SensitivePermissions=[AcademyPermissions.PayrollPay,AcademyPermissions.PayrollStatementApprove,AcademyPermissions.CoachAgreementManage]})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await CorrectionPost(admin,path,quote)).StatusCode);using var foreign=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await CorrectionPost(foreign,path,quote)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(path+"/corrections",new PrivateCorrectionWrite(month.AddMonths(1),quote.GetProperty("quoteHash").GetString()!))).StatusCode);
        await PrivatePayment(owner,f.Document,f.Enrollment,f.Plan,f.Actor,1000);Assert.Equal(HttpStatusCode.Conflict,(await CorrectionPost(owner,path,quote)).StatusCode);var freshReview=await PrivatePreview(owner,path);Assert.Equal(100,freshReview.GetProperty("difference").GetDecimal());(await PrivatePost(owner,path,freshReview)).EnsureSuccessStatusCode();var fresh=await CorrectionQuote(owner,path);(await CorrectionPost(owner,path,fresh)).EnsureSuccessStatusCode();Assert.Equal(3100,(await PrivatePayroll(owner,f.Employee,month.AddMonths(1))).GetProperty("netDue").GetDecimal());
    }
    [Fact]
    public async Task V3_positive_correction_never_invents_salary_after_contract_end_or_review_before_month_is_closed()
    {
        var f=await PrivateFixture();using var owner=f.Owner;var path=(await PrivateBindUnits(owner,f.Coach,f.Document,f.Sessions[0])).Single();await PrivatePayment(owner,f.Document,f.Enrollment,f.Plan,f.Actor,1000);var preview=await PrivatePreview(owner,path);Assert.Equal("Accrual",preview.GetProperty("action").GetString());
        Assert.Equal(HttpStatusCode.Conflict,(await PrivatePost(owner,path,preview,action:"Review")).StatusCode);(await PrivatePost(owner,path,preview)).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var original=await db.PrivateCompensationEntries.SingleAsync(e=>e.EmployeeId==f.Employee);var id=Guid.NewGuid();
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PrivateCompensationEntries\" SELECT (jsonb_populate_record(NULL::\"PrivateCompensationEntries\",to_jsonb(e)||jsonb_build_object('Id',{id},'RequestKey','forged-positive-review','Revision',2,'Kind','Review','PostedBefore',50,'TargetAmount',100,'Delta',0,'PayrollAdjustmentId',NULL,'SourceSnapshot',e.\"SourceSnapshot\"||jsonb_build_object('Earned',100)))).* FROM \"PrivateCompensationEntries\" e WHERE e.\"Id\"={original.Id}"))).SqlState);
        var ended=await PrivateFixture();using var endedOwner=ended.Owner;var month=new DateOnly(ended.Today.Year,ended.Today.Month,1);var employee=await db.Employees.SingleAsync(e=>e.Id==ended.Employee);
        (await StatementCommand(endedOwner,$"/api/v1/manage/employees/{ended.Employee}/contract-versions",new ContractChange(month,employee.ContractStart,ended.Today,1),null)).EnsureSuccessStatusCode();
        var endedPath=(await PrivateBindUnits(endedOwner,ended.Coach,ended.Document,ended.Sessions[0])).Single();(await StatementApprove(endedOwner,ended.Employee,month,await StatementView(endedOwner,ended.Employee,month))).EnsureSuccessStatusCode();await PrivatePayment(endedOwner,ended.Document,ended.Enrollment,ended.Plan,ended.Actor,1000);(await PrivatePost(endedOwner,endedPath,await PrivatePreview(endedOwner,endedPath))).EnsureSuccessStatusCode();var q=await CorrectionQuote(endedOwner,endedPath);Assert.False(q.GetProperty("canCorrect").GetBoolean());Assert.Contains("نهاية الخدمة",q.GetProperty("message").GetString());Assert.Equal(HttpStatusCode.Conflict,(await CorrectionPost(endedOwner,endedPath,q)).StatusCode);Assert.False(await db.PayrollAdjustments.AnyAsync(a=>a.EmployeeId==ended.Employee));
    }
}
