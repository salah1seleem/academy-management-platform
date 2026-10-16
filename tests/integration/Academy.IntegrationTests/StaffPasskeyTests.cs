using System.Formats.Cbor;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class StaffPasskeyTests : IAsyncLifetime
{
    private sealed class Clock:TimeProvider {public DateTimeOffset Now=new(2026,10,16,10,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>Now;}
    private sealed class Factory(Clock clock,bool enabled=true):WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment("Demo");b.UseSetting("ConnectionStrings:Default",Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING")??throw new InvalidOperationException("Isolated database required"));
            b.UseSetting("Demo:SeedProfile","LegacyRegression");b.UseSetting("Demo:SeedEnabled","true");b.UseSetting("Demo:StaffPassword","Demo-Only-123!");b.UseSetting("Demo:ReferenceDate","2026-09-28");
            b.UseSetting("StaffPasskeys:Enabled",enabled.ToString());b.UseSetting("StaffPasskeys:RpId","academy.test");b.UseSetting("StaffPasskeys:Origins:0","https://academy.test");
            b.ConfigureServices(s=>{s.RemoveAll<TimeProvider>();s.AddSingleton<TimeProvider>(clock);});
        }
    }
    private readonly Clock clock=new();private Factory factory=null!;
    public Task InitializeAsync(){factory=new(clock);_=factory.CreateClient();return Task.CompletedTask;}
    public Task DisposeAsync()=>factory.DisposeAsync().AsTask();
    private async Task<(HttpClient Client,Guid Member)> Staff(AcademyRole role=AcademyRole.Coach,bool narrow=false)
    {
        await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();var id=Guid.NewGuid();var u=new ApplicationUser{Id=Guid.NewGuid(),UserName=$"passkey-{id:N}",Email=$"passkey-{id:N}@example.test",DisplayName="موظف تحقق اصطناعي",SecurityStamp=Guid.NewGuid().ToString()};u.NormalizedEmail=u.Email.ToUpperInvariant();
        var m=new AcademyMembership{Id=id,AcademyId=DemoSeed.NogoomAcademyId,UserId=u.Id,Role=role,UseActionGrants=narrow,AllSports=!narrow};db.Users.Add(u);db.AcademyMemberships.Add(m);
        if(narrow){var sport=await db.Sports.Where(x=>x.AcademyId==m.AcademyId&&x.IsActive).Select(x=>x.Id).FirstAsync();db.MembershipSportAccesses.Add(new(){Id=Guid.NewGuid(),AcademyId=m.AcademyId,MembershipId=m.Id,SportId=sport});}
        await db.SaveChangesAsync();var c=factory.CreateClient();await MobileCoachTestSession.SignInAsync(c,factory.Services,u.Email,role);return(c,id);
    }
    private static Task<HttpResponseMessage> Post(HttpClient c,string path,object body)=>c.PostAsJsonAsync(StaffPasskeys.Path+path,body);
    private static async Task<JsonElement> Options(HttpClient c,string kind="register"){var r=await Post(c,$"/{kind}/options",new{name="وسيلة عرض اصطناعية"});Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());return await r.Content.ReadFromJsonAsync<JsonElement>();}
    private static async Task<JsonElement> Finish(HttpClient c,JsonElement o,object credential,string kind="register"){var r=await Post(c,$"/{kind}/complete",new{challengeId=o.GetProperty("challengeId").GetGuid(),credential});Assert.True(r.IsSuccessStatusCode,await r.Content.ReadAsStringAsync());return await r.Content.ReadFromJsonAsync<JsonElement>();}

    // Ephemeral standards-format authenticator for verifier tests, not production
    // code or a claim that physical Face ID/GPS has been tested. Browser E2E is separate.
    private sealed class Authenticator:IDisposable
    {
        private readonly ECDsa key=ECDsa.Create(ECCurve.NamedCurves.nistP256);private readonly byte[] id=RandomNumberGenerator.GetBytes(32);
        private static string B(byte[] b)=>Convert.ToBase64String(b).TrimEnd('=').Replace('+','-').Replace('/','_');
        private static byte[] Decode(string s)=>Convert.FromBase64String(s.Replace('-','+').Replace('_','/').PadRight((s.Length+3)/4*4,'='));
        private static byte[] Client(JsonElement o,string type,string origin,string? challenge)=>JsonSerializer.SerializeToUtf8Bytes(new{type,origin,challenge=challenge??o.GetProperty("options").GetProperty("challenge").GetString(),crossOrigin=false});
        private static byte[] Header(byte flags,uint count,string rp="academy.test"){var b=new byte[37];SHA256.HashData(Encoding.UTF8.GetBytes(rp)).CopyTo(b,0);b[32]=flags;System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(33),count);return b;}
        public object Register(JsonElement o,string origin="https://academy.test",bool uv=true)
        {
            var p=key.ExportParameters(false);var cose=new CborWriter();cose.WriteStartMap(5);cose.WriteInt32(1);cose.WriteInt32(2);cose.WriteInt32(3);cose.WriteInt32(-7);cose.WriteInt32(-1);cose.WriteInt32(1);cose.WriteInt32(-2);cose.WriteByteString(p.Q.X!);cose.WriteInt32(-3);cose.WriteByteString(p.Q.Y!);cose.WriteEndMap();
            var length=new byte[2];System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(length,(ushort)id.Length);var auth=Header((byte)(uv?0x45:0x41),0).Concat(new byte[16]).Concat(length).Concat(id).Concat(cose.Encode()).ToArray();
            var att=new CborWriter();att.WriteStartMap(3);att.WriteTextString("fmt");att.WriteTextString("none");att.WriteTextString("attStmt");att.WriteStartMap(0);att.WriteEndMap();att.WriteTextString("authData");att.WriteByteString(auth);att.WriteEndMap();
            return new{id=B(id),rawId=B(id),type="public-key",response=new{clientDataJSON=B(Client(o,"webauthn.create",origin,null)),attestationObject=B(att.Encode()),transports=new[]{"internal"}},extensions=new{}};
        }
        public object Verify(JsonElement o,uint count=1,bool uv=true,string origin="https://academy.test",string? challenge=null,bool badSignature=false,string rp="academy.test",string? userHandle=null)
        {
            var client=Client(o,"webauthn.get",origin,challenge);var auth=Header((byte)(uv?5:1),count,rp);var signature=key.SignData(auth.Concat(SHA256.HashData(client)).ToArray(),HashAlgorithmName.SHA256,DSASignatureFormat.Rfc3279DerSequence);if(badSignature)signature[^1]^=1;
            return new{id=B(id),rawId=B(id),type="public-key",response=new{clientDataJSON=B(client),authenticatorData=B(auth),signature=B(signature),userHandle=userHandle??(o.GetProperty("options").TryGetProperty("user",out var u)?u.GetProperty("id").GetString():null)},extensions=new{}};
        }
        public void Dispose()=>key.Dispose();
    }
    [Fact]
    public async Task Registration_signed_verification_replay_revocation_and_storage_guards_never_record_attendance()
    {
        var s=await Staff();using var c=s.Client;using var a=new Authenticator();var o=await Options(c);Assert.Equal("required",o.GetProperty("options").GetProperty("authenticatorSelection").GetProperty("userVerification").GetString());
        var registered=await Finish(c,o,a.Register(o));Assert.False(registered.GetProperty("attendanceRecorded").GetBoolean());var id=registered.GetProperty("credential").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/register/complete",new{challengeId=o.GetProperty("challengeId").GetGuid(),credential=a.Register(o)})).StatusCode);
        var verify=await Options(c,"verify");Assert.Equal("required",verify.GetProperty("options").GetProperty("userVerification").GetString());var proof=a.Verify(verify);var saved=await Finish(c,verify,proof,"verify");Assert.True(saved.GetProperty("verified").GetBoolean());Assert.False(saved.GetProperty("attendanceRecorded").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/verify/complete",new{challengeId=verify.GetProperty("challengeId").GetGuid(),credential=proof})).StatusCode);
        var list=await c.GetFromJsonAsync<JsonElement>(StaffPasskeys.Path+"/");Assert.Single(list.GetProperty("items").EnumerateArray());Assert.NotNull(list.GetProperty("items")[0].GetProperty("lastVerifiedAtUtc").GetString());Assert.DoesNotContain("publicKey",list.GetRawText());Assert.DoesNotContain("credentialHash",list.GetRawText());
        var pending=await Options(c,"verify");(await Post(c,$"/credentials/{id}/revoke",new{})).EnsureSuccessStatusCode();(await Post(c,$"/credentials/{id}/revoke",new{})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(c,"/verify/complete",new{challengeId=pending.GetProperty("challengeId").GetGuid(),credential=a.Verify(pending,2)})).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.False(await db.StaffDailyAttendances.AnyAsync(x=>x.MembershipId==s.Member));Assert.Equal(1,await db.StaffPasskeys.Where(k=>k.Id==id).Select(k=>k.SignCount).SingleAsync());
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StaffPasskeys\" SET \"IsActive\"=true,\"RevokedAtUtc\"=null WHERE \"Id\"={id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StaffPasskeyChallenges\" SET \"OptionsJson\"='{{}}' WHERE \"Id\"={o.GetProperty("challengeId").GetGuid()}"))).SqlState);
    }
    [Theory]
    [InlineData("origin")][InlineData("uv")][InlineData("challenge")][InlineData("signature")][InlineData("rp")][InlineData("handle")]
    public async Task Wrong_origin_uv_challenge_or_signature_is_consumed_without_acceptance(string defect)
    {
        var s=await Staff();using var c=s.Client;using var a=new Authenticator();var create=await Options(c);await Finish(c,create,a.Register(create));var o=await Options(c,"verify");
        var proof=a.Verify(o,uv:defect!="uv",origin:defect=="origin"?"https://attacker.test":"https://academy.test",challenge:defect=="challenge"?Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)):null,badSignature:defect=="signature",rp:defect=="rp"?"attacker.test":"academy.test",userHandle:defect=="handle"?Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)):null);var body=new{challengeId=o.GetProperty("challengeId").GetGuid(),credential=proof};Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(c,"/verify/complete",body)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/verify/complete",body)).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();Assert.Null(await db.StaffPasskeys.Where(k=>k.MembershipId==s.Member).Select(k=>k.LastVerifiedAtUtc).SingleAsync());
    }
    [Fact]
    public async Task Registration_requires_uv_and_a_client_boolean_is_not_device_proof()
    {
        var s=await Staff();using var c=s.Client;using var a=new Authenticator();var o=await Options(c);Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(c,"/register/complete",new{challengeId=o.GetProperty("challengeId").GetGuid(),credential=a.Register(o,uv:false)})).StatusCode);
        var next=await Options(c);Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(c,"/register/complete",new{challengeId=next.GetProperty("challengeId").GetGuid(),credential=new{biometricPassed=true}})).StatusCode);Assert.Empty((await c.GetFromJsonAsync<JsonElement>(StaffPasskeys.Path+"/")).GetProperty("items").EnumerateArray());
    }
    [Fact]
    public async Task Unchanged_signature_counter_and_disabled_configuration_do_not_mark_verified()
    {
        var s=await Staff();using var c=s.Client;using var a=new Authenticator();var create=await Options(c);await Finish(c,create,a.Register(create));var one=await Options(c,"verify");await Finish(c,one,a.Verify(one),"verify");var two=await Options(c,"verify");Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(c,"/verify/complete",new{challengeId=two.GetProperty("challengeId").GetGuid(),credential=a.Verify(two)})).StatusCode);
        await using var disabled=new Factory(clock,false);using var dc=disabled.CreateClient();dc.DefaultRequestHeaders.Authorization=c.DefaultRequestHeaders.Authorization;Assert.False((await dc.GetFromJsonAsync<JsonElement>(StaffPasskeys.Path+"/")).GetProperty("configured").GetBoolean());Assert.Equal(HttpStatusCode.Conflict,(await Post(dc,"/verify/options",new{})).StatusCode);
    }
    [Fact]
    public async Task Expired_wrong_purpose_cross_identity_and_parallel_challenges_fail_closed()
    {
        var s=await Staff();using var c=s.Client;var other=await Staff();using var oc=other.Client;using var a=new Authenticator();var create=await Options(c);await Finish(c,create,a.Register(create));var o=await Options(c,"verify");var proof=a.Verify(o);var body=new{challengeId=o.GetProperty("challengeId").GetGuid(),credential=proof};
        Assert.Equal(HttpStatusCode.NotFound,(await Post(oc,"/verify/complete",body)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/register/complete",body)).StatusCode);
        var expired=await Options(c,"verify");clock.Now=clock.Now.AddSeconds(121);Assert.Equal(HttpStatusCode.Conflict,(await Post(c,"/verify/complete",new{challengeId=expired.GetProperty("challengeId").GetGuid(),credential=a.Verify(expired)})).StatusCode);
        var fresh=await Options(c,"verify");var command=new{challengeId=fresh.GetProperty("challengeId").GetGuid(),credential=a.Verify(fresh)};var results=await Task.WhenAll(Post(c,"/verify/complete",command),Post(c,"/verify/complete",command));Assert.Single(results,r=>r.IsSuccessStatusCode);Assert.Single(results,r=>r.StatusCode==HttpStatusCode.Conflict);
    }
    [Fact]
    public async Task Narrow_admin_owns_keys_without_staff_crud_and_a_foreign_academy_or_guardian_cannot_use_them()
    {
        var s=await Staff(AcademyRole.AcademyAdmin,true);using var c=s.Client;using var a=new Authenticator();var o=await Options(c);var result=await Finish(c,o,a.Register(o));var id=result.GetProperty("credential").GetProperty("id").GetGuid();Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/v1/manage/staff/")).StatusCode);
        Assert.False(StaffPasskeys.IsOwnPath(StaffPasskeys.Path+"/credentials/revoke"));Assert.False(StaffPasskeys.IsOwnPath(StaffPasskeys.Path+"/credentials/not-a-guid/revoke"));Assert.True(StaffPasskeys.IsOwnPath(StaffPasskeys.Path+$"/credentials/{id}/revoke"));
        var g=await Staff(AcademyRole.Guardian);using var guardian=g.Client;Assert.Equal(HttpStatusCode.Forbidden,(await guardian.GetAsync(StaffPasskeys.Path+"/")).StatusCode);
        using var foreign=factory.CreateClient();var csrf=await foreign.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");using var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email=DemoSeed.FutureOwnerEmail,password="Demo-Only-123!"})};login.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());(await foreign.SendAsync(login)).EnsureSuccessStatusCode();
        Assert.Empty((await foreign.GetFromJsonAsync<JsonElement>(StaffPasskeys.Path+"/")).GetProperty("items").EnumerateArray());Assert.Equal(HttpStatusCode.BadRequest,(await Post(foreign,$"/credentials/{id}/revoke",new{})).StatusCode);
        var fc=await foreign.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");using var revoke=new HttpRequestMessage(HttpMethod.Post,StaffPasskeys.Path+$"/credentials/{id}/revoke"){Content=JsonContent.Create(new{})};revoke.Headers.Add("X-CSRF-TOKEN",fc.GetProperty("token").GetString());Assert.Equal(HttpStatusCode.NotFound,(await foreign.SendAsync(revoke)).StatusCode);
    }
}
