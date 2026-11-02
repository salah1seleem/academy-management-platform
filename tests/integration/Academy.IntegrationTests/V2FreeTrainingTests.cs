using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Slice3;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;

public sealed partial class SubscriptionPaymentTests
{
    private const string Waivers = "/api/v1/finance/free-training";
    private async Task<FreeTrainingApprove> WaiverBody(HttpClient owner, FamilyFixture f, string reason="منحة تدريب اصطناعية")
    {
        var c = f.Choices[0]; var r = await Post(owner, Waivers+"/quote", new FreeTrainingReview(c.SportEnrollmentId,c.SubscriptionPlanId,reason),await Csrf(owner)); r.EnsureSuccessStatusCode();
        return new(c.SportEnrollmentId,c.SubscriptionPlanId,reason,(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteHash").GetString()!);
    }
    private async Task<HttpResponseMessage> Waive(HttpClient owner,FreeTrainingApprove body,string? key=null)=>await Post(owner,Waivers+"/approve",body,await Csrf(owner),key??Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task Free_training_owner_approves_once_with_no_cash_or_receipt_and_guardian_sees_only_own_service()
    {
        await using var f=await NewFamily();using var owner=await InboxStaff(DemoSeed.OwnerEmail);using var other=await GuardianClient();var body=await WaiverBody(owner,f);var enrollment=body.SportEnrollmentId;
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.False(await db.SubscriptionPeriods.AnyAsync(p=>p.SportEnrollmentId==enrollment));Assert.False(await db.FreeTrainingApprovals.AnyAsync(p=>p.SportEnrollmentId==enrollment));
        var key=Guid.NewGuid().ToString("N");var replies=await Task.WhenAll(Waive(owner,body,key),Waive(owner,body,key));Assert.All(replies,r=>r.EnsureSuccessStatusCode());var result=await replies[0].Content.ReadFromJsonAsync<JsonElement>();var second=await replies[1].Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(result.GetProperty("periodId").GetGuid(),second.GetProperty("periodId").GetGuid());var id=result.GetProperty("periodId").GetGuid();
        var period=await db.SubscriptionPeriods.SingleAsync(p=>p.Id==id);Assert.Equal(SubscriptionOrigin.ApprovedWaiver,period.Origin);Assert.Null(period.CollectionId);Assert.Null(period.ImportJobId);Assert.Equal(0,period.PriceSnapshot);Assert.NotNull(period.FreeTrainingApprovalId);Assert.Equal(1,await db.FreeTrainingApprovals.CountAsync(a=>a.SportEnrollmentId==enrollment));
        Assert.False(await db.Collections.AnyAsync(p=>p.SportEnrollmentId==enrollment));Assert.False(await db.Receipts.AnyAsync(p=>p.SportEnrollmentId==enrollment));Assert.False(await db.RenewalRequests.AnyAsync(p=>p.SportEnrollmentId==enrollment));Assert.False(await db.ReceivableDocuments.AnyAsync(p=>p.SportEnrollmentId==enrollment));
        var view=await f.Client.GetAsync($"/api/v1/guardian/subscriptions/periods/{id}/adjustments");view.EnsureSuccessStatusCode();var json=await view.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal("ApprovedWaiver",json.GetProperty("origin").GetString());Assert.DoesNotContain(body.Reason!,json.ToString());Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/guardian/subscriptions/periods/{id}/adjustments")).StatusCode);
        var plan=await db.SubscriptionPlans.SingleAsync(p=>p.Id==body.SubscriptionPlanId);var old=plan.Price;try{plan.Price+=1;await db.SaveChangesAsync();(await Waive(owner,body,key)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await Waive(owner,body with{Reason="سبب مختلف"},key)).StatusCode);}finally{plan.Price=old;await db.SaveChangesAsync();}
    }
    [Fact]
    public async Task Free_training_rejects_administrator_coach_guardian_foreign_tenant_and_missing_csrf()
    {
        await using var f=await NewFamily();using var owner=await InboxStaff(DemoSeed.OwnerEmail);var body=await WaiverBody(owner,f);
        foreach(var email in new[]{DemoSeed.AdminEmail,DemoSeed.CoachEmail}){using var staff=await InboxStaff(email);Assert.Equal(HttpStatusCode.Forbidden,(await Waive(staff,body)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await staff.GetAsync($"{Waivers}/{body.SportEnrollmentId}/options")).StatusCode);}
        Assert.Equal(HttpStatusCode.Forbidden,(await Waive(f.Client,body)).StatusCode);using var foreign=await InboxStaff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await Waive(foreign,body)).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(Waivers+"/approve",body)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(owner,Waivers+"/quote",new FreeTrainingReview(body.SportEnrollmentId,body.SubscriptionPlanId," "),await Csrf(owner))).StatusCode);Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Waive(owner,body,"short")).StatusCode);
    }
    [Fact]
    public async Task Free_training_stale_price_pending_payment_and_busy_enrollment_never_activate()
    {
        await using var f=await NewFamily();using var owner=await InboxStaff(DemoSeed.OwnerEmail);var body=await WaiverBody(owner,f);await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var plan=await db.SubscriptionPlans.SingleAsync(p=>p.Id==body.SubscriptionPlanId);var old=plan.Price;
        try{plan.Price+=1;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.Conflict,(await Waive(owner,body)).StatusCode);}finally{plan.Price=old;await db.SaveChangesAsync();}
        await using(var tx=await db.Database.BeginTransactionAsync()){await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"renewal-enrollment:{DemoSeed.NogoomAcademyId:N}:{body.SportEnrollmentId:N}"},0))");Assert.Equal(HttpStatusCode.Conflict,(await Waive(owner,body)).StatusCode);await tx.RollbackAsync();}
        _=await CreateFamily(f);Assert.Equal(HttpStatusCode.Conflict,(await Waive(owner,body)).StatusCode);Assert.False(await db.SubscriptionPeriods.AnyAsync(p=>p.SportEnrollmentId==body.SportEnrollmentId));Assert.False(await db.FreeTrainingApprovals.AnyAsync(p=>p.SportEnrollmentId==body.SportEnrollmentId));
    }
    [Fact]
    public async Task Free_training_database_and_ef_guard_sources_terms_and_zero_amount_without_blocking_cancellation()
    {
        await using var f=await NewFamily();using var owner=await InboxStaff(DemoSeed.OwnerEmail);var body=await WaiverBody(owner,f);var r=await Waive(owner,body);r.EnsureSuccessStatusCode();var id=(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("periodId").GetGuid();await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();var period=await db.SubscriptionPeriods.SingleAsync(p=>p.Id==id);var source=await db.FreeTrainingApprovals.SingleAsync(a=>a.Id==period.FreeTrainingApprovalId);
        source.Reason="تغيير تاريخي";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"FreeTrainingApprovals\" SET \"Reason\"='forged' WHERE \"Id\"={source.Id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SubscriptionPeriods\" SET \"PriceSnapshot\"=1 WHERE \"Id\"={id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SubscriptionPeriods\" SET \"PlanNameSnapshot\"='forged' WHERE \"Id\"={id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"SubscriptionPeriods\" WHERE \"Id\"={id}"))).SqlState);
        (await Post(owner,$"/api/v1/subscriptions/periods/{id}/cancel",new{effectiveDate=period.StartDate,reason="",expectedVersion=period.Version},await Csrf(owner),Guid.NewGuid().ToString("N"))).EnsureSuccessStatusCode();Assert.Equal(SubscriptionPeriodStatus.Cancelled,await db.SubscriptionPeriods.Where(p=>p.Id==id).Select(p=>p.Status).SingleAsync());
    }
    [Fact]
    public async Task Free_training_period_failure_rolls_back_approval_and_can_retry_same_operation()
    {
        await using var f=await NewFamily();using var owner=await InboxStaff(DemoSeed.OwnerEmail);var body=await WaiverBody(owner,f);var key=Guid.NewGuid().ToString("N");var name="waiver_failure_"+body.SportEnrollmentId.ToString("N");await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();
        try{var sql=$"CREATE FUNCTION {name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"SportEnrollmentId\"='{body.SportEnrollmentId}'::uuid THEN RAISE EXCEPTION 'synthetic waiver rollback'; END IF; RETURN NEW; END $$; CREATE TRIGGER {name} BEFORE INSERT ON \"SubscriptionPeriods\" FOR EACH ROW EXECUTE FUNCTION {name}();";await db.Database.ExecuteSqlRawAsync(sql);Assert.Equal(HttpStatusCode.InternalServerError,(await Waive(owner,body,key)).StatusCode);Assert.False(await db.FreeTrainingApprovals.AnyAsync(a=>a.SportEnrollmentId==body.SportEnrollmentId));Assert.False(await db.SubscriptionPeriods.AnyAsync(a=>a.SportEnrollmentId==body.SportEnrollmentId));}
        finally{var sql=$"DROP TRIGGER IF EXISTS {name} ON \"SubscriptionPeriods\"; DROP FUNCTION IF EXISTS {name}();";await db.Database.ExecuteSqlRawAsync(sql);}
        (await Waive(owner,body,key)).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Free_training_owner_audit_is_private_and_native_guardian_reads_the_same_entitlement()
    {
        await using var f=await NewFamily();using var owner=await InboxStaff(DemoSeed.OwnerEmail);using var admin=await InboxStaff(DemoSeed.AdminEmail);var body=await WaiverBody(owner,f,"سبب إعفاء داخلي اصطناعي");var r=await Waive(owner,body);r.EnsureSuccessStatusCode();var id=(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("periodId").GetGuid();
        var details=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/periods/{id}");Assert.Equal(body.Reason,details.GetProperty("waiver").GetProperty("reason").GetString());Assert.Null(details.GetProperty("receiptId").GetString());var limited=await admin.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/periods/{id}");Assert.Equal(JsonValueKind.Null,limited.GetProperty("waiver").ValueKind);Assert.DoesNotContain(body.Reason!,limited.ToString());
        await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();var phone=await db.Users.Where(u=>u.Id==f.User).Select(u=>u.PhoneNumber).SingleAsync();using var mobile=factory.CreateClient();var challenge=await mobile.PostAsJsonAsync("/api/v1/mobile/auth/otp/request",new{phoneNumber=phone});challenge.EnsureSuccessStatusCode();var challengeId=(await challenge.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetGuid();var verified=await mobile.PostAsJsonAsync("/api/v1/mobile/auth/otp/verify",new{phoneNumber=phone,challengeId,code="246810",deviceName="Waiver integration test"});verified.EnsureSuccessStatusCode();mobile.DefaultRequestHeaders.Authorization=new("Bearer",(await verified.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        var native=await mobile.GetAsync($"/api/v1/guardian/subscriptions/periods/{id}/adjustments");native.EnsureSuccessStatusCode();Assert.Equal("ApprovedWaiver",(await native.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("origin").GetString());Assert.DoesNotContain(body.Reason!,await native.Content.ReadAsStringAsync());Assert.Equal(HttpStatusCode.Forbidden,(await mobile.GetAsync($"{Waivers}/{body.SportEnrollmentId}/options")).StatusCode);
        var player=await db.SportEnrollments.Where(e=>e.Id==body.SportEnrollmentId).Select(e=>e.PlayerId).SingleAsync();var profile=await mobile.GetAsync($"/api/v1/guardian/children/{player}/profile");profile.EnsureSuccessStatusCode();Assert.Contains(id.ToString(),await profile.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Free_training_database_refuses_forged_actor_and_a_period_for_another_child()
    {
        await using var f=await NewFamily();using var owner=await InboxStaff(DemoSeed.OwnerEmail);var body=await WaiverBody(owner,f);var r=await Waive(owner,body);r.EnsureSuccessStatusCode();var id=(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("periodId").GetGuid();await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();var period=await db.SubscriptionPeriods.AsNoTracking().SingleAsync(p=>p.Id==id);var source=await db.FreeTrainingApprovals.AsNoTracking().SingleAsync(a=>a.Id==period.FreeTrainingApprovalId);var admin=await db.AcademyMemberships.SingleAsync(m=>m.AcademyId==DemoSeed.NogoomAcademyId&&m.User.Email==DemoSeed.AdminEmail);
        db.FreeTrainingApprovals.Add(new(){Id=Guid.NewGuid(),AcademyId=source.AcademyId,BranchId=source.BranchId,SportEnrollmentId=source.SportEnrollmentId,SubscriptionPlanId=source.SubscriptionPlanId,ActorUserId=admin.UserId,ActorMembershipId=admin.Id,WaivedAmount=source.WaivedAmount,Currency=source.Currency,Reason="مصدر مزور اصطناعي",IdempotencyKey=Guid.NewGuid().ToString("N"),RequestHash=new('A',64),QuoteHash=new('B',64),PlanTermsJson=source.PlanTermsJson,StartDate=source.StartDate,EndDate=source.EndDate,InitialSessions=source.InitialSessions});Assert.Equal("23514",Assert.IsType<PostgresException>((await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync())).InnerException).SqlState);db.ChangeTracker.Clear();
        var forged=new SubscriptionPeriod{Id=Guid.NewGuid(),AcademyId=period.AcademyId,SportEnrollmentId=f.Choices[1].SportEnrollmentId,SportId=period.SportId,SubscriptionPlanId=period.SubscriptionPlanId,Origin=SubscriptionOrigin.ApprovedWaiver,FreeTrainingApprovalId=source.Id,PriceSnapshot=0,CurrencySnapshot=period.CurrencySnapshot,CreatedByUserId=period.CreatedByUserId,StartDate=period.StartDate,EndDate=period.EndDate,InitialSessions=period.InitialSessions,RemainingSessions=period.RemainingSessions,Status=period.Status};PlanTerms.From(period,(await db.SubscriptionPlans.SingleAsync(p=>p.Id==period.SubscriptionPlanId))).Capture(forged);db.SubscriptionPeriods.Add(forged);Assert.Equal("23514",Assert.IsType<PostgresException>((await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync())).InnerException).SqlState);db.ChangeTracker.Clear();Assert.False(await db.SubscriptionPeriods.AnyAsync(p=>p.Id==forged.Id));
    }
    [Fact]
    public async Task Free_training_demo_seed_is_idempotent_and_does_not_issue_waivers_or_collections()
    {
        await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();var users=s.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var before=await db.FreeTrainingApprovals.CountAsync(a=>a.SportEnrollmentId==FreeTrainingDemoSeed.EnrollmentId);var periods=await db.SubscriptionPeriods.CountAsync(p=>p.SportEnrollmentId==FreeTrainingDemoSeed.EnrollmentId);
        await FreeTrainingDemoSeed.Seed(db,users);await FreeTrainingDemoSeed.Seed(db,users);Assert.Equal(1,await db.Players.CountAsync(p=>p.Id==FreeTrainingDemoSeed.PlayerId));Assert.Equal(before,await db.FreeTrainingApprovals.CountAsync(a=>a.SportEnrollmentId==FreeTrainingDemoSeed.EnrollmentId));Assert.Equal(periods,await db.SubscriptionPeriods.CountAsync(p=>p.SportEnrollmentId==FreeTrainingDemoSeed.EnrollmentId));Assert.False(await db.Collections.AnyAsync(p=>p.SportEnrollmentId==FreeTrainingDemoSeed.EnrollmentId));
    }
}
