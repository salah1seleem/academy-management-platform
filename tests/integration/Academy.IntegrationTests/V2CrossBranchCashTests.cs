using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;

public sealed partial class V2ReceivablesTests
{
    [Fact]
    public async Task Cross_branch_cash_distinct_keys_cannot_collect_the_same_review_twice()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashCollectionFixture(owner,otherBranch:true);var quote=await CashQuote(owner,f.Session,f.Enrollment);var hash=quote.GetProperty("quoteHash").GetString()!;
        var responses=await Task.WhenAll(CashCollect(owner,f.Session,f.Enrollment,hash),CashCollect(owner,f.Session,f.Enrollment,hash));Assert.Single(responses,r=>r.IsSuccessStatusCode);Assert.Single(responses,r=>r.StatusCode==HttpStatusCode.Conflict);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Single(await db.Collections.Where(c=>c.SportEnrollmentId==f.Enrollment).ToListAsync());Assert.Single(await db.SubscriptionPeriods.Where(p=>p.SportEnrollmentId==f.Enrollment).ToListAsync());Assert.Single(await db.CashReceiptPostings.Where(p=>p.SessionId==f.Session).ToListAsync());
        var refreshed=await CashQuote(owner,f.Session,f.Enrollment);Assert.NotEqual(hash,refreshed.GetProperty("quoteHash").GetString());
    }
    [Fact]
    public async Task Cross_branch_cash_renews_at_home_but_posts_reports_csv_and_receipt_to_collecting_branch_once()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashCollectionFixture(owner,otherBranch:true);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var enrollment=await db.SportEnrollments.AsNoTracking().Include(e=>e.Branch).SingleAsync(e=>e.Id==f.Enrollment);var home=enrollment.BranchId;var group=enrollment.TrainingGroupId;
        var collecting=await db.Branches.SingleAsync(b=>b.Id==f.Branch);var code=await db.Players.Where(p=>p.Id==f.Player).Select(p=>p.PlayerCode).SingleAsync();Assert.NotEqual(home,f.Branch);
        var options=await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{f.Session}/collection-options?search={code}");
        Assert.True(options.GetProperty("crossBranch").GetBoolean());var option=Assert.Single(options.GetProperty("items").EnumerateArray());Assert.Equal(home,option.GetProperty("branchId").GetGuid());
        var quote=await CashQuote(owner,f.Session,f.Enrollment);Assert.Equal(home,quote.GetProperty("enrollmentBranchId").GetGuid());Assert.Equal(f.Branch,quote.GetProperty("collectionBranchId").GetGuid());
        var key=Guid.NewGuid().ToString();var hash=quote.GetProperty("quoteHash").GetString()!;
        var results=await Task.WhenAll(CashCollect(owner,f.Session,f.Enrollment,hash,key),CashCollect(owner,f.Session,f.Enrollment,hash,key));Assert.All(results,r=>r.EnsureSuccessStatusCode());
        var receipt=(await results[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptId").GetGuid();Assert.Equal(receipt,(await results[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptId").GetGuid());
        var posting=await db.CashReceiptPostings.SingleAsync(p=>p.SessionId==f.Session);Assert.Equal(f.Branch,posting.BranchId);Assert.Single(await db.Collections.Where(c=>c.SportEnrollmentId==f.Enrollment).ToListAsync());Assert.Single(await db.SubscriptionPeriods.Where(p=>p.SportEnrollmentId==f.Enrollment).ToListAsync());
        enrollment=await db.SportEnrollments.AsNoTracking().SingleAsync(e=>e.Id==f.Enrollment);Assert.Equal(home,enrollment.BranchId);Assert.Equal(group,enrollment.TrainingGroupId);
        using var guardian=await Guardian();var detail=await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/subscriptions/receipts/{receipt}");Assert.Equal(collecting.ArabicName,detail.GetProperty("collectionBranch").GetString());
        var staffDetail=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/receipts/{receipt}");Assert.Equal(detail.GetProperty("enrollmentBranch").GetString(),staffDetail.GetProperty("enrollmentBranch").GetString());Assert.DoesNotContain("sessionId",detail.GetRawText(),StringComparison.OrdinalIgnoreCase);
        var filter=$"allDates=true&collectionType=Subscription&search={code}";
        var atCollecting=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial?{filter}&branchId={f.Branch}");Assert.Equal(posting.Amount,atCollecting.GetProperty("totalAmount").GetDecimal());Assert.Equal(collecting.ArabicName,Assert.Single(atCollecting.GetProperty("items").EnumerateArray()).GetProperty("branch").GetString());
        var atHome=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial?{filter}&branchId={home}");Assert.Equal(0,atHome.GetProperty("totalCount").GetInt32());
        var csv=await owner.GetStringAsync($"/api/v1/reports/financial/export?{filter}&branchId={f.Branch}");Assert.Contains(collecting.ArabicName,csv);Assert.Contains(posting.Amount.ToString("0.00",CultureInfo.InvariantCulture),csv);
        var summary=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-summary?allDates=true&branchId={f.Branch}");Assert.Contains(summary.GetProperty("byBranch").EnumerateArray(),b=>b.GetProperty("id").GetGuid()==f.Branch&&b.GetProperty("amount").GetDecimal()>=posting.Amount);
        var shift=await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{f.Session}");Assert.Equal(CashRegisterService.Money(100+posting.Amount),shift.GetProperty("balance").GetString());
        (await Post(owner,$"{CashRoot}/sessions/{f.Session}/close",new{countedAmount=shift.GetProperty("balance").GetString()})).EnsureSuccessStatusCode();(await CashCollect(owner,f.Session,f.Enrollment,hash,key)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Cross_branch_cash_uses_home_plan_publication_and_rejects_stale_group_and_inactive_home()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashCollectionFixture(owner,otherBranch:true);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var e=await db.SportEnrollments.SingleAsync(e=>e.Id==f.Enrollment);var originalGroup=e.TrainingGroupId;
        var plan=new SubscriptionPlan{Id=Guid.NewGuid(),AcademyId=e.AcademyId,SportId=e.SportId,ArabicName="باقة فرع التسجيل اصطناعية "+Guid.NewGuid().ToString("N")[..8],Price=500,Currency="EGP",PlanType=SubscriptionPlanType.Duration,DurationDays=30,AllBranches=false};
        db.SubscriptionPlans.Add(plan);db.PlanBranchPublications.Add(new(){Id=Guid.NewGuid(),AcademyId=e.AcademyId,SubscriptionPlanId=plan.Id,BranchId=e.BranchId});await db.SaveChangesAsync();
        var q=await Post(owner,$"{CashRoot}/sessions/{f.Session}/collection-quote",new{enrollmentId=e.Id,planId=plan.Id});q.EnsureSuccessStatusCode();
        var publication=await db.PlanBranchPublications.SingleAsync(p=>p.SubscriptionPlanId==plan.Id);publication.BranchId=f.Branch;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.NotFound,(await Post(owner,$"{CashRoot}/sessions/{f.Session}/collection-quote",new{enrollmentId=e.Id,planId=plan.Id})).StatusCode);
        publication.BranchId=e.BranchId;await db.SaveChangesAsync();var quote=await CashQuote(owner,f.Session,e.Id);
        var original=await db.TrainingGroups.SingleAsync(g=>g.Id==originalGroup);var replacement=new TrainingGroup{Id=Guid.NewGuid(),AcademyId=e.AcademyId,BranchId=e.BranchId,SportId=e.SportId,AgeCategoryId=original.AgeCategoryId,ArabicName="تسكين بعد المعاينة اصطناعي "+Guid.NewGuid().ToString("N")[..8],Capacity=10};db.TrainingGroups.Add(replacement);await db.SaveChangesAsync();e.TrainingGroupId=replacement.Id;await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict,(await CashCollect(owner,f.Session,e.Id,quote.GetProperty("quoteHash").GetString()!)).StatusCode);Assert.False(await db.Collections.AnyAsync(c=>c.SportEnrollmentId==e.Id));
        var branch=await db.Branches.SingleAsync(b=>b.Id==e.BranchId);var active=branch.IsActive;try{branch.IsActive=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.NotFound,(await Post(owner,$"{CashRoot}/sessions/{f.Session}/collection-quote",new{enrollmentId=e.Id,planId=Slice3DemoSeed.FootballMonthlyId})).StatusCode);}finally{branch.IsActive=active;await db.SaveChangesAsync();}
    }

    [Fact]
    public async Task Cross_branch_cash_does_not_expand_scoped_admin_or_other_academy_visibility()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashCollectionFixture(owner,otherBranch:true);var email=$"cash.branch.{Guid.NewGuid():N}@example.test";
        var response=await Post(owner,"/api/v1/manage/staff",new{name="إداري فرع اصطناعي",email,phone="010"+Random.Shared.Next(10000000,99999999),temporaryPassword="Demo-Only-123!",role="AcademyAdmin",jobTitle="اختبار"});response.EnsureSuccessStatusCode();var member=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var options=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff/{member}/access");var actions=options.GetProperty("modules").EnumerateArray().Select(x=>new{module=x.GetString(),read=x.GetString() is "finance" or "players" or "subscriptions" or "payments",add=x.GetString() is "finance" or "subscriptions" or "payments",edit=false,delete=false}).ToArray();
        var csrf=await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");var change=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/manage/staff/{member}/access"){Content=JsonContent.Create(new{allBranches=false,branches=new[]{f.Branch},actions})};change.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());(await owner.SendAsync(change)).EnsureSuccessStatusCode();
        (await CashAssign(owner,f.Register,member)).EnsureSuccessStatusCode();(await Post(owner,$"{CashRoot}/sessions/{f.Session}/close",new{countedAmount="100"})).EnsureSuccessStatusCode();using var admin=await Staff(email);var session=await CashOpen(admin,f.Register,"0");
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var code=await db.Players.Where(p=>p.Id==f.Player).Select(p=>p.PlayerCode).SingleAsync();
        var search=await admin.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{session}/collection-options?search={code}");Assert.False(search.GetProperty("crossBranch").GetBoolean());Assert.Empty(search.GetProperty("items").EnumerateArray());Assert.Equal(HttpStatusCode.NotFound,(await Post(admin,$"{CashRoot}/sessions/{session}/collection-quote",new{enrollmentId=f.Enrollment,planId=Slice3DemoSeed.FootballMonthlyId})).StatusCode);
        using var other=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"{CashRoot}/sessions/{session}/collection-options?search={code}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await Post(other,$"{CashRoot}/sessions/{session}/collection-quote",new{enrollmentId=f.Enrollment,planId=Slice3DemoSeed.FootballMonthlyId})).StatusCode);
        Assert.False(await db.CashReceiptPostings.AnyAsync(p=>p.SessionId==session));
        (await Post(admin,$"{CashRoot}/sessions/{session}/close",new{countedAmount="0"})).EnsureSuccessStatusCode();
        // The child's home-branch administrator must not reattribute another
        // branch's cash merely because its immutable posting is outside their scope.
        var home=await db.SportEnrollments.Where(e=>e.Id==f.Enrollment).Select(e=>e.BranchId).SingleAsync();
        var ownerShift=await CashOpen(owner,f.Register,"100");var quote=await CashQuote(owner,ownerShift,f.Enrollment);var collected=await CashCollect(owner,ownerShift,f.Enrollment,quote.GetProperty("quoteHash").GetString()!);collected.EnsureSuccessStatusCode();var receiptId=(await collected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptId").GetGuid();
        // V2-07E2 grants financial receipt facts at the collecting branch only;
        // it still never grants the foreign child's operational profile.
        Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync($"/api/v1/subscriptions/receipts/{receiptId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/api/v1/people/players/{f.Player}")).StatusCode);
        var collectingReport=await admin.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial?allDates=true&search={code}");Assert.Equal(1,collectingReport.GetProperty("totalCount").GetInt32());Assert.Equal("خدمة في فرع تسجيل آخر",collectingReport.GetProperty("items")[0].GetProperty("group").GetString());
        var access=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/manage/staff/{member}/access"){Content=JsonContent.Create(new{allBranches=false,branches=new[]{home},actions})};var token=await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");access.Headers.Add("X-CSRF-TOKEN",token.GetProperty("token").GetString());(await owner.SendAsync(access)).EnsureSuccessStatusCode();
        var homeReport=await admin.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial?allDates=true&search={code}&branchId={home}");Assert.Equal(0,homeReport.GetProperty("totalCount").GetInt32());
        var unfiltered=await admin.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial?allDates=true&search={code}");Assert.Equal(0,unfiltered.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"{CashRoot}/sessions/{ownerShift}")).StatusCode);
        var receipt=await admin.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/receipts/{receiptId}");Assert.NotEqual(receipt.GetProperty("enrollmentBranch").GetString(),receipt.GetProperty("collectionBranch").GetString());
    }

    [Fact]
    public async Task Cross_branch_cash_refund_uses_collecting_branch_and_net_reporting_without_moving_service()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashCollectionFixture(owner,otherBranch:true);var q=await CashQuote(owner,f.Session,f.Enrollment);(await CashCollect(owner,f.Session,f.Enrollment,q.GetProperty("quoteHash").GetString()!)).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var c=await db.Collections.SingleAsync(c=>c.SportEnrollmentId==f.Enrollment);var home=await db.SportEnrollments.Where(e=>e.Id==f.Enrollment).Select(e=>e.BranchId).SingleAsync();var code=await db.Players.Where(p=>p.Id==f.Player).Select(p=>p.PlayerCode).SingleAsync();
        var b=await CashRefundQuote(owner,f.Player,c.Id,f.Session,40);(await Post(owner,RefundRoot(f.Player),b)).EnsureSuccessStatusCode();var posting=await db.CashRefundPostings.SingleAsync(p=>p.SessionId==f.Session);Assert.Equal(f.Branch,posting.BranchId);
        var report=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial?allDates=true&branchId={f.Branch}&search={code}&collectionType=Subscription");Assert.Equal(c.Amount-40,report.GetProperty("netAmount").GetDecimal());Assert.Equal(2,report.GetProperty("totalCount").GetInt32());
        var other=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial?allDates=true&branchId={home}&search={code}&collectionType=Subscription");Assert.Equal(0,other.GetProperty("totalCount").GetInt32());Assert.Single(await db.SubscriptionPeriods.Where(p=>p.SportEnrollmentId==f.Enrollment).ToListAsync());
        var remaining=await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{f.Session}");Assert.Equal(CashRegisterService.Money(100+c.Amount-40),remaining.GetProperty("balance").GetString());
        var registerResponse=await Post(owner,CashRoot,new{branchId=home,name="استرداد بفرع خاطئ "+Guid.NewGuid().ToString("N")[..8]});registerResponse.EnsureSuccessStatusCode();var register=(await registerResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();(await CashAssign(owner,register,f.Member)).EnsureSuccessStatusCode();var wrongShift=await CashOpen(owner,register,"100");
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,RefundRoot(f.Player)+"/quote",new Academy.Api.Operations.RefundWrite(c.Id,10,null,wrongShift))).StatusCode);
        await using var tx=await db.Database.BeginTransactionAsync();var refund=new Academy.Infrastructure.Operations.CollectionRefund{Id=Guid.NewGuid(),AcademyId=c.AcademyId,CollectionId=c.Id,PlayerId=f.Player,Amount=10,Currency=c.Currency,PerformedByUserId=Guid.Parse(c.ConfirmedBy),IdempotencyKey=Guid.NewGuid().ToString()};db.CollectionRefunds.Add(refund);await db.SaveChangesAsync();
        db.CashRefundPostings.Add(new(){Id=Guid.NewGuid(),AcademyId=c.AcademyId,SessionId=wrongShift,BranchId=home,RefundId=refund.Id,Amount=refund.Amount,Currency=refund.Currency,ActorUserId=refund.PerformedByUserId,RequestHash=new string('A',64),CreatedAtUtc=refund.CreatedAtUtc,UpdatedAtUtc=refund.CreatedAtUtc});
        var error=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.Equal("23514",Assert.IsType<PostgresException>(error.InnerException).SqlState);await tx.RollbackAsync();
    }

    [Fact]
    public async Task Cross_branch_cash_demo_seed_is_repeatable_without_creating_a_collection_or_payment()
    {
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var before=await db.Collections.CountAsync();var payments=await db.PaymentRequests.CountAsync();
        await CrossBranchCashDemoSeed.Seed(db);await CrossBranchCashDemoSeed.Seed(db);Assert.Single(await db.Players.Where(p=>p.Id==CrossBranchCashDemoSeed.PlayerId).ToListAsync());Assert.Single(await db.SportEnrollments.Where(e=>e.Id==CrossBranchCashDemoSeed.EnrollmentId).ToListAsync());
        Assert.Equal(before,await db.Collections.CountAsync());Assert.Equal(payments,await db.PaymentRequests.CountAsync());Assert.False(await db.Collections.AnyAsync(c=>c.SportEnrollmentId==CrossBranchCashDemoSeed.EnrollmentId));
        var publication=await db.PlanBranchPublications.SingleAsync(p=>p.SubscriptionPlanId==CrossBranchCashDemoSeed.PlanId);Assert.Equal(await db.SportEnrollments.Where(e=>e.Id==CrossBranchCashDemoSeed.EnrollmentId).Select(e=>e.BranchId).SingleAsync(),publication.BranchId);
    }
}
