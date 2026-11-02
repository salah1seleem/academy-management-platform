using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    [Fact]
    public async Task Session_lifecycle_active_login_requires_explicit_logout_and_does_not_replace_or_revoke_identity()
    {
        using var owner=await Staff();var before=await owner.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Equal(HttpStatusCode.Conflict,(await Write(owner,"/api/v1/auth/login",new{email=DemoSeed.AdminEmail,password="Demo-Only-123!"})).StatusCode);
        Assert.Equal(before.GetProperty("userId").GetGuid(),(await owner.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("userId").GetGuid());
        (await Write(owner,"/api/v1/auth/logout",new{})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Unauthorized,(await owner.GetAsync("/api/v1/me")).StatusCode);
        (await Write(owner,"/api/v1/auth/login",new{email=DemoSeed.AdminEmail,password="Demo-Only-123!"})).EnsureSuccessStatusCode();Assert.Equal("AcademyAdmin",(await owner.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("role").GetString());
    }
    [Fact]
    public async Task Session_lifecycle_OTP_over_active_cookie_is_not_consumed_and_logout_then_OTP_creates_a_working_guardian_session()
    {
        using var owner=await Staff();var request=await Write(owner,"/api/v1/auth/guardian/otp/request",new{phoneNumber=DemoSeed.GuardianPhone});request.EnsureSuccessStatusCode();var challenge=(await request.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetGuid();var body=new{challengeId=challenge,phoneNumber=DemoSeed.GuardianPhone,code="246810"};Assert.Equal(HttpStatusCode.Conflict,(await Write(owner,"/api/v1/auth/guardian/otp/verify",body)).StatusCode);Assert.Equal("AcademyOwner",(await owner.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("role").GetString());
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Null(await db.GuardianOtpChallenges.Where(c=>c.Id==challenge).Select(c=>c.ConsumedAtUtc).SingleAsync());
        (await Write(owner,"/api/v1/auth/logout",new{})).EnsureSuccessStatusCode();(await Write(owner,"/api/v1/auth/guardian/otp/verify",body)).EnsureSuccessStatusCode();Assert.Equal("Guardian",(await owner.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("role").GetString());Assert.Equal(HttpStatusCode.OK,(await owner.GetAsync("/api/v1/guardian/home")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await owner.GetAsync("/api/v1/manage/staff")).StatusCode);(await Write(owner,"/api/v1/auth/logout",new{})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Unauthorized,(await Write(owner,"/api/v1/auth/guardian/otp/verify",body)).StatusCode);
    }
    [Fact]
    public async Task Session_lifecycle_academy_switch_preserves_account_absolute_expiry_and_same_session_without_role_union()
    {
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var user=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync();var before=await db.UserSessions.Where(s=>s.UserId==user).Select(s=>s.Id).ToArrayAsync();using var owner=await Staff();var session=await db.UserSessions.SingleAsync(s=>s.UserId==user&&!before.Contains(s.Id));var absolute=session.AbsoluteExpiresAtUtc;var expiry=session.ExpiresAtUtc;
        var member=await db.AcademyMemberships.SingleOrDefaultAsync(m=>m.UserId==user&&m.AcademyId==DemoSeed.FutureAcademyId&&m.Role==AcademyRole.AcademyAdmin);var wasActive=member?.IsActive??false;if(member is null){member=new(){Id=Guid.NewGuid(),AcademyId=DemoSeed.FutureAcademyId,UserId=user,Role=AcademyRole.AcademyAdmin,AllBranches=false};db.AcademyMemberships.Add(member);}member.IsActive=true;await db.SaveChangesAsync();
        try {
            Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync("/api/v1/session/academy",new{academyId=DemoSeed.FutureAcademyId})).StatusCode);
            (await Write(owner,"/api/v1/session/academy",new{academyId=DemoSeed.FutureAcademyId})).EnsureSuccessStatusCode();var me=await owner.GetFromJsonAsync<JsonElement>("/api/v1/me");Assert.Equal(DemoSeed.FutureAcademyId,me.GetProperty("academyId").GetGuid());Assert.Equal("AcademyAdmin",me.GetProperty("role").GetString());Assert.Equal(user,me.GetProperty("userId").GetGuid());Assert.Equal(HttpStatusCode.Forbidden,(await owner.GetAsync($"/api/v1/tenant/probe/{DemoSeed.NogoomAcademyId}")).StatusCode);await db.Entry(session).ReloadAsync();Assert.Null(session.RevokedAtUtc);Assert.Equal(absolute,session.AbsoluteExpiresAtUtc);Assert.Equal(expiry,session.ExpiresAtUtc);Assert.Equal(DemoSeed.FutureAcademyId,session.ActiveAcademyId);
            (await Write(owner,"/api/v1/session/academy",new{academyId=DemoSeed.NogoomAcademyId})).EnsureSuccessStatusCode();Assert.Equal("AcademyOwner",(await owner.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("role").GetString());(await Write(owner,"/api/v1/auth/logout",new{})).EnsureSuccessStatusCode();await db.Entry(session).ReloadAsync();Assert.NotNull(session.RevokedAtUtc);
        }finally{member.IsActive=wasActive;await db.SaveChangesAsync();}
    }
    [Fact]
    public async Task Session_lifecycle_ticket_renewal_cannot_replace_identity_revive_expiry_or_extend_absolute_limit()
    {
        SetVisitClock();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var user=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync();var other=await db.Users.Where(u=>u.Email==DemoSeed.AdminEmail).Select(u=>u.Id).SingleAsync();var store=factory.Services.GetRequiredService<DatabaseTicketStore>();var now=factory.VisitClock.GetUtcNow();AuthenticationTicket Ticket(Guid id)=>new(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,id.ToString()),new("academy_id",DemoSeed.NogoomAcademyId.ToString())],IdentityConstants.ApplicationScheme)),new(){IssuedUtc=now,ExpiresUtc=now.AddMinutes(30)},IdentityConstants.ApplicationScheme);var key=await store.StoreAsync(Ticket(user));await Assert.ThrowsAsync<InvalidOperationException>(()=>store.RenewAsync(key,Ticket(other)));Assert.Equal(user.ToString(),(await store.RetrieveAsync(key))!.Principal.FindFirstValue(ClaimTypes.NameIdentifier));var session=await db.UserSessions.SingleAsync(s=>s.Id==key);var absolute=session.AbsoluteExpiresAtUtc;await store.RenewAsync(key,Ticket(user));await db.Entry(session).ReloadAsync();Assert.Equal(absolute,session.AbsoluteExpiresAtUtc);factory.VisitClock.Now=now.AddMinutes(31);Assert.Null(await store.RetrieveAsync(key));await Assert.ThrowsAsync<InvalidOperationException>(()=>store.RenewAsync(key,Ticket(user)));await store.RemoveAsync(key);factory.VisitClock.Now=now;await store.RenewAsync(key,Ticket(user));Assert.Null(await store.RetrieveAsync(key));
    }
}
