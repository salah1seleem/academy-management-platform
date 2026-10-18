using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    private static async Task<GuardianFeedback[]> OverdueFamilyNotes(FoundationDbContext db,TrainingGroup g)
    {
        var today=await AttendanceCalendar.TodayAsync(g.AcademyId,db,TimeProvider.System);var now=DateTimeOffset.UtcNow;
        var link=await db.GuardianPlayerLinks.Where(l=>l.AcademyId==g.AcademyId&&l.IsActive&&l.Guardian.IsActive&&l.Guardian.User.PhoneNumber==DemoSeed.GuardianPhone).Select(l=>new{l.PlayerId,l.Guardian.UserId}).FirstAsync();
        var actor=await db.Users.Where(u=>u.Email==DemoSeed.OwnerEmail).Select(u=>u.Id).SingleAsync();
        var coach=await db.AcademyMemberships.Where(m=>m.AcademyId==g.AcademyId&&m.Role==AcademyRole.Coach&&m.IsActive).Select(m=>m.Id).FirstAsync();
        var ratingMonth=(await db.GuardianFeedbacks.Where(f=>f.AcademyId==g.AcademyId&&f.Kind=="Rating"&&f.GuardianUserId==link.UserId&&f.PlayerId==link.PlayerId&&f.CoachMembershipId==coach).Select(f=>(DateOnly?)f.PeriodMonth).MaxAsync()??new DateOnly(today.Year,today.Month,1)).AddMonths(1);
        var rows=Enumerable.Range(0,7).Select(i=>new GuardianFeedback{Id=Guid.NewGuid(),AcademyId=g.AcademyId,TrainingGroupId=g.Id,BranchId=g.BranchId,PlayerId=link.PlayerId,GuardianUserId=link.UserId,CoachMembershipId=i==6?coach:null,
            Kind=i==1?"Suggestion":i==6?"Rating":"Complaint",Score=i==6?4:null,Message="نص أسرة خاص لا ينتقل إلى مهمة الإداري "+i,Status=i==4?"Resolved":i==1?"Reviewed":"Open",DueOn=i==2?null:i==3?today:today.AddDays(-2),
            DueSetByUserId=actor,DueSetAtUtc=now,PeriodMonth=i==6?ratingMonth:new DateOnly(today.Year,today.Month,1),IdempotencyKey=Guid.NewGuid().ToString(),IsActive=i!=5,CreatedAtUtc=now,UpdatedAtUtc=now}).ToArray();
        db.GuardianFeedbacks.AddRange(rows);await db.SaveChangesAsync();return rows;
    }
    [Fact] public async Task Feedback_followup_uses_real_deadlines_exact_source_and_only_source_reply_resolves_it()
    {
        using var owner=await Staff();using var guardian=await Mobile(AcademyRole.Guardian);await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var group=await FollowUpGroup(db);var notes=await OverdueFamilyNotes(db,group);
        async Task<JsonElement[]> Candidates()=>(await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/owner-followups/options")).GetProperty("candidates").EnumerateArray().Where(c=>c.GetProperty("condition").GetString()=="GroupOverdueFeedback"&&c.GetProperty("source").GetProperty("groupId").GetGuid()==group.Id).ToArray();
        var summary=Assert.Single(await Candidates()).GetProperty("source").GetProperty("overdueFeedback");Assert.Equal(1,summary.GetProperty("complaintCount").GetInt32());Assert.Equal(1,summary.GetProperty("suggestionCount").GetInt32());Assert.Equal(1,summary.GetProperty("submitterCount").GetInt32());Assert.False(summary.TryGetProperty("feedbackIds",out _));
        var report=await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28");var notices=await db.AcademyNotices.CountAsync();
        var create=await Send(owner,"/api/v1/manage/owner-followups/",new OwnerFollowUpCreate(group.Id,"GroupOverdueFeedback",Guid.NewGuid()));create.EnsureSuccessStatusCode();var id=(await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        async Task<JsonElement> Read()=>(await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/owner-followups/{id}")).GetProperty("item");
        var task=await Read();var evidence=task.GetProperty("evidence").GetRawText();Assert.Equal(notes.Take(2).Select(f=>f.Id).Order(),task.GetProperty("evidence").GetProperty("feedbackIds").EnumerateArray().Select(x=>x.GetGuid()).Order());
        var detail=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/feedback/{notes[0].Id}");Assert.Equal(notes[0].Message,detail.GetProperty("message").GetString());Assert.Equal(group.Id,detail.GetProperty("trainingGroupId").GetGuid());Assert.False(detail.TryGetProperty("guardianUserId",out _));
        var scoped=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/feedback?groupId={group.Id}&status=Unresolved&overdue=true");Assert.Equal(notes.Take(2).Select(f=>f.Id).Order(),scoped.EnumerateArray().Select(x=>x.GetProperty("id").GetGuid()).Order());
        (await Send(owner,$"/api/v1/manage/owner-followups/{id}",new OwnerFollowUpWrite("Closed",task.GetProperty("version").GetUInt32(),Guid.NewGuid(),ResolutionReason:"متابعة بشرية فقط، لا رد للأسرة"))).EnsureSuccessStatusCode();Assert.True((await Read()).GetProperty("conditionActive").GetBoolean());Assert.Equal(notices,await db.AcademyNotices.CountAsync());
        foreach(var f in notes.Take(2)){
            var response=new FeedbackResolution("رد اصطناعي محفوظ للأسرة صاحبة الملاحظة فقط");
            (await Send(owner,$"/api/v1/manage/feedback/{f.Id}/resolve",response)).EnsureSuccessStatusCode();(await Send(owner,$"/api/v1/manage/feedback/{f.Id}/resolve",response)).EnsureSuccessStatusCode();
            var mine=await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{f.PlayerId}/feedback-options");Assert.Contains(mine.GetProperty("mine").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==f.Id&&x.GetProperty("ownerResponse").GetString()==response.Response);
        }
        Assert.Empty(await Candidates());task=await Read();Assert.False(task.GetProperty("conditionActive").GetBoolean());Assert.Equal("Closed",task.GetProperty("state").GetString());Assert.Equal(evidence,task.GetProperty("evidence").GetRawText());Assert.Equal(notices+2,await db.AcademyNotices.CountAsync());Assert.Equal(report,await owner.GetStringAsync("/api/v1/reports/financial?from=2026-09-01&to=2026-09-28"));
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync($"/api/v1/manage/feedback/{notes[0].Id}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync($"/api/v1/manage/feedback?groupId={group.Id}")).StatusCode);
    }
    [Fact] public async Task Feedback_followup_delegate_sees_counts_not_family_text_identity_or_feedback_ids()=>await DelegateFixture(async(owner,admin,db,g,member)=>{
        var notes=await OverdueFamilyNotes(db,g);var create=await Send(owner,"/api/v1/manage/owner-followups/",new OwnerFollowUpCreate(g.Id,"GroupOverdueFeedback",Guid.NewGuid(),member));create.EnsureSuccessStatusCode();var id=(await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var raw=await admin.GetStringAsync($"/api/v1/my-followups/{id}");Assert.DoesNotContain("نص أسرة خاص",raw);Assert.DoesNotContain("feedbackIds",raw);Assert.DoesNotContain(notes[0].GuardianUserId.ToString(),raw);Assert.DoesNotContain(notes[0].PlayerId.ToString(),raw);foreach(var f in notes)Assert.DoesNotContain(f.Id.ToString(),raw);
        Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync($"/api/v1/manage/feedback/{notes[0].Id}")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,$"/api/v1/manage/feedback/{notes[0].Id}/resolve",new FeedbackResolution("رد غير مخول ممنوع"))).StatusCode);
        using var coach=await Mobile(AcademyRole.Coach);using var guardian=await Mobile(AcademyRole.Guardian);foreach(var c in new[]{coach,guardian})Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync($"/api/v1/manage/feedback/{notes[0].Id}")).StatusCode);
        var task=JsonDocument.Parse(raw).RootElement.GetProperty("item");(await Send(admin,$"/api/v1/my-followups/{id}",new AssignedFollowUpWrite("Closed",task.GetProperty("version").GetUInt32(),Guid.NewGuid(),ResolutionReason:"مهمة تشغيل فقط بلا كشف أسرة"))).EnsureSuccessStatusCode();Assert.NotEqual("Resolved",(await db.GuardianFeedbacks.AsNoTracking().SingleAsync(f=>f.Id==notes[0].Id)).Status);
    });
}
