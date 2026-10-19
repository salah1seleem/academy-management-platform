using System.Net;
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
    [Fact] public async Task Guardian_request_intake_owner_resolution_replay_privacy_and_no_business_side_effects()
    {
        using var family = await Mobile(AcademyRole.Guardian); using var owner = await Staff(); using var other = await Staff(DemoSeed.FutureOwnerEmail);
        using var admin = await Staff(DemoSeed.AdminEmail); using var coach = await Mobile(AcademyRole.Coach);
        var (child, group, today) = await TriageOptions(family);
        var path = $"/api/v1/guardian/children/{child}/feedback"; var money = await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28");
        async Task<int> Count() => (await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/owner-summary?from=2026-09-01&to=2026-09-28")).GetProperty("operations").GetProperty("openRequests").GetInt32();
        var count = await Count();
        var body = new FeedbackWrite(group, null, "Request", null, "طلب اصطناعي للإدارة عن الدفع؛ حل الطلب لا ينشئ تحصيلًا أو تجديدًا", "Payment", "Routine", today);
        foreach (var invalid in new[] { body with { Score = 5 }, body with { Message = "قصير"[..3] }, body with { Kind = "PaymentApproved" } })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(family, path, invalid, key: Guid.NewGuid().ToString())).StatusCode);
        var key = Guid.NewGuid().ToString(); var submissions = await Task.WhenAll(Send(family,path,body,key:key), Send(family,path,body,key:key));
        Assert.Single(submissions,r => r.StatusCode == HttpStatusCode.Created); Assert.All(submissions, r => r.EnsureSuccessStatusCode());
        var ids = await Task.WhenAll(submissions.Select(async r => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid())); Assert.Equal(ids[0],ids[1]); var id=ids[0];
        Assert.Equal(count+1,await Count()); var detail=$"/api/v1/manage/feedback/{id}";
        var list=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/feedback?kind=Request&status=Unresolved"); Assert.Contains(list.EnumerateArray(),x=>x.GetProperty("id").GetGuid()==id);
        foreach(var c in new[]{family,admin,coach}) Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(detail)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync(detail)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Send(family,path,body with{TrainingGroupId=Guid.NewGuid()},key:Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Send(family,path,body with{Message="محتوى آخر بنفس المفتاح"},key:key)).StatusCode);
        (await Send(owner,detail+"/due",new FeedbackDueDate(today))).EnsureSuccessStatusCode();
        var reply=new FeedbackResolution("تمت مراجعة طلبك؛ أي دفع أو تعديل للاشتراك يتم من مساره المعتمد وليس من هذا الرد.");
        await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var notices=await db.AcademyNotices.CountAsync();
        (await Send(owner,detail+"/resolve",reply)).EnsureSuccessStatusCode(); (await Send(owner,detail+"/resolve",reply)).EnsureSuccessStatusCode(); Assert.Equal(notices+1,await db.AcademyNotices.CountAsync());
        Assert.Equal(count,await Count()); var mine=(await family.GetFromJsonAsync<JsonElement>(path+"-options")).GetProperty("mine").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==id);
        Assert.Equal("Request",mine.GetProperty("kind").GetString()); Assert.Equal(reply.Response,mine.GetProperty("ownerResponse").GetString()); Assert.False(mine.TryGetProperty("history",out _));
        var row=await owner.GetFromJsonAsync<JsonElement>(detail); (await Send(owner,detail+"/transition",new FeedbackCaseWrite("Reopened",row.GetProperty("version").GetUInt32(),Guid.NewGuid()))).EnsureSuccessStatusCode(); Assert.Equal(count+1,await Count());
        Assert.Equal(money,await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
        var sql=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.GuardianFeedbacks.Where(x=>x.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Score,4))); Assert.Equal("23514",sql.SqlState);
    }
    [Fact] public async Task Guardian_request_alone_drives_private_overdue_followup_without_becoming_complaint()
    {
        using var owner=await Staff(); await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var group=await FollowUpGroup(db);
        var notes=await OverdueFamilyNotes(db,group); notes[0].Kind="Request";notes[1].IsActive=false;await db.SaveChangesAsync();
        var candidates=(await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/owner-followups/options")).GetProperty("candidates").EnumerateArray().Where(x=>x.GetProperty("condition").GetString()=="GroupOverdueFeedback"&&x.GetProperty("source").GetProperty("groupId").GetGuid()==group.Id).ToArray();
        var source=Assert.Single(candidates).GetProperty("source").GetProperty("overdueFeedback");Assert.Equal(1,source.GetProperty("requestCount").GetInt32());Assert.Equal(0,source.GetProperty("complaintCount").GetInt32());Assert.Equal(0,source.GetProperty("suggestionCount").GetInt32());Assert.Equal(1,source.GetProperty("submitterCount").GetInt32());Assert.False(source.TryGetProperty("feedbackIds",out _));
        var made=await Send(owner,"/api/v1/manage/owner-followups/",new OwnerFollowUpCreate(group.Id,"GroupOverdueFeedback",Guid.NewGuid()));made.EnsureSuccessStatusCode();var id=(await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var saved=(await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/owner-followups/{id}")).GetProperty("item");Assert.True(saved.GetProperty("conditionActive").GetBoolean());
        Assert.Contains(notes[0].Id,saved.GetProperty("evidence").GetProperty("feedbackIds").EnumerateArray().Select(x=>x.GetGuid()));
        (await Send(owner,$"/api/v1/manage/feedback/{notes[0].Id}/resolve",new FeedbackResolution("تم الرد على طلب توضيح المواعيد دون تغيير الجدول"))).EnsureSuccessStatusCode();
        Assert.False((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/owner-followups/{id}")).GetProperty("item").GetProperty("conditionActive").GetBoolean());
    }
}
