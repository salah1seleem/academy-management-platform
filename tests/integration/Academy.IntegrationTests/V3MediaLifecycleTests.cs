using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    private async Task<(HttpClient Family,Guid Player,Guid Group,DateOnly Day)> LifecycleFamily(HttpClient owner)
    {
        var family=await ExclusiveMediaFamily();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var source=await db.SportEnrollments.FirstAsync(e=>e.AcademyId==DemoSeed.NogoomAcademyId&&e.IsActive&&e.TrainingGroupId!=null);var child=await FreshMediaChild(db,source,family.Guardian);var group=child.TrainingGroupId!.Value;var day=DateOnly.Parse((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/assets/media-audience?groupId={group}")).GetProperty("asOfDate").GetString()!);
        (await Send(family.Client,$"/api/v1/guardian/children/{child.PlayerId}/group-media-consent",new GroupMediaConsentWrite(true,0,Guid.NewGuid()))).EnsureSuccessStatusCode();return(family.Client,child.PlayerId,group,day);
    }
    private async Task<AcademyRuntimeSettings> MediaSettings(HttpClient owner)=>JsonSerializer.Deserialize<AcademyRuntimeSettings>((await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/configuration")).GetProperty("versions").EnumerateArray().First(v=>v.GetProperty("state").GetString()=="Active").GetProperty("settings").GetRawText(),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    private async Task ActivateMediaSettings(HttpClient owner,AcademyRuntimeSettings settings)
    {
        var response=await Send(owner,"/api/v1/manage/configuration/drafts",settings);response.EnsureSuccessStatusCode();var id=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        foreach(var target in new[]{"Validated","Staged","Active"})
        {
            var list=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/configuration");var version=list.GetProperty("versions").EnumerateArray().Single(v=>v.GetProperty("id").GetGuid()==id).GetProperty("version").GetUInt32();(await Send(owner,$"/api/v1/manage/configuration/{id}/transition",new{target,version})).EnsureSuccessStatusCode();
        }
    }
    [Fact] public async Task Media_lifecycle_manual_erasure_preserves_evidence_denies_every_role_and_old_links_and_replays_without_double_purge_or_money_change()
    {
        using var owner=await Staff();using var foreign=await Staff(DemoSeed.FutureOwnerEmail);using var coach=await Mobile(AcademyRole.Coach);var f=await LifecycleFamily(owner);using var family=f.Family;
        var id=await MediaCreated(await PrivacyUpload(owner,f.Group,f.Day,f.Player));var url=$"/api/v1/assets/{id}";var money=await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28");await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var config=scope.ServiceProvider.GetRequiredService<IConfiguration>();var asset=await db.PrivateAssets.AsNoTracking().SingleAsync(a=>a.Id==id);var path=Path.Combine(config["Media:PrivateRoot"]!,asset.StorageName);Assert.True(File.Exists(path));
        using var raw=factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions{AllowAutoRedirect=false});raw.DefaultRequestHeaders.Authorization=family.DefaultRequestHeaders.Authorization;var ticket=(await raw.GetAsync(url)).Headers.Location!.ToString();Assert.Equal(HttpStatusCode.OK,(await raw.GetAsync(ticket)).StatusCode);
        var command=new MediaErasureWrite(Guid.NewGuid(),null);
        Assert.Equal(HttpStatusCode.Forbidden,(await Send(family,url+"/delete",command)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await Send(coach,url+"/delete",command)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await Send(foreign,url+"/delete",command)).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(url+"/delete",command)).StatusCode);Assert.True(File.Exists(path));
        var result=await Send(owner,url+"/delete",command);result.EnsureSuccessStatusCode();var saved=await result.Content.ReadFromJsonAsync<JsonElement>();Assert.True(saved.GetProperty("erased").GetBoolean());Assert.True(saved.GetProperty("purged").GetBoolean());Assert.False(File.Exists(path));Assert.Equal(HttpStatusCode.NotFound,(await raw.GetAsync(ticket)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync(url)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await family.GetPrivateAssetAsync(factory,url)).StatusCode);Assert.DoesNotContain(id.ToString(),await family.GetStringAsync($"/api/v1/assets/gallery?playerId={f.Player}"));
        (await Send(owner,url+"/delete",command)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,url+"/delete",command with{Note="ملاحظة أخرى"})).StatusCode);Assert.Equal(1,await db.PrivateMediaErasures.CountAsync(e=>e.AssetId==id));Assert.Equal(1,await db.PrivateMediaPurges.CountAsync(p=>p.AssetId==id));Assert.True(await db.PrivateAssets.AnyAsync(a=>a.Id==id));Assert.True(await db.PrivateAssetAudiences.AnyAsync(a=>a.AssetId==id));Assert.Equal(money,await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
        var history=await db.PrivateMediaErasures.SingleAsync(e=>e.AssetId==id);history.Note="تعديل ممنوع";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();var sql=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"PrivateMediaPurges\" WHERE \"AssetId\"={id}"));Assert.Equal("23514",sql.SqlState);
    }
    [Fact] public async Task Media_lifecycle_retention_is_versioned_new_upload_snapshot_expiry_is_live_and_only_due_opted_files_are_purged()
    {
        using var owner=await Staff();var f=await LifecycleFamily(owner);using var family=f.Family;var original=await MediaSettings(owner);await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var config=scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var old=await MediaCreated(await PrivacyUpload(owner,f.Group,f.Day,f.Player));
        try
        {
            await ActivateMediaSettings(owner,original with{MediaRetention=new(1,true)});var id=await MediaCreated(await PrivacyUpload(owner,f.Group,f.Day,f.Player));var live=await db.PrivateAssets.AsNoTracking().SingleAsync(a=>a.Id==id);Assert.True(live.DeleteOnExpiry);Assert.InRange(live.RetainUntilUtc!.Value,DateTimeOffset.UtcNow.AddHours(23),DateTimeOffset.UtcNow.AddHours(25));
            await ActivateMediaSettings(owner,original with{MediaRetention=new(30,false)});Assert.Equal(live.RetainUntilUtc,(await db.PrivateAssets.AsNoTracking().SingleAsync(a=>a.Id==id)).RetainUntilUtc);Assert.Null((await db.PrivateAssets.AsNoTracking().SingleAsync(a=>a.Id==old)).RetainUntilUtc);
            db.PrivateMediaErasures.Add(new(){Id=Guid.NewGuid(),AcademyId=live.AcademyId,AssetId=id,Reason="Retention",RequestId=Guid.NewGuid(),RequestHash=PrivateMediaLifecycle.Hash(id,null)});var early=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.Equal("23514",Assert.IsType<Npgsql.PostgresException>(early.InnerException).SqlState);db.ChangeTracker.Clear();
            var now=DateTimeOffset.UtcNow;var expired=new PrivateAsset{Id=Guid.NewGuid(),AcademyId=live.AcademyId,UploadedByUserId=live.UploadedByUserId,ContentType=live.ContentType,StorageName=Guid.NewGuid().ToString("N"),Length=live.Length,PlayerId=f.Player,Purpose="Media",Caption="ملف احتفاظ اصطناعي من أيام سابقة",MediaPolicyVersion=1,MediaEventOn=f.Day,RetainUntilUtc=now.AddDays(-1),DeleteOnExpiry=true,CreatedAtUtc=now.AddDays(-2),UpdatedAtUtc=now.AddDays(-2)};db.PrivateAssets.Add(expired);await db.SaveChangesAsync();var path=Path.Combine(config["Media:PrivateRoot"]!,expired.StorageName);File.Copy(Path.Combine(config["Media:PrivateRoot"]!,live.StorageName),path);
            Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync($"/api/v1/assets/{expired.Id}")).StatusCode);Assert.DoesNotContain(expired.Id.ToString(),await family.GetStringAsync($"/api/v1/assets/gallery?playerId={f.Player}"));
            var job=scope.ServiceProvider.GetRequiredService<PrivateMediaLifecycle>();await job.RunBatch(CancellationToken.None);await job.RunBatch(CancellationToken.None);Assert.False(File.Exists(path));Assert.Equal(1,await db.PrivateMediaPurges.CountAsync(p=>p.AssetId==expired.Id));var erasure=await db.PrivateMediaErasures.SingleAsync(e=>e.AssetId==expired.Id);Assert.Equal("Retention",erasure.Reason);Assert.Null(erasure.ActorUserId);Assert.False(await db.PrivateMediaErasures.AnyAsync(e=>e.AssetId==old||e.AssetId==id));Assert.True(File.Exists(Path.Combine(config["Media:PrivateRoot"]!,live.StorageName)));
            Assert.Equal(HttpStatusCode.OK,(await family.GetPrivateAssetAsync(factory,$"/api/v1/assets/{old}")).StatusCode);
        }
        finally{await ActivateMediaSettings(owner,original);}
    }
    [Fact] public async Task Media_lifecycle_storage_is_charged_until_physical_purge_and_daily_reservations_are_atomic_without_weakening_authority()
    {
        using var owner=await Staff();var f=await LifecycleFamily(owner);using var family=f.Family;var id=await MediaCreated(await PrivacyUpload(owner,f.Group,f.Day,f.Player));await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var config=scope.ServiceProvider.GetRequiredService<IConfiguration>();var service=scope.ServiceProvider.GetRequiredService<PrivateMediaLifecycle>();var asset=await db.PrivateAssets.AsNoTracking().SingleAsync(a=>a.Id==id);var used=await service.StoredBytes(asset.AcademyId);var priorStorage=config["Media:StorageLimitBytes"];var priorDownload=config["Media:DailyDownloadLimitBytes"];var path=Path.Combine(config["Media:PrivateRoot"]!,asset.StorageName);var backup=path+".test-held";
        try
        {
            config["Media:StorageLimitBytes"]=used.ToString();Assert.Equal(HttpStatusCode.Conflict,(await PrivacyUpload(owner,f.Group,f.Day,f.Player)).StatusCode);(await Send(owner,$"/api/v1/assets/{id}/withdraw",new MediaWithdrawalWrite(Guid.NewGuid(),null))).EnsureSuccessStatusCode();Assert.Equal(used,await service.StoredBytes(asset.AcademyId));Assert.Equal(HttpStatusCode.Conflict,(await PrivacyUpload(owner,f.Group,f.Day,f.Player)).StatusCode);
            File.Move(path,backup);Directory.CreateDirectory(path);var command=new MediaErasureWrite(Guid.NewGuid(),null);var pending=await Send(owner,$"/api/v1/assets/{id}/delete",command);pending.EnsureSuccessStatusCode();Assert.False((await pending.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("purged").GetBoolean());Assert.Equal(used,await service.StoredBytes(asset.AcademyId));Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync($"/api/v1/assets/{id}")).StatusCode);
            Directory.Delete(path);File.Move(backup,path);(await Send(owner,$"/api/v1/assets/{id}/delete",command)).EnsureSuccessStatusCode();Assert.False(File.Exists(path));Assert.Equal(used-asset.Length,await service.StoredBytes(asset.AcademyId));var fresh=await MediaCreated(await PrivacyUpload(owner,f.Group,f.Day,f.Player));Assert.Equal(used,await service.StoredBytes(asset.AcademyId));
            config["Media:DailyDownloadLimitBytes"]=asset.Length.ToString();var downloads=await Task.WhenAll(family.GetPrivateAssetAsync(factory,$"/api/v1/assets/{fresh}"),family.GetPrivateAssetAsync(factory,$"/api/v1/assets/{fresh}"));Assert.Single(downloads,r=>r.StatusCode==HttpStatusCode.OK);Assert.Single(downloads,r=>r.StatusCode==HttpStatusCode.TooManyRequests);var user=await db.GuardianProfiles.Where(g=>g.Id==db.GuardianPlayerLinks.Where(l=>l.PlayerId==f.Player).Select(l=>l.GuardianId).First()).Select(g=>g.UserId).SingleAsync();var budget=await db.PrivateMediaDownloadBudgets.SingleAsync(b=>b.UserId==user);Assert.Equal(asset.Length,budget.ReservedBytes);Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow),budget.UtcDay);
            Assert.Null((await family.GetFromJsonAsync<JsonElement>("/api/v1/assets/storage-policy")).GetProperty("storedBytes").GetString());
        }
        finally{config["Media:StorageLimitBytes"]=priorStorage;config["Media:DailyDownloadLimitBytes"]=priorDownload;if(Directory.Exists(path))Directory.Delete(path);if(File.Exists(backup))File.Move(backup,path);}
    }
    [Fact] public async Task Media_lifecycle_delegated_delete_requires_read_delete_and_current_branch_even_on_replay()=>await DelegateFixture(async(owner,admin,db,group,member)=>{
        var m=await db.AcademyMemberships.SingleAsync(x=>x.Id==member);m.AllSports=true;
        var grant=await db.StaffActionGrants.SingleOrDefaultAsync(g=>g.MembershipId==member&&g.Module=="media");var before=grant==null?null:JsonSerializer.Serialize(grant);grant??=new(){Id=Guid.NewGuid(),AcademyId=group.AcademyId,MembershipId=member,Module="media"};if(before==null)db.StaffActionGrants.Add(grant);
        var settingsGrant=await db.StaffActionGrants.SingleOrDefaultAsync(g=>g.MembershipId==member&&g.Module=="settings");var settingsBefore=settingsGrant==null?null:JsonSerializer.Serialize(settingsGrant);settingsGrant??=new(){Id=Guid.NewGuid(),AcademyId=group.AcademyId,MembershipId=member,Module="settings"};if(settingsBefore==null)db.StaffActionGrants.Add(settingsGrant);
        try{
            grant.Read=true;grant.Add=true;grant.Edit=true;grant.Delete=false;settingsGrant.Read=true;settingsGrant.Add=true;settingsGrant.Edit=true;await db.SaveChangesAsync();
            var family=await ExclusiveMediaFamily();using var familyClient=family.Client;await FreshMediaChild(db,new(){AcademyId=group.AcademyId,SportId=group.SportId,BranchId=group.BranchId,TrainingGroupId=group.Id},family.Guardian);
            var day=DateOnly.Parse((await admin.GetFromJsonAsync<JsonElement>($"/api/v1/assets/media-audience?groupId={group.Id}")).GetProperty("asOfDate").GetString()!);var id=await MediaCreated(await PrivacyUpload(admin,group.Id,day,noChildren:true));var command=new MediaErasureWrite(Guid.NewGuid(),null);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,$"/api/v1/assets/{id}/delete",command)).StatusCode);grant.Delete=true;grant.Add=false;grant.Edit=false;await db.SaveChangesAsync();(await Send(admin,$"/api/v1/assets/{id}/delete",command)).EnsureSuccessStatusCode();
            var settings=await MediaSettings(owner);(await Send(admin,"/api/v1/manage/configuration/drafts",settings)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,"/api/v1/manage/configuration/drafts",settings with{MediaRetention=new(5,true)})).StatusCode);
            var b=await db.MembershipBranchAccesses.SingleAsync(x=>x.MembershipId==member&&x.IsActive);b.IsActive=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.NotFound,(await Send(admin,$"/api/v1/assets/{id}/delete",command)).StatusCode);
        }finally{
            if(before==null)db.StaffActionGrants.Remove(grant);else db.Entry(grant).CurrentValues.SetValues(JsonSerializer.Deserialize<StaffActionGrant>(before)!);
            if(settingsBefore==null)db.StaffActionGrants.Remove(settingsGrant);else db.Entry(settingsGrant).CurrentValues.SetValues(JsonSerializer.Deserialize<StaffActionGrant>(settingsBefore)!);
            await db.SaveChangesAsync();
        }
    });
    [Fact]public async Task Media_lifecycle_shared_legacy_filename_is_not_removed_or_released_until_its_other_reference_is_resolved()
    {
        using var owner=await Staff();var f=await LifecycleFamily(owner);using var family=f.Family;var id=await MediaCreated(await PrivacyUpload(owner,f.Group,f.Day,f.Player));await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var config=scope.ServiceProvider.GetRequiredService<IConfiguration>();var service=scope.ServiceProvider.GetRequiredService<PrivateMediaLifecycle>();var a=await db.PrivateAssets.AsNoTracking().SingleAsync(x=>x.Id==id);var path=Path.Combine(config["Media:PrivateRoot"]!,a.StorageName);var bytes=await service.StoredBytes(a.AcademyId);
        var foreign=new PrivateAsset{Id=Guid.NewGuid(),AcademyId=DemoSeed.FutureAcademyId,UploadedByUserId=await db.Users.Where(u=>u.Email==DemoSeed.FutureOwnerEmail).Select(u=>u.Id).SingleAsync(),ContentType=a.ContentType,StorageName=a.StorageName,Length=a.Length,Purpose="Media",Caption="مرجع قديم اصطناعي مشترك للاختبار فقط"};db.PrivateAssets.Add(foreign);await db.SaveChangesAsync();var command=new MediaErasureWrite(Guid.NewGuid(),null);
        try
        {
            var r=await Send(owner,$"/api/v1/assets/{id}/delete",command);r.EnsureSuccessStatusCode();Assert.False((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("purged").GetBoolean());Assert.True(File.Exists(path));Assert.Equal(bytes,await service.StoredBytes(a.AcademyId));Assert.Equal(HttpStatusCode.NotFound,(await family.GetPrivateAssetAsync(factory,$"/api/v1/assets/{id}")).StatusCode);
        }
        finally{db.PrivateAssets.Remove(foreign);await db.SaveChangesAsync();} // Only the synthetic, policy0 fixture without publication/audience/history.
        (await Send(owner,$"/api/v1/assets/{id}/delete",command)).EnsureSuccessStatusCode();Assert.False(File.Exists(path));Assert.Equal(bytes-a.Length,await service.StoredBytes(a.AcademyId));
    }
}
