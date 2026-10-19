using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;
public sealed partial class SubscriptionPaymentTests
{
    private const string VipRoot="/api/v1/manage/vip-rewards";
    private async Task<Guid> QualifyVip(FamilyFixture f)
    {
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var academy=DemoSeed.NogoomAcademyId;
        var owner=await db.AcademyMemberships.Where(m=>m.AcademyId==academy&&m.Role==AcademyRole.AcademyOwner&&m.IsActive).Select(m=>m.UserId).FirstAsync();
        var plan=new SubscriptionPlan{Id=Guid.NewGuid(),AcademyId=academy,ArabicName="باقة أهلية اصطناعية "+Guid.NewGuid().ToString("N")[..8],SportId=Slice2DemoSeed.FootballId,PlanType=SubscriptionPlanType.Duration,DurationMonths=1,Price=100000+await db.SubscriptionPlans.MaxAsync(p=>p.Price),Currency="EGP",Revision=1};db.SubscriptionPlans.Add(plan);await db.SaveChangesAsync();
        await using(var tx=await db.Database.BeginTransactionAsync()){await scope.ServiceProvider.GetRequiredService<CollectionService>().CreateCashAsync(academy,owner,f.Choices[0].SportEnrollmentId,plan.Id,"vip-qualify-"+f.Guardian,"Online");await tx.CommitAsync();}
        var renewal=new SubscriptionPlan{Id=Guid.NewGuid(),AcademyId=academy,ArabicName="باقة مكافأة ألف "+Guid.NewGuid().ToString("N")[..8],SportId=Slice2DemoSeed.FootballId,PlanType=SubscriptionPlanType.Duration,DurationMonths=1,Price=1000,Currency="EGP",Revision=1};db.SubscriptionPlans.Add(renewal);await db.SaveChangesAsync();return renewal.Id;
    }
    private async Task<VipRewardWrite> VipBody(HttpClient owner,Guid guardian,decimal percentage=20,DateOnly? expiry=null)
    {
        var body=new VipRewardWrite(Guid.NewGuid(),[guardian],percentage,"هدية أسرة اصطناعية دون إرسال خارجي",expiry??new(2026,10,28));
        var preview=await Post(owner,VipRoot+"/preview",body,await Csrf(owner));Assert.True(preview.IsSuccessStatusCode,await preview.Content.ReadAsStringAsync());return body with{QuoteHash=(await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteHash").GetString()};
    }
    private async Task<Guid> IssueVip(HttpClient owner,VipRewardWrite b){var r=await Post(owner,VipRoot+"/",b,await Csrf(owner));Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());return(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ids")[0].GetGuid();}
    private async Task<Guid> VipPayment(HttpClient c,Guid enrollment,Guid plan)
    {var r=await Post(c,"/api/v1/guardian/subscriptions/renewals",new{sportEnrollmentId=enrollment,subscriptionPlanId=plan},await Csrf(c),Guid.NewGuid().ToString("N"));Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());return(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid();}
    private async Task VipSimulate(HttpClient c,Guid id,string outcome){var r=await Post(c,$"/api/v1/guardian/subscriptions/payments/{id}/simulate",new{outcome},await Csrf(c));Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());}

    [Fact] public async Task V4_VIP_preview_issue_replay_owner_security_and_cancel_are_scoped_and_audited()
    {
        await using var f=await NewFamily();await QualifyVip(f);using var owner=await InboxStaff(DemoSeed.OwnerEmail);using var admin=await InboxStaff(DemoSeed.AdminEmail);using var foreign=await InboxStaff(DemoSeed.FutureOwnerEmail);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var notices=await db.AcademyNotices.CountAsync(n=>n.UserId==f.User);var before=await db.VipRewards.CountAsync();
        var body=await VipBody(owner,f.Guardian);Assert.Equal(before,await db.VipRewards.CountAsync());Assert.Equal(notices,await db.AcademyNotices.CountAsync(n=>n.UserId==f.User));
        foreach(var c in new[]{admin,f.Client})Assert.Equal(HttpStatusCode.Forbidden,(await Post(c,VipRoot+"/preview",body,await Csrf(c))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(foreign,VipRoot+"/preview",body,await Csrf(foreign))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,VipRoot+"/",body with{QuoteHash="stale"},await Csrf(owner))).StatusCode);
        var id=await IssueVip(owner,body);Assert.Equal(id,await IssueVip(owner,body));Assert.Equal(before+1,await db.VipRewards.CountAsync());Assert.Equal(notices+1,await db.AcademyNotices.CountAsync(n=>n.UserId==f.User));
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,VipRoot+"/",body with{Percentage=30},await Csrf(owner))).StatusCode);
        var guardian=await f.Client.GetFromJsonAsync<JsonElement>("/api/v1/guardian/vip-rewards");Assert.Contains(guardian.GetProperty("items").EnumerateArray(),r=>r.GetProperty("id").GetGuid()==id&&r.GetProperty("state").GetString()=="Available");
        var other=await foreign.GetFromJsonAsync<JsonElement>(VipRoot);Assert.DoesNotContain(other.GetProperty("items").EnumerateArray(),r=>r.GetProperty("id").GetGuid()==id);
        var rule=await db.VipRewards.Where(v=>v.Id==id).Select(v=>v.DiscountRuleId).SingleAsync();var catalog=await owner.GetFromJsonAsync<JsonElement>("/api/v1/discount-rules");Assert.DoesNotContain(catalog.GetProperty("items").EnumerateArray(),r=>r.GetProperty("id").GetGuid()==rule);
        var player=await db.SportEnrollments.Where(e=>e.Id==f.Choices[0].SportEnrollmentId).Select(e=>e.PlayerId).SingleAsync();using var link=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/discount-rules/players/{player}"){Content=JsonContent.Create(new{discountRuleId=rule,isActive=true})};link.Headers.Add("X-CSRF-TOKEN",await Csrf(owner));Assert.Equal(HttpStatusCode.NotFound,(await owner.SendAsync(link)).StatusCode);
        (await Post(owner,$"{VipRoot}/{id}/cancel",new{},await Csrf(owner))).EnsureSuccessStatusCode();Assert.NotNull(await db.VipRewards.Where(v=>v.Id==id).Select(v=>v.CancelledAtUtc).SingleAsync());
        Assert.Equal(HttpStatusCode.NotFound,(await Post(foreign,$"{VipRoot}/{id}/cancel",new{},await Csrf(foreign))).StatusCode);
        await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"VipRewards\" SET \"Percentage\"=25 WHERE \"Id\"={id}"));
    }
    [Fact] public async Task V4_VIP_1000_minus_20_percent_is_800_once_and_receipt_report_are_actual()
    {
        await using var f=await NewFamily();var plan=await QualifyVip(f);using var owner=await InboxStaff(DemoSeed.OwnerEmail);var id=await IssueVip(owner,await VipBody(owner,f.Guardian));
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();await using var internalRoute=await InternalGatewayRouteScope.Begin(factory.Services);var before=await db.Collections.Where(c=>c.AcademyId==DemoSeed.NogoomAcademyId).SumAsync(c=>c.Amount);
        var reportBefore=(await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?allDates=true")).GetProperty("netAmount").GetDecimal();
        var payment=await VipPayment(f.Client,f.Choices[0].SportEnrollmentId,plan);Assert.Equal(800,(await db.PaymentRequests.AsNoTracking().SingleAsync(p=>p.Id==payment)).Amount);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,$"{VipRoot}/{id}/cancel",new{},await Csrf(owner))).StatusCode);
        var second=await VipPayment(f.Client,f.Choices[1].SportEnrollmentId,plan);Assert.Equal(800,(await db.PaymentRequests.AsNoTracking().SingleAsync(p=>p.Id==second)).Amount); // sibling offer, not a second VIP reservation
        Assert.Single(await db.VipRewardUses.Where(u=>u.RewardId==id).ToArrayAsync());
        await VipSimulate(f.Client,payment,"Success");await VipSimulate(f.Client,payment,"Success");db.ChangeTracker.Clear();var receipt=await db.Receipts.SingleAsync(r=>r.Collection.PaymentRequestId==payment);Assert.Equal((1000m,200m,800m),(receipt.OriginalAmount,receipt.DiscountAmount,receipt.Amount));Assert.Contains("مكافأة",receipt.DiscountReason);Assert.Equal(before+800,await db.Collections.Where(c=>c.AcademyId==DemoSeed.NogoomAcademyId).SumAsync(c=>c.Amount));Assert.NotNull(await db.VipRewardUses.Where(u=>u.RewardId==id).Select(u=>u.UsedAtUtc).SingleAsync());
        var report=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?allDates=true");Assert.Equal(await db.Collections.Where(c=>c.AcademyId==DemoSeed.NogoomAcademyId).SumAsync(c=>c.Amount),(await owner.GetFromJsonAsync<JsonElement>("/api/v1/subscriptions/collections")).GetProperty("total").GetDecimal());Assert.Equal(reportBefore+800,report.GetProperty("netAmount").GetDecimal());
    }
    [Fact] public async Task V4_VIP_family_quote_uses_one_better_reward_line_and_approval_consumes_it_once()
    {
        await using var f=await NewFamily();var plan=await QualifyVip(f);
        var choices=f.Choices.Select(c=>c with{SubscriptionPlanId=plan}).ToArray();var updated=f with{Choices=choices};using var owner=await InboxStaff(DemoSeed.OwnerEmail);var id=await IssueVip(owner,await VipBody(owner,f.Guardian,30));
        var quoted=await Post(f.Client,FamilyQuotes+"/quote",new FamilyRenewalWrite(choices),await Csrf(f.Client));quoted.EnsureSuccessStatusCode();var quote=await quoted.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(500m,Decimal(quote,"discountAmount"));Assert.Single(quote.GetProperty("items").EnumerateArray(),l=>l.GetProperty("discountReason").GetString()?.Contains("مكافأة")==true);
        var pending=await FamilyProof(updated,await CreateFamily(updated));var requestId=pending.GetProperty("id").GetGuid();var approved=await Post(owner,$"{FamilyStaff}/{requestId}/review",FamilyApproval(pending),await Csrf(owner));Assert.True(approved.IsSuccessStatusCode,await approved.Content.ReadAsStringAsync());(await Post(owner,$"{FamilyStaff}/{requestId}/review",FamilyApproval(pending),await Csrf(owner))).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Single(await db.VipRewardUses.Where(u=>u.RewardId==id&&u.UsedAtUtc!=null).ToArrayAsync());Assert.Equal(1500m,await db.Receipts.Where(r=>choices.Select(c=>c.SportEnrollmentId).Contains(r.SportEnrollmentId)&&r.SubscriptionPlanId==plan).SumAsync(r=>r.Amount));
    }
    [Fact] public async Task V4_VIP_better_existing_offer_does_not_stack_or_consume_and_expired_entitlement_is_not_priced()
    {
        await using var f=await NewFamily();var plan=await QualifyVip(f);using var owner=await InboxStaff(DemoSeed.OwnerEmail);var id=await IssueVip(owner,await VipBody(owner,f.Guardian,30,new(2026,9,28)));
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var player=await db.SportEnrollments.Where(e=>e.Id==f.Choices[0].SportEnrollmentId).Select(e=>e.PlayerId).SingleAsync();
        var r=await Post(owner,"/api/v1/discount-rules/",new RuleWrite("عرض أفضل "+Guid.NewGuid().ToString("N")[..8],40,false,true,null),await Csrf(owner));r.EnsureSuccessStatusCode();var rule=(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();using var link=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/discount-rules/players/{player}"){Content=JsonContent.Create(new{discountRuleId=rule,isActive=true})};link.Headers.Add("X-CSRF-TOKEN",await Csrf(owner));(await owner.SendAsync(link)).EnsureSuccessStatusCode();
        var payment=await VipPayment(f.Client,f.Choices[0].SportEnrollmentId,plan);Assert.Equal(600,(await db.PaymentRequests.AsNoTracking().SingleAsync(p=>p.Id==payment)).Amount);Assert.False(await db.VipRewardUses.AnyAsync(u=>u.RewardId==id));
        var config=factory.Services.GetRequiredService<IConfiguration>();var old=config["Demo:ReferenceDate"];try{config["Demo:ReferenceDate"]="2026-09-29";var next=await VipPayment(f.Client,f.Choices[1].SportEnrollmentId,plan);Assert.Equal(800,(await db.PaymentRequests.AsNoTracking().SingleAsync(p=>p.Id==next)).Amount);Assert.False(await db.VipRewardUses.AnyAsync(u=>u.RewardId==id));var list=await f.Client.GetFromJsonAsync<JsonElement>("/api/v1/guardian/vip-rewards");Assert.Contains(list.GetProperty("items").EnumerateArray(),v=>v.GetProperty("id").GetGuid()==id&&v.GetProperty("state").GetString()=="Expired");}finally{config["Demo:ReferenceDate"]=old;}
    }
    [Fact] public async Task V4_VIP_new_child_checkout_reserves_until_proof_approval_and_uses_one_actual_receipt()
    {
        await using var f=await NewFamily();var plan=await QualifyVip(f);using var owner=await InboxStaff(DemoSeed.OwnerEmail);var reward=await IssueVip(owner,await VipBody(owner,f.Guardian,30));
        var created=await Post(f.Client,"/api/v1/guardian/enrollment-requests",new{requestType=2,newChildArabicName="طفل مكافأة اصطناعي "+Guid.NewGuid().ToString("N")[..6],newChildDateOfBirth="2016-01-01",sportId=Slice2DemoSeed.FootballId,preferredBranchId=Slice2DemoSeed.CityBranchId,subscriptionPlanId=plan},await Csrf(f.Client),Guid.NewGuid().ToString("N"));Assert.True(created.IsSuccessStatusCode,await created.Content.ReadAsStringAsync());var request=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var upload=new MultipartFormDataContent();var file=new ByteArrayContent([137,80,78,71,13,10,26,10,0,0,0,0]);file.Headers.ContentType=new("image/png");upload.Add(file,"file","synthetic-vip-proof.png");upload.Add(new StringContent("Proof"),"purpose");using var send=new HttpRequestMessage(HttpMethod.Post,"/api/v1/assets/upload"){Content=upload};send.Headers.Add("X-CSRF-TOKEN",await Csrf(f.Client));var saved=await f.Client.SendAsync(send);saved.EnsureSuccessStatusCode();var asset=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();(await Post(f.Client,$"/api/v1/guardian/enrollment-requests/{request}/proof",new{assetId=asset},await Csrf(f.Client))).EnsureSuccessStatusCode();
        var approval=await Post(owner,$"/api/v1/enrollment-requests/{request}/approve",new{trainingGroupId=Slice2DemoSeed.FootballGroupId},await Csrf(owner));Assert.True(approval.IsSuccessStatusCode,await approval.Content.ReadAsStringAsync());(await Post(owner,$"/api/v1/enrollment-requests/{request}/approve",new{trainingGroupId=Slice2DemoSeed.FootballGroupId},await Csrf(owner))).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var enrollment=await db.NewEnrollmentRequests.Where(e=>e.Id==request).Select(e=>e.CreatedSportEnrollmentId).SingleAsync();var receipt=await db.Receipts.SingleAsync(r=>r.SportEnrollmentId==enrollment);Assert.Equal((1000m,300m,700m),(receipt.OriginalAmount,receipt.DiscountAmount,receipt.Amount));Assert.Single(await db.VipRewardUses.Where(u=>u.RewardId==reward&&u.SourceKind=="Enrollment"&&u.SourceId==request&&u.UsedAtUtc!=null).ToArrayAsync());
    }
    [Fact] public async Task V4_VIP_concurrent_different_child_checkouts_cannot_reserve_one_reward_twice()
    {
        await using var f=await NewFamily();var plan=await QualifyVip(f);using var owner=await InboxStaff(DemoSeed.OwnerEmail);var id=await IssueVip(owner,await VipBody(owner,f.Guardian,30));var token=await Csrf(f.Client);
        var results=await Task.WhenAll(f.Choices.Select(c=>Post(f.Client,"/api/v1/guardian/subscriptions/renewals",new{sportEnrollmentId=c.SportEnrollmentId,subscriptionPlanId=plan},token,Guid.NewGuid().ToString("N"))));Assert.All(results,r=>r.EnsureSuccessStatusCode());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Single(await db.VipRewardUses.Where(u=>u.RewardId==id&&u.ReleasedAtUtc==null).ToArrayAsync());var ids=new List<Guid>();foreach(var r in results)ids.Add((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paymentId").GetGuid());Assert.Equal(new[]{700m,800m},await db.PaymentRequests.Where(p=>ids.Contains(p.Id)).OrderBy(p=>p.Amount).Select(p=>p.Amount).ToArrayAsync());
    }
    [Fact] public async Task V4_VIP_failed_reservation_releases_for_another_checkout_and_reserved_price_survives_expiry()
    {
        await using var f=await NewFamily();var plan=await QualifyVip(f);using var owner=await InboxStaff(DemoSeed.OwnerEmail);var id=await IssueVip(owner,await VipBody(owner,f.Guardian,30,new(2026,9,28)));
        await using var internalRoute=await InternalGatewayRouteScope.Begin(factory.Services);var first=await VipPayment(f.Client,f.Choices[0].SportEnrollmentId,plan);await VipSimulate(f.Client,first,"Failed");var second=await VipPayment(f.Client,f.Choices[1].SportEnrollmentId,plan);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Equal(2,await db.VipRewardUses.CountAsync(u=>u.RewardId==id));Assert.Equal(1,await db.VipRewardUses.CountAsync(u=>u.RewardId==id&&u.ReleasedAtUtc!=null));
        var config=factory.Services.GetRequiredService<IConfiguration>();var old=config["Demo:ReferenceDate"];
        try{config["Demo:ReferenceDate"]="2026-09-29";await VipSimulate(f.Client,second,"Success");Assert.Equal(700,await db.Receipts.Where(r=>r.Collection.PaymentRequestId==second).Select(r=>r.Amount).SingleAsync());}
        finally{config["Demo:ReferenceDate"]=old;}
    }
}
