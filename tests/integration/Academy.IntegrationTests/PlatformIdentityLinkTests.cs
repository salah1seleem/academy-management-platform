using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Importing;
using Academy.Api.Auth;
using Academy.Api.Mobile;
using Academy.Api.Platform;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;
public sealed partial class PlatformSetupImportTests
{
    [Fact]
    public async Task Exact_existing_identity_can_own_A_and_parent_B_without_client_link_flag_without_password_overwrite_or_phone_only_context_disclosure()
    {
        using var platform=await Platform();var phone="01097"+Random.Shared.Next(100000,999999);var a=await Create(platform,phone);var b=await Create(platform,"01098"+Random.Shared.Next(100000,999999));string email,passwordHash;Guid ownerUser;
        await using(var scope=factory.Services.CreateAsyncScope()) {
            var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var owner=await db.AcademyMemberships.Include(m=>m.User).SingleAsync(m=>m.AcademyId==a&&m.Role==AcademyRole.AcademyOwner);email=owner.User.Email!;passwordHash=owner.User.PasswordHash!;ownerUser=owner.UserId;
            foreach(var id in new[]{a,b})(await db.Academies.SingleAsync(x=>x.Id==id)).IsActive=true;await db.SaveChangesAsync();
        }
        var request=new PlatformAcademyWrite(Guid.NewGuid(),"linked-"+Guid.NewGuid().ToString("N")[..12],"أكاديمية هوية خيالية","EGP","Africa/Cairo","مالك اختبار خيالي",email,"Ignored-Not-Replaced-123!","swimming",phone);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(platform,"/api/v1/platform/academies",request with{OwnerName="شخص مختلف"})).StatusCode);
        var linked=await Send(platform,"/api/v1/platform/academies",request);linked.EnsureSuccessStatusCode();var c=(await linked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var licenses=await platform.GetFromJsonAsync<JsonElement>($"/api/v1/platform/academies/{b}/entitlements");var features=licenses.EnumerateArray().ToDictionary(x=>x.GetProperty("feature").GetString()!,x=>x.GetProperty("feature").GetString()=="training");(await Send(platform,$"/api/v1/platform/academies/{b}/entitlements",new{planCode="Trial",features},true)).EnsureSuccessStatusCode();
        var book=FriendlyImportRehearsal.Create(b,new(2026,10,25),2,Random.Shared.Next(82_000_000,89_000_000));
        var optional=new[]{"فترات سابقة اختيارية","حصص فعلية اختيارية","حضور اختياري","معايير تقييم اختيارية","تقييمات اختيارية","درجات اختيارية","تحصيلات سابقة اختيارية"};
        book=new(book.Sheets.Select(s=>optional.Contains(s.Name)?s with{Rows=s.Rows.Take(1).ToArray()}:s.Name!="أولياء الأمور"?s:s with{Rows=s.Rows.Select((r,i)=>i!=1?r:r.Select((cell,n)=>n==1?new ImportCell("مالك اختبار خيالي",true):n==2?new ImportCell(phone,true):cell).ToArray()).ToArray()}).ToArray());
        var bytes=ImportXlsx.Write(book);var preview=await Preview(platform,b,bytes);Assert.True(preview.CanApprove,string.Join(";",preview.Errors.Select(e=>e.Message)));
        var staged=await Stage(platform,b,bytes,preview);staged.EnsureSuccessStatusCode();var job=(await staged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();(await db.AcademyImportJobs.SingleAsync(x=>x.Id==job)).State=ImportJobState.Running;await db.SaveChangesAsync();}
        await using(var scope=factory.Services.CreateAsyncScope())await scope.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(job);
        Guid parent;
        await using(var scope=factory.Services.CreateAsyncScope()) {var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();parent=await db.AcademyMemberships.Where(m=>m.AcademyId==b&&m.UserId==ownerUser&&m.Role==AcademyRole.Guardian).Select(m=>m.Id).SingleAsync();Assert.Equal(passwordHash,await db.Users.Where(u=>u.Id==ownerUser).Select(u=>u.PasswordHash).SingleAsync());Assert.Equal(1,await db.Users.CountAsync(u=>u.PhoneNumber==EgyptPhoneNormalizer.Normalize(phone)));Assert.Equal(ownerUser,await db.AcademyMemberships.Where(m=>m.AcademyId==c).Select(m=>m.UserId).SingleAsync());}
        using var mobile=factory.CreateClient();var otp=await mobile.PostAsJsonAsync("/api/v1/mobile/auth/otp/request",new{phoneNumber=phone});otp.EnsureSuccessStatusCode();var challenge=await otp.Content.ReadFromJsonAsync<JsonElement>();Assert.False(challenge.TryGetProperty("memberships",out _));
        Assert.Equal(HttpStatusCode.Unauthorized,(await mobile.GetAsync("/api/v1/my-academies")).StatusCode);
        var verified=await mobile.PostAsJsonAsync("/api/v1/mobile/auth/otp/verify",new{phoneNumber=phone,challengeId=challenge.GetProperty("challengeId").GetGuid(),code="246810",deviceName="Identity linking test"});verified.EnsureSuccessStatusCode();var credential=(await verified.Content.ReadFromJsonAsync<MobileCredentials>())!;Assert.Contains(credential.Memberships,m=>m.AcademyId==a);Assert.Contains(credential.Memberships,m=>m.Id==parent);mobile.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",credential.AccessToken);
        var selection=await mobile.PostAsJsonAsync("/api/v1/mobile/auth/select-role",new{membershipId=parent});selection.EnsureSuccessStatusCode();var selected=(await selection.Content.ReadFromJsonAsync<MobileCredentials>())!;mobile.DefaultRequestHeaders.Authorization=new("Bearer",selected.AccessToken);Assert.Equal(HttpStatusCode.Forbidden,(await mobile.GetAsync("/api/v1/reports/owner-summary")).StatusCode);
    }
}
