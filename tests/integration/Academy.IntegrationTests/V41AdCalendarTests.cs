using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice8;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact] public async Task V41_ad_local_dates_are_inclusive_and_use_the_trusted_academy_timezone()
    {
        using var owner=await Staff();var original=await AdSettings(owner,true);var branch=await AdBranch();
        try{
            var day=new DateOnly(2026,10,20);var body=AdDraft(branch) with{LocalStartsOn=day,LocalEndsOn=day};
            var saved=await Send(owner,"/api/v1/manage/community-campaigns/",body);saved.EnsureSuccessStatusCode();var id=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revisionId").GetGuid();
            await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
            var zone=TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a=>a.Id==DemoSeed.NogoomAcademyId).Select(a=>a.TimeZone).SingleAsync());
            var start=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue),zone));var end=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue),zone));
            var row=await db.CommunityCampaignRevisions.AsNoTracking().SingleAsync(r=>r.Id==id);Assert.Equal(start,row.StartsAtUtc);Assert.Equal(end,row.EndsAtUtc);Assert.True(row.EndsAtUtc>row.StartsAtUtc);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,"/api/v1/manage/community-campaigns/",body with{RequestId=Guid.NewGuid(),LocalEndsOn=day.AddDays(-1)})).StatusCode);
        }finally{await AdRestore(owner,original);}
    }
}
