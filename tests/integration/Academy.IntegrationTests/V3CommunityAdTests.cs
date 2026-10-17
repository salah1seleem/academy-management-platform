using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    private async Task<HttpClient> AdGuardian(bool linked=true)
    {
        // Independent synthetic account per case: never erase measured events or relax the daily cap.
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var user=Guid.NewGuid();var member=Guid.NewGuid();var guardian=Guid.NewGuid();var now=DateTimeOffset.UtcNow;var stamp=Guid.NewGuid().ToString("N");
        db.Users.Add(new(){Id=user,DisplayName="ولي أمر إعلان اصطناعي",UserName="ads-qa-"+user.ToString("N"),NormalizedUserName="ADS-QA-"+user.ToString("N"),SecurityStamp=stamp,IsActive=true,CreatedAtUtc=now,UpdatedAtUtc=now});
        db.AcademyMemberships.Add(new(){Id=member,AcademyId=DemoSeed.NogoomAcademyId,UserId=user,Role=AcademyRole.Guardian,CreatedAtUtc=now,UpdatedAtUtc=now});
        db.GuardianProfiles.Add(new(){Id=guardian,AcademyId=DemoSeed.NogoomAcademyId,UserId=user,DisplayName="ولي أمر إعلان اصطناعي",CreatedAtUtc=now,UpdatedAtUtc=now});if(linked)db.GuardianPlayerLinks.Add(new(){Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,GuardianId=guardian,PlayerId=Academy.Api.Slice2.Slice2DemoSeed.OmarPlayerId,CreatedAtUtc=now,UpdatedAtUtc=now});
        var token=Academy.Api.Mobile.MobileAuthenticationHandler.NewToken();db.MobileSessions.Add(new(){Id=Guid.NewGuid(),UserId=user,MembershipId=member,AccessTokenHash=Academy.Api.Mobile.MobileAuthenticationHandler.Hash(token),RefreshTokenHash=Academy.Api.Mobile.MobileAuthenticationHandler.Hash(Academy.Api.Mobile.MobileAuthenticationHandler.NewToken()),SecurityStamp=stamp,DeviceName="Isolated sponsor API case",CreatedAtUtc=now,LastUsedAtUtc=now,AccessExpiresAtUtc=now.AddMinutes(15),ExpiresAtUtc=now.AddHours(1)});await db.SaveChangesAsync();var client=factory.CreateClient();client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);return client;
    }
    private async Task<AcademyRuntimeSettings> AdSettings(HttpClient owner,bool enabled)
    {
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var active=await db.AcademyConfigurationVersions.AsNoTracking().SingleAsync(x=>x.AcademyId==DemoSeed.NogoomAcademyId&&x.State==ConfigurationState.Active);var original=AcademyRuntimeSettings.Read(active.Payload);var updated=original with{Features=new(original.Features),TenantMarketingOptIn=enabled};updated.Features["community_ads"]=enabled;
        var license=await db.AcademyFeatureEntitlements.SingleAsync(x=>x.AcademyId==DemoSeed.NogoomAcademyId&&x.Feature=="community_ads");license.Enabled=true;license.PlanCode="Premium";await db.SaveChangesAsync();
        await AdActivate(owner,updated);return original;
    }
    private async Task AdActivate(HttpClient owner,AcademyRuntimeSettings settings)
    {
        var draft=await Send(owner,"/api/v1/manage/configuration/drafts",settings);draft.EnsureSuccessStatusCode();var id=(await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        foreach(var target in new[]{"Validated","Staged","Active"}){await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var version=await db.AcademyConfigurationVersions.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync();var changed=await Send(owner,$"/api/v1/manage/configuration/{id}/transition",new{target,version});changed.EnsureSuccessStatusCode();}
    }
    private async Task AdRestore(HttpClient owner,AcademyRuntimeSettings original)
    {
        await AdActivate(owner,original);await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var l=await db.AcademyFeatureEntitlements.SingleAsync(x=>x.AcademyId==DemoSeed.NogoomAcademyId&&x.Feature=="community_ads");l.Enabled=false;l.PlanCode="LegacyDemo";await db.SaveChangesAsync();
    }
    private static async Task<JsonElement> AdFeed(HttpClient guardian)=>(await guardian.GetFromJsonAsync<JsonElement>("/api/v1/guardian/community-ads/feed")).GetProperty("item");
    private async Task<Guid> AdBranch(){await using var scope=factory.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().SportEnrollments.Where(e=>e.AcademyId==DemoSeed.NogoomAcademyId&&e.PlayerId==Academy.Api.Slice2.Slice2DemoSeed.OmarPlayerId).Select(e=>e.BranchId).FirstAsync();}
    private static CommunityCampaignWrite AdDraft(Guid branch)=>new("حملة اختبار اصطناعية","مركز برمجة افتراضي","ورشة برمجة لأولياء الأمور","تفاصيل اصطناعية دون شراء أو إرسال أو بيانات أطفال.","تعرف على التفاصيل",null,DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(7),5,[branch],Guid.NewGuid());
    private async Task<uint> AdVersion(Guid id){await using var scope=factory.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().CommunityCampaignRevisions.Where(r=>r.Id==id).Select(r=>r.Version).SingleAsync();}
    private async Task<HttpResponseMessage> AdReview(HttpClient owner,Guid id,string action,Guid? branch=null)=>await Send(owner,$"/api/v1/manage/community-campaigns/{id}/review",new CommunityCampaignReviewWrite(action,await AdVersion(id),Guid.NewGuid(),branch));
    private async Task<(Guid Campaign,Guid Revision)> AdCreate(HttpClient owner,Guid branch){var r=await Send(owner,"/api/v1/manage/community-campaigns/",AdDraft(branch));r.EnsureSuccessStatusCode();var d=await r.Content.ReadFromJsonAsync<JsonElement>();return(d.GetProperty("id").GetGuid(),d.GetProperty("revisionId").GetGuid());}
    private async Task AdStop(HttpClient owner,Guid campaign){await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var current=await db.CommunityCampaigns.Where(c=>c.Id==campaign).Select(c=>c.CurrentRevision).SingleAsync();var r=await db.CommunityCampaignRevisions.Where(r=>r.CampaignId==campaign&&r.Revision==current).Select(r=>new{r.Id,r.State}).SingleAsync();if(r.State=="Approved")(await AdReview(owner,r.Id,"Pause")).EnsureSuccessStatusCode();}
    [Fact] public async Task Community_campaign_requires_every_consent_and_branch_approval_and_new_creative_loses_old_approval()
    {
        using var owner=await Staff();using var guardian=await AdGuardian();using var admin=await Staff(DemoSeed.AdminEmail);using var coach=await Mobile(AcademyRole.Coach);using var other=await Staff(DemoSeed.FutureOwnerEmail);var branch=await AdBranch();
        Assert.Equal(JsonValueKind.Null,(await AdFeed(guardian)).ValueKind);Assert.Equal(HttpStatusCode.Forbidden,(await Send(owner,"/api/v1/manage/community-campaigns/",AdDraft(branch))).StatusCode);
        var original=await AdSettings(owner,true);var created=await AdCreate(owner,branch);
        try
        {
            foreach(var c in new[]{admin,coach,guardian})Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/v1/manage/community-campaigns/")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await Send(other,$"/api/v1/manage/community-campaigns/{created.Revision}/review",new CommunityCampaignReviewWrite("Pause",0,Guid.NewGuid()))).StatusCode);
            Assert.Equal(JsonValueKind.Null,(await AdFeed(guardian)).ValueKind);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(guardian,"/api/v1/guardian/community-ads/preference",new CommunityAdPreferenceWrite(true,false),true)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,(await Send(guardian,"/api/v1/guardian/community-ads/preference",new CommunityAdPreferenceWrite(true,true),true)).StatusCode);
            (await AdReview(owner,created.Revision,"Submit")).EnsureSuccessStatusCode();Assert.Equal(JsonValueKind.Null,(await AdFeed(guardian)).ValueKind);
            (await AdReview(owner,created.Revision,"Approve",branch)).EnsureSuccessStatusCode();Assert.Equal(created.Revision,(await AdFeed(guardian)).GetProperty("id").GetGuid());Assert.Equal((await guardian.GetFromJsonAsync<JsonElement>("/api/v1/guardian/home")).GetProperty("today").GetString(),(await AdFeed(guardian)).GetProperty("subscriptionAsOf").GetString());
            (await AdReview(owner,created.Revision,"Pause")).EnsureSuccessStatusCode();Assert.Equal(JsonValueKind.Null,(await AdFeed(guardian)).ValueKind);Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync($"/api/v1/guardian/community-ads/{created.Revision}")).StatusCode);
            (await AdReview(owner,created.Revision,"Resume")).EnsureSuccessStatusCode();Assert.Equal(created.Revision,(await AdFeed(guardian)).GetProperty("id").GetGuid());
            (await AdReview(owner,created.Revision,"Revoke",branch)).EnsureSuccessStatusCode();Assert.Equal(JsonValueKind.Null,(await AdFeed(guardian)).ValueKind);
            await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var version=await db.CommunityCampaigns.Where(c=>c.Id==created.Campaign).Select(c=>c.Version).SingleAsync();
            var edit=await Send(owner,"/api/v1/manage/community-campaigns/",AdDraft(branch) with{CampaignId=created.Campaign,Version=version,Title="تصميم معدل يحتاج مراجعة"});edit.EnsureSuccessStatusCode();Assert.Equal(JsonValueKind.Null,(await AdFeed(guardian)).ValueKind);Assert.Equal(2,await db.CommunityCampaignRevisions.CountAsync(r=>r.CampaignId==created.Campaign));
            Assert.Equal(HttpStatusCode.Conflict,(await AdReview(owner,created.Revision,"Approve",branch)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync($"/api/v1/guardian/community-ads/{created.Revision}")).StatusCode);
            var p=await db.CommunityCampaignRevisions.SingleAsync(r=>r.Id==created.Revision);p.Title="لا تعديل للتاريخ";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
        }
        finally{await AdStop(owner,created.Campaign);await Send(guardian,"/api/v1/guardian/community-ads/preference",new CommunityAdPreferenceWrite(false,false),true);await AdRestore(owner,original);}
    }
    [Fact] public async Task Community_measurements_are_not_get_requests_are_deduplicated_capped_private_and_revocable()
    {
        using var owner=await Staff();using var guardian=await AdGuardian();var branch=await AdBranch();var original=await AdSettings(owner,true);var created=await AdCreate(owner,branch);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var collections=await db.Collections.CountAsync();var periods=await db.SubscriptionPeriods.CountAsync();
        try
        {
            (await AdReview(owner,created.Revision,"Submit")).EnsureSuccessStatusCode();(await AdReview(owner,created.Revision,"Approve",branch)).EnsureSuccessStatusCode();(await Send(guardian,"/api/v1/guardian/community-ads/preference",new CommunityAdPreferenceWrite(true,true),true)).EnsureSuccessStatusCode();
            await AdFeed(guardian);await AdFeed(guardian);Assert.Equal(0,await db.CommunityAdEvents.CountAsync(e=>e.RevisionId==created.Revision));
            var responses=await Task.WhenAll(Send(guardian,$"/api/v1/guardian/community-ads/{created.Revision}/events",new CommunityAdEventWrite("Impression")),Send(guardian,$"/api/v1/guardian/community-ads/{created.Revision}/events",new CommunityAdEventWrite("Impression")));Assert.All(responses,r=>Assert.Equal(HttpStatusCode.NoContent,r.StatusCode));Assert.Equal(1,await db.CommunityAdEvents.CountAsync(e=>e.RevisionId==created.Revision));Assert.Equal(JsonValueKind.Null,(await AdFeed(guardian)).ValueKind);
            (await Send(guardian,$"/api/v1/guardian/community-ads/{created.Revision}/events",new CommunityAdEventWrite("Click"))).EnsureSuccessStatusCode();
            var list=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/community-campaigns/");var row=list.GetProperty("items").EnumerateArray().Single(c=>c.GetProperty("id").GetGuid()==created.Campaign).GetProperty("versions")[0];Assert.All(row.GetProperty("metrics").EnumerateArray(),m=>{Assert.True(m.GetProperty("suppressed").GetBoolean());Assert.Equal(JsonValueKind.Null,m.GetProperty("count").ValueKind);});Assert.DoesNotContain("userId",row.GetRawText(),StringComparison.OrdinalIgnoreCase);
            (await Send(guardian,"/api/v1/guardian/community-ads/preference",new CommunityAdPreferenceWrite(false,true),true)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync($"/api/v1/guardian/community-ads/{created.Revision}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await Send(guardian,$"/api/v1/guardian/community-ads/{created.Revision}/events",new CommunityAdEventWrite("Click"))).StatusCode);
            Assert.Equal(collections,await db.Collections.CountAsync());Assert.Equal(periods,await db.SubscriptionPeriods.CountAsync());
            var e=await db.CommunityAdEvents.FirstAsync(e=>e.RevisionId==created.Revision);e.Kind="Hide";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
        }
        finally{await AdStop(owner,created.Campaign);await Send(guardian,"/api/v1/guardian/community-ads/preference",new CommunityAdPreferenceWrite(false,false),true);await AdRestore(owner,original);}
    }
    [Fact] public async Task Community_branch_approval_is_not_academy_wide_and_client_cannot_forge_target_or_external_destination()
    {
        using var owner=await Staff();using var guardian=await AdGuardian();var branch=await AdBranch();var original=await AdSettings(owner,true);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var foreignBranch=new Academy.Infrastructure.Structure.Branch{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,ArabicName="فرع إعلان غير مؤهل "+Guid.NewGuid().ToString("N")[..8],CreatedAtUtc=DateTimeOffset.UtcNow,UpdatedAtUtc=DateTimeOffset.UtcNow};db.Branches.Add(foreignBranch);await db.SaveChangesAsync();
        var body=AdDraft(branch) with{BranchIds=[branch,foreignBranch.Id]};var response=await Send(owner,"/api/v1/manage/community-campaigns/",body);response.EnsureSuccessStatusCode();var created=await response.Content.ReadFromJsonAsync<JsonElement>();var id=created.GetProperty("revisionId").GetGuid();var campaign=created.GetProperty("id").GetGuid();
        try
        {
            Assert.Equal(HttpStatusCode.OK,(await Send(owner,"/api/v1/manage/community-campaigns/",body)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,"/api/v1/manage/community-campaigns/",body with{Title="محاولة مختلفة بنفس المفتاح"})).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,"/api/v1/manage/community-campaigns/",body with{RequestId=Guid.NewGuid(),DestinationUrl="javascript:alert(1)"})).StatusCode);
            (await Send(guardian,"/api/v1/guardian/community-ads/preference",new CommunityAdPreferenceWrite(true,true),true)).EnsureSuccessStatusCode();(await AdReview(owner,id,"Submit")).EnsureSuccessStatusCode();(await AdReview(owner,id,"Approve",foreignBranch.Id)).EnsureSuccessStatusCode();Assert.Equal(JsonValueKind.Null,(await AdFeed(guardian)).ValueKind);
            (await AdReview(owner,id,"Approve",branch)).EnsureSuccessStatusCode();Assert.Equal(id,(await AdFeed(guardian)).GetProperty("id").GetGuid());
            (await Send(guardian,$"/api/v1/guardian/community-ads/{id}/events",new{kind="Click",branchId=foreignBranch.Id})).EnsureSuccessStatusCode();Assert.Equal(branch,await db.CommunityAdEvents.Where(e=>e.RevisionId==id).Select(e=>e.BranchId).SingleAsync());
            (await AdReview(owner,id,"Revoke",branch)).EnsureSuccessStatusCode();Assert.Equal(JsonValueKind.Null,(await AdFeed(guardian)).ValueKind);
            var license=await db.AcademyFeatureEntitlements.SingleAsync(x=>x.AcademyId==DemoSeed.NogoomAcademyId&&x.Feature=="community_ads");license.Enabled=false;await db.SaveChangesAsync();Assert.Equal(JsonValueKind.Null,(await AdFeed(guardian)).ValueKind);
            // Withdrawal is still possible when entitlement has been removed.
            (await Send(guardian,"/api/v1/guardian/community-ads/preference",new CommunityAdPreferenceWrite(false,false),true)).EnsureSuccessStatusCode();license.Enabled=true;await db.SaveChangesAsync();
        }
        finally{await AdStop(owner,campaign);await Send(guardian,"/api/v1/guardian/community-ads/preference",new CommunityAdPreferenceWrite(false,false),true);await AdRestore(owner,original);}
    }
    [Fact] public async Task Community_design_upload_and_download_follow_live_approval_not_media_permission_or_child_photo_reference()
    {
        using var owner=await Staff();using var guardian=await AdGuardian();using var unrelated=await AdGuardian(false);using var admin=await Staff(DemoSeed.AdminEmail);using var other=await Staff(DemoSeed.FutureOwnerEmail);var branch=await AdBranch();var original=await AdSettings(owner,true);Guid? campaign=null;
        try
        {
            var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aapcAAAAASUVORK5CYII=");
            async Task<HttpResponseMessage> Upload(HttpClient client){var csrf=await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");using var form=new MultipartFormDataContent();var file=new ByteArrayContent(png);file.Headers.ContentType=new MediaTypeHeaderValue("image/png");form.Add(file,"file","synthetic-ad.png");form.Add(new StringContent("Campaign"),"purpose");using var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/assets/upload"){Content=form};request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());return await client.SendAsync(request);}
            Assert.Equal(HttpStatusCode.Forbidden,(await Upload(admin)).StatusCode);var uploaded=await Upload(owner);uploaded.EnsureSuccessStatusCode();var asset=(await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync($"/api/v1/assets/{asset}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/api/v1/assets/{asset}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/v1/assets/{asset}")).StatusCode);
            var created=await Send(owner,"/api/v1/manage/community-campaigns/",AdDraft(branch) with{ImageAssetId=asset,ImageAlt="تصميم تجريبي بلا أطفال"});created.EnsureSuccessStatusCode();var data=await created.Content.ReadFromJsonAsync<JsonElement>();campaign=data.GetProperty("id").GetGuid();var id=data.GetProperty("revisionId").GetGuid();
            (await Send(guardian,"/api/v1/guardian/community-ads/preference",new CommunityAdPreferenceWrite(true,true),true)).EnsureSuccessStatusCode();(await Send(unrelated,"/api/v1/guardian/community-ads/preference",new CommunityAdPreferenceWrite(true,true),true)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync($"/api/v1/assets/{asset}")).StatusCode);
            (await AdReview(owner,id,"Submit")).EnsureSuccessStatusCode();(await AdReview(owner,id,"Approve",branch)).EnsureSuccessStatusCode();var image=await guardian.GetAsync($"/api/v1/assets/{asset}");image.EnsureSuccessStatusCode();Assert.Equal(png,await image.Content.ReadAsByteArrayAsync());Assert.True(image.Headers.CacheControl!.NoStore);Assert.Equal(HttpStatusCode.NotFound,(await unrelated.GetAsync($"/api/v1/assets/{asset}")).StatusCode);
            (await AdReview(owner,id,"Pause")).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync($"/api/v1/assets/{asset}")).StatusCode);Assert.Equal(HttpStatusCode.OK,(await owner.GetAsync($"/api/v1/assets/{asset}")).StatusCode);
            await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var user=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync();var photo=Guid.NewGuid();db.PrivateAssets.Add(new(){Id=photo,AcademyId=DemoSeed.NogoomAcademyId,UploadedByUserId=user,Purpose="Photo",PlayerId=Academy.Api.Slice2.Slice2DemoSeed.OmarPlayerId,StorageName="synthetic-unread-photo",ContentType="image/png",Length=1,CreatedAtUtc=DateTimeOffset.UtcNow,UpdatedAtUtc=DateTimeOffset.UtcNow});await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,"/api/v1/manage/community-campaigns/",AdDraft(branch) with{ImageAssetId=photo,ImageAlt="ليس تصميم إعلان"})).StatusCode);
            await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CommunityCampaignRevisions\" SET \"Title\"='changed' WHERE \"Id\"={id}"));
        }
        finally{if(campaign.HasValue)await AdStop(owner,campaign.Value);await Send(guardian,"/api/v1/guardian/community-ads/preference",new CommunityAdPreferenceWrite(false,false),true);await Send(unrelated,"/api/v1/guardian/community-ads/preference",new CommunityAdPreferenceWrite(false,false),true);await AdRestore(owner,original);}
    }
    [Fact] public async Task Match_schedule_change_is_typed_urgent_scoped_and_idempotent_without_inventing_old_notice_priority()
    {
        using var owner=await Staff();using var guardian=await Mobile(AcademyRole.Guardian);var f=await Fixtures(owner);var date=DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);var body=new MatchWrite(f.Tournament,f.Team,"منافس اصطناعي",date,new TimeOnly(17,0),"ملعب عرض",null,null,null,[],true,true);
        var save=await Send(owner,"/api/v1/manage/matches/",body);save.EnsureSuccessStatusCode();var id=(await save.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();var home=await guardian.GetFromJsonAsync<JsonElement>("/api/v1/guardian/home");Assert.DoesNotContain(home.GetProperty("urgentAlerts").EnumerateArray(),a=>a.GetProperty("matchId").GetGuid()==id);
        var changed=body with{StartTime=new TimeOnly(18,0)};(await Send(owner,$"/api/v1/manage/matches/{id}",changed,true)).EnsureSuccessStatusCode();home=await guardian.GetFromJsonAsync<JsonElement>("/api/v1/guardian/home");Assert.Contains(home.GetProperty("urgentAlerts").EnumerateArray(),a=>a.GetProperty("matchId").GetGuid()==id);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var count=await db.AcademyNotices.CountAsync(n=>n.MatchId==id);(await Send(owner,$"/api/v1/manage/matches/{id}",changed,true)).EnsureSuccessStatusCode();Assert.Equal(count,await db.AcademyNotices.CountAsync(n=>n.MatchId==id));
        var link=await db.GuardianPlayerLinks.Include(l=>l.Guardian).SingleAsync(l=>l.AcademyId==DemoSeed.NogoomAcademyId&&l.PlayerId==f.Player&&l.Guardian.User.PhoneNumber==DemoSeed.GuardianPhone);link.IsActive=false;await db.SaveChangesAsync();
        try{Assert.DoesNotContain((await guardian.GetFromJsonAsync<JsonElement>("/api/v1/guardian/home")).GetProperty("urgentAlerts").EnumerateArray(),a=>a.GetProperty("matchId").GetGuid()==id);}finally{link.IsActive=true;await db.SaveChangesAsync();}
    }
}
