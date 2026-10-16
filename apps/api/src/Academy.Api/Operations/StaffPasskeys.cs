using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Academy.Api.Operations;

public static class StaffPasskeys
{
    public const string Path="/api/v1/staff/passkeys";
    public static bool IsOwnPath(string path)
    {
        var p=path.TrimEnd('/');
        return p is Path or Path+"/register/options" or Path+"/register/complete" or Path+"/verify/options" or Path+"/verify/complete"
            ||p.Length>Path.Length+20&&p.StartsWith(Path+"/credentials/",StringComparison.Ordinal)&&p.EndsWith("/revoke",StringComparison.Ordinal)&&Guid.TryParse(p[(Path.Length+13)..^7],out _);
    }
    public static void MapStaffPasskeys(this WebApplication app)
    {
        var g=app.MapGroup(Path).RequireAuthorization(AcademyPermissions.TenantAccess);
        g.MapGet("/",List);
        g.MapPost("/register/options",(PasskeyName b,HttpContext h,CurrentTenant t,FoundationDbContext d,TimeProvider c,IConfiguration cfg,IHostEnvironment e)=>Options("Register",b.Name,h,t,d,c,cfg,e)).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("staff-passkey");
        g.MapPost("/verify/options",(HttpContext h,CurrentTenant t,FoundationDbContext d,TimeProvider c,IConfiguration cfg,IHostEnvironment e)=>Options("Verify","اختبار وسيلة التحقق",h,t,d,c,cfg,e)).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("staff-passkey");
        g.MapPost("/register/complete",(PasskeyResponse b,HttpContext h,CurrentTenant t,FoundationDbContext d,TimeProvider c,IConfiguration cfg,IHostEnvironment e)=>Complete("Register",b,h,t,d,c,cfg,e)).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("staff-passkey");
        g.MapPost("/verify/complete",(PasskeyResponse b,HttpContext h,CurrentTenant t,FoundationDbContext d,TimeProvider c,IConfiguration cfg,IHostEnvironment e)=>Complete("Verify",b,h,t,d,c,cfg,e)).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("staff-passkey");
        g.MapPost("/credentials/{id:guid}/revoke",Revoke).AddEndpointFilter<CsrfFilter>();
    }
    private static Guid Actor(HttpContext h)=>Guid.Parse(h.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static bool Staff(TenantMembership t)=>t.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin or AcademyRole.Coach;
    private static Task Lock(FoundationDbContext db,string key)=>db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))");
    private static Task<bool> Live(FoundationDbContext db,TenantMembership t,Guid actor)=>db.AcademyMemberships.AnyAsync(m=>m.AcademyId==t.AcademyId&&m.Id==t.MembershipId&&m.UserId==actor&&m.IsActive&&m.User.IsActive&&m.Academy.IsActive&&m.Role==t.Role);
    private static IResult Invalid(string message)=>Results.UnprocessableEntity(new{message});
    private static IResult Conflict(string message)=>Results.Conflict(new{message});
    private static object View(StaffPasskey k)=>new{k.Id,k.Name,k.CreatedAtUtc,k.LastVerifiedAtUtc,k.IsActive,k.RevokedAtUtc,mayBeSynced=k.BackupEligible};
    private static Fido2Configuration? Configuration(IConfiguration cfg,IHostEnvironment env)
    {
        if(!cfg.GetValue<bool>("StaffPasskeys:Enabled"))return null;
        var rp=cfg["StaffPasskeys:RpId"];var origins=cfg.GetSection("StaffPasskeys:Origins").Get<string[]>()??[];
        if(string.IsNullOrWhiteSpace(rp)||rp.Length>253||Uri.CheckHostName(rp)!=UriHostNameType.Dns||origins.Length is <1 or >10)return null;
        foreach(var origin in origins)
        {
            if(!Uri.TryCreate(origin,UriKind.Absolute,out var uri)||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0||uri.AbsolutePath!="/"||
                uri.Scheme!="https"&&!(env.IsEnvironment("Demo")&&uri.Scheme=="http"&&uri.IsLoopback)||
                !(uri.Host==rp||uri.Host.EndsWith("."+rp,StringComparison.OrdinalIgnoreCase)))return null;
        }
        return new(){RPID=rp,RPName="أكاديميتي — تحقق الموظف",Origins=origins.ToHashSet(StringComparer.Ordinal),ChallengeSize=32,Timeout=120000};
    }
    private static bool SameConfig(StaffPasskeyChallenge c,Fido2Configuration cfg)=>c.RpId==cfg.RPID&&JsonSerializer.Deserialize<string[]>(c.OriginsJson)!.Order().SequenceEqual(cfg.Origins.Order());
    private static async Task<IResult> List(CurrentTenant tenant,FoundationDbContext db,IConfiguration cfg,IHostEnvironment env)
    {
        var t=(await tenant.ResolveAsync())!;if(!Staff(t))return Results.Forbid();
        return Results.Ok(new{configured=Configuration(cfg,env) is not null,items=(await db.StaffPasskeys.AsNoTracking().Where(k=>k.AcademyId==t.AcademyId&&k.MembershipId==t.MembershipId).OrderByDescending(k=>k.CreatedAtUtc).ToArrayAsync()).Select(View)});
    }
    private static async Task<IResult> Options(string purpose,string? name,HttpContext h,CurrentTenant tenant,FoundationDbContext db,TimeProvider clock,IConfiguration configuration,IHostEnvironment env)
    {
        var t=(await tenant.ResolveAsync())!;if(!Staff(t))return Results.Forbid();
        var cfg=Configuration(configuration,env);if(cfg is null)return Conflict("وسيلة التحقق الآمن غير مهيأة؛ استخدم طلب إثبات الحضور للمراجعة.");
        name=name?.Trim();if(name?.Length is not(>=2 and <=80))return Invalid("اكتب اسمًا واضحًا لوسيلة التحقق من حرفين إلى80حرفًا.");
        var actor=Actor(h);await using var tx=await db.Database.BeginTransactionAsync();await Lock(db,$"grants:{t.AcademyId}:{t.MembershipId}");await Lock(db,$"staff-passkeys:{t.AcademyId}:{t.MembershipId}");
        if(!await Live(db,t,actor))return Results.Forbid();
        var now=clock.GetUtcNow();
        if(await db.StaffPasskeyChallenges.CountAsync(x=>x.MembershipId==t.MembershipId&&x.ConsumedAtUtc==null&&x.ExpiresAtUtc>now)>=5)return Conflict("توجد محاولات تحقق مفتوحة؛ انتظر انتهاءها قبل محاولة جديدة.");
        var keys=await db.StaffPasskeys.AsNoTracking().Where(k=>k.MembershipId==t.MembershipId&&k.IsActive&&k.RpId==cfg.RPID).ToArrayAsync();
        if(purpose=="Register"&&keys.Length>=5)return Conflict("لديك خمس وسائل تحقق فعالة؛ أوقف غير المستخدمة أولًا.");
        if(purpose=="Verify"&&keys.Length==0)return Conflict("سجل وسيلة تحقق فعالة أولًا؛ لم يسجل أي حضور.");
        var fido=new Fido2(cfg);string json;
        if(purpose=="Register")
        {
            var display=await db.Users.Where(u=>u.Id==actor).Select(u=>u.DisplayName).SingleAsync();
            json=fido.RequestNewCredential(new(){User=new(){Id=t.MembershipId.ToByteArray(),Name=t.MembershipId.ToString(),DisplayName=display},ExcludeCredentials=keys.Select(k=>new PublicKeyCredentialDescriptor(k.CredentialId)).ToArray(),AuthenticatorSelection=new(){ResidentKey=ResidentKeyRequirement.Preferred,UserVerification=UserVerificationRequirement.Required},AttestationPreference=AttestationConveyancePreference.None}).ToJson();
        }
        else json=fido.GetAssertionOptions(new GetAssertionOptionsParams{AllowedCredentials=keys.Select(k=>new PublicKeyCredentialDescriptor(k.CredentialId)).ToArray(),UserVerification=UserVerificationRequirement.Required}).ToJson();
        var challenge=new StaffPasskeyChallenge{Id=Guid.NewGuid(),AcademyId=t.AcademyId,MembershipId=t.MembershipId,UserId=actor,Purpose=purpose,Name=name!,OptionsJson=json,RpId=cfg.RPID,OriginsJson=JsonSerializer.Serialize(cfg.Origins.Order()),ExpiresAtUtc=now.AddSeconds(120),CreatedAtUtc=now,UpdatedAtUtc=now};
        db.StaffPasskeyChallenges.Add(challenge);await db.SaveChangesAsync();await tx.CommitAsync();
        return Results.Ok(new{challengeId=challenge.Id,challenge.ExpiresAtUtc,purpose,options=JsonSerializer.Deserialize<JsonElement>(json)});
    }
    private static async Task<IResult> Complete(string purpose,PasskeyResponse body,HttpContext h,CurrentTenant tenant,FoundationDbContext db,TimeProvider clock,IConfiguration configuration,IHostEnvironment env)
    {
        var t=(await tenant.ResolveAsync())!;if(!Staff(t))return Results.Forbid();
        var cfg=Configuration(configuration,env);if(cfg is null)return Conflict("وسيلة التحقق الآمن غير مهيأة؛ لم يسجل أي حضور.");
        if(body.Credential.ValueKind!=JsonValueKind.Object||body.Credential.GetRawText().Length>24000)return Invalid("استجابة وسيلة التحقق غير صالحة.");
        var actor=Actor(h);await using var tx=await db.Database.BeginTransactionAsync();await Lock(db,$"grants:{t.AcademyId}:{t.MembershipId}");await Lock(db,$"staff-passkeys:{t.AcademyId}:{t.MembershipId}");
        if(!await Live(db,t,actor))return Results.Forbid();
        var challenge=await db.StaffPasskeyChallenges.SingleOrDefaultAsync(c=>c.Id==body.ChallengeId&&c.AcademyId==t.AcademyId&&c.MembershipId==t.MembershipId&&c.UserId==actor);
        if(challenge is null)return Results.NotFound();
        if(challenge.ConsumedAtUtc.HasValue)return Conflict("انتهت محاولة التحقق أو استُخدمت؛ حدّث الوسائل أو ابدأ محاولة جديدة.");
        var now=clock.GetUtcNow();challenge.ConsumedAtUtc=now;challenge.UpdatedAtUtc=now;
        if(challenge.Purpose!=purpose||challenge.ExpiresAtUtc<=now||!SameConfig(challenge,cfg)){await db.SaveChangesAsync();await tx.CommitAsync();return Conflict("انتهت المحاولة أو تغير سياق التحقق؛ ابدأ محاولة جديدة.");}
        if(purpose=="Register"&&await db.StaffPasskeys.CountAsync(k=>k.MembershipId==t.MembershipId&&k.IsActive)>=5){await db.SaveChangesAsync();await tx.CommitAsync();return Conflict("لديك خمس وسائل فعالة؛ أوقف غير المستخدمة أولًا.");}
        var fido=new Fido2(cfg);StaffPasskey? key=null;
        try
        {
            if(purpose=="Register")
            {
                var response=body.Credential.Deserialize<AuthenticatorAttestationRawResponse>()!;
                var credentialHash=Convert.ToHexString(SHA256.HashData(response.RawId));
                await Lock(db,$"staff-passkey-credential:{t.AcademyId}:{credentialHash}");
                var duplicate=await db.StaffPasskeys.IgnoreQueryFilters().AnyAsync(k=>k.AcademyId==t.AcademyId&&k.CredentialHash==credentialHash);
                var result=await fido.MakeNewCredentialAsync(new(){AttestationResponse=response,OriginalOptions=CredentialCreateOptions.FromJson(challenge.OptionsJson),IsCredentialIdUniqueToUserCallback=(args,ct)=>Task.FromResult(!duplicate&&args.CredentialId.SequenceEqual(response.RawId))});
                if(result.Id.Length>1024||result.PublicKey.Length>4096)throw new FormatException();
                key=new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,MembershipId=t.MembershipId,UserId=actor,Name=challenge.Name,CredentialHash=Convert.ToHexString(SHA256.HashData(result.Id)),CredentialId=result.Id,PublicKey=result.PublicKey,UserHandle=t.MembershipId.ToByteArray(),SignCount=result.SignCount,BackupEligible=result.IsBackupEligible,BackedUp=result.IsBackedUp,RpId=cfg.RPID,RegistrationChallengeId=challenge.Id,CreatedAtUtc=now,UpdatedAtUtc=now};
                db.StaffPasskeys.Add(key);
            }
            else
            {
                var response=body.Credential.Deserialize<AuthenticatorAssertionRawResponse>()!;
                var hash=Convert.ToHexString(SHA256.HashData(response.RawId));
                key=await db.StaffPasskeys.SingleOrDefaultAsync(k=>k.AcademyId==t.AcademyId&&k.MembershipId==t.MembershipId&&k.UserId==actor&&k.CredentialHash==hash&&k.IsActive&&k.RpId==cfg.RPID);
                if(key is null)throw new FormatException();
                var result=await fido.MakeAssertionAsync(new(){AssertionResponse=response,OriginalOptions=AssertionOptions.FromJson(challenge.OptionsJson),StoredPublicKey=key.PublicKey,StoredSignatureCounter=checked((uint)key.SignCount),IsUserHandleOwnerOfCredentialIdCallback=(args,ct)=>Task.FromResult(args.CredentialId.SequenceEqual(key.CredentialId)&&args.UserHandle.SequenceEqual(key.UserHandle))});
                key.SignCount=result.SignCount;key.BackedUp=result.IsBackedUp;key.LastVerifiedAtUtc=now;key.UpdatedAtUtc=now;
            }
        }
        catch(Exception e)when(e is Fido2VerificationException or JsonException or FormatException or ArgumentException or NullReferenceException or IndexOutOfRangeException or CryptographicException or OverflowException)
        {
            await db.SaveChangesAsync();await tx.CommitAsync();return Invalid("تعذر إثبات وسيلة التحقق؛ انتهت المحاولة دون تسجيل حضور. حاول مجددًا أو اطلب مراجعة الإدارة.");
        }
        challenge.VerifiedAtUtc=now;
        try{await db.SaveChangesAsync();await tx.CommitAsync();}
        catch(DbUpdateException e)when(e.InnerException is PostgresException{SqlState:"23505"}){return Conflict("وسيلة التحقق مسجلة من قبل؛ حدّث القائمة.");}
        return Results.Ok(new{verified=true,purpose,credential=View(key!),attendanceRecorded=false});
    }
    private static async Task<IResult> Revoke(Guid id,HttpContext h,CurrentTenant tenant,FoundationDbContext db,TimeProvider clock)
    {
        var t=(await tenant.ResolveAsync())!;if(!Staff(t))return Results.Forbid();var actor=Actor(h);
        await using var tx=await db.Database.BeginTransactionAsync();await Lock(db,$"grants:{t.AcademyId}:{t.MembershipId}");await Lock(db,$"staff-passkeys:{t.AcademyId}:{t.MembershipId}");
        if(!await Live(db,t,actor))return Results.Forbid();
        var key=await db.StaffPasskeys.SingleOrDefaultAsync(k=>k.Id==id&&k.AcademyId==t.AcademyId&&k.MembershipId==t.MembershipId&&k.UserId==actor);if(key is null)return Results.NotFound();
        if(key.IsActive){key.IsActive=false;key.RevokedAtUtc=clock.GetUtcNow();key.UpdatedAtUtc=key.RevokedAtUtc.Value;await db.SaveChangesAsync();}
        await tx.CommitAsync();return Results.Ok(View(key));
    }
}
public sealed record PasskeyName(string? Name);
public sealed record PasskeyResponse(Guid ChallengeId,JsonElement Credential);
