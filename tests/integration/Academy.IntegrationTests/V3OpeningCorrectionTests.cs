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
    private static PayrollOpeningCorrection OpeningCorrectionBody(JsonElement view,decimal target)=>new(target,"سند تصحيح داخلي اصطناعي",view.GetProperty("correctionQuoteHash").GetString()!,"ملاحظة خاصة بالمالك");
    private static Task<HttpResponseMessage> OpeningCorrect(HttpClient c,Guid id,PayrollOpeningCorrection body,string? key=null)=>StatementCommand(c,OpeningRoot(id)+"/corrections",body,key);

    [Fact]
    public async Task V3_opening_correction_signed_chain_matches_cash_advances_csv_cutoff_and_own_without_rewriting_sources()
    {
        var f=await AgreementFixture();using var owner=f.Owner;var month=new DateOnly(f.Today.Year,f.Today.Month,1);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var expenseCount=await db.OperatingExpenses.CountAsync();var collectionCount=await db.Collections.CountAsync();
        (await OpeningPost(owner,f.Employee,OpeningBody(await OpeningView(owner,f.Employee),1200))).EnsureSuccessStatusCode();
        (await StatementApprove(owner,f.Employee,month,await StatementView(owner,f.Employee,month))).EnsureSuccessStatusCode();
        (await OpeningPay(owner,f.Employee,300,0,1200)).EnsureSuccessStatusCode();
        var source=await db.PayrollStatements.AsNoTracking().SingleAsync(s=>s.EmployeeId==f.Employee&&s.Kind=="Opening");var oldHash=source.SourceHash;var oldSnapshot=source.SourceSnapshot;
        var before=await owner.GetFromJsonAsync<JsonElement>(AccountRoot(f.Employee));
        var first=OpeningCorrectionBody(await OpeningView(owner,f.Employee),1000);(await OpeningCorrect(owner,f.Employee,first)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await OpeningPay(owner,f.Employee,1,300,900)).StatusCode);
        var corrected=await OpeningView(owner,f.Employee);Assert.Equal(700,corrected.GetProperty("opening").GetProperty("remaining").GetDecimal());Assert.Equal(1200,corrected.GetProperty("opening").GetProperty("originalAmount").GetDecimal());
        var a=await AdvanceRecord(owner,f.Employee,500);a.EnsureSuccessStatusCode();var advance=(await a.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await StatementCommand(owner,$"/api/v1/manage/payroll/{f.Employee}/advances/{advance}/applications",new PayrollAdvanceApply(month.AddMonths(-1),200,500,700,true),null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await OpeningCorrect(owner,f.Employee,OpeningCorrectionBody(await OpeningView(owner,f.Employee),499))).StatusCode);
        (await OpeningCorrect(owner,f.Employee,OpeningCorrectionBody(await OpeningView(owner,f.Employee),1300))).EnsureSuccessStatusCode();
        var account=await owner.GetFromJsonAsync<JsonElement>(AccountRoot(f.Employee));var o=account.GetProperty("opening");
        Assert.Equal(1200,o.GetProperty("originalAmount").GetDecimal());Assert.Equal(1300,o.GetProperty("amount").GetDecimal());Assert.Equal(500,o.GetProperty("paid").GetDecimal());Assert.Equal(800,o.GetProperty("remaining").GetDecimal());
        Assert.Equal(new decimal[]{-200,300},o.GetProperty("corrections").EnumerateArray().Select(c=>c.GetProperty("delta").GetDecimal()));
        Assert.Equal(4300,account.GetProperty("totalDue").GetDecimal());Assert.Equal(500,account.GetProperty("totalPaid").GetDecimal());Assert.Equal(3800,account.GetProperty("remaining").GetDecimal());Assert.Equal(800,account.GetProperty("cashOutRecorded").GetDecimal());Assert.Equal(300,account.GetProperty("unallocatedAdvances").GetDecimal());Assert.Equal(3000,account.GetProperty("items")[0].GetProperty("netDue").GetDecimal());
        var advances=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/payroll/{f.Employee}/advances");var opening=advances.GetProperty("statements").EnumerateArray().Single(s=>s.GetProperty("kind").GetString()=="Opening");Assert.Equal(1300,opening.GetProperty("netDue").GetDecimal());Assert.Equal(800,opening.GetProperty("remaining").GetDecimal());
        var suffix=$"?asOf={f.Today:yyyy-MM-dd}&cutoffUtc={Uri.EscapeDataString(before.GetProperty("cutoffUtc").GetString()!)}";
        var frozen=await owner.GetFromJsonAsync<JsonElement>(AccountRoot(f.Employee)+suffix);Assert.Equal(1200,frozen.GetProperty("opening").GetProperty("amount").GetDecimal());Assert.Empty(frozen.GetProperty("opening").GetProperty("corrections").EnumerateArray());Assert.Equal(300,frozen.GetProperty("totalPaid").GetDecimal());
        var csv=await owner.GetStringAsync(AccountRoot(f.Employee)+"/export");Assert.Contains("\"4300.00\",\"500.00\",\"3800.00\"",csv);Assert.Contains("'-200.00",csv);Assert.DoesNotContain("سند تصحيح داخلي",csv);Assert.DoesNotContain("ملاحظة خاصة",csv);
        using var coach=factory.CreateClient();await MobileCoachTestSession.SignInAsync(coach,factory.Services,f.Email);var own=await coach.GetFromJsonAsync<JsonElement>("/api/v1/staff/payroll/account");Assert.Equal(3800,own.GetProperty("remaining").GetDecimal());Assert.Equal(2,own.GetProperty("opening").GetProperty("corrections").GetArrayLength());Assert.DoesNotContain("سند تصحيح داخلي",own.GetRawText());Assert.DoesNotContain("ملاحظة خاصة",own.GetRawText());Assert.False(own.GetProperty("opening").GetProperty("corrections")[0].TryGetProperty("actorUserId",out _));
        var unchanged=await db.PayrollStatements.AsNoTracking().SingleAsync(s=>s.Id==source.Id);Assert.Equal(1200,unchanged.NetDue);Assert.Equal(oldHash,unchanged.SourceHash);Assert.Equal(oldSnapshot,unchanged.SourceSnapshot);Assert.Equal(2,await db.PayrollDisbursements.CountAsync(p=>p.EmployeeId==f.Employee));Assert.False(await db.PayrollAdjustments.AnyAsync(p=>p.EmployeeId==f.Employee));Assert.Equal(expenseCount,await db.OperatingExpenses.CountAsync());Assert.Equal(collectionCount,await db.Collections.CountAsync());
        // Both settlement routes use the corrected balance, never the original 1200.
        (await OpeningPay(owner,f.Employee,800,500,800)).EnsureSuccessStatusCode();Assert.False((await OpeningView(owner,f.Employee)).GetProperty("canPay").GetBoolean());Assert.Equal(HttpStatusCode.Conflict,(await OpeningPay(owner,f.Employee,1,1300,0)).StatusCode);
    }

    [Fact]
    public async Task V3_opening_correction_replay_races_stale_quotes_and_zero_opening_are_explicit()
    {
        var f=await AgreementFixture();using var owner=f.Owner;(await OpeningPost(owner,f.Employee,OpeningBody(await OpeningView(owner,f.Employee),0))).EnsureSuccessStatusCode();
        var b=OpeningCorrectionBody(await OpeningView(owner,f.Employee),200);var key=Guid.NewGuid().ToString();var both=await Task.WhenAll(OpeningCorrect(owner,f.Employee,b,key),OpeningCorrect(owner,f.Employee,b,key));Assert.Single(both,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(both,r=>r.StatusCode==HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict,(await OpeningCorrect(owner,f.Employee,b with{TargetAmount=300},key)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await OpeningCorrect(owner,f.Employee,b)).StatusCode);
        var v=await OpeningView(owner,f.Employee);var same=OpeningCorrectionBody(v,200);foreach(var bad in new[]{same,same with{TargetAmount=-1},same with{TargetAmount=1.001m},same with{Reference=""},same with{TargetAmount=100000001}})Assert.Equal(HttpStatusCode.UnprocessableEntity,(await OpeningCorrect(owner,f.Employee,bad)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await StatementCommand(owner,OpeningRoot(f.Employee)+"/corrections",new{same.Reference,same.QuoteHash},null)).StatusCode);
        var race=await Task.WhenAll(OpeningCorrect(owner,f.Employee,OpeningCorrectionBody(v,100)),OpeningPay(owner,f.Employee,150,0,200));Assert.Single(race,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(race,r=>r.StatusCode==HttpStatusCode.Conflict);
        var final=await OpeningView(owner,f.Employee);Assert.True(final.GetProperty("opening").GetProperty("remaining").GetDecimal()>=0);Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(OpeningRoot(f.Employee)+"/corrections",b)).StatusCode);
    }

    [Fact]
    public async Task V3_opening_correction_owner_only_live_scope_csrf_and_sql_immutable_guards()
    {
        var f=await AgreementFixture();using var owner=f.Owner;(await OpeningPost(owner,f.Employee,OpeningBody(await OpeningView(owner,f.Employee),1200))).EnsureSuccessStatusCode();
        var a=await CreateAdmin();using var admin=a.Admin;using var grants=a.Owner;var access=Access(f.Branch,true,true) with{SensitivePermissions=[AcademyPermissions.PayrollStatementApprove,AcademyPermissions.PayrollPay]};(await Write(grants,$"/api/v1/manage/staff/{a.Member}/access",access)).EnsureSuccessStatusCode();
        var b=OpeningCorrectionBody(await OpeningView(owner,f.Employee),1000);Assert.False((await OpeningView(admin,f.Employee)).GetProperty("canCorrect").GetBoolean());Assert.Equal(HttpStatusCode.Forbidden,(await OpeningCorrect(admin,f.Employee,b)).StatusCode);
        using var coach=factory.CreateClient();await MobileCoachTestSession.SignInAsync(coach,factory.Services,f.Email);Assert.Equal(HttpStatusCode.Forbidden,(await OpeningCorrect(coach,f.Employee,b)).StatusCode);
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await OpeningCorrect(foreign,f.Employee,b)).StatusCode);
        (await OpeningCorrect(owner,f.Employee,b)).EnsureSuccessStatusCode();(await OpeningPay(owner,f.Employee,100,0,1000)).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var c=await db.PayrollPayableCorrections.SingleAsync(c=>c.EmployeeId==f.Employee);c.Note="changed";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PayrollPayableCorrections\" WHERE \"Id\"={c.Id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"PayrollPayableCorrections\" SET \"Note\"='forged' WHERE \"Id\"={c.Id}"))).SqlState);
        var forged=Guid.NewGuid();Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PayrollPayableCorrections\" SELECT (jsonb_populate_record(NULL::\"PayrollPayableCorrections\",to_jsonb(c)||jsonb_build_object('Id',{forged},'Revision',2,'PreviousAmount',1000,'TargetAmount',0,'Delta',-1000,'PaidAtApproval',0,'RequestKey','forged-correction'))).* FROM \"PayrollPayableCorrections\" c WHERE c.\"Id\"={c.Id}"))).SqlState);
        var adminUser=await db.AcademyMemberships.Where(m=>m.Id==a.Member).Select(m=>m.UserId).SingleAsync();
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PayrollPayableCorrections\" SELECT (jsonb_populate_record(NULL::\"PayrollPayableCorrections\",to_jsonb(c)||jsonb_build_object('Id',{forged},'Revision',2,'PreviousAmount',1000,'TargetAmount',1100,'Delta',100,'PaidAtApproval',100,'CreatedAtUtc',clock_timestamp(),'ActorMembershipId',{a.Member},'ActorUserId',{adminUser},'RequestKey','forged-admin-correction'))).* FROM \"PayrollPayableCorrections\" c WHERE c.\"Id\"={c.Id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PayrollDisbursements\" SELECT (jsonb_populate_record(NULL::\"PayrollDisbursements\",to_jsonb(p)||jsonb_build_object('Id',{forged},'Sequence',2,'PaidBefore',100,'Amount',1,'RemainingAfter',899,'CreatedAtUtc',clock_timestamp()-interval '1 day','RequestKey','forged-backdated-pay'))).* FROM \"PayrollDisbursements\" p WHERE p.\"EmployeeId\"={f.Employee}"))).SqlState);
        // A forged payment valid against the old 1200 but not corrected 1000 is rejected.
        Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PayrollDisbursements\" SELECT (jsonb_populate_record(NULL::\"PayrollDisbursements\",to_jsonb(p)||jsonb_build_object('Id',{forged},'Sequence',2,'PaidBefore',100,'Amount',1000,'RemainingAfter',100,'RequestKey','forged-old-opening-pay'))).* FROM \"PayrollDisbursements\" p WHERE p.\"EmployeeId\"={f.Employee}"))).SqlState);
    }
}
