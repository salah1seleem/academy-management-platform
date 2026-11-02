using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2ReceivablesTests
{
    private async Task<(HttpClient Client,Guid Member,Guid Session)> CrossCashAdmin(HttpClient owner,Guid register,Guid branch,Guid ownerSession,bool payments=true)
    {
        var email=$"cross.payment.{Guid.NewGuid():N}@example.test";
        var created=await Post(owner,"/api/v1/manage/staff",new{name="إداري دفع عبر الفروع اصطناعي",email,phone="010"+Random.Shared.Next(10000000,99999999),temporaryPassword="Demo-Only-123!",role="AcademyAdmin",jobTitle="اختبار"});created.EnsureSuccessStatusCode();var member=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await CrossCashAccess(owner,member,branch,payments);(await CashAssign(owner,register,member)).EnsureSuccessStatusCode();(await Post(owner,$"{CashRoot}/sessions/{ownerSession}/close",new{countedAmount="100"})).EnsureSuccessStatusCode();
        var client=await Staff(email);return(client,member,await CashOpen(client,register,"0"));
    }
    private async Task CrossCashAccess(HttpClient owner,Guid member,Guid branch,bool payments)
    {
        var options=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/staff/{member}/access");var actions=options.GetProperty("modules").EnumerateArray().Select(x=>new{module=x.GetString(),read=x.GetString() is "finance" or "players" or "subscriptions" || payments&&x.GetString()=="payments",add=x.GetString() is "finance" or "subscriptions" || payments&&x.GetString()=="payments",edit=false,delete=false}).ToArray();
        var csrf=await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");var request=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/manage/staff/{member}/access"){Content=JsonContent.Create(new{allBranches=false,branches=new[]{branch},actions})};request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());(await owner.SendAsync(request)).EnsureSuccessStatusCode();
    }
    private async Task<JsonElement> CrossCashFind(HttpClient admin,Guid session,Guid player)
    {
        await using var scope=factory.Services.CreateAsyncScope();var code=await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Players.Where(p=>p.Id==player).Select(p=>p.PlayerCode).SingleAsync();
        var response=await Post(admin,$"{CashRoot}/sessions/{session}/cross-branch/search",new{search=code});Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());return Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
    }
    [Fact]
    public async Task Admin_cross_branch_cash_only_grants_payment_and_preserves_roster_links_and_financial_branch()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashCollectionFixture(owner,otherBranch:true);var a=await CrossCashAdmin(owner,f.Register,f.Branch,f.Session);using var admin=a.Client;
        var item=await CrossCashFind(admin,a.Session,f.Player);Assert.Equal(f.Enrollment,item.GetProperty("enrollmentId").GetGuid());Assert.DoesNotContain("guardian",item.GetRawText(),StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("medical",item.GetRawText(),StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("group",item.GetRawText(),StringComparison.OrdinalIgnoreCase);
        var reference=item.GetProperty("paymentReference").GetString()!;var root=$"{CashRoot}/sessions/{a.Session}/cross-branch";
        var review=await Post(admin,root+"/quote",new{reference,planId=Slice3DemoSeed.FootballMonthlyId});Assert.True(review.IsSuccessStatusCode,await review.Content.ReadAsStringAsync());var hash=(await review.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteHash").GetString()!;
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var before=await db.SportEnrollments.AsNoTracking().SingleAsync(e=>e.Id==f.Enrollment);
        var key=Guid.NewGuid().ToString();var body=new{reference,planId=Slice3DemoSeed.FootballMonthlyId,quoteHash=hash};var results=await Task.WhenAll(Post(admin,root+"/collect",body,key),Post(admin,root+"/collect",body,key));Assert.All(results,r=>Assert.True(r.IsSuccessStatusCode,r.Content.ReadAsStringAsync().GetAwaiter().GetResult()));
        var receiptId=(await results[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptId").GetGuid();Assert.Equal(receiptId,(await results[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptId").GetGuid());
        Assert.Single(await db.Collections.Where(c=>c.SportEnrollmentId==f.Enrollment).ToListAsync());Assert.Single(await db.SubscriptionPeriods.Where(p=>p.SportEnrollmentId==f.Enrollment).ToListAsync());var after=await db.SportEnrollments.AsNoTracking().SingleAsync(e=>e.Id==f.Enrollment);Assert.Equal(before.BranchId,after.BranchId);Assert.Equal(before.TrainingGroupId,after.TrainingGroupId);Assert.Single(await db.GuardianPlayerLinks.Where(l=>l.PlayerId==f.Player).ToListAsync());
        Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/api/v1/people/players/{f.Player}")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync($"/api/v1/manage/structure/groups/{before.TrainingGroupId}")).StatusCode);
        var receipt=await admin.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/receipts/{receiptId}");Assert.NotEqual(receipt.GetProperty("enrollmentBranch").GetString(),receipt.GetProperty("collectionBranch").GetString());
        var code=await db.Players.Where(p=>p.Id==f.Player).Select(p=>p.PlayerCode).SingleAsync();var report=await admin.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial?allDates=true&search={code}");Assert.Equal(1,report.GetProperty("totalCount").GetInt32());Assert.Equal("خدمة في فرع تسجيل آخر",report.GetProperty("items")[0].GetProperty("group").GetString());
        var detail=await admin.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{a.Session}");Assert.Single(detail.GetProperty("receipts").EnumerateArray());
        var recovery=await Post(admin,$"{CashRoot}/sessions/{a.Session}/collection-result",new{key,reference});recovery.EnsureSuccessStatusCode();Assert.Equal(receiptId,(await recovery.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptId").GetGuid());
        using var guardian=await Guardian();Assert.Equal(HttpStatusCode.OK,(await guardian.GetAsync($"/api/v1/guardian/subscriptions/receipts/{receiptId}")).StatusCode);
        (await Post(admin,$"{CashRoot}/sessions/{a.Session}/close",new{countedAmount=detail.GetProperty("balance").GetString()})).EnsureSuccessStatusCode();(await Post(admin,root+"/collect",body,key)).EnsureSuccessStatusCode();
        var ownerRegisterResponse=await Post(owner,CashRoot,new{branchId=f.Branch,name="صرف لنفس الفرع اصطناعي "+Guid.NewGuid().ToString("N")[..8]});ownerRegisterResponse.EnsureSuccessStatusCode();var ownerRegister=(await ownerRegisterResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();(await CashAssign(owner,ownerRegister,f.Member)).EnsureSuccessStatusCode();var ownerShift=await CashOpen(owner,ownerRegister,"100");
        var collectionId=await db.CashReceiptPostings.Where(p=>p.ReceiptId==receiptId).Select(p=>p.CollectionId).SingleAsync();var refund=await CashRefundQuote(owner,f.Player,collectionId,ownerShift,40);(await Post(owner,RefundRoot(f.Player),refund)).EnsureSuccessStatusCode();
        var payoutView=await admin.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{ownerShift}");var payout=Assert.Single(payoutView.GetProperty("refunds").EnumerateArray());Assert.False(payout.GetProperty("canReadPlayer").GetBoolean());Assert.Equal(receiptId,payout.GetProperty("receiptId").GetGuid());
    }
    [Fact]
    public async Task Admin_cross_branch_reference_is_actor_membership_shift_tenant_csrf_and_permission_bound()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashCollectionFixture(owner,otherBranch:true);var a=await CrossCashAdmin(owner,f.Register,f.Branch,f.Session);using var admin=a.Client;var item=await CrossCashFind(admin,a.Session,f.Player);var reference=item.GetProperty("paymentReference").GetString()!;var root=$"{CashRoot}/sessions/{a.Session}/cross-branch";
        var body=new{reference,planId=Slice3DemoSeed.FootballMonthlyId};Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsJsonAsync(root+"/quote",body)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(admin,root+"/quote",new{reference=reference+"x",planId=Slice3DemoSeed.FootballMonthlyId})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post(owner,root+"/quote",body)).StatusCode);using var other=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await Post(other,root+"/quote",body)).StatusCode);using var guardian=await Guardian();Assert.Equal(HttpStatusCode.Forbidden,(await Post(guardian,root+"/quote",body)).StatusCode);
        await CrossCashAccess(owner,a.Member,f.Branch,false);Assert.Equal(HttpStatusCode.Forbidden,(await Post(admin,root+"/quote",body)).StatusCode);await CrossCashAccess(owner,a.Member,f.Branch,true);
        var another=await Post(owner,CashRoot,new{branchId=f.Branch,name="مرجع لوردية أخرى "+Guid.NewGuid().ToString("N")[..8]});another.EnsureSuccessStatusCode();var register=(await another.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();(await CashAssign(owner,register,a.Member)).EnsureSuccessStatusCode();var secondShift=await CashOpen(admin,register,"0");
        Assert.Equal(HttpStatusCode.Forbidden,(await Post(admin,$"{CashRoot}/sessions/{secondShift}/cross-branch/quote",body)).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var user=await db.AcademyMemberships.Where(m=>m.Id==a.Member).Select(m=>m.UserId).SingleAsync();
        var expired="CP1."+scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("Academy.CrossBranchCashPayment.v1").Protect(JsonSerializer.Serialize(new{Academy=DemoSeed.NogoomAcademyId,User=user,Membership=a.Member,Session=a.Session,Enrollment=f.Enrollment,ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(-1)}));
        Assert.Equal(HttpStatusCode.Conflict,(await Post(admin,root+"/quote",new{reference=expired,planId=Slice3DemoSeed.FootballMonthlyId})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(admin,root+"/collect",new{reference=expired,planId=Slice3DemoSeed.FootballMonthlyId,quoteHash=new string('A',64)})).StatusCode);Assert.False(await db.Collections.AnyAsync(c=>c.SportEnrollmentId==f.Enrollment));
        var pending=Guid.NewGuid().ToString();var absent=await Post(admin,$"{CashRoot}/sessions/{a.Session}/collection-result",new{key=pending});absent.EnsureSuccessStatusCode();Assert.False((await absent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("canDiscard").GetBoolean());
        var retired=await Post(admin,$"{CashRoot}/sessions/{a.Session}/collection-result",new{key=pending,reference=expired});retired.EnsureSuccessStatusCode();Assert.True((await retired.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("canDiscard").GetBoolean());
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(admin,$"{CashRoot}/sessions/{a.Session}/collection-result",new{key=(string?)null})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/api/v1/people/players/{f.Player}")).StatusCode);
    }
    [Fact]
    public async Task Cash_payment_purpose_context_rejects_roster_identity_debt_and_read_only_writes()
    {
        var f=await Fixture();using var owner=await Staff(DemoSeed.OwnerEmail);var document=await Charge(owner,f.Enrollment,100);await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();db.BindRequestScope(DemoSeed.NogoomAcademyId,f.Owner,Guid.NewGuid(),null);db.LimitToCashPayment(Guid.NewGuid(),f.Branch,f.Enrollment,Slice3DemoSeed.FootballMonthlyId);
        var player=await db.Players.SingleAsync(p=>p.Id==f.Player);player.ArabicName="محاولة غير مسموحة";await Assert.ThrowsAsync<ResourceScopeException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
        var debt=await db.ReceivableDocuments.SingleAsync(d=>d.Id==document);debt.DueDate=debt.DueDate.AddDays(1);await Assert.ThrowsAsync<ResourceScopeException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
        var user=await db.Users.SingleAsync(u=>u.Id==f.Owner);user.DisplayName="محاولة هوية غير مسموحة";await Assert.ThrowsAsync<ResourceScopeException>(()=>db.SaveChangesAsync());
        await using var readScope=factory.Services.CreateAsyncScope();var read=readScope.ServiceProvider.GetRequiredService<FoundationDbContext>();read.BindRequestScope(DemoSeed.NogoomAcademyId,f.Owner,Guid.NewGuid(),null);read.LimitToCashPayment(Guid.NewGuid(),f.Branch);var row=await read.Players.SingleAsync(p=>p.Id==f.Player);row.ArabicName="محاولة من سياق بحث";await Assert.ThrowsAsync<ResourceScopeException>(()=>read.SaveChangesAsync());
    }
}
