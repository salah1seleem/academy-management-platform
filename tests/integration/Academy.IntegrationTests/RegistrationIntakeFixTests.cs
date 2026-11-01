using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class V2ReceivablesTests
{
    [Fact]
    public async Task Registration_intake_admin_confirms_age_exception_or_registers_unassigned_without_reason_and_replays_once()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);
        var f=await CashRegistrationFixture(owner);
        var email=$"intake-{Guid.NewGuid():N}@example.test";
        var created=await Post(owner,"/api/v1/manage/staff",new{name="إداري استقبال اصطناعي",email,phone="010"+Random.Shared.Next(10000000,99999999),temporaryPassword="Demo-Only-123!",role="AcademyAdmin",jobTitle="استقبال"});created.EnsureSuccessStatusCode();
        var member=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var csrf=await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var accessRequest=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/manage/staff/{member}/access"){Content=JsonContent.Create(new{allBranches=false,branches=new[]{f.Body.BranchId},actions=StaffAccessRules.Modules.Select(module=>new{module,read=module is "players" or "subscriptions" or "finance",add=module is "players" or "subscriptions" or "finance",edit=false,delete=false})})};
        accessRequest.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());(await owner.SendAsync(accessRequest)).EnsureSuccessStatusCode();
        await using(var scope=factory.Services.CreateAsyncScope()){
            var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var category=await db.TrainingGroups.Where(g=>g.Id==f.Body.GroupId).Select(g=>g.AgeCategory).SingleAsync();category.MinimumBirthYear=2014;category.MaximumBirthYear=2018;
            db.StaffGrants.Add(new(){Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,MembershipId=member,Permission=ManualEnrollmentDiscount.Permission,Allowed=true});await db.SaveChangesAsync();
        }
        using var admin=await Staff(email);
        var body=f.Body with{PaymentMethod="Online",CashSessionId=null,DateOfBirth=new(2020,1,1)};
        Assert.DoesNotContain("AgeOverrideConfirmed",JsonSerializer.Serialize(body));
        var rejected=await Post(admin,"/api/v1/people/registrations",body);Assert.Equal(HttpStatusCode.UnprocessableEntity,rejected.StatusCode);
        Assert.Equal("registration_group_age",(await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var discount=new ExceptionalEnrollmentDiscount(RenewalDiscountType.FixedAmount,10,null);
        var quote=await Post(admin,"/api/v1/people/registration-quote",new{body.PlanId,body.BranchId,body.GuardianPhone,exceptionalDiscount=discount});quote.EnsureSuccessStatusCode();
        var due=decimal.Parse((await quote.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("finalAmount").GetString()!,System.Globalization.CultureInfo.InvariantCulture);
        body=body with{AgeOverrideConfirmed=true,ExceptionalDiscount=discount,ExpectedAmount=due};
        var key=Guid.NewGuid().ToString();var saved=await Post(admin,"/api/v1/people/registrations",body,key);Assert.True(saved.IsSuccessStatusCode,await saved.Content.ReadAsStringAsync());
        var result=await saved.Content.ReadFromJsonAsync<JsonElement>();var player=result.GetProperty("playerId").GetGuid();var enrollment=result.GetProperty("sportEnrollmentId").GetGuid();
        var replay=await Post(admin,"/api/v1/people/registrations",body,key);replay.EnsureSuccessStatusCode();Assert.Equal(player,(await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerId").GetGuid());
        var pending=body with{PlayerArabicName="لاعب انتظار اصطناعي "+Guid.NewGuid(),GuardianPhone="010"+Random.Shared.Next(10000000,99999999),GroupId=null,ExceptionalDiscount=null,AgeOverrideConfirmed=false,ExpectedAmount=f.Body.ExpectedAmount};
        var unassigned=await Post(admin,"/api/v1/people/registrations",pending);Assert.True(unassigned.IsSuccessStatusCode,await unassigned.Content.ReadAsStringAsync());
        var unassignedId=(await unassigned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sportEnrollmentId").GetGuid();
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await Post(foreign,"/api/v1/people/registrations",pending)).StatusCode);
        await using var verify=factory.Services.CreateAsyncScope();var proof=verify.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(f.Body.GroupId,(await proof.SportEnrollments.SingleAsync(e=>e.Id==enrollment)).TrainingGroupId);
        var audit=await proof.AcademyPolicyAudits.SingleAsync(a=>a.Id==enrollment);Assert.True(audit.After);Assert.Equal(member,await proof.AcademyMemberships.Where(m=>m.UserId==audit.ActorUserId&&m.Role==Academy.Infrastructure.Tenancy.AcademyRole.AcademyAdmin).Select(m=>m.Id).SingleAsync());
        Assert.Single(await proof.Receipts.Where(r=>r.PlayerId==player).ToListAsync());
        Assert.Single(await proof.SubscriptionPeriods.Where(p=>p.SportEnrollmentId==enrollment).ToListAsync());
        Assert.Null((await proof.SportEnrollments.SingleAsync(e=>e.Id==unassignedId)).TrainingGroupId);
        Assert.Single(await proof.SubscriptionPeriods.Where(p=>p.SportEnrollmentId==unassignedId).ToListAsync());
        Assert.False(await proof.PlayerAttendances.AnyAsync(a=>a.SportEnrollmentId==unassignedId));
        Assert.Contains(await proof.RenewalDiscountAdjustments.Where(a=>a.PerformedByUserId==audit.ActorUserId).ToListAsync(),a=>a.Reason=="خصم استثنائي عند التسجيل");
    }
}
