using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;

public sealed partial class V2ReceivablesTests
{
    private async Task<(Guid Session,Guid Enrollment,Guid Player,Guid Register,Guid Member,Guid Branch)> CashCollectionFixture(HttpClient owner, bool otherBranch = false)
    {
        var f=await Fixture();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var branch=otherBranch?await db.Branches.Where(b=>b.AcademyId==DemoSeed.NogoomAcademyId&&b.Id!=f.Branch&&b.IsActive).Select(b=>b.Id).FirstAsync():f.Branch;
        var response=await Post(owner,CashRoot,new{branchId=branch,name="قبض اشتراك اختبار "+Guid.NewGuid().ToString("N")[..10]});response.EnsureSuccessStatusCode();var register=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var member=await db.AcademyMemberships.Where(m=>m.AcademyId==DemoSeed.NogoomAcademyId&&m.UserId==f.Owner).Select(m=>m.Id).SingleAsync();(await CashAssign(owner,register,member)).EnsureSuccessStatusCode();
        return(await CashOpen(owner,register,"100"),f.Enrollment,f.Player,register,member,branch);
    }
    private async Task<JsonElement> CashQuote(HttpClient owner,Guid session,Guid enrollment)
    {
        var response=await Post(owner,$"{CashRoot}/sessions/{session}/collection-quote",new{enrollmentId=enrollment,planId=Slice3DemoSeed.FootballMonthlyId});response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private Task<HttpResponseMessage> CashCollect(HttpClient owner,Guid session,Guid enrollment,string quote,string? key=null)=>Post(owner,$"{CashRoot}/sessions/{session}/collections",new{enrollmentId=enrollment,planId=Slice3DemoSeed.FootballMonthlyId,quoteHash=quote},key);
    [Fact]
    public async Task Cash_collection_posts_receipt_service_and_shift_atomically_and_retries_once()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashCollectionFixture(owner);var quote=await CashQuote(owner,f.Session,f.Enrollment);var hash=quote.GetProperty("quoteHash").GetString()!;var key=Guid.NewGuid().ToString();
        var results=await Task.WhenAll(CashCollect(owner,f.Session,f.Enrollment,hash,key),CashCollect(owner,f.Session,f.Enrollment,hash,key));Assert.All(results,r=>r.EnsureSuccessStatusCode());
        var receipt=(await results[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptId").GetGuid();Assert.Equal(receipt,(await results[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptId").GetGuid());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var posting=await db.CashReceiptPostings.SingleAsync(p=>p.SessionId==f.Session);var source=await db.Receipts.SingleAsync(r=>r.Id==receipt);
        Assert.Equal(source.CollectionId,posting.CollectionId);Assert.Equal(source.Amount,posting.Amount);Assert.Equal("Cash",source.PaymentMethod);Assert.Single(await db.SubscriptionPeriods.Where(p=>p.SportEnrollmentId==f.Enrollment).ToListAsync());Assert.Single(await db.Collections.Where(c=>c.SportEnrollmentId==f.Enrollment).ToListAsync());
        var detail=await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{f.Session}");Assert.Equal((100+source.Amount).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture),detail.GetProperty("balance").GetString());Assert.Single(detail.GetProperty("receipts").EnumerateArray());Assert.Equal(source.Amount.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture),detail.GetProperty("receipts")[0].GetProperty("amount").GetString());
        using var guardian=await Guardian();Assert.Equal(HttpStatusCode.OK,(await guardian.GetAsync($"/api/v1/guardian/subscriptions/receipts/{receipt}")).StatusCode);
        (await Post(owner,$"{CashRoot}/sessions/{f.Session}/close",new{countedAmount=detail.GetProperty("balance").GetString(),reason=(string?)null})).EnsureSuccessStatusCode();
        (await CashCollect(owner,f.Session,f.Enrollment,hash,key)).EnsureSuccessStatusCode(); // lost response can be replayed after close
        Assert.Equal(HttpStatusCode.Conflict,(await CashCollect(owner,f.Session,f.Enrollment,new string('f',64),key)).StatusCode);
    }
    [Fact]
    public async Task Cash_collection_changed_price_rejects_every_financial_effect_until_requoted()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashCollectionFixture(owner);var quote=await CashQuote(owner,f.Session,f.Enrollment);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var plan=await db.SubscriptionPlans.SingleAsync(p=>p.Id==Slice3DemoSeed.FootballMonthlyId);var price=plan.Price;
        try{plan.Price+=1;await db.SaveChangesAsync();var rejected=await CashCollect(owner,f.Session,f.Enrollment,quote.GetProperty("quoteHash").GetString()!);Assert.Equal(HttpStatusCode.Conflict,rejected.StatusCode);Assert.False(await db.Collections.AnyAsync(c=>c.SportEnrollmentId==f.Enrollment));Assert.False(await db.SubscriptionPeriods.AnyAsync(p=>p.SportEnrollmentId==f.Enrollment));Assert.False(await db.CashReceiptPostings.AnyAsync(p=>p.SessionId==f.Session));}
        finally{plan.Price=price;await db.SaveChangesAsync();}
    }
    [Fact]
    public async Task Cash_collection_closed_disabled_or_revoked_shift_cannot_collect()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashCollectionFixture(owner);var quote=await CashQuote(owner,f.Session,f.Enrollment);var hash=quote.GetProperty("quoteHash").GetString()!;
        (await CashAssign(owner,f.Register,f.Member,false)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await CashCollect(owner,f.Session,f.Enrollment,hash)).StatusCode);(await CashAssign(owner,f.Register,f.Member)).EnsureSuccessStatusCode();
        var settings=await CashSettings();try{await CashSetConfiguration(owner,settings with{CashEnabled=false});Assert.Equal(HttpStatusCode.Conflict,(await CashCollect(owner,f.Session,f.Enrollment,hash)).StatusCode);}finally{await CashSetConfiguration(owner,settings);}
        (await Post(owner,$"{CashRoot}/sessions/{f.Session}/close",new{countedAmount="100",reason=(string?)null})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await CashCollect(owner,f.Session,f.Enrollment,hash)).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();Assert.False(await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Collections.AnyAsync(c=>c.SportEnrollmentId==f.Enrollment));
    }
    [Fact]
    public async Task Cash_collection_race_with_close_never_changes_a_closed_expected_balance()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashCollectionFixture(owner);var quote=await CashQuote(owner,f.Session,f.Enrollment);var responses=await Task.WhenAll(CashCollect(owner,f.Session,f.Enrollment,quote.GetProperty("quoteHash").GetString()!),Post(owner,$"{CashRoot}/sessions/{f.Session}/close",new{countedAmount="100",reason=(string?)null}));responses[1].EnsureSuccessStatusCode();Assert.True(responses[0].IsSuccessStatusCode||responses[0].StatusCode==HttpStatusCode.Conflict);
        var detail=await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{f.Session}");Assert.Equal(detail.GetProperty("balance").GetString(),detail.GetProperty("session").GetProperty("expectedAmount").GetString());
    }
    [Fact]
    public async Task Cash_collection_isolated_search_csrf_and_inactive_resource_are_enforced()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);using var other=await Staff(DemoSeed.FutureOwnerEmail);using var guardian=await Guardian();var f=await CashCollectionFixture(owner);
        string code;await using(var s=factory.Services.CreateAsyncScope())code=await s.ServiceProvider.GetRequiredService<FoundationDbContext>().Players.Where(p=>p.Id==f.Player).Select(p=>p.PlayerCode).SingleAsync();
        var search=await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{f.Session}/collection-options?search={code}");Assert.Contains(search.GetProperty("items").EnumerateArray(),i=>i.GetProperty("enrollmentId").GetGuid()==f.Enrollment);
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{f.Session}/collection-options?search=x")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"{CashRoot}/sessions/{f.Session}/collection-options?search=aa")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await guardian.GetAsync($"{CashRoot}/sessions/{f.Session}/collection-options?search=aa")).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync($"{CashRoot}/sessions/{f.Session}/collection-quote",new{enrollmentId=f.Enrollment,planId=Slice3DemoSeed.FootballMonthlyId})).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var player=await db.Players.SingleAsync(p=>p.Id==f.Player);player.IsActive=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.NotFound,(await Post(owner,$"{CashRoot}/sessions/{f.Session}/collection-quote",new{enrollmentId=f.Enrollment,planId=Slice3DemoSeed.FootballMonthlyId})).StatusCode);
    }
    [Fact]
    public async Task Cash_collection_sources_and_posting_are_immutable_in_sql_and_ef()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashCollectionFixture(owner);var quote=await CashQuote(owner,f.Session,f.Enrollment);(await CashCollect(owner,f.Session,f.Enrollment,quote.GetProperty("quoteHash").GetString()!)).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var p=await db.CashReceiptPostings.SingleAsync(p=>p.SessionId==f.Session);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CashReceiptPostings\" SET \"Amount\"=1 WHERE \"Id\"={p.Id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Collections\" SET \"Amount\"=1 WHERE \"Id\"={p.CollectionId}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Receipts\" WHERE \"Id\"={p.ReceiptId}"))).SqlState);
        p.Amount+=1;await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
    }
    [Fact]
    public async Task Cash_collection_database_rejects_a_second_posting_with_forged_source_amount()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashCollectionFixture(owner);var quote=await CashQuote(owner,f.Session,f.Enrollment);(await CashCollect(owner,f.Session,f.Enrollment,quote.GetProperty("quoteHash").GetString()!)).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var p=await db.CashReceiptPostings.SingleAsync(p=>p.SessionId==f.Session);
        db.CashReceiptPostings.Add(new(){Id=Guid.NewGuid(),AcademyId=p.AcademyId,SessionId=p.SessionId,BranchId=p.BranchId,CollectionId=p.CollectionId,ReceiptId=p.ReceiptId,Amount=p.Amount+1,Currency=p.Currency,ActorUserId=p.ActorUserId,RequestKey=Guid.NewGuid().ToString(),RequestHash=new string('A',64)});
        var error=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.Equal("23514",Assert.IsType<PostgresException>(error.InnerException).SqlState);Assert.Single(await db.CashReceiptPostings.Where(x=>x.SessionId==f.Session).AsNoTracking().ToListAsync());
    }
    [Fact]
    public async Task Cash_collection_admin_needs_both_subscription_and_cash_permissions_and_own_shift()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashCollectionFixture(owner);var email=$"cash.collect.{Guid.NewGuid():N}@example.test";
        var created=await Post(owner,"/api/v1/manage/staff",new{name="أمين تحصيل اصطناعي",email,phone="010"+Random.Shared.Next(10000000,99999999),temporaryPassword="Demo-Only-123!",role="AcademyAdmin",jobTitle="اختبار"});created.EnsureSuccessStatusCode();var member=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        async Task Access(bool subscriptions)
        {
            var options=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff/{member}/access");var actions=options.GetProperty("modules").EnumerateArray().Select(x=>new{module=x.GetString(),read=x.GetString() is "finance" or "players" || subscriptions&&x.GetString() is "subscriptions" or "payments",add=x.GetString()=="finance"||subscriptions&&x.GetString() is "subscriptions" or "payments",edit=false,delete=false}).ToArray();
            var csrf=await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");var r=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/manage/staff/{member}/access"){Content=JsonContent.Create(new{allBranches=false,branches=new[]{f.Branch},actions})};r.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());(await owner.SendAsync(r)).EnsureSuccessStatusCode();
        }
        await Access(false);(await CashAssign(owner,f.Register,member)).EnsureSuccessStatusCode();using var admin=await Staff(email);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post(admin,$"{CashRoot}/sessions/{f.Session}/collection-quote",new{enrollmentId=f.Enrollment,planId=Slice3DemoSeed.FootballMonthlyId})).StatusCode);
        await Access(true);Assert.Equal(HttpStatusCode.Forbidden,(await Post(admin,$"{CashRoot}/sessions/{f.Session}/collection-quote",new{enrollmentId=f.Enrollment,planId=Slice3DemoSeed.FootballMonthlyId})).StatusCode);
        (await Post(owner,$"{CashRoot}/sessions/{f.Session}/close",new{countedAmount="100",reason=(string?)null})).EnsureSuccessStatusCode();var session=await CashOpen(admin,f.Register,"0");var quote=await CashQuote(admin,session,f.Enrollment);(await CashCollect(admin,session,f.Enrollment,quote.GetProperty("quoteHash").GetString()!)).EnsureSuccessStatusCode();
        var pending = await PendingCashRequest(admin, f.Enrollment);
        var pendingAmount = await PendingCashAmount(admin, pending);
        (await PendingCash(admin, pending, session, pendingAmount)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await PendingCash(owner, pending, session, pendingAmount)).StatusCode);
    }
    [Fact]
    public async Task Cash_collection_guardian_receipt_access_requires_a_current_trusted_link()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashCollectionFixture(owner);
        // An owned household keeps the latest-100 list assertion deterministic
        // even when other tests retain immutable receipts for the shared guardian.
        var phone="010"+Random.Shared.Next(10000000,99999999);
        await using(var household=factory.Services.CreateAsyncScope()) {
            var householdDb=household.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var normalized=EgyptPhoneNormalizer.Normalize(phone)!;
            var user=new Academy.Infrastructure.Identity.ApplicationUser {Id=Guid.NewGuid(),UserName=normalized,PhoneNumber=normalized,DisplayName="ولي إيصال اصطناعي",IsActive=true};
            var created=await household.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Academy.Infrastructure.Identity.ApplicationUser>>().CreateAsync(user);Assert.True(created.Succeeded);
            var profile=new Academy.Infrastructure.People.GuardianProfile {Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,UserId=user.Id,DisplayName=user.DisplayName,ContactPhone=normalized};
            householdDb.AcademyMemberships.Add(new(){Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,UserId=user.Id,Role=Academy.Infrastructure.Tenancy.AcademyRole.Guardian});householdDb.GuardianProfiles.Add(profile);
            (await householdDb.GuardianPlayerLinks.SingleAsync(l=>l.PlayerId==f.Player)).GuardianId=profile.Id;await householdDb.SaveChangesAsync();
        }
        using var guardian=await Guardian(phone);var quote=await CashQuote(owner,f.Session,f.Enrollment);var response=await CashCollect(owner,f.Session,f.Enrollment,quote.GetProperty("quoteHash").GetString()!);response.EnsureSuccessStatusCode();var receipt=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptId").GetGuid();
        var list=await guardian.GetFromJsonAsync<JsonElement>("/api/v1/guardian/subscriptions/receipts");Assert.Contains(list.GetProperty("items").EnumerateArray(),r=>r.GetProperty("id").GetGuid()==receipt);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var link=await db.GuardianPlayerLinks.SingleAsync(l=>l.PlayerId==f.Player);link.IsActive=false;await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync($"/api/v1/guardian/subscriptions/receipts/{receipt}")).StatusCode);
        list=await guardian.GetFromJsonAsync<JsonElement>("/api/v1/guardian/subscriptions/receipts");Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(),r=>r.GetProperty("id").GetGuid()==receipt);
    }
}
