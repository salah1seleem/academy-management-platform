using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class OwnerReviewOperationsTests
{
    private static async Task<HttpResponseMessage> ContentWrite(HttpClient client,string path,object body,HttpMethod? method=null)
    {
        var csrf=await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        var request=new HttpRequestMessage(method??HttpMethod.Post,path){Content=JsonContent.Create(body)};
        request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());return await client.SendAsync(request);
    }
    [Fact] public async Task Approved_content_filters_role_branch_schedule_and_private_image_using_same_audience()
    {
        using var owner=await Owner();using var guardian=await Guardian();using var coach=factory.CreateClient();
        await MobileCoachTestSession.SignInAsync(coach,factory.Services);
        var image=await Upload(owner,"News");
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var now=scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var otherBranch=Guid.NewGuid();db.Branches.Add(new Branch{Id=otherBranch,AcademyId=DemoSeed.NogoomAcademyId,ArabicName="فرع محتوى اصطناعي "+otherBranch.ToString("N")[..8],CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();
        var body=new NewsWrite("خبر اختبار عمليات "+Guid.NewGuid(),"تفاصيل اصطناعية للمستلمين المخولين فقط",image,7,true,true,"Event",[otherBranch],["Guardian"],now.AddMinutes(-1),now.AddDays(6),RequestId:Guid.NewGuid());
        var created=await ContentWrite(owner,"/api/v1/content/news/",body);created.EnsureSuccessStatusCode();var row=await created.Content.ReadFromJsonAsync<JsonElement>();var id=row.GetProperty("id").GetGuid();
        var exact = $"/api/v1/news?kind=Event&page=2&item={id}";
        Assert.Equal(0, (await guardian.GetFromJsonAsync<JsonElement>(exact)).GetProperty("items").GetArrayLength());
        Assert.DoesNotContain(body.Title,await guardian.GetStringAsync("/api/v1/news"));Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync($"/api/v1/assets/{image}")).StatusCode);
        body=body with{TargetBranchIds=[Slice2DemoSeed.CityBranchId],AudienceRoles=["Coach"],ExpectedVersion=row.GetProperty("version").GetUInt32()};
        var edited=await ContentWrite(owner,$"/api/v1/content/news/{id}",body,HttpMethod.Put);edited.EnsureSuccessStatusCode();row=await edited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(body.Title,await coach.GetStringAsync("/api/v1/news"));Assert.Equal(HttpStatusCode.OK,(await coach.GetAsync($"/api/v1/assets/{image}")).StatusCode);
        Assert.DoesNotContain(body.Title,await guardian.GetStringAsync("/api/v1/news"));Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync($"/api/v1/assets/{image}")).StatusCode);
        body=body with{AudienceRoles=["Guardian"],StartsAtUtc=now.AddDays(2),EndsAtUtc=now.AddDays(6),ExpectedVersion=row.GetProperty("version").GetUInt32()};
        edited=await ContentWrite(owner,$"/api/v1/content/news/{id}",body,HttpMethod.Put);edited.EnsureSuccessStatusCode();row=await edited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Scheduled",row.GetProperty("state").GetString());Assert.DoesNotContain(body.Title,await guardian.GetStringAsync("/api/v1/news"));Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync($"/api/v1/assets/{image}")).StatusCode);
        Assert.Equal(0, (await guardian.GetFromJsonAsync<JsonElement>(exact)).GetProperty("items").GetArrayLength());
        body=body with{StartsAtUtc=now.AddMinutes(-1),ExpectedVersion=row.GetProperty("version").GetUInt32()};
        edited=await ContentWrite(owner,$"/api/v1/content/news/{id}",body,HttpMethod.Put);edited.EnsureSuccessStatusCode();row=await edited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(body.Title,await guardian.GetStringAsync("/api/v1/news"));Assert.Equal(HttpStatusCode.OK,(await guardian.GetAsync($"/api/v1/assets/{image}")).StatusCode);
        Assert.DoesNotContain(body.Title,await coach.GetStringAsync("/api/v1/news"));Assert.Equal(HttpStatusCode.NotFound,(await coach.GetAsync($"/api/v1/assets/{image}")).StatusCode);
        using var foreign=factory.CreateClient();(await Send(foreign,"/api/v1/auth/login",new{email=DemoSeed.FutureOwnerEmail,password="Demo-Only-123!"})).EnsureSuccessStatusCode();
        Assert.Contains(body.Title, await guardian.GetStringAsync("/api/v1/news?kind=Event&page=1"));
        Assert.DoesNotContain(body.Title, await guardian.GetStringAsync("/api/v1/news?kind=News&page=1"));
        Assert.DoesNotContain(body.Title, await foreign.GetStringAsync("/api/v1/news?kind=Event&page=1"));
        var selected = await guardian.GetFromJsonAsync<JsonElement>(exact);
        Assert.Equal(1, selected.GetProperty("page").GetInt32());
        Assert.Equal(id, Assert.Single(selected.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(0, (await coach.GetFromJsonAsync<JsonElement>(exact)).GetProperty("items").GetArrayLength());
        Assert.Equal(0, (await foreign.GetFromJsonAsync<JsonElement>(exact)).GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync($"/api/v1/content/news/{id}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync($"/api/v1/assets/{image}")).StatusCode);
        var archived=await ContentWrite(owner,$"/api/v1/content/news/{id}",new{expectedVersion=row.GetProperty("version").GetUInt32()},HttpMethod.Delete);archived.EnsureSuccessStatusCode();
        Assert.DoesNotContain(body.Title,await guardian.GetStringAsync("/api/v1/news"));Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync($"/api/v1/assets/{image}")).StatusCode);
        Assert.Equal(0, (await guardian.GetFromJsonAsync<JsonElement>(exact)).GetProperty("items").GetArrayLength());
    }
    [Fact] public async Task Approved_content_creation_replays_once_rejects_stale_and_uses_local_publication_days()
    {
        using var owner=await Owner();using var guardian=await Guardian();
        var calendar=await owner.GetFromJsonAsync<JsonElement>("/api/v1/academy/calendar");var day=DateOnly.Parse(calendar.GetProperty("today").GetString()!);
        var body=new NewsWrite("خبر اختبار عمليات "+Guid.NewGuid(),"إعلان أكاديمية اصطناعي",null,7,true,true,"Announcement",[],["Guardian"],RequestId:Guid.NewGuid(),LocalStartsOn:day,LocalEndsOn:day.AddDays(6));
        var result=await ContentWrite(owner,"/api/v1/content/news/",body);result.EnsureSuccessStatusCode();var row=await result.Content.ReadFromJsonAsync<JsonElement>();var id=row.GetProperty("id").GetGuid();
        Assert.Equal(day.ToString("yyyy-MM-dd"),row.GetProperty("localStartsOn").GetString());Assert.Equal("Published",row.GetProperty("state").GetString());
        var replay=await ContentWrite(owner,"/api/v1/content/news/",body);replay.EnsureSuccessStatusCode();Assert.Equal(id,(await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict,(await ContentWrite(owner,"/api/v1/content/news/",body with{Body="تغيير برمز الإنشاء نفسه"})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await ContentWrite(owner,$"/api/v1/content/news/{id}",body with{ExpectedVersion=0},HttpMethod.Put)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await ContentWrite(owner,"/api/v1/content/news/",body with{RequestId=Guid.NewGuid(),AudienceRoles=["PlatformAdmin"]})).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await ContentWrite(owner,"/api/v1/content/news/",body with{RequestId=Guid.NewGuid(),TargetBranchIds=[Guid.NewGuid()]})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await ContentWrite(guardian,"/api/v1/content/news/",body)).StatusCode);
        Assert.Contains(body.Title,await guardian.GetStringAsync("/api/v1/news"));
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(1,await db.AcademyNews.CountAsync(n=>n.AcademyId==DemoSeed.NogoomAcademyId&&n.CreationRequestId==body.RequestId));
    }
    [Fact] public async Task Approved_content_admin_news_only_grant_enforces_branch_and_does_not_open_media_or_finance()
    {
        using var owner=await Owner();var staff=await CreateStaff(owner,"AcademyAdmin");using var admin=factory.CreateClient();
        (await Send(admin,"/api/v1/auth/login",new{email=staff.Email,password="Demo-Only-123!"})).EnsureSuccessStatusCode();
        var access=new StaffAccessWrite(false,[Slice2DemoSeed.CityBranchId],StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,m=="news",m=="news",m=="news",false)).ToArray());
        (await Send(owner,$"/api/v1/manage/staff/{staff.Id}/access",access,put:true)).EnsureSuccessStatusCode();
        var body=new NewsWrite("خبر اختبار عمليات "+Guid.NewGuid(),"محتوى إداري مخول",null,7,true,true,"News",[Slice2DemoSeed.CityBranchId],["AcademyAdmin"],RequestId:Guid.NewGuid());
        var result=await ContentWrite(admin,"/api/v1/content/news/",body);result.EnsureSuccessStatusCode();var row=await result.Content.ReadFromJsonAsync<JsonElement>();var id=row.GetProperty("id").GetGuid();
        Assert.Contains(body.Title,await admin.GetStringAsync("/api/v1/news"));
        Assert.Equal(HttpStatusCode.Forbidden,(await ContentWrite(admin,"/api/v1/content/news/",body with{RequestId=Guid.NewGuid(),TargetBranchIds=[]})).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await ContentWrite(admin,"/api/v1/content/news/",body with{RequestId=Guid.NewGuid(),TargetBranchIds=[Slice2DemoSeed.FifthBranchId]})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await ContentWrite(admin,$"/api/v1/content/news/{id}",new{expectedVersion=row.GetProperty("version").GetUInt32()},HttpMethod.Delete)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("/api/v1/reports/financial")).StatusCode);
        using var form=new MultipartFormDataContent();var image=new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII="));image.Headers.ContentType=new("image/png");form.Add(image,"file","synthetic.png");form.Add(new StringContent("News"),"purpose");
        var csrf=await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");var upload=new HttpRequestMessage(HttpMethod.Post,"/api/v1/content/news/image"){Content=form};upload.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());
        var uploaded=await admin.SendAsync(upload);uploaded.EnsureSuccessStatusCode();var asset=(await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync($"/api/v1/assets/{asset}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("/api/v1/assets/gallery")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync("/api/v1/content/news/"+Guid.NewGuid())).StatusCode);
        (await Send(owner,$"/api/v1/manage/staff/{staff.Id}/access",access with{WorkflowActions=[]},put:true)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden,(await ContentWrite(admin,"/api/v1/content/news/",body with{RequestId=Guid.NewGuid()})).StatusCode);
        var noActions=access with{Actions=StaffAccessRules.Modules.Select(m=>new StaffActionValue(m,false,false,false,false)).ToArray()};
        (await Send(owner,$"/api/v1/manage/staff/{staff.Id}/access",noActions,put:true)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("/api/v1/content/news/options")).StatusCode);
        // Own targeted feed remains available without granting content management.
        Assert.Contains(body.Title,await admin.GetStringAsync("/api/v1/news"));
        Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/api/v1/assets/{asset}")).StatusCode);
    }
    [Fact] public async Task Approved_content_management_paginates_before_materializing_and_preserves_legacy_array_contract()
    {
        using var owner=await Owner();await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var now=DateTimeOffset.UtcNow;
        for(var i=0;i<26;i++)db.AcademyNews.Add(new AcademyNews{Id=Guid.NewGuid(),AcademyId=DemoSeed.NogoomAcademyId,Title="خبر اختبار عمليات صفحة "+i,
            Body="بيانات صفحات اصطناعية",Kind="Announcement",CreatedAtUtc=now.AddSeconds(i),UpdatedAtUtc=now});
        await db.SaveChangesAsync();
        var first=await owner.GetFromJsonAsync<JsonElement>("/api/v1/content/news/?paged=true&page=1&kind=Announcement");
        var second=await owner.GetFromJsonAsync<JsonElement>("/api/v1/content/news/?paged=true&page=2&kind=Announcement");
        Assert.Equal(25,first.GetProperty("items").GetArrayLength());Assert.True(first.GetProperty("total").GetInt32()>=26);
        var ids=first.GetProperty("items").EnumerateArray().Select(n=>n.GetProperty("id").GetGuid()).ToHashSet();
        Assert.DoesNotContain(second.GetProperty("items").EnumerateArray(),n=>ids.Contains(n.GetProperty("id").GetGuid()));
        Assert.Equal(JsonValueKind.Array,(await owner.GetFromJsonAsync<JsonElement>("/api/v1/content/news/")).ValueKind);
    }
}
