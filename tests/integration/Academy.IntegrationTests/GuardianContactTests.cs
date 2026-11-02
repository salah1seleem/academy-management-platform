using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
[Collection("Tenant authentication database")]
public sealed class GuardianContactTests:IAsyncLifetime
{
    private Factory factory=null!;
    public Task InitializeAsync(){factory=new(Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")??throw new InvalidOperationException("Separate test database required"));_=factory.CreateClient();return Task.CompletedTask;}
    public async Task DisposeAsync()=>await factory.DisposeAsync();
    private static async Task<HttpResponseMessage> Post(HttpClient c,string path,object body){var token=await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");var r=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};r.Headers.Add("X-CSRF-TOKEN",token.GetProperty("token").GetString());return await c.SendAsync(r);}
    private async Task<HttpClient> Staff(string email){var c=factory.CreateClient();(await Post(c,"/api/v1/auth/login",new {email,password="Demo-Only-123!"})).EnsureSuccessStatusCode();return c;}
    private async Task<HttpClient> Mobile(string phone)
    {
        var c=factory.CreateClient();var requested=await c.PostAsJsonAsync("/api/v1/mobile/auth/otp/request",new {phoneNumber=phone});requested.EnsureSuccessStatusCode();var challenge=await requested.Content.ReadFromJsonAsync<JsonElement>();var verified=await c.PostAsJsonAsync("/api/v1/mobile/auth/otp/verify",new {phoneNumber=phone,challengeId=challenge.GetProperty("challengeId").GetGuid(),code="246810",deviceName="Contact isolation test"});verified.EnsureSuccessStatusCode();var tokens=await verified.Content.ReadFromJsonAsync<JsonElement>();c.DefaultRequestHeaders.Authorization=new("Bearer",tokens.GetProperty("accessToken").GetString());return c;
    }
    [Theory]
    [InlineData("٠١٠٠٠٠٠٠٠٠١")]
    [InlineData("00201000000001")]
    [InlineData("+20 (100) 000-0001")]
    public void Click_to_chat_normalizes_digits_and_contains_only_a_minimal_encoded_draft(string phone){var value=GuardianContactEndpoints.ChatUrl(phone,"أكاديمية خيالية","طفل خيالي");Assert.NotNull(value);var uri=new Uri(value);Assert.Equal("wa.me",uri.Host);Assert.Equal("/201000000001",uri.AbsolutePath);var draft=Uri.UnescapeDataString(uri.Query);Assert.Contains("أكاديمية خيالية",draft);Assert.Contains("طفل خيالي",draft);Assert.DoesNotContain("تقييم",draft);Assert.Null(GuardianContactEndpoints.ChatUrl("not-a-phone","خيالية"));}
    [Fact]
    public async Task Contacts_are_linked_scoped_and_never_a_guardian_directory()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);using var coach=await Mobile(DemoSeed.CoachPhone);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var day=await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(DemoSeed.NogoomAcademyId,db,TimeProvider.System);
        var coachId=await db.AcademyMemberships.Where(m=>m.AcademyId==DemoSeed.NogoomAcademyId&&m.User.Email==DemoSeed.CoachEmail).Select(m=>m.Id).SingleAsync();
        var player=await db.SportEnrollments.Where(e=>e.AcademyId==DemoSeed.NogoomAcademyId&&e.IsActive&&db.StaffGroupAssignments.Any(a=>a.AcademyMembershipId==coachId&&a.IsActive&&a.TrainingGroupId==FoundationDbContext.AssignedGroupAt(e.AcademyId,e.Id,day))&&db.GuardianPlayerLinks.Any(l=>l.PlayerId==e.PlayerId&&l.IsActive)).Select(e=>e.PlayerId).FirstAsync();
        var original=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/players/{player}/guardian-contacts");Assert.NotEmpty(original.GetProperty("contacts").EnumerateArray());
        var phone="+20109"+Random.Shared.Next(1000000,9999999);var now=DateTimeOffset.UtcNow;var user=new ApplicationUser{Id=Guid.NewGuid(),UserName=phone,PhoneNumber=phone,DisplayName="ولي إضافي خيالي",CreatedAtUtc=now,UpdatedAtUtc=now};Assert.True((await users.CreateAsync(user)).Succeeded);
        var guardian=new GuardianProfile{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,UserId=user.Id,DisplayName=user.DisplayName,ContactPhone=phone,CreatedAtUtc=now,UpdatedAtUtc=now};db.GuardianProfiles.Add(guardian);db.AcademyMemberships.Add(new(){Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,UserId=user.Id,Role=AcademyRole.Guardian,CreatedAtUtc=now,UpdatedAtUtc=now});var ownerId=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync();db.GuardianPlayerLinks.Add(new(){Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,GuardianId=guardian.Id,PlayerId=player,RelationshipType="والد خيالي",CreatedByUserId=ownerId,CreatedAtUtc=now,UpdatedAtUtc=now});
        var outside=new Player{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,PlayerCode="CONTACT-"+Guid.NewGuid().ToString("N")[..10],ArabicName="طفل غير مسند خيالي",DateOfBirth=new(2016,1,1),CreatedAtUtc=now,UpdatedAtUtc=now};db.Players.Add(outside);await db.SaveChangesAsync();
        var result=await coach.GetFromJsonAsync<JsonElement>($"/api/v1/people/players/{player}/guardian-contacts");Assert.Contains(result.GetProperty("contacts").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==guardian.Id&&x.GetProperty("phone").GetString()==phone);Assert.Contains(result.GetProperty("contacts").EnumerateArray(),x=>x.GetProperty("url").GetString()!.StartsWith("https://wa.me/"));
        Assert.Equal(HttpStatusCode.NotFound,(await coach.GetAsync($"/api/v1/people/players/{outside.Id}/guardian-contacts")).StatusCode);
        var foreign=await db.Players.Where(p=>p.AcademyId==DemoSeed.FutureAcademyId).Select(p=>p.Id).FirstAsync();Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync($"/api/v1/people/players/{foreign}/guardian-contacts")).StatusCode);
        using var parent=await Mobile(DemoSeed.GuardianPhone);Assert.Equal(HttpStatusCode.Forbidden,(await parent.GetAsync($"/api/v1/people/players/{player}/guardian-contacts")).StatusCode);
        guardian.ContactPhone="invalid";await db.SaveChangesAsync();var invalid=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/guardians/{guardian.Id}/contact");Assert.Equal(JsonValueKind.Null,invalid.GetProperty("contacts")[0].GetProperty("url").ValueKind);
        var link=await db.GuardianPlayerLinks.SingleAsync(l=>l.PlayerId==player&&l.GuardianId==guardian.Id);link.IsActive=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.NotFound,(await coach.GetAsync($"/api/v1/people/guardians/{guardian.Id}/contact")).StatusCode);
    }
    private sealed class Factory(string connection):WebApplicationFactory<Program>{protected override void ConfigureWebHost(IWebHostBuilder b){b.UseEnvironment("Demo");b.UseSetting("ConnectionStrings:Default",connection);b.UseSetting("Demo:SeedEnabled","true");b.UseSetting("Demo:SeedProfile","LegacyRegression");b.UseSetting("Demo:StaffPassword","Demo-Only-123!");b.UseSetting("Demo:FixedOtpEnabled","true");b.UseSetting("Demo:FixedOtp","246810");b.UseSetting("Imports:WorkerEnabled","false");}}
}
