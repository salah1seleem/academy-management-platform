using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;
public sealed partial class EvaluationIntegrationTests
{
    [Fact]
    public async Task Internal_publication_is_immutable_staff_report_only_and_never_notifies_guardian()
    {
        using var staff=await Staff(DemoSeed.AdminEmail);
        using var guardian=await Guardian();
        var id=await CreateDraft(staff);var draft=await GetDetails(staff,id);
        (await Put(staff,$"/api/v1/evaluations/{id}",new{reportingPeriod="تقييم داخلي اصطناعي",version=draft.Version,scores=new[]{new{criterionId=draft.CriterionId,score=0}}})).EnsureSuccessStatusCode();
        var saved=await GetDetails(staff,id);
        var published=await Post(staff,$"/api/v1/evaluations/{id}/publish",new{version=saved.Version,shareWithGuardian=false});published.EnsureSuccessStatusCode();
        Assert.Equal("PublishedInternal",(await published.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK,(await staff.GetAsync($"/api/v1/evaluations/{id}/report")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync($"/api/v1/guardian/evaluations/{id}/report")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(staff,$"/api/v1/evaluations/{id}/publish",new{version=saved.Version,shareWithGuardian=true})).StatusCode);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.False(await db.AcademyNotices.AnyAsync(n=>n.TargetId==id));
    }
    [Fact]
    public async Task Owner_can_limit_coach_to_attendance_without_widening_role_or_group_scope()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);
        Guid membership;
        await using(var scope=factory.Services.CreateAsyncScope()) membership=await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().AcademyMemberships.Where(m=>m.AcademyId==DemoSeed.NogoomAcademyId&&m.User.Email==DemoSeed.CoachEmail&&m.Role==Academy.Infrastructure.Tenancy.AcademyRole.Coach).Select(m=>m.Id).SingleAsync();
        var path=$"/api/v1/manage/staff/{membership}/coach-access";
        var before=await owner.GetFromJsonAsync<JsonElement>(path);
        var original=before.GetProperty("actions").EnumerateArray().Where(a=>a.GetProperty("allowed").GetBoolean()).Select(a=>a.GetProperty("key").GetString()!).ToArray();
        try {
            (await Put(owner,path,new{revision=before.GetProperty("revision").GetInt32(),actions=new[]{"workflow.attendance.players"}})).EnsureSuccessStatusCode();
            using var coach=await Staff(DemoSeed.CoachEmail);
            var options=await coach.GetFromJsonAsync<JsonElement>("/api/v1/evaluations/options");Assert.False(options.GetProperty("canEvaluate").GetBoolean());
            Assert.Equal(HttpStatusCode.Forbidden,(await Post(coach,"/api/v1/evaluations",new{sportEnrollmentId=Guid.NewGuid(),evaluationDate="2026-09-28",reportingPeriod="مرفوض"})).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await coach.GetAsync("/api/v1/coach/groups")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await coach.GetAsync($"/api/v1/manage/staff/{membership}/coach-access")).StatusCode);
            using var foreign=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync(path)).StatusCode);
        } finally {
            var current=await owner.GetFromJsonAsync<JsonElement>(path);
            (await Put(owner,path,new{revision=current.GetProperty("revision").GetInt32(),actions=original})).EnsureSuccessStatusCode();
        }
    }

}
