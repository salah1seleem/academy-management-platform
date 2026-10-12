using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;

public sealed partial class V2ReceivablesTests
{
    private static string RefundRoot(Guid player) => $"/api/v1/people/players/{player}/refunds";
    private async Task<(Guid Player,Guid Enrollment,Guid Collection,Guid Session,Guid Register,Guid Member,decimal Amount)> CashRefundFixture(HttpClient owner)
    {
        var f=await CashCollectionFixture(owner); var quote=await CashQuote(owner,f.Session,f.Enrollment);
        (await CashCollect(owner,f.Session,f.Enrollment,quote.GetProperty("quoteHash").GetString()!)).EnsureSuccessStatusCode();
        await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var c=await db.Collections.SingleAsync(c=>c.SportEnrollmentId==f.Enrollment);
        return(f.Player,f.Enrollment,c.Id,f.Session,f.Register,f.Member,c.Amount);
    }
    private async Task<RefundWrite> CashRefundQuote(HttpClient owner,Guid player,Guid collection,Guid session,decimal amount)
    {
        var body=new RefundWrite(collection,amount,null,session);var r=await Post(owner,RefundRoot(player)+"/quote",body);r.EnsureSuccessStatusCode();
        return body with {QuoteHash=(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteHash").GetString()};
    }
    [Fact]
    public async Task Cash_refund_is_atomic_exact_once_and_replayable_after_close_without_changing_service_or_debt()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRefundFixture(owner);var b=await CashRefundQuote(owner,f.Player,f.Collection,f.Session,40.25m);var key=Guid.NewGuid().ToString();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var period=await db.SubscriptionPeriods.AsNoTracking().SingleAsync(p=>p.SportEnrollmentId==f.Enrollment);var before=JsonSerializer.Serialize(period);
        var results=await Task.WhenAll(Post(owner,RefundRoot(f.Player),b,key),Post(owner,RefundRoot(f.Player),b,key));Assert.All(results,r=>r.EnsureSuccessStatusCode());
        var id=(await results[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();Assert.Equal(id,(await results[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        var posting=Assert.Single(await db.CashRefundPostings.Where(p=>p.SessionId==f.Session).ToArrayAsync());Assert.Equal(id,posting.RefundId);Assert.Equal(40.25m,posting.Amount);
        Assert.Single(await db.CollectionRefunds.Where(p=>p.CollectionId==f.Collection).ToArrayAsync());
        Assert.Equal(before,JsonSerializer.Serialize(await db.SubscriptionPeriods.AsNoTracking().SingleAsync(p=>p.SportEnrollmentId==f.Enrollment)));
        Assert.False(await db.ReceivableMovements.AnyAsync(m=>m.CollectionId==f.Collection)); Assert.Single(await db.Receipts.Where(r=>r.CollectionId==f.Collection).ToArrayAsync());
        var detail=await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{f.Session}");var expected=CashRegisterService.Money(100+f.Amount-40.25m);
        Assert.Equal(expected,detail.GetProperty("balance").GetString());Assert.Equal(id,detail.GetProperty("refunds")[0].GetProperty("refundId").GetGuid());
        (await Post(owner,$"{CashRoot}/sessions/{f.Session}/close",new{countedAmount=expected})).EnsureSuccessStatusCode();
        (await Post(owner,RefundRoot(f.Player),b,key)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,RefundRoot(f.Player),b with {Reason="محتوى آخر"},key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,RefundRoot(f.Player),b with {CashSessionId=Guid.NewGuid()},key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,RefundRoot(f.Player),b)).StatusCode);
    }
    [Fact]
    public async Task Cash_refund_cannot_bypass_review_with_legacy_body_or_stale_pool()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRefundFixture(owner);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(owner,RefundRoot(f.Player),new RefundWrite(f.Collection,10,null))).StatusCode);
        var b=await CashRefundQuote(owner,f.Player,f.Collection,f.Session,10);
        var doc=await Charge(owner,f.Enrollment,100);(await Move(owner,doc,"PaymentAllocation",20,f.Collection)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,RefundRoot(f.Player),b)).StatusCode);
        b=await CashRefundQuote(owner,f.Player,f.Collection,f.Session,10);(await Post(owner,RefundRoot(f.Player),b)).EnsureSuccessStatusCode();
        var report=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/finance/receivables?playerId={f.Player}");Assert.Equal("80.00",report.GetProperty("outstanding").GetString());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Single(await db.CollectionRefunds.Where(r=>r.CollectionId==f.Collection).ToArrayAsync());
    }
    [Fact]
    public async Task Cash_refund_parallel_distinct_keys_cannot_spend_the_same_review_twice()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRefundFixture(owner);var b=await CashRefundQuote(owner,f.Player,f.Collection,f.Session,40);
        var results=await Task.WhenAll(Post(owner,RefundRoot(f.Player),b),Post(owner,RefundRoot(f.Player),b));Assert.Single(results,r=>r.IsSuccessStatusCode);Assert.Single(results,r=>r.StatusCode==HttpStatusCode.Conflict);
        await using var s=factory.Services.CreateAsyncScope();Assert.Single(await s.ServiceProvider.GetRequiredService<FoundationDbContext>().CashRefundPostings.Where(p=>p.SessionId==f.Session).ToArrayAsync());
    }
    [Fact]
    public async Task Cash_refund_close_race_preserves_frozen_balance_and_has_no_partial_refund()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRefundFixture(owner);var b=await CashRefundQuote(owner,f.Player,f.Collection,f.Session,40);
        var results=await Task.WhenAll(Post(owner,RefundRoot(f.Player),b),Post(owner,$"{CashRoot}/sessions/{f.Session}/close",new{countedAmount=CashRegisterService.Money(100+f.Amount)}));results[1].EnsureSuccessStatusCode();Assert.True(results[0].IsSuccessStatusCode||results[0].StatusCode==HttpStatusCode.Conflict);
        var detail=await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{f.Session}");Assert.Equal(detail.GetProperty("balance").GetString(),detail.GetProperty("session").GetProperty("expectedAmount").GetString());
        await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();var count=results[0].IsSuccessStatusCode?1:0;
        Assert.Equal(count,await db.CollectionRefunds.CountAsync(r=>r.CollectionId==f.Collection));Assert.Equal(count,await db.CashRefundPostings.CountAsync(r=>r.SessionId==f.Session));
    }
    [Fact]
    public async Task Cash_refund_respects_disabled_cash_revoked_cashier_currency_branch_and_balance()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRefundFixture(owner);var b=await CashRefundQuote(owner,f.Player,f.Collection,f.Session,40);
        (await CashAssign(owner,f.Register,f.Member,false)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await Post(owner,RefundRoot(f.Player),b)).StatusCode);(await CashAssign(owner,f.Register,f.Member)).EnsureSuccessStatusCode();
        var settings=await CashSettings();try{await CashSetConfiguration(owner,settings with {CashEnabled=false});Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,RefundRoot(f.Player),b)).StatusCode);}finally{await CashSetConfiguration(owner,settings);}
        (await Post(owner,$"{CashRoot}/sessions/{f.Session}/close",new{countedAmount=CashRegisterService.Money(100+f.Amount)})).EnsureSuccessStatusCode();var empty=await CashOpen(owner,f.Register,"0");
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,RefundRoot(f.Player)+"/quote",b with {CashSessionId=empty})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,RefundRoot(f.Player),b with {CashSessionId=empty})).StatusCode);
        await using var s=factory.Services.CreateAsyncScope();Assert.False(await s.ServiceProvider.GetRequiredService<FoundationDbContext>().CollectionRefunds.AnyAsync(r=>r.CollectionId==f.Collection));
    }
    [Fact]
    public async Task Cash_refund_csrf_finance_role_tenant_and_player_scope_are_server_enforced()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);using var legacy=await Staff(DemoSeed.AdminEmail);using var guardian=await Guardian();using var other=await Staff(DemoSeed.FutureOwnerEmail);
        var f=await CashRefundFixture(owner);var b=await CashRefundQuote(owner,f.Player,f.Collection,f.Session,10);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(RefundRoot(f.Player),b)).StatusCode);
        foreach(var client in new[]{legacy,guardian}) {Assert.Equal(HttpStatusCode.Forbidden,(await Post(client,RefundRoot(f.Player),b)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await Post(client,RefundRoot(f.Player)+"/quote",b)).StatusCode);}
        Assert.Equal(HttpStatusCode.NotFound,(await Post(other,RefundRoot(f.Player),b)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await Post(other,RefundRoot(f.Player)+"/quote",b)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Post(owner,RefundRoot(Guid.NewGuid()),b)).StatusCode);
        foreach(var amount in new[]{0m,-1,.001m,1000000000m}) Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(owner,RefundRoot(f.Player),b with {Amount=amount})).StatusCode);
    }
    [Fact]
    public async Task Cash_refund_posting_source_and_original_collection_are_immutable()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRefundFixture(owner);var b=await CashRefundQuote(owner,f.Player,f.Collection,f.Session,10);(await Post(owner,RefundRoot(f.Player),b)).EnsureSuccessStatusCode();
        await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();var p=await db.CashRefundPostings.SingleAsync(p=>p.SessionId==f.Session);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CashRefundPostings\" SET \"Amount\"=1 WHERE \"Id\"={p.Id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"CollectionRefunds\" WHERE \"Id\"={p.RefundId}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CollectionRefunds\" SET \"Reason\"='x' WHERE \"Id\"={p.RefundId}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Collections\" SET \"Amount\"=1 WHERE \"Id\"={f.Collection}"))).SqlState);
        p.Amount++;await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.Entry(p).State=EntityState.Unchanged;
        db.CashRefundPostings.Add(new() {Id=Guid.NewGuid(),AcademyId=p.AcademyId,SessionId=p.SessionId,BranchId=p.BranchId,RefundId=p.RefundId,Amount=100,Currency=p.Currency,ActorUserId=p.ActorUserId,RequestHash=new string('A',64)});
        var error=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.Equal("23514",Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }
    [Fact]
    public async Task Cash_refund_admin_needs_finance_edit_subscription_player_read_branch_and_own_shift()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRefundFixture(owner);var email=$"refund.{Guid.NewGuid():N}@example.test";
        var created=await Post(owner,"/api/v1/manage/staff",new{name="أمين استرداد اصطناعي",email,phone="010"+Random.Shared.Next(10000000,99999999),temporaryPassword="Demo-Only-123!",role="AcademyAdmin",jobTitle="مالية"});created.EnsureSuccessStatusCode();var member=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var branch=await db.CashSessions.Where(s=>s.Id==f.Session).Select(s=>s.BranchId).SingleAsync();
        async Task Access(bool finance)
        {
            var actions=StaffAccessRules.Modules.Select(module=>new{module,read=module is "players" or "subscriptions" || module=="finance"&&finance,add=module=="finance"&&finance,edit=module=="subscriptions",delete=false});
            var csrf=await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");using var r=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/manage/staff/{member}/access"){Content=JsonContent.Create(new{allBranches=false,branches=new[]{branch},actions})};r.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());(await owner.SendAsync(r)).EnsureSuccessStatusCode();
        }
        await Access(false);using var admin=await Staff(email);var body=new RefundWrite(f.Collection,10,null,f.Session);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post(admin,RefundRoot(f.Player)+"/quote",body)).StatusCode);
        await Access(true);(await CashAssign(owner,f.Register,member)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden,(await Post(admin,RefundRoot(f.Player)+"/quote",body)).StatusCode);
        (await Post(owner,$"{CashRoot}/sessions/{f.Session}/close",new{countedAmount=CashRegisterService.Money(100+f.Amount)})).EnsureSuccessStatusCode();
        var shift=await CashOpen(admin,f.Register,"100");var b=await CashRefundQuote(admin,f.Player,f.Collection,shift,10);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post(owner,RefundRoot(f.Player)+"/quote",b)).StatusCode);
        (await Post(admin,RefundRoot(f.Player),b)).EnsureSuccessStatusCode();
        var second=await CashRefundQuote(admin,f.Player,f.Collection,shift,10);await Access(false);Assert.Equal(HttpStatusCode.Forbidden,(await Post(admin,RefundRoot(f.Player),second)).StatusCode);
    }
    [Fact]
    public async Task Cash_refund_uses_payout_date_in_report_rows_csv_and_owner_net_without_rewriting_receipt()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRefundFixture(owner);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var source=await db.Collections.AsNoTracking().SingleAsync(c=>c.Id==f.Collection);
        var player=await db.Players.Where(p=>p.Id==f.Player).Select(p=>p.PlayerCode).SingleAsync();
        var branch=await db.CashSessions.Where(s=>s.Id==f.Session).Select(s=>s.BranchId).SingleAsync();
        var b=await CashRefundQuote(owner,f.Player,f.Collection,f.Session,40.25m);
        (await Post(owner,RefundRoot(f.Player),b)).EnsureSuccessStatusCode();
        var refund=await db.CollectionRefunds.AsNoTracking().SingleAsync(r=>r.CollectionId==f.Collection);
        Assert.Equal("Cash",refund.PayoutMethod);
        Assert.InRange(refund.CreatedAtUtc,DateTimeOffset.UtcNow.AddMinutes(-2),DateTimeOffset.UtcNow.AddMinutes(2));
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        var date=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(refund.CreatedAtUtc,zone).DateTime);
        var sourceDate=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(source.ConfirmedAtUtc,zone).DateTime);
        Assert.NotEqual(sourceDate,date); // Demo receipt is September; cash payout uses the real clock.
        var filter=$"from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}&provider=Cash&search={player}";
        var report=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?"+filter);
        Assert.Equal(0,report.GetProperty("totalAmount").GetDecimal());Assert.Equal(40.25m,report.GetProperty("totalRefunds").GetDecimal());Assert.Equal(-40.25m,report.GetProperty("netAmount").GetDecimal());
        var row=Assert.Single(report.GetProperty("items").EnumerateArray());Assert.Equal("Refund",row.GetProperty("collectionType").GetString());Assert.Equal(refund.CreatedAtUtc,row.GetProperty("confirmedAtUtc").GetDateTimeOffset());
        Assert.Equal(0,row.GetProperty("amount").GetDecimal());Assert.Equal(40.25m,row.GetProperty("refundAmount").GetDecimal());
        var original=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial?from={sourceDate:yyyy-MM-dd}&to={sourceDate:yyyy-MM-dd}&search={player}");
        Assert.Equal(f.Amount,original.GetProperty("netAmount").GetDecimal());Assert.Equal(0,original.GetProperty("totalRefunds").GetDecimal());
        var csvResponse=await owner.GetAsync("/api/v1/reports/financial/export?"+filter);csvResponse.EnsureSuccessStatusCode();var csv=await csvResponse.Content.ReadAsStringAsync();
        Assert.Contains("استرداد اشتراك",csv);Assert.Contains("-40.25",csv);Assert.Contains(refund.CreatedAtUtc.ToString("O"),csv);
        var period=$"from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}&branchId={branch}";
        var summary=(await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/owner-summary?"+period)).GetProperty("collections");
        var periodReport=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?"+period);
        Assert.Equal(periodReport.GetProperty("netAmount").GetDecimal(),summary.GetProperty("total").GetDecimal());
        var cashReport=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?"+period+"&provider=Cash");
        Assert.Equal(cashReport.GetProperty("netAmount").GetDecimal(),summary.GetProperty("todayCash").GetDecimal());
        Assert.Equal(summary.GetProperty("total").GetDecimal(),summary.GetProperty("monthCash").GetDecimal()+summary.GetProperty("monthOnline").GetDecimal()+summary.GetProperty("monthUnspecified").GetDecimal());
        using var other=await Staff(DemoSeed.FutureOwnerEmail);var isolated=await other.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?"+filter);Assert.Equal(0,isolated.GetProperty("totalCount").GetInt32());
    }
    [Fact]
    public async Task Cash_refund_database_rejects_electronic_source_and_preserves_historical_unknown_method()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRefundFixture(owner);var e=await Fixture();var collection=await Collection(e.Enrollment,e.Owner,100);
        (await Post(owner,RefundRoot(e.Player),new RefundWrite(collection,10,null))).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var refund=await db.CollectionRefunds.AsNoTracking().SingleAsync(r=>r.CollectionId==collection);
        Assert.Equal("Online",refund.PayoutMethod);Assert.InRange(refund.CreatedAtUtc,DateTimeOffset.UtcNow.AddMinutes(-2),DateTimeOffset.UtcNow.AddMinutes(2));
        var branch=await db.CashSessions.Where(s=>s.Id==f.Session).Select(s=>s.BranchId).SingleAsync();
        var posting=new Academy.Infrastructure.Subscriptions.CashRefundPosting {Id=Guid.NewGuid(),AcademyId=refund.AcademyId,SessionId=f.Session,BranchId=branch,RefundId=refund.Id,Amount=refund.Amount,Currency=refund.Currency,ActorUserId=refund.PerformedByUserId,RequestHash=new string('A',64),CreatedAtUtc=refund.CreatedAtUtc,UpdatedAtUtc=refund.CreatedAtUtc};
        db.CashRefundPostings.Add(posting);var error=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());var pg=Assert.IsType<PostgresException>(error.InnerException);Assert.Equal("23514",pg.SqlState);Assert.Contains("cash payout source",pg.MessageText);db.Entry(posting).State=EntityState.Detached;
        var historical=new Academy.Infrastructure.Operations.CollectionRefund {Id=Guid.NewGuid(),AcademyId=refund.AcademyId,CollectionId=collection,PlayerId=e.Player,Amount=5,Currency="EGP",Reason="استرداد تاريخي اصطناعي غير موثق الطريقة",PerformedByUserId=e.Owner,IdempotencyKey=Guid.NewGuid().ToString(),CreatedAtUtc=refund.CreatedAtUtc,UpdatedAtUtc=refund.CreatedAtUtc};db.CollectionRefunds.Add(historical);await db.SaveChangesAsync();
        var player=await db.Players.Where(p=>p.Id==e.Player).Select(p=>p.PlayerCode).SingleAsync();
        var report=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial?allDates=true&provider=Unspecified&search={player}");Assert.Equal(5,report.GetProperty("totalRefunds").GetDecimal());
        var row=Assert.Single(report.GetProperty("items").EnumerateArray());Assert.Equal("Unspecified",row.GetProperty("paymentMethod").GetString());Assert.Equal(-5,row.GetProperty("netAmount").GetDecimal());
        Assert.False(await db.CashRefundPostings.AnyAsync(p=>p.RefundId==historical.Id||p.RefundId==refund.Id));
    }
    [Fact]
    public async Task Cash_refund_wrong_branch_currency_and_changed_shift_balance_require_a_new_review()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRefundFixture(owner);var b=await CashRefundQuote(owner,f.Player,f.Collection,f.Session,10);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var branch=await db.CashSessions.Where(s=>s.Id==f.Session).Select(s=>s.BranchId).SingleAsync();var other=await db.Branches.Where(x=>x.AcademyId==DemoSeed.NogoomAcademyId&&x.IsActive&&x.Id!=branch).Select(x=>x.Id).FirstAsync();
        var created=await Post(owner,CashRoot,new{branchId=other,name="استرداد فرع آخر اصطناعي "+Guid.NewGuid()});created.EnsureSuccessStatusCode();var register=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();(await CashAssign(owner,register,f.Member)).EnsureSuccessStatusCode();var otherShift=await CashOpen(owner,register,"100");
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,RefundRoot(f.Player)+"/quote",b with {CashSessionId=otherShift})).StatusCode);
        (await CashTransfer(owner,f.Session,otherShift,"1")).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,RefundRoot(f.Player),b)).StatusCode);
        b=await CashRefundQuote(owner,f.Player,f.Collection,f.Session,10);(await Post(owner,RefundRoot(f.Player),b)).EnsureSuccessStatusCode();
        var e=await Fixture();var usd=await Collection(e.Enrollment,e.Owner,100);var source=await db.Collections.SingleAsync(c=>c.Id==usd);source.Currency="USD";(await db.Receipts.SingleAsync(r=>r.CollectionId==usd)).Currency="USD";await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,RefundRoot(e.Player)+"/quote",new RefundWrite(usd,10,null,f.Session))).StatusCode);
    }
}
