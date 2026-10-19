using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    // Deferred with the other V4 functional gates, not a claim of execution.
    [Fact] public async Task V4_coach_rankings_use_real_family_scores_minimum_sample_and_tenant_scope()
    {
        using var owner=await Staff();using var coach=await Mobile(AcademyRole.Coach);using var foreign=await Staff(DemoSeed.FutureOwnerEmail);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var active=await db.AcademyMemberships.Where(c=>c.AcademyId==DemoSeed.NogoomAcademyId&&c.IsActive&&c.User.IsActive&&c.Role==AcademyRole.Coach).Select(c=>c.Id).ToArrayAsync();
        var values=await db.GuardianFeedbacks.Where(f=>f.AcademyId==DemoSeed.NogoomAcademyId&&f.IsActive&&f.Kind=="Rating"&&f.CoachMembershipId!=null&&active.Contains(f.CoachMembershipId.Value)&&f.Score>=1&&f.Score<=5).Select(f=>new{Id=f.CoachMembershipId!.Value,Score=f.Score!.Value}).ToArrayAsync();
        var expected=values.GroupBy(f=>f.Id).Where(g=>g.Count()>=3).ToDictionary(g=>g.Key,g=>new{Count=g.Count(),Average=Math.Round(g.Average(x=>(decimal)x.Score),2)});
        var data=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/feedback/coach-rankings?allDates=true");Assert.Equal(3,data.GetProperty("minimumReviews").GetInt32());
        foreach(var key in new[]{"highest","lowest"}){var rows=data.GetProperty(key).EnumerateArray().ToArray();Assert.Equal(Math.Min(5,expected.Count),rows.Length);foreach(var row in rows){var value=expected[row.GetProperty("id").GetGuid()];Assert.Equal(value.Count,row.GetProperty("reviewCount").GetInt32());Assert.Equal(value.Average,row.GetProperty("averageRating").GetDecimal());Assert.False(row.TryGetProperty("guardianName",out _));}}
        Assert.Equal(HttpStatusCode.Forbidden,(await coach.GetAsync("/api/v1/manage/feedback/coach-rankings")).StatusCode);
        var other=await foreign.GetFromJsonAsync<JsonElement>("/api/v1/manage/feedback/coach-rankings?allDates=true");Assert.DoesNotContain(other.GetProperty("highest").EnumerateArray(),x=>active.Contains(x.GetProperty("id").GetGuid()));
        var empty=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/feedback/coach-rankings?from=2001-01-01&to=2001-01-02");Assert.Equal(0,empty.GetProperty("highest").GetArrayLength());
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await owner.GetAsync("/api/v1/manage/feedback/coach-rankings?from=2026-10-20&to=2026-10-01")).StatusCode);
        var detail=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/feedback?kind=Rating&from=2001-01-01&to=2001-01-02");Assert.Equal(0,detail.GetArrayLength());
    }
}
