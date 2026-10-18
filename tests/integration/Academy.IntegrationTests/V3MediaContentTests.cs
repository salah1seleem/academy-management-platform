using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact] public async Task Media_content_unavailable_inspector_is_503_with_no_raw_bytes_asset_or_audience_saved()
    {
        using var owner=await Staff();var options=await owner.GetFromJsonAsync<JsonElement>("/api/v1/evaluations/options");var group=options.GetProperty("enrollments").EnumerateArray().First(x=>x.GetProperty("groupId").ValueKind==JsonValueKind.String).GetProperty("groupId").GetGuid();var audience=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/assets/media-audience?groupId={group}");var day=DateOnly.Parse(audience.GetProperty("asOfDate").GetString()!);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var count=await db.PrivateAssets.CountAsync();var config=scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();var previous=config["Media:FFprobePath"];var storage=config["Media:PrivateRoot"]!;
        try
        {
            config["Media:FFprobePath"]=Path.Combine(storage,"no-such-inspector");var r=await PrivacyUpload(owner,group,day,noChildren:true);Assert.Equal(HttpStatusCode.ServiceUnavailable,r.StatusCode);Assert.Contains("لم يُنشر",(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());Assert.Equal(count,await db.PrivateAssets.CountAsync());Assert.True(!Directory.Exists(storage)||!Directory.EnumerateFiles(storage,"*",SearchOption.AllDirectories).Any());
        }
        finally{config["Media:FFprobePath"]=previous;}
    }
    private static async Task AssertMediaPixel(byte[] bytes)
    {
        var root=Directory.CreateTempSubdirectory("academity-pixel-assert-");
        try
        {
            var file=Path.Combine(root.FullName,"pixel.png");await File.WriteAllBytesAsync(file,bytes);
            var decoded=await PrivateMediaProcessor.Run("ffmpeg",["-v","error","-xerror","-f","png_pipe","-i",file,"-frames:v","1","-pix_fmt","rgba","-f","framemd5","-"],TimeSpan.FromSeconds(5),CancellationToken.None);
            Assert.Contains("#dimensions 0: 1x1",decoded);Assert.EndsWith("a54f0041a9e15b050f25c463f1db7449",decoded.Split('\n').Single(s=>s.StartsWith("0,")));
        }
        finally {root.Delete(true);}
    }
    [Fact] public async Task Media_content_corrupt_body_mime_spoof_and_overlong_video_are_rejected_before_any_publication_or_financial_change()
    {
        using var owner=await Staff();using var coach=await Mobile(Academy.Infrastructure.Tenancy.AcademyRole.Coach);using var foreign=await Staff(DemoSeed.FutureOwnerEmail);
        var options=await coach.GetFromJsonAsync<JsonElement>("/api/v1/evaluations/options");var group=options.GetProperty("enrollments")[0].GetProperty("groupId").GetGuid();var audience=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/assets/media-audience?groupId={group}");var day=DateOnly.Parse(audience.GetProperty("asOfDate").GetString()!);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var count=await db.PrivateAssets.CountAsync();var audienceCount=await db.PrivateAssetAudiences.CountAsync();var money=await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28");
        var corrupt=await PrivacyUpload(owner,group,day,noChildren:true,payload:MediaPng[..20]);Assert.Equal(HttpStatusCode.BadRequest,corrupt.StatusCode);Assert.Contains("لم يُنشر",await corrupt.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest,(await PrivacyUpload(owner,group,day,noChildren:true,payload:MediaPng,mime:"video/mp4")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await PrivacyUpload(foreign,group,day,noChildren:true)).StatusCode);
        var root=Directory.CreateTempSubdirectory("academity-video-api-");
        try
        {
            var path=Path.Combine(root.FullName,"long.mp4");await PrivateMediaProcessor.Run("ffmpeg",["-nostdin","-v","error","-f","lavfi","-i","color=c=blue:s=16x16:r=1:d=121","-c:v","mpeg4",path],TimeSpan.FromSeconds(10),CancellationToken.None);
            Assert.Equal(HttpStatusCode.BadRequest,(await PrivacyUpload(owner,group,day,noChildren:true,payload:await File.ReadAllBytesAsync(path),mime:"video/mp4")).StatusCode);
        }
        finally {root.Delete(true);}
        Assert.Equal(count,await db.PrivateAssets.CountAsync());Assert.Equal(audienceCount,await db.PrivateAssetAudiences.CountAsync());Assert.Equal(money,await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
    }
    [Fact] public async Task Media_content_actual_mov_is_reencoded_then_authorized_download_serves_only_the_metadata_free_mp4_and_withdrawal_still_wins()
    {
        using var owner=await Staff();using var coach=await Mobile(Academy.Infrastructure.Tenancy.AcademyRole.Coach);using var foreign=await Staff(DemoSeed.FutureOwnerEmail);
        var options=await coach.GetFromJsonAsync<JsonElement>("/api/v1/evaluations/options");var group=options.GetProperty("enrollments")[0].GetProperty("groupId").GetGuid();var audience=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/assets/media-audience?groupId={group}");var day=DateOnly.Parse(audience.GetProperty("asOfDate").GetString()!);
        var root=Directory.CreateTempSubdirectory("academity-video-api-");
        try
        {
            var input=Path.Combine(root.FullName,"source.mov");await PrivateMediaProcessor.Run("ffmpeg",["-nostdin","-v","error","-f","lavfi","-i","color=c=red:s=320x180:r=25:d=1","-c:v","mpeg4","-metadata","location=+30.0000+031.0000/","-metadata","title=Synthetic-camera-owner",input],TimeSpan.FromSeconds(10),CancellationToken.None);
            var original=await File.ReadAllBytesAsync(input);var response=await PrivacyUpload(coach,group,day,noChildren:true,payload:original,mime:"video/quicktime");Assert.Equal(HttpStatusCode.Created,response.StatusCode);var saved=await response.Content.ReadFromJsonAsync<JsonElement>();var id=saved.GetProperty("id").GetGuid();Assert.Equal("video/mp4",saved.GetProperty("contentType").GetString());
            var file=await coach.GetPrivateAssetAsync(factory,$"/api/v1/assets/{id}");Assert.Equal(HttpStatusCode.OK,file.StatusCode);Assert.Equal("video/mp4",file.Content.Headers.ContentType!.MediaType);var bytes=await file.Content.ReadAsByteArrayAsync();Assert.NotEqual(original,bytes);Assert.DoesNotContain("Synthetic-camera-owner",Encoding.UTF8.GetString(bytes));Assert.DoesNotContain("+30.000",Encoding.UTF8.GetString(bytes));
            var output=Path.Combine(root.FullName,"download.mp4");await File.WriteAllBytesAsync(output,bytes);await PrivateMediaProcessor.Run("ffmpeg",["-nostdin","-v","error","-xerror","-i",output,"-f","null","-"],TimeSpan.FromSeconds(5),CancellationToken.None);
            await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var asset=await db.PrivateAssets.SingleAsync(a=>a.Id==id);Assert.Equal(bytes.Length,asset.Length);Assert.Equal("video/mp4",asset.ContentType);Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetPrivateAssetAsync(factory,$"/api/v1/assets/{id}")).StatusCode);
            (await Send(owner,$"/api/v1/assets/{id}/withdraw",new MediaWithdrawalWrite(Guid.NewGuid(),null))).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.NotFound,(await coach.GetPrivateAssetAsync(factory,$"/api/v1/assets/{id}")).StatusCode);Assert.Equal(HttpStatusCode.OK,(await owner.GetPrivateAssetAsync(factory,$"/api/v1/assets/{id}")).StatusCode);
        }
        finally {root.Delete(true);}
    }
}
