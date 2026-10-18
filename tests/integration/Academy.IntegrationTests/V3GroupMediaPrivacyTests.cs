using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    private static readonly byte[] MediaPng=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
    internal static async Task<HttpResponseMessage> PrivacyUpload(HttpClient c,Guid group,DateOnly day,Guid? subject=null,bool noChildren=false,bool confirmed=true,Guid? session=null,byte[]? payload=null,string mime="image/png")
    {
        var csrf=await c.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");using var form=new MultipartFormDataContent();var file=new ByteArrayContent(payload??MediaPng);file.Headers.ContentType=new MediaTypeHeaderValue(mime);form.Add(file,"file","synthetic-media");
        foreach(var (k,v) in new Dictionary<string,string>{{"purpose","Media"},{"groupId",group.ToString()},{"eventOn",day.ToString("yyyy-MM-dd")},{"privacyConfirmed",confirmed?"true":"false"},{"noRecognizableChildren",noChildren?"true":"false"},{"subjectPlayerIds",subject?.ToString()??""}})form.Add(new StringContent(v),k);
        if(session.HasValue)form.Add(new StringContent(session.ToString()!),"sessionId");
        using var r=new HttpRequestMessage(HttpMethod.Post,"/api/v1/assets/upload"){Content=form};r.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());return await c.SendAsync(r);
    }
    internal static async Task<Guid> MediaCreated(HttpResponseMessage r){Assert.True(r.StatusCode==HttpStatusCode.Created,await r.Content.ReadAsStringAsync());return(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();}
    private static async Task<Academy.Infrastructure.People.SportEnrollment> FreshMediaChild(FoundationDbContext db,Academy.Infrastructure.People.SportEnrollment source,Guid guardian)
    {
        var e=await FreshSkillChild(db,source,guardian);e.CreatedAtUtc=DateTimeOffset.UtcNow;e.UpdatedAtUtc=e.CreatedAtUtc;await db.SaveChangesAsync();return e;
    }
    private async Task<(HttpClient Client,Guid Guardian)> ExclusiveMediaFamily()
    {
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var now=DateTimeOffset.UtcNow;
        var u=new Academy.Infrastructure.Identity.ApplicationUser{Id=Guid.NewGuid(),DisplayName="ولي أمر خصوصية اصطناعي",UserName="media-test-"+Guid.NewGuid().ToString("N"),SecurityStamp=Guid.NewGuid().ToString("N"),IsActive=true};
        var g=new Academy.Infrastructure.People.GuardianProfile{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,UserId=u.Id,DisplayName=u.DisplayName};var m=new AcademyMembership{Id=Guid.NewGuid(),AcademyId=g.AcademyId,UserId=u.Id,Role=AcademyRole.Guardian,AllBranches=true};
        db.Users.Add(u);db.GuardianProfiles.Add(g);db.AcademyMemberships.Add(m);var token=Academy.Api.Mobile.MobileAuthenticationHandler.NewToken();db.MobileSessions.Add(new(){Id=Guid.NewGuid(),UserId=u.Id,MembershipId=m.Id,DeviceName="اختبار خصوصية اصطناعي",AccessTokenHash=Academy.Api.Mobile.MobileAuthenticationHandler.Hash(token),RefreshTokenHash=Academy.Api.Mobile.MobileAuthenticationHandler.Hash(Guid.NewGuid().ToString()),SecurityStamp=u.SecurityStamp,CreatedAtUtc=now,LastUsedAtUtc=now,AccessExpiresAtUtc=now.AddMinutes(10),ExpiresAtUtc=now.AddHours(1)});await db.SaveChangesAsync();var c=factory.CreateClient();c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);return(c,g.Id);
    }
    [Fact] public async Task Group_media_privacy_authenticated_choice_snapshot_withdrawal_and_regrant_do_not_restore_old_files_or_change_money()
    {
        using var owner=await Staff();using var coach=await Mobile(AcademyRole.Coach);var f=await ExclusiveMediaFamily();using var family=f.Client;using var foreign=await Staff(DemoSeed.FutureOwnerEmail);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var opts=await coach.GetFromJsonAsync<JsonElement>("/api/v1/evaluations/options");var source=await db.SportEnrollments.AsNoTracking().SingleAsync(e=>e.Id==opts.GetProperty("enrollments")[0].GetProperty("id").GetGuid());var guardian=f.Guardian;var e=await FreshMediaChild(db,source,guardian);var group=e.TrainingGroupId!.Value;var path=$"/api/v1/guardian/children/{e.PlayerId}/group-media-consent";
        var options=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/assets/media-audience?groupId={group}");var day=DateOnly.Parse(options.GetProperty("asOfDate").GetString()!);Assert.False(options.GetProperty("players").EnumerateArray().Single(p=>p.GetProperty("id").GetGuid()==e.PlayerId).GetProperty("consented").GetBoolean());var money=await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28");var count=await db.PrivateAssets.CountAsync();
        var yesterday=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/assets/media-audience?groupId={group}&eventOn={day.AddDays(-1):yyyy-MM-dd}");Assert.DoesNotContain(yesterday.GetProperty("players").EnumerateArray(),p=>p.GetProperty("id").GetGuid()==e.PlayerId);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await PrivacyUpload(owner,group,day,e.PlayerId)).StatusCode);Assert.Equal(count,await db.PrivateAssets.CountAsync());var b=new GroupMediaConsentWrite(true,0,Guid.NewGuid());Assert.Equal(HttpStatusCode.Forbidden,(await Send(owner,path,b)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await Send(coach,path,b)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await family.GetPrivateAssetAsync(factory,$"/api/v1/guardian/children/{Guid.NewGuid()}/group-media-consent")).StatusCode);
        using var cookieFamily=factory.CreateClient();var otp=await Send(cookieFamily,"/api/v1/auth/guardian/otp/request",new{phoneNumber=DemoSeed.GuardianPhone});otp.EnsureSuccessStatusCode();var challenge=(await otp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetGuid();(await Send(cookieFamily,"/api/v1/auth/guardian/otp/verify",new{phoneNumber=DemoSeed.GuardianPhone,challengeId=challenge,code="246810"})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.BadRequest,(await cookieFamily.PostAsJsonAsync(path,b)).StatusCode);
        var consent=await Send(family,path,b);Assert.Equal(HttpStatusCode.OK,consent.StatusCode);Assert.Equal(HttpStatusCode.OK,(await Send(family,path,b)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await Send(family,path,b with{Allowed=false})).StatusCode);var id=await MediaCreated(await PrivacyUpload(coach,group,day,e.PlayerId));var url=$"/api/v1/assets/{id}";
var file=await family.GetPrivateAssetAsync(factory,url);Assert.Equal(HttpStatusCode.OK,file.StatusCode);await AssertMediaPixel(await file.Content.ReadAsByteArrayAsync());Assert.Contains("no-store",file.Headers.CacheControl!.ToString());Assert.Contains(id.ToString(),await family.GetStringAsync($"/api/v1/assets/gallery?playerId={e.PlayerId}"));Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetPrivateAssetAsync(factory,url)).StatusCode);
        var later=await FreshMediaChild(db,source,guardian);Assert.DoesNotContain(id.ToString(),await family.GetStringAsync($"/api/v1/assets/gallery?playerId={later.PlayerId}"));later.IsActive=false;await db.SaveChangesAsync();
        (await Send(family,path,new GroupMediaConsentWrite(false,1,Guid.NewGuid()))).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await family.GetPrivateAssetAsync(factory,url)).StatusCode);Assert.DoesNotContain(id.ToString(),await family.GetStringAsync($"/api/v1/assets/gallery?playerId={e.PlayerId}"));Assert.Equal(HttpStatusCode.OK,(await owner.GetPrivateAssetAsync(factory,url)).StatusCode);
        (await Send(family,path,b)).EnsureSuccessStatusCode();Assert.False((await family.GetFromJsonAsync<JsonElement>(path)).GetProperty("allowed").GetBoolean());(await Send(family,path,new GroupMediaConsentWrite(true,2,Guid.NewGuid()))).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await family.GetPrivateAssetAsync(factory,url)).StatusCode);var fresh=await MediaCreated(await PrivacyUpload(owner,group,day,e.PlayerId));Assert.Equal(HttpStatusCode.OK,(await family.GetPrivateAssetAsync(factory,$"/api/v1/assets/{fresh}")).StatusCode);
        var enrollment=await db.SportEnrollments.SingleAsync(x=>x.Id==e.Id);enrollment.IsActive=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.NotFound,(await family.GetPrivateAssetAsync(factory,$"/api/v1/assets/{fresh}")).StatusCode);enrollment.IsActive=true;await db.SaveChangesAsync();
        var current=await db.GroupMediaConsents.OrderByDescending(c=>c.Revision).FirstAsync(c=>c.PlayerId==e.PlayerId);current.Allowed=false;await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();var sql=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PrivateMediaConsentBasiss\" WHERE \"AssetId\"={fresh}"));Assert.Equal("23514",sql.SqlState);
        Assert.Equal(money,await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));var link=await db.GuardianPlayerLinks.SingleAsync(l=>l.PlayerId==e.PlayerId);link.IsActive=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.NotFound,(await Send(family,path,b)).StatusCode);
    }
    [Fact] public async Task Group_media_privacy_requires_review_and_actual_day_and_does_not_treat_unknown_legacy_audience_as_consented()
    {
        using var owner=await Staff();using var family=await Mobile(AcademyRole.Guardian);using var coach=await Mobile(AcademyRole.Coach);var opts=await coach.GetFromJsonAsync<JsonElement>("/api/v1/evaluations/options");var group=opts.GetProperty("enrollments")[0].GetProperty("groupId").GetGuid();var data=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/assets/media-audience?groupId={group}");var day=DateOnly.Parse(data.GetProperty("asOfDate").GetString()!);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await PrivacyUpload(owner,group,day,noChildren:true,confirmed:false)).StatusCode);Assert.Equal(HttpStatusCode.UnprocessableEntity,(await PrivacyUpload(owner,group,day.AddDays(1),noChildren:true)).StatusCode);Assert.Equal(HttpStatusCode.UnprocessableEntity,(await PrivacyUpload(owner,group,day)).StatusCode);Assert.Equal(HttpStatusCode.UnprocessableEntity,(await PrivacyUpload(owner,group,day,Guid.NewGuid(),true)).StatusCode);await MediaCreated(await PrivacyUpload(coach,group,day,noChildren:true));Assert.Equal(HttpStatusCode.NotFound,(await coach.GetPrivateAssetAsync(factory,$"/api/v1/assets/media-audience?groupId={Guid.NewGuid()}")).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var player=await db.GuardianPlayerLinks.Where(l=>l.Guardian.User.PhoneNumber==DemoSeed.GuardianPhone&&l.IsActive&&l.Player.IsActive&&l.Guardian.IsActive).Select(l=>l.PlayerId).FirstAsync();var old=new PrivateAsset{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,TrainingGroupId=group,Purpose="Media",ContentType="image/png",StorageName="legacy-synthetic-no-file",Length=100,UploadedByUserId=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync()};db.PrivateAssets.Add(old);db.PrivateAssetAudiences.Add(new(){Id=Guid.NewGuid(),AcademyId=old.AcademyId,AssetId=old.Id,PlayerId=player});await db.SaveChangesAsync();Assert.DoesNotContain(old.Id.ToString(),await family.GetStringAsync($"/api/v1/assets/gallery?playerId={player}"));
    }
    [Fact] public async Task Group_media_privacy_all_linked_guardians_must_consent_and_removing_a_withdrawing_guardian_never_revives_the_old_publication()
    {
        using var owner=await Staff();var a=await ExclusiveMediaFamily();var b=await ExclusiveMediaFamily();using var first=a.Client;using var second=b.Client;await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var source=await db.SportEnrollments.AsNoTracking().FirstAsync(e=>e.AcademyId==DemoSeed.NogoomAcademyId&&e.TrainingGroupId!=null&&e.IsActive);var e=await FreshMediaChild(db,source,a.Guardian);var other=new Academy.Infrastructure.People.GuardianPlayerLink{Id=Guid.NewGuid(),AcademyId=e.AcademyId,PlayerId=e.PlayerId,GuardianId=b.Guardian};db.GuardianPlayerLinks.Add(other);await db.SaveChangesAsync();var group=e.TrainingGroupId!.Value;var day=DateOnly.Parse((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/assets/media-audience?groupId={group}")).GetProperty("asOfDate").GetString()!);var path=$"/api/v1/guardian/children/{e.PlayerId}/group-media-consent";
        (await Send(first,path,new GroupMediaConsentWrite(true,0,Guid.NewGuid()))).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.UnprocessableEntity,(await PrivacyUpload(owner,group,day,e.PlayerId)).StatusCode);(await Send(second,path,new GroupMediaConsentWrite(true,0,Guid.NewGuid()))).EnsureSuccessStatusCode();var id=await MediaCreated(await PrivacyUpload(owner,group,day,e.PlayerId));Assert.Equal(HttpStatusCode.OK,(await first.GetPrivateAssetAsync(factory,$"/api/v1/assets/{id}")).StatusCode);
        (await Send(second,path,new GroupMediaConsentWrite(false,1,Guid.NewGuid()))).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await first.GetPrivateAssetAsync(factory,$"/api/v1/assets/{id}")).StatusCode);other.IsActive=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.NotFound,(await first.GetPrivateAssetAsync(factory,$"/api/v1/assets/{id}")).StatusCode);var newCopy=await MediaCreated(await PrivacyUpload(owner,group,day,e.PlayerId));Assert.Equal(HttpStatusCode.OK,(await first.GetPrivateAssetAsync(factory,$"/api/v1/assets/{newCopy}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await second.GetPrivateAssetAsync(factory,$"/api/v1/assets/{newCopy}")).StatusCode);
    }
    [Fact] public async Task Group_media_privacy_scoped_admin_add_read_and_branch_revocation_apply_even_to_the_original_uploader()=>await DelegateFixture(async(owner,admin,db,group,member)=>{
        var m=await db.AcademyMemberships.SingleAsync(m=>m.Id==member);m.AllSports=true;var grant=await db.StaffActionGrants.SingleOrDefaultAsync(g=>g.MembershipId==member&&g.Module=="media");var before=grant==null?null:JsonSerializer.Serialize(grant);var added=grant==null;grant??=new(){Id=Guid.NewGuid(),AcademyId=group.AcademyId,MembershipId=member,Module="media"};if(added)db.StaffActionGrants.Add(grant);grant.IsActive=true;grant.Read=true;grant.Add=false;grant.Edit=true;await db.SaveChangesAsync();
        var f=await ExclusiveMediaFamily();using var family=f.Client;var e=await FreshMediaChild(db,new(){AcademyId=group.AcademyId,SportId=group.SportId,BranchId=group.BranchId,TrainingGroupId=group.Id},f.Guardian);var options=await admin.GetFromJsonAsync<JsonElement>($"/api/v1/assets/media-audience?groupId={group.Id}");var day=DateOnly.Parse(options.GetProperty("asOfDate").GetString()!);(await Send(family,$"/api/v1/guardian/children/{e.PlayerId}/group-media-consent",new GroupMediaConsentWrite(true,0,Guid.NewGuid()))).EnsureSuccessStatusCode();
        try{
            Assert.Equal(HttpStatusCode.Forbidden,(await PrivacyUpload(admin,group.Id,day,e.PlayerId)).StatusCode);
            grant.Add=true;await db.SaveChangesAsync();
            var id=await MediaCreated(await PrivacyUpload(admin,group.Id,day,e.PlayerId));
            Assert.Equal(HttpStatusCode.OK,(await admin.GetPrivateAssetAsync(factory,$"/api/v1/assets/{id}")).StatusCode);
            grant.Edit=false;await db.SaveChangesAsync();
            var withdraw=new MediaWithdrawalWrite(Guid.NewGuid(),null);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,$"/api/v1/assets/{id}/withdraw",withdraw)).StatusCode);
            var readOnly=await admin.GetFromJsonAsync<JsonElement>($"/api/v1/assets/gallery?groupId={group.Id}&includeWithdrawn=true");Assert.False(readOnly.EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==id).GetProperty("canWithdraw").GetBoolean());
            grant.Edit=true;grant.Add=false;await db.SaveChangesAsync();
            (await Send(admin,$"/api/v1/assets/{id}/withdraw",withdraw)).EnsureSuccessStatusCode();
            var editOnly=await admin.GetFromJsonAsync<JsonElement>($"/api/v1/assets/gallery?groupId={group.Id}&includeWithdrawn=true");var editRow=editOnly.EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==id);Assert.True(editRow.GetProperty("canWithdraw").GetBoolean());Assert.False(editRow.GetProperty("canPublish").GetBoolean());
            var branch=await db.MembershipBranchAccesses.SingleAsync(b=>b.MembershipId==member&&b.BranchId==group.BranchId);
            branch.IsActive=false;await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.NotFound,(await admin.GetPrivateAssetAsync(factory,$"/api/v1/assets/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await admin.GetPrivateAssetAsync(factory,$"/api/v1/assets/media-audience?groupId={group.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await Send(admin,$"/api/v1/assets/{id}/withdraw",withdraw)).StatusCode);
            branch.IsActive=true;grant.Read=false;await db.SaveChangesAsync();
            // Explicit module permission denial is 403 at middleware; a foreign branch resource remains hidden with 404.
            Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetPrivateAssetAsync(factory,$"/api/v1/assets/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,$"/api/v1/assets/{id}/withdraw",withdraw)).StatusCode);
        }
        finally{if(added)db.StaffActionGrants.Remove(grant);else db.Entry(grant).CurrentValues.SetValues(JsonSerializer.Deserialize<StaffActionGrant>(before!)!);await db.SaveChangesAsync();}
    });
}
