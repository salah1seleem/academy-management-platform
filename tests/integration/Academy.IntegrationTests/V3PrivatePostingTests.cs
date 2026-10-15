using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Academy.Api.Auth;
using Academy.Api.Demo;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2StaffScopeTests
{
    private static async Task<HttpResponseMessage> PrivatePost(HttpClient client, string path, JsonElement quote, string? key = null, string? action = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post,path + "/entries") { Content = JsonContent.Create(new PrivatePostingWrite(action ?? quote.GetProperty("action").GetString()!,quote.GetProperty("quoteHash").GetString()!)) };
        request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString()); request.Headers.Add("Idempotency-Key",key ?? Guid.NewGuid().ToString()); return await client.SendAsync(request);
    }
    private static async Task<string[]> PrivateBindUnits(HttpClient owner, Guid coach, Guid document, params Guid[] sessions)
    {
        var root = PrivateRoot(coach); var r = await Write(owner,root + "/packages",new PrivatePackageWrite(document,8),false); r.EnsureSuccessStatusCode();
        var package = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        for (var i=0;i<sessions.Length;i++) (await Write(owner,$"{root}/{package}/deliveries",new PrivateDeliveryWrite(sessions[i],i+1),false)).EnsureSuccessStatusCode();
        return (await PrivateRows(owner,coach)).Where(r=>r.GetProperty("documentId").GetGuid()==document).OrderBy(r=>r.GetProperty("ordinal").GetInt32()).Select(r=>$"{root}/{r.GetProperty("id").GetGuid()}").ToArray();
    }
    private static Task<JsonElement> PrivatePreview(HttpClient owner,string path) => owner.GetFromJsonAsync<JsonElement>(path+"/posting-preview");
    private static async Task<JsonElement> PrivatePayroll(HttpClient owner,Guid employee,DateOnly month) =>
        (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/payroll/?month={month:yyyy-MM-dd}")).GetProperty("items").EnumerateArray().Single(r=>r.GetProperty("employeeId").GetGuid()==employee);

    [Fact]
    public async Task V3_private_posting_partial_then_complete_posts_only_delta_once_and_keeps_cash_and_base_unchanged()
    {
        var f=await PrivateFixture(isolatedBranch:true); using var owner=f.Owner; var paths=await PrivateBindUnits(owner,f.Coach,f.Document,f.Sessions[0],f.Sessions[1]);
        await PrivatePayment(owner,f.Document,f.Enrollment,f.Plan,f.Actor,1000);
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var collections=await db.Collections.CountAsync(); var receipts=await db.Receipts.CountAsync();
        foreach(var path in paths)
        {
            var q=await PrivatePreview(owner,path); Assert.Equal("Accrual",q.GetProperty("action").GetString()); Assert.Equal(50,q.GetProperty("difference").GetDecimal());
            var key=Guid.NewGuid().ToString(); var requests=await Task.WhenAll(PrivatePost(owner,path,q,key),PrivatePost(owner,path,q,key));
            Assert.Equal(1,requests.Count(r=>r.StatusCode==HttpStatusCode.Created)); Assert.Equal(1,requests.Count(r=>r.StatusCode==HttpStatusCode.OK));
            Assert.Equal(HttpStatusCode.Conflict,(await PrivatePost(owner,path,q)).StatusCode);
            Assert.Equal("None",(await PrivatePreview(owner,path)).GetProperty("action").GetString());
        }
        var half=await PrivatePayroll(owner,f.Employee,f.Today); Assert.Equal(100,half.GetProperty("additions").GetDecimal()); Assert.Equal(3100,half.GetProperty("netDue").GetDecimal()); Assert.Equal(100,half.GetProperty("privateCompensation").GetDecimal());
        Assert.Equal(collections,await db.Collections.CountAsync()); Assert.Equal(receipts,await db.Receipts.CountAsync());
        await PrivatePayment(owner,f.Document,f.Enrollment,f.Plan,f.Actor,1000);
        foreach(var path in paths)
        {
            var q=await PrivatePreview(owner,path); Assert.Equal(50,q.GetProperty("currentPosted").GetDecimal()); Assert.Equal(100,q.GetProperty("targetAmount").GetDecimal()); Assert.Equal(50,q.GetProperty("difference").GetDecimal());
            var posted=await PrivatePost(owner,path,q); Assert.True(posted.IsSuccessStatusCode,await posted.Content.ReadAsStringAsync());
            var history=await owner.GetFromJsonAsync<JsonElement[]>(path+"/entries"); Assert.Equal(2,history!.Length); Assert.All(history,r=>Assert.Equal(50,r.GetProperty("delta").GetDecimal()));
        }
        var full=await PrivatePayroll(owner,f.Employee,f.Today); Assert.Equal(200,full.GetProperty("additions").GetDecimal()); Assert.Equal(3200,full.GetProperty("netDue").GetDecimal());
        var report=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/payroll/?month={f.Today:yyyy-MM-dd}&branchId={f.Branch}");
        var summary=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-summary?allDates=true&branchId={f.Branch}");
        Assert.Equal(report.GetProperty("totalNet").GetDecimal(),summary.GetProperty("payroll").GetProperty("netDue").GetDecimal());
        Assert.Equal(report.GetProperty("totalAdditions").GetDecimal(),summary.GetProperty("payroll").GetProperty("additions").GetDecimal());
        var sportName=await db.Sports.Where(s=>s.Id==f.Sport).Select(s=>s.ArabicName).SingleAsync();
        var financial=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial?allDates=true&branchId={f.Branch}&sportId={f.Sport}");
        var financialRows=financial.GetProperty("items").EnumerateArray().ToArray(); Assert.Equal(2,financialRows.Length); Assert.All(financialRows,r=>Assert.Equal(sportName,r.GetProperty("sport").GetString())); Assert.Equal(2000,financialRows.Sum(r=>r.GetProperty("amount").GetDecimal()));
        Assert.Equal(3000,await db.Employees.Where(e=>e.Id==f.Employee).Select(e=>e.MonthlySalary).SingleAsync()); Assert.False(await db.PayrollSettlements.AnyAsync(s=>s.EmployeeId==f.Employee));
        var entries=await db.PrivateCompensationEntries.Where(e=>e.EmployeeId==f.Employee).ToArrayAsync(); Assert.Equal(4,entries.Length);
        Assert.All(entries,e=> { var snapshot=JsonDocument.Parse(e.SourceSnapshot).RootElement; Assert.Equal(f.Document,snapshot.GetProperty("DocumentId").GetGuid()); Assert.Equal(40,snapshot.GetProperty("VariableValue").GetDecimal()); Assert.NotEqual(default,e.CutoffAtUtc); Assert.NotNull(e.PayrollAdjustmentId); });
        using var coach=factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach,factory.Services,f.Email);
        var own=await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/payroll/private-services"); Assert.True(own.GetProperty("postedToPayroll").GetBoolean()); Assert.All(own.GetProperty("items").EnumerateArray(),r=> { Assert.Equal(100,r.GetProperty("postedAmount").GetDecimal()); Assert.Equal(0,r.GetProperty("pendingDelta").GetDecimal()); Assert.False(r.TryGetProperty("documentId",out _)); Assert.False(r.TryGetProperty("player",out _)); });
    }

    [Fact]
    public async Task V3_private_credit_needs_review_then_explicit_unpaid_reversal_and_preserves_original_sources()
    {
        var f=await PrivateFixture(); using var owner=f.Owner; var path=(await PrivateBindUnits(owner,f.Coach,f.Document,f.Sessions[0])).Single();
        var stale=await PrivatePreview(owner,path); await PrivatePayment(owner,f.Document,f.Enrollment,f.Plan,f.Actor,2000);
        Assert.Equal(HttpStatusCode.Conflict,(await PrivatePost(owner,path,stale,action:"Accrual")).StatusCode);
        var q=await PrivatePreview(owner,path); (await PrivatePost(owner,path,q)).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var original=await db.PrivateCompensationEntries.SingleAsync(e=>e.EmployeeId==f.Employee); var snapshot=original.SourceSnapshot;
        (await PrivateFinancialWrite(owner,$"/api/v1/finance/receivables/{f.Document}/movements",new { kind="CreditNote",amount=400m,reason="تخفيض اصطناعي موثق" })).EnsureSuccessStatusCode();
        var review=await PrivatePreview(owner,path); Assert.Equal("Review",review.GetProperty("action").GetString()); Assert.Equal(-20,review.GetProperty("difference").GetDecimal());
        Assert.Equal(3100,(await PrivatePayroll(owner,f.Employee,f.Today)).GetProperty("netDue").GetDecimal());
        (await PrivatePost(owner,path,review)).EnsureSuccessStatusCode(); Assert.Equal(1,await db.PayrollAdjustments.CountAsync(a=>a.EmployeeId==f.Employee));
        var reverse=await PrivatePreview(owner,path); Assert.Equal("Reversal",reverse.GetProperty("action").GetString()); var key=Guid.NewGuid().ToString();
        (await PrivatePost(owner,path,reverse,key)).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.OK,(await PrivatePost(owner,path,reverse,key)).StatusCode);
        var row=await PrivatePayroll(owner,f.Employee,f.Today); Assert.Equal(100,row.GetProperty("additions").GetDecimal()); Assert.Equal(20,row.GetProperty("deductions").GetDecimal()); Assert.Equal(80,row.GetProperty("privateCompensation").GetDecimal()); Assert.Equal(3080,row.GetProperty("netDue").GetDecimal());
        Assert.Equal(snapshot,(await db.PrivateCompensationEntries.AsNoTracking().SingleAsync(e=>e.Id==original.Id)).SourceSnapshot);
        original.Delta=1; await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync()); db.ChangeTracker.Clear();
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PrivateCompensationEntries\" WHERE \"Id\"={original.Id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PayrollAdjustments\" SET \"Amount\"=1 WHERE \"Id\"={original.PayrollAdjustmentId}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PrivateCompensationEntries\" SELECT (jsonb_populate_record(NULL::\"PrivateCompensationEntries\",to_jsonb(e)||jsonb_build_object('Id',{Guid.NewGuid()},'RequestKey',{Guid.NewGuid().ToString()},'Revision',99))).* FROM \"PrivateCompensationEntries\" e WHERE \"Id\"={original.Id}"))).SqlState);
    }

    [Fact]
    public async Task V3_private_paid_month_credit_records_zero_effect_review_never_rewrites_paid_salary_or_future_period()
    {
        var f=await PrivateFixture(); using var owner=f.Owner; var path=(await PrivateBindUnits(owner,f.Coach,f.Document,f.Sessions[0])).Single(); await PrivatePayment(owner,f.Document,f.Enrollment,f.Plan,f.Actor,2000);
        (await PrivatePost(owner,path,await PrivatePreview(owner,path))).EnsureSuccessStatusCode();
        (await PrivateFinancialWrite(owner,$"/api/v1/manage/payroll/{f.Employee}/pay",new { month=f.Today,expectedNetDue=3100m })).EnsureSuccessStatusCode();
        (await PrivateFinancialWrite(owner,$"/api/v1/finance/receivables/{f.Document}/movements",new { kind="CreditNote",amount=400m,reason="تصحيح اصطناعي بعد سداد الراتب" })).EnsureSuccessStatusCode();
        var review=await PrivatePreview(owner,path); Assert.Equal("Review",review.GetProperty("action").GetString()); (await PrivatePost(owner,path,review)).EnsureSuccessStatusCode();
        var pending=await PrivatePreview(owner,path); Assert.Equal("ReviewPending",pending.GetProperty("action").GetString()); Assert.Equal(HttpStatusCode.Conflict,(await PrivatePost(owner,path,review)).StatusCode);
        var row=await PrivatePayroll(owner,f.Employee,f.Today); Assert.True(row.GetProperty("isPaid").GetBoolean()); Assert.Equal(3100,row.GetProperty("netDue").GetDecimal()); Assert.Equal(0,row.GetProperty("deductions").GetDecimal());
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(3100,(await db.PayrollSettlements.SingleAsync(s=>s.EmployeeId==f.Employee)).NetDue); Assert.Single(await db.PayrollAdjustments.Where(a=>a.EmployeeId==f.Employee).ToArrayAsync());
        var recorded=await db.PrivateCompensationEntries.Where(e=>e.EmployeeId==f.Employee).OrderBy(e=>e.Revision).ToArrayAsync(); Assert.Equal(2,recorded.Length); Assert.Equal(0,recorded[1].Delta); Assert.Null(recorded[1].PayrollAdjustmentId); Assert.All(recorded,e=>Assert.Equal(f.Today,e.DueDate));
    }

    [Fact]
    public async Task V3_private_posting_requires_owner_csrf_current_quote_and_rejects_foreign_or_delegated_contract_permissions()
    {
        var f=await PrivateFixture(); using var owner=f.Owner; var path=(await PrivateBindUnits(owner,f.Coach,f.Document,f.Sessions[0])).Single(); await PrivatePayment(owner,f.Document,f.Enrollment,f.Plan,f.Actor,1000); var q=await PrivatePreview(owner,path);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(path+"/entries",new PrivatePostingWrite("Accrual",q.GetProperty("quoteHash").GetString()!))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await PrivatePost(owner,path,q,"short")).StatusCode);
        var a=await CreateAdmin(); using var admin=a.Admin; using var grantOwner=a.Owner;
        (await Write(grantOwner,$"/api/v1/manage/staff/{a.Member}/access",Access(f.Branch,true,true) with { SensitivePermissions=[AcademyPermissions.CoachAgreementManage,AcademyPermissions.PayrollPay] })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync(path+"/posting-preview")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden,(await PrivatePost(admin,path,q)).StatusCode);
        using var other=await Staff(DemoSeed.FutureOwnerEmail); Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync(path+"/posting-preview")).StatusCode); Assert.Equal(HttpStatusCode.NotFound,(await PrivatePost(other,path,q)).StatusCode);
        using var coach=factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach,factory.Services,f.Email); Assert.Equal(HttpStatusCode.Forbidden,(await coach.GetAsync(path+"/posting-preview")).StatusCode);
        var key=Guid.NewGuid().ToString(); (await PrivatePost(owner,path,q,key)).EnsureSuccessStatusCode(); var next=await PrivatePreview(owner,path); Assert.Equal(HttpStatusCode.Conflict,(await PrivatePost(owner,path,next,key,"Accrual")).StatusCode);
    }

    [Fact]
    public async Task V3_private_conflicting_execution_records_null_assessment_for_review_without_zero_accrual_and_rejects_fake_null_review()
    {
        var f=await PrivateFixture(); using var owner=f.Owner; var path=(await PrivateBindUnits(owner,f.Coach,f.Document,f.Sessions[0])).Single();
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var group=await db.TrainingSessions.Where(s=>s.Id==f.Sessions[0]).Select(s=>s.TrainingGroupId).SingleAsync();
        db.PlayerAttendances.Add(new() { Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,TrainingSessionId=f.Sessions[0],TrainingGroupId=group,SportEnrollmentId=f.Enrollment,Status=AttendanceStatus.Absent,RecordedByUserId=f.Actor }); await db.SaveChangesAsync();
        var q=await PrivatePreview(owner,path); Assert.Equal("Review",q.GetProperty("action").GetString()); Assert.Equal(JsonValueKind.Null,q.GetProperty("targetAmount").ValueKind); Assert.Equal(JsonValueKind.Null,q.GetProperty("difference").ValueKind);
        (await PrivatePost(owner,path,q)).EnsureSuccessStatusCode(); var saved=await db.PrivateCompensationEntries.SingleAsync(e=>e.EmployeeId==f.Employee); Assert.Null(saved.TargetAmount); Assert.Equal(0,saved.Delta); Assert.Null(saved.PayrollAdjustmentId); Assert.False(await db.PayrollAdjustments.AnyAsync(a=>a.EmployeeId==f.Employee));
        var falseFacts=JsonNode.Parse(saved.SourceSnapshot)!; falseFacts["RequiresReview"]=false; var invalidSnapshot=falseFacts.ToJsonString();
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PrivateCompensationEntries\" SELECT (jsonb_populate_record(NULL::\"PrivateCompensationEntries\",to_jsonb(e)||jsonb_build_object('Id',{Guid.NewGuid()},'RequestKey',{Guid.NewGuid().ToString()},'Revision',2,'SourceSnapshot',CAST({invalidSnapshot} AS jsonb)))).* FROM \"PrivateCompensationEntries\" e WHERE \"Id\"={saved.Id}"))).SqlState);
        Assert.Equal("ReviewPending",(await PrivatePreview(owner,path)).GetProperty("action").GetString());
    }
}
