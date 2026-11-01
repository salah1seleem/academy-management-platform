using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class EvaluationIntegrationTests
{
    [Fact]
    public async Task New_swimming_sport_has_explicit_scoped_repeatable_criteria_and_owner_can_create_an_unscored_draft()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);using var foreign=await Staff("owner.future@example.test");using var coach=await Staff(DemoSeed.CoachEmail);
        var suffix=Guid.NewGuid().ToString("N")[..8];
        var invalid=await Post(owner,"/api/v1/manage/sports",new{arabicName="",kind="Swimming"});Assert.Equal(HttpStatusCode.UnprocessableEntity,invalid.StatusCode);
        var added=await Post(owner,"/api/v1/manage/sports",new{arabicName="سباحة تشغيل خيالية "+suffix,kind="Swimming"});added.EnsureSuccessStatusCode();var sport=await added.Content.ReadFromJsonAsync<Guid>();
        var branchResponse=await Post(owner,"/api/v1/manage/branches",new{arabicName="فرع تشغيل خيالي "+suffix,description="القاهرة"});branchResponse.EnsureSuccessStatusCode();var branch=await branchResponse.Content.ReadFromJsonAsync<Guid>();
        Guid category;int year;
        await using(var sc=factory.Services.CreateAsyncScope()){
            var db=sc.ServiceProvider.GetRequiredService<FoundationDbContext>();var s=await db.Sports.SingleAsync(x=>x.Id==sport);Assert.Equal(SportKind.Swimming,s.Kind);Assert.Equal(DemoSeed.NogoomAcademyId,s.AcademyId);
            var age=await db.AgeCategories.FirstAsync(x=>x.AcademyId==s.AcademyId&&x.IsActive);category=age.Id;year=age.MinimumBirthYear??Math.Min(age.MaximumBirthYear??2014,2014);Assert.False(await db.EvaluationCriteria.AnyAsync(x=>x.SportId==sport));
        }
        var groupResponse=await Post(owner,"/api/v1/manage/groups",new{arabicName="مجموعة تشغيل خيالية "+suffix,branchId=branch,sportId=sport,ageCategoryId=category,capacity=20});groupResponse.EnsureSuccessStatusCode();var group=await groupResponse.Content.ReadFromJsonAsync<Guid>();
        var enrollment=Guid.NewGuid();var player=Guid.NewGuid();
        await using(var sc=factory.Services.CreateAsyncScope()){
            var db=sc.ServiceProvider.GetRequiredService<FoundationDbContext>();db.Players.Add(new(){Id=player,AcademyId=DemoSeed.NogoomAcademyId,PlayerCode="UI-"+suffix,ArabicName="طفل مصري خيالي",DateOfBirth=new(year,1,1)});db.SportEnrollments.Add(new(){Id=enrollment,PlayerId=player,AcademyId=DemoSeed.NogoomAcademyId,BranchId=branch,SportId=sport,TrainingGroupId=group});await db.SaveChangesAsync();
        }
        var body=new{sportEnrollmentId=enrollment,evaluationDate=DateOnly.FromDateTime(DateTime.UtcNow),reportingPeriod="تقييم دوري"};
        var missing=await Post(owner,"/api/v1/evaluations",body);Assert.Equal(HttpStatusCode.Conflict,missing.StatusCode);Assert.Equal("evaluation_criteria_missing",(await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var proposal=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/evaluations/criteria/defaults?sportId={sport}");Assert.Equal(7,proposal.GetProperty("criteria").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound,(await Post(foreign,"/api/v1/evaluations/criteria/defaults",new{sportId=sport})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Post(coach,"/api/v1/evaluations/criteria/defaults",new{sportId=sport})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync("/api/v1/evaluations/criteria/defaults",new{sportId=sport})).StatusCode);
        await using(var sc=factory.Services.CreateAsyncScope())Assert.False(await sc.ServiceProvider.GetRequiredService<FoundationDbContext>().EvaluationCriteria.AnyAsync(x=>x.SportId==sport));
        var saved=await Post(owner,"/api/v1/evaluations/criteria/defaults",new{sportId=sport});saved.EnsureSuccessStatusCode();Assert.Equal(7,(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetInt32());
        var replay=await Post(owner,"/api/v1/evaluations/criteria/defaults",new{sportId=sport});replay.EnsureSuccessStatusCode();Assert.Equal(0,(await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetInt32());
        var created=await Post(owner,"/api/v1/evaluations",body);created.EnsureSuccessStatusCode();var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();var draft=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/evaluations/{id}");Assert.Equal("Draft",draft.GetProperty("status").GetString());Assert.Equal(7,draft.GetProperty("scores").GetArrayLength());Assert.All(draft.GetProperty("scores").EnumerateArray(),x=>Assert.Equal(JsonValueKind.Null,x.GetProperty("score").ValueKind));
        var options=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/structure/options");var displayed=options.GetProperty("groups").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==group);Assert.Equal(1,displayed.GetProperty("playerCount").GetInt32());Assert.Equal("فرع تشغيل خيالي "+suffix,displayed.GetProperty("branchName").GetString());
        var players=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/players?search=UI-{suffix}&page=1&pageSize=25");var row=players.GetProperty("items")[0];Assert.Equal(player,row.GetProperty("id").GetGuid());Assert.True(row.TryGetProperty("dateOfBirth",out _));Assert.True(row.GetProperty("enrollmentSummaries")[0].GetProperty("criteriaReady").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync($"/api/v1/evaluations/{id}")).StatusCode);
        var data=await owner.GetFromJsonAsync<JsonElement>("/api/v1/evaluations/options");Assert.True(data.GetProperty("enrollments").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==enrollment).GetProperty("criteriaReady").GetBoolean());
    }
}
